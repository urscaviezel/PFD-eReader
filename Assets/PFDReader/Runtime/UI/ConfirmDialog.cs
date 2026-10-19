using System;
using PFDReader.Core;
using TMPro;
using UnityEngine;

namespace PFDReader.UI
{
    /// <summary>Kleine Sicherheitsabfrage (z. B. Buch loeschen).</summary>
    public sealed class ConfirmDialog
    {
        public readonly Canvas Canvas;
        readonly TextMeshProUGUI m_Title, m_Detail;
        Action m_OnYes;
        readonly UnityEngine.UI.Button m_Yes;

        public ConfirmDialog(Transform parent, Camera eventCamera)
        {
            Canvas = UiKit.CreateCanvas("Confirm", parent, new Vector2(620, 300), eventCamera);
            Canvas.sortingOrder = 40;
            var bg = UiKit.Panel("Bg", Canvas.transform, UiKit.Bg);
            bg.rectTransform.Stretch();
            m_Title = UiKit.Label("Title", bg.transform, "", 30, UiKit.Text, TextAlignmentOptions.Center);
            m_Title.fontStyle = FontStyles.Bold;
            m_Title.rectTransform.Stretch(30, 26, 30, 210);
            m_Detail = UiKit.Label("Detail", bg.transform, "", 21, UiKit.TextDim, TextAlignmentOptions.Center);
            m_Detail.textWrappingMode = TextWrappingModes.Normal;
            m_Detail.rectTransform.Stretch(30, 90, 30, 100);
            var row = UiKit.Rect("Row", bg.transform);
            row.anchorMin = new Vector2(0.5f, 0); row.anchorMax = new Vector2(0.5f, 0); row.pivot = new Vector2(0.5f, 0);
            row.anchoredPosition = new Vector2(0, 26); row.sizeDelta = new Vector2(460, 60);
            var h = UiKit.Row(row, 20); h.childAlignment = TextAnchor.MiddleCenter;
            UiKit.Button("No", row, Loc.T("Abbrechen"), 24, Hide).Width(200);
            m_Yes = UiKit.Button("Yes", row, Loc.T("Löschen"), 24, () => { Hide(); m_OnYes?.Invoke(); }, UiKit.Danger);
            m_Yes.Width(200);
            Canvas.gameObject.SetActive(false);
        }

        public bool Visible => Canvas.gameObject.activeSelf;

        public void Show(string title, string detail, string yes, Action onYes) => Show(title, detail, yes, onYes, true);

        /// <summary>danger = roter Knopf (Loeschen), sonst Hauptfarbe (z. B. Update ansehen).</summary>
        public void Show(string title, string detail, string yes, Action onYes, bool danger)
        {
            m_Title.text = title; m_Detail.text = detail; m_OnYes = onYes;
            UiKit.SetButtonText(m_Yes, yes);
            if (danger)
            {
                UiGradient.Set(m_Yes.targetGraphic, false);
                var cb = m_Yes.colors; cb.normalColor = UiKit.Danger; cb.selectedColor = UiKit.Danger; m_Yes.colors = cb;
                m_Yes.targetGraphic.CrossFadeColor(UiKit.Danger, 0f, true, true);
            }
            else UiKit.SetToggled(m_Yes, true);
            Canvas.gameObject.SetActive(true);
        }

        public void Hide() => Canvas.gameObject.SetActive(false);
    }
}
