using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PFDReader.Books;
using UnityEngine;

namespace PFDReader.Core
{
    public sealed class LibraryEntry
    {
        public string path;
        public string key;
        public string title;
        public BookKind kind;
        public long size;
        public bool legacy;
        public DateTime modified;
    }

    /// <summary>Buecherliste aus dem gemeinsamen Ordner eBooks und dem privaten Buecherordner der alten App.</summary>
    public static class Library
    {
        static readonly string[] HiddenGuides = { "willkommen.txt", "welcome.txt", "bienvenue.txt" };

        public static bool HasStorageAccess
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try { using (var b = new AndroidJavaClass("org.pfd.reader.unity.PfdBridge")) return b.CallStatic<bool>("hasAllFilesAccess"); }
                catch { return false; }
#else
                return true;
#endif
            }
        }

        public static void RequestStorageAccess()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var b = new AndroidJavaClass("org.pfd.reader.unity.PfdBridge"))
                    b.CallStatic("requestAllFilesAccess", act);
            }
            catch (Exception e) { Debug.LogWarning("[PFD] Berechtigungsdialog: " + e.Message); }
#endif
        }

        public static List<LibraryEntry> Scan(out string message)
        {
            message = null;
            var list = new List<LibraryEntry>();
            var shared = Storage.SharedBooksDir;
            if (HasStorageAccess)
            {
                try { Directory.CreateDirectory(shared); Collect(shared, false, list); }
                catch (Exception e) { message = "Ordner eBooks nicht lesbar: " + e.Message; }
            }
            else message = "Zugriff auf den Ordner eBooks ist noch nicht freigegeben.";
            try { if (Directory.Exists(Storage.LegacyBooksDir)) Collect(Storage.LegacyBooksDir, true, list); }
            catch (Exception e) { Debug.LogWarning("[PFD] Private Buecher: " + e.Message); }
            return list
                .GroupBy(e => e.key).Select(g => g.OrderBy(x => x.legacy).First())
                .OrderBy(e => e.title, NaturalComparer.Instance)
                .ToList();
        }

        static void Collect(string dir, bool legacy, List<LibraryEntry> list)
        {
            foreach (var f in Directory.GetFiles(dir))
            {
                if (!BookFormats.IsSupported(f)) continue;
                var name = Path.GetFileName(f);
                if (HiddenGuides.Contains(name.ToLowerInvariant()) && IsAppGuide(f)) continue;
                var fi = new FileInfo(f);
                list.Add(new LibraryEntry
                {
                    path = f, key = name, title = Path.GetFileNameWithoutExtension(name), kind = BookFormats.KindOf(f),
                    size = fi.Length, legacy = legacy, modified = fi.LastWriteTime
                });
            }
        }

        static bool IsAppGuide(string path)
        {
            try
            {
                using (var r = new StreamReader(path))
                {
                    var first = r.ReadLine() ?? "";
                    return first.Contains("PFD");
                }
            }
            catch { return false; }
        }

        public static string FormatLabel(BookKind k, string path)
        {
            var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            return ext;
        }

        public static string SizeLabel(long bytes)
        {
            if (bytes >= 1024 * 1024) return (bytes / 1048576f).ToString("0.0") + " MB";
            return Math.Max(1, bytes / 1024) + " KB";
        }
    }
}
