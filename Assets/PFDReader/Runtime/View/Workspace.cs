using PFDReader.Core;
using UnityEngine;

namespace PFDReader.View
{
    /// <summary>Gemeinsamer Anker fuer Buch, Bibliothek und Leisten: Platzierung vor dem Kopf, Greifen/Verschieben, Distanz/Groesse.</summary>
    public sealed class Workspace : MonoBehaviour
    {
        public const float MinDistance = 0.45f, MaxDistance = 4f, MinScale = 0.45f, MaxScale = 2.5f;
        public Transform Head;

        bool m_Grabbing;
        Pose m_GrabOffset;   // Workspace relativ zum Controller
        Transform m_GrabHand;

        public bool Grabbing => m_Grabbing;

        /// <summary>Vor dem Blick auf Augenhoehe ausrichten (Neigung/Rollen des Kopfes werden ignoriert).</summary>
        public void Recenter()
        {
            if (Head == null) return;
            var fwd = Vector3.ProjectOnPlane(Head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Head.up, Vector3.up);
            fwd.Normalize();
            float d = Mathf.Clamp(Storage.Settings.distance, MinDistance, MaxDistance);
            var pos = Head.position + fwd * d + Vector3.down * 0.10f * d;
            var rot = Quaternion.LookRotation(pos - Head.position, Vector3.up);
            ApplyScale();
            // vom Kopf aus nach vorne schieben: steht Mobiliar im Weg, bleibt das Buch davor
            var near = Head.position + (pos - Head.position).normalized * 0.25f;
            m_BoundsT = -1f;
            transform.position = Constrain(near, pos, rot);
            transform.rotation = rot;
            CaptureFollow();
        }

        public void ApplyScale() => transform.localScale = Vector3.one * Mathf.Clamp(Storage.Settings.scale, MinScale, MaxScale);

        public void BeginGrab(Transform hand)
        {
            m_Grabbing = true;
            m_GrabHand = hand;
            var inv = Quaternion.Inverse(hand.rotation);
            m_GrabOffset = new Pose(inv * (transform.position - hand.position), inv * transform.rotation);
        }

        /// <summary>stick.y: weiter weg / naeher, stick.x: groesser / kleiner.</summary>
        public void UpdateGrab(Vector2 stick)
        {
            if (!m_Grabbing || m_GrabHand == null) return;
            if (Mathf.Abs(stick.y) > 0.25f)
            {
                var local = m_GrabOffset.position;
                float len = local.magnitude;
                float target = Mathf.Clamp(len * (1f + stick.y * 1.2f * Time.deltaTime), 0.2f, MaxDistance);
                if (len > 1e-3f) m_GrabOffset.position = local * (target / len);
            }
            if (Mathf.Abs(stick.x) > 0.25f)
            {
                Storage.Settings.scale = Mathf.Clamp(Storage.Settings.scale * (1f + stick.x * 0.9f * Time.deltaTime), MinScale, MaxScale);
                ApplyScale();
            }
            var rot = m_GrabHand.rotation * m_GrabOffset.rotation;
            transform.position = Constrain(transform.position, m_GrabHand.position + m_GrabHand.rotation * m_GrabOffset.position, rot);
            transform.rotation = rot;
        }

        // ---------------------------------------------------------------- Kollision mit dem Raum
        Bounds m_Local; bool m_HasBounds; float m_BoundsT = -1f;
        static readonly System.Collections.Generic.List<Vector3> s_Verts = new System.Collections.Generic.List<Vector3>();

        void Add(Vector3 lp)
        {
            if (!m_HasBounds) { m_Local = new Bounds(lp, Vector3.zero); m_HasBounds = true; } else m_Local.Encapsulate(lp);
        }

