using UnityEngine;

namespace PFDReader.Rendering
{
    /// <summary>
    /// Prozedurale, kachelbare Ledernarbung: Voronoi-Zellen (gewoelbte "Kiesel") + feines Rauschen.
    /// RG = Normale (Tangentenraum), B = Hoehe. Lineare Textur, wird einmal erzeugt und geteilt.
    /// </summary>
    public static class LeatherTexture
    {
        static Texture2D s_Tex;

        public static Texture2D Get()
        {
            if (s_Tex != null) return s_Tex;
            const int N = 256, Cells = 22;
            var rnd = new System.Random(1234);
            var pts = new Vector2[Cells * Cells];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
            var hgt = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = (x + 0.5f) / N * Cells, fy = (y + 0.5f) / N * Cells;
                    int cx = Mathf.FloorToInt(fx), cy = Mathf.FloorToInt(fy);
                    float d1 = 9, d2 = 9;
                    for (int oy = -1; oy <= 1; oy++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int gx = cx + ox, gy = cy + oy;
                            int wx = ((gx % Cells) + Cells) % Cells, wy = ((gy % Cells) + Cells) % Cells;
                            var p = pts[wy * Cells + wx];
                            float dx = gx + p.x - fx, dy = gy + p.y - fy;
                            float d = dx * dx + dy * dy;
                            if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                        }
                    float edge = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);            // 0 an der Zellgrenze
                    float pebble = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge * 2.4f));
                    hgt[y * N + x] = pebble;
                }
            // feines Rauschen (kachelbar ueber Sinus-Summen) fuer lebendigere Oberflaeche
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x * Mathf.PI * 2f / N, v = y * Mathf.PI * 2f / N;
                    float n = Mathf.Sin(u * 37 + Mathf.Sin(v * 11) * 2) * Mathf.Sin(v * 41 + Mathf.Sin(u * 13) * 2);
                    hgt[y * N + x] = Mathf.Clamp01(hgt[y * N + x] * 0.9f + n * 0.06f + 0.05f);
                }
            var px = new Color32[N * N];
            const float strength = 3.2f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float l = hgt[y * N + (x + N - 1) % N], r = hgt[y * N + (x + 1) % N];
                    float d = hgt[((y + N - 1) % N) * N + x], u = hgt[((y + 1) % N) * N + x];
                    var nrm = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                    px[y * N + x] = new Color32((byte)((nrm.x * 0.5f + 0.5f) * 255), (byte)((nrm.y * 0.5f + 0.5f) * 255), (byte)(hgt[y * N + x] * 255), 255);
                }
            s_Tex = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "PFD Leather Grain", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            s_Tex.SetPixels32(px);
            s_Tex.Apply(true, true);
            return s_Tex;
        }
    }
}
