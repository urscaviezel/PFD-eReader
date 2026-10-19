using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PFDReader.EditorTools
{
    /// <summary>Analyse ohne die offene Szene anzufassen (Vorschauszene): Collider eines Raums und Sichtlinien Sitzplatz -> TV.</summary>
    public static class AudioProbe
    {
        public static string Probe(string id)
        {
            var scene = EditorSceneManager.OpenPreviewScene($"Assets/PFDReader/Rooms/{id}/{id}.unity");
            try
            {
                var ps = scene.GetPhysicsScene();
                var roots = scene.GetRootGameObjects();
                var info = roots.SelectMany(r => r.GetComponentsInChildren<View.RoomInfo>(true)).FirstOrDefault();
                var cols = roots.SelectMany(r => r.GetComponentsInChildren<Collider>(true)).ToArray();
                var sb = new StringBuilder();
                sb.AppendLine($"Collider: {cols.Length} (Mesh {cols.Count(c => c is MeshCollider)}, Box {cols.Count(c => c is BoxCollider)})");
                foreach (var c in cols.OrderByDescending(c => c.bounds.size.sqrMagnitude).Take(25))
                    sb.AppendLine($"  {c.name} {c.GetType().Name} enabled={c.enabled && c.gameObject.activeInHierarchy} size={c.bounds.size:F1} c={c.bounds.center:F1}");
                if (info == null || info.tv == null) return sb.Append("kein TV").ToString();
                var tv = info.tv.position;
                sb.AppendLine($"TV {tv:F2}");
                var hits = new RaycastHit[32];
                foreach (var seat in info.seats)
                {
                    var p = seat.position;
                    var dir = tv - p; float len = dir.magnitude;
                    int n = ps.Raycast(p, dir / len, hits, len - 0.2f, ~0, QueryTriggerInteraction.Ignore);
                    sb.AppendLine($"{seat.name} {p:F2} d={len:F1}: " + string.Join(", ", hits.Take(n).OrderBy(h => h.distance).Select(h => $"{h.collider.name}@{h.distance:F1}")));
                }
                return sb.ToString();
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        /// <summary>Verdeckungsanteil wie im TvController (9 Punkte) fuer alle Sitzplaetze + optionale Zusatzpunkte "x,y,z;x,y,z".</summary>
        public static string Occl(string id, string extra)
        {
            var scene = EditorSceneManager.OpenPreviewScene($"Assets/PFDReader/Rooms/{id}/{id}.unity");
            try
            {
                var ps = scene.GetPhysicsScene();
                var info = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<View.RoomInfo>(true)).First();
                var tv = info.tv; var size = new Vector2(tv.localScale.x, tv.localScale.y);
                var pts = info.seats.Select(t => (t.name, t.position)).ToList();
                if (!string.IsNullOrEmpty(extra) && extra != "-")
                    foreach (var e in extra.Split(';')) { var v = e.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); pts.Add(("P" + e, new Vector3(v[0], v[1], v[2]))); }
                var hits = new RaycastHit[16]; var sb = new StringBuilder();
                foreach (var (name, head) in pts)
                {
                    var ax = tv.rotation * Vector3.right * (size.x * 0.5f + 0.5f);
                    var ay = tv.rotation * Vector3.up * (size.y * 0.5f);
                    int blocked = 0, total = 0; var what = new System.Collections.Generic.HashSet<string>();
                    for (int ix = -1; ix <= 1; ix++) for (int iy = -1; iy <= 1; iy++)
                    {
                        var p = tv.position + ax * ix + ay * iy; var dir = p - head; float len = dir.magnitude; if (len < 0.4f) continue; total++;
                        int n = ps.Raycast(head, dir / len, hits, len - 0.3f, ~(1 << 2), QueryTriggerInteraction.Ignore);
                        if (n > 0) { blocked++; what.Add(hits[0].collider.name); }
                    }
                    float o = total == 0 ? 0 : blocked / (float)total;
                    sb.AppendLine($"{name}: {blocked}/{total} occl={o:F2} vol x{Mathf.Lerp(1f, 0.3f, o * o):F2} cutoff {Mathf.Lerp(20000f, 1200f, o * o):F0} [{string.Join(",", what)}]");
                }
                return sb.ToString();
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        /// <summary>Buchhuelle (Box) an Position mit Moebeln ueberlappen lassen und Herausdruecken pruefen.</summary>
        public static string Pen(string id, string pos)
        {
            var scene = EditorSceneManager.OpenPreviewScene($"Assets/PFDReader/Rooms/{id}/{id}.unity");
            var go = new GameObject("hull"); var box = go.AddComponent<BoxCollider>(); box.size = new Vector3(0.3f, 0.42f, 0.06f);
            if (pos.StartsWith("m")) { pos = pos.Substring(1); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene); }
            try
            {
                var v = pos.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var p = new Vector3(v[0], v[1], v[2]); var rot = Quaternion.Euler(20, 30, 0);
                var cols = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>()).ToArray();
                var sb = new StringBuilder();
                for (int iter = 0; iter < 4; iter++)
                    foreach (var c in cols)
                        if (Physics.ComputePenetration(box, p, rot, c, c.transform.position, c.transform.rotation, out var d, out var depth) && depth > 0.0005f)
                        { sb.AppendLine($"iter{iter} {c.name} depth={depth:F3} dir={d:F2}"); p += d * (depth + 0.002f); }
                return sb.AppendLine($"-> {p:F3}").ToString();
            }
            finally { Object.DestroyImmediate(go); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
