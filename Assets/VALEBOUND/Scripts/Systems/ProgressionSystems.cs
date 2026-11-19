using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound.Systems
{
    /// <summary>
    /// Credits, XP levels 1-30, perks. Single source of truth for progression.
    /// </summary>
    public class EconomyManager : MonoBehaviour
    {
        public static EconomyManager Instance { get; private set; }

        [Header("Leveling")]
        [SerializeField] private int xpBase = 120;
        [SerializeField] private float xpGrowth = 1.18f;
        [SerializeField] private int maxLevel = 30;

        public int Credits { get; private set; }
        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }
        public int XpToNext => Mathf.CeilToInt(xpBase * Mathf.Pow(xpGrowth, Level - 1));

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void AddCredits(int amount)
        {
            if (amount <= 0) return;
            Credits += amount;
            GameEvents.RaiseCreditsChanged(Credits);
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || Credits < amount) return false;
            Credits -= amount;
            GameEvents.RaiseCreditsChanged(Credits);
            return true;
        }

        public void AddXp(int amount)
        {
            if (amount <= 0) return;
            Xp += amount;
            while (Level < maxLevel && Xp >= XpToNext)
            {
                Xp -= XpToNext;
                Level++;
                GameEvents.RaiseLevelUp(Level, Xp);
                UI.UIManager.Instance?.ShowNotification(
                    $"Level {Level}", "New perks available in the pause menu.", 3f);
                Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/levelup", 0.85f);
            }
        }

        public void CaptureSaveData(SaveData d)
        {
            d.credits = Credits;
            d.level = Level;
            d.xp = Xp;
        }

        public void ApplySaveData(SaveData d)
        {
            Credits = d.credits;
            Level = Mathf.Clamp(d.level, 1, maxLevel);
            Xp = Mathf.Max(0, d.xp);
            GameEvents.RaiseCreditsChanged(Credits);
        }
    }

    /// <summary>
    /// 60 collectible signal shards scattered by WorldBuilder. Grants credits, XP
    /// and achievement at milestones; spawns the pickup effect & sound.
    /// </summary>
    public class CollectibleManager : MonoBehaviour
    {
        public static CollectibleManager Instance { get; private set; }

        [Serializable]
        public class Shard
        {
            public int index;
            public Vector3 pos;
            public bool collected;
        }

        [Header("Baked shard positions")]
        [SerializeField] private Shard[] shards;

        public Shard[] Shards => shards;
        public int CollectedCount { get; private set; }

        private readonly HashSet<int> collected = new HashSet<int>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void NotifyCollected(int index)
        {
            if (collected.Contains(index)) return;
            collected.Add(index);
            CollectedCount++;
            GameEvents.RaiseShardCollected(index);

            int reward = 25 + CollectedCount * 2;
            EconomyManager.Instance?.AddCredits(reward);
            EconomyManager.Instance?.AddXp(20);

            if (CollectedCount == 10) AchievementSystem.Instance?.Unlock("shard_10");
            if (CollectedCount == 30) AchievementSystem.Instance?.Unlock("shard_30");
            if (CollectedCount == 60) AchievementSystem.Instance?.Unlock("shard_all");
            DailyChallengeSystem.Instance?.Notify("shards");

            if (CollectedCount % 10 == 0)
                UI.UIManager.Instance?.ShowNotification("Signal Shards",
                    $"{CollectedCount}/60 collected. +{reward} credits.", 3f);
        }

        /// <summary>Editor WorldBuilder injects baked shard data here.</summary>
        public void SetShards(Shard[] baked) => shards = baked;

        public void CaptureSaveData(SaveData d) => d.collectedShards = new List<int>(collected);

        public void ApplySaveData(SaveData d)
        {
            collected.Clear();
            CollectedCount = 0;
            foreach (var i in d.collectedShards) collected.Add(i);
            if (shards == null) return;
            foreach (var s in shards) s.collected = collected.Contains(s.index);
        }
    }

    /// <summary>
    /// 18 achievements: story progress, exploration, economy, driving, collection.
    /// </summary>
    public class AchievementSystem : MonoBehaviour
    {
        public static AchievementSystem Instance { get; private set; }

        [Serializable]
        public class Achievement
        {
            public string id;
            public string title;
            [TextArea] public string description;
            public bool unlocked;
        }

        [SerializeField] private Achievement[] achievements;

        public Achievement[] Achievements => achievements;

        private readonly HashSet<string> unlocked = new HashSet<string>();
        private int courierCount;
        private float totalDriveMeters;
        private bool sub1, sub2, sub3; // race time thresholds

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void Unlock(string id)
        {
            if (unlocked.Contains(id)) return;
            unlocked.Add(id);
            if (achievements != null)
                foreach (var a in achievements)
                    if (a.id == id) a.unlocked = true;
            GameEvents.RaiseAchievementUnlocked(id);
            var a2 = Find(id);
            UI.UIManager.Instance?.ShowNotification("Achievement Unlocked",
                a2 != null ? a2.title : id, 3.5f);
            Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/achievement", 0.9f);
        }

        private Achievement Find(string id)
        {
            if (achievements == null) return null;
            foreach (var a in achievements) if (a.id == id) return a;
            return null;
        }

        // -------- notification hooks (called by other systems) --------

        public void NotifyCourierDone()
        {
            courierCount++;
            if (courierCount >= 5) Unlock("courier_5");
            if (courierCount >= 25) Unlock("courier_25");
            DailyChallengeSystem.Instance?.Notify("courier");
        }

        public void NotifyDriving(float metersThisFrame)
        {
            totalDriveMeters += metersThisFrame;
            if (totalDriveMeters > 42_195f) Unlock("drive_marathon"); // 42.2 km
            // daily challenges (km stored x10; night drive only after 22:00)
            DailyChallengeSystem.Instance?.Notify("drive_km", Mathf.RoundToInt(metersThisFrame * 10f));
            if (WorldClock.Instance != null && WorldClock.Instance.IsNight)
                DailyChallengeSystem.Instance?.Notify("night_drive", Mathf.RoundToInt(metersThisFrame * 10f));
        }

        public void NotifyRaceFinished(float seconds, bool firstPlace)
        {
            DailyChallengeSystem.Instance?.Notify("races");
            if (!firstPlace) return;
            if (seconds < 90f && !sub1) { Unlock("race_sub90"); sub1 = true; }
            if (seconds < 75f && !sub2) { Unlock("race_sub75"); sub2 = true; }
            if (seconds < 60f && !sub3) { Unlock("race_sub60"); sub3 = true; }
            Unlock("race_first");
        }

        private void Update()
        {
            // level & wealth achievements (cheap checks)
            if (EconomyManager.Instance != null)
            {
                if (EconomyManager.Instance.Level >= 10) Unlock("level_10");
                if (EconomyManager.Instance.Credits >= 10_000) Unlock("rich");
            }
        }

        public void NotifyPoiDiscovered(string poiId)
        {
            if (poiId != null && poiId.StartsWith("biome_")) DailyChallengeSystem.Instance?.Notify("biome_visit");
            if (WorldMapSystem.Instance == null) return;
            int discovered = 0;
            foreach (var p in WorldMapSystem.Instance.AllPOIs) if (p.discovered) discovered++;
            if (discovered >= 20) Unlock("explorer_20");
            if (discovered >= 45) Unlock("explorer_45");
        }

        public void CaptureSaveData(SaveData d) => d.achievements = new List<string>(unlocked);

        public void ApplySaveData(SaveData d)
        {
            unlocked.Clear();
            foreach (var id in d.achievements) { unlocked.Add(id); Unlock(id); }
        }
    }

    /// <summary>
    /// Vehicle progression: which vehicles are unlocked / owned, spawn-on-demand
    /// at garage POIs, paint color selection & persistence.
    /// </summary>
    public class VehicleGarage : MonoBehaviour
    {
        public static VehicleGarage Instance { get; private set; }

        [Serializable]
        public class GarageEntry
        {
            public string vehicleId;
            public Vehicle.VehicleDef def;
            public bool unlocked;
            public bool owned;
        }

        [Header("All vehicles (populated at build from Data/ defs)")]
        [SerializeField] private GarageEntry[] entries;

        [Header("Spawn-on-demand")]
        [SerializeField] private Transform spawnRoot;
        [SerializeField] private float spawnClearance = 4f;

        public event Action<string> OnVehicleUnlocked;
        public event Action<string, string> OnPaintChanged;      // vehicleId, paintId

        public GarageEntry[] Entries => entries;

        private readonly HashSet<string> unlockedIds = new HashSet<string>();
        private readonly HashSet<string> ownedIds = new HashSet<string>();
        private readonly Dictionary<string, string> paints = new Dictionary<string, string>();
        private readonly Dictionary<string, Vehicle.VehicleDef> defsById = new Dictionary<string, Vehicle.VehicleDef>();
        private Vehicle.PlayerVehicle lastSpawned;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            // index defs for id lookups
            if (entries != null)
                foreach (var e in entries)
                    if (e != null && e.def != null) defsById[e.vehicleId] = e.def;
        }

        /// <summary>Editor DataAssetBuilder injects the roster here.</summary>
        public void SetEntries(GarageEntry[] baked) => entries = baked;

        public void Unlock(string vehicleId)
        {
            unlockedIds.Add(vehicleId);
            var e = Find(vehicleId);
            if (e != null) e.unlocked = true;
            OnVehicleUnlocked?.Invoke(vehicleId);
        }

        /// <summary>Force-unlock everything the player owns (used after load).</summary>
        public void UnlockAllOwned()
        {
            foreach (var id in ownedIds) Unlock(id);
        }

        public GarageEntry Find(string vehicleId)
        {
            if (entries == null) return null;
            foreach (var e in entries) if (e != null && e.vehicleId == vehicleId) return e;
            return null;
        }

        public bool TryBuy(string vehicleId, out string message)
        {
            var e = Find(vehicleId);
            if (e == null) { message = "Unknown vehicle."; return false; }
            if (e.owned) { message = "Already owned."; return false; }
            if (!e.unlocked) { message = "Locked — progress the story."; return false; }
            if (EconomyManager.Instance == null || !EconomyManager.Instance.TrySpend(e.def.price))
            { message = "Not enough credits."; return false; }
            e.owned = true;
            ownedIds.Add(vehicleId);
            message = $"{e.def.displayName} added to your garage!";
            UI.UIManager.Instance?.ShowNotification("Vehicle Purchased", message, 3.5f);
            return true;
        }

        // ---------------- paint ----------------

        /// <summary>Purchase & apply a paint to an owned vehicle (premium paints cost credits).</summary>
        public bool TryPaint(string vehicleId, string paintId, out string message)
        {
            var e = Find(vehicleId);
            if (e == null || !e.owned) { message = "You don't own that vehicle."; return false; }
            var target = lastSpawned != null && lastSpawned.Controller.Def.vehicleId == vehicleId
                ? lastSpawned : null;
            var skin = target != null ? target.GetComponent<Vehicle.VehicleSkin>() : null;
            if (skin == null || !skin.TryGetPaint(paintId, out var opt))
            { message = "Unknown paint."; return false; }
            if (opt.price > 0 && EconomyManager.Instance != null
                && !EconomyManager.Instance.TrySpend(opt.price))
            { message = "Not enough credits for that paint."; return false; }
            skin.ApplyPaint(paintId);
            paints[vehicleId] = paintId;
            message = $"{e.def.displayName} repainted {opt.displayName}.";
            OnPaintChanged?.Invoke(vehicleId, paintId);
            SaveSystem.SaveAutosave(MissionManager.Instance.CaptureCurrentState());
            return true;
        }

        /// <summary>Reapply stored paint when a vehicle spawns.</summary>
        public string GetStoredPaint(string vehicleId) =>
            paints.TryGetValue(vehicleId, out var p) ? p : null;

        // ---------------- spawn ----------------

        /// <summary>Spawn an owned vehicle at the garage pad facing the road.</summary>
        public Vehicle.PlayerVehicle SpawnOwned(string vehicleId, Vector3 pos, float heading)
        {
            var e = Find(vehicleId);
            if (e == null || !e.owned)
            {
                UI.UIManager.Instance?.ShowNotification("Garage",
                    e == null ? "Unknown vehicle." : "You don't own that vehicle yet.", 3f);
                return null;
            }
            var prefab = LoadVehiclePrefab(vehicleId);
            if (prefab == null) return null;
            if (spawnRoot == null)
            {
                spawnRoot = new GameObject("GarageSpawns").transform;
                DontDestroyOnLoad(spawnRoot.gameObject);
            }
            if (lastSpawned != null) Destroy(lastSpawned.gameObject);
            var go = Instantiate(prefab, pos, Quaternion.Euler(0f, heading, 0f), spawnRoot);
            var pv = go.GetComponent<Vehicle.PlayerVehicle>();
            lastSpawned = pv;
            // reapply saved paint
            var skin = go.GetComponent<Vehicle.VehicleSkin>();
            var paint = GetStoredPaint(vehicleId);
            if (skin != null && !string.IsNullOrEmpty(paint)) skin.ApplyPaint(paint);
            UI.UIManager.Instance?.ShowNotification("Garage",
                $"{e.def.displayName} delivered.", 2.5f);
            return pv;
        }

        private static GameObject LoadVehiclePrefab(string vehicleId)
        {
#if UNITY_EDITOR
            var path = $"Assets/VALEBOUND/Prefabs/Vehicles/Vehicle_{vehicleId}.prefab";
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            // runtime: resources folder copy built by BuildScript
            return UnityEngine.Resources.Load<GameObject>($"Vehicles/Vehicle_{vehicleId}");
#endif
        }

        public void CaptureSaveData(SaveData d)
        {
            d.unlockedVehicles = new List<string>(unlockedIds);
            d.ownedVehicleIds = new List<string>(ownedIds);
            d.vehiclePaints = new List<string>();
            foreach (var kv in paints)
            {
                d.vehiclePaints.Add(kv.Key);
                d.vehiclePaints.Add(kv.Value);
            }
        }

        public void ApplySaveData(SaveData d)
        {
            unlockedIds.Clear(); ownedIds.Clear(); paints.Clear();
            foreach (var id in d.unlockedVehicles) unlockedIds.Add(id);
            foreach (var id in d.ownedVehicleIds) ownedIds.Add(id);
            for (int i = 0; i + 1 < d.vehiclePaints.Count; i += 2)
                paints[d.vehiclePaints[i]] = d.vehiclePaints[i + 1];
            if (entries != null)
                foreach (var e in entries)
                {
                    if (e == null) continue;
                    e.unlocked = unlockedIds.Contains(e.vehicleId) || e.def != null && e.def.price == 0;
                    e.owned = ownedIds.Contains(e.vehicleId) || e.def != null && e.def.price == 0;
                }
        }
    }
}
