using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PFDReader.Books;
using PFDReader.Spaces;
using PFDReader.Input;
using PFDReader.Reading;
using PFDReader.Rendering;
using PFDReader.UI;
using PFDReader.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using YVR.Core;

namespace PFDReader.Core
{
    /// <summary>Einstiegspunkt: baut Szene/Oberflaeche zur Laufzeit auf und verteilt die Controller-Eingaben.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class App : MonoBehaviour
    {
        public const string Version = "1.1.0-etappe2";

        TMP_FontAsset m_PageFont;
        Camera m_Camera;
        Transform m_Head, m_LeftAnchor, m_RightAnchor;
        Controllers m_Ctrl;
        LaserPointer m_LaserL, m_LaserR;
        GameObject m_ModelL, m_ModelR;
        XRUIInputModule m_UI;
        Workspace m_Workspace;
        BookView m_Book;
        ReaderController m_Reader;
        LibraryPanel m_Library;
        Toolbar m_Toolbar;
        ListPopup m_Popup;
        SettingsPopup m_Settings;
        FloatingWindow m_SettingsWin;
        EnvironmentManager m_Env;
        YVRCameraRig m_Rig;
        string m_LastEnv = "dark";
        Locomotion m_Loco;
        bool m_Roaming;
        SeatMarker m_HoverMarker;
        Audio.SoundManager m_Sound; bool m_SoundHooked;
        Media.TvController m_Tv;
        bool m_PointingTv;
        ConfirmDialog m_Confirm;

        Hand m_Active = Hand.Right;
        bool m_UiOwnsPress, m_BookDrag;
        float m_NextStickTurn;
        bool m_Placed;
        float m_StartTime;
        bool m_Loading;
        string m_ShownError;

        // ---------------------------------------------------------------- Aufbau
        void Awake()
        {
            MainThread.Capture();
            PFDReader.Books.ExtractedStore.CacheRoot = Application.temporaryCachePath;   // Cache fuer entpackte CBR/CB7
            Application.targetFrameRate = 90;
            Storage.Load();
            UiKit.Font = Resources.Load<TMP_FontAsset>("Fonts/Inter SDF") ?? TMP_Settings.defaultFontAsset;
            m_PageFont = Resources.Load<TMP_FontAsset>("Fonts/Lora SDF") ?? UiKit.Font;
            m_Active = Storage.Settings.preferredHand == 0 ? Hand.Left : Hand.Right;

            var rig = FindAnyObjectByType<YVRCameraRig>();
            m_Rig = rig;
            // Handtracking ist (noch) nicht in Gebrauch: SDK-Handmodelle ausblenden, sonst bleiben sie beim Bewegen im Raum stehen
            if (rig != null) { var hr = rig.transform.Find("TrackingSpace/HandRoot"); if (hr != null) hr.gameObject.SetActive(false); }
            if (rig != null)
            {
                m_Head = rig.centerEyeAnchor != null ? rig.centerEyeAnchor : rig.transform.Find("TrackingSpace/CenterEyeAnchor");
                m_LeftAnchor = rig.transform.Find("TrackingSpace/LeftControllerAnchor");
                m_RightAnchor = rig.transform.Find("TrackingSpace/RightControllerAnchor");
            }
            m_ModelL = AttachControllerModel(m_LeftAnchor, "Controllers/ControllerLeft", Quaternion.Euler(90, 180, -90));
            m_ModelR = AttachControllerModel(m_RightAnchor, "Controllers/ControllerRight", Quaternion.Euler(90, 0, -90));
            m_Camera = m_Head != null ? m_Head.GetComponent<Camera>() : Camera.main;
            if (m_Head == null && m_Camera != null) m_Head = m_Camera.transform;
            m_Ctrl = new Controllers(m_LeftAnchor, m_RightAnchor);

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            m_UI = es.AddComponent<XRUIInputModule>();

            m_LaserL = new GameObject("Laser L").AddComponent<LaserPointer>();
            m_LaserR = new GameObject("Laser R").AddComponent<LaserPointer>();

            m_Workspace = new GameObject("Workspace").AddComponent<Workspace>();
            m_Workspace.Head = m_Head;
            m_Book = new GameObject("Book").AddComponent<BookView>();
            m_Book.transform.SetParent(m_Workspace.transform, false);
            m_Reader = new ReaderController(m_Book, m_PageFont);
            m_Reader.Changed += RefreshToolbar;

            m_SettingsWin = new GameObject("SettingsWindow").AddComponent<FloatingWindow>();
            m_Env = new GameObject("Environment").AddComponent<EnvironmentManager>();
            m_Env.Rig = m_Rig != null ? m_Rig.transform : null;
            m_Env.Head = m_Head;
            m_Env.FollowWithRig = new[] { m_Workspace.transform, m_SettingsWin.transform };
            m_Env.SetPassthroughNative = SetPassthroughVisual;
            m_Env.MarkerFont = UiKit.Font;
            m_Env.SetSkybox = mat =>
            {
                if (m_Camera == null) return;
                if (mat != null) { RenderSettings.skybox = mat; m_Camera.clearFlags = CameraClearFlags.Skybox; }
                else if (m_Camera.clearFlags == CameraClearFlags.Skybox) { m_Camera.clearFlags = CameraClearFlags.SolidColor; }
            };
            m_Sound = new GameObject("Sound").AddComponent<Audio.SoundManager>();
            m_Tv = new GameObject("TV").AddComponent<Media.TvController>();
            m_Tv.EventCamera = m_UI.RayCamera;
            m_Tv.Head = m_Head;
            m_Env.RoomChanged += r => m_Tv.Attach(r);
            m_Loco = new GameObject("Locomotion").AddComponent<Locomotion>();
            m_Loco.Head = m_Head;
            m_Loco.MoveRig = m_Env.MoveRig;
            m_Env.Seated += () => m_Loco.SetSeated(m_Env.LastSeat);
            m_Env.Changed += () => { if (m_Settings != null) m_Settings.Refresh(); RefreshToolbar(); };
            BuildUi();
            Loc.Changed += () => { bool lib = m_Library.Visible; BuildUi(); ShowLibrary(lib || !m_Reader.IsOpen); m_Settings.Show(); RefreshToolbar(); };
            m_StartTime = Time.time;
        }

        /// <summary>Erzeugt alle Oberflaechen in der aktuellen Sprache (auch nach Sprachwechsel).</summary>
        void BuildUi()
        {
            foreach (var c in new Component[] { m_Library?.Canvas, m_Toolbar?.Canvas, m_Popup?.Canvas, m_Settings?.Canvas, m_Confirm?.Canvas })
                if (c) Destroy(c.gameObject);
            var cam = m_UI.RayCamera;
            var ws = m_Workspace.transform;

            m_Library = new LibraryPanel(ws, cam);
            m_Library.Canvas.transform.localPosition = new Vector3(0, 0, -0.012f);
            m_Library.Open += OpenBook;
            m_Library.Back += () => ShowLibrary(false);
            m_Library.Guide += OpenGuide;
            m_Library.Settings += ToggleSettings;
            m_Library.Import += () => { m_SettingsWin.PlaceInFront(m_Head); m_Settings.Show(SettingsPopup.PageImport); };
            m_Library.Delete += AskDelete;

            m_Toolbar = new Toolbar(ws, cam);
            m_Toolbar.Canvas.transform.localPosition = new Vector3(0, m_Book.pageHeight * 0.5f + 0.012f + 0.078f, -0.01f);
            m_Toolbar.Library += () => ShowLibrary(true);
            m_Toolbar.Chapters += ShowChapters;
            m_Toolbar.Bookmarks += ShowBookmarks;
            m_Toolbar.ToggleMark += () => m_Reader.ToggleMark();
            m_Toolbar.FontDown += () => ChangeSize(-1);
            m_Toolbar.FontUp += () => ChangeSize(+1);
            m_Toolbar.Prev += () => m_Reader.Turn(-1);
            m_Toolbar.Next += () => m_Reader.Turn(+1);
            m_Toolbar.ToggleDark += () => SetDark(!Storage.Settings.dark);
            m_Toolbar.Settings += ToggleSettings;

            m_Popup = new ListPopup(ws, cam);
            m_Popup.Canvas.transform.localPosition = new Vector3(0, 0, -0.07f);

            m_Settings = new SettingsPopup(m_SettingsWin.transform, cam);
            m_Settings.Canvas.transform.localPosition = Vector3.zero;
            m_Settings.Canvas.transform.localScale = Vector3.one * 0.00045f; // feste Groesse (~39 x 24 cm), unabhaengig vom Buch
            m_Settings.Env = m_Env;
            m_Settings.PassthroughChanged += SetPassthrough;
            m_Settings.DarkChanged += SetDark;
            m_Settings.FollowChanged += on => { Storage.Settings.follow = on; Storage.SaveSettings(); if (on) m_Workspace.CaptureFollow(); };
            m_Settings.LanguageChanged += Loc.SetLanguage;
            m_Settings.HandChanged += h => { Storage.Settings.preferredHand = h; Storage.SaveSettings(); m_Active = h == 0 ? Hand.Left : Hand.Right; };
            m_Settings.Recenter += () => m_Workspace.Recenter();
            m_Settings.Quit += QuitApp;
            m_Settings.Host = this;
            m_Settings.ShowLicenses += OpenLicenses;
            m_Settings.HeightChanged += () => m_Loco.HeightChanged();
            m_Settings.Sound = m_Sound;
            m_Settings.Imported += path => { m_Library.Refresh(); m_Library.SetStatus(Loc.T("Importiert: ") + Path.GetFileName(path)); };
            if (!m_SoundHooked) { m_SoundHooked = true; m_Sound.Changed += () => m_Settings?.RefreshPage(SettingsPopup.PageSound); }

            m_Confirm = new ConfirmDialog(ws, cam);
            m_Confirm.Canvas.transform.localPosition = new Vector3(0, 0, -0.11f);

            bool reading = m_Reader.IsOpen && m_Library != null;
            m_Toolbar.Canvas.gameObject.SetActive(reading);
        }

        void ToggleSettings()
        {
            if (m_Settings.Visible) { m_Settings.Hide(); return; }
            m_SettingsWin.PlaceInFront(m_Head);
            m_Settings.Show();
        }

        /// <summary>Aufloesung der Augenbilder: hoeher = weniger Kantenflimmern (Supersampling), mehr GPU-Last.</summary>
        public static void ApplyRenderQuality()
        {
            // Die Augenbild-Aufloesung legt die YVR-Laufzeit fest (nicht aenderbar). Wirksam ist der Kompositor:
            // 0 Standard = Supersampling normal, 1 Hoch = Supersampling Qualitaet, 2 Sehr hoch = zusaetzlich Nachschaerfen
            // Messung: Augenbild 2880x2664 wird aufs Display hochskaliert, Supersampling bringt sichtbar nichts.
            // Diese Kombination belastet den Kompositor am wenigsten (5,1 statt 6,6 ms) bei gleichem Bild.
            int q = 2;
            try { YVR.Core.YVRPlugin.Instance.SetEyeBufferLayerSettings(true, true, true, true); }
            catch (Exception e) { Debug.LogWarning("[PFD] Bildschaerfe: " + e.Message); }
            Debug.Log("[PFD] render quality=" + q);
        }

        /// <summary>Fixed Foveated Rendering des YVR SDK (Rand des Augenbildes mit geringerer Aufloesung).</summary>
        public static void ApplyFoveation()
        {
            int f = Mathf.Clamp(Storage.Settings.foveation, 0, 3);
            try
            {
                var m = YVR.Core.YVRManager.instance;
                if (m == null) return;
                m.fixedFoveatedRenderingDynamic = false;
                m.fixedFoveatedRenderingLevel = (YVR.Core.YVRQualityManager.FixedFoveatedRenderingLevel)f;
                Debug.Log("[PFD] foveation=" + f);
            }
            catch (Exception e) { Debug.LogWarning("[PFD] Foveation: " + e.Message); }
        }

        void Start()
        {
            ApplyRenderQuality();
            StartCoroutine(EnableSuperSampling());
            StartCoroutine(ApplySystemRefreshRate());
            StartCoroutine(AutoUpdateCheck());
            SetPassthroughVisual(Storage.Settings.environment == "passthrough");
            if (!Storage.Settings.firstRunDone)
            {
                // Erster Start nach der Installation: Bedienungsanleitung aufschlagen
                Storage.Settings.firstRunDone = true; Storage.SaveSettings();
                OpenGuide();
            }
            else
            {
                var last = Storage.Settings.lastBook;
                if (!string.IsNullOrEmpty(last) && File.Exists(last)) OpenBook(new LibraryEntry { path = last, key = Path.GetFileName(last), title = Path.GetFileNameWithoutExtension(last), kind = BookFormats.KindOf(last) });
                else ShowLibrary(true);
                // spaetere Starts: Lesebereich ausgeblendet, erst ueber X einblenden
                m_Workspace.gameObject.SetActive(false);
            }
            RefreshToolbar();
        }

        // Bildrate alle 10 s ins Log (Diagnose fuer Ruckeln/Zittern bei laengerem Sitzen)
        float m_PerfT; int m_PerfFrames; float m_PerfWorst;
        void LogPerf()
        {
            m_PerfFrames++; m_PerfWorst = Mathf.Max(m_PerfWorst, Time.unscaledDeltaTime);
            if (Time.unscaledTime - m_PerfT < 10f) return;
            float fps = m_PerfFrames / Mathf.Max(0.001f, Time.unscaledTime - m_PerfT);
            string room = m_Env.CurrentRoom != null ? m_Env.CurrentRoom.id : "-";
            Debug.Log("[PFD] perf fps=" + fps.ToString("F1") + " worst=" + (m_PerfWorst * 1000f).ToString("F0") + "ms q=" + Storage.Settings.renderQuality + " ffr=" + Storage.Settings.foveation + " room=" + room + " tv=" + (m_Tv.On ? 1 : 0) + " hz=" + DisplayHz());
            m_PerfT = Time.unscaledTime; m_PerfFrames = 0; m_PerfWorst = 0;
        }

        /// <summary>
        /// Supersampling-Filter des Kompositors einschalten (glaettet feine Linien/Kanten auf Distanz).
        /// Der YVRQualityManager setzt die Ebenen-Einstellungen nach 1 s selbst (ohne Supersampling) – daher danach.
        /// </summary>
        System.Collections.IEnumerator EnableSuperSampling()
        {
            yield return new WaitForSecondsRealtime(2f);
            ApplyRenderQuality();
            ApplyFoveation();
            StartCoroutine(FoveationTest());
        }

        static string StereoMode()
        {
            try { return YVR.Core.YVRManager.instance.cameraRenderer.stereoRenderingMode + " eye=" + YVR.Core.YVRPlugin.Instance.GetEyeTrackingEnable(); }
            catch (Exception e) { return "? " + e.Message; }
        }

        /// <summary>
        /// Messhilfe: "fovtest.txt" im App-Ordner mit Inhalt "run" schaltet die FFR-Stufen durch,
        /// mit "scale" die Bildschaerfe-Stufen (je 20 s) – Messwerte landen in fovtest-result.txt.
        /// </summary>
        System.Collections.IEnumerator FoveationTest()
        {
            string trig = Path.Combine(Application.persistentDataPath, "fovtest.txt");
            string mode = File.Exists(trig) ? File.ReadAllText(trig).Trim() : "";
            if (mode != "run" && mode != "scale") yield break;
            int keepF = Storage.Settings.foveation, keepQ = Storage.Settings.renderQuality;
            for (int round = 0; round < 2; round++)
                for (int i = 0; i <= (mode == "run" ? 3 : 2); i++)
                {
                    if (mode == "run") { Storage.Settings.foveation = i; ApplyFoveation(); }
                    else { Storage.Settings.renderQuality = i; ApplyRenderQuality(); }
                    yield return new WaitForSecondsRealtime(20f);
                    DumpPerf("round " + round + " " + mode + " " + i + " room " + (m_Env.CurrentRoom != null ? m_Env.CurrentRoom.id : "-") + " ffr " + Storage.Settings.foveation + " " + RenderInfo());
                }
            Storage.Settings.foveation = keepF; Storage.Settings.renderQuality = keepQ; ApplyFoveation(); ApplyRenderQuality();
        }

        static string RenderInfo()
        {
            string r = "q=" + Storage.Settings.renderQuality + " eyeTex=" + UnityEngine.XR.XRSettings.eyeTextureWidth + "x" + UnityEngine.XR.XRSettings.eyeTextureHeight;
            try
            {
                var rec = default(YVR.Core.YVRExtent2DInt);
                if (YVR.Core.YVRPlugin.Instance.GetRecommendedResolution(ref rec)) r += " recommended=" + rec.width + "x" + rec.height;
                var er = Vector2.zero; YVR.Core.YVRPlugin.Instance.GetEyeResolution(ref er); r += " eyeRes=" + er.x + "x" + er.y;
                r += " power=" + YVR.Core.YVRManager.instance.qualityManager.powerSetting;
            }
            catch (Exception e) { r += " (" + e.Message + ")"; }
            return r;
        }

        static void DumpPerf(string label)
        {
            Debug.Log("[PFD] dump " + label);
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var b = new AndroidJavaClass("org.pfd.reader.unity.PfdBridge"))
                    b.CallStatic("dumpPerf", act, label);
            }
            catch (Exception e) { Debug.LogWarning("[PFD] dumpPerf: " + e.Message); }
#endif
        }

        /// <summary>
        /// Bildwiederholrate des Headsets (72/80/90 Hz) uebernehmen – sonst waehlt die Laufzeit fuer unsere Sitzung 90 Hz.
        /// </summary>
        static float DisplayHz() { try { return YVR.Core.YVRPlugin.Instance.GetDisplayFrequency(); } catch { return 0f; } }

        System.Collections.IEnumerator ApplySystemRefreshRate()
        {
            float sys = 0f;
            float before = 0f;
            try { before = YVR.Core.YVRPlugin.Instance.GetDisplayFrequency(); } catch { }
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var b = new AndroidJavaClass("org.pfd.reader.unity.PfdBridge"))
                    sys = b.CallStatic<float>("systemRefreshRate", act);
            }
            catch (Exception e) { Debug.LogWarning("[PFD] display: " + e.Message); }
