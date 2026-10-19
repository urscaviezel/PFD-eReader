using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PFDReader.Books
{
    /// <summary>
    /// Oeffnet Comic-Archive nach Inhalt (nicht nach Endung – viele .cbr sind in Wahrheit ZIPs und umgekehrt):
    /// ZIP (CBZ), TAR (CBT) direkt; RAR (CBR) und 7z (CB7) ueber SharpCompress.
    /// </summary>
    public static class ComicArchives
    {
        public enum Format { Unknown, Zip, Rar, SevenZip, Tar }

        public static Format Sniff(string path)
        {
            var head = new byte[512];
            int n;
            using (var f = File.OpenRead(path)) n = f.Read(head, 0, head.Length);
            if (n >= 4 && head[0] == 'P' && head[1] == 'K' && (head[2] == 3 || head[2] == 5) && (head[3] == 4 || head[3] == 6)) return Format.Zip;
            if (n >= 6 && head[0] == 'R' && head[1] == 'a' && head[2] == 'r' && head[3] == '!' && head[4] == 0x1A && head[5] == 0x07) return Format.Rar;
            if (n >= 6 && head[0] == '7' && head[1] == 'z' && head[2] == 0xBC && head[3] == 0xAF && head[4] == 0x27 && head[5] == 0x1C) return Format.SevenZip;
            if (n >= 262 && Encoding.ASCII.GetString(head, 257, 5) == "ustar") return Format.Tar;
            // altes V7-TAR ohne "ustar": Pruefsumme im Kopf stimmt
            if (n >= 512 && TarStore.ChecksumOk(head)) return Format.Tar;
            return Format.Unknown;
        }

        public static IPageStore Open(string path)
        {
            Format fmt;
            try { fmt = Sniff(path); }
            catch (Exception e) { throw new BookException("Archiv konnte nicht gelesen werden.", e); }
            try
            {
                switch (fmt)
                {
                    case Format.Zip: return new ZipStore(path);
                    case Format.Tar: return new TarStore(path);
                    case Format.Rar:
                    case Format.SevenZip: return ExtractedStore.Open(path);
                    default: throw new BookException("Unbekanntes Archivformat.");
                }
            }
            catch (BookException) { throw; }
            catch (Exception e) { throw new BookException("Archiv konnte nicht geoeffnet werden.", e); }
        }
    }

    // ---------------------------------------------------------------- ZIP
    sealed class ZipStore : IPageStore
    {
        readonly ZipArchive m_Zip;
        readonly Dictionary<string, ZipArchiveEntry> m_Map = new Dictionary<string, ZipArchiveEntry>();
        public IList<string> Names { get; }

        public ZipStore(string path)
        {
            m_Zip = ZipFile.OpenRead(path);
            foreach (var e in m_Zip.Entries) m_Map[e.FullName] = e;
            Names = m_Map.Keys.ToList();
        }

        public long SizeOf(string name) => m_Map[name].Length;

        public byte[] Read(string name)
        {
            var e = m_Map[name];
            using (var s = e.Open())
            using (var ms = new MemoryStream((int)Math.Max(0, Math.Min(e.Length, int.MaxValue))))
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public void Dispose() => m_Zip.Dispose();
    }

    // ---------------------------------------------------------------- TAR (ustar, GNU-Langnamen, PAX-Pfade)
    sealed class TarStore : IPageStore
    {
        readonly FileStream m_File;
        readonly Dictionary<string, (long offset, long size)> m_Map = new Dictionary<string, (long, long)>();
        public IList<string> Names { get; }

        public static bool ChecksumOk(byte[] h)
        {
            long stored = Octal(h, 148, 8);
            if (stored <= 0) return false;
            long sum = 0;
            for (int i = 0; i < 512; i++) sum += (i >= 148 && i < 156) ? 32 : h[i];
            return sum == stored;
        }

        static long Octal(byte[] b, int off, int len)
        {
            long v = 0; bool any = false;
            for (int i = off; i < off + len; i++)
            {
                byte c = b[i];
                if (c == 0 || c == ' ') { if (any) break; else continue; }
                if (c < '0' || c > '7') return -1;
                v = v * 8 + (c - '0'); any = true;
            }
            return any ? v : 0;
        }

        static string Str(byte[] b, int off, int len)
        {
            int end = off; while (end < off + len && b[end] != 0) end++;
            return Encoding.UTF8.GetString(b, off, end - off);
        }

        public TarStore(string path)
        {
            m_File = File.OpenRead(path);
            var h = new byte[512];
            string longName = null;
            long pos = 0;
            while (pos + 512 <= m_File.Length && m_Map.Count <= ComicPageSource.MaxEntries)
            {
                m_File.Position = pos;
                if (m_File.Read(h, 0, 512) < 512) break;
                if (h.All(x => x == 0)) break;                          // Archivende
                if (!ChecksumOk(h)) throw new BookException("TAR-Archiv ist beschaedigt.");
                long size = Octal(h, 124, 12);
                if (size < 0) throw new BookException("TAR-Archiv ist beschaedigt.");
                char type = (char)h[156];
                long data = pos + 512;
                if (type == 'L' || type == 'x')
                {
                    var buf = new byte[(int)Math.Min(size, 65536)];
                    m_File.Position = data; m_File.Read(buf, 0, buf.Length);
                    if (type == 'L') longName = Str(buf, 0, buf.Length);
                    else
                    {
                        // PAX: "len path=wert\n"
                        foreach (var line in Encoding.UTF8.GetString(buf).Split('\n'))
                        {
                            int sp = line.IndexOf(' '); if (sp < 0) continue;
                            var kv = line.Substring(sp + 1);
                            if (kv.StartsWith("path=")) longName = kv.Substring(5);
                        }
                    }
                }
                else
                {
                    string name = longName ?? (Str(h, 345, 155) is var prefix && prefix.Length > 0 ? prefix + "/" + Str(h, 0, 100) : Str(h, 0, 100));
                    longName = null;
                    if (type == '0' || type == '\0' || type == '7') m_Map[name] = (data, size);
                }
                pos = data + ((size + 511) / 512) * 512;
            }
            Names = m_Map.Keys.ToList();
        }

        public long SizeOf(string name) => m_Map[name].size;

        public byte[] Read(string name)
        {
            var (off, size) = m_Map[name];
            var buf = new byte[size];
            m_File.Position = off;
            int got = 0;
            while (got < size) { int r = m_File.Read(buf, got, (int)(size - got)); if (r <= 0) break; got += r; }
            return buf;
        }

        public void Dispose() => m_File.Dispose();
    }
}
