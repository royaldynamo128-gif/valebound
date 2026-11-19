using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound
{
    /// <summary>
    /// Central game state machine. Single authoritative source for:
    /// play state, time scale, player reference, and global events.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public enum GameState
        {
            Boot,
            MainMenu,
            Loading,
            Playing,
            Paused,
            GameOver,
            Cutscene
        }

        [Header("State")]
        [SerializeField] private GameState currentState = GameState.Boot;

        [Header("Prefabs")]
        [Tooltip("Player prefab spawned on New Game / Continue")]
        [SerializeField] private GameObject playerPrefab;
        [Tooltip("UI canvas prefab (HUD + menus)")]
        [SerializeField] private GameObject uiPrefab;

        public GameState State => currentState;
        public PlayerController Player { get; private set; }
        public Vehicle.PlayerVehicle CurrentVehicle { get; private set; }

        public event Action<GameState> OnGameStateChanged;
        public event Action<Valebound.PlayerController> OnPlayerSpawned;
        public event Action<Valebound.Vehicle.PlayerVehicle> OnVehicleEntered;
        public event Action<Valebound.Vehicle.PlayerVehicle> OnVehicleExited;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            QualitySettings.shadowDistance = 80f;
        }

        private void Start()
        {
            SetState(GameState.MainMenu);
        }

        public void SetState(GameState newState)
        {
            if (currentState == newState) return;
            currentState = newState;
            Time.timeScale = newState == GameState.Paused ? 0f : 1f;
            AudioListener.pause = newState == GameState.Paused;
            OnGameStateChanged?.Invoke(newState);
        }

        public void SetPlayer(PlayerController player)
        {
            Player = player;
            if (player != null) OnPlayerSpawned?.Invoke(player);
        }

        public void NotifyVehicleEntered(Valebound.Vehicle.PlayerVehicle v)
        {
            CurrentVehicle = v;
            OnVehicleEntered?.Invoke(v);
        }

        public void NotifyVehicleExited(Valebound.Vehicle.PlayerVehicle v)
        {
            if (CurrentVehicle == v) CurrentVehicle = null;
            OnVehicleExited?.Invoke(v);
        }

        // ---- flow control called by UI ----

        public void StartNewGame()
        {
            StartCoroutine(StartNewGameRoutine());
        }

        private IEnumerator StartNewGameRoutine()
        {
            SetState(GameState.Loading);
            SaveSystem.DeleteAutosave();
            yield return null; // let loading screen paint
            SpawnPlayerAtSpawnPoint();
            MissionManager.Instance?.ResetAllProgress();
            SetState(GameState.Playing);
        }

        public void ContinueGame()
        {
            StartCoroutine(ContinueGameRoutine());
        }

        private IEnumerator ContinueGameRoutine()
        {
            SetState(GameState.Loading);
            yield return null;
            var data = SaveSystem.Load();
            if (data == null)
            {
                SpawnPlayerAtSpawnPoint();
            }
            else
            {
                SpawnPlayerAtSpawnPoint();
                ApplySaveToWorld(data);
            }
            SetState(GameState.Playing);
        }

        public void PauseGame()
        {
            if (currentState != GameState.Playing) return;
            SetState(GameState.Paused);
        }

        public void ResumeGame()
        {
            if (currentState != GameState.Paused) return;
            SetState(GameState.Playing);
        }

        public void ReturnToMainMenu()
        {
            SetState(GameState.MainMenu);
        }

        public void TriggerGameOver(string reason)
        {
            if (currentState != GameState.Playing) return;
            SetState(GameState.GameOver);
        }

        /// <summary>Respawn player at the last checkpoint with a small fade.</summary>
        public void RespawnPlayer()
        {
            StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            if (Player == null) yield break;
            var fade = UI.UIManager.Instance != null ? UI.UIManager.Instance.FadeOverlay : null;
            if (fade != null) yield return fade.FadeOut(0.4f);

            if (CurrentVehicle != null)
            {
                CurrentVehicle.EjectOccupant(silent: true);
            }
            Vector3 pos = MissionManager.Instance != null
                ? MissionManager.Instance.GetCheckpointPosition()
                : Player.transform.position;
            Player.Teleport(pos, restoreHealth: true);
            yield return new WaitForSecondsRealtime(0.2f);
            if (fade != null) yield return fade.FadeIn(0.4f);
            SetState(GameState.Playing);
        }

        private void SpawnPlayerAtSpawnPoint()
        {
            if (playerPrefab == null)
            {
                // fallback: scene may carry the prefab reference only in editor data
                playerPrefab = Resources.Load<GameObject>("Prefabs/Player");
                if (playerPrefab == null)
                {
                    Debug.LogError("[GameManager] playerPrefab not assigned!");
                    return;
                }
            }
            var spawn = GameObject.FindWithTag("PlayerSpawn");
            Vector3 pos = spawn != null ? spawn.transform.position : new Vector3(0f, 1f, 0f);
            var go = Instantiate(playerPrefab, pos, Quaternion.identity);
            SetPlayer(go.GetComponent<PlayerController>());
        }

        private void ApplySaveToWorld(SaveData data)
        {
            if (Player == null) return;
            Player.ApplySaveData(data);
            MissionManager.Instance?.ApplySaveData(data);
            Systems.WorldClock.Instance?.ApplySaveData(data);
            Systems.WeatherSystem.Instance?.ApplySaveData(data);
            Systems.EconomyManager.Instance?.ApplySaveData(data);
            Systems.AchievementSystem.Instance?.ApplySaveData(data);
            Systems.CollectibleManager.Instance?.ApplySaveData(data);
            Systems.FastTravelSystem.Instance?.ApplySaveData(data);
            Systems.DailyChallengeSystem.Instance?.ApplySaveData(data);
            UI.UIManager.Instance?.ShowNotification(
                "Welcome back, Rin", "Progress loaded from your save.", 3f);
        }

        /// <summary>Cheapest possible null-safe quit.</summary>
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
