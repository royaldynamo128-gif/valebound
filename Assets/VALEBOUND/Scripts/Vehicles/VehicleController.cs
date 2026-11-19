using System;
using UnityEngine;

namespace Valebound.Vehicle
{
    /// <summary>
    /// Data-only description of a vehicle class. Created as ScriptableObjects by
    /// the editor PrefabBuilder from real model names in the Car Kit.
    /// </summary>
    [CreateAssetMenu(fileName = "VehicleDef", menuName = "VALEBOUND/Vehicle Definition")]
    public class VehicleDef : ScriptableObject
    {
        [Header("Identity")]
        public string vehicleId = "sedan";
        public string displayName = "Sedan";
        public VehicleClass vehicleClass = VehicleClass.Sedan;
        public Sprite icon;

        [Header("Dynamics")]
        public float maxSpeedKph = 130f;
        public float reverseSpeedKph = 30f;
        public float acceleration = 9f;          // m/s^2
        public float braking = 16f;
        public float steeringDegPerSec = 75f;
        public float gripFactor = 0.92f;
        public float downforce = 1.2f;
        public float suspensionHeight = 0.28f;
        public float bodyTiltRoll = 4f;          // cosmetic body roll degrees

        [Header("Model")]
        public GameObject modelPrefab;
        public bool isBike;                    // bike dynamics: no reverse accel, stronger yaw, lighter grip

        [Header("Economy")]
        public int price = 0;
        public int unlockMissionIndex = -1;      // story mission that unlocks it (-1 = start)

        [Header("Audio")]
        public AudioClip engineStart;
        public AudioClip engineLoop;
        public AudioClip horn;
    }

    public enum VehicleClass { Sedan, Van, Sports, Pickup, Bus, Motorcycle }

    /// <summary>
    /// Arcade vehicle physics on a single Rigidbody: force-accel, steer-by-yaw,
    /// visual suspension/wheel spin/steer visuals, damage & audio.
    /// Drivable by both player (via TouchInputHub vehicle controls) and AI (traffic).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour, PlayerInteraction.IInteractable
    {
        [Header("Definition")]
        [SerializeField] private VehicleDef def;

        [Header("Wheels (visual only)")]
        [SerializeField] private Transform[] steerWheels;
        [SerializeField] private Transform[] driveWheels;
        [SerializeField] private float wheelRadius = 0.32f;
        [SerializeField] private float maxSteerVisual = 28f;

        [Header("Body visual tilt")]
        [SerializeField] private Transform bodyMesh;
        [SerializeField] private float maxBodyRoll = 5f;
        [SerializeField] private float maxBodyPitch = 3f;

        [Header("Damage")]
        [SerializeField] private float maxDurability = 100f;
        [SerializeField] private GameObject smokePfxPrefab;
        [SerializeField] private GameObject explodePfxPrefab;

        [Header("Lights")]
        [SerializeField] private Light[] headlights;
        [SerializeField] private GameObject[] brakeLightObjects;

        [Header("Audio")]
        [SerializeField] private AudioSource engineSource;
        [SerializeField] private AudioSource hornSource;
        [SerializeField] private float enginePitchMin = 0.8f;
        [SerializeField] private float enginePitchMax = 2.2f;

        public VehicleDef Def => def;
        public bool IsPlayerDriven { get; private set; }
        public float SpeedKph => rb.velocity.magnitude * 3.6f;
        public float Speed01 => Mathf.Clamp01(rb.velocity.magnitude / (def != null ? def.maxSpeedKph / 3.6f : 30f));
        public float Durability { get; private set; }
        public float Durability01 => Durability / maxDurability;
        public string Prompt => IsPlayerDriven ? string.Empty : "Enter " + (def != null ? def.displayName : "Vehicle");

        public event Action<float> OnDamageTaken;
        public event Action<VehicleController> OnDestroyed;

        private Rigidbody rb;
        private float steerInput;    // -1..1
        private float throttleInput; // -1..1
        private bool handbrake;
        private bool headlightsOn;
        private float wheelSpin;
        private float bodyRollVel, bodyPitchVel;
        private float bodyRoll, bodyPitch;
        private Systems.WorldClock clock;
        private PlayerController occupant;
        private Systems.TrafficAIAgent trafficAgent;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            Durability = maxDurability;
            trafficAgent = GetComponent<Systems.TrafficAIAgent>();
        }

