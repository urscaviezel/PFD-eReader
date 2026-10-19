using UnityEngine;

namespace PFDReader.Input
{
    /// <summary>Sichtbarer Zeigestrahl mit Ring-Cursor am Trefferpunkt.</summary>
    public sealed class LaserPointer : MonoBehaviour
    {
        public Color color = new Color(0.55f, 0.45f, 1f, 0.85f);
        public Color activeColor = new Color(0.13f, 0.83f, 1f, 0.95f);
        LineRenderer m_Line;
        Transform m_Cursor;
        Material m_LineMat, m_CursorMat;

        void Awake()
        {
            var shader = Shader.Find("PFD/Overlay");
            m_LineMat = new Material(shader);
            m_Line = gameObject.AddComponent<LineRenderer>();
            m_Line.sharedMaterial = m_LineMat;
            m_Line.sortingOrder = 200; // vor Weltraum-Canvases (Menues, Fernseher) zeichnen
            m_Line.positionCount = 2;
            m_Line.useWorldSpace = true;
            m_Line.widthCurve = new AnimationCurve(new Keyframe(0, 0.0035f), new Keyframe(1, 0.0012f));
            m_Line.colorGradient = Gradient(color);
            m_Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Line.numCapVertices = 2;

            var cur = new GameObject("Cursor");
            cur.transform.SetParent(transform, false);
            cur.AddComponent<MeshFilter>().sharedMesh = RingMesh(0.0055f, 0.0085f, 32);
            m_CursorMat = new Material(shader) { color = Color.white };
            var r = cur.AddComponent<MeshRenderer>();
            r.sharedMaterial = m_CursorMat;
            r.sortingOrder = 201;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Cursor = cur.transform;
        }

        static Gradient Gradient(Color c)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) },
                      new[] { new GradientAlphaKey(c.a, 0), new GradientAlphaKey(0.05f, 1) });
            return g;
        }

        /// <summary>Zeichnet den Strahl. hit: Trefferpunkt mit Flaechennormale, sonst Strahl ins Leere.</summary>
        public void Show(Ray ray, bool hasHit, Vector3 hitPoint, Vector3 hitNormal, bool active, float scale = 1f)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            var end = hasHit ? hitPoint : ray.GetPoint(1.5f);
            m_Line.SetPosition(0, ray.origin);
            m_Line.SetPosition(1, end);
            var c = active ? activeColor : color;
            m_Line.colorGradient = Gradient(c);
            m_Cursor.gameObject.SetActive(hasHit);
            if (hasHit)
            {
                m_Cursor.position = hitPoint - hitNormal * 0.001f;
                m_Cursor.rotation = Quaternion.LookRotation(hitNormal);
                float d = Vector3.Distance(ray.origin, hitPoint);
                m_Cursor.localScale = Vector3.one * Mathf.Clamp(d, 0.3f, 4f) * scale * (active ? 0.8f : 1f);
                m_CursorMat.color = active ? activeColor : Color.white;
            }
        }

        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }

        static Mesh RingMesh(float inner, float outer, int seg)
        {
            var m = new Mesh { name = "Ring" };
            var v = new Vector3[seg * 2]; var t = new int[seg * 6]; var uv = new Vector2[seg * 2];
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v[i * 2] = d * inner; v[i * 2 + 1] = d * outer;
                int n = (i + 1) % seg;
                t[i * 6] = i * 2; t[i * 6 + 1] = n * 2; t[i * 6 + 2] = i * 2 + 1;
                t[i * 6 + 3] = i * 2 + 1; t[i * 6 + 4] = n * 2; t[i * 6 + 5] = n * 2 + 1;
            }
            m.vertices = v; m.triangles = t; m.uv = uv;
            return m;
        }

        void OnDestroy() { if (m_LineMat) Destroy(m_LineMat); if (m_CursorMat) Destroy(m_CursorMat); }
    }
}
