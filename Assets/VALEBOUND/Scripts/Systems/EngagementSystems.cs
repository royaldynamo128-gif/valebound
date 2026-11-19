using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound.Systems
{
    // =====================================================================
    // DAILY CHALLENGES
    // =====================================================================

    /// <summary>
    /// Three rotating challenges per calendar day: one drive, one collect, one
    /// social/misc. Rewards credits + XP; completing all three grants a bonus.
    /// </summary>
    public class DailyChallengeSystem : MonoBehaviour
    {
        public static DailyChallengeSystem Instance { get; private set; }

        [Serializable]
        public class Challenge
        {
            public string id;
            public string title;
            public string description;
            public int target;          // e.g. km x10, shards, jobs
            public int progress;
            public int rewardCredits;
            public int rewardXp;
            public bool claimed;
        }

        public Challenge[] Today { get; private set; } = Array.Empty<Challenge>();
        private string currentDate = "";
        private bool allBonusClaimed;

        private readonly Dictionary<string, Action<int>> counters = new Dictionary<string, Action<int>>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            RollForToday();
        }

        private void RollForToday()
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (today == currentDate && Today.Length > 0) return;
            currentDate = today;

            // deterministic per-date pool
            int seed = today.GetHashCode();
            var rng = new System.Random(seed);
            var pool = new (string id, string title, string desc, int target, int credits, int xp)[]
            {
                ("drive_km",     "Road Trip",       "Drive 3 km in any vehicle",            30, 150, 60),
                ("shards",       "Signal Sweep",    "Collect 5 signal shards",              5,  180, 70),
                ("courier",      "Working Day",     "Complete 2 courier jobs",              2,  160, 60),
                ("airtime",      "Get Some Air",    "Bank 2 seconds of total air time",     2,  200, 80),
                ("races",        "Lap Dog",         "Finish 1 race",                        1,  220, 90),
                ("photos",       "Shutterbug",      "Photograph 1 vista",                   1,  120, 50),
                ("taxis",        "People Person",   "Complete 2 taxi passenger jobs",       2,  170, 70),
                ("biome_visit",  "Tourist",         "Visit any biome city",                 1,  150, 60),
                ("night_drive",  "Night Owl",       "Drive 1 km after 22:00",               10,  180, 70),
            };
            // pick 3 distinct
            var picked = new List<int>();
            while (picked.Count < 3) { int i = rng.Next(pool.Length); if (!picked.Contains(i)) picked.Add(i); }

            Today = new Challenge[3];
            for (int i = 0; i < 3; i++)
            {
                var p = pool[picked[i]];
                Today[i] = new Challenge
                {
                    id = p.id,
                    title = p.title,
                    description = p.desc,
                    target = p.target,
                    rewardCredits = p.credits,
                    rewardXp = p.xp,
                };
            }
            allBonusClaimed = false;
            UI.UIManager.Instance?.ShowNotification("Daily Challenges",
                $"{Today[0].title} - {Today[1].title} - {Today[2].title}", 4f);
        }

        /// <summary>Advance any challenge with this counter id (km stored x10).</summary>
        public void Notify(string counterId, int amount = 1)
        {
            if (Today == null) return;
            bool any = false;
            foreach (var c in Today)
            {
                if (c.id != counterId || c.claimed) continue;
                c.progress = Mathf.Min(c.target, c.progress + amount);
                any = true;
                if (c.progress >= c.target)
                {
                    EconomyManager.Instance?.AddCredits(c.rewardCredits);
                    EconomyManager.Instance?.AddXp(c.rewardXp);
                    c.claimed = true;
                    UI.UIManager.Instance?.ShowNotification("Challenge Done",
                        $"{c.title} - +{c.rewardCredits} cr", 3f);
                    Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/levelup", 0.7f);
                }
            }
            if (any) CheckAllBonus();
        }

        private void CheckAllBonus()
        {
            if (allBonusClaimed || Today.Length < 3) return;
            foreach (var c in Today) if (!c.claimed) return;
            allBonusClaimed = true;
            EconomyManager.Instance?.AddCredits(300);
            EconomyManager.Instance?.AddXp(120);
            AchievementSystem.Instance?.Unlock("daily_triple");
            UI.UIManager.Instance?.ShowNotification("Clean Sweep!",
                "All daily challenges complete. +300 cr bonus.", 4f);
        }

        public void CaptureSaveData(SaveData d)
        {
            d.dailyChallengeDate = currentDate;
            d.dailyChallengeIds = new List<string>();
            d.completedDailyIds = new List<string>();
            foreach (var c in Today)
            {
                d.dailyChallengeIds.Add(c.id);
                if (c.claimed) d.completedDailyIds.Add(c.id);
            }
        }

        public void ApplySaveData(SaveData d)
        {
            RollForToday();
            // mark same-day completions from the save
            if (d.dailyChallengeDate == currentDate && Today != null)
                foreach (var c in Today)
                    c.claimed = d.completedDailyIds.Contains(c.id);
        }
    }

    // =====================================================================
    // TAXI PASSENGER JOBS
    // =====================================================================

    /// <summary>
    /// Hop-in taxi jobs: pedestrians with a "Taxi?" prompt ride with you to a
    /// destination POI. Fare scales with distance; tipping for smooth driving.
    /// </summary>
    public class TaxiJobSystem : MonoBehaviour
    {
        public static TaxiJobSystem Instance { get; private set; }

        [Header("Fares")]
        [SerializeField] private int baseFare = 40;
        [SerializeField] private int perKm = 22;
        [SerializeField] private float smoothTipMultiplier = 1.4f;

        public class ActiveFare
        {
            public GameObject passenger;
            public string destName;
            public Vector3 destPos;
            public float distanceMeters;
            public bool inTransit;
        }

        public ActiveFare Current { get; private set; }
        private int jobsDone;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Called by TaxiPassengerNPC when the player picks them up.</summary>
        public bool BeginFare(GameObject passenger, out string destName, out Vector3 destPos)
        {
            destName = null; destPos = default;
            if (Current != null) return false;
            var map = WorldMapSystem.Instance;
            if (map == null || map.AllPOIs == null || map.AllPOIs.Length == 0) return false;

            // pick a POI 250-1200 m away
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            Vector3 ppos = player != null ? player.transform.position : Vector3.zero;
            var candidates = new List<POI>();
            foreach (var poi in map.AllPOIs)
            {
                float d = Vector3.Distance(poi.worldPos, ppos);
                if (d > 250f && d < 1200f) candidates.Add(poi);
            }
            if (candidates.Count == 0) return false;
            var dest = candidates[UnityEngine.Random.Range(0, candidates.Count)];

            Current = new ActiveFare
            {
                passenger = passenger,
                destName = dest.displayName,
                destPos = dest.worldPos,
                distanceMeters = Vector3.Distance(dest.worldPos, ppos),
                inTransit = true,
            };
            destName = dest.displayName;
            destPos = dest.worldPos;
            UI.UIManager.Instance?.ShowNotification("Fare Accepted",
                $"Take the passenger to {dest.displayName}.", 3.5f);
            return true;
        }

        private void Update()
        {
            if (Current == null || !Current.inTransit) return;
            var gm = GameManager.Instance;
            if (gm == null) return;
            // dropped off at destination while in a vehicle
            var v = gm.CurrentVehicle;
            if (v == null) return;
            if ((v.transform.position - Current.destPos).sqrMagnitude < 14f * 14f)
            {
                CompleteFare();
            }
        }

        private void CompleteFare()
        {
            int fare = baseFare + Mathf.RoundToInt(Current.distanceMeters / 1000f * perKm);
            // smooth driving tip: durability of the vehicle as proxy
            var v = GameManager.Instance != null ? GameManager.Instance.CurrentVehicle : null;
            if (v != null && v.Controller.Durability01 > 0.9f) fare = Mathf.RoundToInt(fare * smoothTipMultiplier);
            EconomyManager.Instance?.AddCredits(fare);
            EconomyManager.Instance?.AddXp(40 + (int)(Current.distanceMeters / 100f));
            jobsDone++;
            DailyChallengeSystem.Instance?.Notify("taxis");
            if (jobsDone >= 10) AchievementSystem.Instance?.Unlock("taxi_10");
            UI.UIManager.Instance?.ShowNotification("Fare Complete",
                $"+{fare} cr{(v != null && v.Controller.Durability01 > 0.9f ? " (smooth ride tip!)" : "")}", 3.5f);
            Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/mission_complete", 0.7f);

            // passenger hops out
            if (Current.passenger != null)
            {
                Current.passenger.GetComponent<NPC.TaxiPassengerNPC>()?.DropOff(v != null ? v.transform.position : Current.destPos);
            }
            Current = null;
        }

        public void CancelFare()
        {
            if (Current == null) return;
            UI.UIManager.Instance?.ShowNotification("Fare Lost",
                "The passenger gave up and walked.", 3f);
            Current.passenger?.GetComponent<NPC.TaxiPassengerNPC>()?.DropOff(Current.destPos);
            Current = null;
        }
    }

    // =====================================================================
    // STUNT TRACKER
    // =====================================================================

    /// <summary>
    /// Watches the driven vehicle for air time and big jumps; awards stunt
    /// credits and feeds the airtime daily challenge + achievement.
    /// </summary>
    public class StuntTracker : MonoBehaviour
    {
        public static StuntTracker Instance { get; private set; }

        [Header("Rewards")]
        [SerializeField] private float minAirTimeForCredit = 0.45f;
        [SerializeField] private int creditsPerAirSecond = 60;
        [SerializeField] private float bigJumpMeters = 6f;

        private Vehicle.VehicleController watched;
        private bool airborne;
        private float airStartTime;
        private float airTimeTotal;
        private Vector3 jumpStartPos;
        private bool landNotified;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            var gm = GameManager.Instance;
            var v = gm != null && gm.CurrentVehicle != null ? gm.CurrentVehicle.Controller : null;
            if (v == null) { airborne = false; return; }

            // airborne = not grounded raycast (same test the controller uses)
            bool ground = Physics.Raycast(v.transform.position + v.transform.up * 0.3f,
                -v.transform.up, 0.65f, ~0, QueryTriggerInteraction.Ignore);
            if (!ground)
            {
                if (!airborne)
                {
                    airborne = true;
                    airStartTime = Time.time;
                    jumpStartPos = v.transform.position;
                    landNotified = false;
                }
            }
            else if (airborne)
            {
                float airTime = Time.time - airStartTime;
                float drop = jumpStartPos.y - v.transform.position.y;
                airborne = false;
                if (airTime >= minAirTimeForCredit)
                {
                    airTimeTotal += airTime;
                    int credits = Mathf.RoundToInt(airTime * creditsPerAirSecond);
                    EconomyManager.Instance?.AddCredits(credits);
                    EconomyManager.Instance?.AddXp(Mathf.RoundToInt(airTime * 25f));
                    DailyChallengeSystem.Instance?.Notify("airtime", Mathf.Max(1, Mathf.RoundToInt(airTime)));
                    if (drop > bigJumpMeters && !landNotified)
                    {
                        landNotified = true;
                        AchievementSystem.Instance?.Unlock("big_air");
                        UI.UIManager.Instance?.ShowNotification("Big Air!",
                            $"{airTime:F1}s airborne - +{credits} cr", 3f);
                    }
                    else
                    {
                        UI.UIManager.Instance?.ShowNotification("Air Time",
                            $"{airTime:F1}s - +{credits} cr", 2.2f);
                    }
                    if (airTimeTotal > 30f) AchievementSystem.Instance?.Unlock("airtime_30");
                }
            }
        }
    }
}
