using UnityEngine;

namespace PFDReader.View
{
    /// <summary>Eigenstaendiges, frei greifbares Fenster (z. B. Einstellungen) mit fester Groesse, unabhaengig vom Lesebereich.</summary>
    public sealed class FloatingWindow : MonoBehaviour
    {
        bool m_Grabbing;
        Pose m_Offset;
        Transform m_Hand;
        public bool Grabbing => m_Grabbing;

        /// <summary>Vor den Blick setzen: leicht unter Augenhoehe, zum Kopf gedreht.</summary>
        public void PlaceInFront(Transform head, float distance = 0.72f)
        {
            if (head == null) return;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            var pos = head.position + fwd * distance + Vector3.down * 0.12f;
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(pos - head.position, Vector3.up);
        }

        public void BeginGrab(Transform hand)
        {
            m_Grabbing = true; m_Hand = hand;
            var inv = Quaternion.Inverse(hand.rotation);
            m_Offset = new Pose(inv * (transform.position - hand.position), inv * transform.rotation);
        }

        public void UpdateGrab(Vector2 stick)
        {
            if (!m_Grabbing || m_Hand == null) return;
            if (Mathf.Abs(stick.y) > 0.25f)
            {
                float len = m_Offset.position.magnitude;
                float target = Mathf.Clamp(len * (1f + stick.y * 1.2f * Time.deltaTime), 0.2f, 3f);
                if (len > 1e-3f) m_Offset.position *= target / len;
            }
            transform.position = m_Hand.position + m_Hand.rotation * m_Offset.position;
            transform.rotation = m_Hand.rotation * m_Offset.rotation;
        }

        public void EndGrab() { m_Grabbing = false; m_Hand = null; }
    }
}
