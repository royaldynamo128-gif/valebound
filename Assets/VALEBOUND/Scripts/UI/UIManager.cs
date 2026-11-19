using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Valebound.UI
{
    /// <summary>
    /// UI hub: owns overlay screens (menus, HUD, dialogue, map, notifications).
    /// Built as a prefab by the editor SceneBuilder; this script drives it.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Screens")]
        [SerializeField] private GameObject mainMenuRoot;
        [SerializeField] private GameObject hudRoot;
        [SerializeField] private GameObject pauseRoot;
        [SerializeField] private GameObject settingsRoot;
        [SerializeField] private GameObject mapRoot;
        [SerializeField] private GameObject missionBoardRoot;
        [SerializeField] private GameObject garageRoot;
        [SerializeField] private GameObject achievementsRoot;
        [SerializeField] private GameObject gameOverRoot;
        [SerializeField] private GameObject loadingRoot;
        [SerializeField] private GameObject dialogueRoot;

        [Header("HUD elements")]
        [SerializeField] private TMPro.TextMeshProUGUI creditsText;
        [SerializeField] private TMPro.TextMeshProUGUI clockText;
        [SerializeField] private TMPro.TextMeshProUGUI weatherText;
        [SerializeField] private TMPro.TextMeshProUGUI missionObjectiveText;
        [SerializeField] private Slider healthSlider;
        [SerializeField] private Slider staminaSlider;
        [SerializeField] private GameObject vehiclePanel;
        [SerializeField] private TMPro.TextMeshProUGUI speedText;
        [SerializeField] private GameObject interactionPrompt;
        [SerializeField] private TMPro.TextMeshProUGUI interactionText;

        [Header("Notifications")]
        [SerializeField] private RectTransform notificationContainer;
        [SerializeField] private GameObject notificationPrefab;

        [Header("Dialogue")]
        [SerializeField] private TMPro.TextMeshProUGUI dialogueSpeaker;
        [SerializeField] private TMPro.TextMeshProUGUI dialogueText;
        [SerializeField] private GameObject dialogueNextButton;
        [SerializeField] private GameObject dialogueAcceptButton;

        [Header("Mission banner")]
        [SerializeField] private GameObject missionBanner;
        [SerializeField] private TMPro.TextMeshProUGUI bannerTitle;
        [SerializeField] private TMPro.TextMeshProUGUI bannerBody;

        [Header("Fade")]
        [SerializeField] internal Image fadeOverlayImage;

        public ScreenFadeOverlay FadeOverlay { get; private set; }
        public bool IsDialogueOpen { get; private set; }

        private readonly Queue<Notification> pending = new Queue<Notification>();
        private Coroutine notifRoutine;
        private DialogueLine[] currentDialogue;
        private int dialogueIndex;
        private Action onDialogueEnd;

        [Serializable]
        public class DialogueLine
        {
            public string speaker;
            public string text;
        }

        private class Notification
        {
            public string title, body;
            public float duration;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            FadeOverlay = gameObject.AddComponent<ScreenFadeOverlay>();
            if (fadeOverlayImage != null)
            {
                var c = fadeOverlayImage.color; c.a = 0f; fadeOverlayImage.color = c;
                fadeOverlayImage.raycastTarget = false;
            }
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.OnGameStateChanged += HandleGameStateChanged;
                HandleGameStateChanged(gm.State);
            }
            GameEvents.CreditsChanged += c => { if (creditsText != null) creditsText.text = $"{c:N0} cr"; };
            GameEvents.MissionObjectiveChanged += d => { if (missionObjectiveText != null) missionObjectiveText.text = d; };
            HideAllScreens();
            ShowMainMenu(true);
        }

        // ---------------- screens ----------------

        private void HideAllScreens()
        {
            SetActive(mainMenuRoot, false);
            SetActive(pauseRoot, false);
            SetActive(settingsRoot, false);
            SetActive(mapRoot, false);
            SetActive(missionBoardRoot, false);
            SetActive(garageRoot, false);
            SetActive(achievementsRoot, false);
            SetActive(gameOverRoot, false);
            SetActive(loadingRoot, false);
            SetActive(dialogueRoot, false);
            SetActive(hudRoot, false);
        }

        private void SetActive(GameObject go, bool on) { if (go != null) go.SetActive(on); }

        /// <summary>Closes every modal overlay (map/missions/garage/achievements/settings
        /// + their sub-panels). Called before opening another so screens never stack.</summary>
        private void CloseAllOverlays()
        {
            SetActive(mapRoot, false);
            SetActive(missionBoardRoot, false);
            SetActive(garageRoot, false);
            SetActive(achievementsRoot, false);
            SetActive(settingsRoot, false);
            var map = mapRoot != null ? mapRoot.GetComponent<WorldMapScreen>() : null;
            map?.CloseFastTravel();
        }

        private void HandleGameStateChanged(GameManager.GameState state)
        {
            SetActive(loadingRoot, state == GameManager.GameState.Loading);
            SetActive(hudRoot, state == GameManager.GameState.Playing || state == GameManager.GameState.Paused);
            SetActive(pauseRoot, state == GameManager.GameState.Paused);
            SetActive(gameOverRoot, state == GameManager.GameState.GameOver);
            if (state == GameManager.GameState.MainMenu) ShowMainMenu(true);
        }

        public void ShowMainMenu(bool on)
        {
            SetActive(mainMenuRoot, on);
        }

        public void TogglePause()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            if (gm.State == GameManager.GameState.Playing) gm.PauseGame();
            else if (gm.State == GameManager.GameState.Paused) gm.ResumeGame();
        }

        public void ToggleMap()
        {
            if (mapRoot == null) return;
            bool show = !mapRoot.activeSelf;
            CloseAllOverlays();   // never stack modal screens
            SetActive(mapRoot, show);
            if (show) mapRoot.GetComponent<WorldMapScreen>()?.Refresh();
        }

        public void ToggleMissionBoard()
        {
            if (missionBoardRoot == null) return;
            bool show = !missionBoardRoot.activeSelf;
            CloseAllOverlays();
            SetActive(missionBoardRoot, show);
            if (show) missionBoardRoot.GetComponent<MissionBoardScreen>()?.Refresh();
        }

        public void ToggleGarage()
        {
            if (garageRoot == null) return;
            bool show = !garageRoot.activeSelf;
            CloseAllOverlays();
            SetActive(garageRoot, show);
            if (show) garageRoot.GetComponent<GarageScreen>()?.Refresh();
        }

        public void ToggleAchievements()
        {
            if (achievementsRoot == null) return;
            bool show = !achievementsRoot.activeSelf;
            CloseAllOverlays();
            SetActive(achievementsRoot, show);
            if (show) achievementsRoot.GetComponent<AchievementsScreen>()?.Refresh();
        }

        private void achievementsvementsRootRefresh()
        {
            achievementsRoot.GetComponent<AchievementsScreen>()?.Refresh();
        }

        public void ShowSettings(bool on) => SetActive(settingsRoot, on);

        public void ShowLoading(bool on) => SetActive(loadingRoot, on);

        // ---------------- HUD updates ----------------

        public void SetHealth(float pct) { if (healthSlider != null) healthSlider.value = pct; }
        public void SetStamina(float pct) { if (staminaSlider != null) staminaSlider.value = pct; }

        public void SetClock(string text) { if (clockText != null) clockText.text = text; }
        public void SetWeather(string text) { if (weatherText != null) weatherText.text = text; }

        public void SetVehicleHud(bool on, float speedKph = 0f)
        {
            SetActive(vehiclePanel, on);
            if (on && speedText != null) speedText.text = $"{Mathf.RoundToInt(speedKph)} km/h";
        }

        public void UpdateSpeed(float speedKph)
        {
            if (vehiclePanel != null && vehiclePanel.activeSelf && speedText != null)
                speedText.text = $"{Mathf.RoundToInt(speedKph)} km/h";
        }

        public void SetInteractionPrompt(string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            SetActive(interactionPrompt, show);
            if (show && interactionText != null) interactionText.text = text;
        }

        // ---------------- notifications ----------------

        public void ShowNotification(string title, string body, float duration = 3f)
        {
            pending.Enqueue(new Notification { title = title, body = body, duration = duration });
            if (notifRoutine == null) notifRoutine = StartCoroutine(NotifLoop());
        }

        private IEnumerator NotifLoop()
        {
            while (pending.Count > 0)
            {
                var n = pending.Dequeue();
                if (notificationPrefab != null && notificationContainer != null)
                {
                    var go = Instantiate(notificationPrefab, notificationContainer);
                    var t = go.GetComponent<NotificationToast>() ?? go.AddComponent<NotificationToast>();
                    t.Show(n.title, n.body, n.duration);
                    yield return new WaitForSeconds(n.duration + 0.4f);
                    Destroy(go);
                }
                else
                {
                    Debug.Log($"[Notif] {n.title}: {n.body}");
                    yield return new WaitForSeconds(0.1f);
                }
            }
            notifRoutine = null;
        }

        // ---------------- mission banner ----------------

        public void ShowMissionBanner(string title, string body)
        {
            if (missionBanner == null) return;
            StartCoroutine(BannerRoutine(title, body));
        }

        private IEnumerator BannerRoutine(string title, string body)
        {
            if (bannerTitle != null) bannerTitle.text = title;
            if (bannerBody != null) bannerBody.text = body;
            missionBanner.SetActive(true);
            yield return new WaitForSeconds(3.6f);
            missionBanner.SetActive(false);
        }

        // ---------------- dialogue ----------------

        public void ShowDialogue(string speaker, NPC.MissionGiverNPC.DialogueLine[] lines, Action onEnd)
        {
            if (dialogueRoot == null || lines == null || lines.Length == 0) { onEnd?.Invoke(); return; }
            currentDialogue = new DialogueLine[lines.Length];
            for (int i = 0; i < lines.Length; i++)
                currentDialogue[i] = new DialogueLine { speaker = lines[i].speaker, text = lines[i].text };
            dialogueIndex = 0;
            onDialogueEnd = onEnd;
            IsDialogueOpen = true;
            SetActive(dialogueRoot, true);
            ShowDialogueLine(0);
            GameManager.Instance?.PauseGame();
        }

        private void ShowDialogueLine(int index)
        {
            if (currentDialogue == null || index < 0 || index >= currentDialogue.Length)
            {
                EndDialogue();
                return;
            }
            dialogueIndex = index;
            if (dialogueSpeaker != null) dialogueSpeaker.text = currentDialogue[index].speaker;
            if (dialogueText != null) dialogueText.text = currentDialogue[index].text;
            bool last = index == currentDialogue.Length - 1;
            if (dialogueNextButton != null) dialogueNextButton.SetActive(!last);
            if (dialogueAcceptButton != null) dialogueAcceptButton.SetActive(last);
        }

        /// <summary>Wired to Next button.</summary>
        public void OnDialogueNext() => ShowDialogueLine(dialogueIndex + 1);

        /// <summary>Wired to Accept/Close button: closes and fires callback.</summary>
        public void OnDialogueAccept()
        {
            EndDialogue();
        }

        private void EndDialogue()
        {
            IsDialogueOpen = false;
            SetActive(dialogueRoot, false);
            GameManager.Instance?.ResumeGame();
            onDialogueEnd?.Invoke();
            onDialogueEnd = null;
        }

        // ---------------- mission offer panel ----------------

        public void ShowMissionOfferPanel(MissionDef def, Action accept)
        {
            ShowNotification("Mission Offered", def.title, 4f);
            MissionBoardScreen.PendingAccept = def;
            accept?.Invoke(); // simplified: auto-accept, board shows details
        }

        // ---------------- barks ----------------

        public void ShowBark(Transform source, string text)
        {
            ShowNotification(source.name, text, 1.8f);
        }

        // ---------------- fade ----------------

    }


    /// <summary>Fullscreen fade helper owned by UIManager.</summary>
    public class ScreenFadeOverlay : MonoBehaviour
    {
        private Image img;
        private Image Img
        {
            get
            {
                if (img == null && UIManager.Instance != null)
                    img = UIManager.Instance.fadeOverlayImage;
                return img;
            }
        }

        public IEnumerator FadeOut(float duration)
        {
            return Fade(1f, duration);
        }

        public IEnumerator FadeIn(float duration)
        {
            return Fade(0f, duration);
        }

        private IEnumerator Fade(float targetAlpha, float duration)
        {
            Image image = Img;
            if (image == null) yield break;
            float start = image.color.a;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Lerp(start, targetAlpha, t / duration);
                image.color = new Color(0f, 0f, 0f, a);
                yield return null;
            }
            image.color = new Color(0f, 0f, 0f, targetAlpha);
        }
    }
}
