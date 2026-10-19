using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace PFDReader.Rendering
{
    /// <summary>
    /// Rastert die Goldpraegung des Einbands (Titel, Autor) bzw. des Buchruckens mit TextMeshPro in eine RenderTexture.
    /// Hintergrund transparent: der Alphakanal ist die Praegemaske fuer den Leder-Shader.
    /// </summary>
    public static class CoverRenderer
    {
        const int W = 1024, H = 1365;
        static readonly Color Gold = new Color(0.86f, 0.72f, 0.42f);

        const int SW = 1024, SH = 96;

        static RenderTexture Target(RenderTexture reuse, int w, int h, string name)
        {
            if (reuse != null && reuse.width == w && reuse.height == h) return reuse;
            var rt = new RenderTexture(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0) { sRGB = true, useMipMap = true, autoGenerateMips = true })
            { name = name, filterMode = FilterMode.Trilinear, anisoLevel = 4, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            return rt;
        }

        /// <summary>Buchruecken: Titel (fett) und Autor in einer Zeile, Textlaenge = Buchhoehe.</summary>
        public static RenderTexture RenderSpine(string title, string author, TMP_FontAsset font, RenderTexture reuse = null)
        {
            var rt = Target(reuse, SW, SH, "PFD Spine");
            var root = new GameObject("PFD SpineRaster") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = new Vector3(0, -2400, 0);
            try
            {
                bool hasAuthor = !string.IsNullOrWhiteSpace(author);
                var t = Text(root, font, title, 44, new Vector2(hasAuthor ? 600 : 860, 70), new Vector3(hasAuthor ? -110 : 0, 0, 0), FontStyles.Bold);
                var list = new System.Collections.Generic.List<TextMeshPro> { t };
                if (hasAuthor) list.Add(Text(root, font, author, 32, new Vector2(240, 60), new Vector3(330, 0, 0), FontStyles.Normal));
                Draw(rt, root, list, SW, SH);
            }
            finally { if (Application.isPlaying) Object.Destroy(root); else Object.DestroyImmediate(root); }
            return rt;
        }

        public static RenderTexture Render(string title, string author, TMP_FontAsset font, RenderTexture reuse = null)
        {
            var rt = Target(reuse, W, H, "PFD Cover");
            var root = new GameObject("PFD CoverRaster") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = new Vector3(0, -2000, 0);
            try
            {
                var t = Text(root, font, title, 96, new Vector2(W - 220, 620), new Vector3(0, 170, 0), FontStyles.Bold);
                var a = Text(root, font, author, 52, new Vector2(W - 220, 200), new Vector3(0, -320, 0), FontStyles.Normal);
                var line = Text(root, font, "—", 60, new Vector2(200, 80), new Vector3(0, -150, 0), FontStyles.Normal);
                Draw(rt, root, new System.Collections.Generic.List<TextMeshPro> { t, a, line }, W, H);
            }
            finally { if (Application.isPlaying) Object.Destroy(root); else Object.DestroyImmediate(root); }
            return rt;
        }

        static void Draw(RenderTexture rt, GameObject root, System.Collections.Generic.List<TextMeshPro> items, int w, int h)
        {
            var cmd = new CommandBuffer { name = "PFD Cover" };
            cmd.SetRenderTarget(rt);
            var bg = QualitySettings.activeColorSpace == ColorSpace.Linear ? Gold.linear : Gold;
            bg.a = 0;
            cmd.ClearRenderTarget(true, true, bg);
            var proj = Matrix4x4.Ortho(-w * 0.5f, w * 0.5f, -h * 0.5f, h * 0.5f, -100f, 100f);
            var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * root.transform.worldToLocalMatrix;
            cmd.SetViewProjectionMatrices(view, GL.GetGPUProjectionMatrix(proj, true));
            foreach (var x in items)
            {
                if (x.mesh != null && x.mesh.vertexCount > 0) cmd.DrawMesh(x.mesh, x.transform.localToWorldMatrix, x.fontSharedMaterial, 0, 0);
                foreach (var sub in x.GetComponentsInChildren<TMP_SubMesh>(true))
                    if (sub.mesh != null && sub.sharedMaterial != null) cmd.DrawMesh(sub.mesh, sub.transform.localToWorldMatrix, sub.sharedMaterial, 0, 0);
            }
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }

        static TextMeshPro Text(GameObject root, TMP_FontAsset font, string s, float size, Vector2 rect, Vector3 pos, FontStyles style)
        {
            var go = new GameObject("T") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            var t = go.AddComponent<TextMeshPro>();
            t.font = font; t.richText = false; t.isOrthographic = true;
            t.fontSize = size; t.fontStyle = style; t.color = Gold;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.enableAutoSizing = true; t.fontSizeMin = size * 0.45f; t.fontSizeMax = size;
            t.rectTransform.sizeDelta = rect;
            t.text = s ?? "";
            t.ForceMeshUpdate(true, true);
            t.renderer.enabled = false;
            foreach (var sub in t.GetComponentsInChildren<TMP_SubMesh>(true)) if (sub.renderer) sub.renderer.enabled = false;
            return t;
        }
    }
}
