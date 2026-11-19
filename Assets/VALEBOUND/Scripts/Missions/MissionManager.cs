using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound
{
    // =====================================================================
    // MISSION DATA
    // =====================================================================

    public enum ObjectiveType { Goto, Deliver, EnterVehicle, DriveTo, Collect, SurviveTimer, Race, Photo, Meet }

    [Serializable]
    public class MissionObjective
    {
        public ObjectiveType type;
        public string description;
        public Vector3 targetPos;
        public float radius = 6f;
        public float timeLimit;          // 0 = none
        public string vehicleId;         // for EnterVehicle
        public int count = 1;            // for Collect
        public int collected;
        [NonSerialized] public bool complete;
    }

    [Serializable]
    public class MissionReward
    {
        public int credits;
        public int xp;
        public string unlockVehicleId;
        public string achievementId;
    }

    [CreateAssetMenu(fileName = "MissionDef", menuName = "VALEBOUND/Mission Definition")]
    public class MissionDef : ScriptableObject
    {
        [Header("Identity")]
        public string missionId;
        public string title;
        [TextArea(2, 6)] public string introText;
        [TextArea(2, 6)] public string completeText;
        public string giverNpcId;
        public int storyIndex = -1;          // -1 = side mission
        public bool isMainStory;

        [Header("Flow")]
        public MissionObjective[] objectives;
        public MissionReward reward = new MissionReward();

        [Header("Prereqs")]
        public string requiresMissionId;
        public int requiredPlayerLevel = 1;
    }

    // =====================================================================
    // MISSION MANAGER
    // =====================================================================

    /// <summary>
    /// Owns mission state: what's offered / active / completed, objective
    /// progression, rewards, checkpoints, and the courier generator.
    /// Exposes events for HUD and the world map.
    /// </summary>
    public class MissionManager : MonoBehaviour
    {
        public static MissionManager Instance { get; private set; }

        [Header("Registry (populated at build)")]
        [SerializeField] private MissionDef[] allMissions;

        [Header("Courier generator")]
        [SerializeField] private int courierUnlockLevel = 2;

        public MissionDef[] AllMissions => allMissions;
        public List<MissionDef> ActiveMissions { get; } = new List<MissionDef>();
        public List<MissionDef> OfferedMissions { get; } = new List<MissionDef>();
        public List<MissionDef> CompletedMissions { get; } = new List<MissionDef>();

        public MissionDef CurrentFocus { get; private set; }
        public MissionObjective CurrentObjective =>
            CurrentFocus != null && CurrentFocus.objectives != null && objectiveIndex < CurrentFocus.objectives.Length
                ? CurrentFocus.objectives[objectiveIndex] : null;

        private int objectiveIndex;
        private float objectiveTimer;
        private readonly HashSet<string> completedIds = new HashSet<string>();
        private readonly Dictionary<string, int> collectProgress = new Dictionary<string, int>();
        private Vector3 checkpointPos;
        private Systems.CourierGenerator courier;
        private Systems.EconomyManager economy;

        public event Action<MissionDef> OnFocusChanged;
        public event Action OnMissionListChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            economy = Systems.EconomyManager.Instance;
            courier = GetComponent<Systems.CourierGenerator>();
            if (allMissions != null)
            {
                foreach (var m in allMissions)
                {
                    if (m.isMainStory && m.storyIndex == 0)
                        OfferMission(m);
                }
            }
        }

        // ---------------- offering / accept ----------------

        public void OfferMission(MissionDef def)
        {
            if (def == null || completedIds.Contains(def.missionId) || OfferedMissions.Contains(def) || ActiveMissions.Contains(def)) return;
            OfferedMissions.Add(def);
            OnMissionListChanged?.Invoke();
            GameEvents.RaiseMissionOffered(def.missionId, def.title, def.reward.credits);
        }

        public bool CanOffer(MissionDef def)
        {
            if (def == null) return false;
            if (completedIds.Contains(def.missionId)) return false;
            if (!string.IsNullOrEmpty(def.requiresMissionId) && !completedIds.Contains(def.requiresMissionId)) return false;
            if (economy != null && economy.Level < def.requiredPlayerLevel) return false;
            return true;
        }

        public void AcceptMission(MissionDef def)
        {
            if (!OfferedMissions.Remove(def)) return;
            if (ActiveMissions.Contains(def)) return;
            ActiveMissions.Add(def);
            if (CurrentFocus == null) SetFocus(def);
            GameEvents.RaiseMissionStarted(def.missionId);
            UI.UIManager.Instance?.ShowMissionBanner(def.title, def.introText);
            OnMissionListChanged?.Invoke();
        }

        public void AbandonMission(MissionDef def)
        {
            ActiveMissions.Remove(def);
            OfferedMissions.Add(def);
            if (CurrentFocus == def) SetFocus(ActiveMissions.Count > 0 ? ActiveMissions[0] : null);
            OnMissionListChanged?.Invoke();
        }

        public void SetFocus(MissionDef def)
        {
            CurrentFocus = def;
            objectiveIndex = 0;
            objectiveTimer = 0f;
            ResetObjectiveState();
            OnFocusChanged?.Invoke(def);
            if (CurrentObjective != null)
                GameEvents.RaiseMissionObjectiveChanged(CurrentObjective.description);
        }

        private void ResetObjectiveState()
        {
            if (CurrentFocus == null || CurrentFocus.objectives == null) return;
            foreach (var o in CurrentFocus.objectives) { o.complete = false; o.collected = 0; }
        }

        // ---------------- ticking ----------------

        private void Update()
        {
            var obj = CurrentObjective;
            if (obj == null || obj.complete) return;

            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null) return;

            float dt = Time.deltaTime;

            // timed objective
            if (obj.timeLimit > 0f)
            {
                objectiveTimer += dt;
                if (objectiveTimer > obj.timeLimit)
                {
                    FailMission(CurrentFocus);
                    return;
                }
            }

            // proximity objectives
            switch (obj.type)
            {
                case ObjectiveType.Goto:
                case ObjectiveType.Meet:
                case ObjectiveType.Photo:
                    if (NearTarget(player.transform.position, obj))
                        CompleteObjective();
                    break;

                case ObjectiveType.DriveTo:
                    if (player.InVehicle && NearTarget(player.transform.position, obj))
                        CompleteObjective();
                    break;

                case ObjectiveType.Deliver:
                    if (NearTarget(player.transform.position, obj))
                        CompleteObjective();
                    break;

                case ObjectiveType.EnterVehicle:
                    if (GameManager.Instance != null && GameManager.Instance.CurrentVehicle != null
                        && (string.IsNullOrEmpty(obj.vehicleId) ||
                            GameManager.Instance.CurrentVehicle.Controller.Def.vehicleId == obj.vehicleId))
                        CompleteObjective();
                    break;

                case ObjectiveType.Collect:
                    // collect progress advanced externally via NotifyCollected
                    break;

                case ObjectiveType.SurviveTimer:
                    objectiveTimer += dt;
                    if (objectiveTimer >= obj.count) CompleteObjective();
                    break;

                case ObjectiveType.Race:
                    // race logic handled by RaceMissionComponent; check for completion marker
                    if (NearTarget(player.transform.position, obj)) CompleteObjective();
                    break;
            }
        }

        private bool NearTarget(Vector3 pos, MissionObjective obj)
        {
            Vector3 t = obj.targetPos;
            return (pos - t).sqrMagnitude <= obj.radius * obj.radius;
        }

        public void NotifyCollected(string missionId, int amount)
        {
            var def = ActiveMissions.Find(m => m.missionId == missionId);
            var obj = def != null && def.objectives != null ? CurrentObjective : null;
            if (obj == null || obj.type != ObjectiveType.Collect) return;
            obj.collected += amount;
            GameEvents.RaiseMissionObjectiveChanged($"{obj.description} ({obj.collected}/{obj.count})");
            if (obj.collected >= obj.count) CompleteObjective();
        }

        public void NotifyVehicleEntered(string vehicleId)
        {
            var obj = CurrentObjective;
            if (obj != null && obj.type == ObjectiveType.EnterVehicle && obj.vehicleId == vehicleId)
                CompleteObjective();
        }

        public void CompleteObjective()
        {
            var obj = CurrentObjective;
            if (obj == null || obj.complete) return;
            obj.complete = true;
            objectiveTimer = 0f;

            if (CurrentFocus.objectives != null && objectiveIndex < CurrentFocus.objectives.Length - 1)
            {
                objectiveIndex++;
                var next = CurrentObjective;
                GameEvents.RaiseMissionObjectiveChanged(next.description);
                Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/objective_next", 0.7f);
            }
            else
            {
                CompleteMission(CurrentFocus);
            }
        }

        public void CompleteMission(MissionDef def)
        {
            if (def == null || !ActiveMissions.Remove(def)) return;
            completedIds.Add(def.missionId);
            CompletedMissions.Add(def);
            checkpointPos = GameManager.Instance != null && GameManager.Instance.Player != null
                ? GameManager.Instance.Player.transform.position : checkpointPos;

            // rewards
            if (economy != null)
            {
                economy.AddCredits(def.reward.credits);
                economy.AddXp(def.reward.xp);
            }
            if (!string.IsNullOrEmpty(def.reward.unlockVehicleId))
                Systems.VehicleGarage.Instance?.Unlock(def.reward.unlockVehicleId);
            // roster vehicles with unlockMissionIndex <= this story chapter become purchasable
            if (def.isMainStory && Systems.VehicleGarage.Instance != null
                && Systems.VehicleGarage.Instance.Entries != null)
            {
                foreach (var e in Systems.VehicleGarage.Instance.Entries)
                {
                    if (e?.def == null || e.unlocked) continue;
                    if (e.def.unlockMissionIndex >= 0 && e.def.unlockMissionIndex <= def.storyIndex)
                        Systems.VehicleGarage.Instance.Unlock(e.vehicleId);
                }
            }
            if (!string.IsNullOrEmpty(def.reward.achievementId))
                Systems.AchievementSystem.Instance?.Unlock(def.reward.achievementId);

            GameEvents.RaiseMissionCompleted(def.missionId, true);
            UI.UIManager.Instance?.ShowMissionBanner("Mission Complete", def.completeText);
            Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/mission_complete", 0.9f);

            // chain: next story mission
            if (def.isMainStory)
            {
                var next = FindNextStory(def.storyIndex + 1);
                if (next != null && CanOffer(next)) OfferMission(next);
            }

            if (CurrentFocus == def)
                SetFocus(ActiveMissions.Count > 0 ? ActiveMissions[0] : null);
            OnMissionListChanged?.Invoke();

            // autosave after every mission
            SaveSystem.SaveAutosave(CaptureCurrentState());
        }

        public void FailMission(MissionDef def)
        {
            if (def == null || !ActiveMissions.Contains(def)) return;
            ActiveMissions.Remove(def);
            OfferedMissions.Add(def); // allow retry
            ResetObjectiveState();
            if (CurrentFocus == def) SetFocus(null);
            GameEvents.RaiseMissionCompleted(def.missionId, false);
            UI.UIManager.Instance?.ShowNotification("Mission Failed",
                "Take a breath — the job is still on the board.", 3.5f);
            OnMissionListChanged?.Invoke();
        }

        private MissionDef FindNextStory(int index)
        {
            if (allMissions == null) return null;
            foreach (var m in allMissions)
                if (m.isMainStory && m.storyIndex == index) return m;
            return null;
        }

        // ---------------- checkpoints ----------------

        public Vector3 GetCheckpointPosition() =>
            checkpointPos != Vector3.zero ? checkpointPos
            : (GameManager.Instance?.Player != null ? GameManager.Instance.Player.transform.position
               : new Vector3(0f, 1f, 0f));

        public void SetCheckpoint(Vector3 pos) => checkpointPos = pos;

        // ---------------- courier generator ----------------

        public void TryGenerateCourierJob()
        {
            courier?.GenerateJob();
        }

        // ---------------- save / load ----------------

        public void CaptureSaveData(SaveData d)
        {
            d.storyProgress = CurrentStoryProgress();
            d.completedMissionIds = new List<string>(completedIds);
            d.activeMissionIds = ActiveMissions.ConvertAll(m => m.missionId);
            d.offeredMissionIds = OfferedMissions.ConvertAll(m => m.missionId);
        }

        public void ApplySaveData(SaveData d)
        {
            completedIds.Clear();
            foreach (var id in d.completedMissionIds) completedIds.Add(id);
            CompletedMissions.Clear();
            ActiveMissions.Clear();
            OfferedMissions.Clear();

            if (allMissions != null)
            {
                foreach (var m in allMissions)
                {
                    if (completedIds.Contains(m.missionId))
                    {
                        CompletedMissions.Add(m);
                        continue;
                    }
                    if (d.activeMissionIds.Contains(m.missionId)) ActiveMissions.Add(m);
                    else if (d.offeredMissionIds.Contains(m.missionId)) OfferedMissions.Add(m);
                }
                // ensure next story mission is offered after load
                var next = FindNextStory(CurrentStoryProgress());
                if (next != null && !ActiveMissions.Contains(next) && CanOffer(next) && !OfferedMissions.Contains(next))
                    OfferMission(next);
            }
            SetFocus(ActiveMissions.Count > 0 ? ActiveMissions[0] : null);
            OnMissionListChanged?.Invoke();
        }

        public int CurrentStoryProgress()
        {
            int best = -1;
            foreach (var id in completedIds)
            {
                var m = FindById(id);
                if (m != null && m.isMainStory) best = Mathf.Max(best, m.storyIndex);
            }
            return best;
        }

        public MissionDef FindById(string id)
        {
            if (allMissions == null) return null;
            foreach (var m in allMissions)
                if (m.missionId == id) return m;
            return null;
        }

        public void ResetAllProgress()
        {
            completedIds.Clear();
            ActiveMissions.Clear();
            OfferedMissions.Clear();
            CompletedMissions.Clear();
            SetFocus(null);
            if (allMissions != null)
                foreach (var m in allMissions)
                    if (m.isMainStory && m.storyIndex == 0) OfferMission(m);
            OnMissionListChanged?.Invoke();
        }

        public SaveData CaptureCurrentState()
        {
            var d = new SaveData();
            var gm = GameManager.Instance;
            if (gm != null && gm.Player != null) gm.Player.CaptureSaveData(d);
            Systems.WorldClock.Instance?.CaptureSaveData(d);
            Systems.WeatherSystem.Instance?.CaptureSaveData(d);
            Systems.EconomyManager.Instance?.CaptureSaveData(d);
            Systems.AchievementSystem.Instance?.CaptureSaveData(d);
            Systems.CollectibleManager.Instance?.CaptureSaveData(d);
            Systems.FastTravelSystem.Instance?.CaptureSaveData(d);
            Systems.VehicleGarage.Instance?.CaptureSaveData(d);
            Systems.DailyChallengeSystem.Instance?.CaptureSaveData(d);
            CaptureSaveData(d);
            return d;
        }
    }
}
