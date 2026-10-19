using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PFDReader.Input
{
    /// <summary>
    /// UGUI-Eingabe fuer Weltraum-Canvases per Zeigestrahl: eine unsichtbare Kamera sitzt am Controller und schaut
    /// entlang des Strahls; der Zeiger liegt in ihrer Bildmitte. Unterstuetzt Hover, Klick, Ziehen (ScrollRect) und Scrollrad.
    /// </summary>
    public sealed class XRUIInputModule : BaseInputModule
    {
        public Camera RayCamera { get; private set; }
        PointerEventData m_Data;
        readonly List<RaycastResult> m_Results = new List<RaycastResult>();

        // vom Interaktionscontroller gesetzt
        public Ray CurrentRay;
        public bool PointerActive;
        public bool Pressed, PressedThisFrame, ReleasedThisFrame;
        public Vector2 Scroll;

        public RaycastResult CurrentHit { get; private set; }
        public bool HasHit => CurrentHit.gameObject != null;
        public bool IsDragging => m_Data != null && m_Data.dragging;

        protected override void Awake()
        {
            base.Awake();
            var go = new GameObject("UI Ray Camera");
            go.transform.SetParent(transform, false);
            RayCamera = go.AddComponent<Camera>();
            RayCamera.enabled = false;
            RayCamera.clearFlags = CameraClearFlags.Nothing;
            RayCamera.cullingMask = 0;
            RayCamera.nearClipPlane = 0.01f;
            RayCamera.farClipPlane = 20f;
            RayCamera.fieldOfView = 2f;
            RayCamera.targetTexture = new RenderTexture(64, 64, 0) { name = "UI Ray Camera RT" };
        }

        public override bool ShouldActivateModule() => true;
        public override bool IsPointerOverGameObject(int pointerId) => HasHit;

        public override void Process()
        {
            if (m_Data == null) m_Data = new PointerEventData(eventSystem) { pointerId = 1 };
            if (!PointerActive)
            {
                if (m_Data.pointerEnter != null) HandlePointerExitAndEnter(m_Data, null);
                CurrentHit = default;
                return;
            }
            RayCamera.transform.SetPositionAndRotation(CurrentRay.origin, Quaternion.LookRotation(CurrentRay.direction));
            var center = new Vector2(RayCamera.pixelWidth * 0.5f, RayCamera.pixelHeight * 0.5f);
            m_Data.Reset();
            m_Data.delta = center - m_Data.position;
            m_Data.position = center;
            m_Data.scrollDelta = Scroll;
            m_Data.button = PointerEventData.InputButton.Left;

            m_Results.Clear();
            eventSystem.RaycastAll(m_Data, m_Results);
            var hit = FindFirstRaycast(m_Results);
            CurrentHit = hit;
            m_Data.pointerCurrentRaycast = hit;

            ProcessPress(hit);
            HandlePointerExitAndEnter(m_Data, hit.gameObject);
            ProcessDrag(m_Data);
            if (Scroll.sqrMagnitude > 0f && hit.gameObject != null)
            {
                var scrollHandler = ExecuteEvents.GetEventHandler<IScrollHandler>(hit.gameObject);
                ExecuteEvents.ExecuteHierarchy(scrollHandler, m_Data, ExecuteEvents.scrollHandler);
            }
        }

        void ProcessPress(RaycastResult hit)
        {
            var go = hit.gameObject;
            if (PressedThisFrame)
            {
                m_Data.eligibleForClick = true;
                m_Data.delta = Vector2.zero;
                m_Data.dragging = false;
                m_Data.useDragThreshold = true;
                m_Data.pressPosition = m_Data.position;
                m_Data.pointerPressRaycast = hit;
                if (eventSystem.currentSelectedGameObject != null && ExecuteEvents.GetEventHandler<ISelectHandler>(go) != eventSystem.currentSelectedGameObject) eventSystem.SetSelectedGameObject(null, m_Data);
                var press = ExecuteEvents.ExecuteHierarchy(go, m_Data, ExecuteEvents.pointerDownHandler);
                if (press == null) press = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
                m_Data.pointerPress = press;
                m_Data.rawPointerPress = go;
                m_Data.clickTime = Time.unscaledTime;
                m_Data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(go);
                if (m_Data.pointerDrag != null) ExecuteEvents.Execute(m_Data.pointerDrag, m_Data, ExecuteEvents.initializePotentialDrag);
            }
            if (ReleasedThisFrame)
            {
                ExecuteEvents.Execute(m_Data.pointerPress, m_Data, ExecuteEvents.pointerUpHandler);
                var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
                if (m_Data.pointerPress == clickHandler && m_Data.eligibleForClick)
                    ExecuteEvents.Execute(m_Data.pointerPress, m_Data, ExecuteEvents.pointerClickHandler);
                if (m_Data.pointerDrag != null && m_Data.dragging)
                    ExecuteEvents.Execute(m_Data.pointerDrag, m_Data, ExecuteEvents.endDragHandler);
                m_Data.eligibleForClick = false;
                m_Data.pointerPress = null;
                m_Data.rawPointerPress = null;
                m_Data.dragging = false;
                m_Data.pointerDrag = null;
            }
        }

        void ProcessDrag(PointerEventData d)
        {
            if (!Pressed || d.pointerDrag == null) return;
            if (!d.dragging)
            {
                // Schwelle in Canvas-Einheiten statt Pixel
                if ((d.pointerCurrentRaycast.worldPosition - d.pointerPressRaycast.worldPosition).magnitude > 0.012f)
                {
                    ExecuteEvents.Execute(d.pointerDrag, d, ExecuteEvents.beginDragHandler);
                    d.dragging = true;
                    d.eligibleForClick = false;
                    if (d.pointerPress != d.pointerDrag) { ExecuteEvents.Execute(d.pointerPress, d, ExecuteEvents.pointerUpHandler); d.pointerPress = null; }
                }
            }
            if (d.dragging) ExecuteEvents.Execute(d.pointerDrag, d, ExecuteEvents.dragHandler);
        }
    }
}
