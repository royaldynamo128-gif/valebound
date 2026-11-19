using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound.Systems
{
    /// <summary>
    /// Checkpoint race activity: gates along a baked route; pass gates in order,
    /// beat the clock. Trigger starts via interactable start marker.
    /// </summary>
    public class RaceActivity : MonoBehaviour
    {
        [Serializable]
        public class Gate
        {
            public Vector3 pos;
            public float radius = 9f;
            [NonSerialized] public bool passed;
        }

        [Serializable]
        public class RaceDef
        {
            public string raceId;
            public string raceName;
            public Gate[] gates;
            public float timeLimitSeconds = 120f;
            public int rewardCredits = 250;
        }

        [Header("Races (baked)")]
        [SerializeField] private RaceDef[] races;

        public RaceDef[] Races => races;

        /// <summary>Editor WorldBuilder injects baked race data here.</summary>
        public void SetRaces(RaceDef[] baked) => races = baked;

        public RaceDef ActiveRace { get; private set; }
        public int CurrentGate { get; private set; }
        public float RaceTimer { get; private set; }
        public bool Racing => ActiveRace != null;

        public event Action<RaceDef> OnRaceStarted;
        public event Action<RaceDef, float, bool> OnRaceFinished; // def, time, firstPlace

        private void Update()
        {
            if (!Racing) return;
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null) { StopRace(false, silent: true); return; }

            RaceTimer += Time.deltaTime;
            if (RaceTimer > ActiveRace.timeLimitSeconds)
            {
                StopRace(false);
                return;
            }

            var gate = ActiveRace.gates[CurrentGate];
            if ((player.transform.position - gate.pos).sqrMagnitude <= gate.radius * gate.radius)
            {
                gate.passed = true;
                CurrentGate++;
                Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/gate", 0.8f);
                UI.UIManager.Instance?.ShowNotification("Gate",
                    $"{CurrentGate}/{ActiveRace.gates.Length}  ·  {Mathf.Max(0, ActiveRace.timeLimitSeconds - RaceTimer):F0}s", 1.2f);
                if (CurrentGate >= ActiveRace.gates.Length)
                {
                    StopRace(true);
                }
            }
        }

        public void StartRace(string raceId)
        {
            if (Racing) return;
            foreach (var r in races)
            {
                if (r.raceId != raceId) continue;
                foreach (var g in r.gates) g.passed = false;
                ActiveRace = r;
                CurrentGate = 0;
                RaceTimer = 0f;
                OnRaceStarted?.Invoke(r);
                UI.UIManager.Instance?.ShowMissionBanner(r.raceName, "Pass every gate before the clock runs out!");
                Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/race_start", 0.9f);
                return;
            }
        }

        private void StopRace(bool finished, bool silent = false)
        {
            var race = ActiveRace;
            ActiveRace = null;
            if (race == null) return;
            OnRaceFinished?.Invoke(race, RaceTimer, finished);
            if (silent) return;
            if (finished)
            {
                Systems.EconomyManager.Instance?.AddCredits(race.rewardCredits);
                Systems.EconomyManager.Instance?.AddXp(Mathf.RoundToInt(race.rewardCredits * 0.4f));
                UI.UIManager.Instance?.ShowMissionBanner("Race Complete",
                    $"Final time {RaceTimer:F1}s! +{race.rewardCredits} credits.");
                Systems.AchievementSystem.Instance?.NotifyRaceFinished(RaceTimer, true);
                Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/mission_complete", 0.9f);
            }
            else
            {
                UI.UIManager.Instance?.ShowNotification("Race Over", "Out of time. Try again!", 3f);
                Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/fail", 0.8f);
            }
        }
    }
}