        /// <summary>Umriss des 3D-Buchs (alle sichtbaren Meshes) im lokalen Raum des Workspace.</summary>
        void UpdateBounds()
        {
            if (Time.unscaledTime - m_BoundsT < 0.5f) return;
            m_BoundsT = Time.unscaledTime;
            m_HasBounds = false;
            foreach (var r in GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled) continue;
                var t = r.transform;
                // echte Eckpunkte statt Mesh-Bounds: Buchruecken/Blattmeshes haben absichtlich grosszuegige Bounds (gegen Culling)
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf ? mf.sharedMesh : null;
                if (mesh != null && mesh.isReadable && mesh.vertexCount <= 4000)
                {
                    mesh.GetVertices(s_Verts);
                    foreach (var v in s_Verts) Add(transform.InverseTransformPoint(t.TransformPoint(v)));
                    continue;
                }
                var b = r.localBounds;
                for (int i = 0; i < 8; i++)
                    Add(transform.InverseTransformPoint(t.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))));
            }
        }

        BoxCollider m_Hull;
        static readonly Collider[] s_Overlap = new Collider[32];

        /// <summary>Unsichtbare Kollisionshuelle des Buchs (Trigger auf Layer 2, stoert weder Zeiger noch Gehen).</summary>
        BoxCollider Hull()
        {
            if (m_Hull) return m_Hull;
            var go = new GameObject("BookHull") { layer = 2 };
            DontDestroyOnLoad(go);
            m_Hull = go.AddComponent<BoxCollider>();
            m_Hull.isTrigger = true;
            return m_Hull;
        }

        /// <summary>
        /// Zielpose so korrigieren, dass das Buch nicht in Moebel/Waende der Raumszene eindringt:
        /// erst die Bewegung abfangen (BoxCast), dann eine verbliebene Ueberlappung (z. B. durch Drehen) herausdruecken.
        /// </summary>
        Vector3 Constrain(Vector3 from, Vector3 to, Quaternion rot)
        {
            UpdateBounds();
            if (!m_HasBounds) return to;
            var sc = transform.lossyScale;
            var half = Vector3.Scale(m_Local.extents, sc);
            var off = rot * Vector3.Scale(m_Local.center, sc);
            var pos = to;

            var delta = to - from; float dist = delta.magnitude;
            if (dist > 1e-5f)
            {
                var dir = delta / dist; float best = dist;
                var hits = Physics.BoxCastAll(from + off, half * 0.98f, dir, rot, dist, ~(1 << 2), QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (h.collider.gameObject.scene == gameObject.scene) continue;      // nur Raumgeometrie
                    if (h.distance <= 0f && h.point == Vector3.zero) continue;            // Startueberlappung: unten aufloesen
                    best = Mathf.Min(best, Mathf.Max(0f, h.distance - 0.005f));
                }
                pos = from + dir * best;
            }

            // Ueberlappung herausdruecken (mehrere Durchgaenge fuer Ecken)
            var hull = Hull();
            hull.size = half * 2f;
            hull.transform.SetPositionAndRotation(pos + off, rot);
            for (int iter = 0; iter < 4; iter++)
            {
                int n = Physics.OverlapBoxNonAlloc(pos + off, half, s_Overlap, rot, ~(1 << 2), QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int i = 0; i < n; i++)
                {
                    var c = s_Overlap[i];
                    if (c.gameObject.scene == gameObject.scene) continue;
                    if (Physics.ComputePenetration(hull, pos + off, rot, c, c.transform.position, c.transform.rotation, out var d, out var depth) && depth > 0.0005f)
                    { pos += d * (depth + 0.002f); moved = true; }
                }
                if (!moved) break;
            }
            return pos;
        }

        // ---------------------------------------------------------------- Follow Me
        Pose m_FollowRel;            // Pose relativ zum horizontalen Kopfrahmen
        bool m_FollowValid, m_FollowMoving;

        static Pose HeadFrame(Transform head)
        {
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(head.up, Vector3.up);
            return new Pose(head.position, Quaternion.LookRotation(fwd.normalized, Vector3.up));
        }

        /// <summary>Aktuelle Platzierung als Follow-Me-Bezug uebernehmen (nach Greifen, Einschalten, Zentrieren).</summary>
        public void CaptureFollow()
        {
            if (Head == null) return;
            var f = HeadFrame(Head);
            var inv = Quaternion.Inverse(f.rotation);
            m_FollowRel = new Pose(inv * (transform.position - f.position), inv * transform.rotation);
            m_FollowValid = true;
        }

        /// <summary>Weiches Nachfuehren: startet bei >18° Blickabweichung oder >25 cm Kopfbewegung, laeuft bis fast deckungsgleich.</summary>
        public void TickFollow(bool enabled, bool paused)
        {
            if (!enabled || Head == null || m_Grabbing) { m_FollowMoving = false; return; }
            if (!m_FollowValid) CaptureFollow();
            if (paused) return;
            var f = HeadFrame(Head);
            var targetPos = f.position + f.rotation * m_FollowRel.position;
            var targetRot = f.rotation * m_FollowRel.rotation;
            float angle = Quaternion.Angle(transform.rotation, targetRot);
            float dist = Vector3.Distance(transform.position, targetPos);
            if (!m_FollowMoving && (angle > 18f || dist > 0.25f)) m_FollowMoving = true;
            if (m_FollowMoving)
            {
                float k = 1f - Mathf.Exp(-3.2f * Time.deltaTime);
                var nrot = Quaternion.Slerp(transform.rotation, targetRot, k);
                transform.position = Constrain(transform.position, Vector3.Lerp(transform.position, targetPos, k), nrot);
                transform.rotation = nrot;
                if (angle < 1.5f && dist < 0.02f) m_FollowMoving = false;
            }
        }

        public void EndGrab()
        {
            if (!m_Grabbing) return;
            m_Grabbing = false;
            m_GrabHand = null;
            if (Head != null) Storage.Settings.distance = Mathf.Clamp(Vector3.Distance(Head.position, transform.position), MinDistance, MaxDistance);
            Storage.SaveSettings();
            CaptureFollow();
        }
    }
}
