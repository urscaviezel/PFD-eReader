using System;
using PFDReader.View;
using TMPro;
using UnityEngine;

namespace PFDReader.Spaces
{
    /// <summary>
    /// Bewegung im 3D-Raum (nur bei ausgeblendetem Lesebereich): freie Fortbewegung mit dem linken Stick
    /// (Kollision + Treppen ueber einen CharacterController), 15°-Drehung mit dem rechten Stick,
    /// Teleport per Trigger auf den Boden und Sitzplatz-Markierungen zum Hinsetzen.
    /// </summary>
    public sealed class Locomotion : MonoBehaviour
    {
        public const float SnapAngle = 15f, Speed = 1.4f;
        public Transform Head;
        public Func<Quaternion, Vector3, Vector3, bool> MoveRig;   // (Drehung, von, nach)

        CharacterController m_Body;
        bool m_SnapReady = true;
        float m_FallSpeed;
        bool m_Seated = true;       // nach dem Laden sitzt man am Standardplatz
        float m_FeetY; bool m_HasFeet;

        /// <summary>Augenhoehe im Stehen aus der eingestellten Koerpergroesse.</summary>
        public static float StandingEye => Mathf.Clamp(Core.Storage.Settings.playerHeight, 1.2f, 2.2f) - 0.11f;
        public bool Seated => m_Seated;

        void Awake()
        {
            var go = new GameObject("Body");
            go.transform.SetParent(transform, false);
            m_Body = go.AddComponent<CharacterController>();
            m_Body.radius = 0.22f; m_Body.height = 1.5f; m_Body.stepOffset = 0.32f; m_Body.slopeLimit = 50f;
            m_Body.skinWidth = 0.02f; m_Body.center = new Vector3(0, 0.77f, 0);
            m_Body.enabled = false;
        }

        /// <summary>Nach dem Hinsetzen: Kopf steht am Augenpunkt des Sitzplatzes.</summary>
        Transform m_Seat;
        public void SetSeated(Transform seat = null) { m_Seated = true; m_HasFeet = false; m_Seat = seat; }

