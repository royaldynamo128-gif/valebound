using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound
{
    /// <summary>
    /// Radius-based interaction system. The nearest interactable within range and
    /// in front of the player becomes the prompt target; the Interact button fires it.
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        [Header("Range")]
        [SerializeField] private float range = 2.6f;
        [SerializeField] private float vehicleRange = 3.4f;
        [SerializeField] private LayerMask interactableMask = ~0;
        [SerializeField] private Transform rayOrigin;

        public IInteractable Current { get; private set; }
        public string CurrentPrompt { get; private set; }

        public event Action<IInteractable> OnTargetChanged;

        private PlayerController player;
        private float rescanTimer;
        private readonly Collider[] hits = new Collider[8];
        private readonly List<IInteractable> candidates = new List<IInteractable>(8);

        public interface IInteractable
        {
            string Prompt { get; }
            bool CanInteract(PlayerController player);
            void Interact(PlayerController player);
        }

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            if (rayOrigin == null) rayOrigin = transform;
        }

        private void Update()
        {
            if (player == null || player.InVehicle) { SetTarget(null); return; }
            if (GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Playing)
            { SetTarget(null); return; }

            rescanTimer -= Time.deltaTime;
            if (rescanTimer <= 0f)
            {
                rescanTimer = 0.2f;
                Scan();
            }
        }

        private void Scan()
        {
            candidates.Clear();
            int n = Physics.OverlapSphereNonAlloc(rayOrigin.position, vehicleRange, hits, interactableMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var inter = hits[i].GetComponentInParent<IInteractable>();
                if (inter != null && inter.CanInteract(player)) candidates.Add(inter);
            }

            IInteractable best = null;
            float bestScore = float.MaxValue;
            Vector3 fwd = transform.forward;
            foreach (var c in candidates)
            {
                var comp = c as UnityEngine.Component;
                if (comp == null) continue;
                Vector3 to = comp.transform.position - rayOrigin.position;
                float dist = to.magnitude;
                float maxRange = c is Vehicle.VehicleController ? vehicleRange : range;
                if (dist > maxRange) continue;
                float dot = Vector3.Dot(fwd, to / dist);
                float score = dist - dot * 1.5f; // prefer close & in front
                if (score < bestScore) { bestScore = score; best = c; }
            }
            SetTarget(best);
        }

        private void SetTarget(IInteractable target)
        {
            if (ReferenceEquals(Current, target)) return;
            Current = target;
            CurrentPrompt = target != null ? target.Prompt : null;
            OnTargetChanged?.Invoke(target);
        }

        /// <summary>Called by the Interact UI button.</summary>
        public void TryInteract()
        {
            if (Current != null && Current.CanInteract(player))
                Current.Interact(player);
        }
    }
}
