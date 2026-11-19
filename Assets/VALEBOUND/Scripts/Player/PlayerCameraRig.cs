using System;
using UnityEngine;

namespace Valebound
{
    /// <summary>
    /// Third-person orbit camera with touch drag, pinch zoom, camera collision
    /// (sphere cast against environment), and smooth follow. Also usable in
    /// vehicles with a wider FOV/offset preset.
    /// </summary>
    public class PlayerCameraRig : MonoBehaviour
    {
        [Header("Follow")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 1.7f, 0f);
        [SerializeField] private float followDamp = 12f;
        [SerializeField] private float vehicleFollowDamp = 7f;

        [Header("Orbit")]
        [SerializeField] private float yawSpeed = 140f;
        [SerializeField] private float pitchSpeed = 110f;
        [SerializeField] private float pitchMin = -35f;
        [SerializeField] private float pitchMax = 70f;

        [Header("Distance")]
        [SerializeField] private float defaultDistance = 5.2f;
        [SerializeField] private float vehicleDistance = 8.5f;
        [SerializeField] private float minDistance = 2.2f;
        [SerializeField] private float maxDistance = 11f;
        [SerializeField] private float zoomSpeed = 260f;

        [Header("Vehicle mode")]
        [SerializeField] private float carFov = 62f;
        [SerializeField] private float footFov = 58f;
        [SerializeField] private float fovLerp = 6f;

        [Header("Collision")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float collisionRadius = 0.3f;
        [SerializeField] private float collisionRecover = 14f;

        public Vector3 FlatForward
        {
            get
            {
                Vector3 f = transform.forward; f.y = 0f;
                return f.sqrMagnitude < 0.001f ? Vector3.forward : f.normalized;
            }
        }

        public Vector3 FlatRight
        {
            get
            {
                Vector3 r = transform.right; r.y = 0f;
                return r.sqrMagnitude < 0.001f ? Vector3.right : r.normalized;
            }
        }

        private Camera cam;
        private float yaw;
        private float pitch = 18f;
        private float distance;
        private float collisionDistance;
        private bool vehicleMode;
        private Vector3 followPos;
        private Vector3 followVel;

        private void Awake()
        {
            cam = GetComponentInChildren<Camera>();
            distance = defaultDistance;
            collisionDistance = defaultDistance;
        }

        public void Bind(Transform followTarget)
        {
            target = followTarget;
            if (target != null)
            {
                followPos = target.position + followOffset;
                transform.position = followPos - Vector3.forward * distance;
            }
        }

        public void SetVehicleMode(bool on)
        {
            vehicleMode = on;
        }

        /// <summary>External orbit input (touch drag or mouse).</summary>
        public void AddOrbitInput(float dYaw, float dPitch)
        {
            var settings = SaveSystem.LoadSettings();
            float sens = Mathf.Clamp(settings.cameraSensitivity, 0.3f, 2.5f);
            yaw += dYaw * yawSpeed * sens * Time.deltaTime;
            if (settings.invertCameraY) dPitch = -dPitch;
            pitch -= dPitch * pitchSpeed * sens * Time.deltaTime;
            pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
        }

        public void AddZoomInput(float delta)
        {
            distance = Mathf.Clamp(distance + delta * zoomSpeed * Time.deltaTime, minDistance, maxDistance);
        }

        private void LateUpdate()
        {
            if (target == null) return;

            float targetDist = vehicleMode ? vehicleDistance : distance;
            float damp = vehicleMode ? vehicleFollowDamp : followDamp;

            // follow point
            Vector3 desired = target.position + followOffset;
            followPos = Vector3.SmoothDamp(followPos, desired, ref followVel, 1f / damp);

            // orbit
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 castFrom = followPos;
            Vector3 castDir = rot * Vector3.back;

            // collision: pull camera in when obstructed
            float wanted = targetDist;
            if (Physics.SphereCast(castFrom, collisionRadius, castDir, out RaycastHit hit, wanted + 0.2f, collisionMask, QueryTriggerInteraction.Ignore))
            {
                collisionDistance = Mathf.Max(minDistance, hit.distance - 0.25f);
            }
            else
            {
                collisionDistance = Mathf.MoveTowards(collisionDistance, wanted, collisionRecover * Time.deltaTime);
            }

            transform.position = castFrom + castDir * collisionDistance;
            transform.rotation = rot;

            // FOV
            if (cam != null)
            {
                float fov = vehicleMode ? carFov : footFov;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, fovLerp * Time.deltaTime);
            }
        }

        /// <summary>Vehicle camera preset: behind-car yaw with slight auto-align.</summary>
        public void SnapBehind(float forwardYaw)
        {
            yaw = forwardYaw;
            pitch = 14f;
        }
    }
}
