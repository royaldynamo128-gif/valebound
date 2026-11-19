using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Valebound
{
    /// <summary>All persisted data for one save slot. Serialized as JSON (human-readable, robust).</summary>
    [Serializable]
    public class SaveData
    {
        // meta
        public int version = 1;
        public string saveDateUtc;
        public float playTimeSeconds;

        // player
        public float px, py, pz;
        public float heading;
        public float health;
        public int level;
        public int xp;

        // world
        public float worldTimeHours;
        public int weatherIndex;

        // economy
        public int credits;
        public List<string> unlockedVehicles = new List<string>();
        public List<string> ownedVehicleIds = new List<string>();
        public string currentVehicleId;
        public List<string> vehiclePaints = new List<string>();   // pairs: vehicleId, paintId

        // daily challenges
        public string dailyChallengeDate = "";
        public List<string> dailyChallengeIds = new List<string>();
        public List<string> completedDailyIds = new List<string>();

        // stats
        public float totalAirTimeSeconds;
        public int taxiJobsDone;

        // missions
        public int storyProgress;
        public List<string> completedMissionIds = new List<string>();
        public List<string> activeMissionIds = new List<string>();
        public List<string> offeredMissionIds = new List<string>();
        public List<string> availableMissionIds = new List<string>();

        // exploration
        public List<string> discoveredPois = new List<string>();
        public List<string> unlockedFastTravel = new List<string>();
        public List<int> collectedShards = new List<int>();
        public List<string> achievements = new List<string>();

        // settings (persisted separately but kept here as backup)
        public int graphicsTier;
    }

    /// <summary>
    /// JSON-file save system. One autosave slot + 3 manual slots.
    /// Writes atomically (temp file + rename) to avoid corruption on app pause.
    /// </summary>
    public static class SaveSystem
    {
        public const int LatestVersion = 1;
        private static readonly string Dir =
#if UNITY_EDITOR || UNITY_STANDALONE
            Application.persistentDataPath;
#else
            Application.persistentDataPath;
#endif

        public static string SaveDir => Dir;

        public static void EnsureDirectory()
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
        }

        // ---------------- autosave ----------------

        public static void SaveAutosave(SaveData data)
        {
            data.version = LatestVersion;
            data.saveDateUtc = DateTime.UtcNow.ToString("o");
            WriteFile(AutoPath, data);
        }

        public static SaveData LoadAutosave()
        {
            return ReadFile(AutoPath);
        }

        public static bool AutosaveExists() => File.Exists(AutoPath);

        public static void DeleteAutosave()
        {
            if (File.Exists(AutoPath)) File.Delete(AutoPath);
        }

        public static string AutoPath => Path.Combine(Dir, "valebound_autosave.json");

        // ---------------- manual slots ----------------

        public static string SlotPath(int slot) => Path.Combine(Dir, $"valebound_slot{slot}.json");

        public static void SaveSlot(int slot, SaveData data)
        {
            data.version = LatestVersion;
            data.saveDateUtc = DateTime.UtcNow.ToString("o");
            WriteFile(SlotPath(slot), data);
        }

        public static SaveData LoadSlot(int slot) => ReadFile(SlotPath(slot));

        public static bool SlotExists(int slot) => File.Exists(SlotPath(slot));

        public static List<SaveData> ListSlots()
        {
            var list = new List<SaveData>();
            for (int i = 0; i < 3; i++)
            {
                if (SlotExists(i)) list.Add(ReadFile(SlotPath(i)));
                else list.Add(null);
            }
            return list;
        }

        public static SaveData Load()
        {
            var data = LoadAutosave();
            if (data != null) return data;
            for (int i = 2; i >= 0; i--)
            {
                var d = LoadSlot(i);
                if (d != null) return d;
            }
            return null;
        }

        // ---------------- settings ----------------

        public static void SaveSettings(GameSettings settings)
        {
            WriteFile(SettingsPath, settings);
        }

        public static GameSettings LoadSettings()
        {
            var s = ReadFileGeneric<GameSettings>(SettingsPath);
            return s ?? new GameSettings();
        }

        public static string SettingsPath => Path.Combine(Dir, "settings.json");

        // ---------------- low-level ----------------

        private static void WriteFile<T>(string path, T obj)
        {
            try
            {
                EnsureDirectory();
                string json = JsonUtility.ToJson(obj, prettyPrint: true);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Failed writing {path}: {e.Message}");
            }
        }

        private static SaveData ReadFile(string path)
        {
            return ReadFileGeneric<SaveData>(path);
        }

        private static T ReadFileGeneric<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Failed reading {path}: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>User-facing options; persisted independently of save games.</summary>
    [Serializable]
    public class GameSettings
    {
        public int graphicsTier = 1;          // 0 low 1 medium 2 high
        public float musicVolume = 0.8f;
        public float sfxVolume = 1f;
        public float ambienceVolume = 0.8f;
        public float cameraSensitivity = 1f;
        public bool invertCameraY = false;
        public bool leftHanded = false;
        public float joystickSize = 1f;
        public int targetFps = 60;
        public bool showFps = false;
    }
}
