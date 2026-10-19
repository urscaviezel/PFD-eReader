using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PFDReader.UI
{
    /// <summary>Baukasten fuer die Weltraum-Oberflaeche (UGUI + TextMeshPro, im Code erzeugt). Stil: Graphit / Violett / Cyan.</summary>
    public static class UiKit
    {
        // Farbschema angelehnt an pfdhub.com
        public static readonly Color Bg = new Color32(14, 21, 48, 245);      // --pfd-surface #0e1530
        public static readonly Color BgSoft = new Color32(21, 29, 60, 255);  // --pfd-surface-2 #151d3c
        public static readonly Color Line = new Color32(37, 48, 86, 255);    // --pfd-border #253056
        public static readonly Color Text = new Color32(220, 225, 240, 255); // --pfd-text #dce1f0
        public static readonly Color TextDim = new Color32(142, 152, 184, 255); // --pfd-muted #8e98b8
        public static readonly Color Violet = new Color32(155, 92, 255, 255);   // --pfd-violet #9b5cff
        public static readonly Color Cyan = new Color32(34, 211, 255, 255);     // --pfd-cyan #22d3ff
        public static readonly Color GradLeft = new Color32(138, 43, 242, 255); // Knopf-Verlauf #8a2bf2 -> #00b8e6
        public static readonly Color GradRight = new Color32(0, 184, 230, 255);
        public static readonly Color Danger = new Color32(239, 83, 80, 255);

        public const float MetersPerUnit = 0.001f; // 1 UI-Einheit = 1 mm

        public static TMP_FontAsset Font;
        static Sprite s_Round, s_RoundSmall;

        public static Sprite Rounded => s_Round ? s_Round : (s_Round = MakeRounded(64, 22));
        public static Sprite RoundedSmall => s_RoundSmall ? s_RoundSmall : (s_RoundSmall = MakeRounded(32, 10));

        static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Rounded", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        // ---------------------------------------------------------------- Canvas
        public static Canvas CreateCanvas(string name, Transform parent, Vector2 sizeMm, Camera eventCamera)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eventCamera;
            canvas.sortingOrder = 10;
            go.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizeMm;
            rt.localScale = Vector3.one * MetersPerUnit;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(this RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static Image Panel(string name, Transform parent, Color color, bool small = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = small ? RoundedSmall : Rounded;
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        public static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(string name, Transform parent, string text, float fontSize, UnityAction onClick, Color? bg = null)
        {
            var img = Panel(name, parent, Color.white, true);
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors;
            var baseCol = bg ?? BgSoft;
            cb.normalColor = baseCol;
            cb.highlightedColor = Color.Lerp(baseCol, Violet, 0.55f);
            cb.pressedColor = Violet;
            cb.selectedColor = baseCol;
            cb.disabledColor = new Color(baseCol.r, baseCol.g, baseCol.b, 0.35f);
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            if (bg.HasValue && bg.Value == Violet) MakePrimary(b);
            // sofort in Grundfarbe: sonst blendet der neue Knopf von Weiss (Standardfarbe beim Aktivieren) herueber
            img.CrossFadeColor(cb.normalColor, 0f, true, true);
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null) b.onClick.AddListener(onClick);
            if (text != null)
            {
                var l = Label("Text", img.transform, text, fontSize, Text, TextAlignmentOptions.Center);
                l.rectTransform.Stretch(10, 4, 10, 4);
            }
            return b;
        }

        public static void SetButtonText(Button b, string text)
        {
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t) t.text = text;
        }

        /// <summary>Primaerknopf: Verlauf Violett -> Cyan (Vertexfarben), Farbe selbst weiss.</summary>
        public static void MakePrimary(Button b)
        {
            var cb = b.colors;
            cb.normalColor = Color.white; cb.selectedColor = Color.white;
            cb.highlightedColor = new Color(0.82f, 0.86f, 0.95f); cb.pressedColor = new Color(0.65f, 0.65f, 0.75f);
            b.colors = cb;
            UiGradient.Set(b.targetGraphic, true);
            if (b.targetGraphic != null) b.targetGraphic.CrossFadeColor(cb.normalColor, 0f, true, true);
        }

        public static void SetToggled(Button b, bool on)
        {
            if (on) { MakePrimary(b); return; }
            UiGradient.Set(b.targetGraphic, false);
            var cb = b.colors;
            var baseCol = BgSoft;
            cb.normalColor = baseCol; cb.selectedColor = baseCol; cb.pressedColor = Violet;
            cb.highlightedColor = Color.Lerp(baseCol, Violet, 0.55f);
            b.colors = cb;
            if (b.targetGraphic != null) b.targetGraphic.CrossFadeColor(baseCol, 0f, true, true);
        }

        /// <summary>Senkrechte Liste mit ScrollRect. Liefert den Content-Container (VerticalLayoutGroup).</summary>
        public static RectTransform ScrollList(string name, Transform parent, out ScrollRect scroll, float spacing = 8)
        {
            var view = Rect(name, parent);
            var mask = view.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;
            var bg = view.gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.001f); // Treffer fuer Ziehen im Leerraum
            scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            var content = Rect("Content", view);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            layout.padding = new RectOffset(0, 0, 0, 0);
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = view;
            scroll.content = content;
            return content;
        }

        /// <summary>Waagrechter Regler 0..1 (Klick oder Ziehen mit gehaltenem Trigger).</summary>
        public static RectTransform Slider(Transform parent, float value, Action<float> onChange, Action onRelease)
        {
            var root = Rect("Slider", parent);
            var track = Panel("Track", root, Line, true); track.rectTransform.anchorMin = new Vector2(0, 0.5f); track.rectTransform.anchorMax = new Vector2(1, 0.5f);
            track.rectTransform.offsetMin = new Vector2(10, -4); track.rectTransform.offsetMax = new Vector2(-10, 4);
            var fillArea = Rect("FillArea", root); fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.offsetMin = new Vector2(10, -4); fillArea.offsetMax = new Vector2(-10, 4);
            var fill = Panel("Fill", fillArea, Color.white, true);
            UiGradient.Set(fill, true);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(value, 1); fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Rect("HandleArea", root); handleArea.Stretch(10, 0, 10, 0);
            var handle = Panel("Handle", handleArea, Text, true);
            handle.rectTransform.anchorMin = new Vector2(value, 0); handle.rectTransform.anchorMax = new Vector2(value, 1); handle.rectTransform.sizeDelta = new Vector2(22, 0);
            var s = root.gameObject.AddComponent<UnityEngine.UI.Slider>();
            s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight; s.minValue = 0; s.maxValue = 1;
            s.navigation = new Navigation { mode = Navigation.Mode.None };
            s.SetValueWithoutNotify(value);
            s.onValueChanged.AddListener(v => onChange?.Invoke(v));
            var et = root.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var up = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp };
            up.callback.AddListener(_ => onRelease?.Invoke());
            et.triggers.Add(up);
            // Trefferflaeche ueber die ganze Hoehe
            var hit = root.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0.001f);
            return root;
        }

        public static LayoutElement Height(this Component c, float h)
        {
            var le = c.gameObject.GetComponent<LayoutElement>(); if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = h; le.minHeight = h; le.flexibleHeight = 0; // Reihen mit ForceExpand melden sonst flexible Hoehe
            return le;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false);
                c.transform.SetParent(null, false); // sofort aus dem Layout nehmen (Destroy wirkt erst am Frame-Ende)
                if (Application.isPlaying) UnityEngine.Object.Destroy(c); else UnityEngine.Object.DestroyImmediate(c);
            }
        }

        public static HorizontalLayoutGroup Row(Transform t, float spacing, RectOffset padding = null)
        {
            var h = t.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childControlHeight = true; h.childControlWidth = true;
            h.childForceExpandHeight = true; h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            if (padding != null) h.padding = padding;
            return h;
        }

        public static LayoutElement Width(this Component c, float w, float flex = 0)
        {
            var le = c.gameObject.GetComponent<LayoutElement>(); if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.minWidth = flex > 0 ? 0 : w; le.flexibleWidth = flex;
            return le;
        }
    }
}
