using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound.Systems
{
    /// <summary>
    /// Endless procedural courier jobs: pick a package at a POI, deliver to another
    /// POI within a time limit scaled by route length. Payouts scale with distance
    /// and player level. Provides the "always something to do" loop.
    /// </summary>
    public class CourierGenerator : MonoBehaviour
    {
        [Serializable]
        public class DropPoint
        {
            public string poiId;
            public Vector3 pos;
        }

        [Header("Drop points (baked from POIs)")]
        [SerializeField] private DropPoint[] dropPoints;

        [Header("Rules")]
        [SerializeField] private float minRouteMeters = 220f;
        [SerializeField] private float maxRouteMeters = 1400f;
        [SerializeField] private float metersPerSecond = 9.5f;   // time budget
        [SerializeField] private int basePay = 60;
        [SerializeField] private float payPerKm = 38f;
        [SerializeField] private float levelBonusPct = 2.5f;

        public DropPoint[] DropPoints => dropPoints;
        public CourierJob ActiveJob { get; private set; }

        public class CourierJob
        {
            public DropPoint pickup;
            public DropPoint dropoff;
            public float timeLimit;
            public int payout;
            public float timer;
            public bool pickedUp;
        }

        private void Start()
        {
            // bake drop points from POI registry if not set
            if ((dropPoints == null || dropPoints.Length == 0) && WorldMapSystem.Instance != null)
            {
                var pois = WorldMapSystem.Instance.AllPOIs;
                var list = new List<DropPoint>();
                foreach (var p in pois)
                {
                    if (p.type == POIType.Landmark || p.type == POIType.Shop || p.type == POIType.CityCenter)
                        list.Add(new DropPoint { poiId = p.poiId, pos = p.worldPos });
                }
                dropPoints = list.ToArray();
            }
        }

        public void GenerateJob()
        {
            if (ActiveJob != null) return;
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null || dropPoints == null || dropPoints.Length < 2) return;

            // pickup: nearest-ish; dropoff: within route length band
            DropPoint pickup = null, dropoff = null;
            float bestD = float.MaxValue;
            foreach (var dp in dropPoints)
            {
                float d = (dp.pos - player.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; pickup = dp; }
            }
            int tries = 12;
            while (tries-- > 0)
            {
                var cand = dropPoints[UnityEngine.Random.Range(0, dropPoints.Length)];
                if (cand == pickup) continue;
                float route = Vector3.Distance(pickup.pos, cand.pos);
                if (route >= minRouteMeters && route <= maxRouteMeters) { dropoff = cand; break; }
            }
            if (dropoff == null) return;

            float routeLen = Vector3.Distance(pickup.pos, dropoff.pos);
            float timeLimit = 25f + routeLen / metersPerSecond;
            int level = EconomyManager.Instance != null ? EconomyManager.Instance.Level : 1;
            int payout = Mathf.RoundToInt((basePay + routeLen / 1000f * payPerKm)
                * (1f + (level - 1) * levelBonusPct / 100f));

            ActiveJob = new CourierJob
            {
                pickup = pickup,
                dropoff = dropoff,
                timeLimit = timeLimit,
                payout = payout,
            };

            GameEvents.RaiseMissionOffered("courier_job", "Express Courier Run", payout);
            UI.UIManager.Instance?.ShowNotification("Courier Job Available",
                $"Pick up at {pickup.poiId}. Reward: {payout:C0}", 4f);
        }

        private void Update()
        {
            if (ActiveJob == null) return;
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null) return;

            ActiveJob.timer += Time.deltaTime;
            float remaining = ActiveJob.timeLimit - ActiveJob.timer;
            if (remaining <= 0f)
            {
                FailJob("Out of time.");
                return;
            }

            if (!ActiveJob.pickedUp)
            {
                if ((player.transform.position - ActiveJob.pickup.pos).sqrMagnitude < 25f)
                {
                    ActiveJob.pickedUp = true;
                    UI.UIManager.Instance?.ShowNotification("Package picked up",
                        $"Deliver to {ActiveJob.dropoff.poiId}. {Mathf.CeilToInt(remaining)}s on the clock.", 3.5f);
                    Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/pickup", 0.8f);
                }
            }
            else if ((player.transform.position - ActiveJob.dropoff.pos).sqrMagnitude < 25f)
            {
                CompleteJob();
            }
        }

        private void CompleteJob()
        {
            int pay = ActiveJob.payout;
            EconomyManager.Instance?.AddCredits(pay);
            EconomyManager.Instance?.AddXp(Mathf.RoundToInt(pay * 0.35f));
            UI.UIManager.Instance?.ShowMissionBanner("Delivery Complete",
                $"Right on time. +{pay} credits.");
            Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/mission_complete", 0.9f);
            ActiveJob = null;
            AchievementSystem.Instance?.NotifyCourierDone();
        }

        private void FailJob(string reason)
        {
            UI.UIManager.Instance?.ShowNotification("Delivery Failed", reason, 3.5f);
            Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/fail", 0.8f);
            ActiveJob = null;
        }
    }
}
