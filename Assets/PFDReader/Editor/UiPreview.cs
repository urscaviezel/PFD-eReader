using System;
using System.IO;
using PFDReader.Core;
using PFDReader.UI;
using TMPro;
using UnityEngine;

namespace PFDReader.EditorTools
{
    /// <summary>Rendert Oberflaechen im Editor als PNG (fuer die Sichtkontrolle durch Claude), ohne Headset.</summary>
    public static class UiPreview
    {
        static int s_Lang = -1;

        /// <summary>Bilder fuer die Anleitung: Menues in DE/EN/FR nach Builds/guide/.</summary>
        public static string SaveScenes() { UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes(); return Dirty(); }
        public static string Dirty() => "Main dirty=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;

        public static string GuideShots()
        {
            var sb = new System.Text.StringBuilder();
            string[] codes = { "de", "en", "fr" };
            Directory.CreateDirectory("Builds/guide");
            for (int lang = 0; lang < 3; lang++)
            {
                s_Lang = lang;
                foreach (var (which, page) in new[] { ("library", (string)null), ("toolbar", null), ("settings", "0"), ("settings", "1"), ("settings", "2"), ("settings", "3"), ("settings", "6") })
                {
                    var src = Render(which, page);
                    var dst = Path.GetFullPath($"Builds/guide/{which}{page}-{codes[lang]}.png");
                    File.Copy(src, dst, true); sb.AppendLine(dst);
                }
            }
            s_Lang = -1;
            return sb.ToString();
        }

        public static string Render(string which, string page)
        {
            Storage.Load();
            if (s_Lang >= 0) Storage.Settings.language = s_Lang;
            UiKit.Font = Resources.Load<TMP_FontAsset>("Fonts/Inter SDF");
            // eigene Vorschauszene: die offene Szene wird nicht veraendert (kein "Scene modified"-Dialog beim Build)
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags("UiPreviewRoot", HideFlags.HideAndDontSave);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            root.transform.position = new Vector3(0, 200, 0);
            var camGo = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags("UiPreviewCam", HideFlags.HideAndDontSave);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, preview);
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.scene = preview;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.25f, 0.27f, 0.3f, 1);
                cam.orthographic = true;
                Canvas canvas;
                switch (which)
                {
                    case "library":
                    {
                        var dir = Path.GetFullPath("Builds/guide/books"); Directory.CreateDirectory(dir);
                        var demo = new[] { ("Der Zauberberg.epub", 812345), ("Die Verwandlung.epub", 214000), ("Reiseführer Japan.pdf", 18400000), ("Space Explorers - Band 1.cbz", 46200000), ("Notizen.txt", 12000), ("Die unendliche Geschichte - Sonderausgabe mit Illustrationen und Nachwort (2. Auflage).epub", 2400000), ("Sherlock Holmes.epub", 1530000) };
                        foreach (var (n, sz) in demo) { var f = Path.Combine(dir, n); if (!File.Exists(f)) File.WriteAllBytes(f, new byte[sz]); }
                        Storage.BooksDirOverride = dir;
                        var l = new LibraryPanel(root.transform, cam); canvas = l.Canvas; l.Visible = true;
                        l.SetStatus(demo.Length + " " + Loc.T("Bücher gefunden") + "  ·  /storage/emulated/0/eBooks");
                        Storage.BooksDirOverride = null;
                        break;
                    }
                    case "toolbar": { var t = new Toolbar(root.transform, cam); canvas = t.Canvas; canvas.GetComponent<CanvasGroup>().alpha = 1; t.SetLocation("Beispieltitel  ·  Kapitel 3  ·  42 %", 0.42f); break; }
                    default:
                    {
                        var s = new SettingsPopup(root.transform, cam); canvas = s.Canvas;
                        var sgo = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags("PreviewSound", HideFlags.HideAndDontSave); sgo.transform.SetParent(root.transform);
                        s.Sound = sgo.AddComponent<PFDReader.Audio.SoundManager>();
                        if (Storage.Settings.ambient.Count == 0) { Storage.Settings.ambient.Add(new AmbientEntry { path = "/x/Regen am Fenster.mp3", volume = 0.6f }); Storage.Settings.ambient.Add(new AmbientEntry { path = "/x/Kaminfeuer.ogg", volume = 0.4f, on = false }); }
                        s.Show();
                        if (int.TryParse(page, out var p)) s.GetType().GetMethod("ShowPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(s, new object[] { p });
                        break;
                    }
                }
                var rt = (RectTransform)canvas.transform;
                Canvas.ForceUpdateCanvases();
                foreach (var g in canvas.GetComponentsInChildren<UnityEngine.UI.LayoutGroup>(true))
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)g.transform);
                Canvas.ForceUpdateCanvases();
                var size = rt.sizeDelta * rt.localScale.x;
                cam.transform.position = rt.position + new Vector3(0, 0, -1);
                cam.transform.rotation = Quaternion.identity;
                cam.orthographicSize = size.y * 0.5f * 1.15f;
                int h = 900, w = Mathf.RoundToInt(h * size.x / size.y * 1.0f / 1.0f);
                cam.aspect = size.x / size.y;
                var tex = new RenderTexture(w, h, 24);
                cam.targetTexture = tex;
                cam.Render();
                RenderTexture.active = tex;
                var img = new Texture2D(w, h, TextureFormat.RGB24, false);
                img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                img.Apply();
                RenderTexture.active = null;
                Directory.CreateDirectory("Builds");
                var path = Path.GetFullPath("Builds/preview-" + which + (page ?? "") + ".png");
                File.WriteAllBytes(path, img.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(img);
                tex.Release(); UnityEngine.Object.DestroyImmediate(tex);
                return path;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
        }
    }
}
