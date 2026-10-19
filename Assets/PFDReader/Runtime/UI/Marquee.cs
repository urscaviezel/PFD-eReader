using TMPro;
using UnityEngine;

namespace PFDReader.UI
{
    /// <summary>Laufschrift: passt der Text nicht, wandert er langsam durch (Pause am Anfang und Ende).</summary>
    public sealed class Marquee : MonoBehaviour
    {
        public TextMeshProUGUI Label;
        public float Speed = 40f, Pause = 1.6f;
        RectTransform m_View;
        float m_T, m_W = -1; string m_Last;

        void Awake() => m_View = (RectTransform)transform;

        void LateUpdate()
        {
            if (Label == null) return;
            var rt = Label.rectTransform;
            float vw = m_View.rect.width;
            if (vw < 1f) return;
            if (Label.text != m_Last) { m_Last = Label.text; m_T = 0; m_W = -1; }
            if (m_W < 0) m_W = Label.GetPreferredValues(Label.text, 100000f, 1000f).x; // ohne Umbruch messen
            float w = m_W;
            rt.sizeDelta = new Vector2(Mathf.Max(w, vw), rt.sizeDelta.y);
            if (w <= vw + 1f) { rt.anchoredPosition = new Vector2(0, 0); Label.alignment = TextAlignmentOptions.Center; return; }
            Label.alignment = TextAlignmentOptions.MidlineLeft;
            float travel = w - vw, dur = travel / Speed, cycle = Pause + dur + Pause;
            m_T = (m_T + Time.deltaTime) % cycle;
            float x = m_T < Pause ? 0 : m_T < Pause + dur ? (m_T - Pause) * Speed : travel;
            rt.anchoredPosition = new Vector2(-x, 0);
        }
    }
}
