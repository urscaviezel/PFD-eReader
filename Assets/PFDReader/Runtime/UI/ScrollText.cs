using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PFDReader.UI
{
    /// <summary>
    /// Laufschrift fuer zu lange einzeilige Texte: der Text wird in einen beschnittenen Bereich gesetzt und
    /// laeuft langsam hin und her (mit Pausen an den Enden). Passt er hinein, bleibt er stehen.
    /// </summary>
    public sealed class ScrollText : MonoBehaviour
    {
        const float Speed = 40f, Pause = 1.6f;
        TMP_Text m_Text;
        RectTransform m_Clip;
        float m_T, m_Width = -1;

        /// <summary>Macht aus einem (schon platzierten) Label eine Laufschrift.</summary>
        public static ScrollText Make(TMP_Text label)
        {
            var lrt = label.rectTransform;
            var clip = new GameObject(label.name + "_Clip", typeof(RectTransform)).GetComponent<RectTransform>();
            clip.SetParent(lrt.parent, false);
            clip.SetSiblingIndex(lrt.GetSiblingIndex());
            clip.anchorMin = lrt.anchorMin; clip.anchorMax = lrt.anchorMax; clip.pivot = lrt.pivot;
            clip.offsetMin = lrt.offsetMin; clip.offsetMax = lrt.offsetMax;
            clip.gameObject.AddComponent<RectMask2D>();
            var le = lrt.GetComponent<LayoutElement>();
            if (le) { var cle = clip.gameObject.AddComponent<LayoutElement>(); cle.minWidth = le.minWidth; cle.preferredWidth = le.preferredWidth; cle.flexibleWidth = le.flexibleWidth; cle.minHeight = le.minHeight; cle.preferredHeight = le.preferredHeight; cle.flexibleHeight = le.flexibleHeight; le.ignoreLayout = true; }
            lrt.SetParent(clip, false);
            lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(0, 1); lrt.pivot = new Vector2(0, 0.5f);
            label.overflowMode = TextOverflowModes.Overflow; label.textWrappingMode = TextWrappingModes.NoWrap;
            if (label.alignment == TextAlignmentOptions.Center || label.alignment == TextAlignmentOptions.Midline) label.alignment = TextAlignmentOptions.MidlineLeft;
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(label.GetPreferredValues(label.text, 100000f, 1000f).x + 2f, 0);
            var m = clip.gameObject.AddComponent<ScrollText>();
            m.m_Text = label; m.m_Clip = clip;
            return m;
        }

        void OnEnable() { m_T = 0; }

        void Update()
        {
            if (m_Text == null) return;
            float pref = m_Text.GetPreferredValues(m_Text.text, 100000f, 1000f).x;
            if (!Mathf.Approximately(pref, m_Width))
            {
                m_Width = pref;
                m_Text.rectTransform.sizeDelta = new Vector2(pref + 2f, 0);
                m_T = 0;
            }
            float over = pref - m_Clip.rect.width;
            if (over <= 2f) { m_Text.rectTransform.anchoredPosition = Vector2.zero; return; }
            // hin: Pause – laufen – Pause – zurueck laufen
            float run = over / Speed, cycle = 2f * (Pause + run);
            m_T = (m_T + Time.unscaledDeltaTime) % cycle;
            float t = m_T, x;
            if (t < Pause) x = 0;
            else if (t < Pause + run) x = (t - Pause) * Speed;
            else if (t < 2 * Pause + run) x = over;
            else x = over - (t - 2 * Pause - run) * Speed;
            m_Text.rectTransform.anchoredPosition = new Vector2(-x, 0);
        }
    }
}
