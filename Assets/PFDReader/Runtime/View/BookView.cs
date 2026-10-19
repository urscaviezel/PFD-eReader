using UnityEngine;

namespace PFDReader.View
{
    /// <summary>
    /// 3D-Buch: zwei aufliegende Seiten, ein gewoelbtes Blatt zum Umblaettern (Vorder-/Rueckseite), Buchblock und Einband.
    /// Lokales Koordinatensystem: Bund auf der Y-Achse, Seiten in der Ebene z=0, Leser schaut von -Z auf das Buch.
    /// Masse in Metern bei Skalierung 1.
    /// </summary>
    public sealed class BookView : MonoBehaviour
    {
        public float pageWidth = 0.30f;
        public float pageHeight = 0.40f;
        public float blockThickness = 0.022f;
        public float coverMargin = 0.008f;
        public float coverThickness = 0.004f;
        public Color coverColor = new Color(0.075f, 0.155f, 0.19f);   // Petrol-Leder
        public Color goldColor = new Color(0.86f, 0.70f, 0.42f);
        public Color edgeColor = new Color(0.94f, 0.91f, 0.84f);
        public Color ribbonColor = new Color(0.50f, 0.07f, 0.09f);
        const float CornerRadius = 0.008f, Joint = 0.006f;
        const int SpineSegs = 16;

        const int Columns = 48;

        Material m_LeftMat, m_RightMat, m_LeafFrontMat, m_LeafBackMat, m_CoverMat, m_FrontCoverMat, m_SpineMat, m_EdgeMat, m_RibbonMat;
        Transform m_LeftPage, m_RightPage, m_Pivot, m_LeftHalf;
        float m_Closed, m_ShownClosed;
        Transform m_LeftBlock, m_RightBlock, m_LeftCover, m_RightCover;
        Mesh m_SpineMesh, m_LeftBoard, m_RightBoard;
        Vector3[] m_SpineV; Vector3[] m_SpineN; Vector2[] m_SpineUv, m_SpineUv1;
        float m_TL = -1, m_TR = -1; bool m_SpineDirty = true;
        Mesh m_LeafFront, m_LeafBack;
        GameObject m_LeafFrontGo, m_LeafBackGo;
        Vector3[] m_V; Vector3[] m_NFront; Vector3[] m_NBack;
        float m_Progress = 0.5f, m_ShownProgress = 0.5f;
        Texture m_Blank;

        public float TurnT { get; private set; }
        bool m_Backward;
        public bool Turning { get; private set; }

        bool m_Built;
        void Awake() { if (!m_Built) { m_Built = true; Build(); } }

