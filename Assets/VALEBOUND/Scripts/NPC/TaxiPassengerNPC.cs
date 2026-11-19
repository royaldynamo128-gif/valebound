using System;
using UnityEngine;
using UnityEngine.AI;

namespace Valebound.NPC
{
    /// <summary>
    /// A pedestrian that occasionally wants a taxi: shows a "Taxi?" prompt when
    /// the player drives near; rides to a POI destination; hops out at arrival.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class TaxiPassengerNPC : MonoBehaviour, PlayerInteraction.IInteractable
    {
        [Header("Hail")]
        [SerializeField] private float hailRadius = 10f;
        [SerializeField] private float patienceSeconds = 100f;

        public bool WantsTaxi { get; private set; }
        public string Prompt => WantsTaxi ? "Taxi? — pick up passenger" : "Chat";

        private Systems.PedestrianNPC pedestrian;
        private float patienceTimer;
        private bool riding;
        private Vector3 originalSpot;

        private void Awake()
        {
            pedestrian = GetComponent<Systems.PedestrianNPC>();
        }

        private void Start()
        {
            // decide to hail periodically while idle
            InvokeRepeating(nameof(TryHail), UnityEngine.Random.Range(4f, 12f), 14f);
        }

        private void TryHail()
        {
            if (riding || WantsTaxi) return;
            if (UnityEngine.Random.value < 0.35f)
            {
                var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
                if (player == null) return;
                // only hail when a vehicle is near (they want a ride, not a walk)
                var v = GameManager.Instance.CurrentVehicle;
                if (v != null && (v.transform.position - transform.position).sqrMagnitude < hailRadius * hailRadius)
                {
                    WantsTaxi = true;
                    patienceTimer = patienceSeconds;
                    ForceDestination_Fields();
                }
            }
        }

        private void ForceDestination_Fields()
        {
            // walk toward the road edge and wait
            if (pedestrian != null)
            {
                var net = Systems.RoadNetwork.Instance;
                if (net != null && net.Data != null)
                {
                    int node = net.Data.NearestNode(transform.position);
                    if (node >= 0) pedestrian.ForceDestination(net.Data.NodePos(node));
                }
            }
        }

        private void Update()
        {
            if (WantsTaxi && !riding)
            {
                patienceTimer -= Time.deltaTime;
                if (patienceTimer <= 0f) WantsTaxi = false;
            }
        }

        // ---------------- IInteractable ----------------

        public bool CanInteract(PlayerController player)
        {
            if (!WantsTaxi || riding) return false;
            var gm = GameManager.Instance;
            // must be in a vehicle to pick up (driver picks up passenger)
            return gm != null && gm.CurrentVehicle != null;
        }

        public void Interact(PlayerController player)
        {
            var taxi = Systems.TaxiJobSystem.Instance;
            if (taxi == null) return;
            if (taxi.BeginFare(gameObject, out string destName, out _))
            {
                riding = true;
                WantsTaxi = false;
                // "get in": hide the pedestrian, they ride along
                originalSpot = transform.position;
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
                pedestrian?.ForceDestination(transform.position); // stop walking
                transform.SetParent(GameManager.Instance.CurrentVehicle.transform, true);
                UI.UIManager.Instance?.ShowNotification("Passenger Aboard",
                    $"Destination: {destName}", 3f);
            }
        }

        /// <summary>Called by TaxiJobSystem at arrival (or when the fare is cancelled).</summary>
        public void DropOff(Vector3 at)
        {
            riding = false;
            transform.SetParent(null, true);
            transform.position = at + new Vector3(2.2f, 0f, 0f);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 6f, NavMesh.AllAreas)
                && pedestrian != null)
                pedestrian.ForceDestination(hit.position);
        }
    }
}
