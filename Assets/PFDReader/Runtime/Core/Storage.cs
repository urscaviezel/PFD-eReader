using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using PFDReader.Books;
using UnityEngine;

namespace PFDReader.Core
{
    [Serializable]
    public sealed class AppSettings
    {
        public int version = 1;
        public bool passthrough = false;
        public bool dark = false;
        public float fontSize = 46f;
        public float distance = 1.15f;
        public float scale = 1f;
        public string lastBook = "";
        public int language = 1; // 0 DE, 1 EN, 2 FR (Standard: Englisch)
        public bool languageSet = false;
        public int turnAngle = 15;                 // Drehung rechter Stick: 0 = gleitend, sonst Schritt in Grad (15/30/45/60)          // Sprache ausdruecklich gewaehlt (sonst Anleitung auf Englisch)
        public bool follow = false;
        public int preferredHand = 1; // 0 links, 1 rechts
        public string environment = "room:Loft"; // Standard: Loft // passthrough | dark | room:<id> | pano:<pfad>
        public float environmentDim = 1f;          // 1 = volle Helligkeit
        public bool panoramaSwapEyes = false;
        public int timeOfDay = 0;                  // 0 Tag, 1 Abend (3D-Raeume)
        public float playerHeight = 1.75f;         // Koerpergroesse in m (Augenhoehe stehend = Groesse - 0.11)
        // Sound
        public float tvVolume = 0.7f;
        public bool tvMode = true;
        public bool firstRunDone = false;          // erster Start nach Installation: Anleitung zeigen
        // Updates (GitHub Releases)
        public bool autoUpdateCheck = true;
        public long lastUpdateCheck = 0;           // UTC-Ticks der letzten automatischen Suche
        public string skippedVersion = "";         // "Spaeter" fuer diese Version gewaehlt
        public int foveation = 3;                 // Fixed Foveated Rendering 0 Aus, 1 Niedrig, 2 Mittel, 3 Hoch (Standard: Hoch)
        public int renderQuality = 0;              // 0 Standard, 1 Hoch, 2 Sehr hoch (Aufloesung der Augenbilder)
        public bool soundOn = true;                // Hauptschalter
        public float masterVolume = 0.8f;
        public List<AmbientEntry> ambient = new List<AmbientEntry>();
        public bool ambientPlaying = false;
        public string musicSource = "";            // Ordner oder Playlist (.m3u/.m3u8)
        public float musicVolume = 0.6f;
        public int musicIndex = 0;
        // Import
        public string smbServer = "", smbShare = "", smbUser = "", smbDomain = "", smbFolder = "";
        public bool smbRemember = false;
        public int keyboardLayout = -1;            // -1 = wie UI-Sprache, sonst 0 DE, 1 EN, 2 FR
        public string importLastDir = "";
        public bool migratedLegacy = false;
    }

    [Serializable]
    public sealed class AmbientEntry
    {
        public string path;
        public float volume = 0.7f;
        public bool on = true;
    }

    [Serializable]
    public sealed class BookState
    {
        public string key;
        public string position = "0:0";
        public List<string> marks = new List<string>();
        public float zoom = 1f;
        public long lastOpened;
    }

    [Serializable]
    sealed class BookStateList { public List<BookState> books = new List<BookState>(); }

    /// <summary>Pfade, Einstellungen und Lesestaende (JSON im App-Speicher).</summary>
    public static class Storage
    {
        static string Dir => Application.persistentDataPath;
        static string SettingsPath => Path.Combine(Dir, "settings.json");
        static string BooksPath => Path.Combine(Dir, "books.json");
        static Dictionary<string, BookState> s_Books;
        public static AppSettings Settings { get; private set; }

        /// <summary>Wurzel des gemeinsamen Speichers (/storage/emulated/0).</summary>
        public static string ExternalRoot => Path.GetDirectoryName(SharedBooksDir);

        /// <summary>Gemeinsamer Buecherordner (wie alte App): /storage/emulated/0/eBooks.</summary>
        public static string BooksDirOverride;   // nur fuer Editor-Vorschaubilder
        public static string SharedBooksDir
        {
            get
            {
                if (!string.IsNullOrEmpty(BooksDirOverride)) return BooksDirOverride;
#if UNITY_ANDROID && !UNITY_EDITOR
                try { using (var b = new AndroidJavaClass("org.pfd.reader.unity.PfdBridge")) return Path.Combine(b.CallStatic<string>("externalRoot"), "eBooks"); }
                catch { return "/storage/emulated/0/eBooks"; }
#else
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "eBooks");
#endif
            }
        }

        /// <summary>Privater Datenordner der App (/data/user/0/&lt;paket&gt;), dort liegen Buecher/Prefs der alten Version.</summary>
        public static string PrivateRoot
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var dir = act.Call<AndroidJavaObject>("getFilesDir"))
                        return Path.GetDirectoryName(dir.Call<string>("getAbsolutePath"));
                }
                catch { return "/data/data/" + Application.identifier; }
#else
                return Path.Combine(Dir, "private");