        static void Kill(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        /// <summary>Editor-Vorschau: aufbauen und Zustand sofort anwenden (ohne Update-Schleife).</summary>
        public void EditorRefresh()
        {
            Awake();
            m_ShownProgress = m_Progress; m_ShownClosed = m_Closed;
            ApplyClose(m_ShownClosed);
        }

        void Build()
        {
            var pageShader = Shader.Find("PFD/Page");
            var litShader = Shader.Find("PFD/SimpleLit");
            m_Blank = Texture2D.whiteTexture;
            m_LeftMat = NewPageMat(pageShader, 1, false);
            m_RightMat = NewPageMat(pageShader, 0, false);
            m_LeafFrontMat = NewPageMat(pageShader, 0, false);
            m_LeafBackMat = NewPageMat(pageShader, 0, true);
            var leather = Shader.Find("PFD/Leather");
            m_CoverMat = new Material(leather) { color = coverColor };
            m_CoverMat.SetTexture("_GrainTex", Rendering.LeatherTexture.Get());
            m_CoverMat.SetColor("_Gold", goldColor);
            m_CoverMat.SetFloat("_DecalFlip", SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
            m_FrontCoverMat = new Material(m_CoverMat); m_FrontCoverMat.SetFloat("_DecalMode", 1);
            m_SpineMat = new Material(m_CoverMat); m_SpineMat.SetFloat("_DecalMode", 2);
            m_SpineMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            m_EdgeMat = new Material(litShader) { color = edgeColor };
            m_EdgeMat.SetFloat("_Stripes", 0.14f);
            m_EdgeMat.SetFloat("_StripeFreq", 900f);
            m_EdgeMat.SetFloat("_Ambient", 0.6f);
            m_RibbonMat = new Material(litShader) { color = ribbonColor };
            m_RibbonMat.SetFloat("_Ambient", 0.5f);
            foreach (var m in new[] { m_CoverMat, m_FrontCoverMat, m_SpineMat, m_EdgeMat, m_RibbonMat }) m.renderQueue = 2600;

            m_Pivot = new GameObject("Pivot").transform;
            m_Pivot.SetParent(transform, false);
            m_LeftHalf = new GameObject("LeftHalf").transform;
            m_LeftHalf.SetParent(m_Pivot, false);

            m_LeftPage = Quad("LeftPage", m_LeftMat, new Vector3(-pageWidth * 0.5f, 0, 0), m_LeftHalf);
            m_RightPage = Quad("RightPage", m_RightMat, new Vector3(pageWidth * 0.5f, 0, 0), m_Pivot);

            m_LeftBlock = Box("LeftBlock", m_EdgeMat, m_LeftHalf);
            m_RightBlock = Box("RightBlock", m_EdgeMat, m_Pivot);
            float cw = pageWidth + coverMargin, ch = pageHeight + coverMargin * 2;
            m_LeftBoard = BoardMesh(-1, cw, ch, coverThickness, true);
            m_RightBoard = BoardMesh(+1, cw, ch, coverThickness, false);
            m_LeftCover = MeshObject("LeftCover", m_LeftBoard, m_FrontCoverMat, m_LeftHalf).transform;
            m_RightCover = MeshObject("RightCover", m_RightBoard, m_CoverMat, m_Pivot).transform;
            m_SpineMesh = new Mesh { name = "Spine" }; m_SpineMesh.MarkDynamic();
            MeshObject("Spine", m_SpineMesh, m_SpineMat, m_Pivot);
            InitSpine(ch);

            // Lesebaendchen: haengt unten am Bund aus dem Buchblock
            var ribbon = Box("Ribbon", m_RibbonMat, m_Pivot);
            ribbon.localScale = new Vector3(0.0065f, 0.055f, 0.0005f);
            ribbon.localPosition = new Vector3(0.012f, -pageHeight * 0.5f - 0.018f, 0.0012f);
            ribbon.localRotation = Quaternion.Euler(0, 0, 7f);

            m_LeafFront = NewLeafMesh(false);
            m_LeafBack = NewLeafMesh(true);
            m_LeafFrontGo = LeafObject("LeafFront", m_LeafFront, m_LeafFrontMat);
            m_LeafBackGo = LeafObject("LeafBack", m_LeafBack, m_LeafBackMat);
            m_V = new Vector3[(Columns + 1) * 2];
            m_NFront = new Vector3[m_V.Length]; m_NBack = new Vector3[m_V.Length];
            SetLeafVisible(false);
            LayoutBlocks(m_Progress);
        }

        GameObject MeshObject(string name, Mesh mesh, Material mat, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>
        /// Einbanddeckel als flache Platte mit abgerundeten Ecken an der Vorderkante. Bund bei x=0, Vorderkante bei side*w,
        /// Dicke um z=0. UV0 in Metern (Narbung), UV1 = Praegung (nur Aussenseite +z des linken Deckels: der Buchdeckel).
        /// </summary>
        static Mesh BoardMesh(int side, float w, float h, float t, bool coverDecal)
        {
            // Umriss (x, y), gegen den Uhrzeigersinn
            var outline = new System.Collections.Generic.List<Vector2>();
            float r = CornerRadius; const int seg = 8;
            float xf = side * w; // Vorderkante
            void Corner(Vector2 c, float a0)
            {
                for (int i = 0; i <= seg; i++) { float a = (a0 + 90f * i / seg) * Mathf.Deg2Rad; outline.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); }
            }
            if (side > 0)
            {
                outline.Add(new Vector2(0, -h / 2));
                Corner(new Vector2(xf - r, -h / 2 + r), -90);
                Corner(new Vector2(xf - r, h / 2 - r), 0);
                outline.Add(new Vector2(0, h / 2));
            }
            else
            {
                outline.Add(new Vector2(0, h / 2));
                Corner(new Vector2(xf + r, h / 2 - r), 90);
                Corner(new Vector2(xf + r, -h / 2 + r), 180);
                outline.Add(new Vector2(0, -h / 2));
            }
            var v = new System.Collections.Generic.List<Vector3>(); var n = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>(); var uv1 = new System.Collections.Generic.List<Vector2>();
            var tri = new System.Collections.Generic.List<int>();
            var none = new Vector2(-1, -1);
            void Tri(int a, int b, int c, Vector3 nrm)
            {
                if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), nrm) >= 0) { tri.Add(a); tri.Add(b); tri.Add(c); }
                else { tri.Add(a); tri.Add(c); tri.Add(b); }
            }
            Vector2 center = new Vector2(xf * 0.5f, 0);
            foreach (var face in new[] { -1, 1 })
            {
                var nrm = new Vector3(0, 0, face);
                int c0 = v.Count;
                bool decal = coverDecal && face > 0;
                v.Add(new Vector3(center.x, center.y, face * t / 2)); n.Add(nrm); uv.Add(center);
                uv1.Add(decal ? new Vector2(-center.x / w, (center.y + h / 2) / h) : none);
                foreach (var p in outline)
                {
                    v.Add(new Vector3(p.x, p.y, face * t / 2)); n.Add(nrm); uv.Add(p);
                    uv1.Add(decal ? new Vector2(-p.x / w, (p.y + h / 2) / h) : none);
                }
                for (int i = 0; i < outline.Count - 1; i++) Tri(c0, c0 + 1 + i, c0 + 2 + i, nrm);
                Tri(c0, c0 + outline.Count, c0 + 1, nrm); // Bundkante schliessen
            }
            // Seitenkanten (inkl. Bundkante)
            float run = 0;
            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Count];
                var dir = (b - a); float len = dir.magnitude; if (len < 1e-6f) continue;
                var en = new Vector3(dir.y, -dir.x, 0).normalized;
                if (Vector3.Dot(en, (Vector3)(a + b) * 0.5f - (Vector3)center) < 0) en = -en;
                int s0 = v.Count;
                v.Add(new Vector3(a.x, a.y, -t / 2)); v.Add(new Vector3(a.x, a.y, t / 2)); v.Add(new Vector3(b.x, b.y, -t / 2)); v.Add(new Vector3(b.x, b.y, t / 2));
                for (int k = 0; k < 4; k++) { n.Add(en); uv1.Add(none); }
                uv.Add(new Vector2(run, -t / 2)); uv.Add(new Vector2(run, t / 2)); uv.Add(new Vector2(run + len, -t / 2)); uv.Add(new Vector2(run + len, t / 2));
                run += len;
                Tri(s0, s0 + 1, s0 + 3, en); Tri(s0, s0 + 3, s0 + 2, en);
            }
            var mesh = new Mesh { name = side < 0 ? "BoardLeft" : "BoardRight" };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetUVs(1, uv1); mesh.SetTriangles(tri, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        void InitSpine(float ch)
        {
            int ring = SpineSegs + 1;
            int nv = ring * 2 + (ring + 1) * 2; // Mantel + 2 Deckel (Faecher)
            m_SpineV = new Vector3[nv]; m_SpineN = new Vector3[nv]; m_SpineUv = new Vector2[nv]; m_SpineUv1 = new Vector2[nv];
            var tri = new System.Collections.Generic.List<int>();
            for (int i = 0; i < SpineSegs; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tri.AddRange(new[] { a, b, d, a, d, c });
            }
            for (int cap = 0; cap < 2; cap++)
            {
                int c0 = ring * 2 + cap * (ring + 1);
                for (int i = 0; i < SpineSegs; i++) tri.AddRange(new[] { c0, c0 + 1 + i, c0 + 2 + i });
            }
            m_SpineMesh.vertices = m_SpineV;
            m_SpineMesh.SetTriangles(tri, 0);
            m_SpineMesh.bounds = new Bounds(Vector3.zero, new Vector3(0.2f, ch * 1.2f, 0.2f));
        }

        /// <summary>
        /// Gewoelbter Buchruecken zwischen den Bundkanten beider Deckel (aussen): quadratische Bezier-Kurve, die sich
        /// beim Zuklappen vom flachen Steg hinter dem Bund zur runden Rueckenwoelbung links am Buch formt.
        /// </summary>
        void UpdateSpine()
        {
            if (m_SpineMesh == null) return;
            float ch = pageHeight + coverMargin * 2;
            var rot = m_LeftHalf.localRotation;
            float zl = m_TL + coverThickness + 0.0006f, zr = m_TR + coverThickness + 0.0006f;
            Vector3 A = rot * new Vector3(-Joint, 0, zl), B = new Vector3(Joint, 0, zr);
            Vector3 lc = rot * new Vector3(-pageWidth * 0.5f, 0, m_TL * 0.5f), rc = new Vector3(pageWidth * 0.5f, 0, m_TR * 0.5f);
            var center = (lc + rc) * 0.5f;
            var mid = (A + B) * 0.5f; var chord = B - A; float len = chord.magnitude;
            var outw = len > 1e-5f ? new Vector3(-chord.z, 0, chord.x) / len : Vector3.forward;
            if (Vector3.Dot(outw, mid - center) < 0) outw = -outw;
            float bulge = 0.5f * len * 0.85f + 0.0015f;
            var C = mid + outw * (2f * bulge);
            int ring = SpineSegs + 1;
            float arc = 0; Vector3 prev = A;
            for (int i = 0; i <= SpineSegs; i++)
            {
                float t = i / (float)SpineSegs, u = 1 - t;
                var p = u * u * A + 2 * u * t * C + t * t * B;
                var tan = (2 * u * (C - A) + 2 * t * (B - C)).normalized;
                var nrm = new Vector3(-tan.z, 0, tan.x); if (Vector3.Dot(nrm, p - mid) < 0 && Vector3.Dot(nrm, outw) < 0) nrm = -nrm;
                if (Vector3.Dot(nrm, outw) < -0.2f) nrm = -nrm;
                arc += (p - prev).magnitude; prev = p;
                m_SpineV[i * 2] = new Vector3(p.x, -ch / 2, p.z); m_SpineV[i * 2 + 1] = new Vector3(p.x, ch / 2, p.z);
                m_SpineN[i * 2] = m_SpineN[i * 2 + 1] = nrm;
                m_SpineUv[i * 2] = new Vector2(arc, -ch / 2); m_SpineUv[i * 2 + 1] = new Vector2(arc, ch / 2);
                m_SpineUv1[i * 2] = new Vector2(t, 0); m_SpineUv1[i * 2 + 1] = new Vector2(t, 1);
            }
            for (int cap = 0; cap < 2; cap++)
            {
                int c0 = ring * 2 + cap * (ring + 1);
                float y = cap == 0 ? -ch / 2 : ch / 2;
                var cn = new Vector3(0, cap == 0 ? -1 : 1, 0);
                m_SpineV[c0] = new Vector3(mid.x, y, mid.z); m_SpineN[c0] = cn; m_SpineUv[c0] = new Vector2(mid.x, mid.z); m_SpineUv1[c0] = new Vector2(-1, -1);
                for (int i = 0; i <= SpineSegs; i++)
                {
                    var p = m_SpineV[i * 2];
                    m_SpineV[c0 + 1 + i] = new Vector3(p.x, y, p.z); m_SpineN[c0 + 1 + i] = cn;
                    m_SpineUv[c0 + 1 + i] = new Vector2(p.x, p.z); m_SpineUv1[c0 + 1 + i] = new Vector2(-1, -1);
                }
            }
            m_SpineMesh.vertices = m_SpineV; m_SpineMesh.normals = m_SpineN; m_SpineMesh.uv = m_SpineUv; m_SpineMesh.uv2 = m_SpineUv1;
            m_SpineMesh.RecalculateTangents();
        }

        Material NewPageMat(Shader s, float spineSide, bool mirror)
        {
            var m = new Material(s);
            m.SetFloat("_SpineSide", spineSide);
            m.SetFloat("_Mirror", mirror ? 1 : 0);
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            m.SetFloat("_Shade", 0.18f);
            m.renderQueue = 2600; // nach Raum/Panorama und Dimmer (2450), damit das Buch nicht abgedunkelt wird
            return m;
        }

        Transform Quad(string name, Material mat, Vector3 pos, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Kill(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(pageWidth, pageHeight, 1);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        Transform Box(string name, Material mat, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Kill(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        GameObject LeafObject(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_Pivot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        Mesh NewLeafMesh(bool back)
        {
            var mesh = new Mesh { name = back ? "LeafBack" : "LeafFront" };
            mesh.MarkDynamic();
            int n = (Columns + 1) * 2;
            var verts = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i <= Columns; i++)
            {
                float u = i / (float)Columns;
                uv[i * 2] = new Vector2(u, 0); uv[i * 2 + 1] = new Vector2(u, 1);
            }
            var tris = new int[Columns * 6];
            for (int i = 0; i < Columns; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3; // a=unten, b=oben bei u_i; c/d bei u_i+1
                if (!back) { tris[i * 6 + 0] = a; tris[i * 6 + 1] = b; tris[i * 6 + 2] = d; tris[i * 6 + 3] = a; tris[i * 6 + 4] = d; tris[i * 6 + 5] = c; }
                else { tris[i * 6 + 0] = a; tris[i * 6 + 1] = d; tris[i * 6 + 2] = b; tris[i * 6 + 3] = a; tris[i * 6 + 4] = c; tris[i * 6 + 5] = d; }
            }
            mesh.vertices = verts; mesh.uv = uv; mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(pageWidth * 2.2f, pageHeight * 1.2f, pageWidth * 2.2f));
            return mesh;
        }

        // ---------------------------------------------------------------- Oeffentliche API
        /// <summary>Ruhezustand: zwei aufliegende Seiten.</summary>
        public void SetStatic(Texture left, Texture right)
        {
            Turning = false;
            SetLeafVisible(false);
            m_LeftMat.mainTexture = left ? left : m_Blank;
            m_RightMat.mainTexture = right ? right : m_Blank;
        }

        /// <summary>Blaettern: Unterlage links/rechts, Blatt vorne (rechte Seite) / hinten (linke Seite), t: 0 = Blatt rechts, 1 = links.</summary>
        public void SetTurn(Texture baseLeft, Texture baseRight, Texture leafFront, Texture leafBack, float t, bool backward = false)
        {
            m_Backward = backward;
            Turning = true;
            m_LeftMat.mainTexture = baseLeft ? baseLeft : m_Blank;
            m_RightMat.mainTexture = baseRight ? baseRight : m_Blank;
            m_LeafFrontMat.mainTexture = leafFront ? leafFront : m_Blank;
            m_LeafBackMat.mainTexture = leafBack ? leafBack : m_Blank;
            SetLeafVisible(true);
            UpdateLeaf(Mathf.Clamp01(t));
        }

        /// <summary>Textur-Ausschnitt je Seite (0 links, 1 rechts, 2 Blatt vorne, 3 Blatt hinten): xy = Skalierung, zw = Versatz.</summary>
        public void SetPageST(int which, Vector4 st)
        {
            var m = which == 0 ? m_LeftMat : which == 1 ? m_RightMat : which == 2 ? m_LeafFrontMat : m_LeafBackMat;
            m.SetVector("_MainTex_ST", st);
        }

        Material Mat(int which) => which == 0 ? m_LeftMat : which == 1 ? m_RightMat : which == 2 ? m_LeafFrontMat : m_LeafBackMat;

        /// <summary>
        /// Passt den Seiteninhalt (feste Seiten: PDF/Comic) mit Rand und Bundsteg ins Blatt ein und erhaelt das Seitenverhaeltnis.
        /// tex == null oder textLayout: ganze Flaeche.
        /// </summary>
        public void FitContent(int which, Texture tex, bool fixedLayout)
        {
            var m = Mat(which);
            if (!fixedLayout || tex == null) { m.SetVector("_ContentRect", new Vector4(0, 0, 1, 1)); return; }
            bool spineRight = which == 0 || which == 3;     // linke Seite (auch Blatt-Rueckseite): Bund rechts
            const float outer = 0.0225f, spine = 0.033f, vert = 0.0175f;
            float u0 = spineRight ? outer : spine, u1 = 1f - (spineRight ? spine : outer);
            float v0 = vert, v1 = 1f - vert;
            float availW = (u1 - u0) * pageWidth, availH = (v1 - v0) * pageHeight;
            float a = tex.width / (float)Mathf.Max(1, tex.height);
            float w = availW, h = availW / a;
            if (h > availH) { h = availH; w = availH * a; }
            float du = w / pageWidth, dv = h / pageHeight;
            // waagrecht zum Bund hin ausrichten, senkrecht zentrieren
            float cu0 = spineRight ? u1 - du : u0;
            float cv0 = (v0 + v1) * 0.5f - dv * 0.5f;
            m.SetVector("_ContentRect", new Vector4(cu0, cv0, cu0 + du, cv0 + dv));
        }

        public void SetPaper(Color paper)
        {
            var c = QualitySettings.activeColorSpace == ColorSpace.Linear ? paper : paper;
            foreach (var m in new[] { m_LeftMat, m_RightMat, m_LeafFrontMat, m_LeafBackMat }) m.SetColor("_Paper", c);
        }

        public void SetTint(Color paper)
        {
            foreach (var m in new[] { m_LeftMat, m_RightMat, m_LeafFrontMat, m_LeafBackMat }) m.SetColor("_Tint", paper);
        }

        /// <summary>Lesefortschritt 0..1: verteilt die Dicke des Buchblocks auf links/rechts (geglaettet).</summary>
        public void SetProgress(float p) => m_Progress = Mathf.Clamp01(p);

        /// <summary>Strahl gegen die Buchebene. uv.x: -1 (linker Rand) .. 0 (Bund) .. 1 (rechter Rand), uv.y: -1 unten .. 1 oben.</summary>
        public bool Raycast(Ray worldRay, out Vector2 uv, out float distance)
        {
            uv = default; distance = 0;
            var plane = new Plane(transform.TransformDirection(Vector3.back), transform.position);
            if (!plane.Raycast(worldRay, out distance)) return false;
            var local = m_Pivot.InverseTransformPoint(worldRay.GetPoint(distance));
            uv = new Vector2(local.x / pageWidth, local.y / (pageHeight * 0.5f));
            float mx = 1f + (coverMargin / pageWidth), my = 1f + (coverMargin / (pageHeight * 0.5f));
            float minX = Mathf.Lerp(-mx, -0.02f, m_ShownClosed);
            return uv.x >= minX && uv.x <= mx && Mathf.Abs(uv.y) <= my;
        }

        public float Width => pageWidth * 2f * transform.lossyScale.x;

        // ---------------------------------------------------------------- Intern
        void SetLeafVisible(bool v)
        {
            if (m_LeafFrontGo.activeSelf != v) m_LeafFrontGo.SetActive(v);
            if (m_LeafBackGo.activeSelf != v) m_LeafBackGo.SetActive(v);
        }

        void UpdateLeaf(float t)
        {
            TurnT = t;
            float w = pageWidth, h2 = pageHeight * 0.5f;
            float bend = 0.38f * Mathf.Sin(Mathf.PI * t);
            float lift = -0.0012f - 0.01f * Mathf.Sin(Mathf.PI * t); // leicht ueber den Seiten
            float x = 0, z = lift;
            float du = 1f / Columns;
            for (int i = 0; i <= Columns; i++)
            {
                float u = i * du;
                // Die greifende Aussenkante eilt voraus: vorwaerts (t 0->1) dreht sie weiter, rueckwaerts (t 1->0) weniger weit
                float phi = Mathf.PI * Mathf.Clamp01(t + (m_Backward ? -1f : 1f) * bend * (u - 0.35f));
                var n = new Vector3(-Mathf.Sin(phi), 0, -Mathf.Cos(phi));
                m_V[i * 2] = new Vector3(x, -h2, z);
                m_V[i * 2 + 1] = new Vector3(x, h2, z);
                m_NFront[i * 2] = m_NFront[i * 2 + 1] = n;
                m_NBack[i * 2] = m_NBack[i * 2 + 1] = -n;
                x += Mathf.Cos(phi) * w * du;
                z += -Mathf.Sin(phi) * w * du;
            }
            m_LeafFront.vertices = m_V; m_LeafFront.normals = m_NFront;
            m_LeafBack.vertices = m_V; m_LeafBack.normals = m_NBack;
        }

        /// <summary>0 = offen, 1 = geschlossen (Deckel vorne, Buch zentriert). Wird weich animiert.</summary>
        public void SetClosed(bool closed, bool instant = false)
        {
            m_Closed = closed ? 1 : 0;
            if (instant) { m_ShownClosed = m_Closed; ApplyClose(m_ShownClosed); }
        }

        void ApplyClose(float c)
        {
            float e = c * c * (3 - 2 * c);
            m_LeftHalf.localRotation = Quaternion.Euler(0, -180f * e, 0);
            float left = Mathf.Min(0f, -pageWidth * Mathf.Cos(Mathf.PI * e));
            m_Pivot.localPosition = new Vector3(-(left + pageWidth) * 0.5f, 0, 0);
            m_SpineDirty = true; LayoutBlocks(m_ShownProgress);
        }
        public bool IsClosedVisual => m_ShownClosed > 0.98f;
        public bool IsOpenVisual => m_ShownClosed < 0.02f;
        public void SetCoverTexture(Texture t) => m_FrontCoverMat.SetTexture("_DecalTex", t ? t : Texture2D.blackTexture);
        public void SetSpineTexture(Texture t) => m_SpineMat.SetTexture("_DecalTex", t ? t : Texture2D.blackTexture);

        /// <summary>Farbe des Ledereinbands (spaeter waehlbar).</summary>
        public void SetCoverColor(Color c) { coverColor = c; foreach (var m in new[] { m_CoverMat, m_FrontCoverMat, m_SpineMat }) m.color = c; }

        /// <summary>Lokale X-Koordinate (in Pivot-Raum) eines Weltpunkts relativ zur Seitenbreite: -1 links .. 1 rechts.</summary>
        public float LocalU(Vector3 world) => m_Pivot.InverseTransformPoint(world).x / pageWidth;

        void Update()
        {
            if (!Mathf.Approximately(m_ShownClosed, m_Closed))
            {
                m_ShownClosed = Mathf.MoveTowards(m_ShownClosed, m_Closed, Time.deltaTime / 0.6f);
                float e = m_ShownClosed * m_ShownClosed * (3 - 2 * m_ShownClosed);
                m_LeftHalf.localRotation = Quaternion.Euler(0, -180f * e, 0);
                // Silhouette in jedem Oeffnungswinkel mittig halten: rechte Haelfte [0,W], linke reicht bis -W*cos(pi*e)
                float left = Mathf.Min(0f, -pageWidth * Mathf.Cos(Mathf.PI * e));
                m_Pivot.localPosition = new Vector3(-(left + pageWidth) * 0.5f, 0, 0);
                m_SpineDirty = true;
            }
            m_ShownProgress = Mathf.MoveTowards(m_ShownProgress, m_Progress, Time.deltaTime / 0.15f * Mathf.Max(0.02f, Mathf.Abs(m_Progress - m_ShownProgress)));
            LayoutBlocks(m_ShownProgress);
        }

        void LayoutBlocks(float p)
        {
            float tl = Mathf.Max(0.0015f, blockThickness * p), tr = Mathf.Max(0.0015f, blockThickness * (1 - p));
            float w = pageWidth, h = pageHeight;
            m_LeftBlock.localPosition = new Vector3(-w * 0.5f, 0, tl * 0.5f + 0.0004f);
            m_LeftBlock.localScale = new Vector3(w - 0.002f, h - 0.002f, tl);
            m_RightBlock.localPosition = new Vector3(w * 0.5f, 0, tr * 0.5f + 0.0004f);
            m_RightBlock.localScale = new Vector3(w - 0.002f, h - 0.002f, tr);
            m_LeftCover.localPosition = new Vector3(0, 0, tl + coverThickness * 0.5f + 0.0006f);
            m_RightCover.localPosition = new Vector3(0, 0, tr + coverThickness * 0.5f + 0.0006f);
            bool changed = !Mathf.Approximately(tl, m_TL) || !Mathf.Approximately(tr, m_TR) || m_SpineDirty;
            m_TL = tl; m_TR = tr;
            if (changed) { UpdateSpine(); m_SpineDirty = false; }
            // Geschlossen: linke Haelfte liegt auf der rechten; Dicke beider Bloecke stapeln
            m_LeftHalf.localPosition = new Vector3(0, 0, 0);
        }

        void OnDestroy()
        {
            foreach (var m in new Object[] { m_LeftMat, m_RightMat, m_LeafFrontMat, m_LeafBackMat, m_CoverMat, m_FrontCoverMat, m_SpineMat, m_EdgeMat, m_RibbonMat, m_LeafFront, m_LeafBack, m_SpineMesh, m_LeftBoard, m_RightBoard })
                if (m) Kill(m);
        }
    }
}
