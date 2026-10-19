using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace PFDReader.Books
{
    /// <summary>
    /// RAR (CBR) und 7z (CB7) ueber SharpCompress (MIT). Solche Archive sind oft "solid" – einzelne Seiten liessen sich
    /// nur durch Entpacken ab Archivbeginn lesen. Darum werden die Bildseiten beim Oeffnen einmal nacheinander in einen
    /// Cache-Ordner entpackt und danach von dort gelesen. Die letzten paar Archive bleiben im Cache.
    /// </summary>
    sealed class ExtractedStore : IPageStore
    {
        /// <summary>Wird von der App auf dem Hauptthread gesetzt (Application.temporaryCachePath).</summary>
        public static string CacheRoot;
        const long MaxTotal = 2L * 1024 * 1024 * 1024;
        static volatile float s_Progress = -1f;
        /// <summary>Fortschritt des laufenden Entpackens 0..1, sonst -1.</summary>
        public static float Progress => s_Progress;
        const int KeepArchives = 3;

        readonly string m_Dir;
        readonly Dictionary<string, string> m_Map = new Dictionary<string, string>();   // Name im Archiv -> Datei im Cache
        public IList<string> Names { get; }

        ExtractedStore(string dir, Dictionary<string, string> map) { m_Dir = dir; m_Map = map; Names = map.Keys.ToList(); }

        public static IPageStore Open(string path)
        {
            var root = Path.Combine(string.IsNullOrEmpty(CacheRoot) ? Path.GetTempPath() : CacheRoot, "comics");
            var fi = new FileInfo(path);
            var key = Hash(fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks);
            var dir = Path.Combine(root, key);
            var index = Path.Combine(dir, "index.txt");
            if (File.Exists(index))
            {
                var map = Load(dir, index);
                if (map != null) { Touch(dir); return new ExtractedStore(dir, map); }
            }
            Directory.CreateDirectory(root);
            Prune(root);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            try
            {
                var map = Extract(path, dir);
                File.WriteAllLines(index, map.Select(kv => Path.GetFileName(kv.Value) + "\t" + kv.Key));
                return new ExtractedStore(dir, map);
            }
            catch
            {
                try { Directory.Delete(dir, true); } catch { }
                throw;
            }
        }

        static Dictionary<string, string> Extract(string path, string dir)
        {
            var map = new Dictionary<string, string>();
            long total = 0, all = 0; int n = 0;
            s_Progress = 0f;
            try
            {
            using (var archive = ArchiveFactory.Open(path))
            {
                if (archive.Entries.Any(Encrypted)) throw new BookException("Archiv ist verschluesselt.");
                foreach (var en in archive.Entries) if (!en.IsDirectory) all += Math.Max(0, en.Size);
                using (var reader = archive.ExtractAllEntries())
                {
                    while (reader.MoveToNextEntry())
                    {
                        var e = reader.Entry;
                        if (e.IsDirectory) continue;
                        var name = (e.Key ?? "").Replace('\\', '/');
                        if (!ComicPageSource.IsSafe(name) || !ComicPageSource.IsImage(name)) continue;
                        if (e.Size > ComicPageSource.MaxImageBytes) continue;
                        total += e.Size;
                        if (total > MaxTotal) throw new BookException("Archiv ist zu gross (entpackt max. 2 GiB).");
                        if (++n > ComicPageSource.MaxPages) break;
                        var file = Path.Combine(dir, n.ToString("D5") + Path.GetExtension(name).ToLowerInvariant());
                        using (var fs = File.Create(file)) reader.WriteEntryTo(fs);
                        map[name] = file;
                        if (all > 0) s_Progress = Math.Min(1f, total / (float)all);
                    }
                }
            }
            }
            finally { s_Progress = -1f; }
            return map;
        }

        /// <summary>SharpCompress wirft bei 7z-Ordnereintraegen in IsEncrypted eine NullReference – darum abgesichert.</summary>
        static bool Encrypted(IArchiveEntry e)
        {
            if (e.IsDirectory) return false;
            try { return e.IsEncrypted; } catch { return false; }
        }

        static Dictionary<string, string> Load(string dir, string index)
        {
            try
            {
                var map = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(index))
                {
                    int tab = line.IndexOf('\t'); if (tab <= 0) continue;
                    var file = Path.Combine(dir, line.Substring(0, tab));
                    if (!File.Exists(file)) return null;
                    map[line.Substring(tab + 1)] = file;
                }
                return map;
            }
            catch { return null; }
        }

        static void Touch(string dir) { try { Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow); } catch { } }

        static void Prune(string root)
        {
            try
            {
                var dirs = new DirectoryInfo(root).GetDirectories().OrderByDescending(d => d.LastWriteTimeUtc).ToList();
                foreach (var d in dirs.Skip(KeepArchives - 1)) { try { d.Delete(true); } catch { } }
            }
            catch { }
        }

        static string Hash(string s)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s))).Replace("-", "").Substring(0, 16);
        }

        public long SizeOf(string name) => new FileInfo(m_Map[name]).Length;
        public byte[] Read(string name) => File.ReadAllBytes(m_Map[name]);
        public void Dispose() { }
    }
}
