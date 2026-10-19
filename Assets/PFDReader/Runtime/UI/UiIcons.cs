using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PFDReader.UI
{
    /// <summary>Meldet Zeiger-Eintritt/-Austritt (fuer Tooltips).</summary>
    public sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<bool> Changed;
        public void OnPointerEnter(PointerEventData e) => Changed?.Invoke(true);
        public void OnPointerExit(PointerEventData e) => Changed?.Invoke(false);
        void OnDisable() => Changed?.Invoke(false);
    }

    public static class UiIcons
    {
        static readonly Dictionary<string, Sprite> s_Cache = new Dictionary<string, Sprite>();

        public static Sprite Get(string name)
        {
            if (s_Cache.TryGetValue(name, out var s)) return s;
            var tex = Resources.Load<Texture2D>("Icons/" + name);
            if (tex == null) { Debug.LogWarning("[PFD] Icon fehlt: " + name); return null; }
            tex.filterMode = FilterMode.Trilinear;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
            s_Cache[name] = s;
            return s;
        }

        /// <summary>Quadratischer Icon-Knopf. tooltip: bereits uebersetzter Text, onHover meldet ihn.</summary>
        public static Button IconButton(string icon, Transform parent, float size, UnityAction onClick, string tooltip = null, Action<string> onHover = null)
        {
            var b = UiKit.Button(icon, parent, null, 0, onClick);
            b.Width(size); b.Height(size);
            ((RectTransform)b.transform).sizeDelta = new Vector2(size, size); // auch ausserhalb von Layoutgruppen
            var img = UiKit.Rect("Icon", b.transform).gameObject.AddComponent<Image>();
            img.sprite = Get(icon);
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = UiKit.Text;
            ((RectTransform)img.transform).Stretch(size * 0.2f, size * 0.2f, size * 0.2f, size * 0.2f);
            if (tooltip != null && onHover != null)
            {
                var h = b.gameObject.AddComponent<Hover>();
                h.Changed = on => onHover(on ? tooltip : null);
            }
            return b;
        }

        public static void SetIcon(Button b, string icon)
        {
            var img = b.transform.Find("Icon")?.GetComponent<Image>();
            if (img) img.sprite = Get(icon);
        }

        /// <summary>Knopf mit Icon links und Text.</summary>
        public static Button IconTextButton(string icon, string text, Transform parent, float height, float fontSize, UnityAction onClick)
        {
            var b = UiKit.Button(icon, parent, null, 0, onClick);
            b.Height(height);
            var img = UiKit.Rect("Icon", b.transform).gameObject.AddComponent<Image>();
            img.sprite = Get(icon); img.preserveAspect = true; img.raycastTarget = false; img.color = UiKit.Text;
            var irt = (RectTransform)img.transform;
            irt.anchorMin = new Vector2(0, 0.5f); irt.anchorMax = new Vector2(0, 0.5f); irt.pivot = new Vector2(0, 0.5f);
            irt.anchoredPosition = new Vector2(16, 0); irt.sizeDelta = new Vector2(height * 0.56f, height * 0.56f);
            var l = UiKit.Label("Text", b.transform, text, fontSize, UiKit.Text);
            l.rectTransform.Stretch(height * 0.56f + 32, 0, 12, 0);
            return b;
        }

        /// <summary>Segmentierte Auswahl (z. B. Ein/Aus, DE/EN/FR). Liefert die Knoepfe.</summary>
        public static Button[] Segmented(Transform parent, string[] options, int selected, Action<int> onSelect, float height = 34, float width = 96)
        {
            var row = UiKit.Rect("Segmented", parent);
            UiKit.Row(row, 8);
            row.Height(height);
            var buttons = new Button[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                buttons[i] = UiKit.Button(options[i], row, options[i], 17, null);
                buttons[i].Width(width);
                buttons[i].onClick.AddListener(() => { Select(buttons, idx); onSelect(idx); });
            }
            Select(buttons, selected);
            return buttons;
        }

        public static void Select(Button[] buttons, int selected)
        {
            for (int i = 0; i < buttons.Length; i++) UiKit.SetToggled(buttons[i], i == selected);
        }
    }
}
