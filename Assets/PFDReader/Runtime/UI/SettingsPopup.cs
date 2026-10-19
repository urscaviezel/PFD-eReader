using System;
using System.Collections.Generic;
using PFDReader.Core;
using PFDReader.Spaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PFDReader.UI
{
    /// <summary>Modales Einstellungen-Popup: links Navigation, rechts Optionen. Linke Menuetaste oder Zahnrad.</summary>
    public sealed partial class SettingsPopup
    {
        public const int PageDisplay = 0, PageEnvironment = 1, PageSound = 2, PageImport = 3, PageInfo = 9;
        public readonly Canvas Canvas;
        readonly RectTransform m_Content;
        readonly List<Button> m_Nav = new List<Button>();
        readonly List<int> m_NavPage = new List<int>();
        int m_Page;

        public event Action<bool> PassthroughChanged, DarkChanged, FollowChanged;
        public event Action<int> LanguageChanged, HandChanged;
        public event Action Recenter, Quit, HeightChanged;
        public EnvironmentManager Env;
        public Audio.SoundManager Sound;
        /// <summary>Ein Buch wurde nach eBooks kopiert (Pfad).</summary>
        public event Action<string> Imported;
        VerticalLayoutGroup m_ContentLayout;
        ScrollRect m_ActiveScroll;
        VirtualKeyboard m_Keyboard;

        public SettingsPopup(Transform parent, Camera eventCamera)
        {
            Canvas = UiKit.CreateCanvas("Settings", parent, new Vector2(860, 540), eventCamera);
            Canvas.sortingOrder = 30;
            var bg = UiKit.Panel("Bg", Canvas.transform, UiKit.Bg);
            bg.rectTransform.Stretch();

            var title = UiKit.Label("Title", bg.transform, Loc.T("Einstellungen"), 34, UiKit.Text);
            title.fontStyle = FontStyles.Bold;
            title.rectTransform.anchorMin = new Vector2(0, 1); title.rectTransform.anchorMax = new Vector2(1, 1);
            title.rectTransform.pivot = new Vector2(0.5f, 1);
            title.rectTransform.offsetMin = new Vector2(30, -74); title.rectTransform.offsetMax = new Vector2(-100, -20);
            var close = UiIcons.IconButton("close", bg.transform, 44, Hide);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(1, 1); crt.anchoredPosition = new Vector2(-20, -18);

            var nav = UiKit.Rect("Nav", bg.transform);
            nav.anchorMin = new Vector2(0, 0); nav.anchorMax = new Vector2(0, 1); nav.pivot = new Vector2(0, 1);
            nav.offsetMin = new Vector2(20, 20); nav.offsetMax = new Vector2(280, -88);
            var v = nav.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 3; v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            AddNav(nav, "display", "Anzeige", 0);
            AddNav(nav, "room", "Umgebung", 1);
            AddNav(nav, "sound", "Sound", 2);
            AddNav(nav, "import", "Import", 3);
            AddNav(nav, "follow", "Follow Me", 4);
            AddNav(nav, "globe", "Sprache", 5);
            AddNav(nav, "controller", "Controller", 6);
            AddNav(nav, "center", "Fenster zentrieren", 7);
            AddNav(nav, "info", "App-Info", PageInfo);
            AddNav(nav, "power", "App beenden", 8);

            var sep = UiKit.Panel("Sep", bg.transform, UiKit.Line, true);
            sep.rectTransform.anchorMin = new Vector2(0, 0); sep.rectTransform.anchorMax = new Vector2(0, 1);
            sep.rectTransform.offsetMin = new Vector2(296, 24); sep.rectTransform.offsetMax = new Vector2(298, -92);

            m_Content = UiKit.Rect("Content", bg.transform);
            m_Content.Stretch(318, 92, 30, 24);
            var cv = m_ContentLayout = m_Content.gameObject.AddComponent<VerticalLayoutGroup>();
            cv.spacing = 14; cv.childControlHeight = true; cv.childControlWidth = true; cv.childForceExpandHeight = false; cv.childForceExpandWidth = true;

            Canvas.gameObject.SetActive(false);
        }

        void AddNav(Transform nav, string icon, string de, int page)
        {
            var b = UiIcons.IconTextButton(icon, Loc.T(de), nav, 40, 19, () => ShowPage(page));
            m_Nav.Add(b); m_NavPage.Add(page);
        }

        public bool Visible => Canvas.gameObject.activeSelf;
        public void Show() { Canvas.gameObject.SetActive(true); ShowPage(m_Page); }
        public void Show(int page) { Canvas.gameObject.SetActive(true); ShowPage(page); }
        public void Hide() => Canvas.gameObject.SetActive(false);
        public void Toggle() { if (Visible) Hide(); else Show(); }

        void ShowPage(int page)
        {
            if (page != m_Page) { m_ImportMode = ImportMode.Form; m_SoundBrowse = false; }
            m_Page = page;
            for (int i = 0; i < m_Nav.Count; i++) UiKit.SetToggled(m_Nav[i], m_NavPage[i] == page);
            UiKit.Clear(m_Content);
            m_ActiveScroll = null; m_Keyboard = null; m_Status = null;
            m_ContentLayout.spacing = page == PageSound || page == PageImport ? 8 : 14;
            var s = Storage.Settings;
            switch (page)
            {
                case 0:
                    Heading(Loc.T("Passthrough"));
                    UiIcons.Segmented(m_Content, new[] { Loc.T("Ein"), Loc.T("Aus") }, s.passthrough ? 0 : 1, i => PassthroughChanged?.Invoke(i == 0));
                    Heading(Loc.T("Lesemodus"));
                    UiIcons.Segmented(m_Content, new[] { Loc.T("Hell"), Loc.T("Dunkel") }, s.dark ? 1 : 0, i => DarkChanged?.Invoke(i == 1));
                    Heading(Loc.T("Foveated Rendering"));
                    UiIcons.Segmented(m_Content, new[] { Loc.T("Aus"), Loc.T("Niedrig"), Loc.T("Mittel"), Loc.T("Hoch") }, Mathf.Clamp(s.foveation, 0, 3), i => { s.foveation = i; Storage.SaveSettings(); Core.App.ApplyFoveation(); }, 34, 100);
                    Text(Loc.T("Rand des Sichtfelds mit weniger Auflösung berechnen – spart Grafikleistung, der Rand wird etwas unschärfer."));
                    break;
                case 1:
                    BuildEnvironmentPage();
                    break;
                case 2:
                    BuildSoundPage();
                    break;
                case 3:
                    BuildImportPage();
                    break;
                case 4:
                    Heading(Loc.T("Follow Me"));
                    UiIcons.Segmented(m_Content, new[] { Loc.T("Ein"), Loc.T("Aus") }, s.follow ? 0 : 1, i => FollowChanged?.Invoke(i == 0));
                    Text(Loc.T("Das Buch folgt sanft deiner Blickrichtung. Greifen und Verschieben bleibt möglich; der neue Platz wird übernommen."));
                    break;
                case 5:
                    Heading(Loc.T("Sprache"));
                    UiIcons.Segmented(m_Content, new[] { "Deutsch", "English", "Français" }, Loc.Language, i => LanguageChanged?.Invoke(i), 34, 110);
                    break;
                case 6:
                    Heading(Loc.T("Bevorzugte Hand"));
                    UiIcons.Segmented(m_Content, new[] { Loc.T("Links"), Loc.T("Rechts") }, s.preferredHand, i => HandChanged?.Invoke(i));
                    Text(Loc.T("Diese Hand zeigt im Ruhezustand. Beide Controller bleiben nutzbar; die Menütaste ist immer links."));
                    break;
                case 7:
                    Text(Loc.T("Richtet das Buch auf Augenhöhe vor dir aus. Distanz und Grösse bleiben erhalten."));
                    Action(Loc.T("Fenster zentrieren"), "center", () => { Recenter?.Invoke(); Hide(); });
                    break;
                case 8:
                    Text(Loc.T("Speichert die Leseposition und beendet die App."));
                    Action(Loc.T("App beenden"), "power", () => Quit?.Invoke());
                    break;
                case PageInfo:
                    BuildInfoPage();
                    break;
            }
        }

        // ---------------------------------------------------------------- Umgebung
        void BuildEnvironmentPage()
        {
            Heading(Loc.T("Umgebungshelligkeit"));
            var sl = UiKit.Slider(m_Content, Storage.Settings.environmentDim, v => Env?.SetDim(v), () => Storage.SaveSettings());
            sl.Height(34);

            if (Env != null && Env.CurrentRoom != null)
            {
                if (Env.HasTimeOfDay)
                {
                    var tr = UiKit.Rect("TimeRow", m_Content); tr.Height(34); UiKit.Row(tr, 10);
                    var tl = UiKit.Label("L", tr, Loc.T("Tageszeit"), 18, UiKit.TextDim); tl.Width(150);
                    UiIcons.Segmented(tr, new[] { Loc.T("Tag"), Loc.T("Nacht") }, Storage.Settings.timeOfDay, i => Env.SetTimeOfDay(i));
                }
                var hr = UiKit.Rect("HeightRow", m_Content); hr.Height(34); UiKit.Row(hr, 10);
                var hl = UiKit.Label("L", hr, Loc.T("Körpergrösse"), 18, UiKit.TextDim); hl.Width(150);
                const float hMin = 1.40f, hMax = 2.10f;
                var val = UiKit.Label("V", hr, "", 18, UiKit.Text, TextAlignmentOptions.MidlineRight);
                void ShowH(float m) => val.text = Mathf.RoundToInt(m * 100) + " cm";
                var hs = UiKit.Slider(hr, Mathf.InverseLerp(hMin, hMax, Storage.Settings.playerHeight), v =>
                {
                    Storage.Settings.playerHeight = Mathf.Round(Mathf.Lerp(hMin, hMax, v) * 100f) / 100f;
                    ShowH(Storage.Settings.playerHeight); HeightChanged?.Invoke();
                }, () => Storage.SaveSettings());
                hs.Width(0, 1); hs.Height(34);
                val.transform.SetAsLastSibling(); val.Width(80);
                ShowH(Storage.Settings.playerHeight);

                // Drehung mit dem rechten Stick: gleitend oder 15/30/45/60° Schritte
                var rr = UiKit.Rect("TurnRow", m_Content); rr.Height(34); UiKit.Row(rr, 10);
                var rl = UiKit.Label("L", rr, Loc.T("Drehung"), 18, UiKit.TextDim); rl.Width(150);
                var rv = UiKit.Label("V", rr, "", 18, UiKit.Text, TextAlignmentOptions.MidlineRight);
                void ShowT(int d) => rv.text = d <= 0 ? Loc.T("Gleitend") : d + "°";
                var rs = UiKit.Slider(rr, Mathf.Clamp(Storage.Settings.turnAngle, 0, 60) / 60f, v =>
                {
                    int idx = Mathf.RoundToInt(v * 4f);
                    Storage.Settings.turnAngle = idx * 15; ShowT(idx * 15);
                }, () => Storage.SaveSettings());
                rs.Width(0, 1); rs.Height(34);
                rv.transform.SetAsLastSibling(); rv.Width(80);
                ShowT(Storage.Settings.turnAngle);
            }

            var listRoot = UiKit.Rect("EnvList", m_Content);
            var le = listRoot.gameObject.AddComponent<LayoutElement>(); le.flexibleHeight = 1; le.minHeight = 200;
            var content = UiKit.ScrollList("Scroll", listRoot, out var scroll, 8);
            ((RectTransform)scroll.transform).Stretch();
            // Raster statt Liste
            UnityEngine.Object.DestroyImmediate(content.GetComponent<VerticalLayoutGroup>());
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(150, 118); grid.spacing = new Vector2(10, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
            m_ActiveScroll = scroll;

            string cur = Env != null ? Env.Current : "passthrough";
            Card(content, "passthrough", Loc.T("Passthrough"), null, "passthrough", cur);
            Card(content, "dark", Loc.T("Dunkel (ohne Raum)"), null, "moon", cur);
            if (Env != null)
            {
                foreach (var r in Env.Rooms) Card(content, "room:" + r.id, r.title, r.preview, "room", cur);
                var panos = Env.ScanPanoramas();
                foreach (var pn in panos)
                {
                    var card = Card(content, "pano:" + pn.path, pn.title, null, "globe", cur);
                    var cardRef = card;
                    Env.RequestThumbnail(pn.path, tex => { if (tex != null && cardRef != null) SetCardPreview(cardRef, tex); });
                }
                if (panos.Count == 0)
                {
                    var hint = UiKit.Label("Hint", content, Loc.T("Keine Panoramen gefunden. Bilder (2:1 mono oder 1:1 Stereo oben/unten) nach Pictures/Envoirements kopieren."), 14, UiKit.TextDim, TextAlignmentOptions.TopLeft);
                    hint.textWrappingMode = TextWrappingModes.Normal; hint.overflowMode = TextOverflowModes.Overflow;
                }
                if (cur.StartsWith("pano:"))
                {
                    var row = UiKit.Rect("SwapRow", m_Content); row.Height(34); UiKit.Row(row, 10);
                    var l = UiKit.Label("L", row, Loc.T("Augen tauschen"), 18, UiKit.TextDim); l.Width(160);
                    UiIcons.Segmented(row, new[] { Loc.T("Aus"), Loc.T("Ein") }, Storage.Settings.panoramaSwapEyes ? 1 : 0, i => Env.SetSwapEyes(i == 1));
                }
            }
        }


        static void SetCardPreview(Button card, Texture2D tex)
        {
            var img = card.transform.Find("Preview");
            if (img == null) return;
            var ic = img.Find("Icon"); if (ic != null) UnityEngine.Object.Destroy(ic.gameObject);
            var raw = img.GetComponent<RawImage>(); if (raw == null) raw = img.gameObject.AddComponent<RawImage>();
            raw.texture = tex; raw.raycastTarget = false;
        }

        Button Card(Transform parent, string env, string title, Texture2D preview, string icon, string current)
        {
            var b = UiKit.Button("Env", parent, null, 0, () => { Env?.Apply(env); });
            if (env == current) UiKit.SetToggled(b, true);
            var img = UiKit.Rect("Preview", b.transform);
            img.anchorMin = new Vector2(0, 0.3f); img.anchorMax = new Vector2(1, 1);
            img.offsetMin = new Vector2(6, 0); img.offsetMax = new Vector2(-6, -6);
            if (preview != null)
            {
                var raw = img.gameObject.AddComponent<RawImage>(); raw.texture = preview; raw.raycastTarget = false;
            }
            else
            {
                var ic = UiKit.Rect("Icon", img).gameObject.AddComponent<Image>();
                ic.sprite = UiIcons.Get(icon); ic.preserveAspect = true; ic.color = UiKit.TextDim; ic.raycastTarget = false;
                ((RectTransform)ic.transform).Stretch(40, 10, 40, 10);
            }
            var l = UiKit.Label("Title", b.transform, title, 15, UiKit.Text, TextAlignmentOptions.Center);
            l.rectTransform.anchorMin = new Vector2(0, 0); l.rectTransform.anchorMax = new Vector2(1, 0.3f);
            l.rectTransform.offsetMin = new Vector2(4, 2); l.rectTransform.offsetMax = new Vector2(-4, 0);
            return b;
        }

        /// <summary>Stick-Scrollen in der Umgebungsliste.</summary>
        public void ScrollBy(float rows)
        {
            var sc = m_ActiveScroll;
            if (!Visible || sc == null) return;
            var content = sc.content; float view = ((RectTransform)sc.transform).rect.height;
            if (content.rect.height <= view) return;
            sc.verticalNormalizedPosition = Mathf.Clamp01(sc.verticalNormalizedPosition + rows * 128f / (content.rect.height - view));
        }

        public void Refresh() { if (Visible && m_Keyboard == null) ShowPage(m_Page); }
        public void RefreshPage(int page) { if (m_Page == page) Refresh(); }

        /// <summary>Jedes Frame (aus App.Update): Tastatur-Cursor, Import-/Suchfortschritt.</summary>
        public void Tick()
        {
            if (!Visible) return;
            m_Keyboard?.Tick();
            if (m_Page == PageImport) TickImport();
        }

        /// <summary>Bildschirmtastatur anstelle der Seite zeigen; danach Seite neu aufbauen.</summary>
        void EditText(string label, string value, bool password, Action<string> ok)
        {
            UiKit.Clear(m_Content);
            m_ActiveScroll = null; m_Status = null;
            m_Keyboard = new VirtualKeyboard(m_Content, label, value, password,
                t => { m_Keyboard = null; ok(t); ShowPage(m_Page); },
                () => { m_Keyboard = null; ShowPage(m_Page); });
        }

        void Heading(string t)
        {
            var l = UiKit.Label("H", m_Content, t, 24, UiKit.TextDim);
            l.Height(34);
        }

        void Text(string t)
        {
            var l = UiKit.Label("T", m_Content, t, 18, UiKit.TextDim, TextAlignmentOptions.TopLeft);
            l.textWrappingMode = TextWrappingModes.Normal;
            l.overflowMode = TextOverflowModes.Overflow;
            l.Height(78);
        }

        void Action(string label, string icon, UnityEngine.Events.UnityAction a)
        {
            var row = UiKit.Rect("Row", m_Content);
            row.Height(36);
            UiKit.Row(row, 0);
            var b = UiIcons.IconTextButton(icon, label, row, 36, 17, a);
            b.Width(240);
        }
    }
}