#endif
            Debug.Log("[PFD] display start=" + before + " system=" + sys);
            yield return new WaitForSecondsRealtime(1.5f);
            if (sys <= 0f) yield break;
            try
            {
                YVR.Core.YVRPlugin.Instance.SetDisplayFrequency(sys);
                Application.targetFrameRate = Mathf.RoundToInt(sys);
            }
            catch (Exception e) { Debug.LogWarning("[PFD] display set: " + e.Message); }
            yield return new WaitForSecondsRealtime(1f);
            float now = 0f;
            try { now = YVR.Core.YVRPlugin.Instance.GetDisplayFrequency(); } catch { }
            Debug.Log("[PFD] display set=" + sys + " now=" + now);
        }

        /// <summary>Originale PFD-MR-Controller (YVR SDK "D3") am Anker zeigen, SDK-Standardmodelle ausblenden.</summary>
        static GameObject AttachControllerModel(Transform anchor, string res, Quaternion rot)
        {
            if (anchor == null) return null;
            var prefab = Resources.Load<GameObject>(res);
            if (prefab == null) return null;
            foreach (Transform ch in anchor)
                if (ch.name.Contains("Touch") || ch.name.Contains("Controller")) ch.gameObject.SetActive(false);
            var go = Instantiate(prefab, anchor, false);
            go.name = "PFDController";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = rot;   // links ist das SDK-Modell um 180° verdreht
            return go;
        }

        // ---------------------------------------------------------------- Buecher
        async void OpenBook(LibraryEntry e)
        {
            if (m_Loading) return;
            m_Loading = true;
            m_Library.SetStatus(Loc.T("Lade") + " " + e.title + " …");
            try
            {
                var path = e.path;
                var task = Task.Run(() => ReaderDocument.Parse(path));
                while (!task.IsCompleted)
                {
                    float p = PFDReader.Books.ExtractedStore.Progress;
                    if (p >= 0f)
                    {
                        m_Library.SetStatus(Loc.T("Comic wird entpackt (nur beim ersten Öffnen)") + " … " + Mathf.RoundToInt(p * 100) + " %");
                        m_Library.SetProgress(p);
                    }
                    await Task.Delay(150);
                }
                m_Library.SetProgress(-1f);
                object parsed = await task;
                var doc = ReaderDocument.Open(path, parsed, m_PageFont, new TextStyle { fontSize = Storage.Settings.fontSize, dark = Storage.Settings.dark });
                m_Reader.Open(doc);
                ShowLibrary(false);
                RefreshToolbar();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PFD] Buch konnte nicht geoeffnet werden: " + ex);
                ShowLibrary(true);
                m_Library.SetStatus(Loc.T("Konnte nicht öffnen: ") + e.title + " – " + (ex is BookException ? Loc.Msg(ex.Message) : ex.GetType().Name + ": " + Loc.Msg(ex.Message)));
                if (Storage.Settings.lastBook == e.path) { Storage.Settings.lastBook = ""; Storage.SaveSettings(); }
            }
            finally { m_Loading = false; m_Library.SetProgress(-1f); }
        }

        /// <summary>Lizenzen &amp; Drittanbieter als Textbuch oeffnen (Resources/Licenses/THIRD_PARTY_NOTICES.txt).</summary>
        void OpenLicenses()
        {
            var ta = Resources.Load<TextAsset>("Licenses/THIRD_PARTY_NOTICES");
            if (ta == null) return;
            var dir = Path.Combine(Application.persistentDataPath, "guides");
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, "PFD eReader – Licenses.md");   // als Markdown: Kapitel pro Abschnitt
            File.WriteAllText(p, ta.text);
            if (!m_Workspace.gameObject.activeSelf) { m_Workspace.gameObject.SetActive(true); m_Workspace.Recenter(); }
            OpenBook(new LibraryEntry { path = p, key = "PFD eReader – Licenses.md", title = "PFD eReader – Licenses", kind = BookKind.Text });
        }

        /// <summary>Hoechstens alle 20 h beim Start in den GitHub-Releases nach einer neuen Version suchen.</summary>
        System.Collections.IEnumerator AutoUpdateCheck()
        {
            yield return new WaitForSecondsRealtime(8f);
            var st = Storage.Settings;
            if (!st.autoUpdateCheck || !AppInfo.HasRepo) yield break;
            if ((DateTime.UtcNow - new DateTime(Math.Max(0, st.lastUpdateCheck), DateTimeKind.Utc)).TotalHours < 20) yield break;
            UpdateChecker.Info found = null;
            yield return UpdateChecker.Check((info, err) => { found = info; if (err == null) { st.lastUpdateCheck = DateTime.UtcNow.Ticks; Storage.SaveSettings(); } });
            if (found == null || found.Version == st.skippedVersion) yield break;
            while (m_Confirm.Visible || m_Loading) yield return new WaitForSecondsRealtime(2f);
            if (!m_Workspace.gameObject.activeSelf) { m_Workspace.gameObject.SetActive(true); m_Workspace.Recenter(); }   // Hinweis haengt am Lesebereich
            var notes = found.Notes.Length > 220 ? found.Notes.Substring(0, 220) + " …" : found.Notes;
            m_Confirm.Show(Loc.T("Update verfügbar") + ": Beta v" + found.Version, notes, Loc.T("Ansehen"), () => m_Settings.ShowUpdate(found), false);
        }

        void OpenGuide()
        {
            string[] codes = { "de", "en", "fr" };
            string[] names = { "PFD eReader – Anleitung", "PFD eReader – Guide", "PFD eReader – Guide (FR)" };
            int lang = Storage.Settings.languageSet ? Loc.Language : 1;   // bis eine Sprache gewaehlt wurde: Englisch
            var dir = Path.Combine(Application.persistentDataPath, "guides");
            Directory.CreateDirectory(dir);
            // bebilderte PDF-Anleitung (Resources/Guides/guide-xx.pdf.bytes), sonst die alte Textfassung
            var pdf = Resources.Load<TextAsset>("Guides/guide-" + codes[lang] + ".pdf");
            if (pdf != null)
            {
                var p = Path.Combine(dir, names[lang] + ".pdf");
                File.WriteAllBytes(p, pdf.bytes);
                OpenBook(new LibraryEntry { path = p, key = names[lang] + ".pdf", title = names[lang], kind = BookFormats.KindOf(p) });
                return;
            }
            var asset = Resources.Load<TextAsset>("Guides/guide-" + codes[lang]);
            if (asset == null) { m_Library.SetStatus(Loc.T("Anleitung fehlt im Paket.")); return; }
            var path = Path.Combine(dir, names[lang] + ".txt");
            File.WriteAllText(path, asset.text);
            OpenBook(new LibraryEntry { path = path, key = names[lang] + ".txt", title = names[lang], kind = BookKind.Text });
        }

        void AskDelete(LibraryEntry e)
        {
            m_Confirm.Show(Loc.T("Buch vom Headset löschen?"), e.title + "\n" + e.path, Loc.T("Löschen"), () =>
            {
                try
                {
                    var full = Path.GetFullPath(e.path);
                    var allowed = new[] { Storage.SharedBooksDir, Storage.LegacyBooksDir }.Select(d => Path.GetFullPath(d).TrimEnd('/', '\\') + Path.DirectorySeparatorChar);
                    if (!allowed.Any(d => full.StartsWith(d, StringComparison.Ordinal))) throw new IOException("Pfad ausserhalb der Buecherordner");
                    if (m_Reader.IsOpen && Path.GetFullPath(m_Reader.Doc.FilePath) == full) { m_Reader.Close(); Storage.Settings.lastBook = ""; Storage.SaveSettings(); }
                    File.Delete(full);
                    m_Library.Refresh();
                    m_Library.SetCanGoBack(m_Reader.IsOpen);
                    m_Library.SetStatus(Loc.T("Buch gelöscht.") + " " + e.title);
                }
                catch (Exception ex) { m_Library.SetStatus(Loc.T("Löschen fehlgeschlagen: ") + ex.Message); }
            });
        }

        void ShowLibrary(bool show)
        {
            if (!show && !m_Reader.IsOpen) return;
            m_Popup.Hide();
            m_Library.Visible = show;
            m_Library.SetCanGoBack(m_Reader.IsOpen);
            m_Book.gameObject.SetActive(!show && m_Reader.IsOpen);
            m_Toolbar.Canvas.gameObject.SetActive(!show && m_Reader.IsOpen);
        }

        void ShowChapters()
        {
            if (!m_Reader.IsOpen) return;
            var toc = m_Reader.Doc.Toc();
            var labels = m_Reader.Doc.IsText ? toc.Select(t => t.title).ToList() : toc.Select((t, i) => Loc.T("Seite") + " " + (i + 1)).ToList();
            m_Popup.Show(Loc.T(m_Reader.Doc.IsText ? "Kapitel" : "Seiten"), labels, m_Reader.CurrentChapter, i => m_Reader.GoTo(toc[i].position));
        }

        void ShowBookmarks()
        {
            if (!m_Reader.IsOpen) return;
            var marks = m_Reader.Marks.Select(m => BookPosition.TryParse(m, out var p) ? p : default).ToList();
            var labels = marks.Select(p => m_Reader.DescribePosition(p)).ToList();
            m_Popup.Show(Loc.T("Merkliste"), labels, -1, i => m_Reader.GoTo(marks[i]),
                Loc.T("Noch keine Lesezeichen. Mit dem Stern in der Leiste die aktuelle Doppelseite merken."),
                i => () => { m_Reader.RemoveMark(marks[i].ToString()); ShowBookmarks(); });
        }

        void ChangeSize(int dir)
        {
            if (!m_Reader.IsOpen) return;
            if (m_Reader.CanZoom) m_Reader.StepZoom(dir);
            else m_Reader.SetFontSize(Storage.Settings.fontSize + dir * 4);
        }

        void SetDark(bool dark)
        {
            Storage.Settings.dark = dark;
            Storage.SaveSettings();
            m_Reader.ApplyTheme();
            RefreshToolbar();
        }

        /// <summary>Passthrough ein/aus ueber die Umgebung: aus = zuletzt genutzter Raum/Panorama bzw. dunkel.</summary>
        void SetPassthrough(bool on)
        {
            var cur = Storage.Settings.environment;
            if (on) { if (cur != "passthrough") m_LastEnv = cur; m_Env.Apply("passthrough"); }
            else m_Env.Apply(cur == "passthrough" ? m_LastEnv : cur);
        }

        void SetPassthroughVisual(bool on)
        {
            Storage.Settings.passthrough = on;
            Storage.SaveSettings();
            try { if (YVRManager.instance != null) YVRManager.instance.hmdManager.SetPassthrough(on); }
            catch (Exception e) { Debug.LogWarning("[PFD] Passthrough: " + e.Message); }
            if (m_Camera != null)
            {
                m_Camera.clearFlags = CameraClearFlags.SolidColor;
                m_Camera.backgroundColor = on ? new Color(0, 0, 0, 0) : new Color(0.035f, 0.04f, 0.065f, 1f);
            }
            RefreshToolbar();
        }

        void QuitApp()
        {
            m_Reader.Close();
            Storage.SaveSettings(); Storage.SaveBooks();
            Application.Quit();
        }

        void RefreshToolbar()
        {
            if (m_Toolbar == null) return;
            var err = m_Reader.LastError;
            string loc = !m_Reader.IsOpen ? "" : !string.IsNullOrEmpty(err) ? "<color=#EF5350>" + err + "</color>" : "<color=#E8E9F2>" + m_Reader.Doc.Title + "</color>  ·  " + m_Reader.Location + (m_Reader.Zoom > 1f ? "  ·  " + Mathf.RoundToInt(m_Reader.Zoom * 100) + " %" : "");
            m_Toolbar.SetLocation(loc, m_Reader.IsOpen ? m_Reader.ProgressValue : 0f);
            m_Toolbar.SetMarked(m_Reader.IsMarked);
            m_Toolbar.SetDark(Storage.Settings.dark);
            m_Toolbar.SetZoomMode(m_Reader.CanZoom);
        }

        // ---------------------------------------------------------------- Frame
        bool Modal => m_Confirm.Visible;

        void Update()
        {
            if (!m_Placed && m_Head != null && Time.time - m_StartTime > 0.4f && m_Head.position.sqrMagnitude > 0.0001f) { m_Workspace.Recenter(); m_Placed = true; m_Env.Restore(); }
            else if (!m_Placed && Time.time - m_StartTime > 2f) { m_Workspace.Recenter(); m_Placed = true; m_Env.Restore(); }

            m_Settings?.Tick();
            LogPerf();
            m_Ctrl.Update();
            ref var L = ref m_Ctrl.Left; ref var Rt = ref m_Ctrl.Right;
            if (m_ModelL && m_ModelL.activeSelf != L.connected) m_ModelL.SetActive(L.connected);
            if (m_ModelR && m_ModelR.activeSelf != Rt.connected) m_ModelR.SetActive(Rt.connected);
            if (Rt.triggerDown || Rt.gripDown) m_Active = Hand.Right;
            else if (L.triggerDown || L.gripDown) m_Active = Hand.Left;
            ref var c = ref m_Ctrl.Get(m_Active);
            var anchor = m_Active == Hand.Left ? m_LeftAnchor : m_RightAnchor;

            // Globale Tasten
            if (L.menuDown) { m_Confirm.Hide(); ToggleSettings(); }
            if (Rt.primaryDown) SetPassthrough(Storage.Settings.environment != "passthrough");
            if (L.primaryDown) // X: Lesebereich ein-/ausblenden; ausgeblendet = Umsehen/Bewegen im Raum
            {
                bool show = !m_Workspace.gameObject.activeSelf;
                m_Workspace.gameObject.SetActive(show);
                if (show) m_Workspace.Recenter();
            }
            m_Roaming = !m_Workspace.gameObject.activeSelf;
            m_Env.ShowMarkers(m_Roaming && !m_Loco.Seated);   // nur beim Herumlaufen, nicht im Sitzen
            if ((Rt.secondaryDown || L.secondaryDown) && !(m_PointingTv && Media.TvController.TvMode && m_Tv.On)) Back();

            var ray = new Ray(c.pose.position, c.pose.rotation * Vector3.forward);
            if (m_Roaming) { UpdateRoaming(ray, ref c, ref L, ref Rt); return; }
            bool bookVisible = m_Book.gameObject.activeInHierarchy;
            bool bookHit = false; Vector2 uv = default; float bookDist = float.MaxValue;
            if (bookVisible) bookHit = m_Book.Raycast(ray, out uv, out bookDist);
            if (Modal) bookHit = false; // Popup faengt alle Klicks ab

            m_UI.CurrentRay = ray;
            m_UI.PointerActive = c.connected;
            bool uiHit = m_UI.HasHit && (!bookHit || m_UI.CurrentHit.distance <= bookDist + 0.02f);
            if (c.triggerDown) m_UiOwnsPress = uiHit;
            m_UI.Pressed = c.trigger && m_UiOwnsPress;
            m_UI.PressedThisFrame = c.triggerDown && m_UiOwnsPress;
            m_UI.ReleasedThisFrame = c.triggerUp && m_UiOwnsPress;
            bool overTvUi = uiHit && m_Tv.Owns(m_UI.CurrentHit.gameObject);
            m_PointingTv = overTvUi;
            m_Tv.SetHover(uiHit && m_Tv.OwnsPanel(m_UI.CurrentHit.gameObject));
            bool tvKeys = overTvUi && m_Tv.HandleInput(c.stick, c.stickClickDown, c.secondaryDown);
            m_UI.Scroll = overTvUi && !tvKeys && Mathf.Abs(c.stick.y) > 0.2f ? new Vector2(0, c.stick.y * 0.6f) : Vector2.zero;

            // Greifen (Grip) – Buch, Bibliothek, Leisten und Popups
            bool overSettings = m_UI.HasHit && m_UI.CurrentHit.gameObject != null && m_UI.CurrentHit.gameObject.transform.IsChildOf(m_Settings.Canvas.transform);
            if (c.gripDown && anchor != null)
            {
                if (overSettings && uiHit) m_SettingsWin.BeginGrab(anchor);
                else if (bookHit || m_UI.HasHit) { m_Reader.CancelDrag(); m_Workspace.BeginGrab(anchor); }
            }
            if (m_SettingsWin.Grabbing)
            {
                if (!c.grip) m_SettingsWin.EndGrab();
                else m_SettingsWin.UpdateGrab(c.stick);
            }
            else if (m_Workspace.Grabbing)
            {
                if (!c.grip) m_Workspace.EndGrab();
                else m_Workspace.UpdateGrab(c.stick);
            }
            else if (!overTvUi) HandleStick(c.stick);

            // Blaettern per Trigger am Seitenrand / Aufschlagen
            if (!m_UiOwnsPress && bookHit && c.triggerDown && m_Reader.IsOpen && !m_Popup.Visible && m_Reader.Closed)
                m_Reader.SetClosed(false);
            else if (!m_UiOwnsPress && bookHit && c.triggerDown && m_Reader.IsOpen && !m_Popup.Visible)
            {
                float t = (1f - uv.x) * 0.5f;
                if (uv.x > 0.45f) m_BookDrag = m_Reader.BeginDrag(+1, t);
                else if (uv.x < -0.45f) m_BookDrag = m_Reader.BeginDrag(-1, t);
            }
            if (m_BookDrag)
            {
                if (c.trigger)
                {
                    var plane = new Plane(m_Book.transform.TransformDirection(Vector3.back), m_Book.transform.position);
                    if (plane.Raycast(ray, out var dist)) m_Reader.UpdateDrag((1f - m_Book.LocalU(ray.GetPoint(dist))) * 0.5f);
                }
                else { m_Reader.EndDrag(); m_BookDrag = false; }
            }
            if (c.triggerUp) m_UiOwnsPress = false;

            // Laser
            var laser = m_Active == Hand.Left ? m_LaserL : m_LaserR;
            var other = m_Active == Hand.Left ? m_LaserR : m_LaserL;
            other.Hide();
            if (!c.connected) laser.Hide();
            else if (uiHit) laser.Show(ray, true, m_UI.CurrentHit.worldPosition, overSettings ? m_SettingsWin.transform.forward : m_Workspace.transform.forward, c.trigger || m_SettingsWin.Grabbing);
            else if (bookHit) laser.Show(ray, true, ray.GetPoint(bookDist), m_Book.transform.forward, c.trigger || m_Workspace.Grabbing);
            else laser.Hide();   // nur bei bedienbaren Zielen sichtbar

            // Leseleiste
            bool overToolbar = m_UI.HasHit && m_UI.CurrentHit.gameObject != null && m_UI.CurrentHit.gameObject.transform.IsChildOf(m_Toolbar.Canvas.transform);
            // erst einblenden, wenn der Zeiger ueber die Oberkante des Buchs hinausgeht
            bool aboveBook = bookVisible && !Modal && !m_Workspace.Grabbing && uv.y > 1.05f && uv.y < 1.9f && Mathf.Abs(uv.x) < 1.1f && bookDist < 100f;
            m_Toolbar.Tick((overToolbar || aboveBook) && !overSettings && !m_Workspace.Grabbing, false);

            // Follow Me (pausiert beim Greifen, Blaettern und in Popups)
            m_Workspace.TickFollow(Storage.Settings.follow, m_Reader.IsTurning || m_BookDrag || Modal || m_Popup.Visible || m_SettingsWin.Grabbing);

            m_Reader.Update();
            if (m_Reader.IsOpen && m_Reader.LastError != m_ShownError) { m_ShownError = m_Reader.LastError; RefreshToolbar(); }
        }

        /// <summary>Lesebereich ausgeblendet: 15°-Drehen (rechter Stick), Gehen (linker Stick), Teleport/Hinsetzen (Trigger).</summary>
        void UpdateRoaming(Ray ray, ref ControllerState c, ref ControllerState L, ref ControllerState Rt)
        {
            m_UI.PointerActive = c.connected;   // Einstellungen und Fernseher
            m_UI.CurrentRay = ray;
            bool uiHit = m_UI.HasHit;
            if (c.triggerDown) m_UiOwnsPress = uiHit;
            m_UI.Pressed = c.trigger && m_UiOwnsPress; m_UI.PressedThisFrame = c.triggerDown && m_UiOwnsPress; m_UI.ReleasedThisFrame = c.triggerUp && m_UiOwnsPress;

            // Zeigt der Strahl auf ein Menue/den Fernseher, scrollt der Stick dieser Hand statt zu drehen/gehen
            bool overTv = uiHit && m_Tv.Owns(m_UI.CurrentHit.gameObject);
            m_PointingTv = overTv;
            m_Tv.SetHover(uiHit && m_Tv.OwnsPanel(m_UI.CurrentHit.gameObject));
            bool tvKeys = overTv && m_Tv.HandleInput(c.stick, c.stickClickDown, c.secondaryDown);
            bool stickScroll = uiHit && !tvKeys && Mathf.Abs(c.stick.y) > 0.2f;
            m_UI.Scroll = stickScroll ? new Vector2(0, c.stick.y * 0.6f) : Vector2.zero;
            // Einstellungsfenster greifen & ziehen (Grip), auch bei ausgeblendetem Lesebereich
            var anchor = m_Active == Hand.Left ? m_LeftAnchor : m_RightAnchor;
            bool overSettings = uiHit && m_Settings.Visible && m_UI.CurrentHit.gameObject != null && m_UI.CurrentHit.gameObject.transform.IsChildOf(m_Settings.Canvas.transform);
            if (c.gripDown && overSettings && anchor != null) m_SettingsWin.BeginGrab(anchor);
            bool grabbing = m_SettingsWin.Grabbing;
            if (grabbing) { if (!c.grip) m_SettingsWin.EndGrab(); else m_SettingsWin.UpdateGrab(c.stick); }
            bool activeIsRight = m_Active == Hand.Right;
            bool blockActive = uiHit || grabbing;
            m_Loco.TickSnapTurn(blockActive && activeIsRight ? 0 : Rt.stick.x);
            if (m_Env.CurrentRoom != null) m_Loco.TickMove(blockActive && !activeIsRight ? Vector2.zero : L.stick);

            var laser = m_Active == Hand.Left ? m_LaserL : m_LaserR;
            (m_Active == Hand.Left ? m_LaserR : m_LaserL).Hide();
            if (!c.connected) { laser.Hide(); return; }
            if (uiHit || grabbing) { if (uiHit) laser.Show(ray, true, m_UI.CurrentHit.worldPosition, m_UI.CurrentHit.gameObject.transform.forward, c.trigger || grabbing); else laser.Hide(); if (c.triggerUp) m_UiOwnsPress = false; return; }

            bool hit = Physics.Raycast(ray, out var h, 30f, ~(1 << 2), QueryTriggerInteraction.Collide);   // Layer 2: Buch-Kollisionshuelle
            var marker = hit ? h.collider.GetComponent<SeatMarker>() : null;
            if (marker != m_HoverMarker) { if (m_HoverMarker) m_HoverMarker.SetHover(false); m_HoverMarker = marker; if (marker) marker.SetHover(true); }
            bool floor = hit && marker == null && h.normal.y > 0.7f && m_Env.CurrentRoom != null && m_Loco.CanStandAt(h.point);
            if (marker != null || floor) laser.Show(ray, true, h.point, -h.normal, true);
            else laser.Hide();   // Waende/Moebel: kein Strahl
            if (c.triggerDown && !m_UiOwnsPress)
            {
                if (marker != null) m_Env.SitAt(marker.Seat);
                else if (floor) m_Loco.TeleportTo(h.point);
            }
            if (c.triggerUp) m_UiOwnsPress = false;
        }

        void HandleStick(Vector2 s)
        {
            if (m_Confirm.Visible) return;
            if (m_Settings.Visible && m_UI.HasHit && m_UI.CurrentHit.gameObject != null && m_UI.CurrentHit.gameObject.transform.IsChildOf(m_Settings.Canvas.transform))
            { if (Mathf.Abs(s.y) > 0.3f) m_Settings.ScrollBy(s.y * Time.deltaTime * 6f); return; }
            if (m_Popup.Visible) { if (Mathf.Abs(s.y) > 0.3f) m_Popup.ScrollBy(s.y * Time.deltaTime * 8f); return; }
            if (m_Library.Visible) { if (Mathf.Abs(s.y) > 0.3f) m_Library.ScrollBy(s.y * Time.deltaTime * 8f); return; }
            if (!m_Reader.IsOpen) return;
            if (m_Reader.CanZoom && m_Reader.Zoom > 1f)
            {
                // Bei Zoom verschiebt der Stick den Ausschnitt; Blaettern ueber die Pfeile
                if (s.sqrMagnitude > 0.04f) { m_Reader.PanBy(s * Time.deltaTime * 0.9f); }
                return;
            }
            if (Mathf.Abs(s.x) > 0.6f)
            {
                if (Time.time >= m_NextStickTurn) { m_Reader.Turn(s.x > 0 ? +1 : -1); m_NextStickTurn = Time.time + 0.55f; }
            }
            else m_NextStickTurn = 0;
        }

        void Back()
        {
            if (m_Confirm.Visible) { m_Confirm.Hide(); return; }
            if (m_Settings.Visible) { m_Settings.Hide(); return; }
            if (m_Popup.Visible) { m_Popup.Hide(); return; }
            if (m_Library.Visible && m_Reader.IsOpen) { ShowLibrary(false); return; }
            if (m_Library.Visible || !m_Reader.IsOpen) return;
            if (!m_Reader.Closed) m_Reader.SetClosed(true);
            else ShowLibrary(true);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) { Storage.SaveSettings(); Storage.SaveBooks(); }
            else StartCoroutine(ApplySystemRefreshRate());   // Rate koennte im Systemmenue geaendert worden sein
        }
        void OnApplicationQuit() { m_Reader?.Close(); Storage.SaveSettings(); }
    }
}
