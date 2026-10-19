using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace PFDReader.Books
{
    /// <summary>
    /// EPUB/TXT-Import ohne Fremdbibliotheken (Port des Java-BookLoader der alten App):
    /// keine Netzwerkzugriffe, keine DTDs/Entities, keine DRM-Umgehung, Groessengrenzen.
    /// </summary>
    public static class TextBookLoader
    {
        const int MaxEntry = 8 * 1024 * 1024;
        const long MaxTotal = 64L * 1024 * 1024;
        const int MaxEntries = 4096;

        public static TextBook Load(string path)
        {
            var name = Path.GetFileName(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".txt")
            {
                var fi = new FileInfo(path);
                if (fi.Length > MaxEntry) throw new BookException("Textdatei ist zu gross (max. 8 MiB).");
                var text = Decode(File.ReadAllBytes(path)).Replace("\r\n", "\n").Replace('\r', '\n');
                var stem = Path.GetFileNameWithoutExtension(name);
                return new TextBook(stem, "", new List<Chapter> { new Chapter(stem, text) });
            }
            var stem2 = Path.GetFileNameWithoutExtension(name);
            if (BookFormats.IsZippedFb2(path)) stem2 = Path.GetFileNameWithoutExtension(stem2);
            if (ext == ".fb2") return LoadFb2(ReadLimited(path), stem2);
            if (ext == ".fbz" || BookFormats.IsZippedFb2(path)) return LoadFb2(ReadZippedFb2(path), stem2);
            if (ext == ".html" || ext == ".htm" || ext == ".xhtml") return LoadHtml(DecodeDeclared(ReadLimited(path)), stem2);
            if (ext == ".md" || ext == ".markdown") return LoadMarkdown(Decode(ReadLimited(path)), stem2);
            if (ext != ".epub") throw new BookException("Dieses Textformat wird nicht unterstuetzt.");
            try
            {
                using (var zip = ZipFile.OpenRead(path)) return LoadEpub(zip, Path.GetFileNameWithoutExtension(name));
            }
            catch (BookException) { throw; }
            catch (InvalidDataException e) { throw new BookException("Ungueltiges EPUB-Archiv.", e); }
        }

        static TextBook LoadEpub(ZipArchive zip, string fallbackTitle)
        {
            var archive = new Archive(zip);
            if (archive.Has("META-INF/encryption.xml"))
            {
                var enc = Xml(archive.Get("META-INF/encryption.xml"));
                if (enc.GetElementsByTagName("EncryptedData", "*").Count > 0 || ByLocal(enc, "EncryptedData").Any())
                    throw new BookException("Verschluesselte EPUB-Ressourcen / DRM werden nicht unterstuetzt.");
            }
            var container = Xml(archive.Get("META-INF/container.xml"));
            var root = ByLocal(container, "rootfile").FirstOrDefault();
            if (root == null) throw new BookException("EPUB enthaelt keine Paketdatei.");
            var opf = ResolvePath("", root.GetAttribute("full-path"));
            var doc = Xml(archive.Get(opf));
            var title = FirstText(doc, "title", fallbackTitle);
            var author = FirstText(doc, "creator", "");

            var items = new Dictionary<string, string>();
            foreach (var el in ByLocal(doc, "item"))
            {
                var type = el.GetAttribute("media-type");
                if (type == "application/xhtml+xml" || type == "text/html")
                    items[el.GetAttribute("id")] = ResolvePath(opf, el.GetAttribute("href"));
            }

            var chapters = new List<Chapter>();
            var headingRx = new Regex(@"<h[1-6]\b[^>]*>(.*?)</h[1-6]\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (var itemref in ByLocal(doc, "itemref"))
            {
                if (itemref.GetAttribute("linear") == "no") continue;
                if (!items.TryGetValue(itemref.GetAttribute("idref"), out var target))
                    throw new BookException("EPUB-Lesereihenfolge verweist auf ein nicht unterstuetztes Kapitel.");
                var html = Decode(archive.Get(target));
                var m = headingRx.Match(html);
                var chapterTitle = m.Success ? Plain(m.Groups[1].Value) : "Kapitel " + (chapters.Count + 1);
                if (string.IsNullOrWhiteSpace(chapterTitle)) chapterTitle = "Kapitel " + (chapters.Count + 1);
                var text = Plain(html);
                if (text.Length > 0) chapters.Add(new Chapter(chapterTitle, text));
            }
            if (chapters.Count == 0) throw new BookException("EPUB enthaelt keine lesbaren Textkapitel.");
            return new TextBook(title, author, chapters);
        }

        // ---------------------------------------------------------------- Dateien mit Grenzen
        static byte[] ReadLimited(string path)
        {
            var fi = new FileInfo(path);
            if (fi.Length > MaxEntry) throw new BookException("Datei ist zu gross (max. 8 MiB).");
            return File.ReadAllBytes(path);
        }

        static byte[] ReadZippedFb2(string path)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(path))
                {
                    var e = zip.Entries.FirstOrDefault(x => x.FullName.EndsWith(".fb2", StringComparison.OrdinalIgnoreCase));
                    if (e == null) throw new BookException("Archiv enthaelt keine FB2-Datei.");
                    if (e.Length > MaxEntry) throw new BookException("FB2-Datei ist zu gross (max. 8 MiB).");
                    using (var st = e.Open()) using (var ms = new MemoryStream()) { st.CopyTo(ms); return ms.ToArray(); }
                }
            }
            catch (InvalidDataException ex) { throw new BookException("Ungueltiges FB2-Archiv.", ex); }
        }

        // ---------------------------------------------------------------- FB2 (FictionBook)
        static TextBook LoadFb2(byte[] data, string fallbackTitle)
        {
            var doc = Xml(data);
            var titleInfo = ByLocal(doc, "title-info").FirstOrDefault();
            string title = fallbackTitle, author = "";
            if (titleInfo != null)
            {
                var bt = Child(titleInfo, "book-title");
                if (bt != null && bt.InnerText.Trim().Length > 0) title = bt.InnerText.Trim();
                var au = Child(titleInfo, "author");
                if (au != null)
                    author = string.Join(" ", new[] { "first-name", "middle-name", "last-name" }.Select(k => Child(au, k)?.InnerText.Trim()).Where(x => !string.IsNullOrEmpty(x)));
                if (author.Length == 0 && au != null) author = Child(au, "nickname")?.InnerText.Trim() ?? "";
            }
            var body = ByLocal(doc, "body").FirstOrDefault(b => b.GetAttribute("name") != "notes" && b.GetAttribute("name") != "comments")
                       ?? ByLocal(doc, "body").FirstOrDefault();
            if (body == null) throw new BookException("FB2 enthaelt keinen Text.");

            var chapters = new List<Chapter>();
            var intro = new StringBuilder();
            foreach (XmlNode n in body.ChildNodes)
            {
                if (n is XmlElement e && e.LocalName == "section") CollectFb2(e, chapters);
                else if (n is XmlElement other) Fb2Text(other, intro);
            }
            var introText = Clean(intro.ToString());
            if (introText.Length > 0 && introText != title) chapters.Insert(0, new Chapter(title, introText));   // nur Titelseite: weglassen
            if (chapters.Count == 0) throw new BookException("FB2 enthaelt keine lesbaren Kapitel.");
            return new TextBook(title, author, chapters);
        }

        /// <summary>Abschnitt mit eigenem Text = Kapitel; reine Sammelabschnitte (nur Unterabschnitte) werden aufgeloest.</summary>
        static void CollectFb2(XmlElement section, List<Chapter> chapters)
        {
            bool hasOwnText = section.ChildNodes.OfType<XmlElement>().Any(c => c.LocalName != "section" && c.LocalName != "title" && c.LocalName != "epigraph" && c.LocalName != "image");
            var subs = section.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == "section").ToList();
            if (!hasOwnText && subs.Count > 0 && chapters.Count < 2000)
            {
                foreach (var sub in subs) CollectFb2(sub, chapters);
                return;
            }
            var sb = new StringBuilder();
            Fb2Text(section, sb);
            var text = Clean(sb.ToString());
            if (text.Length == 0) return;
            var t = Child(section, "title");
            var chapterTitle = t != null ? Clean(string.Join(" ", t.ChildNodes.OfType<XmlElement>().Select(x => x.InnerText.Trim()))) : "";
            if (chapterTitle.Length == 0) chapterTitle = "Kapitel " + (chapters.Count + 1);
            chapters.Add(new Chapter(chapterTitle, text));
        }

        static void Fb2Text(XmlElement e, StringBuilder sb)
        {
            switch (e.LocalName)
            {
                case "binary": case "image": case "description": return;
                case "p": case "subtitle": case "text-author": case "v":
                    sb.Append(Regex.Replace(e.InnerText, @"\s+", " ").Trim()).Append(e.LocalName == "v" ? "\n" : "\n\n");
                    return;
                case "empty-line": sb.Append("\n"); return;
                case "stanza": foreach (var c in e.ChildNodes.OfType<XmlElement>()) Fb2Text(c, sb); sb.Append("\n"); return;
            }
            foreach (var c in e.ChildNodes.OfType<XmlElement>()) Fb2Text(c, sb);
        }

        static XmlElement Child(XmlElement e, string local) => e.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == local);

        static string Clean(string s) => RxManyBreaks.Replace(s.Replace("\r", ""), "\n\n").Trim();

        // ---------------------------------------------------------------- HTML
        static readonly Regex RxHeading12 = new Regex(@"<h([12])\b[^>]*>(.*?)</h\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        static readonly Regex RxTitle = new Regex(@"<title\b[^>]*>(.*?)</title\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        static TextBook LoadHtml(string html, string fallbackTitle)
        {
            var tm = RxTitle.Match(html);
            var title = tm.Success ? Plain(tm.Groups[1].Value) : "";
            if (title.Length == 0) title = fallbackTitle;
            var bodyStart = Regex.Match(html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
            if (bodyStart.Success) html = html.Substring(bodyStart.Index + bodyStart.Length);
            var chapters = new List<Chapter>();
            var heads = RxHeading12.Matches(html).Cast<Match>().ToList();
            if (heads.Count == 0)
            {
                var text = Plain(html);
                if (text.Length > 0) chapters.Add(new Chapter(title, text));
            }
            else
            {
                var pre = Plain(html.Substring(0, heads[0].Index));
                if (pre.Length > 0) chapters.Add(new Chapter(title, pre));
                for (int i = 0; i < heads.Count; i++)
                {
                    int end = i + 1 < heads.Count ? heads[i + 1].Index : html.Length;
                    var text = Plain(html.Substring(heads[i].Index, end - heads[i].Index));
                    var ct = Plain(heads[i].Groups[2].Value);
                    if (ct.Length == 0) ct = "Kapitel " + (chapters.Count + 1);
                    if (text.Length > 0) chapters.Add(new Chapter(ct, text));
                }
            }
            if (chapters.Count == 0) throw new BookException("Die Seite enthaelt keinen lesbaren Text.");
            return new TextBook(title, "", chapters);
        }

        // ---------------------------------------------------------------- Markdown
        static readonly Regex RxMdImage = new Regex(@"!\[[^\]]*\]\([^)]*\)");
        static readonly Regex RxMdLink = new Regex(@"\[([^\]]+)\]\([^)]*\)");
        static readonly Regex RxMdRefLink = new Regex(@"\[([^\]]+)\]\[[^\]]*\]");
        static readonly Regex RxMdBold = new Regex(@"(\*\*|__)(.+?)\1");
        static readonly Regex RxMdItalic = new Regex(@"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])|(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)");
        static readonly Regex RxMdCode = new Regex(@"`([^`]+)`");
        static readonly Regex RxMdStrike = new Regex(@"~~(.+?)~~");
        static readonly Regex RxMdList = new Regex(@"^(\s*)([-*+]|\d+[.)])\s+(.*)$");
        static readonly Regex RxMdRefDef = new Regex(@"^\s*\[[^\]]+\]:\s+\S+");

        static string MdInline(string s)
        {
            s = RxMdImage.Replace(s, "");
            s = RxMdLink.Replace(s, "$1");
            s = RxMdRefLink.Replace(s, "$1");
            s = RxMdCode.Replace(s, "$1");
            s = RxMdBold.Replace(s, "$2");
            s = RxMdItalic.Replace(s, m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            s = RxMdStrike.Replace(s, "$1");
            s = RxTag.Replace(s, "");
            return s.Replace("\\*", "*").Replace("\\_", "_").Replace("\\#", "#");
        }

        static TextBook LoadMarkdown(string md, string fallbackTitle)
        {
            var lines = md.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var chapters = new List<Chapter>();
            string bookTitle = null, curTitle = null;
            var text = new StringBuilder();
            var para = new StringBuilder();
            bool fence = false;

            void FlushPara()
            {
                if (para.Length > 0) { text.Append(MdInline(para.ToString().Trim())).Append("\n\n"); para.Clear(); }
            }
            void FlushChapter()
            {
                FlushPara();
                var t = Clean(text.ToString());
                if (t.Length > 0) chapters.Add(new Chapter(curTitle ?? bookTitle ?? fallbackTitle, t));
                text.Clear();
            }

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) { FlushPara(); fence = !fence; continue; }
                if (fence) { text.Append(line).Append('\n'); continue; }
                if (trimmed.Length == 0) { FlushPara(); continue; }
                if (RxMdRefDef.IsMatch(line)) continue;

                // Setext-Ueberschrift (Text, darunter === oder ---)
                if (i + 1 < lines.Length && para.Length == 0 && Regex.IsMatch(lines[i + 1], @"^\s*(=+|-+)\s*$") && !trimmed.StartsWith("#") && !RxMdList.IsMatch(line))
                {
                    bool h1 = lines[i + 1].Trim().StartsWith("=");
                    if (h1 || chapters.Count + text.Length > 0 || true)
                    {
                        FlushChapter();
                        curTitle = MdInline(trimmed);
                        if (bookTitle == null && h1) bookTitle = curTitle;
                        text.Append(curTitle).Append("\n\n");
                        i++; continue;
                    }
                }
                var hm = Regex.Match(trimmed, @"^(#{1,6})\s+(.*?)\s*#*$");
                if (hm.Success)
                {
                    var ht = MdInline(hm.Groups[2].Value);
                    if (hm.Groups[1].Length <= 2)
                    {
                        FlushChapter();
                        curTitle = ht;
                        if (bookTitle == null && hm.Groups[1].Length == 1) bookTitle = ht;
                    }
                    else FlushPara();
                    text.Append(ht).Append("\n\n");
                    continue;
                }
                if (Regex.IsMatch(trimmed, @"^([-*_]\s*){3,}$")) { FlushPara(); text.Append("\n"); continue; }
                if (trimmed.StartsWith(">")) trimmed = trimmed.TrimStart('>', ' ');
                var lm = RxMdList.Match(line);
                if (lm.Success)
                {
                    FlushPara();
                    var marker = char.IsDigit(lm.Groups[2].Value[0]) ? lm.Groups[2].Value + " " : "• ";
                    text.Append(new string(' ', Math.Min(lm.Groups[1].Length, 8))).Append(marker).Append(MdInline(lm.Groups[3].Value)).Append('\n');
                    continue;
                }
                if (para.Length > 0) para.Append(line.EndsWith("  ") ? "\n" : " ");
                para.Append(trimmed);
            }
            FlushChapter();
            if (chapters.Count == 0) throw new BookException("Die Markdown-Datei enthaelt keinen Text.");
            return new TextBook(bookTitle ?? fallbackTitle, "", chapters);
        }

        // ---------------------------------------------------------------- Zeichensatz aus Kopf (XML/HTML)
        /// <summary>Wie Decode, aber ungueltiges UTF-8 wird nach der Angabe im Dateikopf gelesen (z. B. windows-1251 in FB2).</summary>
        public static string DecodeDeclared(byte[] data)
        {
            if (data.Length >= 2 && (data[0] == 0xFF || data[0] == 0xFE)) return Decode(data);
            try
            {
                var s = new UTF8Encoding(false, true).GetString(data);
                return s.Length > 0 && s[0] == '﻿' ? s.Substring(1) : s;
            }
            catch (DecoderFallbackException) { }
            var head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 1024)).ToLowerInvariant();
            if (Regex.IsMatch(head, @"(encoding|charset)\s*=\s*[""']?(windows-1251|cp1251|win-1251)")) return Cp1251(data);
            return Latin1(data);
        }

        static readonly char[] Cp1251Hi =
        {
            'Ђ','Ѓ','‚','ѓ','„','…','†','‡','€','‰','Љ','‹','Њ','Ќ','Ћ','Џ',
            'ђ','‘','’','“','”','•','–','—','\u0098','™','љ','›','њ','ќ','ћ','џ',
            ' ','Ў','ў','Ј','¤','Ґ','¦','§','Ё','©','Є','«','¬','­','®','Ї',
            '°','±','І','і','ґ','µ','¶','·','ё','№','є','»','ј','Ѕ','ѕ','ї'
        };

        static string Cp1251(byte[] data)
        {
            var c = new char[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                var b = data[i];
                c[i] = b < 0x80 ? (char)b : b < 0xC0 ? Cp1251Hi[b - 0x80] : (char)(0x0410 + (b - 0xC0));
            }
            return new string(c);
        }

        // ---------------------------------------------------------------- XML
        static XmlDocument Xml(byte[] data)
        {
            var source = DecodeDeclared(data);
            // Kopfangabe entfernen: der Text ist bereits dekodiert (sonst Konflikt mit encoding="windows-1251")
            source = Regex.Replace(source, @"^\s*<\?xml[^>]*\?>", "");
            if (Regex.IsMatch(source, @"<!\s*(?:DOCTYPE|ENTITY)", RegexOptions.IgnoreCase))
            {
                // XHTML-Doctypes sind in EPUB-Paketdateien selten; Entities/DTDs werden grundsaetzlich abgelehnt.
                throw new BookException("XML-DTDs und externe Entitaeten sind nicht erlaubt.");
            }
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                using (var sr = new StringReader(source))
                using (var xr = XmlReader.Create(sr, settings)) doc.Load(xr);
                return doc;
            }
            catch (XmlException e) { throw new BookException("Ungueltige EPUB-XML-Struktur.", e); }
        }

        static IEnumerable<XmlElement> ByLocal(XmlDocument doc, string local)
        {
            foreach (XmlNode n in doc.GetElementsByTagName("*"))
                if (n is XmlElement e && e.LocalName == local) yield return e;
        }

        static string FirstText(XmlDocument doc, string local, string fallback)
        {
            var e = ByLocal(doc, local).FirstOrDefault();
            var v = e?.InnerText.Trim();
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        // ---------------------------------------------------------------- Pfade
        static string ResolvePath(string basePath, string reference)
        {
            if (string.IsNullOrEmpty(reference)) throw new BookException("Ungueltiger EPUB-Pfad.");
            var hash = reference.IndexOf('#');
            if (hash >= 0) reference = reference.Substring(0, hash);
            if (reference.Contains("://") || reference.StartsWith("//") || reference.Contains("?"))
                throw new BookException("Externe EPUB-Verweise sind nicht erlaubt.");
            var relative = Uri.UnescapeDataString(reference);
            if (relative.Length == 0 || relative.StartsWith("/") || relative.IndexOf('\\') >= 0 || relative.IndexOf('\0') >= 0)
                throw new BookException("Ungueltiger EPUB-Pfad.");
            var prefix = basePath.Contains("/") ? basePath.Substring(0, basePath.LastIndexOf('/') + 1) : "";
            var parts = new List<string>();
            foreach (var part in (prefix + relative).Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..")
                {
                    if (parts.Count == 0) throw new BookException("EPUB-Pfad verlaesst das Archiv.");
                    parts.RemoveAt(parts.Count - 1);
                }
                else
                {
                    if (part.IndexOf(':') >= 0) throw new BookException("Ungueltiger EPUB-Pfad.");
                    parts.Add(part);
                }
            }
            return string.Join("/", parts);
        }

        // ---------------------------------------------------------------- Text
        public static string Decode(byte[] data)
        {
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
            string s;
            try { s = new UTF8Encoding(false, true).GetString(data); }
            catch (DecoderFallbackException) { s = Latin1(data); } // Rueckfall fuer alte TXT (ISO-8859-1/Windows-1252)
            return s.Length > 0 && s[0] == '﻿' ? s.Substring(1) : s;
        }

        static readonly char[] Cp1252 =
        {
            '\u20AC','\u0081','\u201A','\u0192','\u201E','\u2026','\u2020','\u2021','\u02C6','\u2030','\u0160','\u2039','\u0152','\u008D','\u017D','\u008F',
            '\u0090','\u2018','\u2019','\u201C','\u201D','\u2022','\u2013','\u2014','\u02DC','\u2122','\u0161','\u203A','\u0153','\u009D','\u017E','\u0178'
        };

        static string Latin1(byte[] data)
        {
            var c = new char[data.Length];
            for (int i = 0; i < data.Length; i++) { var b = data[i]; c[i] = b >= 0x80 && b < 0xA0 ? Cp1252[b - 0x80] : (char)b; }
            return new string(c);
        }

        static readonly Regex RxComment = new Regex(@"<!--.*?-->", RegexOptions.Singleline);
        static readonly Regex RxDrop = new Regex(@"<(head|script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        static readonly Regex RxBreak = new Regex(@"<br\b[^>]*>|</(?:p|div|h[1-6]|li|blockquote|section|tr)\s*>", RegexOptions.IgnoreCase);
        static readonly Regex RxTag = new Regex(@"<[^>]*>", RegexOptions.Singleline);
        static readonly Regex RxEntity = new Regex(@"&(#x[0-9a-fA-F]+|#[0-9]+|[A-Za-z]+);");
        static readonly Regex RxSpaces = new Regex(@"[\t\x0B\f  ]+");
        static readonly Regex RxLineTrim = new Regex(@" *\r?\n *");
        static readonly Regex RxManyBreaks = new Regex(@"\n{3,}");
        static readonly Dictionary<string, string> Named = new Dictionary<string, string>
        {
            {"amp","&"},{"lt","<"},{"gt",">"},{"quot","\""},{"apos","'"},{"nbsp"," "},{"ndash","–"},{"mdash","—"},
            {"hellip","…"},{"lsquo","‘"},{"rsquo","’"},{"ldquo","“"},{"rdquo","”"},{"bdquo","„"},
            {"sbquo","‚"},{"laquo","«"},{"raquo","»"},{"copy","©"},{"auml","ä"},{"ouml","ö"},
            {"uuml","ü"},{"Auml","Ä"},{"Ouml","Ö"},{"Uuml","Ü"},{"szlig","ß"},{"eacute","é"},
            {"egrave","è"},{"agrave","à"},{"ccedil","ç"},{"shy",""}
        };

        public static string Plain(string html)
        {
            var s = RxComment.Replace(html, "");
            s = RxDrop.Replace(s, "");
            s = RxBreak.Replace(s, "\n\n");
            s = RxTag.Replace(s, "");
            s = RxEntity.Replace(s, m =>
            {
                var e = m.Groups[1].Value;
                if (e.StartsWith("#"))
                {
                    try
                    {
                        int code = e.StartsWith("#x") ? Convert.ToInt32(e.Substring(2), 16) : int.Parse(e.Substring(1));
                        if (code > 0 && code <= 0x10FFFF && !(code >= 0xD800 && code <= 0xDFFF)) return char.ConvertFromUtf32(code);
                    }
                    catch { }
                    return m.Value;
                }
                return Named.TryGetValue(e, out var v) ? v : m.Value;
            });
            s = RxSpaces.Replace(s, " ");
            s = RxLineTrim.Replace(s, "\n");
            s = RxManyBreaks.Replace(s, "\n\n");
            return s.Trim();
        }

        // ---------------------------------------------------------------- Archiv mit Grenzen
        sealed class Archive
        {
            readonly Dictionary<string, ZipArchiveEntry> m_Entries = new Dictionary<string, ZipArchiveEntry>();
            long m_Loaded;

            public Archive(ZipArchive zip)
            {
                long total = 0;
                if (zip.Entries.Count > MaxEntries) throw new BookException("EPUB enthaelt zu viele Dateien.");
                foreach (var e in zip.Entries)
                {
                    var n = e.FullName;
                    if (n.StartsWith("/") || n.IndexOf('\\') >= 0 || n.IndexOf(':') >= 0 || n.IndexOf('\0') >= 0 || n.Split('/').Contains(".."))
                        throw new BookException("Unsicherer EPUB-Archivpfad.");
                    if (m_Entries.ContainsKey(n)) throw new BookException("Doppelter EPUB-Archivpfad.");
                    if (e.Length > MaxEntry) throw new BookException("EPUB-Eintrag ueberschreitet 8 MiB.");
                    total += e.Length;
                    if (total > MaxTotal) throw new BookException("EPUB ueberschreitet entpackt 64 MiB.");
                    m_Entries[n] = e;
                }
            }

            public bool Has(string name) => m_Entries.ContainsKey(name);

            public byte[] Get(string name)
            {
                if (!m_Entries.TryGetValue(name, out var e) || name.EndsWith("/")) throw new BookException("EPUB-Datei fehlt: " + name);
                var limit = (int)Math.Min(MaxEntry, MaxTotal - m_Loaded);
                using (var s = e.Open())
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[8192]; int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (ms.Length + n > limit) throw new BookException("EPUB ueberschreitet das sichere Groessenlimit.");
                        ms.Write(buf, 0, n);
                    }
                    m_Loaded += ms.Length;
                    return ms.ToArray();
                }
            }
        }
    }
}
