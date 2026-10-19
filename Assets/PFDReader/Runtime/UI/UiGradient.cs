using UnityEngine;
using UnityEngine.UI;

namespace PFDReader.UI
{
    /// <summary>Waagrechter Farbverlauf ueber Vertexfarben (wie die Primaerknoepfe auf pfdhub.com: Violett -> Cyan).</summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class UiGradient : BaseMeshEffect
    {
        public Color left = UiKit.GradLeft, right = UiKit.GradRight;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var rect = graphic.rectTransform.rect;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float t = rect.width > 0 ? Mathf.Clamp01((v.position.x - rect.xMin) / rect.width) : 0;
                v.color = (Color32)((Color)v.color * Color.Lerp(left, right, t));
                vh.SetUIVertex(v, i);
            }
        }

        public static void Set(Graphic g, bool on)
        {
            if (g == null) return;
            var gr = g.GetComponent<UiGradient>();
            if (on && gr == null) gr = g.gameObject.AddComponent<UiGradient>();
            if (gr != null && gr.enabled != on) { gr.enabled = on; g.SetVerticesDirty(); }
        }
    }
}
