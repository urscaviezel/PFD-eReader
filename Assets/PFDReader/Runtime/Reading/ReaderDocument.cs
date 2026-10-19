using System;
using PFDReader.Core;
using System.Collections.Generic;
using System.IO;
using PFDReader.Books;
using PFDReader.Rendering;
using TMPro;
using UnityEngine;

namespace PFDReader.Reading
{
    /// <summary>Adresse einer Seite: Text = (Kapitel, Seite im Kapitel), PDF/Comic = (0, Seite).</summary>
    public struct PageRef : IEquatable<PageRef>
    {
        public int chapter, page;
        public PageRef(int c, int p) { chapter = c; page = p; }
        public bool Equals(PageRef o) => chapter == o.chapter && page == o.page;
        public override bool Equals(object obj) => obj is PageRef o && Equals(o);
        public override int GetHashCode() => chapter * 100003 + page;
        public override string ToString() => chapter + "/" + page;
    }

    public struct TocEntry { public string title; public BookPosition position; }

    /// <summary>Ein geoeffnetes Buch beliebigen Formats mit einheitlicher Seitennavigation.</summary>
    public sealed class ReaderDocument : IDisposable
    {
        public readonly string FilePath;
        public readonly string Key;          // wie alte App: Dateiname
        public readonly BookKind Kind;
        public readonly string Title;
        public readonly string Author;
        public readonly TextLayout Text;      // nur Text
        public readonly IFixedPageSource Fixed; // nur PDF/Comic

        ReaderDocument(string path, BookKind kind, string title, string author, TextLayout text, IFixedPageSource fixedSrc)
        {
            FilePath = path; Key = Path.GetFileName(path); Kind = kind; Title = title; Author = author; Text = text; Fixed = fixedSrc;
        }

        /// <summary>Laedt ein Buch. Text-Parsing ist threadsicher; TextLayout muss auf dem Hauptthread erzeugt werden (siehe Open).</summary>
        public static object Parse(string path)
        {
            switch (BookFormats.KindOf(path))
            {
                case BookKind.Text: return TextBookLoader.Load(path);
                case BookKind.Comic: return new ComicPageSource(path);
                default: return null; // PDF auf dem Hauptthread oeffnen (JNI-Objekt)
            }
        }

        public static ReaderDocument Open(string path, object parsed, TMP_FontAsset font, TextStyle style)
        {
            var kind = BookFormats.KindOf(path);
            switch (kind)
            {
                case BookKind.Text:
                {
                    var book = (TextBook)parsed;
                    return new ReaderDocument(path, kind, book.Title, book.Author, new TextLayout(book, font, style), null);
                }
                case BookKind.Comic:
                {
                    var src = (IFixedPageSource)parsed;
                    return new ReaderDocument(path, kind, src.Title, "", null, src);
                }
                default:
                {
                    var src = new PdfPageSource(path);
                    return new ReaderDocument(path, kind, src.Title, "", null, src);
                }
            }
        }

        public bool IsText => Text != null;

        // ---------------------------------------------------------------- Navigation
        public bool IsValid(PageRef r)
        {
            if (IsText) return r.chapter >= 0 && r.chapter < Text.Book.Chapters.Count && r.page >= 0 && r.page < Text.PagesOf(r.chapter).Count;
            return r.chapter == 0 && r.page >= 0 && r.page < Fixed.PageCount;
        }

        public bool TryNext(PageRef r, out PageRef n)
        {
            n = r;
            if (IsText)
            {
                if (r.page + 1 < Text.PagesOf(r.chapter).Count) { n = new PageRef(r.chapter, r.page + 1); return true; }
                if (r.chapter + 1 < Text.Book.Chapters.Count) { n = new PageRef(r.chapter + 1, 0); return true; }
                return false;
            }
            if (r.page + 1 < Fixed.PageCount) { n = new PageRef(0, r.page + 1); return true; }
            return false;
        }

        public bool TryPrev(PageRef r, out PageRef n)
        {
            n = r;
            if (r.page > 0) { n = new PageRef(r.chapter, r.page - 1); return true; }
            if (IsText && r.chapter > 0) { n = new PageRef(r.chapter - 1, Text.PagesOf(r.chapter - 1).Count - 1); return true; }
            return false;
        }

        public PageRef First => new PageRef(0, 0);

        public PageRef FromPosition(BookPosition p)
        {
            if (IsText)
            {
                int c = Mathf.Clamp(p.chapter, 0, Text.Book.Chapters.Count - 1);
                return new PageRef(c, Text.PageIndexForOffset(c, Mathf.Max(0, p.value)));
            }
            return new PageRef(0, Mathf.Clamp(p.value, 0, Fixed.PageCount - 1));
        }

        public BookPosition ToPosition(PageRef r)
        {
            if (IsText) return new BookPosition(r.chapter, Text.PagesOf(r.chapter)[r.page].start);
            return new BookPosition(0, r.page);
        }

        public List<TocEntry> Toc()
        {
            var list = new List<TocEntry>();
            if (IsText)
                for (int i = 0; i < Text.Book.Chapters.Count; i++)
                    list.Add(new TocEntry { title = Text.Book.Chapters[i].Title, position = new BookPosition(i, 0) });
            else
                for (int i = 0; i < Fixed.PageCount; i++)
                    list.Add(new TocEntry { title = Loc.T("Seite") + " " + (i + 1), position = new BookPosition(0, i) });
            return list;
        }

        /// <summary>Fortschritt 0..1 (bei Text: Kapitelanteil plus Seite im Kapitel).</summary>
        public float Progress(PageRef r)
        {
            if (IsText)
            {
                int n = Text.Book.Chapters.Count;
                int pages = Mathf.Max(1, Text.PagesOf(r.chapter).Count);
                return Mathf.Clamp01((r.chapter + (r.page + 1f) / pages) / n);
            }
            return Fixed.PageCount <= 1 ? 1f : r.page / (float)(Fixed.PageCount - 1);
        }

        public string Label(PageRef r)
        {
            if (IsText) return (r.page + 1) + " / " + Text.PagesOf(r.chapter).Count;
            return (r.page + 1) + " / " + Fixed.PageCount;
        }

        public string LocationText(PageRef r)
        {
            if (IsText) return Text.Book.Chapters[r.chapter].Title + "  ·  " + Mathf.RoundToInt(Progress(r) * 100) + " %";
            return Loc.T("Seite") + " " + (r.page + 1) + " " + Loc.T("von") + " " + Fixed.PageCount;
        }

        public void Dispose()
        {
            Text?.Dispose();
            Fixed?.Dispose();
        }
    }
}
