using UnityEngine;
using YVR.Core;

namespace PFDReader.Input
{
    public enum Hand { Left = 0, Right = 1 }

    /// <summary>Zustand eines Controllers in diesem Frame (YVRInput, Raw-Mapping).</summary>
    public struct ControllerState
    {
        public bool connected;
        public Pose pose;            // Weltpose des Controllers
        public bool trigger, triggerDown, triggerUp;
        public bool grip, gripDown, gripUp;
        public bool primaryDown;     // A / X
        public bool secondaryDown;   // B / Y
        public bool menuDown;        // Menue-Taste (nur links)
        public Vector2 stick;
        public bool stickClickDown;
    }

    /// <summary>Liest beide Controller ueber das YVR-SDK und liefert Posen aus dem Kamera-Rig.</summary>
    public sealed class Controllers
    {
        readonly Transform m_Left, m_Right;
        public ControllerState Left, Right;

        public Controllers(Transform leftAnchor, Transform rightAnchor) { m_Left = leftAnchor; m_Right = rightAnchor; }

        public ref ControllerState Get(Hand h) => ref (h == Hand.Left ? ref Left : ref Right);

        public void Update()
        {
            Read(ref Left, Hand.Left);
            Read(ref Right, Hand.Right);
        }

        void Read(ref ControllerState s, Hand h)
        {
            var anchor = h == Hand.Left ? m_Left : m_Right;
            var type = h == Hand.Left ? ControllerType.LeftTouch : ControllerType.RightTouch;
            s.connected = anchor != null && anchor.gameObject.activeInHierarchy;
            if (anchor != null) s.pose = new Pose(anchor.position, anchor.rotation);
#if UNITY_EDITOR
            // Editor ohne Headset: Maus/Tastatur-Ersatz ist nicht vorgesehen; Werte bleiben neutral.
            s.trigger = s.triggerDown = s.triggerUp = s.grip = s.gripDown = s.gripUp = false;
            s.primaryDown = s.secondaryDown = s.menuDown = s.stickClickDown = false;
            s.stick = Vector2.zero;
#else
            var trig = h == Hand.Left ? YVRInput.RawButton.LIndexTrigger : YVRInput.RawButton.RIndexTrigger;
            var grip = h == Hand.Left ? YVRInput.RawButton.LHandTrigger : YVRInput.RawButton.RHandTrigger;
            var prim = h == Hand.Left ? YVRInput.RawButton.X : YVRInput.RawButton.A;
            var sec = h == Hand.Left ? YVRInput.RawButton.Y : YVRInput.RawButton.B;
            var click = h == Hand.Left ? YVRInput.RawButton.LThumbstick : YVRInput.RawButton.RThumbstick;
            var axis = h == Hand.Left ? YVRInput.RawAxis2D.LThumbstick : YVRInput.RawAxis2D.RThumbstick;
            s.trigger = YVRInput.Get(trig, type); s.triggerDown = YVRInput.GetDown(trig, type); s.triggerUp = YVRInput.GetUp(trig, type);
            s.grip = YVRInput.Get(grip, type); s.gripDown = YVRInput.GetDown(grip, type); s.gripUp = YVRInput.GetUp(grip, type);
            s.primaryDown = YVRInput.GetDown(prim, type);
            s.secondaryDown = YVRInput.GetDown(sec, type);
            s.menuDown = h == Hand.Left && YVRInput.GetDown(YVRInput.RawButton.Start, type);
            s.stickClickDown = YVRInput.GetDown(click, type);
            s.stick = YVRInput.Get(axis, type);
#endif
        }

        public void Vibrate(Hand h, float amplitude = 0.25f, float seconds = 0.03f)
        {
#if !UNITY_EDITOR
            try { YVRInput.SetControllerVibration(0.5f, amplitude, seconds, h == Hand.Left ? ControllerType.LeftTouch : ControllerType.RightTouch); } catch { }
#endif
        }
    }
}
