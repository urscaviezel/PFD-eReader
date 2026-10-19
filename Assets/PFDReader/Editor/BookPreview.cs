using System.IO;
using PFDReader.Rendering;
using PFDReader.View;
using TMPro;
using UnityEngine;

namespace PFDReader.EditorTools
{
    /// <summary>Rendert das 3D-Buch im Editor (offen / zu / Ruecken) als JPG nach Builds/book-&lt;mode&gt;.jpg.</summary>
    public static class BookPreview
    {
        public static string Render(string mode)
        {
            var root = new GameObject("BookPreviewRoot");
            var camGo = new GameObject("BookPreviewCam");
            RenderTexture cover = null, spine = null, rt = null;
            try
            {
                var font = Resources.Load<TMP_FontAsset>("Fonts/Lora SDF");
                var book = new GameObject("Book").AddComponent<BookView>();
                book.transform.SetParent(root.transform, false);
                book.EditorRefresh();
                cover = CoverRenderer.Render("Der Zauberberg", "Thomas Mann", font);
                spine = CoverRenderer.RenderSpine("Der Zauberberg", "Thomas Mann", font);
                book.SetCoverTexture(cover); book.SetSpineTexture(spine);
                book.SetStatic(Texture2D.whiteTexture, Texture2D.whiteTexture);
                book.SetProgress(0.35f);
                bool closed = mode != "open";
                book.SetClosed(closed, true);
                book.EditorRefresh();
                book.GetType().GetMethod("LayoutBlocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(book, new object[] { 0.35f });

                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.32f, 0.33f, 0.36f);
                cam.fieldOfView = 30; cam.nearClipPlane = 0.02f; cam.farClipPlane = 10;
                Vector3 target = Vector3.zero;
                switch (mode)
                {
                    case "spine": camGo.transform.position = new Vector3(-0.75f, 0.12f, -0.35f); break;
                    case "angle": camGo.transform.position = new Vector3(-0.45f, 0.35f, -0.8f); break;
                    case "closed": camGo.transform.position = new Vector3(0, 0, -1.0f); break;
                    default: camGo.transform.position = new Vector3(0.0f, 0.15f, -1.2f); break;
                }
                var off = new Vector3(0, 200, 0);   // weg vom Kamera-Rig der offenen Szene
                root.transform.position += off; camGo.transform.position += off; target += off;
                camGo.transform.LookAt(target);
                int w = 1200, h = 900;
                rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                RenderTexture.active = null;
                Directory.CreateDirectory("Builds");
                var outPath = Path.GetFullPath($"Builds/book-{mode}.jpg");
                File.WriteAllBytes(outPath, tex.EncodeToJPG(88));
                Object.DestroyImmediate(tex);
                return outPath;
            }
            finally
            {
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(root);
                if (cover) Object.DestroyImmediate(cover); if (spine) Object.DestroyImmediate(spine);
                if (rt) { rt.Release(); Object.DestroyImmediate(rt); }
            }
        }
    }
}
