using System.IO;
using System.Linq;
using System.Text;
using PFDReader.Books;

namespace PFDReader.EditorTools
{
    /// <summary>Formate ohne Headset pruefen: alle Dateien eines Ordners laden und Kapitel/Seiten ausgeben.</summary>
    public static class FormatTest
    {
        public static string Run(string dir)
        {
            var sb = new StringBuilder();
            foreach (var f in Directory.GetFiles(dir).OrderBy(x => x))
            {
                sb.Append(Path.GetFileName(f)).Append(" [").Append(BookFormats.IsSupported(f) ? BookFormats.KindOf(f).ToString() : "-").Append("] ");
                try
                {
                    if (!BookFormats.IsSupported(f)) { sb.AppendLine("nicht unterstuetzt"); continue; }
                    if (BookFormats.KindOf(f) == BookKind.Comic)
                    {
                        using (var c = new ComicPageSource(f))
                        {
                            var img = c.Render(0, 100, 100, 1, UnityEngine.Vector2.zero);
                            sb.AppendLine($"Seiten={c.PageCount} erste={img.encoded?.Length ?? img.rgba.Length} B");
                        }
                        continue;
                    }
                    var b = TextBookLoader.Load(f);
                    sb.AppendLine($"Titel='{b.Title}' Autor='{b.Author}' Kapitel={b.Chapters.Count}");
                    foreach (var ch in b.Chapters.Take(6))
                        sb.AppendLine($"   - {ch.Title}: {ch.Text.Replace("\n", "⏎").Substring(0, System.Math.Min(160, ch.Text.Length))}");
                }
                catch (System.Exception e) { sb.AppendLine("FEHLER " + e.Message + (e.InnerException != null ? " / " + e.InnerException.Message + "\n" + e.InnerException.StackTrace : "")); }
            }
            return sb.ToString();
        }
    }
}
