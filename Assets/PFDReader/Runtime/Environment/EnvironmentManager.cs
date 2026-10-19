using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PFDReader.Books;
using PFDReader.Core;
using PFDReader.View;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PFDReader.Spaces
{
    public sealed class RoomEntry { public string id, title; public Texture2D preview; }
    public sealed class PanoramaEntry { public string path, title; public bool stereo; }

    /// <summary>
    /// Umgebung: Passthrough, gebackene 3D-Raeume (eigene Szenen, additiv geladen) oder 360°-Panoramen (mono/stereo).
    /// Fuer Raeume wird das Kamera-Rig so versetzt, dass der Kopf am Sitzplatz des Raums landet; Buch und Fenster wandern mit.
    /// </summary>
    public sealed class EnvironmentManager : MonoBehaviour
    {
        public Transform Rig, Head;
        public Transform[] FollowWithRig;          // Workspace, Einstellungsfenster …
        public Action<bool> SetPassthroughNative;   // schaltet YVR-Passthrough + Kamerahintergrund
        public Action<Material> SetSkybox;          // null = kein Himmel (Kamerafarbe)
        public TMPro.TMP_FontAsset MarkerFont;
        public RoomInfo CurrentRoom { get; private set; }
        readonly List<SeatMarker> m_Markers = new List<SeatMarker>();
        public IReadOnlyList<SeatMarker> Markers => m_Markers;
        public event Action Seated;
        public event Action<RoomInfo> RoomChanged;

        Pose m_RigOrigin; bool m_HasOrigin;
        string m_LoadedRoomScene;
        GameObject m_PanoSphere; Material m_PanoMat; Texture2D m_PanoTex;
        GameObject m_DimSphere; Material m_DimMat;
        bool m_Busy;

        public List<RoomEntry> Rooms { get; } = new List<RoomEntry>();
        public string Current => Storage.Settings.environment;
        public bool Busy => m_Busy;
        public event Action Changed;
        public string LastError { get; private set; }

        void Awake()
        {
            var cat = Resources.Load<TextAsset>("rooms");
            if (cat != null)
                foreach (var line in cat.text.Split('\n'))
                {
                    var p = line.Trim().Split('|');
                    if (p.Length >= 2) Rooms.Add(new RoomEntry { id = p[0], title = p[1], preview = Resources.Load<Texture2D>("RoomPreviews/" + p[0]) });
                }
            m_DimMat = new Material(Shader.Find("PFD/Dim"));
            m_DimSphere = Sphere("Dimmer", m_DimMat, 0.35f);
            SetDim(Storage.Settings.environmentDim);
        }

        static GameObject Sphere(string name, Material mat, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * radius * 2f;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            DontDestroyOnLoad(go);
            return go;
        }

        void LateUpdate()
        {
            if (Head == null) return;
            if (m_DimSphere.activeSelf) m_DimSphere.transform.position = Head.position;
            if (m_PanoSphere != null && m_PanoSphere.activeSelf) m_PanoSphere.transform.position = Head.position;
        }

        // ---------------------------------------------------------------- Dimmer
        public void SetDim(float brightness)
        {
            brightness = Mathf.Clamp01(brightness);
            Storage.Settings.environmentDim = brightness;
            m_DimMat.SetFloat("_Amount", 1f - brightness);
            m_DimSphere.SetActive(brightness < 0.995f);
        }

        // ---------------------------------------------------------------- Panoramen
        public static IEnumerable<string> PanoramaFolders()
        {
            var root = Path.GetDirectoryName(Storage.SharedBooksDir); // /storage/emulated/0
            yield return Path.Combine(root, "Pictures", "Envoirements");
            yield return Path.Combine(root, "Pictures", "Environments");
            yield return Path.Combine(root, "Pictures", "Envoirement_3D");
            yield return Path.Combine(root, "Pictures", "Environment_3D");
            yield return Path.Combine(root, "Pictures", "Panoramas");
            yield return Path.Combine(Storage.SharedBooksDir, "Panoramas");
        }

        public List<PanoramaEntry> ScanPanoramas()
        {
            var list = new List<PanoramaEntry>();
            foreach (var dir in PanoramaFolders())
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp")
                            list.Add(new PanoramaEntry { path = f, title = Path.GetFileNameWithoutExtension(f) });
                    }
                }
                catch { }
            }
            return list.GroupBy(p => p.path).Select(g => g.First()).OrderBy(p => p.title, NaturalComparer.Instance).ToList();
        }

        // ---------------------------------------------------------------- Umschalten
        public void Restore() { if (!string.IsNullOrEmpty(Current) && Current != "passthrough") Apply(Current); }

        /// <summary>"passthrough" | "dark" | "room:&lt;id&gt;" | "pano:&lt;pfad&gt;"</summary>
        public void Apply(string env)
        {
            if (m_Busy) return;
            StartCoroutine(ApplyRoutine(env));
        }

        IEnumerator ApplyRoutine(string env)
        {
            m_Busy = true; LastError = null;
            Storage.Settings.environment = env; Storage.SaveSettings();
            Changed?.Invoke();

            // Alte Umgebung abbauen
            string targetRoom = env.StartsWith("room:") ? $"Assets/PFDReader/Rooms/{env.Substring(5)}/{env.Substring(5)}.unity" : null;
            if (m_LoadedRoomScene != null && m_LoadedRoomScene != targetRoom)
            {
                // Raum -> Raum: kein RoomChanged(null), damit z. B. ein laufendes Video am Fernseher weiterlaeuft
                ClearMarkers(); CurrentRoom = null; SetSkybox?.Invoke(null); if (targetRoom == null) RoomChanged?.Invoke(null);
                var op = SceneManager.UnloadSceneAsync(m_LoadedRoomScene);
                while (op != null && !op.isDone) yield return null;
                m_LoadedRoomScene = null;
            }
            if (!env.StartsWith("pano:") && m_PanoSphere != null) { m_PanoSphere.SetActive(false); if (m_PanoTex) { Destroy(m_PanoTex); m_PanoTex = null; } }

            if (!env.StartsWith("room:")) { ClearMarkers(); if (CurrentRoom != null) RoomChanged?.Invoke(null); CurrentRoom = null; SetSkybox?.Invoke(null); }
            if (env == "passthrough" || env == "dark")
            {
                ResetRig();
                SetPassthroughNative?.Invoke(env == "passthrough");
            }
            else if (env.StartsWith("room:"))
            {
                SetPassthroughNative?.Invoke(false);
                var id = env.Substring(5);
                var path = $"Assets/PFDReader/Rooms/{id}/{id}.unity";
                if (m_LoadedRoomScene != path)
                {
                    var op = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
                    if (op == null) { Fail("Raum nicht gefunden: " + id); yield break; }
                    while (!op.isDone) yield return null;
                    m_LoadedRoomScene = path;
                }
                var info = FindObjectsByType<RoomInfo>(FindObjectsSortMode.None).FirstOrDefault(r => r.id == id);
                CurrentRoom = info;
                ApplyTimeOfDay();
                // Keine Himmels-Spiegelung in Innenraeumen: die hellen Reflexe an duennen Kanten (Lamellen, Fugen)
                // flimmerten auf Distanz stark; das Licht kommt ohnehin aus den gebackenen Lightmaps
                RenderSettings.reflectionIntensity = 0f;
                RoomChanged?.Invoke(info);
                ClearMarkers();
                if (info != null)
                {
                    var seats = info.seats != null && info.seats.Length > 0 ? info.seats : new[] { info.seat };
                    foreach (var st in seats) if (st != null) { var mk = SeatMarker.Create(st, MarkerFont); mk.gameObject.SetActive(false); m_Markers.Add(mk); }
                    if (info.seat != null) SitAt(info.seat);
                }
            }
            else if (env.StartsWith("pano:"))
            {
                SetPassthroughNative?.Invoke(false);
                ResetRig();
                var file = env.Substring(5);
                PageImage img = null; string err = null;
                yield return RunTask(() =>
                {
                    try { img = DecodePanorama(file); } catch (Exception e) { err = e.Message; }
                });
                if (img == null) { Fail("Panorama nicht lesbar: " + (err ?? file)); yield break; }
                if (m_PanoTex) Destroy(m_PanoTex);
                m_PanoTex = new Texture2D(img.width > 0 ? img.width : 2, img.height > 0 ? img.height : 2, TextureFormat.RGBA32, false);
                if (img.encoded != null) m_PanoTex.LoadImage(img.encoded, false); else { m_PanoTex.SetPixelData(img.rgba, 0); m_PanoTex.Apply(false, true); }
                m_PanoTex.wrapModeU = TextureWrapMode.Repeat; m_PanoTex.wrapModeV = TextureWrapMode.Clamp;
                m_PanoTex.filterMode = FilterMode.Bilinear;
                if (m_PanoMat == null) { m_PanoMat = new Material(Shader.Find("PFD/Panorama")); m_PanoSphere = Sphere("Panorama", m_PanoMat, 50f); }
                float aspect = m_PanoTex.width / (float)m_PanoTex.height;
                bool stereo = aspect < 1.5f; // 1:1 = Stereo Top/Bottom, 2:1 = Mono
                m_PanoMat.mainTexture = m_PanoTex;
                m_PanoMat.SetFloat("_Stereo", stereo ? 1 : 0);
                m_PanoMat.SetFloat("_Swap", Storage.Settings.panoramaSwapEyes ? 1 : 0);
                m_PanoMat.SetFloat("_Yaw", Head != null ? -Head.eulerAngles.y : 0);
                m_PanoSphere.SetActive(true);
            }
            m_Busy = false;
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- Tageszeit (Raeume)
        public bool HasTimeOfDay => CurrentRoom != null && CurrentRoom.skyEvening != null;

        public void SetTimeOfDay(int t)
        {
            Storage.Settings.timeOfDay = t; Storage.SaveSettings();
            ApplyTimeOfDay();
            Changed?.Invoke();
        }

        /// <summary>Himmel und gebackene Lightmaps der aktuellen Tageszeit setzen (Zuordnung ueber den Texturnamen).</summary>
        void ApplyTimeOfDay()
        {
            var info = CurrentRoom;
            if (info == null) { SetSkybox?.Invoke(null); return; }
            bool evening = Storage.Settings.timeOfDay == 1 && info.skyEvening != null;
            SetSkybox?.Invoke(evening ? info.skyEvening : info.skybox);
            var target = evening ? info.lightmapsEvening : info.lightmapsDay;
            if (target == null || target.Length == 0) return;
            var maps = LightmapSettings.lightmaps;
            bool changed = false;
            for (int i = 0; i < maps.Length; i++)
            {
                var cur = maps[i].lightmapColor; if (cur == null) continue;
                foreach (var t in target)
                    if (t != null && t != cur && t.name == cur.name) { maps[i].lightmapColor = t; changed = true; break; }
            }
            if (changed) LightmapSettings.lightmaps = maps;
        }

        // ---------------------------------------------------------------- Panorama-Vorschaubilder
        readonly Dictionary<string, Texture2D> m_Thumbs = new Dictionary<string, Texture2D>();
        readonly Queue<(string path, Action<Texture2D> done)> m_ThumbQueue = new Queue<(string, Action<Texture2D>)>();
        bool m_ThumbRunning;

        static string ThumbDir => Path.Combine(Application.persistentDataPath, "panothumbs");
        static string ThumbFile(string path)
        {
            long stamp = 0; try { var fi = new FileInfo(path); stamp = fi.Length ^ fi.LastWriteTimeUtc.Ticks; } catch { }
            unchecked
            {
                ulong h = 1469598103934665603UL;
                foreach (var ch in path + "|" + stamp) { h ^= ch; h *= 1099511628211UL; }
                return Path.Combine(ThumbDir, h.ToString("x16") + ".jpg");
            }
        }

        /// <summary>Kleines Vorschaubild eines Panoramas (bei Stereo die obere Haelfte); asynchron, mit Datei-Cache.</summary>
        public void RequestThumbnail(string path, Action<Texture2D> done)
        {
            if (m_Thumbs.TryGetValue(path, out var t) && t != null) { done(t); return; }
            m_ThumbQueue.Enqueue((path, done));
            if (!m_ThumbRunning) StartCoroutine(ThumbWorker());
        }

        IEnumerator ThumbWorker()
        {
            m_ThumbRunning = true;
            while (m_ThumbQueue.Count > 0)
            {
                var (path, done) = m_ThumbQueue.Dequeue();
                if (m_Thumbs.TryGetValue(path, out var have) && have != null) { done(have); continue; }
                var file = ThumbFile(path);
                Texture2D tex = null;
                if (File.Exists(file))
                {
                    try { tex = new Texture2D(2, 2, TextureFormat.RGB24, false); if (!tex.LoadImage(File.ReadAllBytes(file), true)) { Destroy(tex); tex = null; } } catch { tex = null; }
                }
                if (tex == null)
                {
                    PageImage img = null;
                    yield return RunTask(() => { try { img = DecodeThumb(path); } catch (Exception e) { Debug.LogWarning("[PFD] Vorschau: " + e.Message); } });
                    if (img != null)
                    {
                        tex = BuildThumb(img);
                        if (tex != null)
                        {
                            try { Directory.CreateDirectory(ThumbDir); File.WriteAllBytes(file, tex.EncodeToJPG(85)); } catch { }
                            tex.Apply(false, true);
                        }
                    }
                }
                if (tex != null) { tex.name = "Thumb " + Path.GetFileName(path); m_Thumbs[path] = tex; }
                try { done(tex); } catch (Exception e) { Debug.LogException(e); }
                yield return null;
            }
            m_ThumbRunning = false;
        }

        static PageImage DecodeThumb(string path)
        {
            var data = File.ReadAllBytes(path);
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJNI.AttachCurrentThread();
            try
            {
                using (var b = new AndroidJavaClass("org.pfd.reader.unity.PfdBridge"))
                {
                    var raw = b.CallStatic<sbyte[]>("decodeImage", data, 320, 320);
                    if (raw == null || raw.Length < 8) return null;
                    int w = ((byte)raw[0] << 24) | ((byte)raw[1] << 16) | ((byte)raw[2] << 8) | (byte)raw[3];
                    int h = ((byte)raw[4] << 24) | ((byte)raw[5] << 16) | ((byte)raw[6] << 8) | (byte)raw[7];
                    var px = new byte[raw.Length - 8]; Buffer.BlockCopy(raw, 8, px, 0, px.Length);
                    return new PageImage { width = w, height = h, rgba = px };
                }
            }
            finally { AndroidJNI.DetachCurrentThread(); }
#else
            return new PageImage { encoded = data };
#endif
        }

        /// <summary>RGBA (unten zuerst) -> RGB-Vorschau; Stereo (1:1 oben/unten): nur das obere (linke) Auge, Ausschnitt um die Bildmitte.</summary>
        static Texture2D BuildThumb(PageImage img)
        {
            int w = img.width, h = img.height; byte[] px = img.rgba;
            if (img.encoded != null)
            {
                var full = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!full.LoadImage(img.encoded, false)) { Destroy(full); return null; }
                // im Editor: auf ~320 px verkleinern
                int tw = 320, th = Mathf.Max(1, Mathf.RoundToInt(320f * full.height / full.width));
                var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(full, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var small = new Texture2D(tw, th, TextureFormat.RGBA32, false);
                small.ReadPixels(new Rect(0, 0, tw, th), 0, 0); small.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); Destroy(full);
                w = tw; h = th; px = small.GetRawTextureData(); px = (byte[])px.Clone(); Destroy(small);
            }
            if (w <= 0 || h <= 0 || px == null) return null;
            bool stereo = w / (float)h < 1.5f;
            int y0 = 0, rows = h;
            if (stereo) { rows = h / 2; y0 = h - rows; }            // obere Bildhaelfte (Daten liegen unten zuerst)
            // mittleren Ausschnitt im Seitenverhaeltnis der Karte (ca. 1.6:1) waehlen: ganze Hoehe, Breite = 1.6*Hoehe
            int cw = Mathf.Min(w, Mathf.RoundToInt(rows * 1.6f));
            int x0 = (w - cw) / 2;
            int ch = rows;
            var tex = new Texture2D(cw, ch, TextureFormat.RGB24, false);
            var outPx = new byte[cw * ch * 3];
            for (int y = 0; y < ch; y++)
            {
                int src = ((y0 + y) * w + x0) * 4, dst = y * cw * 3;
                for (int x = 0; x < cw; x++, src += 4, dst += 3) { outPx[dst] = px[src]; outPx[dst + 1] = px[src + 1]; outPx[dst + 2] = px[src + 2]; }
            }
            tex.SetPixelData(outPx, 0); tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(false, false);
            return tex;
        }

        void Fail(string msg)
        {
            LastError = msg; Debug.LogWarning("[PFD] " + msg);
            Storage.Settings.environment = "passthrough"; Storage.SaveSettings();
            ResetRig(); SetPassthroughNative?.Invoke(true);
            m_Busy = false; Changed?.Invoke();
        }

        public void SetSwapEyes(bool swap)
        {
            Storage.Settings.panoramaSwapEyes = swap; Storage.SaveSettings();
            if (m_PanoMat) m_PanoMat.SetFloat("_Swap", swap ? 1 : 0);
        }

        static string RoomIdOf(string scenePath) => Path.GetFileNameWithoutExtension(scenePath);

        static IEnumerator RunTask(Action a)
        {
            var t = Task.Run(a);
            while (!t.IsCompleted) yield return null;
        }

        static PageImage DecodePanorama(string path)
        {
            var data = File.ReadAllBytes(path);
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJNI.AttachCurrentThread();
            try
            {
                using (var b = new AndroidJavaClass("org.pfd.reader.unity.PfdBridge"))
                {
                    var raw = b.CallStatic<sbyte[]>("decodeImage", data, 4096, 4096);
                    if (raw == null || raw.Length < 8) throw new Exception("Bild nicht dekodierbar");
                    int w = ((byte)raw[0] << 24) | ((byte)raw[1] << 16) | ((byte)raw[2] << 8) | (byte)raw[3];
                    int h = ((byte)raw[4] << 24) | ((byte)raw[5] << 16) | ((byte)raw[6] << 8) | (byte)raw[7];
                    var px = new byte[raw.Length - 8]; Buffer.BlockCopy(raw, 8, px, 0, px.Length);
                    return new PageImage { width = w, height = h, rgba = px };
                }
            }
            finally { AndroidJNI.DetachCurrentThread(); }
#else
            return new PageImage { encoded = data };
#endif
        }

        // ---------------------------------------------------------------- Rig versetzen
        void ClearMarkers() { foreach (var m in m_Markers) if (m) Destroy(m.gameObject); m_Markers.Clear(); }

        public void ShowMarkers(bool show)
        {
            foreach (var m in m_Markers) if (m) { m.gameObject.SetActive(show); if (show) m.Face(Head); }
        }

        /// <summary>Auf einen Sitzplatz setzen: Kopf an den Augenpunkt, Blick in Sitzrichtung.</summary>
        public Transform LastSeat { get; private set; }
        public void SitAt(Transform seat) { LastSeat = seat; SeatAt(seat); Seated?.Invoke(); }

        void SeatAt(Transform seat)
        {
            if (Rig == null || Head == null) return;
            if (!m_HasOrigin) { m_RigOrigin = new Pose(Rig.position, Rig.rotation); m_HasOrigin = true; }
            float headYaw = Head.eulerAngles.y, seatYaw = seat.eulerAngles.y;
            var rot = Quaternion.Euler(0, seatYaw - headYaw, 0);
            MoveRig(rot, Head.position, seat.position);
        }

        void ResetRig()
        {
            if (!m_HasOrigin || Rig == null) return;
            // Rig zurueck auf den Ursprung; Kopf bleibt dabei im Ursprungssystem, Buch & Co. wandern mit
            var rot = m_RigOrigin.rotation * Quaternion.Inverse(Rig.rotation);
            var pivot = Rig.position;
            MoveRig(rot, pivot, m_RigOrigin.position);
            m_HasOrigin = false;
        }

        /// <summary>Dreht um 'from' um rot und verschiebt 'from' nach 'to' – fuer Rig und mitwandernde Objekte.</summary>
        public bool MoveRig(Quaternion rot, Vector3 from, Vector3 to)
        {
            if (Rig != null && !m_HasOrigin) { m_RigOrigin = new Pose(Rig.position, Rig.rotation); m_HasOrigin = true; }
            void Apply(Transform t)
            {
                if (t == null) return;
                t.SetPositionAndRotation(to + rot * (t.position - from), rot * t.rotation);
            }
            Apply(Rig);
            if (FollowWithRig != null) foreach (var t in FollowWithRig) Apply(t);
            return true;
        }
    }
}
