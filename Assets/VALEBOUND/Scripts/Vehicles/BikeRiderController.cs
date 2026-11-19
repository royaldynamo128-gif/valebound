using System;
using UnityEngine;

namespace Valebound.Vehicle
{
    /// <summary>
    /// Rider rig on bikes: shows a seated character while ridden, hides it when
    /// parked. Also drives the lean-into-corners visual on the bike frame.
    /// </summary>
    public class BikeRiderController : MonoBehaviour
    {
        [Header("Lean visuals")]
        [SerializeField] private float maxLeanDeg = 18f;
        [SerializeField] private float leanSmoothing = 6f;

        private VehicleController vc;
        private float leanVel;

        public void SetVisible(bool visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                r.enabled = visible;
        }

        private void Start()
        {
            vc = GetComponentInParent<VehicleController>();
            if (vc == null) vc = GetComponent<VehicleController>();
        }

        private void Update()
        {
            if (vc == null) return;

            // lean into corners while ridden (visual only, subtle)
            if (vc.IsPlayerDriven)
            {
                float steer = 0f;
                vc.GetSteerVisual(out steer);
                float target = -steer * maxLeanDeg * Mathf.Clamp01(vc.Speed01 * 2f);
                float lean = Mathf.SmoothDampAngle(GetLean(), target, ref leanVel, 1f / leanSmoothing);
                SetLean(lean);
            }
            SetVisible(vc.IsPlayerDriven);
        }

        // lean is applied on the seat's parent (bike root visual child), not the RB
        [SerializeField] private Transform leanPivot;
        private float GetLean() => leanPivot != null ? leanPivot.localEulerAngles.z > 180f
            ? leanPivot.localEulerAngles.z - 360f : leanPivot.localEulerAngles.z : 0f;
        private void SetLean(float deg) =>
            (leanPivot ?? transform).localRotation = Quaternion.Euler(0f, 0f, deg);
    }
}
