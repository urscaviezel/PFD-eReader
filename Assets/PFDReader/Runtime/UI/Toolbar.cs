using System;
using PFDReader.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PFDReader.UI
{
    /// <summary>Leseleiste ueber dem Buch (so breit wie die Doppelseite): Icons, darunter Titel (Laufschrift) und Fortschritt.</summary>
    public sealed class Toolbar
    {
        public const float Width = 600f;
        const float S = 46f;
        public readonly Canvas Canvas;
        readonly CanvasGroup m_Group;
        readonly TextMeshProUGUI m_Location, m_Tip;
        readonly RectTransform m_TipBg;
        readonly Button m_Mark, m_Dark, m_FontDown, m_FontUp;
        readonly Image m_Progress;
        float m_HideAt;
        bool m_Zoom;

        public event Action Library, Chapters, Bookmarks, ToggleMark, FontDown, FontUp, Prev, Next, ToggleDark, Settings;

        public Toolbar(Transform parent, Camera eventCamera)
        {
            Canvas = UiKit.CreateCanvas("Toolbar", parent, new Vector2(Width, 150), eventCamera);
            m_Group = Canvas.gameObject.AddComponent<CanvasGroup>();

            m_TipBg = UiKit.Panel("TipBg", Canvas.transform, UiKit.Bg, true).rectTransform;
            m_TipBg.anchorMin = m_TipBg.anchorMax = new Vector2(0.5f, 1); m_TipBg.pivot = new Vector2(0.5f, 1);
            m_TipBg.anchoredPosition = Vector2.zero; m_TipBg.sizeDelta = new Vector2(260, 34);
            m_Tip = UiKit.Label("Tip", m_TipBg, "", 17, UiKit.Text, TextAlignmentOptions.Center);
            m_Tip.rectTransform.Stretch(8, 0, 8, 0);
            m_TipBg.gameObject.SetActive(false);

            var bg = UiKit.Panel("Bg", Canvas.transform, UiKit.Bg);
            bg.rectTransform.anchorMin = new Vector2(0, 0); bg.rectTransform.anchorMax = new Vector2(1, 0);
            bg.rectTransform.pivot = new Vector2(0.5f, 0);
            bg.rectTransform.offsetMin = Vector2.zero; bg.rectTransform.offsetMax = new Vector2(0, 108);

            var row = UiKit.Rect("Row", bg.transform);
            row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(0.5f, 1);
            row.offsetMin = new Vector2(10, -8 - S); row.offsetMax = new Vector2(-10, -8);
            var h = UiKit.Row(row, 5);
            h.childForceExpandHeight = false; h.childControlHeight = true; h.childAlignment = TextAnchor.MiddleLeft;

            Add(row, "books", "Bücher", () => Library?.Invoke());
            Add(row, "chapters", "Kapitel", () => Chapters?.Invoke());
            Add(row, "bookmarks", "Merkliste", () => Bookmarks?.Invoke());
            m_Mark = Add(row, "star", "Lesezeichen setzen / entfernen", () => ToggleMark?.Invoke());
            m_FontDown = Add(row, "text_smaller", "Schrift kleiner", () => FontDown?.Invoke());
            m_FontUp = Add(row, "text_larger", "Schrift grösser", () => FontUp?.Invoke());
            UiKit.Rect("Spacer", row).Width(0, 1);
            Add(row, "prev", "Vorherige Seite", () => Prev?.Invoke());
            Add(row, "next", "Nächste Seite", () => Next?.Invoke());
            m_Dark = Add(row, "moon", "Dunkler Modus", () => ToggleDark?.Invoke());
            Add(row, "settings", "Einstellungen", () => Settings?.Invoke());

            // Titelzeile mit Laufschrift
            var view = UiKit.Rect("TitleView", bg.transform);
            view.anchorMin = new Vector2(0, 0); view.anchorMax = new Vector2(1, 0); view.pivot = new Vector2(0.5f, 0);
            view.offsetMin = new Vector2(16, 18); view.offsetMax = new Vector2(-16, 46);
            view.gameObject.AddComponent<RectMask2D>();
            m_Location = UiKit.Label("Title", view, "", 18, UiKit.TextDim, TextAlignmentOptions.Center);
            m_Location.overflowMode = TextOverflowModes.Overflow;
            m_Location.richText = true;
            var lrt = m_Location.rectTransform;
            lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(0, 1); lrt.pivot = new Vector2(0, 0.5f);
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var mq = view.gameObject.AddComponent<Marquee>(); mq.Label = m_Location;

            var track = UiKit.Panel("Track", bg.transform, UiKit.Line, true);
            track.rectTransform.anchorMin = new Vector2(0, 0); track.rectTransform.anchorMax = new Vector2(1, 0);
            track.rectTransform.pivot = new Vector2(0.5f, 0);
            track.rectTransform.offsetMin = new Vector2(18, 8); track.rectTransform.offsetMax = new Vector2(-18, 12);
            m_Progress = UiKit.Panel("Fill", track.transform, Color.white, true);
            UiGradient.Set(m_Progress, true);
            m_Progress.rectTransform.anchorMin = Vector2.zero; m_Progress.rectTransform.anchorMax = new Vector2(0, 1);
            m_Progress.rectTransform.offsetMin = m_Progress.rectTransform.offsetMax = Vector2.zero;

            m_Group.alpha = 0; m_Group.blocksRaycasts = false;
        }

        Button Add(Transform row, string icon, string tipDe, UnityEngine.Events.UnityAction a)
            => UiIcons.IconButton(icon, row, S, a, Loc.T(tipDe), ShowTip);

        void ShowTip(string text)
        {
            bool on = !string.IsNullOrEmpty(text);
            m_TipBg.gameObject.SetActive(on);
            if (on)
            {
                m_Tip.text = text;
                m_TipBg.sizeDelta = new Vector2(Mathf.Max(120, m_Tip.GetPreferredValues(text).x + 30), 34);
            }
        }

        public void SetLocation(string text, float progress)
        {
            m_Location.text = text;
            m_Progress.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);
        }

        public void SetMarked(bool marked) => UiIcons.SetIcon(m_Mark, marked ? "star_filled" : "star");
        public void SetDark(bool dark)
        {
            UiIcons.SetIcon(m_Dark, dark ? "sun" : "moon");
            var tip = m_Dark.GetComponent<Hover>();
            if (tip) tip.Changed = on => ShowTip(on ? Loc.T(dark ? "Heller Modus" : "Dunkler Modus") : null);
        }

        public void SetZoomMode(bool zoom)
        {
            if (m_Zoom == zoom) return;
            m_Zoom = zoom;
            UiIcons.SetIcon(m_FontDown, zoom ? "zoom_out" : "text_smaller");
            UiIcons.SetIcon(m_FontUp, zoom ? "zoom_in" : "text_larger");
            m_FontDown.GetComponent<Hover>().Changed = on => ShowTip(on ? Loc.T(zoom ? "Verkleinern" : "Schrift kleiner") : null);
            m_FontUp.GetComponent<Hover>().Changed = on => ShowTip(on ? Loc.T(zoom ? "Vergrössern" : "Schrift grösser") : null);
        }

        public void Tick(bool hovering, bool forceVisible)
        {
            if (hovering || forceVisible) m_HideAt = Time.time + 0.65f;
            bool show = Time.time < m_HideAt;
            m_Group.alpha = Mathf.MoveTowards(m_Group.alpha, show ? 1 : 0, Time.deltaTime / 0.15f);
            m_Group.blocksRaycasts = m_Group.alpha > 0.5f;
            m_Group.interactable = m_Group.blocksRaycasts;
            if (!m_Group.blocksRaycasts && m_TipBg.gameObject.activeSelf) m_TipBg.gameObject.SetActive(false);
        }

        public bool Shown => m_Group.alpha > 0.5f;
    }
}
