using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound.Systems
{
    /// <summary>
    /// Traffic AI agent: follows the road graph, keeps distance from the car ahead,
    /// stops briefly at intersections, despawns when far from the player.
    /// Attached (pooled) to traffic vehicle prefabs by the TrafficSystem.
    /// </summary>
    public class TrafficAIAgent : MonoBehaviour
    {
        [Header("Driving")]
        [SerializeField] private float cruiseSpeedKph = 42f;
        [SerializeField] private float followDistance = 7f;
        [SerializeField] private float intersectionStopSeconds = 1.2f;

        [Header("Awareness")]
        [SerializeField] private float forwardSensorLength = 9f;
        [SerializeField] private LayerMask vehicleMask = ~0;

        public bool IsActive { get; private set; }
        public bool IsAudible => (transform.position - listenerPos).sqrMagnitude < 900f;

        private Vehicle.VehicleController vc;
        private RoadNetworkData net;
        private List<int> path = new List<int>();
        private int pathIndex;
        private float stopTimer;
        private Vector3 lastPos;
        private float stuckTimer;
        private static Vector3 listenerPos;
        private float repathTimer;

        public void Init(Vehicle.VehicleController controller, RoadNetworkData network)
        {
            vc = controller;
            net = network;
        }

        public void SpawnOnNetwork(Vector3 nearPos)
        {
            if (net == null) return;
            path.Clear();
            pathIndex = 0;
            int start = net.NearestNode(nearPos);
            if (start < 0) return;
            // pick a random destination node to give the car a route
            int goal = UnityEngine.Random.Range(0, net.nodes.Count);
            if (goal == start) goal = (goal + 1) % Mathf.Max(1, net.nodes.Count);
            if (!net.FindPath(start, goal, path) || path.Count < 2)
            {
                path.Clear();
                return;
            }
            transform.position = net.NodePos(start);
            transform.rotation = Quaternion.LookRotation(net.NodePos(path[1]) - net.NodePos(start));
            IsActive = true;
            stopTimer = 0f;
            stuckTimer = 0f;
            lastPos = transform.position;
        }

        private void Update()
        {
            if (!IsActive || vc == null || net == null) return;
            listenerPos = Camera.main != null ? Camera.main.transform.position : listenerPos;

            float dt = Time.deltaTime;

            // -------- advance path --------
            if (pathIndex >= path.Count)
            {
                // route finished: pick a new one
                int goal = UnityEngine.Random.Range(0, net.nodes.Count);
                net.FindPath(path.Count > 0 ? path[path.Count - 1] : net.NearestNode(transform.position), goal, path);
                pathIndex = 1;
                if (path.Count < 2) { IsActive = false; return; }
            }

            Vector3 target = net.NodePos(path[pathIndex]);
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            if (dist < 4f)
            {
                pathIndex++;
                return;
            }

            // -------- steering --------
            Vector3 fwd = transform.forward;
            float angle = Vector3.SignedAngle(fwd, toTarget.normalized, Vector3.up);
            float steer = Mathf.Clamp(angle / 35f, -1f, 1f);

            // -------- speed control --------
            float targetSpeed = cruiseSpeedKph / 3.6f;

            // intersection: stop before nodes flagged as intersections (simple timed stop)
            var node = net.nodes[path[pathIndex]];
            if (node.isIntersection && dist < 9f && stopTimer <= 0f && !node.isHighway)
            {
                stopTimer = intersectionStopSeconds * UnityEngine.Random.Range(0.6f, 1.4f);
            }
            if (stopTimer > 0f)
            {
                stopTimer -= dt;
                targetSpeed = 0f;
            }

            // forward sensor: slow for vehicles / player
            if (Physics.Raycast(transform.position + Vector3.up * 0.6f + fwd * 1.5f, fwd,
                    out RaycastHit hit, forwardSensorLength, vehicleMask, QueryTriggerInteraction.Ignore))
            {
                float distAhead = hit.distance;
                if (distAhead < followDistance)
                {
                    targetSpeed = Mathf.Min(targetSpeed,
                        (distAhead - 2.5f) / (followDistance - 2.5f) * cruiseSpeedKph / 3.6f);
                    if (distAhead < 3.2f) targetSpeed = 0f;
                }
            }

            float currentSpeed = Vector3.Dot(vc_rigidbody_velocity(), fwd);
            float throttle = Mathf.Clamp((targetSpeed - currentSpeed) * 0.6f, -1f, 1f);
            vc.SetDriveInput(steer, throttle, targetSpeed < 0.2f && currentSpeed > 0.5f);

            // -------- stuck detection --------
            if ((transform.position - lastPos).sqrMagnitude < 0.01f)
            {
                stuckTimer += dt;
                if (stuckTimer > 6f)
                {
                    SpawnOnNetwork(transform.position); // teleport-repath
                    stuckTimer = 0f;
                }
            }
            else stuckTimer = Mathf.Max(0f, stuckTimer - dt);
            lastPos = transform.position;
        }

        private Vector3 vc_rigidbody_velocity()
        {
            var rb = GetComponent<Rigidbody>();
            return rb != null ? rb.velocity : Vector3.zero;
        }

        public void OnVehicleDestroyed()
        {
            IsActive = false;
        }

        public void Deactivate()
        {
            IsActive = false;
        }
    }

    /// <summary>
    /// Spawns and recycles pooled traffic vehicles around the player using the
    /// road network. Density scales with PerformanceGovernor budget and time of day.
    /// </summary>
    public class TrafficSystem : MonoBehaviour
    {
        public static TrafficSystem Instance { get; private set; }

        [Header("Setup")]
        [SerializeField] private GameObject[] trafficPrefabs;
        [SerializeField] private Transform trafficRoot;

        [Header("Budget")]
        [SerializeField] private int maxTrafficHigh = 14;
        [SerializeField] private int maxTrafficMedium = 9;
        [SerializeField] private int maxTrafficLow = 5;
        [SerializeField] private float spawnRadius = 170f;
        [SerializeField] private float despawnRadius = 240f;
        [SerializeField] private float respawnInterval = 1.5f;

        private readonly List<GameObject> active = new List<GameObject>();
        private float spawnTimer;
        private RoadNetworkData net;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            net = RoadNetwork.Instance != null ? RoadNetwork.Instance.Data : null;
            if (trafficRoot == null) trafficRoot = transform;
        }

        private void Update()
        {
            if (net == null) return;
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null) return;

            float budget = PerformanceGovernor.Instance != null
                ? PerformanceGovernor.Instance.SimulationBudget : 0.7f;
            var clock = WorldClock.Instance;
            // less traffic at night
            float todMult = clock != null ? clock.TrafficDensityMultiplier : 1f;
            int maxTraffic = Mathf.RoundToInt(
                (CurrentQualityIsLow() ? maxTrafficLow : CurrentQualityIsHigh() ? maxTrafficHigh : maxTrafficMedium)
                * budget * todMult);

            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                spawnTimer = respawnInterval;
                DespawnFar(player.transform.position);
                if (CountActive() < maxTraffic) TrySpawn(player.transform.position);
            }
        }

        private bool CurrentQualityIsLow() =>
            PerformanceGovernor.Instance != null && PerformanceGovernor.Instance.CurrentQuality == PerformanceGovernor.Tier.Low;
        private bool CurrentQualityIsHigh() =>
            PerformanceGovernor.Instance != null && PerformanceGovernor.Instance.CurrentQuality == PerformanceGovernor.Tier.High;

        private int CountActive()
        {
            active.RemoveAll(a => a == null || !a.activeSelf);
            return active.Count;
        }

        private void DespawnFar(Vector3 playerPos)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var a = active[i];
                if (a == null) { active.RemoveAt(i); continue; }
                if ((a.transform.position - playerPos).sqrMagnitude > despawnRadius * despawnRadius)
                {
                    var agent = a.GetComponent<TrafficAIAgent>();
                    if (agent != null) agent.Deactivate();
                    a.SetActive(false);
                    active.RemoveAt(i);
                }
            }
        }

        private void TrySpawn(Vector3 playerPos)
        {
            if (trafficPrefabs == null || trafficPrefabs.Length == 0) return;

            // choose a spawn node roughly in the ring [spawnRadius*0.55, spawnRadius] around the player
            int tries = 8;
            while (tries-- > 0)
            {
                Vector2 ring = UnityEngine.Random.insideUnitCircle;
                Vector3 offset = new Vector3(ring.x, 0f, ring.y).normalized * spawnRadius * UnityEngine.Random.Range(0.55f, 1f);
                int node = net.NearestNode(playerPos + offset);
                if (node < 0) return;
                Vector3 pos = net.NodePos(node);
                float d = (pos - playerPos).sqrMagnitude;
                if (d < spawnRadius * spawnRadius * 0.25f) continue; // too close
                if (d > despawnRadius * despawnRadius) continue;     // too far

                var prefab = trafficPrefabs[UnityEngine.Random.Range(0, trafficPrefabs.Length)];
                var pool = PoolManager.Instance != null
                    ? PoolManager.Instance.GetOrCreate(prefab.name, prefab, 2, maxTrafficHigh)
                    : null;
                GameObject go;
                if (pool != null) go = pool.Get(pos, Quaternion.identity);
                else go = Instantiate(prefab, pos, Quaternion.identity, trafficRoot);

                var agent = go.GetComponent<TrafficAIAgent>();
                var vc = go.GetComponent<Vehicle.VehicleController>();
                if (agent == null || vc == null)
                {
                    go.SetActive(false);
                    continue;
                }
                agent.Init(vc, net);
                agent.SpawnOnNetwork(pos);
                if (!agent.IsActive) { go.SetActive(false); continue; }
                active.Add(go);
                return;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (net == null) return;
            Gizmos.color = Color.cyan;
            foreach (var e in net.edges)
            {
                Vector3 a = net.NodePos(e.from), b = net.NodePos(e.to);
                Gizmos.DrawLine(a + Vector3.up, b + Vector3.up);
            }
        }
    }
}
