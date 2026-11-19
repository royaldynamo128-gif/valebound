using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound
{
    /// <summary>
    /// Simple strongly-typed event bus. Systems publish; UI subscribes.
    /// Keeps coupling low without GameObject.Find spaghetti.
    /// </summary>
    public static class GameEvents
    {
        public static event Action<string, string, float> MissionOffered;      // id, title, reward
        public static event Action<string> MissionStarted;
        public static event Action<string, bool> MissionCompleted;             // id, success
        public static event Action<string> MissionObjectiveChanged;            // description
        public static event Action<int> CreditsChanged;
        public static event Action<int, int> LevelUp;                          // new level, xp carried
        public static event Action<string> AchievementUnlocked;
        public static event Action<int> ShardCollected;                        // shard index
        public static event Action<string> POIDiscovered;                      // poi id
        public static event Action<Vehicle.VehicleController> VehicleUnlocked;
        public static event Action WeatherChanged;
        public static event Action<int> HourChanged;                           // world hour tick

        public static void RaiseMissionOffered(string id, string title, float reward) => MissionOffered?.Invoke(id, title, reward);
        public static void RaiseMissionStarted(string id) => MissionStarted?.Invoke(id);
        public static void RaiseMissionCompleted(string id, bool success) => MissionCompleted?.Invoke(id, success);
        public static void RaiseMissionObjectiveChanged(string desc) => MissionObjectiveChanged?.Invoke(desc);
        public static void RaiseCreditsChanged(int newCredits) => CreditsChanged?.Invoke(newCredits);
        public static void RaiseLevelUp(int level, int xp) => LevelUp?.Invoke(level, xp);
        public static void RaiseAchievementUnlocked(string id) => AchievementUnlocked?.Invoke(id);
        public static void RaiseShardCollected(int index) => ShardCollected?.Invoke(index);
        public static void RaisePOIDiscovered(string id) => POIDiscovered?.Invoke(id);
        public static void RaiseVehicleUnlocked(Vehicle.VehicleController v) => VehicleUnlocked?.Invoke(v);
        public static void RaiseWeatherChanged() => WeatherChanged?.Invoke();
        public static void RaiseHourChanged(int hour) => HourChanged?.Invoke(hour);
    }

    /// <summary>Lightweight localization-free text store (English ship language).</summary>
    public static class GameText
    {
        private static readonly Dictionary<string, string> Table = new Dictionary<string, string>
        {
            ["credits"] = "Credits",
            ["mission_new"] = "New Mission Available",
            ["mission_complete"] = "Mission Complete",
            ["mission_failed"] = "Mission Failed",
            ["objective"] = "Objective",
            ["interact"] = "Interact",
            ["enter_vehicle"] = "Enter Vehicle",
            ["exit_vehicle"] = "Exit Vehicle",
            ["save_game"] = "Save Game",
            ["autosave"] = "Autosave",
            ["level_up"] = "Level Up",
            ["checkpoint"] = "Checkpoint",
            ["fast_travel"] = "Fast Travel",
            ["unlock"] = "Unlocked",
            ["shard"] = "Signal Shard",
            ["achievements"] = "Achievements",
            ["settings"] = "Settings",
            ["resume"] = "Resume",
            ["main_menu"] = "Main Menu",
            ["new_game"] = "New Game",
            ["continue"] = "Continue",
            ["loading"] = "Loading Aurora Valley...",
            ["pause"] = "Paused",
            ["game_over"] = "Wiped Out",
            ["respawn"] = "Return to Checkpoint",
        };

        public static string Get(string key) =>
            Table.TryGetValue(key, out var v) ? v : key;
    }
}
