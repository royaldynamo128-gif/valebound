using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound.Systems
{
    public enum POIType { CityCenter, Landmark, Shop, MissionGiver, FastTravel, Vista, Shard, Garage }

    [Serializable]
    public class POI
    {
        public string poiId;
        public string displayName;
        public POIType type;
        public Vector3 worldPos;
        public bool discovered;
    }

    /// <summary>
    /// Registry of all points of interest, discovered flags, minimap icon
    /// providers, and fast travel logic. Data baked by WorldBuilder into a JSON
    /// asset loaded at boot (no runtime mesh generation).
    /// </summary>
    public class WorldMapSystem : MonoBehaviour
    {
        public static WorldMapSystem Instance { get; private set; }

        [Header("Data (baked)")]
        [SerializeField] private POI[] pois;

        public POI[] AllPOIs => pois;
        public event Action<POI> OnPOIDiscovered;

        private readonly HashSet<string> discovered = new HashSet<string>();
        private Systems.EconomyManager economy;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null || pois == null) return;

            // discovery by proximity (cheap: every 15 frames)
            if (Time.frameCount % 15 != 0) return;
            Vector3 p = player.transform.position;
            foreach (var poi in pois)
            {
                if (poi.discovered) continue;
                float r = poi.type == POIType.CityCenter ? 60f : 26f;
                if ((poi.worldPos - p).sqrMagnitude <= r * r)
                {
                    poi.discovered = true;
                    discovered.Add(poi.poiId);
                    OnPOIDiscovered?.Invoke(poi);
                    GameEvents.RaisePOIDiscovered(poi.poiId);
                    UI.UIManager.Instance?.ShowNotification("Discovered",
                        poi.displayName, 2.5f);
                    Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/discover", 0.6f);
                }
            }
        }

        public POI FindPOI(string id)
        {
            if (pois == null) return null;
            foreach (var p in pois) if (p.poiId == id) return p;
            return null;
        }

        /// <summary>Editor WorldBuilder injects baked POI data here.</summary>
        public void SetPOIs(POI[] baked) => pois = baked;

        public List<POI> GetByType(POIType type)
        {
            var list = new List<POI>();
            if (pois == null) return list;
            foreach (var p in pois) if (p.type == type) list.Add(p);
            return list;
        }

        public void CaptureSaveData(SaveData d) => d.discoveredPois = new List<string>(discovered);

        public void ApplySaveData(SaveData d)
        {
            discovered.Clear();
            if (pois == null) return;
            foreach (var id in d.discoveredPois) discovered.Add(id);
            foreach (var p in pois) p.discovered = discovered.Contains(p.poiId);
        }
    }

    /// <summary>
    /// Fast travel between unlocked stops (guild offices, landmarks). Costs a small
    /// fee, blocked while a timed mission is active.
    /// </summary>
    public class FastTravelSystem : MonoBehaviour
    {
        public static FastTravelSystem Instance { get; private set; }

        [SerializeField] private int costPerKm = 4;
        [SerializeField] private int minLevel = 3;

        private readonly HashSet<string> unlocked = new HashSet<string>();

        public event Action<string> OnFastTravelUnlocked;
        public event Action<string> OnFastTravelled;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public bool IsUnlocked(string poiId) => unlocked.Contains(poiId);

        public void Unlock(string poiId)
        {
            if (unlocked.Add(poiId))
            {
                OnFastTravelUnlocked?.Invoke(poiId);
                UI.UIManager.Instance?.ShowNotification("Fast Travel Unlocked",
                    WorldMapSystem.Instance?.FindPOI(poiId)?.displayName ?? poiId, 3f);
            }
        }

        public bool CanFastTravel(string poiId, out string reason)
        {
            reason = null;
            var mm = MissionManager.Instance;
            if (mm != null && mm.CurrentFocus != null)
            {
                var obj = mm.CurrentObjective;
                if (obj != null && obj.timeLimit > 0f) { reason = "Finish the timed job first."; return false; }
            }
            var eco = EconomyManager.Instance;
            if (eco != null && eco.Level < minLevel) { reason = $"Unlocks at level {minLevel}."; return false; }
            if (!unlocked.Contains(poiId)) { reason = "Stop not unlocked."; return false; }
            return true;
        }

        public int Price(Vector3 from, Vector3 to) =>
            Mathf.Max(5, Mathf.RoundToInt(Vector3.Distance(from, to) / 1000f * costPerKm));

        public void Travel(string poiId)
        {
            if (!CanFastTravel(poiId, out _)) return;
            var poi = WorldMapSystem.Instance?.FindPOI(poiId);
            if (poi == null) return;
            var player = GameManager.Instance?.Player;
            if (player == null) return;

            int price = Price(player.transform.position, poi.worldPos);
            if (EconomyManager.Instance != null && !EconomyManager.Instance.TrySpend(price))
            {
                UI.UIManager.Instance?.ShowNotification("Not enough credits",
                    $"Fast travel costs {price} credits.", 3f);
                return;
            }

            StartCoroutine(TravelRoutine(player, poi));
        }

        private System.Collections.IEnumerator TravelRoutine(PlayerController player, POI poi)
        {
            var fade = UI.UIManager.Instance != null ? UI.UIManager.Instance.FadeOverlay : null;
            if (fade != null) yield return fade.FadeOut(0.35f);
            player.Teleport(poi.worldPos + Vector3.up * 1f, restoreHealth: false);
            Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/fasttravel", 0.8f);
            yield return new WaitForSeconds(0.15f);
            if (fade != null) yield return fade.FadeIn(0.35f);
            OnFastTravelled?.Invoke(poi.poiId);
        }

        public void CaptureSaveData(SaveData d) => d.unlockedFastTravel = new List<string>(unlocked);

        public void ApplySaveData(SaveData d)
        {
            unlocked.Clear();
            foreach (var id in d.unlockedFastTravel) unlocked.Add(id);
        }
    }
}
