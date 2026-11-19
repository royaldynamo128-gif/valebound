#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;
using Valebound.UI;
using Valebound.Systems;

namespace Valebound.EditorTools
{
    /// <summary>
    /// Final assembly step: creates the Main scene with world + UI canvas +
    /// wired systems, the Boot scene, and quality/URP settings.
    /// </summary>
    public static class SceneBuilder
    {
        private const string SceneDir = "Assets/VALEBOUND/Scenes";

        [MenuItem("VALEBOUND/Build Scenes")]
        public static void BuildAll()
        {
            if (!Directory.Exists(SceneDir)) Directory.CreateDirectory(SceneDir);
            BuildMainScene();
            BuildBootScene();
            AddScenesToBuildSettings();
            Debug.Log("[SceneBuilder] scenes built.");
        }

        private static void BuildMainScene()
        {
            // If a Main scene with a generated WORLD is already open, keep it and just
            // add UI/spawn/lighting into it. Otherwise create a fresh scene AND generate
            // the world in-place (so Build Scenes can never wipe a generated world).
            var scene = SceneManager.GetActiveScene();
            bool hasWorld = GameObject.Find("WORLD") != null;
            if (!hasWorld && (string.IsNullOrEmpty(scene.path) || scene.path != $"{SceneDir}/Main.unity"))
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            if (GameObject.Find("WORLD") == null)
            {
                Debug.Log("[SceneBuilder] no WORLD found - generating world in-place now...");
                var wb = EditorWindow.GetWindow<WorldBuilder>();
                wb.GenerateFromBuildScript();
            }

            // ---- lighting (only if missing) ----
            if (GameObject.Find("Directional Light") == null)
            {
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.shadows = LightShadows.Soft;
                light.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            // ---- UI canvas ----
            BuildUICanvas();

            // ---- player spawn (only if missing; placed on the road in front of HQ) ----
            if (GameObject.Find("PlayerSpawn") == null)
            {
                var spawn = new GameObject("PlayerSpawn");
                spawn.tag = "PlayerSpawn";
                spawn.transform.position = new Vector3(-72f, 1.2f, 700f); // street intersection one block west of guild HQ
            }

            EditorSceneManager.SaveScene(scene, $"{SceneDir}/Main.unity");
        }

        private static void BuildBootScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bootGo = new GameObject("Boot");
            bootGo.AddComponent<Valebound.Core.BootLoader>();
            var camGo = new GameObject("BootCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            camGo.AddComponent<AudioListener>();
            EditorSceneManager.SaveScene(scene, $"{SceneDir}/Boot.unity");
        }

        private static void AddScenesToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            void Add(string path)
            {
                if (!scenes.Any(s => s.path == path))
                    scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            Add($"{SceneDir}/Boot.unity");
            Add($"{SceneDir}/Main.unity");
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ================= UI canvas =================

        private static void BuildUICanvas()
        {
            // EventSystem
            if (UnityEngine.Object.FindObjectOfType<EventSystem>(true) == null)
            {
                var es = new GameObject("EventSystem",
                    typeof(EventSystem), typeof(StandaloneInputModule));
            }

            var canvasGo = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f; // aspect-safe
            canvas.sortingOrder = 10;

            var uiMgr = canvasGo.AddComponent<UI.UIManager>();

            // ---------- helper factories ----------
            GameObject Panel(string name, Transform parent, Color? color = null,
                Vector2? anchorMin = null, Vector2? anchorMax = null, Vector2? offsetMin = null, Vector2? offsetMax = null)
            {
                var go = new GameObject(name, typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                rt.anchorMin = anchorMin ?? Vector2.zero;
                rt.anchorMax = anchorMax ?? Vector2.one;
                rt.offsetMin = offsetMin ?? Vector2.zero;
                rt.offsetMax = offsetMax ?? Vector2.zero;
                var img = go.GetComponent<Image>();
                img.color = color ?? new Color(0, 0, 0, 0.55f);
                img.raycastTarget = color.HasValue;
                return go;
            }

            TextAlignmentOptions ToTmpAlignment(TextAnchor a)
            {
                switch (a)
                {
                    case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                    case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                    case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                    case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                    case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                    case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                    case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                    case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                    case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                    default: return TextAlignmentOptions.Center;
                }
            }

            GameObject Label(string name, Transform parent, string text, float size,
                TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null)
            {
                var go = new GameObject(name, typeof(TextMeshProUGUI));
                go.transform.SetParent(parent, false);
                var tmp = go.GetComponent<TextMeshProUGUI>();
                tmp.text = text;
                tmp.fontSize = size;
                tmp.alignment = ToTmpAlignment(anchor);
                tmp.color = color ?? Color.white;
                tmp.raycastTarget = false;
                return go;
            }

            GameObject Button(string name, Transform parent, string text, Vector2 anchor, Vector2 size, System.Action onClick)
            {
                var go = new GameObject(name, typeof(Image), typeof(Button));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                rt.anchorMin = rt.anchorMax = anchor;
                rt.sizeDelta = size;
                rt.anchoredPosition = Vector2.zero;
                go.GetComponent<Image>().color = new Color(0.16f, 0.5f, 0.85f, 0.95f);
                var btn = go.GetComponent<Button>();
                btn.onClick.AddListener(() => onClick?.Invoke());
                var lbl = Label("Text", go.transform, text, 34, TextAnchor.MiddleCenter);
                ((RectTransform)lbl.transform).sizeDelta = size;
                lbl.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
                return go;
            }

            // ---------- main menu ----------
            var menu = Panel("MainMenu", canvasGo.transform, new Color(0.04f, 0.07f, 0.12f, 0.96f));
            // single title (was created twice → ghost text)
            {
                var t = (RectTransform)Label("Title", menu.transform, "VALEBOUND", 120, TextAnchor.MiddleCenter).transform;
                t.anchorMin = t.anchorMax = new Vector2(0.5f, 0.78f);
                t.sizeDelta = new Vector2(900f, 160f);
            }
            var menuScreen = menu.AddComponent<UI.MainMenuScreen>();
            Button("NewGame", menu.transform, "New Game", new Vector2(0.5f, 0.55f), new Vector2(420f, 90f),
                () => GameManager.Instance?.StartNewGame());
            var contBtn = Button("Continue", menu.transform, "Continue", new Vector2(0.5f, 0.44f), new Vector2(420f, 90f),
                () => GameManager.Instance?.ContinueGame());
            menuScreen.SetContinueButton(contBtn);
            Button("Quit", menu.transform, "Quit", new Vector2(0.5f, 0.22f), new Vector2(420f, 90f),
                () => GameManager.Instance?.QuitGame());

            // ---------- HUD ----------
            var hud = Panel("HUD", canvasGo.transform, null); // fully transparent overlay
            hud.GetComponent<Image>().raycastTarget = false;
            var hudCtl = hud.AddComponent<UI.HUDController>();

            // top-left: credits & clock
            var topLeft = Panel("Stats", hud.transform, null,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -140f), new Vector2(420f, -16f));
            topLeft.GetComponent<Image>().raycastTarget = false;
            Label("Credits", topLeft.transform, "0 cr", 38);
            Label("Clock", topLeft.transform, "08:30", 30).transform.Translate(0, -50f, 0);
            Label("Weather", topLeft.transform, "Clear", 26).transform.Translate(0, -92f, 0);

            // top-right: mission objective (below the MAP/pause row so nothing overlaps)
            var topRight = Panel("Objective", hud.transform, null,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-520f, -300f), new Vector2(-16f, -196f));
            topRight.GetComponent<Image>().raycastTarget = false;
            Label("MissionText", topRight.transform, "", 30, TextAnchor.UpperRight);

            // minimap top-right under objective? classic bottom-right; go bottom-right
            var minimapGo = new GameObject("Minimap", typeof(RawImage));
            var mmRt = (RectTransform)minimapGo.transform;
            mmRt.SetParent(hud.transform, false);
            mmRt.anchorMin = mmRt.anchorMax = new Vector2(1f, 1f);
            mmRt.pivot = new Vector2(1f, 1f);
            // below the MAP/pause row AND the objective text strip (both live in the top ~300px)
            mmRt.anchoredPosition = new Vector2(-16f, -320f);
            mmRt.sizeDelta = new Vector2(220f, 220f);
            var mmImg = minimapGo.GetComponent<RawImage>();
            mmImg.raycastTarget = false;
            var playerMarker = new GameObject("PlayerMarker", typeof(Image));
            var pmRt = (RectTransform)playerMarker.transform;
            pmRt.SetParent(mmRt, false);
            pmRt.sizeDelta = new Vector2(28f, 28f);
            pmRt.localPosition = Vector3.zero;
            playerMarker.GetComponent<Image>().color = new Color(0.2f, 0.75f, 1f);
            var missionMarker = new GameObject("MissionMarker", typeof(Image));
            var mtRt = (RectTransform)missionMarker.transform;
            mtRt.SetParent(mmRt, false);
            mtRt.sizeDelta = new Vector2(24f, 24f);
            missionMarker.GetComponent<Image>().color = new Color(1f, 0.8f, 0.2f);
            var minimap = minimapGo.AddComponent<UI.MinimapRenderer>();

            // health/stamina bars: clear of the joystick (joystick spans y 150..410 px on a 1080p phone
            // reference; bars sit above it, left-aligned, out of the thumb's reach)
            var bars = Panel("Bars", hud.transform, null,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 430f), new Vector2(320f, 520f));
            bars.GetComponent<Image>().raycastTarget = false;
            var health = MakeBar(bars.transform, "Health", new Color(0.85f, 0.25f, 0.25f), 0f);
            var stamina = MakeBar(bars.transform, "Stamina", new Color(0.25f, 0.8f, 0.4f), -46f);

            // interaction prompt center-bottom
            var prompt = Panel("InteractionPrompt", hud.transform, new Color(0, 0, 0, 0.6f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-180f, 330f), new Vector2(180f, 390f));
            Label("PromptText", prompt.transform, "Interact", 30, TextAnchor.MiddleCenter);

            // vehicle panel (speed) - hidden until a vehicle is entered (HUDController toggles)
            var vehicle = Panel("VehiclePanel", hud.transform, null,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-120f, 60f), new Vector2(120f, 130f));
            vehicle.GetComponent<Image>().raycastTarget = false;
            Label("Speed", vehicle.transform, "0 km/h", 40, TextAnchor.MiddleCenter);
            vehicle.SetActive(false);

