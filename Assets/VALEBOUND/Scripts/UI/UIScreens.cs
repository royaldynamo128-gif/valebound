using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valebound.UI
{
    using Valebound.Systems;
    /// <summary>Full-world map screen: baked map texture, POI pins, fast travel.</summary>
    public class WorldMapScreen : MonoBehaviour
    {
        [SerializeField] private RawImage mapImage;
        [SerializeField] private RectTransform pinContainer;
        [SerializeField] private GameObject pinPrefab;
        [SerializeField] private GameObject fastTravelPanel;
        [SerializeField] private Transform fastTravelList;
        [SerializeField] private GameObject fastTravelButtonPrefab;
        [SerializeField] private float worldSize = 6144f;   // WorldBuilder.WorldSize - keep in sync
        [SerializeField] private Color pinColor = new Color(1f, 0.82f, 0.25f, 0.95f);

        private readonly List<GameObject> pins = new List<GameObject>();

        private void OnEnable() => Refresh();

        public void Refresh()
        {
            foreach (var p in pins) Destroy(p);
            pins.Clear();

            var mm = Systems.WorldMapSystem.Instance;
            if (mm == null || mm.AllPOIs == null || pinPrefab == null || pinContainer == null) return;

            foreach (var poi in mm.AllPOIs)
            {
                if (!poi.discovered && poi.type != POIType.CityCenter) continue;
                var pin = Instantiate(pinPrefab, pinContainer);
                pin.name = $"Pin_{poi.poiId}";
                var rt = (RectTransform)pin.transform;
                Vector2 uv = new Vector2(
                    (poi.worldPos.x + worldSize * 0.5f) / worldSize,
                    (poi.worldPos.z + worldSize * 0.5f) / worldSize);
                rt.anchorMin = rt.anchorMax = uv;
                rt.anchoredPosition = Vector2.zero;
                var label = pin.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = poi.displayName;
                pin.SetActive(true);
                pins.Add(pin);
            }

            // mission target pin
            var obj = MissionManager.Instance != null ? MissionManager.Instance.CurrentObjective : null;
            if (obj != null && pinPrefab != null)
            {
                var pin = Instantiate(pinPrefab, pinContainer);
                pin.name = "Pin_Mission";
                var rt = (RectTransform)pin.transform;
                Vector2 uv = new Vector2(
                    (obj.targetPos.x + worldSize * 0.5f) / worldSize,
                    (obj.targetPos.z + worldSize * 0.5f) / worldSize);
                rt.anchorMin = rt.anchorMax = uv;
                rt.anchoredPosition = Vector2.zero;
                var label = pin.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = "[!] Mission";
                pins.Add(pin);
            }
        }

        /// <summary>Wired to Fast Travel mode button: lists unlocked stops.</summary>
        public void OpenFastTravel()
        {
            if (fastTravelPanel == null) return;
            fastTravelPanel.SetActive(true);
            foreach (Transform c in fastTravelList) Destroy(c.gameObject);

            var ft = Systems.FastTravelSystem.Instance;
            var mm = Systems.WorldMapSystem.Instance;
            if (ft == null || mm == null) return;

            foreach (var poi in mm.GetByType(POIType.FastTravel))
            {
                if (!ft.IsUnlocked(poi.poiId)) continue;
                var btn = Instantiate(fastTravelButtonPrefab, fastTravelList);
                var t = btn.GetComponentInChildren<TMP_Text>();
                var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
                if (t != null)
                {
                    string price = player != null ? $"{ft.Price(player.transform.position, poi.worldPos)} cr" : "";
                    t.text = $"{poi.displayName}  ({price})";
                }
                btn.GetComponent<Button>()?.onClick.AddListener(() =>
                {
                    ft.Travel(poi.poiId);
                    fastTravelPanel.SetActive(false);
                    UIManager.Instance.ToggleMap();
                });
            }
        }

        public void CloseFastTravel()
        {
            if (fastTravelPanel != null) fastTravelPanel.SetActive(false);
        }
    }

    /// <summary>Mission board: offered/active missions with accept & abandon.</summary>
    public class MissionBoardScreen : MonoBehaviour
    {
        /// <summary>Editor SceneBuilder injects the scroll content rect here.</summary>
        public void SetListContainer(RectTransform rt) => listContainer = rt;
        public static MissionDef PendingAccept;

        [SerializeField] private Transform listContainer;
        [SerializeField] private GameObject missionRowPrefab;

        private void OnEnable() => Refresh();

        public void Refresh()
        {
            if (listContainer == null) return;
            foreach (Transform c in listContainer) Destroy(c.gameObject);
            var mm = MissionManager.Instance;
            if (mm == null) return;

            foreach (var m in mm.ActiveMissions) AddRow(m, "Active", abandon: true);
            foreach (var m in mm.OfferedMissions) AddRow(m, "Offered", accept: true);
        }

        private void AddRow(MissionDef m, string tag, bool accept = false, bool abandon = false)
        {
            if (missionRowPrefab == null || listContainer == null) return;
            var row = Instantiate(missionRowPrefab, listContainer);
            var texts = row.GetComponentsInChildren<TMP_Text>();
            if (texts.Length > 0) texts[0].text = $"{m.title}  [{tag}]";
            if (texts.Length > 1) texts[1].text = m.isMainStory ? "Main Story" : "Side Job";
            var buttons = row.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                string name = b.gameObject.name;
                if (name.Contains("Accept") && accept)
                {
                    b.gameObject.SetActive(true);
                    b.onClick.AddListener(() => { MissionManager.Instance.AcceptMission(m); Refresh(); });
                }
                else if (name.Contains("Abandon") && abandon)
                {
                    b.gameObject.SetActive(true);
                    b.onClick.AddListener(() => { MissionManager.Instance.AbandonMission(m); Refresh(); });
                }
                else if (name.Contains("Focus") && abandon)
                {
                    b.gameObject.SetActive(true);
                    b.onClick.AddListener(() => { MissionManager.Instance.SetFocus(m); Refresh(); });
                }
                else b.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>Garage screen: owned/locked vehicles with buy buttons + paint swatches.</summary>
    public class GarageScreen : MonoBehaviour
    {
        /// <summary>Editor SceneBuilder injects the scroll content rect here.</summary>
        public void SetListContainer(RectTransform rt) => listContainer = rt;
        [SerializeField] private Transform listContainer;
        [SerializeField] private GameObject rowPrefab;

        private void OnEnable() => Refresh();

        public void Refresh()
        {
            if (listContainer == null || rowPrefab == null) return;
            foreach (Transform c in listContainer) Destroy(c.gameObject);
            var garage = Systems.VehicleGarage.Instance;
            if (garage == null || garage.Entries == null) return;

            foreach (var e in garage.Entries)
            {
                var row = Instantiate(rowPrefab, listContainer);
                var texts = row.GetComponentsInChildren<TMP_Text>();
                string status = e.owned ? "Owned" : e.unlocked ? $"{e.def.price:N0} cr" : "Locked";
                if (texts.Length > 0) texts[0].text = e.def.displayName;
                if (texts.Length > 1) texts[1].text = status;
                var buy = row.transform.Find("BuyButton");
                if (buy != null)
                {
                    buy.gameObject.SetActive(e.unlocked && !e.owned);
                    buy.GetComponent<Button>()?.onClick.AddListener(() =>
                    {
                        garage.TryBuy(e.vehicleId, out _);
                        Refresh();
                    });
                }
                BuildPaintSwatches(row.transform, e, garage);
            }
        }

        /// <summary>Row of small color dots for owned vehicles; tap to repaint.</summary>
        private void BuildPaintSwatches(Transform row, Systems.VehicleGarage.GarageEntry e, Systems.VehicleGarage garage)
        {
            if (!e.owned) return;
            var paintsRoot = new GameObject("Paints", typeof(RectTransform));
            var rt = (RectTransform)paintsRoot.transform;
            rt.SetParent(row, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-10f, -34f);
            rt.sizeDelta = new Vector2(560f, 56f);

            var palette = Vehicle.VehicleSkin.EditorPalette;
            for (int i = 0; i < palette.Length; i++)
            {
                int idx = i;
                var dotGo = new GameObject("Paint_" + i, typeof(Image));
                var drt = (RectTransform)dotGo.transform;
                drt.SetParent(rt, false);
                drt.anchorMin = drt.anchorMax = new Vector2((i + 0.5f) / palette.Length, 0.5f);
                drt.sizeDelta = new Vector2(34f, 34f);
                var img = dotGo.GetComponent<Image>();
                img.color = palette[i].c;
                img.raycastTarget = true;
                var btn = dotGo.AddComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    garage.TryPaint(e.vehicleId, $"paint_{idx}", out string msg);
                    UI.UIManager.Instance?.ShowNotification("Paint Shop", msg, 2.5f);
                });
            }
        }
    }

    /// <summary>Daily challenges panel (shown inside the pause menu).</summary>
    public class DailyChallengeScreen : MonoBehaviour
    {
        /// <summary>Editor SceneBuilder injects the container here.</summary>
        public void SetListContainer(RectTransform rt) => listContainer = rt;
        [SerializeField] private Transform listContainer;

        private void OnEnable() => Refresh();

        public void Refresh()
        {
            if (listContainer == null) return;
            foreach (Transform c in listContainer) Destroy(c.gameObject);
            var dc = Systems.DailyChallengeSystem.Instance;
            if (dc == null || dc.Today == null) return;
            foreach (var c in dc.Today)
            {
                var row = new GameObject("Challenge", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                row.transform.SetParent(listContainer, false);
                var rrt = (RectTransform)row.transform;
                rrt.sizeDelta = new Vector2(0f, 54f);   // VLG stretches width; fixed row height
                var tmp = row.GetComponent<TMPro.TextMeshProUGUI>();
                string mark = c.claimed ? "[OK]" : "-";   // TMP-default-font-safe glyphs
                tmp.text = $"{mark} {c.title}: {c.description} ({c.progress}/{c.target})";
                tmp.fontSize = 24;
                tmp.enableWordWrapping = true;
                tmp.color = c.claimed ? new Color(0.55f, 0.9f, 0.6f) : Color.white;
            }
        }
    }

    /// <summary>Achievements list screen.</summary>
    public class AchievementsScreen : MonoBehaviour
    {
        /// <summary>Editor SceneBuilder injects the scroll content rect here.</summary>
        public void SetListContainer(RectTransform rt) => listContainer = rt;
        [SerializeField] private Transform listContainer;
        [SerializeField] private GameObject rowPrefab;

        private void OnEnable() => Refresh();

        public void Refresh()
        {
            if (listContainer == null || rowPrefab == null) return;
            foreach (Transform c in listContainer) Destroy(c.gameObject);
            var ach = Systems.AchievementSystem.Instance;
            if (ach == null || ach.Achievements == null) return;
            foreach (var a in ach.Achievements)
            {
                var row = Instantiate(rowPrefab, listContainer);
                var texts = row.GetComponentsInChildren<TMP_Text>();
                if (texts.Length > 0) texts[0].text = (a.unlocked ? "[OK] " : "[..] ") + a.title;
                if (texts.Length > 1) texts[1].text = a.description;
            }
        }
    }

    /// <summary>Settings screen: sliders & toggles writing GameSettings.</summary>
    public class SettingsScreen : MonoBehaviour
    {
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Slider ambienceSlider;
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private Toggle invertYToggle;
        [SerializeField] private Toggle leftHandedToggle;
        [SerializeField] private Toggle showFpsToggle;
        [SerializeField] private TMP_Dropdown graphicsDropdown;

        private GameSettings settings;

        private void OnEnable()
        {
            settings = SaveSystem.LoadSettings();
            if (musicSlider != null) { musicSlider.value = settings.musicVolume; musicSlider.onValueChanged.AddListener(v => { settings.musicVolume = v; Systems.AudioManager.Instance?.SetMusicVolume(v); }); }
            if (sfxSlider != null) { sfxSlider.value = settings.sfxVolume; sfxSlider.onValueChanged.AddListener(v => settings.sfxVolume = v); }
            if (ambienceSlider != null) { ambienceSlider.value = settings.ambienceVolume; ambienceSlider.onValueChanged.AddListener(v => settings.ambienceVolume = v); }
            if (sensitivitySlider != null) { sensitivitySlider.value = settings.cameraSensitivity; sensitivitySlider.onValueChanged.AddListener(v => settings.cameraSensitivity = v); }
            if (invertYToggle != null) { invertYToggle.isOn = settings.invertCameraY; invertYToggle.onValueChanged.AddListener(v => settings.invertCameraY = v); }
            if (leftHandedToggle != null) { leftHandedToggle.isOn = settings.leftHanded; leftHandedToggle.onValueChanged.AddListener(v => settings.leftHanded = v); }
            if (showFpsToggle != null) { showFpsToggle.isOn = settings.showFps; showFpsToggle.onValueChanged.AddListener(v => settings.showFps = v); }
            if (graphicsDropdown != null)
            {
                graphicsDropdown.value = settings.graphicsTier;
                graphicsDropdown.onValueChanged.AddListener(i => { settings.graphicsTier = i; SaveSystem.SaveSettings(settings); });
            }
        }

        private void OnDisable()
        {
            if (settings != null) SaveSystem.SaveSettings(settings);
        }
    }

    /// <summary>Main menu: New Game / Continue / Settings / Quit.</summary>
    public class MainMenuScreen : MonoBehaviour
    {
        /// <summary>Editor SceneBuilder wires the Continue button reference here.</summary>
        public void SetContinueButton(GameObject go) => continueButton = go;

        [SerializeField] private GameObject continueButton; // disabled when no save
        [SerializeField] private TMP_Text versionText;

        private void Start()
        {
            if (continueButton != null)
                continueButton.SetActive(SaveSystem.AutosaveExists() || SaveSystem.SlotExists(0));
            if (versionText != null) versionText.text = Application.version;
        }

        public void OnNewGame() => GameManager.Instance?.StartNewGame();
        public void OnContinue() => GameManager.Instance?.ContinueGame();
        public void OnSettings() => UIManager.Instance?.ShowSettings(true);
        public void OnQuit() => GameManager.Instance?.QuitGame();
    }

    /// <summary>Pause menu buttons.</summary>
    public class PauseMenuScreen : MonoBehaviour
    {
        public void OnResume() => UIManager.Instance?.TogglePause();
        public void OnSave()
        {
            var mm = MissionManager.Instance;
            if (mm != null) { SaveSystem.SaveAutosave(mm.CaptureCurrentState()); }
            UI.UIManager.Instance?.ShowNotification("Saved", "Progress saved.", 2f);
        }
        public void OnSettings() => UIManager.Instance?.ShowSettings(true);
        public void OnMap() => UIManager.Instance?.ToggleMap();
        public void OnMissions() => UIManager.Instance?.ToggleMissionBoard();
        public void OnGarage() => UIManager.Instance?.ToggleGarage();
        public void OnAchievements() => UIManager.Instance?.ToggleAchievements();
        public void OnMainMenu()
        {
            var mm = MissionManager.Instance;
            if (mm != null) SaveSystem.SaveAutosave(mm.CaptureCurrentState());
            GameManager.Instance?.ReturnToMainMenu();
        }
    }

    /// <summary>Game over screen buttons.</summary>
    public class GameOverScreen : MonoBehaviour
    {
        public void OnRespawn() => GameManager.Instance?.RespawnPlayer();
        public void OnQuit() => GameManager.Instance?.ReturnToMainMenu();
    }

    /// <summary>Loading screen with rotating tips.</summary>
    public class LoadingScreen : MonoBehaviour
    {
        [SerializeField] private TMP_Text tipText;
        [SerializeField] private float tipInterval = 4f;

        private static readonly string[] Tips =
        {
            "Hold SPRINT to run - watch your stamina bar.",
            "Tap the map button anytime to check Aurora Valley.",
            "Signal shards glow blue. 60 are hidden across the valley.",
            "Fast travel unlocks at level 3 - visit guild offices.",
            "Rain reduces grip. Brake earlier.",
            "The Bounders garage gets new stock as the story continues.",
            "Traffic is lighter at night - good for courier records.",
            "Tap the radio button for music while you drive.",
            "Photo vistas give bonus XP on first visit.",
            "Cass's races are replayable - beat your best time.",
        };

        private float timer;
        private int tipIndex;

        private void OnEnable()
        {
            tipIndex = UnityEngine.Random.Range(0, Tips.Length);
            ShowTip();
            timer = 0f;
        }

        private void Update()
        {
            timer += Time.unscaledDeltaTime;
            if (timer >= tipInterval)
            {
                timer = 0f;
                tipIndex = (tipIndex + 1) % Tips.Length;
                ShowTip();
            }
        }

        private void ShowTip()
        {
            if (tipText != null) tipText.text = Tips[tipIndex];
        }
    }
}