        /// <summary>Boden unter dem Kopf: tiefste Flaeche bis 1.9 m darunter (Sitzkissen/Bett werden uebersprungen).</summary>
        public static bool FindFloor(Vector3 from, out float y)
        {
            y = 0;
            var hits = Physics.RaycastAll(from + Vector3.up * 0.05f, Vector3.down, 2.2f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool ok = false;
            foreach (var h in hits)
            {
                if (h.normal.y < 0.6f) continue;
                if (!ok || h.point.y < best) { best = h.point.y; ok = true; }
            }
            // nur Flaechen, die nicht weiter als 1.9 m unter dem Kopf liegen (sonst Galerie -> Erdgeschoss)
            if (ok && from.y - best > 1.9f)
            {
                ok = false;
                foreach (var h in hits) if (h.normal.y >= 0.6f && from.y - h.point.y <= 1.9f && (!ok || h.point.y < best)) { best = h.point.y; ok = true; }
            }
            if (ok) y = best;
            return ok;
        }

        /// <summary>Aufstehen: Kopf auf Stehhoehe ueber dem Boden.</summary>
        void StandUp()
        {
            if (Head == null) return;
            var fwd = m_Seat != null ? m_Seat.forward : Head.forward;
            var feet = StandSpot(Head.position, fwd);
            MoveRig?.Invoke(Quaternion.identity, Head.position, feet + Vector3.up * StandingEye);
            m_FeetY = feet.y; m_HasFeet = true; m_Seated = false;
        }

        /// <summary>Fusspunkt beim Aufstehen: freier Bodenplatz neben dem Moebel (zuerst in Sitzrichtung, dann seitlich/hinten).</summary>
        public static Vector3 StandSpot(Vector3 head, Vector3 seatForward)
        {
            if (!FindFloor(head, out var fy)) fy = head.y - 1.15f;
            if (FindFreeSpot(head, seatForward, fy, out var spot)) return spot;
            return new Vector3(head.x, fy, head.z);
        }

        static bool FindFreeSpot(Vector3 from, Vector3 seatForward, float floorY, out Vector3 spot)
        {
            spot = default;
            var fwd = Vector3.ProjectOnPlane(seatForward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var origin = new Vector3(from.x, floorY, from.z);
            float[] angles = { 0, -25, 25, -50, 50, -90, 90, -135, 135, 180 };
            // 1. Durchgang: vorne (bis ±50°, bis 1.3 m); 2. Durchgang: naechster freier Platz rundum
            for (int pass = 0; pass < 2; pass++)
            {
            var blocked = new bool[angles.Length];
            for (float d = 0.35f; d <= (pass == 0 ? 1.3f : 2.2f); d += 0.1f)
            {
                for (int ai = 0; ai < angles.Length; ai++)
                {
                    if (blocked[ai] || (pass == 0 && Mathf.Abs(angles[ai]) > 50f)) continue;
                    var dir = Quaternion.Euler(0, angles[ai], 0) * fwd;
                    var p = origin + dir * d;
                    // Weg vom Sitz zum Platz darf keine Wand kreuzen (sonst landet man hinter/in der Wand)
                    // (Wand = blockiert in 1.0 m und 1.45 m Hoehe; Bett/Lehne nur unten, Lampe nur oben)
                    if (Blocked(from, p, floorY + 1.0f) && Blocked(from, p, floorY + 1.45f)) { blocked[ai] = true; continue; }
                    if (IsFreeFloor(p, floorY, out var y)) { spot = new Vector3(p.x, y, p.z); return true; }
                }
            }
            }
            return false;
        }

        static bool Blocked(Vector3 a, Vector3 b, float h) =>
            Physics.Linecast(new Vector3(a.x, h, a.z), new Vector3(b.x, h, b.z), ~0, QueryTriggerInteraction.Ignore);

        /// <summary>Teleportziel erlaubt? Nur Boden: hoechstens 0.25 m ueber/unter der aktuellen Bodenhoehe (Treppenstufen ja, Moebel nein) und Platz fuer eine Person.</summary>
        public bool CanStandAt(Vector3 p)
        {
            float cur;
            if (m_HasFeet && !m_Seated) cur = m_FeetY;
            else if (Head == null || !FindFloor(Head.position, out cur)) return false;
            if (Mathf.Abs(p.y - cur) > 0.25f) return false;
            var a = new Vector3(p.x, p.y + 0.45f, p.z); var b = new Vector3(p.x, p.y + 1.6f, p.z);
            return !Physics.CheckCapsule(a, b, 0.2f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Freier Bodenplatz: oberste Flaeche ist der Boden (Teppich erlaubt) und eine Person passt hin.</summary>
        static bool IsFreeFloor(Vector3 p, float floorY, out float y)
        {
            y = floorY;
            if (!Physics.Raycast(new Vector3(p.x, floorY + 2.0f, p.z), Vector3.down, out var hit, 2.6f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.normal.y < 0.7f || Mathf.Abs(hit.point.y - floorY) > 0.12f) return false;   // Moebel/Treppe/Loch
            y = hit.point.y;
            var a = new Vector3(p.x, y + 0.45f, p.z); var b = new Vector3(p.x, y + 1.6f, p.z);
            return !Physics.CheckCapsule(a, b, 0.24f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Koerpergroesse geaendert: stehend sofort anpassen.</summary>
        public void HeightChanged()
        {
            if (m_Seated || !m_HasFeet || Head == null) return;
            MoveRig?.Invoke(Quaternion.identity, Head.position, new Vector3(Head.position.x, m_FeetY + StandingEye, Head.position.z));
        }

        /// <summary>Drehen in 15°-Schritten um den Kopf (rechter Stick links/rechts, Totzone mit Rueckstellung).</summary>
        public void TickSnapTurn(float stickX)
        {
            int step = Core.Storage.Settings.turnAngle;
            if (step <= 0)
            {
                // gleitend: bis 90°/s, sanft ansteigend ab der Totzone
                float a = Mathf.Abs(stickX);
                if (a < 0.2f || Head == null) return;
                float deg = Mathf.Sign(stickX) * Mathf.SmoothStep(0f, 1f, (a - 0.2f) / 0.8f) * 90f * Time.deltaTime;
                MoveRig?.Invoke(Quaternion.Euler(0, deg, 0), Head.position, Head.position);
                return;
            }
            if (Mathf.Abs(stickX) < 0.35f) { m_SnapReady = true; return; }
            if (!m_SnapReady || Mathf.Abs(stickX) < 0.7f || Head == null) return;
            m_SnapReady = false;
            var rot = Quaternion.Euler(0, Mathf.Sign(stickX) * step, 0);
            MoveRig?.Invoke(rot, Head.position, Head.position);
        }

        /// <summary>Freie Bewegung relativ zur Blickrichtung (linker Stick). Im Stehen; vom Sitzplatz aus wird zuerst aufgestanden.</summary>
        public void TickMove(Vector2 stick)
        {
            if (Head == null) return;
            bool moving = stick.sqrMagnitude > 0.04f;
            if (!moving) { m_FallSpeed = 0; return; }
            if (m_Seated || !m_HasFeet) StandUp();
            var fwd = Vector3.ProjectOnPlane(Head.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, fwd);
            var wish = (fwd * stick.y + right * stick.x) * Speed;

            // Koerper unter den Kopf stellen (Fuesse auf gemerkter Bodenhoehe), bewegen, Kopf nachfuehren
            var feet = new Vector3(Head.position.x, m_FeetY, Head.position.z);
            m_Body.enabled = false;
            m_Body.transform.position = feet;
            m_Body.enabled = true;
            m_FallSpeed = m_Body.isGrounded ? -0.5f : m_FallSpeed - 9.81f * Time.deltaTime;
            m_Body.Move((wish + Vector3.up * m_FallSpeed) * Time.deltaTime);
            var delta = m_Body.transform.position - feet;
            m_Body.enabled = false;
            if (delta.y < -3f) delta.y = 0; // Sicherheitsnetz: nie durch den Boden fallen
            m_FeetY += delta.y;
            if (delta.sqrMagnitude > 1e-8f) MoveRig?.Invoke(Quaternion.identity, Head.position, Head.position + delta);
        }

        /// <summary>Teleport: stehend auf den Zielpunkt am Boden.</summary>
        public void TeleportTo(Vector3 floorPoint)
        {
            if (Head == null) return;
            MoveRig?.Invoke(Quaternion.identity, Head.position, floorPoint + Vector3.up * StandingEye);
            m_FeetY = floorPoint.y; m_HasFeet = true; m_Seated = false;
        }
    }

    /// <summary>Dezente, anklickbare Markierung eines Sitzplatzes: halbtransparenter Kreis auf der Sitzflaeche.</summary>
    public sealed class SeatMarker : MonoBehaviour
    {
        public Transform Seat;
        static Material s_Mat, s_MatHover;
        static Texture2D s_Tex;
        MeshRenderer m_Disc; bool m_Hover;

        static Texture2D DiscTexture()
        {
            if (s_Tex != null) return s_Tex;
            const int N = 128;
            s_Tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "SeatDisc", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2 - 1, dy = (y + 0.5f) / N * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = Mathf.Clamp01((1f - r) / 0.04f);                       // weicher Aussenrand
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.88f) / 0.07f);      // feiner Ring
                    float a = edge * Mathf.Max(0.35f, ring);                             // Flaeche schwach, Ring kraeftiger
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            s_Tex.SetPixels32(px); s_Tex.Apply(true, true);
            return s_Tex;
        }

        public static SeatMarker Create(Transform seat, TMP_FontAsset font)
        {
            var go = new GameObject("SeatMarker_" + seat.name);
            var m = go.AddComponent<SeatMarker>();
            m.Seat = seat;
            // auf die Sitzflaeche unter dem Augenpunkt legen
            var explicitPt = seat.Find("Marker");
            go.transform.position = explicitPt != null ? explicitPt.position + Vector3.up * 0.015f : FindSeatSurface(seat) + Vector3.up * 0.015f;
            var col = go.AddComponent<SphereCollider>(); col.radius = 0.25f; col.isTrigger = true;

            if (s_Mat == null)
            {
                s_Mat = new Material(Shader.Find("PFD/Overlay")) { color = new Color(1f, 1f, 1f, 0.32f), mainTexture = DiscTexture() };
                s_MatHover = new Material(s_Mat) { color = new Color(0.55f, 0.9f, 1f, 0.75f) };
            }
            var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(disc.GetComponent<Collider>());
            disc.name = "Disc";
            disc.transform.SetParent(go.transform, false);
            disc.transform.localRotation = Quaternion.Euler(90, 0, 0);
            disc.transform.localScale = new Vector3(0.34f, 0.34f, 1);
            m.m_Disc = disc.GetComponent<MeshRenderer>();
            m.m_Disc.sharedMaterial = s_Mat;
            m.m_Disc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return m;
        }

        /// <summary>
        /// Sitzflaeche unter dem Augenpunkt suchen: Flaeche mind. 25 cm ueber dem Boden; liegt der Augenpunkt vor dem Polster,
        /// schrittweise nach hinten (gegen die Blickrichtung) versetzt probieren.
        /// </summary>
        static Vector3 FindSeatSurface(Transform seat)
        {
            var back = -Vector3.ProjectOnPlane(seat.forward, Vector3.up).normalized;
            foreach (var off in new[] { 0f, 0.15f, 0.3f, 0.45f })
            {
                var from = seat.position + back * off;
                var hits = Physics.RaycastAll(from, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore);
                if (hits.Length == 0) continue;
                float floor = float.MaxValue; foreach (var h in hits) floor = Mathf.Min(floor, h.point.y);
                RaycastHit? best = null;
                foreach (var h in hits)
                    if (h.normal.y > 0.6f && h.point.y > floor + 0.25f && h.point.y < seat.position.y - 0.3f && (best == null || h.point.y > best.Value.point.y)) best = h;
                if (best != null) return best.Value.point;
            }
            return seat.position + Vector3.down * 0.65f;
        }

        public void SetHover(bool on)
        {
            if (on == m_Hover || m_Disc == null) return;
            m_Hover = on; m_Disc.sharedMaterial = on ? s_MatHover : s_Mat;
        }

        public void Face(Transform head) { }
    }
}