            // ---------- foot controls ----------
            var foot = Panel("FootControls", hud.transform, null);
            foot.GetComponent<Image>().raycastTarget = false;
            BuildJoystick(foot.transform);
            var jump = MakeTouchButton(foot.transform, "JumpBtn", "JUMP", new Vector2(0.88f, 0.16f), UI.TapButton.TapAction.Jump);
            var sprint = MakeHoldButton(foot.transform, "SprintBtn", "SPRINT", new Vector2(0.88f, 0.34f), UI.HoldButton.HoldAction.Sprint);
            var interact = MakeTouchButton(foot.transform, "InteractBtn", "USE", new Vector2(0.7f, 0.16f), UI.TapButton.TapAction.Interact);
            var mapBtn = MakeTouchButton(foot.transform, "MapBtn", "MAP", new Vector2(0.86f, 0.9f), UI.TapButton.TapAction.Map);
            var pauseBtn = MakeTouchButton(foot.transform, "PauseBtn", "II", new Vector2(0.72f, 0.9f), UI.TapButton.TapAction.Pause);

            // ---------- vehicle controls ----------
            var veh = Panel("VehicleControls", hud.transform, null);
            veh.GetComponent<Image>().raycastTarget = false;
            veh.SetActive(false);
            BuildJoystick(veh.transform); // steering
            MakePedal(veh.transform, "ThrottlePedal", "GAS", new Vector2(0.9f, 0.2f), UI.VehiclePedal.Pedal.Throttle);
            MakePedal(veh.transform, "BrakePedal", "BRAKE", new Vector2(0.74f, 0.2f), UI.VehiclePedal.Pedal.Reverse);
            MakeTouchButton(veh.transform, "HornBtn", "HORN", new Vector2(0.9f, 0.42f), UI.TapButton.TapAction.Horn);
            MakeTouchButton(veh.transform, "ExitBtn", "EXIT", new Vector2(0.74f, 0.42f), UI.TapButton.TapAction.EnterExitVehicle);
            MakeTouchButton(veh.transform, "RadioBtn", "RADIO", new Vector2(0.62f, 0.42f), UI.TapButton.TapAction.Radio);
            MakeTouchButton(veh.transform, "LightsBtn", "LIGHTS", new Vector2(0.62f, 0.2f), UI.TapButton.TapAction.Headlights);
            var vehMapBtn = MakeTouchButton(veh.transform, "MapBtn", "MAP", new Vector2(0.86f, 0.9f), UI.TapButton.TapAction.Map);
            var vehPauseBtn = MakeTouchButton(veh.transform, "PauseBtn", "II", new Vector2(0.72f, 0.9f), UI.TapButton.TapAction.Pause);

