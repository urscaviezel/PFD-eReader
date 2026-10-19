using System;
using System.Collections.Generic;
using PFDReader.Books;
using PFDReader.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PFDReader.UI
{
    /// <summary>Bibliothek: Buecher aus eBooks (und alte private Buecher), Aktualisieren, Ordnerfreigabe, Anleitung, Loeschen.</summary>
    public sealed class LibraryPanel
    {
        public readonly Canvas Canvas;
        readonly RectTransform m_List;
        readonly ScrollRect m_Scroll;
        readonly TextMeshProUGUI m_Status;
        readonly RectTransform m_Bar, m_BarFill;
        readonly Button m_Permission, m_Back;
        public event Action<LibraryEntry> Open;
        public event Action<LibraryEntry> Delete;
        public event Action Back, Guide, Settings, Import;

        public LibraryPanel(Transform parent, Camera eventCamera)
        {
            Canvas = UiKit.CreateCanvas("Library", parent, new Vector2(616, 416), eventCamera);
            var bg = UiKit.Panel("Bg", Canvas.transform, UiKit.Bg);
            bg.rectTransform.Stretch();

            var icon = UiKit.Rect("Logo", bg.transform).gameObject.AddComponent<Image>();
            icon.sprite = UiIcons.Get("books"); icon.color = UiKit.Cyan; icon.raycastTarget = false;
            Place((RectTransform)icon.transform, 20, 18, 34, 34);
            var title = UiKit.Label("Title", bg.transform, Loc.T("Bibliothek"), 28, UiKit.Text);
            title.fontStyle = FontStyles.Bold;
            Place(title.rectTransform, 64, 14, 260, 42);

            var tools = UiKit.Rect("Tools", bg.transform);
            tools.anchorMin = tools.anchorMax = new Vector2(1, 1); tools.pivot = new Vector2(1, 1);
            tools.anchoredPosition = new Vector2(-16, -14); tools.sizeDelta = new Vector2(460, 42);
            var h = UiKit.Row(tools, 8); h.childAlignment = TextAnchor.MiddleRight; h.childForceExpandHeight = false;
            m_Permission = UiKit.Button("Permission", tools, Loc.T("Ordner eBooks freigeben"), 15, () => Library.RequestStorageAccess(), UiKit.Violet);
            m_Permission.Width(200); m_Permission.Height(42);
            m_Back = UiIcons.IconButton("back", tools, 42, () => Back?.Invoke(), null, null);
            UiIcons.IconButton("import", tools, 42, () => Import?.Invoke());
            UiIcons.IconButton("refresh", tools, 42, Refresh);
            UiIcons.IconButton("guide", tools, 42, () => Guide?.Invoke());
            UiIcons.IconButton("settings", tools, 42, () => Settings?.Invoke());

            m_Status = UiKit.Label("Status", bg.transform, "", 15, UiKit.TextDim);
            m_Status.textWrappingMode = TextWrappingModes.Normal;
            Place(m_Status.rectTransform, 20, 62, 576, 24);
            // Fortschrittsbalken (z. B. beim ersten Entpacken von CBR/CB7)
            var bar = UiKit.Panel("Progress", bg.transform, UiKit.Line);
            m_Bar = bar.rectTransform; Place(m_Bar, 20, 86, 576, 5);
            var fill = UiKit.Panel("Fill", m_Bar, UiKit.Cyan);
            UiGradient.Set(fill, true);
            m_BarFill = fill.rectTransform;
            m_BarFill.anchorMin = Vector2.zero; m_BarFill.anchorMax = new Vector2(0, 1); m_BarFill.pivot = new Vector2(0, 0.5f);
            m_BarFill.offsetMin = m_BarFill.offsetMax = Vector2.zero;
            bar.gameObject.SetActive(false);

            m_List = UiKit.ScrollList("List", bg.transform, out m_Scroll, 8);
            ((RectTransform)m_Scroll.transform).Stretch(16, 92, 16, 16);
        }

        static void Place(RectTransform rt, float left, float top, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(left, -top); rt.sizeDelta = new Vector2(w, h);
        }

        public bool Visible
        {
            get => Canvas.gameObject.activeSelf;
            set { Canvas.gameObject.SetActive(value); if (value) Refresh(); }
        }

        public void SetCanGoBack(bool can) => m_Back.gameObject.SetActive(can);

        public void Refresh()
        {
            var entries = Library.Scan(out var msg);
            m_Permission.gameObject.SetActive(!Library.HasStorageAccess);
            UiKit.Clear(m_List);
            foreach (var e in entries) AddRow(e);
            m_Status.text = msg != null ? Loc.Msg(msg) : entries.Count == 0
                ? Loc.T("Keine Bücher gefunden. Bücher (EPUB, FB2, PDF, TXT, HTML, Markdown) oder Comics (CBZ, CBR, CB7, CBT) in den Ordner eBooks kopieren und Aktualisieren wählen.")
                : entries.Count + " " + Loc.T("Bücher gefunden") + "  ·  " + Storage.SharedBooksDir;
        }

        static string KindIcon(BookKind k) => k == BookKind.Comic ? "cover" : k == BookKind.Pdf ? "display" : "books";

        void AddRow(LibraryEntry e)
        {
            var row = UiKit.Rect("Row", m_List);
            row.Height(50);
            var b = UiKit.Button("Book", row, null, 0, () => Open?.Invoke(e));
            ((RectTransform)b.transform).Stretch(0, 0, 56, 0);
            var ic = UiKit.Rect("Kind", b.transform).gameObject.AddComponent<Image>();
            ic.sprite = UiIcons.Get(KindIcon(e.kind)); ic.color = UiKit.TextDim; ic.raycastTarget = false;
            var irt = (RectTransform)ic.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0, 0.5f); irt.pivot = new Vector2(0, 0.5f);
            irt.anchoredPosition = new Vector2(12, 0); irt.sizeDelta = new Vector2(26, 26);

            var st = Storage.Book(e.key);
            var title = UiKit.Label("Title", b.transform, e.title, 18, UiKit.Text);
            title.rectTransform.Stretch(48, 0, 150, 0);
            var meta = UiKit.Label("Meta", b.transform, Library.FormatLabel(e.kind, e.path) + "  ·  " + Library.SizeLabel(e.size) + (e.legacy ? "  ·  " + Loc.T("intern") : ""), 14, UiKit.TextDim, TextAlignmentOptions.MidlineRight);
            meta.rectTransform.Stretch(380, 0, 14, 0);
            if (st.lastOpened > 0) { title.richText = true; title.text = e.title + "  <color=#22D3FF>›</color>"; }
            title.raycastTarget = false; ScrollText.Make(title);   // lange Titel laufen durch

            var del = UiIcons.IconButton("trash", row, 50, () => Delete?.Invoke(e));
            var drt = (RectTransform)del.transform;
            drt.anchorMin = drt.anchorMax = new Vector2(1, 0.5f); drt.pivot = new Vector2(1, 0.5f);
            drt.anchoredPosition = Vector2.zero; drt.sizeDelta = new Vector2(50, 50);
            var le = del.GetComponent<LayoutElement>(); if (le) le.ignoreLayout = true;
        }

        public void SetStatus(string text) => m_Status.text = text;

        /// <summary>Fortschritt 0..1 anzeigen, negativ = ausblenden.</summary>
        public void SetProgress(float p)
        {
            m_Bar.gameObject.SetActive(p >= 0f);
            if (p >= 0f) m_BarFill.anchorMax = new Vector2(Mathf.Clamp01(p), 1f);
        }

        public void ScrollBy(float rows)
        {
            float content = m_List.rect.height, view = ((RectTransform)m_Scroll.transform).rect.height;
            if (content <= view) return;
            m_Scroll.verticalNormalizedPosition = Mathf.Clamp01(m_Scroll.verticalNormalizedPosition + rows * 58f / (content - view));
        }
    }
}
