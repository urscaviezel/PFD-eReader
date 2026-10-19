using System;
using PFDReader.Core;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PFDReader.UI
{
    /// <summary>Modales Listenfenster (Kapitel, Merkliste) vor dem Buch.</summary>
    public sealed class ListPopup
    {
        public readonly Canvas Canvas;
        readonly TextMeshProUGUI m_Title, m_Empty;
        readonly RectTransform m_List;
        readonly ScrollRect m_Scroll;
        Action<int> m_OnPick;

        public ListPopup(Transform parent, Camera eventCamera)
        {
            Canvas = UiKit.CreateCanvas("ListPopup", parent, new Vector2(560, 520), eventCamera);
            Canvas.sortingOrder = 20;
            var bg = UiKit.Panel("Bg", Canvas.transform, UiKit.Bg);
            bg.rectTransform.Stretch();
            m_Title = UiKit.Label("Title", bg.transform, "", 32, UiKit.Text);
            m_Title.fontStyle = FontStyles.Bold;
            m_Title.rectTransform.anchorMin = new Vector2(0, 1); m_Title.rectTransform.anchorMax = new Vector2(1, 1);
            m_Title.rectTransform.pivot = new Vector2(0.5f, 1);
            m_Title.rectTransform.offsetMin = new Vector2(26, -70); m_Title.rectTransform.offsetMax = new Vector2(-170, -18);
            var close = UiKit.Button("Close", bg.transform, Loc.T("Schliessen"), 22, Hide);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(1, 1);
            crt.anchoredPosition = new Vector2(-22, -20); crt.sizeDelta = new Vector2(140, 46);
            m_List = UiKit.ScrollList("List", bg.transform, out m_Scroll, 6);
            ((RectTransform)m_Scroll.transform).Stretch(20, 84, 20, 20);
            m_Empty = UiKit.Label("Empty", bg.transform, "", 24, UiKit.TextDim, TextAlignmentOptions.Center);
            m_Empty.rectTransform.Stretch(30, 100, 30, 30);
            m_Empty.textWrappingMode = TextWrappingModes.Normal;
            Canvas.gameObject.SetActive(false);
        }

        public bool Visible => Canvas.gameObject.activeSelf;

        public void Show(string title, IList<string> items, int highlight, Action<int> onPick, string emptyText = "", Func<int, Action> deleteFor = null)
        {
            m_Title.text = title;
            m_OnPick = onPick;
            UiKit.Clear(m_List);
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var b = UiKit.Button("Item", m_List, null, 0, () => { Hide(); m_OnPick?.Invoke(index); });
                b.Height(54);
                var l = UiKit.Label("Text", b.transform, items[i], 23, i == highlight ? UiKit.Cyan : UiKit.Text);
                l.rectTransform.Stretch(18, 0, deleteFor != null ? 70 : 18, 0);
                if (deleteFor != null)
                {
                    var del = UiIcons.IconButton("trash", b.transform, 44, () => deleteFor(index)?.Invoke());
                    var drt = (RectTransform)del.transform;
                    drt.anchorMin = new Vector2(1, 0.5f); drt.anchorMax = new Vector2(1, 0.5f); drt.pivot = new Vector2(1, 0.5f);
                    drt.anchoredPosition = new Vector2(-8, 0);
                }
            }
            m_Empty.text = items.Count == 0 ? emptyText : "";
            Canvas.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            if (highlight >= 0 && items.Count > 0) m_Scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(highlight / (float)Mathf.Max(1, items.Count - 1));
            else m_Scroll.verticalNormalizedPosition = 1f;
        }

        public void Hide() => Canvas.gameObject.SetActive(false);

        /// <summary>Stick-Scrollen: Zeilen pro Sekunde.</summary>
        public void ScrollBy(float rows)
        {
            if (!Visible || m_List.childCount == 0) return;
            float content = m_List.rect.height, view = ((RectTransform)m_Scroll.transform).rect.height;
            if (content <= view) return;
            float delta = rows * 60f / (content - view);
            m_Scroll.verticalNormalizedPosition = Mathf.Clamp01(m_Scroll.verticalNormalizedPosition + delta);
        }
    }
}
