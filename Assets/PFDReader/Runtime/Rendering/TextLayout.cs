using System;
using System.Collections.Generic;
using PFDReader.Books;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace PFDReader.Rendering
{
    /// <summary>Ein Seitenbereich innerhalb eines Kapitels (Zeichenoffsets, Ende exklusiv).</summary>
    public struct TextPageRange { public int chapter; public int start; public int end; }

    /// <summary>Darstellungsparameter fuer umbrochene Seiten.</summary>
    [Serializable]
    public struct TextStyle : IEquatable<TextStyle>
    {
        public float fontSize;   // in Seitenpixeln bei 1536 px Breite
        public bool dark;
        public bool Equals(TextStyle o) => Mathf.Approximately(fontSize, o.fontSize) && dark == o.dark;
        public bool LayoutEquals(TextStyle o) => Mathf.Approximately(fontSize, o.fontSize);
    }

    /// <summary>
    /// Umbruch und Rasterung von Textseiten mit TextMeshPro (SDF, gestochen scharf) in RenderTextures.
    /// Lange Kapitel werden in Abschnitte (max. ~12k Zeichen, an Absatzgrenzen) geteilt, damit TMP schnell bleibt.
    /// </summary>
    public sealed class TextLayout : IDisposable
    {
        public const int PageW = 1536, PageH = 2048;
        const int SectionChars = 12000;
        const float Margin = 120f, HeaderH = 70f, FooterH = 70f;

        readonly TextBook m_Book;
        readonly TMP_FontAsset m_Font;
        readonly GameObject m_Root;
        readonly TextMeshPro m_Body, m_Header, m_Footer;
        readonly CommandBuffer m_Cmd = new CommandBuffer { name = "PFD TextPage" };
        TextStyle m_Style;

        // Abschnitte je Kapitel und deren Seiten (lazy)
        readonly List<List<(int start, int end)>> m_Sections = new List<List<(int, int)>>();
        readonly Dictionary<int, List<TextPageRange>> m_ChapterPages = new Dictionary<int, List<TextPageRange>>();

        public TextBook Book => m_Book;
        public TextStyle Style => m_Style;

        public TextLayout(TextBook book, TMP_FontAsset font, TextStyle style)
        {
            m_Book = book; m_Font = font; m_Style = style;
            m_Root = new GameObject("PFD TextLayout") { hideFlags = HideFlags.HideAndDontSave };
            m_Root.transform.position = new Vector3(0, -1000, 0);
            m_Body = MakeText("Body", TextAlignmentOptions.TopJustified);
            m_Header = MakeText("Header", TextAlignmentOptions.Center);
            m_Footer = MakeText("Footer", TextAlignmentOptions.Center);
            foreach (var ch in book.Chapters) m_Sections.Add(SplitSections(ch.Text));
            ApplyStyle();
        }

        TextMeshPro MakeText(string name, TextAlignmentOptions align)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(m_Root.transform, false);
            var t = go.AddComponent<TextMeshPro>();
            t.font = m_Font;
            t.richText = false;
            t.parseCtrlCharacters = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Page;
            t.alignment = align;
            t.isOrthographic = true;
            t.extraPadding = true;
            t.renderer.enabled = false;
            return t;
        }

        public void SetStyle(TextStyle s)
        {
            bool relayout = !m_Style.LayoutEquals(s);
            m_Style = s;
            ApplyStyle();
            if (relayout) m_ChapterPages.Clear();
        }

        void ApplyStyle()
        {
            var bodyRect = new Vector2(PageW - 2 * Margin, PageH - 2 * Margin - HeaderH - FooterH);
            m_Body.rectTransform.sizeDelta = bodyRect;
            m_Body.rectTransform.localPosition = new Vector3(0, (FooterH - HeaderH) * 0.5f, 0);
            m_Body.fontSize = m_Style.fontSize;
            m_Body.lineSpacing = 18f;
            m_Body.paragraphSpacing = m_Style.fontSize * 0.55f;

            m_Header.rectTransform.sizeDelta = new Vector2(PageW - 2 * Margin, HeaderH);
            m_Header.rectTransform.localPosition = new Vector3(0, PageH * 0.5f - Margin - HeaderH * 0.5f + 30f, 0);
            m_Header.fontSize = 30f; m_Header.overflowMode = TextOverflowModes.Ellipsis;
            m_Footer.rectTransform.sizeDelta = new Vector2(PageW - 2 * Margin, FooterH);
            m_Footer.rectTransform.localPosition = new Vector3(0, -PageH * 0.5f + Margin + FooterH * 0.5f - 30f, 0);
            m_Footer.fontSize = 30f; m_Footer.overflowMode = TextOverflowModes.Overflow;

            var ink = m_Style.dark ? new Color32(226, 220, 205, 255) : new Color32(34, 30, 26, 255);
            var dim = m_Style.dark ? new Color32(150, 145, 135, 255) : new Color32(120, 112, 100, 255);
            m_Body.color = ink; m_Header.color = dim; m_Footer.color = dim;
        }

        public Color PaperColor => m_Style.dark ? new Color(0.11f, 0.105f, 0.10f) : new Color(0.97f, 0.95f, 0.90f);

        // ---------------------------------------------------------------- Abschnitte / Umbruch
        static List<(int, int)> SplitSections(string text)
        {
            var list = new List<(int, int)>();
            int pos = 0;
            while (pos < text.Length)
            {
                int end = Math.Min(text.Length, pos + SectionChars);
                if (end < text.Length)
                {
                    int cut = text.LastIndexOf("\n\n", end, Math.Min(end - pos, SectionChars / 2), StringComparison.Ordinal);
                    if (cut <= pos) cut = text.LastIndexOf('\n', end - 1, Math.Min(end - pos, SectionChars / 2));
                    if (cut <= pos) cut = text.LastIndexOf(' ', end - 1, Math.Min(end - pos, SectionChars / 2));
                    if (cut > pos) end = cut;
                }
                list.Add((pos, end));
                pos = end;
                while (pos < text.Length && (text[pos] == '\n' || text[pos] == ' ')) pos++;
            }
            if (list.Count == 0) list.Add((0, 0));
            return list;
        }

        /// <summary>Seiten eines Kapitels (Umbruch wird bei Bedarf berechnet, Hauptthread).</summary>
        public List<TextPageRange> PagesOf(int chapter)
        {
            if (m_ChapterPages.TryGetValue(chapter, out var pages)) return pages;
            pages = new List<TextPageRange>();
            var text = m_Book.Chapters[chapter].Text;
            m_Body.overflowMode = TextOverflowModes.Page;
            foreach (var (s, e) in m_Sections[chapter])
            {
                if (e <= s) continue;
                m_Body.text = text.Substring(s, e - s);
                m_Body.pageToDisplay = 1;
                m_Body.ForceMeshUpdate(true, true);
                var info = m_Body.textInfo;
                int count = Math.Max(1, info.pageCount);
                for (int p = 0; p < count; p++)
                {
                    int first = info.pageInfo[p].firstCharacterIndex;
                    int last = info.pageInfo[p].lastCharacterIndex;
                    if (first < 0 || last < first) continue;
                    // TMP-Indizes beziehen sich auf characterInfo; bei richText=false und ohne Ersetzungen = String-Index.
                    int a = info.characterInfo[first].index, b = info.characterInfo[last].index + info.characterInfo[last].stringLength;
                    pages.Add(new TextPageRange { chapter = chapter, start = s + a, end = s + Math.Min(b, e - s) });
                }
            }
            if (pages.Count == 0) pages.Add(new TextPageRange { chapter = chapter, start = 0, end = 0 });
            m_ChapterPages[chapter] = pages;
            return pages;
        }

        public int PageIndexForOffset(int chapter, int offset)
        {
            var pages = PagesOf(chapter);
            for (int i = pages.Count - 1; i >= 0; i--) if (pages[i].start <= offset) return i;
            return 0;
        }

        // ---------------------------------------------------------------- Rastern
        public RenderTexture CreateTarget()
        {
            var rt = new RenderTexture(new RenderTextureDescriptor(PageW, PageH, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = true, useMipMap = true, autoGenerateMips = true, msaaSamples = 1
            })
            { name = "PFD TextPage", filterMode = FilterMode.Trilinear, anisoLevel = 8, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            return rt;
        }

        /// <summary>Rastert eine Seite in rt. pageNumber/total fuer die Fusszeile (total &lt;= 0: nur Nummer).</summary>
        public void Render(TextPageRange page, RenderTexture rt, string pageLabel)
        {
            var text = m_Book.Chapters[page.chapter].Text;
            int len = Math.Max(0, Math.Min(page.end, text.Length) - page.start);
            m_Body.overflowMode = TextOverflowModes.Truncate;
            m_Body.text = len > 0 ? text.Substring(page.start, len).TrimEnd('\n') : "";
            m_Body.pageToDisplay = 1;
            m_Header.text = m_Book.Chapters[page.chapter].Title;
            m_Footer.text = pageLabel ?? "";
            m_Body.ForceMeshUpdate(true, true);
            m_Header.ForceMeshUpdate(true, true);
            m_Footer.ForceMeshUpdate(true, true);

            m_Cmd.Clear();
            m_Cmd.SetRenderTarget(rt);
            m_Cmd.ClearRenderTarget(true, true, QualitySettings.activeColorSpace == ColorSpace.Linear ? PaperColor.linear : PaperColor);
            var proj = Matrix4x4.Ortho(-PageW * 0.5f, PageW * 0.5f, -PageH * 0.5f, PageH * 0.5f, -100f, 100f);
            var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * m_Root.transform.worldToLocalMatrix;
            m_Cmd.SetViewProjectionMatrices(view, GL.GetGPUProjectionMatrix(proj, true));
            Draw(m_Body); Draw(m_Header); Draw(m_Footer);
            Graphics.ExecuteCommandBuffer(m_Cmd);
        }

        void Draw(TextMeshPro t)
        {
            if (t.mesh != null && t.mesh.vertexCount > 0)
                m_Cmd.DrawMesh(t.mesh, t.transform.localToWorldMatrix, t.fontSharedMaterial, 0, 0);
            foreach (var sub in t.GetComponentsInChildren<TMP_SubMesh>(true))
            {
                if (sub.renderer != null) sub.renderer.enabled = false;
                if (sub.mesh != null && sub.mesh.vertexCount > 0 && sub.sharedMaterial != null)
                    m_Cmd.DrawMesh(sub.mesh, sub.transform.localToWorldMatrix, sub.sharedMaterial, 0, 0);
            }
            t.renderer.enabled = false;
        }

        public void Dispose()
        {
            m_Cmd.Release();
            if (m_Root != null) UnityEngine.Object.Destroy(m_Root);
        }
    }
}
