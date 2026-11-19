using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Valebound
{
    /// <summary>
    /// Touch input hub. Feeds PlayerController, camera rig, and UI buttons with a
    /// single normalized input source. Also handles keyboard for editor testing.
    /// </summary>
    public class TouchInputHub : MonoBehaviour
    {
        public static TouchInputHub Instance { get; private set; }

        [Header("Refs")]
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerCameraRig cameraRig;

        [Header("Camera swipe (anywhere outside joystick & buttons)")]
        [SerializeField] private float swipeSensitivity = 1f;

        private Vector2 moveInput;
        private bool sprintHeld;
        private bool jumpQueued;

        // keyboard fallback state
        private readonly Vector2[] touchStart = new Vector2[10];
        private readonly int[] touchFinger = new int[10];
        private int camFinger = -1;
        private Vector2 camLastPos;
        private bool joystickTouch;
        private bool jumpButtonTouch;

        public Vector2 MoveInput => moveInput;
        public bool SprintHeld => sprintHeld;
        public bool ConsumeJump() { bool j = jumpQueued; jumpQueued = false; return j; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (player == null && GameManager.Instance != null && GameManager.Instance.Player != null)
                player = GameManager.Instance.Player;
        }

        private void Update()
        {
            HandleKeyboard();
            HandleTouch();
            PushToPlayer();
        }

        private void PushToPlayer()
        {
            if (player == null)
            {
                var gm = GameManager.Instance;
                if (gm != null && gm.Player != null) player = gm.Player;
            }
            if (player == null) return;

            player.MoveInput = moveInput;
            player.SprintHeld = sprintHeld;
            if (ConsumeJump()) player.JumpPressed = true;
        }

        // ---------------- keyboard (dev) ----------------

        private void HandleKeyboard()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            float x = Input.GetAxisRaw("Horizontal");
            float y = Input.GetAxisRaw("Vertical");
            if (x != 0f || y != 0f) moveInput = new Vector2(x, y).normalized;
            else if (!AnyTouchMove()) moveInput = Vector2.zero;

            if (Input.GetKey(KeyCode.LeftShift)) sprintHeld = true;
            else if (!AnyTouchSprint()) sprintHeld = false;

            if (Input.GetKeyDown(KeyCode.Space)) jumpQueued = true;

            if (cameraRig != null)
            {
                float mx = Input.GetAxisRaw("Mouse X");
                float my = Input.GetAxisRaw("Mouse Y");
                if (mx != 0f || my != 0f)
                    cameraRig.AddOrbitInput(mx * 14f, my * 14f);
                float wheel = Input.GetAxisRaw("Mouse ScrollWheel");
                if (wheel != 0f) cameraRig.AddZoomInput(-wheel * 40f);
            }
#endif
        }

        // ---------------- touch ----------------

        private void HandleTouch()
        {
            joystickTouch = false; jumpButtonTouch = false;
            bool anyMove = false; bool anySprint = false;

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                bool overUI = IsPointerOverUI(t.fingerId);

                if (overUI) { if (t.fingerId == camFinger) camFinger = -1; continue; }

                if (t.phase == TouchPhase.Began)
                {
                    if (camFinger == -1)
                    {
                        camFinger = t.fingerId;
                        camLastPos = t.position;
                    }
                }
                else if (t.fingerId == camFinger)
                {
                    if (t.phase == TouchPhase.Moved)
                    {
                        Vector2 delta = t.deltaPosition;
                        if (cameraRig != null)
                            cameraRig.AddOrbitInput(delta.x * swipeSensitivity, delta.y * swipeSensitivity);
                        camLastPos = t.position;
                    }
                    else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                    {
                        camFinger = -1;
                    }
                }
            }
        }

        private bool IsPointerOverUI(int fingerId)
        {
            if (EventSystem.current == null) return false;
            return EventSystem.current.IsPointerOverGameObject(fingerId);
        }

        private bool AnyTouchMove() => false;   // joystick sets moveInput directly
        private bool AnyTouchSprint() => false; // sprint button sets sprintHeld directly

        // ---------------- called by UI joystick / buttons ----------------

        public void SetMove(Vector2 v)
        {
            moveInput = Vector2.ClampMagnitude(v, 1f);
        }

        public void SetSprint(bool held)
        {
            sprintHeld = held;
        }

        public void SetVehiclePedal(float v)
        {
            vehiclePedal = Mathf.Clamp(v, -1f, 1f);
        }

        public float VehiclePedal => vehiclePedal;

        private float vehiclePedal;

        public void QueueJump()
        {
            jumpQueued = true;
        }

        public void BindPlayer(PlayerController p)
        {
            player = p;
        }
    }
}