            // ---------- dialogue ----------
            var dialogue = Panel("Dialogue", canvasGo.transform, new Color(0.02f, 0.03f, 0.06f, 0.88f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            {
                var rt = (RectTransform)dialogue.transform;
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 40f);
                rt.sizeDelta = new Vector2(1200f, 340f);
            }
            var speaker = Label("Speaker", dialogue.transform, "Mara Vex", 36);
            ((RectTransform)speaker.transform).anchoredPosition = new Vector2(30f, -20f);
            var dText = Label("Text", dialogue.transform, "...", 32);
            ((RectTransform)dText.transform).anchorMin = new Vector2(0f, 0f);
            ((RectTransform)dText.transform).anchorMax = new Vector2(1f, 1f);
            ((RectTransform)dText.transform).offsetMin = new Vector2(30f, 90f);
            ((RectTransform)dText.transform).offsetMax = new Vector2(-30f, -70f);
            var nextBtn = Button("NextBtn", dialogue.transform, "Next >>", new Vector2(0.92f, 0.12f), new Vector2(180f, 64f), null);
            nextBtn.GetComponent<Button>().onClick.AddListener(() => uiMgr.OnDialogueNext());
            var accBtn = Button("AcceptBtn", dialogue.transform, "Accept", new Vector2(0.75f, 0.12f), new Vector2(180f, 64f), null);
            accBtn.GetComponent<Button>().onClick.AddListener(() => uiMgr.OnDialogueAccept());
            dialogue.SetActive(false);

            // ---------- pause menu ----------
            var pause = Panel("PauseMenu", canvasGo.transform, new Color(0.03f, 0.05f, 0.09f, 0.92f));
            var pauseScreen = pause.AddComponent<UI.PauseMenuScreen>();
            // single title, properly anchored (was created twice → ghost "PAU/SED" text)
            {
                var t = (RectTransform)Label("Title", pause.transform, "PAUSED", 80, TextAnchor.UpperCenter).transform;
                t.anchorMin = t.anchorMax = new Vector2(0.5f, 0.92f);
                t.sizeDelta = new Vector2(600f, 100f);
            }
            // daily challenges strip (own top-left corner of the pause overlay,
            // sized to content: title + up to 4 rows - no longer a full-height column)
            var daily = Panel("DailyChallenges", pause.transform, new Color(0.08f, 0.12f, 0.18f, 0.85f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -330f), new Vector2(560f, -80f));
            Label("Title", daily.transform, "TODAY'S CHALLENGES", 30, TextAnchor.UpperLeft, new Color(1f, 0.84f, 0.3f));
            var dailyList = new GameObject("DailyList", typeof(RectTransform));
            var dlRt = (RectTransform)dailyList.transform;
            dlRt.SetParent(daily.transform, false);
            dlRt.anchorMin = new Vector2(0f, 0f); dlRt.anchorMax = new Vector2(1f, 1f);
            dlRt.offsetMin = new Vector2(12f, 12f); dlRt.offsetMax = new Vector2(-12f, -48f);
            var dVlg = dailyList.AddComponent<VerticalLayoutGroup>();
            dVlg.spacing = 8f; dVlg.childForceExpandHeight = false; dVlg.childForceExpandWidth = true;
            daily.AddComponent<UI.DailyChallengeScreen>().SetListContainer(dlRt);

            MakeMenuButton(pause.transform, "Resume", "Resume", 0.62f, () => UI.UIManager.Instance.TogglePause());
            MakeMenuButton(pause.transform, "Save", "Save Game", 0.53f, () => pauseScreen.OnSave());
            MakeMenuButton(pause.transform, "Missions", "Missions", 0.44f, () => UI.UIManager.Instance.ToggleMissionBoard());
            MakeMenuButton(pause.transform, "Garage", "Garage", 0.35f, () => UI.UIManager.Instance.ToggleGarage());
            MakeMenuButton(pause.transform, "Achievements", "Achievements", 0.26f, () => UI.UIManager.Instance.ToggleAchievements());
            MakeMenuButton(pause.transform, "Quit", "Main Menu", 0.17f, () => pauseScreen.OnMainMenu());
            pause.SetActive(false);

            // ---------- map screen ----------
            var map = Panel("MapScreen", canvasGo.transform, new Color(0.05f, 0.08f, 0.13f, 0.96f));
            var mapScreen = map.AddComponent<UI.WorldMapScreen>();
            var mapImgGo = new GameObject("MapImage", typeof(RawImage));
            var miRt = (RectTransform)mapImgGo.transform;
            miRt.SetParent(map.transform, false);
            miRt.anchorMin = miRt.anchorMax = new Vector2(0.5f, 0.5f);
            miRt.sizeDelta = new Vector2(1400f, 1400f);
            var miImg = mapImgGo.GetComponent<RawImage>();
            miImg.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/VALEBOUND/Art/UI/Generated/MinimapMap.png");
            miImg.uvRect = new Rect(0f, 0f, 1f, 1f);
            var pinContainerGo = new GameObject("PinContainer", typeof(RectTransform));
            var pcRt = (RectTransform)pinContainerGo.transform;
            pcRt.SetParent(miRt, false);
            pcRt.anchorMin = Vector2.zero; pcRt.anchorMax = Vector2.one;
            pcRt.offsetMin = pcRt.offsetMax = Vector2.zero;

            // map pin prefab: dot + label (built once, referenced by WorldMapScreen)
            var pinGo = new GameObject("MapPin", typeof(Image));
            var pinRt = (RectTransform)pinGo.transform;
            pinRt.sizeDelta = new Vector2(26f, 26f);
            var pinImg = pinGo.GetComponent<Image>();
            pinImg.color = new Color(1f, 0.82f, 0.25f, 0.95f);
            pinImg.raycastTarget = false;   // pins must not block the Fast Travel button
            var pinLbl = new GameObject("Label", typeof(TextMeshProUGUI));
            var plRt = (RectTransform)pinLbl.transform;
            plRt.SetParent(pinRt, false);
            plRt.anchorMin = plRt.anchorMax = new Vector2(0.5f, 1f);
            plRt.pivot = new Vector2(0.5f, 0f);
            plRt.sizeDelta = new Vector2(260f, 30f);
            var plTmp = pinLbl.GetComponent<TextMeshProUGUI>();
            plTmp.fontSize = 24; plTmp.alignment = TextAlignmentOptions.Bottom; plTmp.raycastTarget = false;
            PrefabUtility.SaveAsPrefabAsset(pinGo, "Assets/VALEBOUND/Prefabs/UI/MapPin.prefab");
            UnityEngine.Object.DestroyImmediate(pinGo);

            // fast travel sub-panel inside map (hidden; WorldMapScreen lists stops)
            var ftPanel = Panel("FastTravelPanel", map.transform, new Color(0.06f, 0.1f, 0.17f, 0.97f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(120f, -280f), new Vector2(640f, 260f));
            Label("Title", ftPanel.transform, "FAST TRAVEL", 34, TextAnchor.UpperCenter);
            var ftList = new GameObject("FastTravelList", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var ftlRt = (RectTransform)ftList.transform;
            ftlRt.SetParent(ftPanel.transform, false);
            ftlRt.anchorMin = new Vector2(0f, 0f); ftlRt.anchorMax = new Vector2(1f, 1f);
            ftlRt.offsetMin = new Vector2(16f, 16f); ftlRt.offsetMax = new Vector2(-16f, -56f);
            var ftVlg = ftList.GetComponent<VerticalLayoutGroup>();
            ftVlg.spacing = 6f; ftVlg.childForceExpandHeight = false; ftVlg.childForceExpandWidth = true;
            // simple list-button prefab for fast travel stops
            var ftBtnGo = new GameObject("FastTravelBtn", typeof(Image), typeof(Button));
            var ftbRt = (RectTransform)ftBtnGo.transform;
            ftbRt.sizeDelta = new Vector2(560f, 54f);
            ftBtnGo.GetComponent<Image>().color = new Color(0.16f, 0.5f, 0.85f, 0.95f);
            var ftbLbl = new GameObject("Text", typeof(TextMeshProUGUI));
            var ftblRt = (RectTransform)ftbLbl.transform;
            ftblRt.SetParent(ftbRt, false);
            ftblRt.anchorMin = Vector2.zero; ftblRt.anchorMax = Vector2.one;
            ftblRt.offsetMin = ftblRt.offsetMax = Vector2.zero;
            var ftbTmp = ftbLbl.GetComponent<TextMeshProUGUI>();
            ftbTmp.fontSize = 26; ftbTmp.alignment = TextAlignmentOptions.Center;
            PrefabUtility.SaveAsPrefabAsset(ftBtnGo, "Assets/VALEBOUND/Prefabs/UI/FastTravelBtn.prefab");
            UnityEngine.Object.DestroyImmediate(ftBtnGo);
            Button("FastTravelMode", map.transform, "Fast Travel", new Vector2(0.5f, 0.9f), new Vector2(300f, 70f),
                () => mapScreen.OpenFastTravel());
            Button("CloseBtn", map.transform, "Close", new Vector2(0.5f, 0.08f), new Vector2(300f, 70f),
                () => UI.UIManager.Instance.ToggleMap());
            ftPanel.SetActive(false);
            map.SetActive(false);

            // ---------- shared row prefabs for list screens ----------
            // mission row: title+tag / subtitle / Accept & Abandon buttons
            var mrGo = new GameObject("MissionRow", typeof(Image));
            var mrRt = (RectTransform)mrGo.transform;
            mrRt.sizeDelta = new Vector2(1160f, 110f);
            mrGo.GetComponent<Image>().color = new Color(0.1f, 0.15f, 0.24f, 0.9f);
            var mrTitle = new GameObject("Title", typeof(TextMeshProUGUI));
            ((RectTransform)mrTitle.transform).SetParent(mrRt, false);
            SetAnchors((RectTransform)mrTitle.transform, new Vector2(0f, 0.55f), new Vector2(0.65f, 1f), new Vector2(20f, -6f), new Vector2(-12f, 6f));
            var mrTitleTmp = mrTitle.GetComponent<TextMeshProUGUI>();
            mrTitleTmp.fontSize = 30; mrTitleTmp.color = Color.white;
            var mrSub = new GameObject("Subtitle", typeof(TextMeshProUGUI));
            ((RectTransform)mrSub.transform).SetParent(mrRt, false);
            SetAnchors((RectTransform)mrSub.transform, new Vector2(0f, 0f), new Vector2(0.65f, 0.55f), new Vector2(20f, 8f), new Vector2(-12f, -4f));
            var mrSubTmp = mrSub.GetComponent<TextMeshProUGUI>();
            mrSubTmp.fontSize = 24; mrSubTmp.color = new Color(0.7f, 0.78f, 0.9f);
            var mrAccept = Button("AcceptButton", mrRt, "Accept", new Vector2(0.76f, 0.5f), new Vector2(180f, 60f), null);
            mrAccept.SetActive(false);
            var mrAbandon = Button("AbandonButton", mrRt, "Abandon", new Vector2(0.93f, 0.5f), new Vector2(180f, 60f), null);
            mrAbandon.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(mrGo, "Assets/VALEBOUND/Prefabs/UI/MissionRow.prefab");
            UnityEngine.Object.DestroyImmediate(mrGo);

            // garage/achievement row: name / status / optional buy button
            var grGo = new GameObject("GarageRow", typeof(Image));
            var grRt = (RectTransform)grGo.transform;
            grRt.sizeDelta = new Vector2(1160f, 110f);
            grGo.GetComponent<Image>().color = new Color(0.1f, 0.15f, 0.24f, 0.9f);
            var grName = new GameObject("Name", typeof(TextMeshProUGUI));
            ((RectTransform)grName.transform).SetParent(grRt, false);
            SetAnchors((RectTransform)grName.transform, new Vector2(0f, 0.55f), new Vector2(0.6f, 1f), new Vector2(20f, -6f), new Vector2(-12f, 6f));
            var grNameTmp = grName.GetComponent<TextMeshProUGUI>();
            grNameTmp.fontSize = 30; grNameTmp.color = Color.white;
            var grStatus = new GameObject("Status", typeof(TextMeshProUGUI));
            ((RectTransform)grStatus.transform).SetParent(grRt, false);
            SetAnchors((RectTransform)grStatus.transform, new Vector2(0f, 0f), new Vector2(0.6f, 0.55f), new Vector2(20f, 8f), new Vector2(-12f, -4f));
            var grStatusTmp = grStatus.GetComponent<TextMeshProUGUI>();
            grStatusTmp.fontSize = 24; grStatusTmp.color = new Color(0.95f, 0.8f, 0.3f);
            var grBuy = Button("BuyButton", grRt, "Buy", new Vector2(0.9f, 0.5f), new Vector2(160f, 60f), null);
            grBuy.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(grGo, "Assets/VALEBOUND/Prefabs/UI/GarageRow.prefab");
            UnityEngine.Object.DestroyImmediate(grGo);

            // ---------- mission board ----------
            var board = Panel("MissionBoard", canvasGo.transform, new Color(0.05f, 0.08f, 0.13f, 0.96f));
            board.AddComponent<UI.MissionBoardScreen>();
            Button("CloseBtn", board.transform, "Close", new Vector2(0.5f, 0.06f), new Vector2(300f, 70f),
                () => UI.UIManager.Instance.ToggleMissionBoard());
            var boardList = MakeScrollList(board.transform);
            board.GetComponent<UI.MissionBoardScreen>().SetListContainer(boardList);
            board.SetActive(false);

            // ---------- garage ----------
            var garage = Panel("Garage", canvasGo.transform, new Color(0.05f, 0.08f, 0.13f, 0.96f));
            garage.AddComponent<UI.GarageScreen>();
            Button("CloseBtn", garage.transform, "Close", new Vector2(0.5f, 0.06f), new Vector2(300f, 70f),
                () => UI.UIManager.Instance.ToggleGarage());
            var garageList = MakeScrollList(garage.transform);
            garage.GetComponent<UI.GarageScreen>().SetListContainer(garageList);
            garage.SetActive(false);

            // ---------- achievements ----------
            var achieve = Panel("Achievements", canvasGo.transform, new Color(0.05f, 0.08f, 0.13f, 0.96f));
            achieve.AddComponent<UI.AchievementsScreen>();
            Button("CloseBtn", achieve.transform, "Close", new Vector2(0.5f, 0.06f), new Vector2(300f, 70f),
                () => UI.UIManager.Instance.ToggleAchievements());
            var achList = MakeScrollList(achieve.transform);
            achieve.GetComponent<UI.AchievementsScreen>().SetListContainer(achList);
            achieve.SetActive(false);

            // ---------- settings ----------
            var settings = Panel("Settings", canvasGo.transform, new Color(0.05f, 0.08f, 0.13f, 0.96f));
            settings.AddComponent<UI.SettingsScreen>();
            MakeMenuButton(settings.transform, "CloseBtn", "Close", 0.08f, () => settings.SetActive(false));
            settings.SetActive(false);

            // ---------- game over ----------
            var over = Panel("GameOver", canvasGo.transform, new Color(0.15f, 0.02f, 0.02f, 0.9f));
            var overScreen = over.AddComponent<UI.GameOverScreen>();
            Label("Title", over.transform, "WIPED OUT", 90, TextAnchor.MiddleCenter);
            Button("Respawn", over.transform, "Return to Checkpoint", new Vector2(0.5f, 0.4f), new Vector2(460f, 90f),
                () => GameManager.Instance.RespawnPlayer());
            Button("Menu", over.transform, "Main Menu", new Vector2(0.5f, 0.27f), new Vector2(460f, 90f),
                () => GameManager.Instance.ReturnToMainMenu());
            over.SetActive(false);

            // ---------- loading ----------
            var loading = Panel("Loading", canvasGo.transform, new Color(0.03f, 0.05f, 0.1f, 1f));
            loading.AddComponent<UI.LoadingScreen>();
            Label("LoadingText", loading.transform, "Loading Aurora Valley...", 50, TextAnchor.MiddleCenter);
            Label("TipText", loading.transform, "", 32, TextAnchor.MiddleCenter).transform.Translate(0, -160f, 0);

            // ---------- mission banner ----------
            var banner = Panel("MissionBanner", canvasGo.transform, new Color(0.06f, 0.1f, 0.16f, 0.92f),
                new Vector2(0.5f, 0.8f), new Vector2(0.5f, 0.8f), new Vector2(-420f, -80f), new Vector2(420f, 80f));
            Label("BannerTitle", banner.transform, "", 44, TextAnchor.MiddleCenter);
            Label("BannerBody", banner.transform, "", 28, TextAnchor.MiddleCenter).transform.Translate(0, -44f, 0);
            banner.SetActive(false);

            // ---------- notifications ----------
            var notifContainer = new GameObject("Notifications", typeof(RectTransform));
            var ncRt = (RectTransform)notifContainer.transform;
            ncRt.SetParent(canvasGo.transform, false);
            ncRt.anchorMin = new Vector2(1f, 1f);
            ncRt.anchorMax = new Vector2(1f, 1f);
            ncRt.pivot = new Vector2(1f, 1f);
            ncRt.anchoredPosition = new Vector2(-20f, -170f);
            ncRt.sizeDelta = new Vector2(460f, 600f);
            var vlg = notifContainer.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperRight;
            vlg.spacing = 10f;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            // fade overlay (topmost)
            var fadeGo = new GameObject("FadeOverlay", typeof(Image));
            var fadeRt = (RectTransform)fadeGo.transform;
            fadeRt.SetParent(canvasGo.transform, false);
            fadeRt.anchorMin = Vector2.zero;
            fadeRt.anchorMax = Vector2.one;
            fadeRt.offsetMin = fadeRt.offsetMax = Vector2.zero;
            fadeRt.SetAsLastSibling();
            var fadeImg = fadeGo.GetComponent<Image>();
            fadeImg.color = new Color(0, 0, 0, 0);

            // ---------- wire UIManager fields via SerializedObject ----------
            var ser = new SerializedObject(uiMgr);
            void GO(string prop, GameObject go) =>
                ser.FindProperty(prop).objectReferenceValue = go;
            GO("mainMenuRoot", menu);
            GO("hudRoot", hud);
            GO("pauseRoot", pause);
            GO("settingsRoot", settings);
            GO("mapRoot", map);
            GO("missionBoardRoot", board);
            GO("garageRoot", garage);
            GO("achievementsRoot", achieve);
            GO("gameOverRoot", over);
            GO("loadingRoot", loading);
            GO("dialogueRoot", dialogue);
            GO("creditsText", topLeft.transform.Find("Credits").gameObject);
            GO("clockText", topLeft.transform.Find("Clock").gameObject);
            GO("weatherText", topLeft.transform.Find("Weather").gameObject);
            GO("missionObjectiveText", topRight.transform.Find("MissionText").gameObject);
            GO("healthSlider", health);
            GO("staminaSlider", stamina);
            GO("vehiclePanel", vehicle);
            GO("speedText", vehicle.transform.Find("Speed").gameObject);
            GO("interactionPrompt", prompt);
            GO("interactionText", prompt.transform.Find("PromptText").gameObject);
            GO("notificationContainer", ncRt.gameObject);
            GO("dialogueSpeaker", speaker);
            GO("dialogueText", dText);
            GO("dialogueNextButton", nextBtn);
            GO("dialogueAcceptButton", accBtn);
            GO("missionBanner", banner);
            GO("bannerTitle", banner.transform.Find("BannerTitle").gameObject);
            GO("bannerBody", banner.transform.Find("BannerBody").gameObject);
            GO("fadeOverlayImage", fadeGo);
            ser.ApplyModifiedProperties();

            // row prefabs for the list screens (MissionBoard / Garage / Achievements)
            var boardSer = new SerializedObject(board.GetComponent<UI.MissionBoardScreen>());
            boardSer.FindProperty("missionRowPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VALEBOUND/Prefabs/UI/MissionRow.prefab");
            boardSer.ApplyModifiedProperties();
            var garageSer = new SerializedObject(garage.GetComponent<UI.GarageScreen>());
            garageSer.FindProperty("rowPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VALEBOUND/Prefabs/UI/GarageRow.prefab");
            garageSer.ApplyModifiedProperties();
            var achSer = new SerializedObject(achieve.GetComponent<UI.AchievementsScreen>());
            achSer.FindProperty("rowPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VALEBOUND/Prefabs/UI/GarageRow.prefab");
            achSer.ApplyModifiedProperties();

            // minimap wiring
            var mmSer = new SerializedObject(minimap);
            mmSer.FindProperty("mapImage").objectReferenceValue = mmImg;
            mmSer.FindProperty("playerMarker").objectReferenceValue = pmRt;
            mmSer.FindProperty("missionMarker").objectReferenceValue = mtRt;
            mmSer.FindProperty("bakedMap").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VALEBOUND/Art/UI/Generated/MinimapMap.png");
            mmSer.ApplyModifiedProperties();

            var mapSer = new SerializedObject(mapScreen);
            mapSer.FindProperty("mapImage").objectReferenceValue = mapImgGo.GetComponent<RawImage>();
            mapSer.FindProperty("pinContainer").objectReferenceValue = pcRt;
            mapSer.FindProperty("pinPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VALEBOUND/Prefabs/UI/MapPin.prefab");
            mapSer.FindProperty("fastTravelPanel").objectReferenceValue = ftPanel;
            mapSer.FindProperty("fastTravelList").objectReferenceValue = ftlRt;
            mapSer.FindProperty("fastTravelButtonPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VALEBOUND/Prefabs/UI/FastTravelBtn.prefab");
            mapSer.ApplyModifiedProperties();

            PrefabUtility.SaveAsPrefabAsset(canvasGo, "Assets/VALEBOUND/Prefabs/UI/UICanvas.prefab");
            Debug.Log("[SceneBuilder] UI canvas built & saved.");
        }

        private static GameObject MakeBar(Transform parent, string name, Color color, float yOff)
        {
            var go = new GameObject(name, typeof(Image), typeof(Slider));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, yOff);
            rt.sizeDelta = new Vector2(280f, 22f);
            var img = go.GetComponent<Image>();
            img.color = new Color(0, 0, 0, 0.5f);
            var slider = go.GetComponent<Slider>();
            var fill = new GameObject("Fill", typeof(Image));
            var frt = (RectTransform)fill.transform;
            frt.SetParent(rt, false);
            frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(1, 1);
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = color;
            var fillRect = frt;
            var fillImg = fill.GetComponent<Image>();
            // slider internals
            slider.fillRect = fillRect;
            slider.targetGraphic = fillImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = 1f;
            // hide handle
            var handle = new GameObject("Handle", typeof(Image));
            var hrt = (RectTransform)handle.transform;
            hrt.SetParent(rt, false);
            hrt.sizeDelta = new Vector2(20f, 30f);
            handle.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            slider.handleRect = hrt;
            return go;
        }

        private static void BuildJoystick(Transform parent)
        {
            var baseGo = new GameObject("Joystick", typeof(Image));
            var rt = (RectTransform)baseGo.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.09f, 0.18f);
            rt.sizeDelta = new Vector2(260f, 260f);
            baseGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.08f);
            var cg = baseGo.AddComponent<CanvasGroup>();
            cg.alpha = 0.55f;

