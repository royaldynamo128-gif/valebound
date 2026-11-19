using System;
using UnityEngine;
using UnityEngine.UI;

namespace Valebound.UI
{
    /// <summary>
    /// Per-frame HUD binding: health/stamina, clock/weather, speed, interaction
    /// prompt, vehicle panel visibility. Attached to the HUD root.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [SerializeField] private RectTransform minimapRect;
        [SerializeField] private MinimapRenderer minimap;

        private PlayerController player;
        private PlayerInteraction interaction;
        private float updateTimer;
        private bool lastVehicleState;

        private void Start()
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.OnPlayerSpawned += p =>
                {
                    player = p;
                    interaction = p != null ? p.GetComponent<PlayerInteraction>() : null;
                    if (interaction != null) interaction.OnTargetChanged += _ => RefreshPrompt();
                    if (p != null)
                    {
                        p.OnHealthChanged += UIManager.Instance.SetHealth;
                        p.OnStaminaChanged += UIManager.Instance.SetStamina;
                    }
                };
                gm.OnVehicleEntered += v =>
                {
                    UIManager.Instance.SetVehicleHud(true);
                    SetControlsMode(true);
                };
                gm.OnVehicleExited += v =>
                {
                    UIManager.Instance.SetVehicleHud(false);
                    SetControlsMode(false);
                };
            }
            RefreshPrompt();
        }

        private void SetControlsMode(bool vehicle)
        {
            if (lastVehicleState == vehicle) return;
            lastVehicleState = vehicle;
            var footControls = transform.Find("FootControls");
            var vehicleControls = transform.Find("VehicleControls");
            if (footControls != null) footControls.gameObject.SetActive(!vehicle);
            if (vehicleControls != null) vehicleControls.gameObject.SetActive(vehicle);
            var vehiclePanel = transform.Find("VehiclePanel");   // speed readout: only in a vehicle
            if (vehiclePanel != null) vehiclePanel.gameObject.SetActive(vehicle);
        }

        private void RefreshPrompt()
        {
            if (interaction != null)
                UIManager.Instance.SetInteractionPrompt(interaction.CurrentPrompt);
        }

        private void Update()
        {
            if (player == null)
            {
                var gm = GameManager.Instance;
                if (gm != null && gm.Player != null)
                {
                    player = gm.Player;
                    interaction = player.GetComponent<PlayerInteraction>();
                }
                else return;
            }

            updateTimer += Time.unscaledDeltaTime;
            bool slowTick = updateTimer >= 0.25f;
            if (slowTick) updateTimer = 0f;

            var gm2 = GameManager.Instance;
            var veh = gm2 != null ? gm2.CurrentVehicle : null;
            if (veh != null && veh.Controller != null)
            {
                UIManager.Instance.UpdateSpeed(veh.Controller.SpeedKph);
                Systems.AchievementSystem.Instance?.NotifyDriving(
                    veh.Controller.GetComponent<Rigidbody>().velocity.magnitude * Time.deltaTime);
            }

            if (!slowTick) return;

            var clock = Systems.WorldClock.Instance;
            if (clock != null)
            {
                int h = Mathf.FloorToInt(clock.Hours);
                int m = Mathf.FloorToInt((clock.Hours - h) * 60f);
                UIManager.Instance.SetClock($"{h:00}:{m:00}");
            }
            var weather = Systems.WeatherSystem.Instance;
            if (weather != null)
                UIManager.Instance.SetWeather(WeatherLabel(weather.Current));

            if (interaction != null && gm2 != null && !player.InVehicle)
                UIManager.Instance.SetInteractionPrompt(interaction.CurrentPrompt);
            else
                UIManager.Instance.SetInteractionPrompt(null);
        }

        private string WeatherLabel(Systems.WeatherSystem.WeatherType w) => w switch
        {
            Systems.WeatherSystem.WeatherType.Clear => "Clear",
            Systems.WeatherSystem.WeatherType.Cloudy => "Cloudy",
            Systems.WeatherSystem.WeatherType.Rain => "Rain",
            Systems.WeatherSystem.WeatherType.Fog => "Fog",
            Systems.WeatherSystem.WeatherType.Storm => "Storm",
            _ => "",
        };
    }

    /// <summary>
    /// Minimap: renders roads/POIs onto a small RenderTexture from a top-down
    /// orthographic camera that follows the player, rotating with heading.
    /// A cheaper alternative: baked map texture with a player marker. We use the
    /// baked approach for mobile: draws baked map image + markers on a RawImage.
    /// </summary>
    public class MinimapRenderer : MonoBehaviour
    {
        [SerializeField] private RawImage mapImage;
        [SerializeField] private RectTransform playerMarker;
        [SerializeField] private RectTransform missionMarker;
        [SerializeField] private RectTransform northMarker;
        [SerializeField] private Texture2D bakedMap;
        [SerializeField] private float worldSize = 6144f;   // world meters covered by the map texture (WorldBuilder.WorldSize)
        [SerializeField] private float zoomWorldRadius = 240f; // meters visible around player

        private Transform playerT;

        private void Start()
        {
            if (mapImage != null && bakedMap != null) mapImage.texture = bakedMap;
        }

        private void Update()
        {
            if (playerT == null)
            {
                var p = GameManager.Instance != null ? GameManager.Instance.Player : null;
                if (p == null) return;
                playerT = p.transform;
            }

            // UV rect window into the baked map centered on the player
            Vector2 uvCenter = new Vector2(
                (playerT.position.x + worldSize * 0.5f) / worldSize,
                (playerT.position.z + worldSize * 0.5f) / worldSize);
            Vector2 uvSize = new Vector2(zoomWorldRadius * 2f / worldSize, zoomWorldRadius * 2f / worldSize);
            if (mapImage != null)
                mapImage.uvRect = new Rect(uvCenter.x - uvSize.x * 0.5f, uvCenter.y - uvSize.y * 0.5f, uvSize.x, uvSize.y);

            if (playerMarker != null)
                playerMarker.localRotation = Quaternion.Euler(0f, 0f, -playerT.eulerAngles.y);

            // mission marker: project current objective pos into minimap space
            var obj = MissionManager.Instance != null ? MissionManager.Instance.CurrentObjective : null;
            if (missionMarker != null)
            {
                bool show = obj != null;
                missionMarker.gameObject.SetActive(show);
                if (show)
                {
                    Vector2 uvObj = new Vector2(
                        (obj.targetPos.x + worldSize * 0.5f) / worldSize,
                        (obj.targetPos.z + worldSize * 0.5f) / worldSize);
                    Vector2 local = new Vector2(
                        (uvObj.x - uvCenter.x) / Mathf.Max(0.0001f, uvSize.x),
                        (uvObj.y - uvCenter.y) / Mathf.Max(0.0001f, uvSize.y)); // -0.5..0.5
                    Vector2 clamped = Vector2.ClampMagnitude(local, 0.42f);
                    RectTransform rt = (RectTransform)transform;
                    Vector2 size = rt != null ? new Vector2(((Rect)rt.rect).width, ((Rect)rt.rect).height) : new Vector2(200f, 200f);
                    missionMarker.anchoredPosition = new Vector2(clamped.x * size.x, clamped.y * size.y);
                }
            }
        }
    }
}