        private void Start()
        {
            clock = Systems.WorldClock.Instance;
            // center of mass low for stability
            if (rb != null)
            {
                rb.centerOfMass = new Vector3(0f, -0.35f, 0f);
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
        }

        public void SetDef(VehicleDef newDef)
        {
            def = newDef;
        }

        // ---------------- driving ----------------

        public void SetDriveInput(float steer, float throttle, bool brakeHeld)
        {
            steerInput = Mathf.Clamp(steer, -1f, 1f);
            throttleInput = Mathf.Clamp(throttle, -1f, 1f);
            handbrake = brakeHeld;
        }

        /// <summary>Current steer input for visual components (bike lean, UI).</summary>
        public void GetSteerVisual(out float steer) => steer = steerInput;

        private void FixedUpdate()
        {
            if (def == null || rb == null) return;

            float dt = Time.fixedDeltaTime;
            Vector3 vel = rb.velocity;
            float forwardSpeed = Vector3.Dot(vel, transform.forward);
            bool bike = def.isBike;

            // ---- steering (speed sensitive) ----
            float speedFactor = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 8f);
            float steerAuthority = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(Mathf.Abs(forwardSpeed) / (def.maxSpeedKph / 3.6f)));
            float steerAngle = steerInput * def.steeringDegPerSec * steerAuthority * (forwardSpeed < -0.5f ? -1f : 1f);
            if (Mathf.Abs(forwardSpeed) > 0.2f)
            {
                float yawDelta = steerAngle * Mathf.Deg2Rad * dt * Mathf.Sign(forwardSpeed) * (0.35f + 0.65f * speedFactor);
                yawDelta *= def.gripFactor * (bike ? 1.45f : 1f);   // bikes carve harder
                Quaternion yawRot = Quaternion.Euler(0f, yawDelta * Mathf.Rad2Deg, 0f);
                rb.MoveRotation(rb.rotation * yawRot);
            }

            // ---- accel / brake / reverse ----
            float maxSpeedMs = def.maxSpeedKph / 3.6f;
            float revSpeedMs = def.reverseSpeedKph / 3.6f;
            Vector3 forceDir = Vector3.zero;
            if (throttleInput > 0.01f)
            {
                if (forwardSpeed < maxSpeedMs)
                    forceDir += transform.forward * (throttleInput * def.acceleration * rb.mass);
            }
            else if (throttleInput < -0.01f)
            {
                if (forwardSpeed > 0.5f)
                    forceDir += -transform.forward * (throttleInput * def.braking * rb.mass); // brake
                else if (forwardSpeed > -revSpeedMs && !bike)
                    forceDir += -transform.forward * (-throttleInput * def.acceleration * 0.5f * rb.mass); // reverse
            }
            if (handbrake)
            {
                Vector3 horizVel = new Vector3(vel.x, 0f, vel.z);
                if (horizVel.sqrMagnitude > 0.1f)
                    forceDir += -horizVel.normalized * def.braking * 0.55f * rb.mass;
            }
            rb.AddForce(forceDir, ForceMode.Force);

            // ---- downforce & lateral grip ----
            Vector3 lateral = Vector3.Dot(vel, transform.right) * transform.right;
            rb.AddForce(-lateral * (def.gripFactor * (bike ? 3.5f : 6f)), ForceMode.Acceleration);
            if (IsGroundedWheels())
                rb.AddForce(-transform.up * def.downforce * speed01_g(vel), ForceMode.Acceleration);

            // ---- drag when no throttle ----
            if (Mathf.Abs(throttleInput) < 0.01f && !handbrake)
            {
                Vector3 horiz = new Vector3(vel.x, 0f, vel.z);
                rb.AddForce(-horiz * (bike ? 0.8f : 0.35f), ForceMode.Acceleration);
            }

