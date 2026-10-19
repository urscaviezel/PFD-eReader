using System;
using PFDReader.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PFDReader.UI
{
    /// <summary>
    /// Bildschirmtastatur fuer den Controller: Layout DE (QWERTZ), EN (QWERTY), FR (AZERTY), umschaltbar;
    /// Shift, Ziffern/Sonderzeichen, Leerzeichen, Ruecktaste; Passwortfelder optional sichtbar.
    /// </summary>
    public sealed class VirtualKeyboard
    {
        static readonly string[] LangNames = { "DE", "EN", "FR" };
        readonly RectTransform m_Root;
        readonly TextMeshProUGUI m_Field;
        readonly RectTransform m_Keys;
        readonly bool m_Password;
        bool m_Shift, m_Symbols, m_Show;
        string m_Text;
        int m_Layout;
        readonly Action<string> m_Ok;
        readonly Action m_Cancel;
        float m_Blink;

        public static int Layout => Storage.Settings.keyboardLayout >= 0 ? Storage.Settings.keyboardLayout : Loc.Language;

        public VirtualKeyboard(Transform parent, string label, string text, bool password, Action<string> ok, Action cancel)
        {
            m_Text = text ?? ""; m_Password = password; m_Ok = ok; m_Cancel = cancel; m_Layout = Layout;
            m_Root = UiKit.Rect("Keyboard", parent);
            var v = m_Root.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6; v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            var le = m_Root.gameObject.AddComponent<LayoutElement>(); le.flexibleHeight = 1;

            var l = UiKit.Label("Label", m_Root, label, 17, UiKit.TextDim); l.Height(22);
            var fieldBg = UiKit.Panel("Field", m_Root, UiKit.BgSoft, true); fieldBg.Height(42);
            m_Field = UiKit.Label("Text", fieldBg.transform, "", 20, UiKit.Text);
            m_Field.rectTransform.Stretch(12, 2, 12, 2);
            m_Field.textWrappingMode = TextWrappingModes.NoWrap; m_Field.overflowMode = TextOverflowModes.Masking;
            m_Field.richText = false;

            m_Keys = UiKit.Rect("Keys", m_Root);
            var kv = m_Keys.gameObject.AddComponent<VerticalLayoutGroup>();
            kv.spacing = 5; kv.childControlHeight = true; kv.childControlWidth = true; kv.childForceExpandHeight = false; kv.childForceExpandWidth = true;
            BuildKeys();
            Refresh();
        }

        static string[] Rows(int lang, bool shift, bool symbols)
        {
            if (symbols) return new[] { "1234567890", "!@#$%^&*()", "[]{}<>/\\|", ":;\"'`~_+=", lang == 2 ? "éèêàâùûôîç" : "äöüß,.?!€§" };
            var rows = lang == 0 ? new[] { "1234567890ß", "qwertzuiopü", "asdfghjklöä", "yxcvbnm,.-" }
                     : lang == 2 ? new[] { "1234567890", "azertyuiop", "qsdfghjklm", "wxcvbn,;:!" }
                     : new[] { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm,./" };
            if (!shift) return rows;
            var r = (string[])rows.Clone();
            for (int i = 1; i < r.Length; i++) r[i] = r[i].ToUpperInvariant();
            r[0] = lang == 0 ? "!\"§$%&/()=?" : lang == 2 ? "&é\"'(-è_çà)" : "!@#$%^&*()";
            return r;
        }

        void BuildKeys()
        {
            UiKit.Clear(m_Keys);
            foreach (var row in Rows(m_Layout, m_Shift, m_Symbols))
            {
                var r = UiKit.Rect("Row", m_Keys); r.Height(38);
                var h = UiKit.Row(r, 4); h.childAlignment = TextAnchor.MiddleCenter;
                foreach (var ch in row)
                {
                    var c = ch.ToString();
                    var b = UiKit.Button("K", r, c, 19, () => Type(c));
                    b.Width(40);
                }
            }
            var f = UiKit.Rect("Fn", m_Keys); f.Height(38);
            var fh = UiKit.Row(f, 4); fh.childAlignment = TextAnchor.MiddleCenter;
            var shift = UiIcons.IconButton("up", f, 38, () => { m_Shift = !m_Shift; BuildKeys(); }); shift.Width(52); UiKit.SetToggled(shift, m_Shift);
            var sym = UiKit.Button("Sym", f, m_Symbols ? "abc" : "123", 16, () => { m_Symbols = !m_Symbols; BuildKeys(); }); sym.Width(62); UiKit.SetToggled(sym, m_Symbols);
            UiKit.Button("Space", f, Loc.T("Leerzeichen"), 15, () => Type(" ")).Width(150);
            UiIcons.IconButton("back", f, 38, () => { if (m_Text.Length > 0) m_Text = m_Text.Substring(0, m_Text.Length - 1); Refresh(); }).Width(56);
            UiKit.Button("Lang", f, LangNames[m_Layout], 16, () => { m_Layout = (m_Layout + 1) % 3; Storage.Settings.keyboardLayout = m_Layout; Storage.SaveSettings(); BuildKeys(); }).Width(52);
            if (m_Password)
            {
                var eye = UiIcons.IconButton(m_Show ? "eye_off" : "eye", f, 38, () => { m_Show = !m_Show; BuildKeys(); Refresh(); });
                eye.Width(46);
            }

            var a = UiKit.Rect("Actions", m_Keys); a.Height(40);
            var ah = UiKit.Row(a, 8); ah.childAlignment = TextAnchor.MiddleRight;
            UiKit.Button("Cancel", a, Loc.T("Abbrechen"), 17, () => m_Cancel?.Invoke()).Width(150);
            UiKit.Button("Ok", a, Loc.T("Übernehmen"), 17, () => m_Ok?.Invoke(m_Text), UiKit.Violet).Width(170);
        }

        void Type(string c)
        {
            if (m_Text.Length >= 256) return;
            m_Text += c;
            if (m_Shift && !m_Symbols) { m_Shift = false; BuildKeys(); }
            Refresh();
        }

        void Refresh()
        {
            var shown = m_Password && !m_Show ? new string('*', m_Text.Length) : m_Text;
            bool caret = (Time.unscaledTime % 1f) < 0.55f;
            m_Field.text = shown + (caret ? "|" : " ");
            m_Field.alignment = TextAlignmentOptions.MidlineLeft;
            m_Field.ForceMeshUpdate();
            // lange Eingaben: Ende sichtbar halten
            m_Field.alignment = m_Field.preferredWidth > m_Field.rectTransform.rect.width ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
        }

        public void Tick()
        {
            if (Time.unscaledTime - m_Blink > 0.5f) { m_Blink = Time.unscaledTime; Refresh(); }
        }
    }
}
