using System;
using System.IO;
using System.Linq;
using System.Text;
using PFDReader.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PFDReader.EditorTools
{
    /// <summary>Werkzeuge fuer 3D-Raeume: Import einstellen, analysieren, Vorschau rendern, Szene bauen, Licht backen.</summary>
    public static class RoomTools
    {
        public const string RoomsDir = "Assets/PFDReader/Rooms";

        static string FbxOf(string id)
        {
            var dir = $"{RoomsDir}/{id}/Source";
            var f = Directory.GetFiles(dir, "*.fbx", SearchOption.AllDirectories).OrderByDescending(x => new FileInfo(x).Length).FirstOrDefault();
            if (f == null) throw new Exception("Keine FBX in " + dir);
            return f.Replace('\\', '/');
        }

        public static string Configure(string id)
        {
            var path = FbxOf(id);
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.generateSecondaryUV = true;
            mi.secondaryUVHardAngle = 88; mi.secondaryUVPackMargin = 8;
            mi.isReadable = false;
            mi.importCameras = false;
            mi.importLights = true;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.SaveAndReimport();
            // eingebettete Texturen herausloesen, damit sie komprimiert/verkleinert werden koennen
            var texDir = $"{RoomsDir}/{id}/Textures";
            Directory.CreateDirectory(texDir);
            bool ok = mi.ExtractTextures(texDir);
            AssetDatabase.Refresh();
            int n = 0;
            var srcTex = Directory.GetFiles($"{RoomsDir}/{id}/Source", "*.*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".meta") && !f.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase));
            foreach (var t in Directory.GetFiles(texDir).Where(f => !f.EndsWith(".meta")).Concat(srcTex))
            {
                var ti = AssetImporter.GetAtPath(t.Replace('\\', '/')) as TextureImporter;
                if (ti == null) continue;
                ti.maxTextureSize = 2048;
                var lower = t.ToLowerInvariant();
                if (lower.Contains("normal")) ti.textureType = TextureImporterType.NormalMap;
                else if (lower.Contains("rough") || lower.Contains("opacity") || lower.Contains("metal") || lower.Contains("_ao")) ti.sRGBTexture = false;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 4;
                var android = ti.GetPlatformTextureSettings("Android");
                android.overridden = true; android.maxTextureSize = 2048; android.format = TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(android);
                ti.SaveAndReimport(); n++;
            }
            mi.SaveAndReimport();
            return $"{path}: Texturen extrahiert={ok}, {n} Texturen konfiguriert";
        }

        public static string Report(string id)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(FbxOf(id));
            var sb = new StringBuilder();
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = new Bounds(); bool first = true;
            long tris = 0;
            foreach (var r in rs)
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) tris += mf.sharedMesh.triangles.LongLength / 3;
            }
            sb.AppendLine($"Renderer={rs.Length} Dreiecke={tris} Bounds center={b.center} size={b.size}");
            sb.AppendLine($"Lichter={go.GetComponentsInChildren<Light>(true).Length}");
            var mats = rs.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToList();
            sb.AppendLine($"Materialien={mats.Count}");
            foreach (var m in mats.Take(80))
            {
                var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                var col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                sb.AppendLine($"  {m.name} [{m.shader.name}] tex={(tex ? tex.name : "-")} col={ColorUtility.ToHtmlStringRGB(col)}");
            }
            // groesste Objekte (Orientierung)
            foreach (var r in rs.OrderByDescending(r => r.bounds.size.sqrMagnitude).Take(12))
                sb.AppendLine($"  obj {r.name}: c={r.bounds.center:F2} s={r.bounds.size:F2}");
            return sb.ToString();
        }

        /// <summary>Rendert den Raum aus einer Kopfposition (Weltkoordinaten des Modells) als Panorama-Streifen und Einzelbild.</summary>
        public static string Preview(string id, string x, string y, string z, string yaw)
        {
            var scenePath = $"{RoomsDir}/{id}/{id}.unity";
            Scene temp;
            GameObject root;
            bool useScene = File.Exists(scenePath);
            var prevSetup = EditorSceneManager.GetSceneManagerSetup();
            if (useScene)
            {
                temp = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                root = null;
            }
            else
            {
                temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FbxOf(id)), temp);
                var lgo = new GameObject("PreviewSun"); SceneManager.MoveGameObjectToScene(lgo, temp);
                var l = lgo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f; lgo.transform.rotation = Quaternion.Euler(50, 30, 0);
            }
            var camGo = new GameObject("PreviewCam"); SceneManager.MoveGameObjectToScene(camGo, temp);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = temp;
            cam.fieldOfView = 90; cam.nearClipPlane = 0.05f; cam.farClipPlane = 200;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.4f, 0.45f, 0.5f);
            var pos = new Vector3(F(x), F(y), F(z));
            int w = 1200, h = 900;
            var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var strip = new Texture2D(w * 2, h * 2, TextureFormat.RGB24, false);
            float baseYaw = F(yaw);
            int i = 0;
            foreach (var dy in new[] { 0f, 90f, 180f, 270f })
            {
                camGo.transform.SetPositionAndRotation(pos, Quaternion.Euler(10, baseYaw + dy, 0));
                cam.Render();
                RenderTexture.active = rt;
                var tmp = new Texture2D(w, h, TextureFormat.RGB24, false);
                tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0); tmp.Apply();
                strip.SetPixels((i % 2) * w, (1 - i / 2) * h, w, h, tmp.GetPixels());
                UnityEngine.Object.DestroyImmediate(tmp);
                i++;
            }
            strip.Apply();
            RenderTexture.active = null;
            var outPath = Path.GetFullPath($"Builds/room-{id}.png");
            Directory.CreateDirectory("Builds");
            File.WriteAllBytes(outPath, strip.EncodeToJPG(85));
            UnityEngine.Object.DestroyImmediate(strip);
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            EditorSceneManager.CloseScene(temp, true);
            return outPath + "  (oben links: Blickrichtung, dann +90°, +180°, +270°)";
        }

        /// <summary>Baut die Raumszene: Modell (nur Umkreis um den Sitzplatz), statisch, Sitzplatz, Licht-Grundaufbau.</summary>
        public static string BuildScene(string id, string title, string x, string y, string z, string yaw, string radius)
            => BuildSceneSeats(id, title, x, y, z, yaw, radius, "");

        public static string BuildSceneSeats(string id, string title, string x, string y, string z, string yaw, string radius, string seatList)
            => BuildRoom(id, title, x, y, z, yaw, radius, seatList, "", "0");

        /// <summary>
        /// Wie BuildSceneSeats, zusaetzlich: alles ausserhalb der Bounds des Objekts 'boundsName' (z. B. Aussenwaende, die den
        /// Sitzplatz umschliessen) + 'pad' Meter wird entfernt – so verschwinden Kopien/Nachbarraeume im Modell.
        /// </summary>
        public static string BuildRoom(string id, string title, string x, string y, string z, string yaw, string radius, string seatList, string boundsName, string pad)
        {
            var scenePath = $"{RoomsDir}/{id}/{id}.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var root = new GameObject("Room_" + id);
            SceneManager.MoveGameObjectToScene(root, scene);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FbxOf(id)), scene);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.transform.SetParent(root.transform, true);
            var seatPos = new Vector3(F(x), F(y), F(z));
            float r = F(radius);
            int removed = 0, outside = 0;
            Bounds? keep = null;
            if (!string.IsNullOrEmpty(boundsName))
            {
                var shell = model.GetComponentsInChildren<Renderer>(true)
                    .Where(rr => rr.name.Equals(boundsName, StringComparison.OrdinalIgnoreCase) || rr.name.StartsWith(boundsName + ".", StringComparison.OrdinalIgnoreCase) || rr.name.StartsWith(boundsName + "_", StringComparison.OrdinalIgnoreCase))
                    .Where(rr => { var bb = rr.bounds; bb.Expand(1f); return bb.Contains(seatPos); })
                    .OrderBy(rr => rr.bounds.size.sqrMagnitude).FirstOrDefault();
                if (shell != null) { var bb = shell.bounds; bb.Expand(F(pad) * 2f); keep = bb; }
            }
            foreach (var rend in model.GetComponentsInChildren<Renderer>(true))
            {
                var c = rend.bounds.ClosestPoint(seatPos);
                if (Vector3.Distance(c, seatPos) > r) { UnityEngine.Object.DestroyImmediate(rend.gameObject); removed++; continue; }
                if (keep.HasValue && !keep.Value.Contains(rend.bounds.center)) { UnityEngine.Object.DestroyImmediate(rend.gameObject); removed++; outside++; continue; }
                var rn = rend.name.ToLowerInvariant();
                bool glass = rn.Contains("window") || rn.Contains("glass") || rn.Contains("fenster")
                             || rend.sharedMaterials.Any(m => m && (m.name.ToLowerInvariant().Contains("glass") || m.name.ToLowerInvariant().Contains("window")));
                rend.shadowCastingMode = glass ? ShadowCastingMode.Off : ShadowCastingMode.On; // Licht durch Fenster lassen
                if (rend is MeshRenderer mr) mr.receiveGI = ReceiveGI.Lightmaps;
            }
            var glassMat = GlassMaterial(id);
            int glassSlots = 0, colliders = 0;
            foreach (var rend in model.GetComponentsInChildren<Renderer>(true))
            {
                var rn = rend.name.ToLowerInvariant();
                var mats = rend.sharedMaterials;
                bool isWindow = rn.Contains("window") || rn.Contains("fenster");
                for (int i = 0; i < mats.Length; i++)
                {
                    var mn = mats[i] ? mats[i].name.ToLowerInvariant() : "";
                    if (mn.Contains("glass") || (isWindow && (mn.Contains("glow") || mn.Contains("white")))) { mats[i] = glassMat; glassSlots++; }
                }
                rend.sharedMaterials = mats;
                // Kollision fuer Bewegung im Raum (Glas bleibt begehbar-dicht durch Rahmen/Waende)
                var mf = rend.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh && rend.bounds.size.magnitude > 0.25f) { var mc = rend.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; colliders++; }
            }
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            foreach (var l in model.GetComponentsInChildren<Light>(true)) l.lightmapBakeType = LightmapBakeType.Baked;

            // Leuchtende Materialien (Lampen) als Lichtquellen fuer das Backen
            foreach (var m in model.GetComponentsInChildren<Renderer>(true).SelectMany(rr => rr.sharedMaterials).Where(m => m).Distinct())
            {
                var n = m.name.ToLowerInvariant();
                if (n.Contains("glow") || n.Contains("emiss") || n.Contains("lamp") || n.Contains("bulb"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.65f) * 3f);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                    EditorUtility.SetDirty(m);
                }
            }

            // Lampen im Modell bekommen gebackene Punktlichter: Objekte mit Lampennamen oder kleinem Leuchtmaterial,
            // nahe beieinander liegende werden zusammengefasst (hoechstens 24)
            int lamps = 0;
            var lampPts = new System.Collections.Generic.List<Vector3>();
            foreach (var rend in model.GetComponentsInChildren<Renderer>(true))
            {
                var rn = rend.name.ToLowerInvariant();
                if (rn.Contains("switch")) continue;
                bool named = rn.Contains("light") || rn.Contains("lamp") || rn.Contains("bulb") || rn.Contains("chandelier");
                bool glows = rend.sharedMaterials.Any(m => m && (m.name.ToLowerInvariant().Contains("glow") || m.name.ToLowerInvariant().Contains("emiss")));
                if (!named && !(glows && rend.bounds.size.magnitude < 3f)) continue;
                var c = rend.bounds.center;
                var pos = c.y > 2.2f ? c + Vector3.down * (rend.bounds.extents.y + 0.25f) : c + Vector3.up * 0.05f;
                if (lampPts.Any(q => Vector3.Distance(q, pos) < 1.0f) || lampPts.Count >= 24) continue;
                lampPts.Add(pos);
                var lgo = new GameObject("Lamp_" + rend.name); lgo.transform.SetParent(root.transform, false);
                lgo.transform.position = pos;
                var pl = lgo.AddComponent<Light>();
                pl.type = LightType.Point; pl.range = c.y > 2.2f ? 6f : 3.5f; pl.intensity = c.y > 2.2f ? 1.6f : 0.9f; pl.color = new Color(1f, 0.82f, 0.62f);
                pl.shadows = LightShadows.Soft; pl.lightmapBakeType = LightmapBakeType.Baked;
                lamps++;
            }
            // Sonne: falls Fenster vorhanden, durch das groesste Fenster hindurch auf den Sitzplatz
            var win = model.GetComponentsInChildren<Renderer>(true).Where(rr => rr.name.ToLowerInvariant().Contains("window")).OrderByDescending(rr => rr.bounds.size.sqrMagnitude).FirstOrDefault();
            var sunGo = new GameObject("Sun"); sunGo.transform.SetParent(root.transform, false);
            if (win != null)
            {
                var toSeat = seatPos - win.bounds.center; toSeat.y = 0; toSeat.Normalize();
                sunGo.transform.rotation = Quaternion.LookRotation(toSeat * 1.0f + Vector3.down * 0.75f);
            }
            else sunGo.transform.rotation = Quaternion.Euler(38, F(yaw) + 55, 0);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1f, 0.93f, 0.82f); sun.intensity = 2.2f;
            sun.shadows = LightShadows.Soft; sun.lightmapBakeType = LightmapBakeType.Baked;

            var seat = new GameObject("Seat"); seat.transform.SetParent(root.transform, false);
            seat.transform.SetPositionAndRotation(seatPos, Quaternion.Euler(0, F(yaw), 0));
            seat.name = "Sofa";
            var info = root.AddComponent<RoomInfo>();
            info.id = id; info.title = title; info.seat = seat.transform;
            var seatList2 = new System.Collections.Generic.List<Transform> { seat.transform };
            info.seats = seatList2.ToArray();
            ApplySeats(info, seatList);
            info.skybox = SkyMaterial();
            info.skyEvening = EveningSkyMaterial();

            EditorSceneManager.SaveScene(scene, scenePath);
            EditorSceneManager.CloseScene(scene, true);
            return $"{scenePath}: {removed} entfernte Objekte (Radius {r} m, ausserhalb Huelle {(keep.HasValue ? keep.Value.ToString("F2") : "-")}: {outside}), {lamps} Lampen, Fenster={(win ? win.name : "-")}, Glas-Slots={glassSlots}, Collider={colliders}, Sitzplaetze={info.seats.Length}";
        }

        /// <summary>
        /// Sitzplaetze setzen: "Name:x,y,z,yaw[,mx,my,mz]" je Eintrag (Augenpunkt, Blickrichtung, optional Markierungspunkt
        /// auf der Sitzflaeche). Ein Eintrag mit dem Namen des Standardplatzes aendert diesen. Nicht genannte Plaetze entfallen.
        /// </summary>
        static void ApplySeats(RoomInfo info, string seatList)
        {
            var list = new System.Collections.Generic.List<Transform> { info.seat };
            foreach (var entry in (seatList ?? "").Split(';'))
            {
                var kv = entry.Split(':'); if (kv.Length != 2) continue;
                var v = kv[1].Split(',').Select(F).ToArray(); if (v.Length < 4) continue;
                var name = kv[0].Trim();
                Transform t = info.seat != null && info.seat.name == name ? info.seat : null;
                if (t == null) { var sgo = new GameObject(name); sgo.transform.SetParent(info.transform, false); t = sgo.transform; list.Add(t); }
                t.SetPositionAndRotation(new Vector3(v[0], v[1], v[2]), Quaternion.Euler(0, v[3], 0));
                var mk = t.Find("Marker");
                if (v.Length >= 7)
                {
                    if (mk == null) { mk = new GameObject("Marker").transform; mk.SetParent(t, false); }
                    mk.position = new Vector3(v[4], v[5], v[6]);
                }
                else if (mk != null) UnityEngine.Object.DestroyImmediate(mk.gameObject);
            }
            if (info.seats != null)
                foreach (var old in info.seats) if (old != null && !list.Contains(old)) UnityEngine.Object.DestroyImmediate(old.gameObject);
            info.seats = list.ToArray();
        }

        /// <summary>Sitzplaetze der bestehenden Raumszene ersetzen (ohne neu zu bauen/backen).</summary>
        public static string SetSeats(string id, string seatList)
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene($"{RoomsDir}/{id}/{id}.unity", OpenSceneMode.Single);
            var info = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            ApplySeats(info, seatList);
            EditorUtility.SetDirty(info);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            var res = string.Join("; ", info.seats.Select(t => $"{t.name}@{t.position:F2}{(t.Find("Marker") ? " M" + t.Find("Marker").position.ToString("F2") : "")}"));
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            return res;
        }

        /// <summary>Gleichmaessige Aufhellung: Raster weicher, schattenloser Punktlichter (nur wo Boden darunter liegt). Danach neu backen.</summary>
        public static string AddFillLights(string id, string spacing, string height)
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene($"{RoomsDir}/{id}/{id}.unity", OpenSceneMode.Single);
            var info = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            foreach (var old in info.GetComponentsInChildren<Light>(true).Where(x => x.name.StartsWith("Fill_")).ToList()) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var rs = info.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float sp = F(spacing), h = F(height); int n = 0;
            Physics.SyncTransforms();
            for (float x = b.min.x + sp * 0.5f; x < b.max.x; x += sp)
                for (float z = b.min.z + sp * 0.5f; z < b.max.z; z += sp)
                {
                    var top = new Vector3(x, h + 3f, z);
                    if (!Physics.Raycast(new Vector3(x, h, z), Vector3.down, out var floor, h + 0.5f)) continue;  // kein Boden: ausserhalb
                    if (floor.point.y > 0.6f) continue;
                    var go = new GameObject("Fill_" + n); go.transform.SetParent(info.transform, false);
                    go.transform.position = new Vector3(x, h, z);
                    var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = sp * 1.8f; l.intensity = 0.7f;
                    l.color = new Color(1f, 0.96f, 0.9f); l.shadows = LightShadows.None; l.lightmapBakeType = LightmapBakeType.Baked;
                    n++;
                }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            return n + " Fuelllichter";
        }

        /// <summary>
        /// Pflanzen mit Transparenzmaske: eigenes URP-Lit-Material mit Alpha-Clip, beidseitig, Textur Rooms/&lt;id&gt;/Textures/&lt;name&gt;_rgba.png.
        /// list: "materialname;…" – ersetzt diese Materialien in der Raumszene.
        /// </summary>
        public static string FixFoliage(string id, string list)
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene($"{RoomsDir}/{id}/{id}.unity", OpenSceneMode.Single);
            var info = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            var sb = new StringBuilder();
            foreach (var name in list.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                var texPath = $"{RoomsDir}/{id}/Textures/{name}_rgba.png";
                var ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (ti == null) { sb.AppendLine("fehlt " + texPath); continue; }
                ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f;
                var a = ti.GetPlatformTextureSettings("Android"); a.overridden = true; a.maxTextureSize = 2048; a.format = TextureImporterFormat.ASTC_6x6; ti.SetPlatformTextureSettings(a);
                ti.SaveAndReimport();
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                var mp = $"{RoomsDir}/{id}/{id}_{name}_Foliage.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, mp); }
                m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0); m.doubleSidedGI = true;
                m.SetFloat("_Smoothness", 0.2f);
                m.renderQueue = (int)RenderQueue.AlphaTest;
                EditorUtility.SetDirty(m);
                int n = 0;
                foreach (var r in info.GetComponentsInChildren<Renderer>(true))
                {
                    var ms = r.sharedMaterials; bool ch = false;
                    for (int i = 0; i < ms.Length; i++)
                    {
                        if (ms[i] == null) continue;
                        if (ms[i].name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            var nrm = ms[i].HasProperty("_BumpMap") ? ms[i].GetTexture("_BumpMap") : null;
                            if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
                            ms[i] = m; ch = true; n++;
                        }
                    }
                    if (ch) r.sharedMaterials = ms;
                }
                sb.AppendLine($"{name}: {n} Slots");
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            return sb.ToString();
        }

        /// <summary>Fernseher-Bildschirm festlegen: Mitte, Blickrichtung (yaw: wohin die Bildflaeche "schaut" + 180), Breite/Hoehe in m.</summary>
        public static string SetTv(string id, string x, string y, string z, string yaw, string w, string h)
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene($"{RoomsDir}/{id}/{id}.unity", OpenSceneMode.Single);
            var info = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            var t = info.transform.Find("TV");
            if (t == null) { t = new GameObject("TV").transform; t.SetParent(info.transform, false); }
            t.SetPositionAndRotation(new Vector3(F(x), F(y), F(z)), Quaternion.Euler(0, F(yaw), 0));
            t.localScale = new Vector3(F(w), F(h), 1);
            info.tv = t; EditorUtility.SetDirty(info);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            return "TV gesetzt";
        }

        /// <summary>
        /// Fernseher-Modell durch eigenen 16:9-Fernseher ersetzen (Bildbreite/-hoehe in m, Tiefe, Unterkante).
        /// Ausrichtung/Position kommen vom bestehenden TV-Anker (SetTv); das Original wird ausgeblendet.
        /// </summary>
        public static string ReplaceTv(string id, string rendererName, string w, string h, string depth, string bottomY)
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene($"{RoomsDir}/{id}/{id}.unity", OpenSceneMode.Single);
            var info = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            if (info.tv == null) return "zuerst SetTv";
            var orig = info.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == rendererName);
            Vector3 c = orig != null ? orig.bounds.center : info.tv.position;
            if (orig != null) orig.gameObject.SetActive(false);
            float W = F(w), H = F(h), D = F(depth), bottom = F(bottomY);
            var old = info.transform.Find("TV_Body"); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "TV_Body";
            body.transform.SetParent(info.transform, false);
            var fwd = info.tv.forward;
            var center = new Vector3(c.x, bottom + (H + 0.02f) * 0.5f, c.z);
            body.transform.SetPositionAndRotation(center, info.tv.rotation);
            body.transform.localScale = new Vector3(W + 0.02f, H + 0.02f, D);
            var mp = $"{RoomsDir}/{id}/{id}_TvBody.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, mp); }
            m.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.025f)); m.SetFloat("_Smoothness", 0.75f); m.SetFloat("_Metallic", 0.2f);
            EditorUtility.SetDirty(m);
            body.GetComponent<MeshRenderer>().sharedMaterial = m;
            info.tv.position = center - fwd * (D * 0.5f + 0.002f);
            info.tv.localScale = new Vector3(W, H, 1);
            EditorUtility.SetDirty(info);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            var res = $"TV {W}x{H} m, Bildmitte {info.tv.position:F3}";
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            return res;
        }

        /// <summary>Dunkler Sichtschutz-Block (z. B. hinter einem Tuerspalt), mit Kollision, ohne Lichtbeitrag.</summary>
        public static string AddBlocker(string id, string name, string cx, string cy, string cz, string sx, string sy, string sz)
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene($"{RoomsDir}/{id}/{id}.unity", OpenSceneMode.Single);
            var info = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            var old = info.transform.Find(name); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(info.transform, false);
            go.transform.position = new Vector3(F(cx), F(cy), F(cz)); go.transform.localScale = new Vector3(F(sx), F(sy), F(sz));
            var path = $"{RoomsDir}/{id}/{id}_Blocker.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", new Color(0.035f, 0.035f, 0.04f)); AssetDatabase.CreateAsset(m, path); }
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveGI = ReceiveGI.LightProbes;
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            var res = $"{name} @ {go.transform.position:F2} size {go.transform.localScale:F2}";
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            return res;
        }

        static Material GlassMaterial(string id)
        {
            var path = $"{RoomsDir}/{id}/{id}_Glass.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetFloat("_ZWrite", 0); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", new Color(0.85f, 0.92f, 1f, 0.08f));
            m.SetFloat("_Smoothness", 0.95f); m.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        public const string DaySkyTex = "Assets/AllSkyFree/Epic_BlueSunset/Epic_BlueSunset_EquiRect_flat.png";
        public const string EveningSkyTex = "Assets/AllSkyFree/Night MoonBurst/AllSky_Night_MoonBurst Equirect.png";
        public const string DaySkyPath = RoomsDir + "/Sky_Day_Pano.mat";
        public const string EveningSkyPath = RoomsDir + "/Sky_Evening_Pano.mat";

        /// <summary>Himmel als 360°-Panorama-Materialien anlegen; Texturen fuer Android verkleinern/komprimieren.</summary>
        public static string SkyImport()
        {
            var sb = new StringBuilder();
            foreach (var (tp, mp, exposure) in new[] { (DaySkyTex, DaySkyPath, 1.0f), (EveningSkyTex, EveningSkyPath, 1.0f) })
            {
                var ti = AssetImporter.GetAtPath(tp) as TextureImporter;
                if (ti == null) { sb.AppendLine("fehlt: " + tp); continue; }
                ti.textureShape = TextureImporterShape.Texture2D;
                ti.mipmapEnabled = true; ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp;
                ti.npotScale = TextureImporterNPOTScale.ToNearest;
                ti.maxTextureSize = 4096;
                var a = ti.GetPlatformTextureSettings("Android");
                a.overridden = true; a.maxTextureSize = 4096; a.format = TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(a);
                ti.SaveAndReimport();
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m == null) { m = new Material(Shader.Find("Skybox/Panoramic")); AssetDatabase.CreateAsset(m, mp); }
                m.shader = Shader.Find("Skybox/Panoramic");
                m.SetTexture("_MainTex", tex);
                m.SetFloat("_Mapping", 1); m.SetFloat("_ImageType", 0); m.SetFloat("_Exposure", exposure);
                m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                EditorUtility.SetDirty(m);
                sb.AppendLine($"{mp}: {tex.width}x{tex.height}");
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static Material EveningSkyMaterial() => AssetDatabase.LoadAssetAtPath<Material>(EveningSkyPath);

        static Material SkyMaterial()
        {
            var day = AssetDatabase.LoadAssetAtPath<Material>(DaySkyPath);
            if (day != null) return day;
            var path = $"{RoomsDir}/Sky_Day.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Skybox/Procedural"));
            m.SetFloat("_SunSize", 0.035f); m.SetFloat("_AtmosphereThickness", 0.85f);
            m.SetColor("_SkyTint", new Color(0.45f, 0.62f, 0.9f)); m.SetColor("_GroundColor", new Color(0.36f, 0.38f, 0.35f));
            m.SetFloat("_Exposure", 1.25f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Startet das Lichtbacken der Raumszene (asynchron). Status: BakeStatus.</summary>
        public static string Bake(string id, string texelsPerUnit)
        {
            PrepareBake(id, texelsPerUnit);
            return "Backen gestartet=" + Lightmapping.BakeAsync();
        }

        static Scene PrepareBake(string id, string texelsPerUnit)
        {
            var scenePath = $"{RoomsDir}/{id}/{id}.unity";
            EditorSceneManager.SaveOpenScenes(); // verhindert den modalen Speichern-Dialog
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(1.0f, 1.0f, 1.05f);
            RenderSettings.ambientEquatorColor = new Color(0.85f, 0.82f, 0.78f);
            RenderSettings.ambientGroundColor = new Color(0.45f, 0.42f, 0.38f);
            RenderSettings.skybox = null;
            var lsPath = $"{RoomsDir}/{id}/{id}_Lighting.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
            if (ls == null) { ls = new LightingSettings { name = id + " Lighting" }; AssetDatabase.CreateAsset(ls, lsPath); }
            ls.bakedGI = true; ls.realtimeGI = false;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            ls.lightmapResolution = F(texelsPerUnit);
            ls.lightmapPadding = 4;
            ls.lightmapMaxSize = 2048;
            ls.lightmapCompression = LightmapCompression.NormalQuality;
            ls.directionalityMode = LightmapsMode.NonDirectional;
            ls.ao = true; ls.aoMaxDistance = 0.6f; ls.aoExponentIndirect = 1f; ls.aoExponentDirect = 0f;
            ls.maxBounces = 3; ls.indirectScale = 1.4f; ls.albedoBoost = 1.2f;
            ls.directSampleCount = 32; ls.indirectSampleCount = 256; ls.environmentSampleCount = 128;
            ls.filteringMode = LightingSettings.FilterMode.Auto;
            ls.denoiserTypeDirect = LightingSettings.DenoiserType.OpenImage;
            ls.denoiserTypeIndirect = LightingSettings.DenoiserType.OpenImage;
            ls.denoiserTypeAO = LightingSettings.DenoiserType.OpenImage;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return scene;
        }

        /// <summary>
        /// Licht fuer eine Tageszeit einstellen und backen: "day" (Sonne, helle Umgebung) oder "evening"
        /// (Sonne aus, Lampen staerker, dunkle Umgebung). Danach mit StoreVariant die Lightmaps sichern.
        /// </summary>
        public static string BakeVariant(string id, string variant, string texelsPerUnit)
        {
            bool evening = variant == "evening";
            PrepareBake(id, texelsPerUnit); // oeffnet Szene, setzt Tageswerte
            var root = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            foreach (var l in root.GetComponentsInChildren<Light>(true))
            {
                if (l.type == LightType.Directional) l.enabled = !evening;
                else if (l.name.StartsWith("Fill_")) { l.intensity = evening ? 0.35f : 0.7f; l.color = evening ? new Color(1f, 0.8f, 0.6f) : new Color(1f, 0.96f, 0.9f); }
                else if (l.name.StartsWith("Lamp_"))
                {
                    bool high = l.transform.position.y - root.transform.position.y > 1.9f;
                    float i0 = high ? 1.6f : 0.9f, r0 = high ? 6f : 3.5f;
                    l.intensity = evening ? i0 * 2.6f : i0; l.range = evening ? r0 * 1.6f : r0;
                }
            }
            if (evening)
            {
                RenderSettings.ambientSkyColor = new Color(0.16f, 0.17f, 0.27f);
                RenderSettings.ambientEquatorColor = new Color(0.14f, 0.12f, 0.12f);
                RenderSettings.ambientGroundColor = new Color(0.08f, 0.065f, 0.05f);
            }
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return variant + ": Backen gestartet=" + Lightmapping.BakeAsync();
        }

        /// <summary>Kopiert die eben gebackenen Lightmaps nach Rooms/&lt;id&gt;/Lightmaps_&lt;variant&gt; und traegt sie im RoomInfo ein.</summary>
        public static string StoreVariant(string id, string variant)
        {
            if (Lightmapping.isRunning) return "Backen laeuft noch";
            var scene = SceneManager.GetActiveScene();
            var info = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            if (info == null || info.id != id) return "Raumszene nicht offen";
            var src = LightmapSettings.lightmaps.Select(l => l.lightmapColor).ToArray();
            Texture2D[] arr;
            if (variant == "day") arr = src; // die Szene selbst traegt das Tageslicht
            else
            {
                var dir = $"{RoomsDir}/{id}/Lightmaps_{variant}";
                Directory.CreateDirectory(dir);
                arr = new Texture2D[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    var sp = AssetDatabase.GetAssetPath(src[i]);
                    var dp = $"{dir}/{Path.GetFileName(sp)}";
                    AssetDatabase.DeleteAsset(dp);
                    AssetDatabase.CopyAsset(sp, dp);
                    arr[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(dp);
                }
            }
            if (variant == "day") info.lightmapsDay = arr; else info.lightmapsEvening = arr;
            EditorUtility.SetDirty(info);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"{variant}: {arr.Length} Lightmaps gesichert ({string.Join(",", arr.Select(t => t ? t.name : "null"))})";
        }

        public static string BakeStatus()
        {
            if (Lightmapping.isRunning) return "laeuft " + Mathf.RoundToInt(Lightmapping.buildProgress * 100) + " %";
            var s = SceneManager.GetActiveScene();
            if (s.isDirty) EditorSceneManager.SaveScene(s);
            return "fertig (" + LightmapSettings.lightmaps.Length + " Lightmaps) in " + s.path;
        }

        /// <summary>Main-Szene wieder oeffnen und alle Raumszenen in die Build-Liste aufnehmen.</summary>
        public static string RegisterRooms()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene> { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
            foreach (var d in Directory.GetDirectories(RoomsDir))
            {
                var id = Path.GetFileName(d);
                var p = $"{RoomsDir}/{id}/{id}.unity";
                if (File.Exists(p)) scenes.Add(new EditorBuildSettingsScene(p, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            // Katalog fuer die Laufzeit: id|Titel je Zeile
            var cat = new StringBuilder();
            foreach (var sc in scenes.Skip(1))
            {
                var id = Path.GetFileNameWithoutExtension(sc.path);
                var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(sc.path), @"\n  title: (.*)");
                cat.Append(id).Append('|').Append(m.Success ? m.Groups[1].Value.Trim() : id).Append('\n');
            }
            Directory.CreateDirectory("Assets/PFDReader/Resources");
            File.WriteAllText("Assets/PFDReader/Resources/rooms.txt", cat.ToString());
            AssetDatabase.ImportAsset("Assets/PFDReader/Resources/rooms.txt");
            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            return string.Join(", ", scenes.Select(x => x.path));
        }

        /// <summary>Vorschaubild vom Sitzplatz (fuer die Galerie) nach Resources/RoomPreviews/&lt;id&gt;.jpg.</summary>
        public static string Thumbnail(string id)
        {
            EditorSceneManager.SaveOpenScenes();
            var scenePath = $"{RoomsDir}/{id}/{id}.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var info = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            var camGo = new GameObject("ThumbCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 70; cam.nearClipPlane = 0.05f; cam.farClipPlane = 200;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.5f, 0.55f, 0.6f);
            camGo.transform.SetPositionAndRotation(info.seat.position, info.seat.rotation * Quaternion.Euler(6, 0, 0));
            int w = 480, h = 300;
            var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(camGo);
            var dir = "Assets/PFDReader/Resources/RoomPreviews";
            Directory.CreateDirectory(dir);
            var outPath = $"{dir}/{id}.jpg";
            File.WriteAllBytes(outPath, tex.EncodeToJPG(88));
            File.WriteAllBytes(Path.GetFullPath($"Builds/thumb-{id}.jpg"), tex.EncodeToJPG(88));
            UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            AssetDatabase.ImportAsset(outPath);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            return outPath;
        }

        /// <summary>Renderer mit Namensfilter: Name, Materialien, Bounds (zum Finden von Fenstern, Sitzgelegenheiten).</summary>
        public static string Find(string id, string filter)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(FbxOf(id));
            var sb = new StringBuilder();
            var f = filter.ToLowerInvariant().Split(',');
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var n = r.name.ToLowerInvariant();
                var mats = string.Join(",", r.sharedMaterials.Where(m => m).Select(m => m.name));
                if (!f.Any(x => n.Contains(x) || mats.ToLowerInvariant().Contains(x))) continue;
                sb.AppendLine($"{r.name}: [{mats}] c={r.bounds.center:F2} s={r.bounds.size:F2}");
            }
            return sb.ToString();
        }

        /// <summary>URP: 4x MSAA gegen Kantenflimmern (alle Pipeline-Assets).</summary>
        public static string Msaa(string samples)
        {
            var sb = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var a = AssetDatabase.LoadMainAssetAtPath(path);
                var so = new SerializedObject(a);
                var p = so.FindProperty("m_MSAA");
                if (p != null) { p.intValue = int.Parse(samples); so.ApplyModifiedProperties(); sb.AppendLine(path + " -> MSAA " + samples); }
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        /// <summary>Materialien der gebauten Raumszene mit Shader (Fehlersuche).</summary>
        public static string SceneMaterials(string id)
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene($"{RoomsDir}/{id}/{id}.unity", OpenSceneMode.Additive);
            var sb = new StringBuilder();
            foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)))
            {
                var ms = r.sharedMaterials;
                if (ms.Any(m => m == null || m.shader == null || !m.shader.isSupported || m.shader.name.Contains("Error") || m.name.ToLowerInvariant().Contains("glow") || m.name.ToLowerInvariant().Contains("wall")))
                    sb.AppendLine(r.name + ": " + string.Join(", ", ms.Select(m => m == null ? "NULL" : m.name + "[" + (m.shader ? m.shader.name : "-") + "]")));
            }
            EditorSceneManager.CloseScene(scene, true);
            return sb.ToString();
        }

        /// <summary>Senkrechte Strahlen in der gebauten Raumszene: "x,z;x,z;…" -> alle Treffer (Hoehe, Objekt) von oben.</summary>
        public static string Probe(string id, string points)
        {
            EditorSceneManager.SaveOpenScenes();
            var sp = $"{RoomsDir}/{id}/{id}.unity";
            Scene scene;
            if (File.Exists(sp)) scene = EditorSceneManager.OpenScene(sp, OpenSceneMode.Additive);
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var m = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FbxOf(id)), scene);
                foreach (var mf in m.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            Physics.SyncTransforms();
            var ps = scene.GetPhysicsScene();
            var sb = new StringBuilder();
            var hits = new RaycastHit[32];
            foreach (var pt in points.Split(';'))
            {
                var v = pt.Split(',').Select(F).ToArray(); if (v.Length < 2) continue;
                int n = ps.Raycast(new Vector3(v[0], 12f, v[1]), Vector3.down, hits, 20f, ~0, QueryTriggerInteraction.Ignore);
                var list = hits.Take(n).OrderByDescending(h => h.point.y).Select(h => $"{h.point.y:F2}:{h.collider.name}");
                sb.AppendLine($"({v[0]:F2},{v[1]:F2}) " + string.Join("  ", list));
            }
            EditorSceneManager.CloseScene(scene, true);
            return sb.ToString();
        }

        /// <summary>
        /// Haengelampen (Kabel, Schirm, leuchtender Boden) unter der Decke an "x,z;x,z", Unterkante auf 'bottom' m;
        /// je Lampe ein "Lamp_"-Punktlicht (gebacken, Tag/Abend-Werte in BakeVariant). Danach neu backen.
        /// </summary>
        public static string AddPendants(string id, string points, string bottom)
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene($"{RoomsDir}/{id}/{id}.unity", OpenSceneMode.Single);
            var info = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            foreach (var old in info.GetComponentsInChildren<Transform>(true).Where(x => x.name.StartsWith("Pendant_")).ToList()) if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            string matDir = $"{RoomsDir}/{id}";
            Material Mat(string name, Color c, Color emit, float smooth)
            {
                var path = $"{matDir}/{id}_{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0.3f);
                if (emit.maxColorComponent > 0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
                EditorUtility.SetDirty(m); return m;
            }
            var shadeMat = Mat("PendantShade", new Color(0.05f, 0.05f, 0.055f), Color.black, 0.4f);
            var glowMat = Mat("PendantGlow", new Color(1f, 0.9f, 0.75f), new Color(1f, 0.82f, 0.6f) * 2.2f, 0.2f);
            Physics.SyncTransforms();
            float bot = F(bottom); int n = 0; var sb = new StringBuilder();
            foreach (var pt in points.Split(';'))
            {
                var v = pt.Split(',').Select(F).ToArray(); if (v.Length < 2) continue;
                float ceil = Physics.Raycast(new Vector3(v[0], bot, v[1]), Vector3.up, out var hit, 6f) ? hit.point.y : bot + 1.2f;
                var root = new GameObject("Pendant_" + n).transform; root.SetParent(info.transform, false);
                root.position = new Vector3(v[0], bot, v[1]);
                GameObject Prim(PrimitiveType t, string name, Vector3 lp, Vector3 sc, Material m)
                {
                    var g = GameObject.CreatePrimitive(t); g.name = name; UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
                    g.transform.SetParent(root, false); g.transform.localPosition = lp; g.transform.localScale = sc;
                    var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    return g;
                }
                float cord = ceil - (bot + 0.22f);
                Prim(PrimitiveType.Cylinder, "Cord", new Vector3(0, 0.22f + cord * 0.5f, 0), new Vector3(0.008f, cord * 0.5f, 0.008f), shadeMat);
                Prim(PrimitiveType.Cylinder, "Canopy", new Vector3(0, ceil - bot - 0.01f, 0), new Vector3(0.1f, 0.01f, 0.1f), shadeMat);
                Prim(PrimitiveType.Cylinder, "Shade", new Vector3(0, 0.12f, 0), new Vector3(0.32f, 0.1f, 0.32f), shadeMat);
                Prim(PrimitiveType.Cylinder, "Diffuser", new Vector3(0, 0.015f, 0), new Vector3(0.29f, 0.005f, 0.29f), glowMat);
                var lgo = new GameObject("Lamp_Pendant_" + n); lgo.transform.SetParent(root, false); lgo.transform.localPosition = new Vector3(0, -0.08f, 0);
                var l = lgo.AddComponent<Light>(); l.type = LightType.Point; l.range = 6f; l.intensity = 1.6f; l.color = new Color(1f, 0.82f, 0.62f);
                l.shadows = LightShadows.Soft; l.lightmapBakeType = LightmapBakeType.Baked;
                sb.Append($"Pendant_{n} bei ({v[0]:F2},{v[1]:F2}) Decke {ceil:F2}; ");
                n++;
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            return sb.ToString();
        }

        /// <summary>Simuliert das Aufstehen von jedem Sitzplatz (gleiche Logik wie im Spiel).</summary>
        public static string TestStandUp(string id)
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene($"{RoomsDir}/{id}/{id}.unity", OpenSceneMode.Single);
            var info = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            Physics.SyncTransforms();
            var sb = new StringBuilder();
            foreach (var seat in info.seats)
            {
                var feet = PFDReader.Spaces.Locomotion.StandSpot(seat.position, seat.forward);
                sb.AppendLine($"{seat.name} kopf={seat.position:F2} yaw={seat.eulerAngles.y:F0} -> fuesse={feet:F2} dist={Vector2.Distance(new Vector2(seat.position.x, seat.position.z), new Vector2(feet.x, feet.z)):F2}");
            }
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            return sb.ToString();
        }

        /// <summary>Material eines Submeshes (z. B. Bildschirm mit Standbild) durch das eines anderen Submeshes ersetzen.</summary>
        public static string CopySubMaterial(string id, string rendererName, string from, string to)
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene($"{RoomsDir}/{id}/{id}.unity", OpenSceneMode.Single);
            var info = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            var r = info.GetComponentsInChildren<Renderer>(true).FirstOrDefault(x => x.name == rendererName);
            if (r == null) return "nicht gefunden";
            var ms = r.sharedMaterials; ms[int.Parse(to)] = ms[int.Parse(from)]; r.sharedMaterials = ms;
            EditorUtility.SetDirty(r);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            return "ok: " + string.Join(",", ms.Select(m => m ? m.name : "-"));
        }

        /// <summary>Einzelbild aus der Raumszene mit frei waehlbarem Sichtfeld/Aufloesung (MSAA aus, um feine Fugen sichtbar zu machen).</summary>
        public static string Shot(string id, string x, string y, string z, string yaw, string pitch, string fov, string w, string h, string name)
            => ShotRefl(id, x, y, z, yaw, pitch, fov, w, h, name, "-1");

        public static string ShotRefl(string id, string x, string y, string z, string yaw, string pitch, string fov, string w, string h, string name, string refl)
        {
            float prevRefl = RenderSettings.reflectionIntensity;
            EditorSceneManager.SaveOpenScenes();
            var temp = EditorSceneManager.OpenScene($"{RoomsDir}/{id}/{id}.unity", OpenSceneMode.Additive);
            var camGo = new GameObject("ShotCam"); SceneManager.MoveGameObjectToScene(camGo, temp);
            var cam = camGo.AddComponent<Camera>(); cam.scene = temp;
            cam.fieldOfView = F(fov); cam.nearClipPlane = 0.05f; cam.farClipPlane = 200;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.magenta;
            camGo.transform.SetPositionAndRotation(new Vector3(F(x), F(y), F(z)), Quaternion.Euler(F(pitch), F(yaw), 0));
            if (F(refl) >= 0) RenderSettings.reflectionIntensity = F(refl);
            var prevSky = RenderSettings.skybox;
            var rinfo = UnityEngine.Object.FindObjectsByType<RoomInfo>(FindObjectsSortMode.None).FirstOrDefault(r => r.gameObject.scene == temp);
            if (rinfo != null && rinfo.skybox != null) { RenderSettings.skybox = rinfo.skybox; cam.clearFlags = CameraClearFlags.Skybox; }
            int W = int.Parse(w), H = int.Parse(h);
            var rt = new RenderTexture(W, H, 24) { antiAliasing = 1 }; cam.targetTexture = rt;
            cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = null;
            var outPath = Path.GetFullPath($"Builds/shot-{name}.png"); Directory.CreateDirectory("Builds");
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            EditorSceneManager.CloseScene(temp, true);
            RenderSettings.reflectionIntensity = prevRefl;
            RenderSettings.skybox = prevSky;
            return outPath;
        }

        /// <summary>Renderer einer Raumszene, deren Name/Material/Textur auf einen Fernseher hindeutet (Bounds + Material).</summary>
        public static string FindTv(string id, string pattern)
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene($"{RoomsDir}/{id}/{id}.unity", OpenSceneMode.Single);
            var rx = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var sb = new StringBuilder();
            var info = UnityEngine.Object.FindFirstObjectByType<RoomInfo>();
            if (info != null && info.tv != null) sb.AppendLine($"Anker: {info.tv.position:F3} rot {info.tv.eulerAngles:F1} scale {info.tv.localScale:F3}");
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mats = string.Join(",", r.sharedMaterials.Where(m => m).Select(m => m.name + "[" + (m.mainTexture ? m.mainTexture.name : "-") + "]"));
                if (!rx.IsMatch(r.name) && !rx.IsMatch(mats)) continue;
                var b = r.bounds;
                sb.AppendLine($"{r.name} active={r.gameObject.activeInHierarchy} c={b.center:F3} size={b.size:F3} rot={r.transform.eulerAngles:F1} lossy={r.transform.lossyScale:F3} mats={mats}");
                var mf = r.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh)
                {
                    var mesh = mf.sharedMesh;
                    sb.AppendLine($"   local bounds c={mesh.bounds.center:F3} size={mesh.bounds.size:F3}");
                    for (int si = 0; si < mesh.subMeshCount; si++)
                    {
                        var idx = mesh.GetIndices(si); var v = mesh.vertices;
                        var bb = new Bounds(v[idx[0]], Vector3.zero); foreach (var k in idx) bb.Encapsulate(v[k]);
                        var wc = r.transform.TransformPoint(bb.center);
                        var n = mesh.normals.Length > 0 ? r.transform.TransformDirection(mesh.normals[idx[0]]) : Vector3.zero;
                        sb.AppendLine($"   sub{si} local c={bb.center:F3} size={bb.size:F3} world c={wc:F3} n={n:F2}");
                    }
                }
            }
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            return sb.ToString();
        }

        /// <summary>Alle Renderer der offenen Szenen (auch DontSave/versteckte) mit Pfad und Position.</summary>
        public static string ListRenderers()
        {
            var sb = new StringBuilder();
            foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (EditorUtility.IsPersistent(r)) continue;
                var t = r.transform; var path = t.name; while (t.parent) { t = t.parent; path = t.name + "/" + path; }
                sb.AppendLine($"{path} [{r.GetType().Name}] pos={r.transform.position:F2} scene={r.gameObject.scene.name} flags={r.gameObject.hideFlags} active={r.gameObject.activeInHierarchy}");
            }
            return sb.ToString();
        }

        /// <summary>Grundriss von oben (orthografisch, alles oberhalb cutY ausgeblendet) -> Builds/top-&lt;id&gt;.png; liefert die Abbildung Welt->Pixel.</summary>
        public static string TopView(string id, string cutY, string ppm)
        {
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FbxOf(id)), temp);
            var lgo = new GameObject("L"); SceneManager.MoveGameObjectToScene(lgo, temp);
            var l = lgo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.3f; lgo.transform.rotation = Quaternion.Euler(70, 20, 0);
            var rs = root.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float cut = F(cutY), p = F(ppm);
            var camGo = new GameObject("TopCam"); SceneManager.MoveGameObjectToScene(camGo, temp);
            var cam = camGo.AddComponent<Camera>(); cam.scene = temp;
            cam.orthographic = true; cam.orthographicSize = b.extents.z;
            cam.aspect = b.size.x / b.size.z;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            camGo.transform.SetPositionAndRotation(new Vector3(b.center.x, cut, b.center.z), Quaternion.Euler(90, 0, 0));
            cam.nearClipPlane = 0.01f; cam.farClipPlane = cut - b.min.y + 1;
            int w = Mathf.RoundToInt(b.size.x * p), h = Mathf.RoundToInt(b.size.z * p);
            var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory("Builds");
            File.WriteAllBytes(Path.GetFullPath($"Builds/top-{id}.png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            EditorSceneManager.CloseScene(temp, true);
            // Bild: x nach rechts = +X, oben = +Z
            return $"minX={b.min.x:F2} maxX={b.max.x:F2} minZ={b.min.z:F2} maxZ={b.max.z:F2} minY={b.min.y:F2} maxY={b.max.y:F2} w={w} h={h}";
        }

        static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    }
}
