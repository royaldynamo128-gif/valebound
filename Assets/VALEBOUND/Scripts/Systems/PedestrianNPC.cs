using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Valebound.Systems
{
    /// <summary>
    /// Pedestrian NPC: patrols sidewalk waypoints via NavMesh, plays idle barks,
    /// reacts to vehicles (dodge), follows daily schedule zones by hour.
    /// Pooled and spawned by NpcSystem around the player.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class PedestrianNPC : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string npcName = "Citizen";
        [SerializeField] private bool isMissionGiver;
        [SerializeField] private string missionGiverId;

        [Header("Wander")]
        [SerializeField] private float wanderRadius = 22f;
        [SerializeField] private float idleTimeMin = 2f;
        [SerializeField] private float idleTimeMax = 6f;
        [SerializeField] private float walkSpeed = 1.35f;

        [Header("Sidewalk routing")]
        [Tooltip("When set, walks street-to-street along the road graph instead of random wander")]
        [SerializeField] private bool useSidewalkRoutes = true;
        [SerializeField] private float sidewalkOffset = 3.2f;   // offset from road centerline
        [SerializeField] private float cornerRadius = 6f;
        private Vector3 lastNodePos;
        private Vector3 nextNodePos;
        private bool routing;

        [Header("Bark")]
        [SerializeField] private string[] barks;
        [SerializeField] private float barkChancePerSec = 0.02f;

        [Header("Dodge")]
        [SerializeField] private float vehicleDodgeRadius = 6f;
        [SerializeField] private float dodgeSpeed = 3.2f;

        public string NpcName => npcName;
        public bool IsMissionGiver => isMissionGiver;
        public string MissionGiverId => missionGiverId;

        private NavMeshAgent agent;
        private Vector3 home;
        private float idleTimer;
        private Vector3 dodgeTarget;
        private bool dodging;
        private float barkTimer;
        private BillboardNameTag nameTag;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.speed = walkSpeed;
            nameTag = GetComponentInChildren<BillboardNameTag>(true);
        }

        public void Spawn(Vector3 pos, string displayName, bool missionGiver, string giverId)
        {
            home = pos;
            npcName = displayName;
            isMissionGiver = missionGiver;
            missionGiverId = giverId;
            transform.position = pos;
            idleTimer = UnityEngine.Random.Range(0.5f, idleTimeMax);
            if (nameTag != null) nameTag.SetText(displayName, missionGiver);
        }

        private void Update()
        {
            if (!agent.isOnNavMesh) return;
            float dt = Time.deltaTime;

            // -------- vehicle dodge --------
            if (!dodging && vehicleDodgeRadius > 0f && Time.frameCount % 6 == 0)
            {
                var cols = Physics.OverlapSphere(transform.position, vehicleDodgeRadius, ~0, QueryTriggerInteraction.Ignore);
                foreach (var c in cols)
                {
                    var vc = c.GetComponentInParent<Vehicle.VehicleController>();
                    if (vc == null) continue;
                    float speed = vc.SpeedKph;
                    if (speed < 18f) continue;
                    // is it coming toward us?
                    Vector3 toNpc = transform.position - vc.transform.position;
                    if (Vector3.Dot(vc.transform.forward, toNpc.normalized) > 0.6f)
                    {
                        Vector3 away = (transform.position - vc.transform.position).normalized;
                        dodgeTarget = transform.position + away * 4f + Vector3.up * 0f;
                        if (NavMesh.SamplePosition(dodgeTarget, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                            dodgeTarget = hit.position;
                        dodging = true;
                        agent.SetDestination(dodgeTarget);
                        agent.speed = dodgeSpeed;
                        break;
                    }
                }
            }
            if (dodging)
            {
                if (!agent.pathPending && agent.remainingDistance < 0.6f)
                {
                    dodging = false;
                    agent.speed = walkSpeed;
                }
                return;
            }

            // -------- idle / wander --------
            if (idleTimer > 0f)
            {
                idleTimer -= dt;
                if (idleTimer <= 0f) PickNewWanderTarget();
            }
            else if (!agent.pathPending && agent.remainingDistance < 0.5f)
            {
                idleTimer = UnityEngine.Random.Range(idleTimeMin, idleTimeMax);
            }

            // -------- bark --------
            barkTimer -= dt;
            if (barkTimer <= 0f)
            {
                barkTimer = 1f;
                if (barks != null && barks.Length > 0 && UnityEngine.Random.value < barkChancePerSec)
                {
                    string bark = barks[UnityEngine.Random.Range(0, barks.Length)];
                    UI.UIManager.Instance?.ShowBark(transform, bark);
                }
            }
        }

        private void PickNewWanderTarget()
        {
            if (useSidewalkRoutes && RoadNetwork.Instance != null && RoadNetwork.Instance.Data != null)
            {
                // pick a nearby non-highway node, walk to its sidewalk point, then along edges
                var net = RoadNetwork.Instance.Data;
                int node = net.NearestNode(transform.position);
                if (node >= 0 && !net.nodes[node].isHighway)
                {
                    var conns = net.nodes[node].connections;
                    if (conns != null && conns.Count > 0)
                    {
                        int next = conns[UnityEngine.Random.Range(0, conns.Count)];
                        if (net.nodes[next].isHighway) { PickRandomWander(); return; }
                        Vector3 a = net.NodePos(node);
                        Vector3 b = net.NodePos(next);
                        Vector3 dir = (b - a).normalized;
                        Vector3 side = new Vector3(-dir.z, 0f, dir.x) * sidewalkOffset;
                        Vector3 target = b + side;
                        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                        {
                            lastNodePos = a; nextNodePos = b;
                            routing = true;
                            agent.SetDestination(hit.position);
                            return;
                        }
                    }
                }
            }
            PickRandomWander();
        }

        private void PickRandomWander()
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * wanderRadius;
            Vector3 target = home + new Vector3(r.x, 0f, r.y);
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
        }

        public void ForceDestination(Vector3 pos)
        {
            if (agent.isOnNavMesh) agent.SetDestination(pos);
        }
    }

    /// <summary>Minimal world-space name tag facing the camera (mission givers).</summary>
    public class BillboardNameTag : MonoBehaviour
    {
        [SerializeField] private TextMesh textMesh;
        private Transform camT;

        public void SetText(string text, bool highlight)
        {
            if (textMesh == null) return;
            textMesh.text = text;
            textMesh.color = highlight ? new Color(1f, 0.82f, 0.25f) : Color.white;
        }

        private void OnEnable()
        {
            var cam = Camera.main;
            camT = cam != null ? cam.transform : null;
        }

        private void LateUpdate()
        {
            if (camT == null)
            {
                var cam = Camera.main;
                camT = cam != null ? cam.transform : null;
            }
            if (camT != null)
                transform.rotation = Quaternion.LookRotation(transform.position - camT.position);
        }
    }
}
