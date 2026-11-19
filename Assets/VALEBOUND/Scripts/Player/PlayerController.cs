using System;
using UnityEngine;

namespace Valebound
{
    /// <summary>
    /// Third-person character controller tuned for touch.
    /// Rigidbody-driven, capsule-grounded, with stamina sprint and jump buffering.
    /// Animation is driven via an Animator with parameters MoveX/MoveY/Speed/IsGrounded/Jump/Grounded.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float walkSpeed = 3.2f;
        [SerializeField] private float runSpeed = 5.6f;
        [SerializeField] private float sprintSpeed = 8.0f;
        [SerializeField] private float acceleration = 14f;
        [SerializeField] private float deceleration = 18f;
        [SerializeField] private float turnSpeed = 11f;
        [SerializeField] private float gravity = -22f;

        [Header("Jump")]
        [SerializeField] private float jumpHeight = 1.15f;
        [SerializeField] private float coyoteTime = 0.12f;
        [SerializeField] private float jumpBuffer = 0.15f;
        [SerializeField] private float airControl = 0.55f;

        [Header("Stats")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float maxStamina = 100f;
        [SerializeField] private float sprintStaminaDrain = 14f;
        [SerializeField] private float staminaRegen = 18f;
        [SerializeField] private float staminaRegenDelay = 0.8f;

        [Header("Camera")]
        [SerializeField] private PlayerCameraRig cameraRig;

        [Header("Effects")]
        [SerializeField] private ParticleSystem dustPfx;
        [SerializeField] private float dustInterval = 0.28f;

        public event Action<Vector3, float> OnFootstep;      // pos, intensity
        public event Action<float> OnHealthChanged;
        public event Action<float> OnStaminaChanged;
        public event Action OnJumped;
        public event Action OnLanded;

        public CharacterController Controller { get; private set; }
        public PlayerCameraRig CameraRig => cameraRig;
        public bool IsSprinting { get; private set; }
        public bool IsGrounded { get; private set; }
        public Vector2 MoveInput { get; set; }
        public bool SprintHeld { get; set; }
        public bool JumpPressed { get; set; }
        public float CurrentHealth { get; private set; }
        public float CurrentStamina { get; private set; }
        public float MaxHealth => maxHealth;
        public float MaxStamina => maxStamina;
        public bool InVehicle { get; set; }

        private Vector3 velocity;
        private float lastGroundedTime;
        private float lastJumpPressTime;
        private float staminaIdleTimer;
        private float turnVelocity;
        private float dustTimer;
        private float footstepTimer;
        private float footstepInterval = 0.42f;
        private Animator animator;
        private static readonly int AnimMoveX = Animator.StringToHash("MoveX");
        private static readonly int AnimMoveY = Animator.StringToHash("MoveY");
        private static readonly int AnimSpeed = Animator.StringToHash("Speed");
        private static readonly int AnimGrounded = Animator.StringToHash("IsGrounded");
        private static readonly int AnimJump = Animator.StringToHash("Jump");

        private void Awake()
        {
            Controller = GetComponent<CharacterController>();
            animator = GetComponentInChildren<Animator>();
            CurrentHealth = maxHealth;
            CurrentStamina = maxStamina;
        }

        private void OnEnable()
        {
            CurrentHealth = Mathf.Max(CurrentHealth, 1f);
        }

        private void Update()
        {
            if (InVehicle || GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Playing)
            {
                UpdateAnimator(0f, Vector2.zero);
                return;
            }

            float dt = Time.deltaTime;

            // ---------- ground check ----------
            bool wasGrounded = IsGrounded;
            IsGrounded = Controller.isGrounded;
            if (IsGrounded) lastGroundedTime = dt;
            else lastGroundedTime += dt;
            if (IsGrounded && !wasGrounded)
            {
                OnLanded?.Invoke();
                if (velocity.y < -14f) TakeDamage(Mathf.Abs(velocity.y + 14f) * 2.2f, "fall");
                velocity.y = -2f;
            }

            // ---------- stamina & sprint ----------
            IsSprinting = SprintHeld && MoveInput.sqrMagnitude > 0.1f
                          && CurrentStamina > 1f && IsGrounded;
            if (IsSprinting)
            {
                CurrentStamina = Mathf.Max(0f, CurrentStamina - sprintStaminaDrain * dt);
                staminaIdleTimer = 0f;
            }
            else
            {
                staminaIdleTimer += dt;
                if (staminaIdleTimer > staminaRegenDelay)
                    CurrentStamina = Mathf.Min(maxStamina, CurrentStamina + staminaRegen * dt);
            }
            OnStaminaChanged?.Invoke(CurrentStamina / maxStamina);

            // ---------- horizontal movement ----------
            float targetSpeed = 0f;
            if (MoveInput.sqrMagnitude > 0.01f)
            {
                bool running = MoveInput.sqrMagnitude > 0.55f * 0.55f; // joystick pushed past 55%
                targetSpeed = IsSprinting ? sprintSpeed : running ? runSpeed : walkSpeed;
            }

            Vector3 wishDir = Vector3.zero;
            if (cameraRig != null && MoveInput.sqrMagnitude > 0.01f)
            {
                Vector3 fwd = cameraRig.FlatForward;
                Vector3 right = cameraRig.FlatRight;
                wishDir = (fwd * MoveInput.y + right * MoveInput.x).normalized;
            }

            Vector3 horizVel = new Vector3(velocity.x, 0f, velocity.z);
            float speedNow = horizVel.magnitude;
            float accel = targetSpeed > 0.01f ? acceleration : deceleration;
            if (!IsGrounded) accel *= airControl;
            speedNow = Mathf.MoveTowards(speedNow, targetSpeed, accel * dt);

            if (wishDir.sqrMagnitude > 0.01f && speedNow > 0.05f)
            {
                float targetYaw = Mathf.Atan2(wishDir.x, wishDir.z) * Mathf.Rad2Deg;
                float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw, ref turnVelocity, 1f / turnSpeed);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            Vector3 moveDir = speedNow > 0.05f ? transform.forward * speedNow : Vector3.zero;
            velocity.x = moveDir.x;
            velocity.z = moveDir.z;

            // ---------- jump ----------
            if (JumpPressed) lastJumpPressTime = 0f;
            else lastJumpPressTime += dt;
            if (lastGroundedTime <= coyoteTime && lastJumpPressTime <= jumpBuffer)
            {
                velocity.y = Mathf.Sqrt(2f * -gravity * jumpHeight);
                lastGroundedTime = 99f;
                lastJumpPressTime = 99f;
                OnJumped?.Invoke();
                if (animator != null) animator.SetTrigger(AnimJump);
            }
            velocity.y += gravity * dt;

            Controller.Move(velocity * dt);

            // ---------- fx & anim ----------
            UpdateAnimator(speedNow, MoveInput);
            if (IsGrounded && speedNow > 0.5f)
            {
                footstepTimer += dt * (speedNow / walkSpeed);
                dustTimer += dt;
                if (footstepTimer >= footstepInterval)
                {
                    footstepTimer = 0f;
                    OnFootstep?.Invoke(transform.position, speedNow / sprintSpeed);
                }
                if (dustPfx != null && dustTimer >= dustInterval && speedNow > runSpeed)
                {
                    dustTimer = 0f;
                    dustPfx.Play();
                }
            }
        }

        private void UpdateAnimator(float speed, Vector2 input)
        {
            if (animator == null) return;
            // local-space direction for blend tree
            Vector3 local = transform.InverseTransformDirection(new Vector3(velocity.x, 0, velocity.z));
            animator.SetFloat(AnimMoveX, Mathf.Clamp(local.x / runSpeed, -1, 1), 0.1f, Time.deltaTime);
            animator.SetFloat(AnimMoveY, Mathf.Clamp(local.z / runSpeed, -1, 1), 0.1f, Time.deltaTime);
            animator.SetFloat(AnimSpeed, speed / sprintSpeed, 0.1f, Time.deltaTime);
            animator.SetBool(AnimGrounded, IsGrounded);
        }

        // ---------------- stats ----------------

        public void TakeDamage(float amount, string source)
        {
            if (amount <= 0f) return;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            OnHealthChanged?.Invoke(CurrentHealth / maxHealth);
            if (CurrentHealth <= 0f && GameManager.Instance != null)
                GameManager.Instance.TriggerGameOver(source);
        }

        public void Heal(float amount)
        {
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            OnHealthChanged?.Invoke(CurrentHealth / maxHealth);
        }

        public void Teleport(Vector3 pos, bool restoreHealth)
        {
            Controller.enabled = false;
            transform.position = pos;
            velocity = Vector3.zero;
            Controller.enabled = true;
            if (restoreHealth)
            {
                CurrentHealth = maxHealth;
                CurrentStamina = maxStamina;
                OnHealthChanged?.Invoke(1f);
                OnStaminaChanged?.Invoke(1f);
            }
        }

        public void ApplySaveData(SaveData d)
        {
            Teleport(new Vector3(d.px, d.py, d.pz), false);
            transform.rotation = Quaternion.Euler(0f, d.heading, 0f);
            CurrentHealth = Mathf.Clamp(d.health, 1f, maxHealth);
            OnHealthChanged?.Invoke(CurrentHealth / maxHealth);
        }

        public void CaptureSaveData(SaveData d)
        {
            d.px = transform.position.x; d.py = transform.position.y; d.pz = transform.position.z;
            d.heading = transform.eulerAngles.y;
            d.health = CurrentHealth;
        }
    }
}