#endif
            }
        }

        public static string LegacyBooksDir => Path.Combine(PrivateRoot, "files", "books");

        public static void Load()
        {
            try { Settings = File.Exists(SettingsPath) ? JsonUtility.FromJson<AppSettings>(File.ReadAllText(SettingsPath)) : null; }
            catch (Exception e) { Debug.LogWarning("[PFD] settings.json defekt: " + e.Message); }
            if (Settings == null) Settings = new AppSettings();
            s_Books = new Dictionary<string, BookState>();
            try
            {
                if (File.Exists(BooksPath))
                    foreach (var b in JsonUtility.FromJson<BookStateList>(File.ReadAllText(BooksPath)).books)
                        if (!string.IsNullOrEmpty(b.key)) s_Books[b.key] = b;
            }
            catch (Exception e) { Debug.LogWarning("[PFD] books.json defekt: " + e.Message); }
            if (!Settings.migratedLegacy) MigrateLegacy();
        }

        public static void SaveSettings() => WriteAtomic(SettingsPath, JsonUtility.ToJson(Settings, true));

        public static BookState Book(string key)
        {
            if (!s_Books.TryGetValue(key, out var b)) { b = new BookState { key = key }; s_Books[key] = b; }
            return b;
        }

        public static void SaveBooks()
        {
            var list = new BookStateList { books = s_Books.Values.ToList() };
            WriteAtomic(BooksPath, JsonUtility.ToJson(list, false));
        }

        static void WriteAtomic(string path, string content)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, content);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogWarning("[PFD] Speichern fehlgeschlagen: " + e.Message); }
        }

        // ---------------------------------------------------------------- Uebernahme aus der alten App (SharedPreferences "reader")
        static void MigrateLegacy()
        {
            Settings.migratedLegacy = true;
            var prefs = Path.Combine(PrivateRoot, "shared_prefs", "reader.xml");
            if (!File.Exists(prefs)) { SaveSettings(); return; }
            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                doc.Load(prefs);
                var ints = new Dictionary<string, int>(); var floats = new Dictionary<string, float>();
                var bools = new Dictionary<string, bool>(); var sets = new Dictionary<string, List<string>>();
                foreach (XmlNode n in doc.DocumentElement.ChildNodes)
                {
                    if (!(n is XmlElement e)) continue;
                    var name = e.GetAttribute("name"); var val = e.GetAttribute("value");
                    switch (e.Name)
                    {
                        case "int": if (int.TryParse(val, out var i)) ints[name] = i; break;
                        case "float": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) floats[name] = f; break;
                        case "boolean": bools[name] = val == "true"; break;
                        case "set": sets[name] = e.ChildNodes.Cast<XmlNode>().Select(x => x.InnerText).ToList(); break;
                    }
                }
                if (bools.TryGetValue("night", out var night)) Settings.dark = night;
                if (bools.TryGetValue("passthrough", out var pt)) Settings.passthrough = pt;
                if (bools.TryGetValue("follow", out var fol)) Settings.follow = fol;
                if (ints.TryGetValue("preferredHand", out var ph)) Settings.preferredHand = Mathf.Clamp(ph, 0, 1);
                if (floats.TryGetValue("environmentBrightness", out var eb)) Settings.environmentDim = Mathf.Clamp01(eb);
                if (floats.TryGetValue("distance", out var dist)) Settings.distance = Mathf.Clamp(dist, 0.45f, 4f);
                if (floats.TryGetValue("windowScale", out var sc)) Settings.scale = Mathf.Clamp(sc, 0.45f, 2.5f);
                if (ints.TryGetValue("language", out var lang)) { Settings.language = Mathf.Clamp(lang, 0, 2); Settings.languageSet = true; }
                if (ints.TryGetValue("font", out var font)) Settings.fontSize = Mathf.Clamp(font * 1.45f, 34f, 80f);

                var keys = new HashSet<string>();
                foreach (var k in ints.Keys.Concat(floats.Keys).Concat(sets.Keys))
                {
                    var p = k.LastIndexOf(':');
                    if (p > 0) keys.Add(k.Substring(0, p));
                }
                foreach (var key in keys)
                {
                    var b = Book(key);
                    ints.TryGetValue(key + ":chapter", out var ch);
                    bool fixedLayout = BookFormats.KindOf(key) != BookKind.Text;
                    int value = fixedLayout ? (ints.TryGetValue(key + ":page", out var pg) ? pg : 0) : (ints.TryGetValue(key + ":offset", out var off) ? off : 0);
                    b.position = new BookPosition(ch, value).ToString();
                    if (sets.TryGetValue(key + ":marks", out var marks)) b.marks = marks.Where(m => BookPosition.TryParse(m, out _)).Distinct().ToList();
                    if (floats.TryGetValue(key + ":zoom", out var z)) b.zoom = Mathf.Clamp(z, 1f, 3f);
                }
                Debug.Log("[PFD] Einstellungen und " + keys.Count + " Lesestaende aus der alten App uebernommen.");
                SaveBooks();
            }
            catch (Exception e) { Debug.LogWarning("[PFD] Uebernahme alter Einstellungen fehlgeschlagen: " + e.Message); }
            SaveSettings();
        }
    }
}
