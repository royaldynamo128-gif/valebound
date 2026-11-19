using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Valebound.Systems
{
    /// <summary>
    /// Spawns pedestrians around the player near sidewalk POIs, with per-district
    /// population caps and time-of-day density (rush hour, quiet night).
    /// </summary>
    public class NpcSystem : MonoBehaviour
    {
        public static NpcSystem Instance { get; private set; }

        [Serializable]
        public class SpawnZone
        {
            public string zoneId;
            public Vector3 center;
            public float radius = 30f;
            [Range(0f, 1f)] public float weight = 1f;
        }

        [Header("Prefabs")]
        [SerializeField] private GameObject[] pedestrianPrefabs;

        [Header("Zones (baked by WorldBuilder)")]
        [SerializeField] private SpawnZone[] zones;

        [Header("Budget")]
        [SerializeField] private int maxNpcHigh = 22;
        [SerializeField] private int maxNpcMedium = 14;
        [SerializeField] private int maxNpcLow = 8;
        [SerializeField] private float spawnRadius = 90f;
        [SerializeField] private float despawnRadius = 130f;
        [SerializeField] private float manageInterval = 2f;

        private readonly List<GameObject> active = new List<GameObject>();
        private float manageTimer;
        private static readonly string[] genericNames =
        {
            "Ana", "Boris", "Cleo", "Dmitri", "Elif", "Farid", "Grace", "Hana", "Igor", "Juno",
            "Kofi", "Lena", "Milo", "Nadia", "Omar", "Priya", "Quinn", "Rosa", "Samir", "Tessa"
        };

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void SetZones(SpawnZone[] baked) => zones = baked;

        private void Update()
        {
            manageTimer -= Time.deltaTime;
            if (manageTimer > 0f) return;
            manageTimer = manageInterval;

            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null || zones == null || zones.Length == 0 || pedestrianPrefabs == null || pedestrianPrefabs.Length == 0)
                return;

            float budget = PerformanceGovernor.Instance != null
                ? PerformanceGovernor.Instance.SimulationBudget : 0.7f;
            float todMult = WorldClock.Instance != null ? WorldClock.Instance.PedestrianDensityMultiplier : 1f;
            int max = Mathf.RoundToInt(CurrentMaxBase() * budget * todMult);

            // despawn far
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var a = active[i];
                if (a == null) { active.RemoveAt(i); continue; }
                if ((a.transform.position - player.transform.position).sqrMagnitude
                    > despawnRadius * despawnRadius)
                {
                    a.SetActive(false);
                    active.RemoveAt(i);
                }
            }

            // spawn near
            int spawnAttempts = 3;
            while (active.Count < max && spawnAttempts-- > 0)
            {
                var zone = zones[UnityEngine.Random.Range(0, zones.Length)];
                // sidewalk bias: shift spawn toward nearest road node (peds walk streets, not lawns)
                if (RoadNetwork.Instance != null && RoadNetwork.Instance.Data != null)
                {
                    int rn = RoadNetwork.Instance.Data.NearestNode(zone.center);
                    if (rn >= 0)
                    {
                        Vector3 nodePos = RoadNetwork.Instance.Data.NodePos(rn);
                        if ((nodePos - zone.center).sqrMagnitude < 30f * 30f)
                            zone.center = Vector3.Lerp(zone.center, nodePos, 0.6f);
                    }
                }
                Vector2 off = UnityEngine.Random.insideUnitCircle * zone.radius;
                Vector3 pos = zone.center + new Vector3(off.x, 0f, off.y);
                float d = (pos - player.transform.position).sqrMagnitude;
                if (d > despawnRadius * despawnRadius) continue;
                if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 8f, NavMesh.AllAreas)) continue;

                var prefab = pedestrianPrefabs[UnityEngine.Random.Range(0, pedestrianPrefabs.Length)];
                var pool = PoolManager.Instance != null
                    ? PoolManager.Instance.GetOrCreate(prefab.name, prefab, 3, maxNpcHigh)
                    : null;
                GameObject go = pool != null ? pool.Get(hit.position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f))
                                             : Instantiate(prefab, hit.position, Quaternion.identity);
                var ped = go.GetComponent<PedestrianNPC>();
                if (ped != null)
                {
                    string nm = genericNames[UnityEngine.Random.Range(0, genericNames.Length)];
                    ped.Spawn(hit.position, nm, false, null);
                }
                active.Add(go);
            }
        }

        private int CurrentMaxBase() =>
            PerformanceGovernor.Instance != null && PerformanceGovernor.Instance.CurrentQuality == PerformanceGovernor.Tier.Low
                ? maxNpcLow
                : PerformanceGovernor.Instance != null && PerformanceGovernor.Instance.CurrentQuality == PerformanceGovernor.Tier.High
                    ? maxNpcHigh : maxNpcMedium;
    }
}
