using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Valebound.UI
{
    /// <summary>
    /// Virtual joystick: drag anywhere in its zone; knob follows within radius;
    /// outputs normalized direction to TouchInputHub. Left or right handed.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        [SerializeField] private RectTransform knob;
        [SerializeField] private RectTransform baseRing;
        [SerializeField] private float maxRadius = 72f;
        [SerializeField] private bool isVehicleSteering;

        public Vector2 Value { get; private set; }
        public bool IsHeld { get; private set; }

        private CanvasGroup canvasGroup;
        private Vector2 baseCenter;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (baseRing != null) baseCenter = baseRing.position;
        }

        private void Start()
        {
            ApplyHandedness();
        }

        private void ApplyHandedness()
        {
            var settings = SaveSystem.LoadSettings();
            if (!settings.leftHanded) return;
            var rt = transform as RectTransform;
            if (rt == null || rt.parent == null) return;
            // mirror across parent center
            Vector2 anchor = rt.anchorMin;
            rt.anchorMin = new Vector2(1f - anchor.x, anchor.y);
            rt.anchorMax = rt.anchorMin;
            rt.anchoredPosition = new Vector2(-rt.anchoredPosition.x, rt.anchoredPosition.y);
        }

        public void OnPointerDown(PointerEventData e)
        {
            IsHeld = true;
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            OnDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!IsHeld) return;
            Vector2 delta = e.position - (Vector2)baseRing.position;
            Vector2 clamped = Vector2.ClampMagnitude(delta, maxRadius);
            if (knob != null) knob.anchoredPosition = clamped;
            Value = clamped / maxRadius;
            if (TouchInputHub.Instance != null) TouchInputHub.Instance.SetMove(Value);
        }

        public void OnPointerUp(PointerEventData e)
        {
            IsHeld = false;
            Value = Vector2.zero;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
            if (TouchInputHub.Instance != null) TouchInputHub.Instance.SetMove(Vector2.zero);
            if (canvasGroup != null) canvasGroup.alpha = 0.55f;
        }
    }

    /// <summary>Simple held-button: sprint, brake, handbrake.</summary>
    [RequireComponent(typeof(Image))]
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public enum HoldAction { Sprint, Brake, Handbrake }

        [SerializeField] private HoldAction action;

        public void OnPointerDown(PointerEventData e)
        {
            var hub = TouchInputHub.Instance;
            if (hub == null) return;
            switch (action)
            {
                case HoldAction.Sprint: hub.SetSprint(true); break;
                case HoldAction.Brake: hub.SetSprint(true); break;       // brake maps to reverse pedal too
                case HoldAction.Handbrake: hub.SetSprint(true); break;
            }
        }

        public void OnPointerUp(PointerEventData e)
        {
            var hub = TouchInputHub.Instance;
            if (hub == null) return;
            switch (action)
            {
                case HoldAction.Sprint:
                case HoldAction.Brake:
                case HoldAction.Handbrake:
                    hub.SetSprint(false); break;
            }
        }
    }

    /// <summary>Touch button firing one-shot actions (jump, interact, horn...).</summary>
    [RequireComponent(typeof(Image))]
    public class TapButton : MonoBehaviour, IPointerDownHandler
    {
        public enum TapAction { Jump, Interact, EnterExitVehicle, Horn, Camera, Map, Pause, Headlights, Radio }

        [SerializeField] private TapAction action;

        public void OnPointerDown(PointerEventData e)
        {
            var hub = TouchInputHub.Instance;
            var gm = GameManager.Instance;
            var ui = UIManager.Instance;
            switch (action)
            {
                case TapAction.Jump: hub?.QueueJump(); break;
                case TapAction.Interact:
                    gm?.Player?.GetComponent<PlayerInteraction>()?.TryInteract();
                    break;
                case TapAction.EnterExitVehicle:
                    var v = gm?.CurrentVehicle;
                    if (v != null) v.EjectOccupant();
                    else gm?.Player?.GetComponent<PlayerInteraction>()?.TryInteract();
                    break;
                case TapAction.Horn:
                    if (gm != null && gm.CurrentVehicle != null) gm.CurrentVehicle.Controller.PlayHorn();
                    break;
                case TapAction.Map: ui?.ToggleMap(); break;
                case TapAction.Pause: ui?.TogglePause(); break;
                case TapAction.Headlights:
                    if (gm != null && gm.CurrentVehicle != null) gm.CurrentVehicle.Controller.ToggleHeadlights();
                    break;
                case TapAction.Radio:
                    Systems.AudioManager.Instance?.PlayStation(UnityEngine.Random.value < 0.5f ? "lofi" : "calm");
                    break;
                case TapAction.Camera: break; // camera cycle reserved
            }
        }
    }

    /// <summary>Vehicle pedals: throttle (up) and reverse/brake (down) as vertical sliders.</summary>
    [RequireComponent(typeof(Image))]
    public class VehiclePedal : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public enum Pedal { Throttle, Reverse }

        [SerializeField] private Pedal pedal;

        private static float throttle;
        private static float steerX;

        public void OnPointerDown(PointerEventData e)
        {
            var hub = TouchInputHub.Instance;
            if (hub == null) return;
            if (pedal == Pedal.Throttle) hub.SetVehiclePedal(1f);
            else hub.SetVehiclePedal(-1f);
        }

        public void OnPointerUp(PointerEventData e)
        {
            TouchInputHub.Instance?.SetVehiclePedal(0f);
        }
    }

    /// <summary>Notification toast UI with simple fade in/out.</summary>
    public class NotificationToast : MonoBehaviour
    {
        [SerializeField] private TMPro.TextMeshProUGUI titleText;
        [SerializeField] private TMPro.TextMeshProUGUI bodyText;
        [SerializeField] private CanvasGroup canvasGroup;

        public void Show(string title, string body, float duration)
        {
            if (titleText != null) titleText.text = title;
            if (bodyText != null) bodyText.text = body;
            if (canvasGroup != null) canvasGroup.alpha = 1f;
        }
    }
}