            var ringGo = new GameObject("Ring", typeof(Image));
            var rrt = (RectTransform)ringGo.transform;
            rrt.SetParent(rt, false);
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(220f, 220f);
            ringGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.15f);

            var knobGo = new GameObject("Knob", typeof(Image));
            var krt = (RectTransform)knobGo.transform;
            krt.SetParent(rt, false);
            krt.anchorMin = krt.anchorMax = new Vector2(0.5f, 0.5f);
            krt.sizeDelta = new Vector2(96f, 96f);
            knobGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.5f);

            var js = baseGo.AddComponent<UI.VirtualJoystick>();
            var ser = new SerializedObject(js);
            ser.FindProperty("knob").objectReferenceValue = krt;
            ser.FindProperty("baseRing").objectReferenceValue = rrt;
            ser.FindProperty("maxRadius").floatValue = 82f;
            ser.ApplyModifiedProperties();
        }

        private static GameObject MakeTouchButton(Transform parent, string name, string label, Vector2 anchor, UI.TapButton.TapAction action)
        {
            var go = new GameObject(name, typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(150f, 150f);
            go.GetComponent<Image>().color = new Color(1, 1, 1, 0.12f);
            var lbl = new GameObject("Label", typeof(TextMeshProUGUI));
            var lrt = (RectTransform)lbl.transform;
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var tmp = lbl.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 30;
            tmp.alignment = TextAlignmentOptions.Center;
            var btn = go.AddComponent<UI.TapButton>();
            var ser = new SerializedObject(btn);
            ser.FindProperty("action").enumValueIndex = (int)action;
            ser.ApplyModifiedProperties();
            return go;
        }

        private static GameObject MakeHoldButton(Transform parent, string name, string label, Vector2 anchor, UI.HoldButton.HoldAction action)
        {
            var go = MakeTouchButton(parent, name, label, anchor, UI.TapButton.TapAction.Jump);
            // replace TapButton with HoldButton
            var tap = go.GetComponent<UI.TapButton>();
            UnityEngine.Object.DestroyImmediate(tap, true);
            var hold = go.AddComponent<UI.HoldButton>();
            var ser = new SerializedObject(hold);
            ser.FindProperty("action").enumValueIndex = (int)action;
            ser.ApplyModifiedProperties();
            return go;
        }

        private static GameObject MakePedal(Transform parent, string name, string label, Vector2 anchor, UI.VehiclePedal.Pedal pedal)
        {
            var go = MakeTouchButton(parent, name, label, anchor, UI.TapButton.TapAction.Jump);
            var tap = go.GetComponent<UI.TapButton>();
            UnityEngine.Object.DestroyImmediate(tap, true);
            var p = go.AddComponent<UI.VehiclePedal>();
            var ser = new SerializedObject(p);
            ser.FindProperty("pedal").enumValueIndex = (int)pedal;
            ser.ApplyModifiedProperties();
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(170f, 190f);
            return go;
        }

        private static void SetAnchors(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = offMin; rt.offsetMax = offMax;
        }

        private static GameObject MakeMenuButton(Transform parent, string name, string label, float height01, System.Action onClick)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, height01);
            rt.sizeDelta = new Vector2(420f, 74f);
            go.GetComponent<Image>().color = new Color(0.16f, 0.5f, 0.85f, 0.95f);
            go.GetComponent<Button>().onClick.AddListener(() => onClick?.Invoke());
            var lblGo = new GameObject("Text", typeof(TextMeshProUGUI));
            var lrt = (RectTransform)lblGo.transform;
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var tmp = lblGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 32; tmp.alignment = TextAlignmentOptions.Center;
            return go;
        }

        private static RectTransform MakeScrollList(Transform parent)
        {
            var scrollGo = new GameObject("ScrollView", typeof(ScrollRect));
            var srt = (RectTransform)scrollGo.transform;
            srt.SetParent(parent, false);
            srt.anchorMin = new Vector2(0.5f, 0.5f);
            srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(1200f, 760f);
            srt.anchoredPosition = new Vector2(0f, -30f);

            var viewport = new GameObject("Viewport", typeof(Image), typeof(Mask));
            var vrt = (RectTransform)viewport.transform;
            vrt.SetParent(srt, false);
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = vrt.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.3f);

            var content = new GameObject("Content", typeof(RectTransform));
            var crt = (RectTransform)content.transform;
            crt.SetParent(vrt, false);
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.sizeDelta = new Vector2(0f, 1200f);
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = vrt;
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return crt;
        }
    }
}
#endif
