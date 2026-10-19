using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PFDReader.Books
{
    /// <summary>Ergebnis einer Seitenrasterung. Entweder Rohpixel (RGBA, Zeile 0 = unten) oder kodierte Bilddaten (nur Editor).</summary>
    public sealed class PageImage
    {
        public int width, height;
        public byte[] rgba;
        public byte[] encoded;
    }

    /// <summary>Buch mit festen Seiten (PDF, Comic). Render() wird auf dem Seiten-Worker-Thread aufgerufen.</summary>
    public interface IFixedPageSource : IDisposable
    {
        int PageCount { get; }
        string Title { get; }
        /// <summary>Breite/Hoehe der Seite (fuer Layout); darf grob sein.</summary>
        float Aspect(int index);
        PageImage Render(int index, int maxW, int maxH, float zoom, Vector2 pan);
    }

    // ==================================================================== PDF
    public sealed class PdfPageSource : IFixedPageSource
    {
        readonly AndroidJavaObject m_Pdf;
        readonly int m_Count;
        readonly Dictionary<int, float> m_Aspects = new Dictionary<int, float>();
        public string Title { get; }
        public int PageCount => m_Count;

        public PdfPageSource(string path)
        {
            Title = Path.GetFileNameWithoutExtension(path);
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                m_Pdf = new AndroidJavaObject("org.pfd.reader.unity.PfdBridge$Pdf", path);
                m_Count = m_Pdf.Call<int>("pageCount");
            }
            catch (Exception e) { throw new BookException("PDF konnte nicht geoeffnet werden: " + e.Message, e); }
            if (m_Count <= 0) throw new BookException("PDF enthaelt keine Seiten.");
#else
            throw new BookException("PDF-Anzeige ist nur auf dem Headset verfuegbar (Android PdfRenderer).");
#endif
        }

        public float Aspect(int index)
        {
            lock (m_Aspects)
            {
                if (m_Aspects.TryGetValue(index, out var a)) return a;
                    a = m_Pdf.Call<float>("aspect", index);
                m_Aspects[index] = a;
                return a;
            }
        }

        public PageImage Render(int index, int maxW, int maxH, float zoom, Vector2 pan)
        {
            {
                var aspect = Aspect(index);
                int w = maxW, h = Mathf.RoundToInt(maxW / aspect);
                if (h > maxH) { h = maxH; w = Mathf.RoundToInt(maxH * aspect); }
                var data = m_Pdf.Call<sbyte[]>("render", index, w, h, zoom, pan.x, pan.y);
                var bytes = new byte[data.Length];
                Buffer.BlockCopy(data, 0, bytes, 0, data.Length);
                return new PageImage { width = w, height = h, rgba = bytes };
            }
        }

        public void Dispose()
        {
            if (m_Pdf == null) return;
            try { m_Pdf.Call("close"); } catch { }
            m_Pdf.Dispose();
        }
    }

    // ==================================================================== Comics (CBZ/ZIP, CBT, CBR, CB7)
    /// <summary>Bildseiten eines Comic-Archivs: Namen in Lesereihenfolge + Lesen einzelner Seiten.</summary>
    public interface IPageStore : IDisposable
    {
        IList<string> Names { get; }
        long SizeOf(string name);
        byte[] Read(string name);
    }

    public sealed class ComicPageSource : IFixedPageSource
    {
        public const int MaxEntries = 20000, MaxPages = 5000;
        public const long MaxImageBytes = 64L * 1024 * 1024;
        static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };

        readonly IPageStore m_Store;
        readonly List<string> m_Pages;
        readonly object m_Lock = new object();
        public string Title { get; }
        /// <summary>Echte Seitenzahl. Ungerade Anzahl: die App ergaenzt eine leere Schlussseite (wie bisher).</summary>
        public int PageCount => m_Pages.Count;

        public ComicPageSource(string path)
        {
            Title = Path.GetFileNameWithoutExtension(path);
            m_Store = ComicArchives.Open(path);
            if (m_Store.Names.Count > MaxEntries) { m_Store.Dispose(); throw new BookException("Archiv enthaelt zu viele Eintraege."); }
            m_Pages = m_Store.Names
                .Where(n => !n.EndsWith("/") && IsSafe(n) && IsImage(n))
                .OrderBy(n => n, NaturalComparer.Instance)
                .Take(MaxPages)
                .ToList();
            if (m_Pages.Count == 0) { m_Store.Dispose(); throw new BookException("Archiv enthaelt keine unterstuetzten Bildseiten."); }
        }

        public static bool IsImage(string n) => ImageExt.Contains(Path.GetExtension(n).ToLowerInvariant());

        public static bool IsSafe(string n)
        {
            if (n.StartsWith("/") || n.IndexOf('\\') >= 0 || n.IndexOf(':') >= 0 || n.Contains("..")) return false;
            foreach (var part in n.Split('/'))
                if (part.StartsWith(".") || part.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public float Aspect(int index) => 0.75f;

        public PageImage Render(int index, int maxW, int maxH, float zoom, Vector2 pan)
        {
            byte[] data;
            lock (m_Lock)
            {
                var n = m_Pages[index];
                if (m_Store.SizeOf(n) > MaxImageBytes) throw new BookException("Bildseite ist zu gross.");
                data = m_Store.Read(n);
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            {
                int dw = zoom > 1f ? Mathf.RoundToInt(maxW * zoom) : maxW, dh = zoom > 1f ? Mathf.RoundToInt(maxH * zoom) : maxH;
                using (var bridge = new AndroidJavaClass("org.pfd.reader.unity.PfdBridge"))
                {
                    var raw = bridge.CallStatic<sbyte[]>("decodeImage", data, dw, dh);
                    if (raw == null || raw.Length < 8) throw new BookException("Bildseite konnte nicht dekodiert werden.");
                    int w = ((byte)raw[0] << 24) | ((byte)raw[1] << 16) | ((byte)raw[2] << 8) | (byte)raw[3];
                    int h = ((byte)raw[4] << 24) | ((byte)raw[5] << 16) | ((byte)raw[6] << 8) | (byte)raw[7];
                    var px = new byte[raw.Length - 8];
                    Buffer.BlockCopy(raw, 8, px, 0, px.Length);
                    return new PageImage { width = w, height = h, rgba = px };
                }
            }
#else
            return new PageImage { encoded = data };
#endif
        }

        public void Dispose() { lock (m_Lock) m_Store.Dispose(); }
    }

    /// <summary>Natuerliche Sortierung: "2" vor "10".</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new NaturalComparer();
        static readonly Regex Parts = new Regex(@"(\d+)|(\D+)");
        public int Compare(string a, string b)
        {
            var ma = Parts.Matches(a.ToLowerInvariant()); var mb = Parts.Matches(b.ToLowerInvariant());
            for (int i = 0; i < Math.Min(ma.Count, mb.Count); i++)
            {
                var x = ma[i].Value; var y = mb[i].Value;
                int c;
                if (char.IsDigit(x[0]) && char.IsDigit(y[0]))
                {
                    var xt = x.TrimStart('0'); var yt = y.TrimStart('0');
                    c = xt.Length != yt.Length ? xt.Length.CompareTo(yt.Length) : string.CompareOrdinal(xt, yt);
                }
                else c = string.CompareOrdinal(x, y);
                if (c != 0) return c;
            }
            return ma.Count.CompareTo(mb.Count);
        }
    }

    public static class MainThread
    {
        static int s_Id = -1;
        public static void Capture() => s_Id = System.Threading.Thread.CurrentThread.ManagedThreadId;
        public static bool IsMain => System.Threading.Thread.CurrentThread.ManagedThreadId == s_Id;
    }
}
