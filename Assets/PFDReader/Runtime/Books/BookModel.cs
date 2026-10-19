using System;
using System.Collections.Generic;

namespace PFDReader.Books
{
    public enum BookKind { Text, Pdf, Comic }

    public sealed class Chapter
    {
        public readonly string Title;
        public readonly string Text;
        public Chapter(string title, string text) { Title = title; Text = text; }
    }

    /// <summary>Umbrechbares Buch (EPUB/TXT): Kapitel mit reinem Text.</summary>
    public sealed class TextBook
    {
        public readonly string Title;
        public readonly string Author;
        public readonly List<Chapter> Chapters;
        public TextBook(string title, string author, List<Chapter> chapters) { Title = title; Author = author; Chapters = chapters; }
    }

    /// <summary>Lesestelle: Kapitel + Zeichen-Offset (Text) bzw. 0 + Seite (PDF/Comic). Format wie in der alten App: "kapitel:wert".</summary>
    [Serializable]
    public struct BookPosition : IEquatable<BookPosition>
    {
        public int chapter;
        public int value;
        public BookPosition(int chapter, int value) { this.chapter = chapter; this.value = value; }
        public override string ToString() => chapter + ":" + value;
        public static bool TryParse(string s, out BookPosition p)
        {
            p = default;
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var c) || !int.TryParse(parts[1], out var v)) return false;
            p = new BookPosition(c, v);
            return true;
        }
        public bool Equals(BookPosition o) => chapter == o.chapter && value == o.value;
        public override bool Equals(object obj) => obj is BookPosition o && Equals(o);
        public override int GetHashCode() => chapter * 397 ^ value;
    }

    public sealed class BookException : Exception
    {
        public BookException(string msg) : base(msg) { }
        public BookException(string msg, Exception inner) : base(msg, inner) { }
    }

    public static class BookFormats
    {
        public static readonly string[] Extensions =
        {
            ".epub", ".txt", ".fb2", ".fbz", ".html", ".htm", ".xhtml", ".md", ".markdown",   // Text
            ".pdf",
            ".cbz", ".zip", ".cbr", ".cb7", ".cbt"                                                 // Comics
        };
        static readonly string[] ComicExt = { ".cbz", ".zip", ".cbr", ".cb7", ".cbt" };

        public static bool IsSupported(string path)
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(Extensions, ext) >= 0;
        }

        /// <summary>"buch.fb2.zip" ist ein gepacktes FictionBook (Text), kein Comic.</summary>
        public static bool IsZippedFb2(string path) => path.EndsWith(".fb2.zip", StringComparison.OrdinalIgnoreCase);

        public static BookKind KindOf(string path)
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".pdf") return BookKind.Pdf;
            if (IsZippedFb2(path)) return BookKind.Text;
            if (Array.IndexOf(ComicExt, ext) >= 0) return BookKind.Comic;
            return BookKind.Text;
        }
    }
}