            // ---- stay upright assist (arcade) ----
            if (transform.up.y < 0.35f && IsGroundedWheels())
            {
                Quaternion upright = Quaternion.FromToRotation(transform.up, Vector3.up) * rb.rotation;
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, upright, 4f * dt));
            }
        }

        private float speed01_g(Vector3 vel) =>
            Mathf.Clamp01(vel.magnitude / (def != null ? def.maxSpeedKph / 3.6f : 30f));

        private bool IsGroundedWheels()
        {
            // cheap: raycast from body center down suspensionHeight + margin
            return Physics.Raycast(transform.position + transform.up * 0.3f, -transform.up, 0.65f, ~0, QueryTriggerInteraction.Ignore);
        }

        private void Update()
        {
            UpdateWheelsAndBody();
            UpdateEngineAudio();
            UpdateLights();
        }

        private void UpdateWheelsAndBody()
        {
            float dt = Time.deltaTime;
            float forwardSpeed = Vector3.Dot(rb.velocity, transform.forward);

            // wheel spin
            wheelSpin += forwardSpeed / wheelRadius * dt * Mathf.Rad2Deg;
            float steerVisual = steerInput * maxSteerVisual;
            foreach (var w in steerWheels)
            {
                if (w == null) continue;
                Vector3 e = w.localEulerAngles;
                w.localRotation = Quaternion.Euler(0f, steerVisual, 0f);
                w.Rotate(wheelSpin * dt * 0f, 0f, 0f); // roll via child? keep simple: spin handled below
            }
            foreach (var w in driveWheels)
            {
                if (w == null) continue;
                w.Rotate(Vector3.right, wheelSpin * dt, Space.Self);
            }

            // body tilt (roll/pitch) — cosmetic
            float targetRoll = -steerInput * maxBodyRoll * Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 12f);
            float targetPitch = -Mathf.Clamp(throttleInput, -1, 1) * maxBodyPitch;
            bodyRoll = Mathf.SmoothDamp(bodyRoll, targetRoll, ref bodyRollVel, 0.15f);
            bodyPitch = Mathf.SmoothDamp(bodyPitch, targetPitch, ref bodyPitchVel, 0.2f);
            if (bodyMesh != null)
                bodyMesh.localRotation = Quaternion.Euler(bodyPitch, 0f, bodyRoll);
        }

        private void UpdateEngineAudio()
        {
            if (engineSource == null) return;
            if (IsPlayerDriven || (trafficAgent != null && trafficAgent.IsAudible))
            {
                if (!engineSource.isPlaying) engineSource.Play();
                engineSource.pitch = Mathf.Lerp(enginePitchMin, enginePitchMax, Speed01);
            }
            else if (engineSource.isPlaying)
            {
                engineSource.Stop();
            }
        }

        private void UpdateLights()
        {
            bool night = clock != null && clock.IsNight;
            if (headlights != null)
            {
                bool on = IsPlayerDriven && (night || headlightsOn);
                foreach (var l in headlights) if (l != null) l.enabled = on;
            }
            if (brakeLightObjects != null)
            {
                bool braking = handbrake || throttleInput < -0.01f;
                foreach (var b in brakeLightObjects) if (b != null) b.SetActive(braking);
            }
        }

        // ---------------- occupancy ----------------

        public bool CanInteract(PlayerController p)
        {
            return !IsPlayerDriven && (trafficAgent == null || !trafficAgent.IsActive);
        }

        public void Interact(PlayerController p)
        {
            var pv = GetComponent<PlayerVehicle>();
            if (pv != null) pv.Enter(p);
        }

        public void SetPlayerDriven(bool driven)
        {
            IsPlayerDriven = driven;
            if (!driven)
            {
                SetDriveInput(0f, 0f, false);
            }
        }

        // ---------------- damage ----------------

        private void OnCollisionEnter(Collision collision)
        {
            float impact = collision.relativeVelocity.magnitude;
            if (impact < 3.5f) return;

            // ignore light taps from pedestrians/props
            if (collision.collider.GetComponentInParent<PlayerController>() != null) return;

            float dmg = (impact - 3.5f) * 4.5f;
            ApplyDamage(dmg);

            if (impact > 7f && Systems.AudioManager.Instance != null)
                Systems.AudioManager.Instance.PlayOneShot3D("sfx/vehicle/crash", transform.position, 0.7f);
        }

        public void ApplyDamage(float amount)
        {
            if (amount <= 0f) return;
            Durability = Mathf.Max(0f, Durability - amount);
            OnDamageTaken?.Invoke(Durability / maxDurability);
            if (Durability <= 0f) DestroyVehicle();
        }

        private void DestroyVehicle()
        {
            if (explodePfxPrefab != null)
                Instantiate(explodePfxPrefab, transform.position, Quaternion.identity);
            if (IsPlayerDriven)
            {
                var pv = GetComponent<PlayerVehicle>();
                if (pv != null) pv.EjectOccupant();
            }
            OnDestroyed?.Invoke(this);
            if (trafficAgent != null) trafficAgent.OnVehicleDestroyed();
            gameObject.SetActive(false);
        }

        public void Repair()
        {
            Durability = maxDurability;
            OnDamageTaken?.Invoke(1f);
        }

        public void ToggleHeadlights()
        {
            headlightsOn = !headlightsOn;
        }

        public void PlayHorn()
        {
            if (hornSource != null && def != null && def.horn != null)
            {
                hornSource.PlayOneShot(def.horn);
            }
            else if (Systems.AudioManager.Instance != null)
            {
                Systems.AudioManager.Instance.PlayOneShot3D("sfx/vehicle/horn", transform.position, 0.8f);
            }
        }
    }

    /// <summary>
    /// Player-facing vehicle wrapper: enter/exit choreography, control binding,
    /// camera mode switch, HUD hookup. Bikes also show/hide their rider rig.
    /// </summary>
    public class PlayerVehicle : MonoBehaviour
    {
        private VehicleController vc;
        private PlayerController player;
        private bool occupied;
        private Transform riderSlot;
        private bool isBike;

        public bool IsOccupied => occupied;
        public VehicleController Controller => vc;
        public bool IsBike => isBike;

        private void Awake()
        {
            vc = GetComponent<VehicleController>();
        }

        /// <summary>Editor VehicleRosterBuilder injects the bike rider seat here.</summary>
        public void SetRiderSlot(Transform slot) => riderSlot = slot;
        public void SetIsBike(bool bike) => isBike = bike;

        public void Enter(PlayerController p)
        {
            if (occupied || vc == null) return;
            occupied = true;
            player = p;
            p.InVehicle = true;
            p.gameObject.SetActive(false);
            vc.SetPlayerDriven(true);
            // bike: rider model on (player capsule hidden anyway)
            if (riderSlot != null) riderSlot.gameObject.SetActive(true);
            GameManager.Instance?.NotifyVehicleEntered(this);
            if (player.CameraRig != null)
            {
                player.CameraRig.Bind(vc.transform);
                player.CameraRig.SetVehicleMode(true);
                player.CameraRig.SnapBehind(vc.transform.eulerAngles.y);
            }
            Systems.AudioManager.Instance?.PlayOneShot3D("sfx/vehicle/door", transform.position, 0.8f);
            if (MissionManager.Instance != null)
                MissionManager.Instance.NotifyVehicleEntered(vc.Def != null ? vc.Def.vehicleId : "");
        }

        public void EjectOccupant(bool silent = false)
        {
            if (!occupied || player == null) { occupied = false; return; }
            Vector3 exitPos = transform.position + transform.right * 1.9f + Vector3.up * 0.4f;
            // smarter exit: try sidewalk side, fall back to right, then behind
            if (Physics.Raycast(transform.position + Vector3.up * 0.6f, transform.right, 1.6f))
                exitPos = transform.position - transform.right * 1.9f + Vector3.up * 0.4f;
            player.gameObject.SetActive(true);
            player.Teleport(exitPos, false);
            player.InVehicle = false;
            vc.SetPlayerDriven(false);
            if (riderSlot != null) riderSlot.gameObject.SetActive(false);
            if (player.CameraRig != null)
            {
                player.CameraRig.Bind(player.transform);
                player.CameraRig.SetVehicleMode(false);
            }
            if (!silent)
            {
                Systems.AudioManager.Instance?.PlayOneShot3D("sfx/vehicle/door", transform.position, 0.8f);
            }
            GameManager.Instance?.NotifyVehicleExited(this);
            occupied = false;
            player = null;
        }

        private void Update()
        {
            if (!occupied || player == null) return;

            var hub = TouchInputHub.Instance;
            if (hub == null) return;

            // drive input: steering from joystick X, throttle from pedal (joystick Y as fallback)
            float steer = hub.MoveInput.x;
            float throttle = Mathf.Abs(hub.VehiclePedal) > 0.05f ? hub.VehiclePedal : hub.MoveInput.y;
            vc.SetDriveInput(steer, throttle, hub.SprintHeld);
        }
    }
}
