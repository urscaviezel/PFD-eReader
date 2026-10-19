using System.IO;
using System.Linq;
using PFDReader.Audio;
using PFDReader.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PFDReader.UI
{
    /// <summary>Einstellungsseite "Sound": Hauptschalter + Master, Ambient-Klaenge, Musik (Ordner/Playlist).</summary>
    public sealed partial class SettingsPopup
    {
        int m_SoundTab;            // 0 Ambient, 1 Musik
        bool m_SoundBrowse;        // Dateiauswahl aktiv
        string m_SoundDir;

        RectTransform RowRect(string name, float h, float spacing = 8)
        {
            var r = UiKit.Rect(name, m_Content); r.Height(h);
            UiKit.Row(r, spacing);
            return r;
        }

        TextMeshProUGUI SmallLabel(Transform parent, string text, float width, float size = 17, Color? color = null)
        {
            var l = UiKit.Label("L", parent, text, size, color ?? UiKit.TextDim);
            l.overflowMode = TextOverflowModes.Ellipsis; l.textWrappingMode = TextWrappingModes.NoWrap;
            if (width > 0) l.Width(width); else l.Width(0, 1);
            return l;
        }

        RectTransform SliderIn(Transform parent, float value, System.Action<float> change, System.Action release, float width)
        {
            var sl = UiKit.Slider(parent, value, change, release);
            if (width > 0) sl.Width(width); else sl.Width(0, 1);
            return sl;
        }

        ScrollRect List(float minHeight, out RectTransform content, float spacing = 6)
        {
            var root = UiKit.Rect("ListRoot", m_Content);
            var le = root.gameObject.AddComponent<LayoutElement>(); le.flexibleHeight = 1; le.minHeight = minHeight;
            content = UiKit.ScrollList("Scroll", root, out var scroll, spacing);
            ((RectTransform)scroll.transform).Stretch();
            m_ActiveScroll = scroll;
            return scroll;
        }

        void BuildSoundPage()
        {
            var S = Storage.Settings;
            if (Sound == null) { Text(Loc.T("Sound nicht verfügbar.")); return; }

            // Hauptschalter + Master-Lautstaerke
            var m = RowRect("Master", 36);
            var sw = UiIcons.IconButton(S.soundOn ? "sound" : "close", m, 36, () => { Sound.SetSoundOn(!S.soundOn); ShowPage(m_Page); });
            UiKit.SetToggled(sw, S.soundOn);
            SmallLabel(m, Loc.T("Gesamt"), 70);
            var mv = SmallLabel(m, Mathf.RoundToInt(S.masterVolume * 100) + " %", 56, 16, UiKit.Text);
            SliderIn(m, S.masterVolume, v => { Sound.SetMaster(v, false); mv.text = Mathf.RoundToInt(v * 100) + " %"; }, () => Sound.SetMaster(S.masterVolume, true), 0);
            mv.transform.SetAsLastSibling();

            var tabs = RowRect("Tabs", 34);
            UiIcons.Segmented(tabs, new[] { Loc.T("Ambient"), Loc.T("Musik") }, m_SoundTab, i => { m_SoundTab = i; m_SoundBrowse = false; ShowPage(m_Page); }, 34, 130);

            if (m_SoundBrowse) { BuildSoundBrowser(); return; }
            if (m_SoundTab == 0) BuildAmbient(); else BuildMusic();
            if (!string.IsNullOrEmpty(Sound.LastError)) SmallLabel(m_Content, Sound.LastError, 0, 15, UiKit.Danger).Height(20);
        }

        void BuildAmbient()
        {
            var S = Storage.Settings;
            var c = RowRect("Ctl", 36);
            var play = UiIcons.IconButton("play", c, 36, () => { Sound.PlayAmbient(true); ShowPage(m_Page); }); UiKit.SetToggled(play, S.ambientPlaying);
            var stop = UiIcons.IconButton("stop", c, 36, () => { Sound.PlayAmbient(false); ShowPage(m_Page); }); UiKit.SetToggled(stop, !S.ambientPlaying);
            SmallLabel(c, "", 0);
            UiIcons.IconTextButton("plus", Loc.T("Hinzufügen"), c, 36, 16, () => { m_SoundBrowse = true; m_SoundDir = SoundManager.AmbientDir; ShowPage(m_Page); }).Width(170);

            List(140, out var list);
            if (S.ambient.Count == 0)
            {
                var hint = UiKit.Label("Hint", list, Loc.T("Noch keine Klänge. Audiodateien in Music/Ambient Sounds ablegen und mit „Hinzufügen“ auswählen."), 15, UiKit.TextDim, TextAlignmentOptions.TopLeft);
                hint.textWrappingMode = TextWrappingModes.Normal; hint.Height(60);
            }
            foreach (var a in S.ambient.ToList())
            {
                var path = a.path;
                var r = UiKit.Rect("Amb", list); r.Height(38); UiKit.Row(r, 6);
                UiIcons.IconButton(a.on ? "box_checked" : "box", r, 36, () => { Sound.SetAmbientOn(path, !a.on); ShowPage(m_Page); });
                SmallLabel(r, Path.GetFileNameWithoutExtension(path), 0, 16, File.Exists(path) ? UiKit.Text : UiKit.Danger);
                SliderIn(r, a.volume, v => Sound.SetAmbientVolume(path, v, false), () => Sound.SetAmbientVolume(path, a.volume, true), 130);
                UiIcons.IconButton("trash", r, 36, () => { Sound.RemoveAmbient(path); ShowPage(m_Page); });
            }
        }

        void BuildMusic()
        {
            var S = Storage.Settings;
            var src = RowRect("Src", 36);
            SmallLabel(src, string.IsNullOrEmpty(S.musicSource) ? Loc.T("Kein Musikordner gewählt") : Path.GetFileName(S.musicSource.TrimEnd('/')), 0, 16, UiKit.Text);
            UiIcons.IconTextButton("folder", Loc.T("Auswählen"), src, 36, 16, () =>
            {
                m_SoundBrowse = true;
                m_SoundDir = Directory.Exists(S.musicSource) ? S.musicSource : (File.Exists(S.musicSource) ? Path.GetDirectoryName(S.musicSource) : SoundManager.MusicDir);
                ShowPage(m_Page);
            }).Width(160);

            var now = RowRect("Now", 30);
            var n = Sound.Playlist.Count;
            SmallLabel(now, n == 0 ? Loc.T("Keine Audiodateien gefunden.") : $"{Sound.MusicIndex + 1} / {n}   {Sound.CurrentTrack}", 0, 17, UiKit.Text);

            var c = RowRect("Ctl", 44, 10);
            c.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            UiIcons.IconButton("skip_prev", c, 44, () => { Sound.Prev(); ShowPage(m_Page); });
            var play = UiIcons.IconButton("play", c, 44, () => { Sound.PlayMusic(); ShowPage(m_Page); }); UiKit.SetToggled(play, Sound.MusicPlaying && !Sound.MusicPaused);
            var pause = UiIcons.IconButton("pause", c, 44, () => { Sound.PauseMusic(); ShowPage(m_Page); }); UiKit.SetToggled(pause, Sound.MusicPaused);
            var stop = UiIcons.IconButton("stop", c, 44, () => { Sound.StopMusic(); ShowPage(m_Page); }); UiKit.SetToggled(stop, !Sound.MusicPlaying);
            UiIcons.IconButton("skip_next", c, 44, () => { Sound.Next(); ShowPage(m_Page); });

            var v = RowRect("Vol", 36);
            SmallLabel(v, Loc.T("Lautstärke"), 110);
            var vl = SmallLabel(v, Mathf.RoundToInt(S.musicVolume * 100) + " %", 56, 16, UiKit.Text);
            SliderIn(v, S.musicVolume, x => { Sound.SetMusicVolume(x, false); vl.text = Mathf.RoundToInt(x * 100) + " %"; }, () => Sound.SetMusicVolume(S.musicVolume, true), 0);
            vl.transform.SetAsLastSibling();

            if (n > 0)
            {
                List(80, out var list, 4);
                for (int i = 0; i < n; i++)
                {
                    int idx = i;
                    var b = UiKit.Button("Track", list, null, 0, () => { Storage.Settings.musicIndex = idx; if (Sound.MusicPlaying) Sound.StopMusic(); Sound.PlayMusic(); ShowPage(m_Page); });
                    b.Height(30);
                    if (i == Sound.MusicIndex) UiKit.SetToggled(b, true);
                    var l = UiKit.Label("T", b.transform, (i + 1) + ".  " + Path.GetFileNameWithoutExtension(Sound.Playlist[i]), 15, UiKit.Text);
                    l.rectTransform.Stretch(10, 0, 10, 0); l.overflowMode = TextOverflowModes.Ellipsis;
                }
            }
        }

        /// <summary>Datei-/Ordnerauswahl im Speicher des Headsets (Ambient: Audiodatei, Musik: Ordner oder Playlist).</summary>
        void BuildSoundBrowser()
        {
            bool music = m_SoundTab == 1;
            var root = Storage.ExternalRoot;
            if (string.IsNullOrEmpty(m_SoundDir) || !Directory.Exists(m_SoundDir)) m_SoundDir = Directory.Exists(SoundManager.MusicDir) ? SoundManager.MusicDir : root;

            var nav = RowRect("Nav", 36);
            UiIcons.IconButton("back", nav, 36, () => { m_SoundBrowse = false; ShowPage(m_Page); });
            UiIcons.IconButton("up", nav, 36, () => { var p = Path.GetDirectoryName(m_SoundDir); if (p != null && p.Length >= root.Length) { m_SoundDir = p; ShowPage(m_Page); } });
            SmallLabel(nav, Display(m_SoundDir), 0, 15);
            if (music) UiKit.Button("Pick", nav, Loc.T("Diesen Ordner"), 15, () => { Sound.LoadPlaylist(m_SoundDir); m_SoundBrowse = false; ShowPage(m_Page); }, UiKit.Violet).Width(150);

            List(150, out var list, 4);
            string[] dirs = new string[0], files = new string[0];
            try
            {
                dirs = Directory.GetDirectories(m_SoundDir).Where(d => !Path.GetFileName(d).StartsWith(".")).OrderBy(d => Path.GetFileName(d), Books.NaturalComparer.Instance).ToArray();
                files = Directory.GetFiles(m_SoundDir).Where(f => SoundManager.IsAudio(f) || (music && SoundManager.IsPlaylist(f))).OrderBy(f => Path.GetFileName(f), Books.NaturalComparer.Instance).ToArray();
            }
            catch (System.Exception e) { SmallLabel(list, e.Message, 0, 15, UiKit.Danger).Height(24); }
            foreach (var d in dirs)
            {
                var dir = d;
                Entry(list, "folder", Path.GetFileName(d), null, () => { m_SoundDir = dir; ShowPage(m_Page); });
            }
            foreach (var f in files)
            {
                var file = f;
                if (music && !SoundManager.IsPlaylist(f)) { Entry(list, "music", Path.GetFileName(f), null, null); continue; }
                bool added = !music && Storage.Settings.ambient.Any(a => a.path == file);
                Entry(list, music ? "file" : (added ? "check" : "music"), Path.GetFileName(f), added ? Loc.T("hinzugefügt") : null, () =>
                {
                    if (music) { Sound.LoadPlaylist(file); m_SoundBrowse = false; }
                    else Sound.AddAmbient(file);
                    ShowPage(m_Page);
                });
            }
            if (dirs.Length == 0 && files.Length == 0) SmallLabel(list, Loc.T("Keine Audiodateien gefunden."), 0, 15).Height(24);
        }

        static string Display(string path)
        {
            var root = Storage.ExternalRoot;
            if (path.StartsWith(root)) path = Loc.T("Speicher") + path.Substring(root.Length);
            return path;
        }

        /// <summary>Listenzeile mit Icon, Name und optionalem Zusatz rechts.</summary>
        Button Entry(Transform list, string icon, string name, string extra, UnityEngine.Events.UnityAction click)
        {
            var b = UiKit.Button("Entry", list, null, 0, click);
            b.Height(34);
            b.interactable = click != null;
            var img = UiKit.Rect("Icon", b.transform).gameObject.AddComponent<Image>();
            img.sprite = UiIcons.Get(icon); img.preserveAspect = true; img.raycastTarget = false; img.color = UiKit.TextDim;
            var irt = (RectTransform)img.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0, 0.5f); irt.pivot = new Vector2(0, 0.5f); irt.anchoredPosition = new Vector2(10, 0); irt.sizeDelta = new Vector2(20, 20);
            var l = UiKit.Label("T", b.transform, name, 15, click != null ? UiKit.Text : UiKit.TextDim);
            l.rectTransform.Stretch(40, 0, extra != null ? 110 : 10, 0); l.raycastTarget = false; ScrollText.Make(l);   // lange Namen laufen durch
            if (extra != null)
            {
                var e = UiKit.Label("X", b.transform, extra, 13, UiKit.TextDim, TextAlignmentOptions.MidlineRight);
                e.rectTransform.anchorMin = new Vector2(1, 0); e.rectTransform.anchorMax = new Vector2(1, 1); e.rectTransform.pivot = new Vector2(1, 0.5f);
                e.rectTransform.anchoredPosition = new Vector2(-10, 0); e.rectTransform.sizeDelta = new Vector2(100, 0);
            }
            return b;
        }
    }
}
