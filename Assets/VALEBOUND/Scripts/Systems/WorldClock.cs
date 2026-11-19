using System;
using UnityEngine;

namespace Valebound.Systems
{
    /// <summary>
    /// Game-world time: 24-minute full day by default, hour events, sun/moon drive,
    /// density multipliers for traffic/pedestrians by time of day.
    /// </summary>
    public class WorldClock : MonoBehaviour
    {
        public static WorldClock Instance { get; private set; }

        [Header("Time")]
        [SerializeField] private float startHour = 8.5f;
        [SerializeField] private float dayLengthMinutes = 24f;
        [SerializeField, Range(0f, 24f)] private float _hours;

        [Header("Sun")]
        [SerializeField] private Light directionalLight;
        [SerializeField] private Gradient sunColor;
        [SerializeField] private AnimationCurve sunIntensity;
        [SerializeField] private float sunOrbitTilt = 28f;

        [Header("Sky / Ambient")]
        [SerializeField] private Material skyboxMaterial;
        [SerializeField] private Gradient ambientColor;
        [SerializeField] private AnimationCurve ambientIntensity;

        public float Hours => _hours;
        public bool IsNight => _hours < 6.2f || _hours > 19.4f;
        public bool IsRushHour => (_hours > 7f && _hours < 9.5f) || (_hours > 16.5f && _hours < 18.5f);
        public float TrafficDensityMultiplier => IsRushHour ? 1.25f : (IsNight ? 0.35f : 1f);
        public float PedestrianDensityMultiplier => IsNight ? 0.25f : (IsRushHour ? 1.2f : 0.9f);

        private int lastHour = -1;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _hours = startHour;
            InitLightingDefaults();
        }

        /// <summary>
        /// Serialized gradients/curves arrive empty from AddComponent (they evaluate
        /// to 0 → black world). Fill sensible day/night defaults at runtime if empty.
        /// </summary>
        private void InitLightingDefaults()
        {
            if (sunColor == null || sunColor.colorKeys.Length == 0)
            {
                sunColor = new Gradient();
                sunColor.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1.00f, 0.55f, 0.32f), 0.00f),  // sunrise
                        new GradientColorKey(new Color(1.00f, 0.96f, 0.88f), 0.25f), // morning
                        new GradientColorKey(new Color(1.00f, 0.99f, 0.95f), 0.50f), // noon
                        new GradientColorKey(new Color(1.00f, 0.80f, 0.60f), 0.75f), // sunset
                        new GradientColorKey(new Color(0.62f, 0.72f, 1.00f), 1.00f), // night
                    },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            }
            if (sunIntensity == null || sunIntensity.length == 0)
                sunIntensity = AnimationCurve.Linear(0f, 0.05f, 1f, 1.15f);
            if (ambientColor == null || ambientColor.colorKeys.Length == 0)
            {
                ambientColor = new Gradient();
                ambientColor.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.18f, 0.22f, 0.35f), 0.00f), // night
                        new GradientColorKey(new Color(0.85f, 0.75f, 0.68f), 0.35f), // golden hour
                        new GradientColorKey(new Color(0.78f, 0.85f, 0.98f), 0.60f), // day
                        new GradientColorKey(new Color(0.80f, 0.86f, 1.00f), 1.00f),
                    },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            }
            if (ambientIntensity == null || ambientIntensity.length == 0)
                ambientIntensity = AnimationCurve.Linear(0f, 0.45f, 1f, 1.25f);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            if (directionalLight == null)
            {
                var l = FindObjectOfType<Light>();
                if (l != null && l.type == LightType.Directional) directionalLight = l;
            }
        }

        private void Update()
        {
            float hoursPerSecond = 24f / (dayLengthMinutes * 60f);
            _hours += hoursPerSecond * Time.deltaTime;
            if (_hours >= 24f) _hours -= 24f;

            int hour = Mathf.FloorToInt(_hours);
            if (hour != lastHour)
            {
                lastHour = hour;
                GameEvents.RaiseHourChanged(hour);
            }

            UpdateSun();
            UpdateSky();
        }

        private void UpdateSun()
        {
            if (directionalLight == null) return;
            // sun angle: 6h = sunrise (0deg), 12h = noon (90), 18h = sunset
            float sunAngle = (_hours / 24f) * 360f - 90f;
            Quaternion rot = Quaternion.Euler(sunAngle, sunOrbitTilt, 0f);
            directionalLight.transform.rotation = rot;

            float daylight = Mathf.Clamp01(Vector3.Dot(-directionalLight.transform.forward, Vector3.up));
            directionalLight.color = sunColor != null ? sunColor.Evaluate(daylight) : Color.white;
            directionalLight.intensity = sunIntensity != null ? sunIntensity.Evaluate(daylight) : daylight;

            // moon: dim opposite-facing light reuse (cheap)
            if (daylight < 0.02f)
            {
                directionalLight.intensity = 0.12f;
                directionalLight.color = new Color(0.62f, 0.72f, 1f);
            }
        }

        private void UpdateSky()
        {
            float daylight = Mathf.Clamp01((Mathf.Sin((_hours - 6f) / 24f * 2f * Mathf.PI) + 1f) * 0.5f);
            RenderSettings.ambientLight = ambientColor != null ? ambientColor.Evaluate(daylight) : Color.gray;
            RenderSettings.ambientIntensity = ambientIntensity != null ? ambientIntensity.Evaluate(daylight) : 1f;
            if (skyboxMaterial != null) RenderSettings.skybox = skyboxMaterial;
        }

        public void SetHour(float h)
        {
            _hours = Mathf.Repeat(h, 24f);
        }

        public void ApplySaveData(SaveData d) => SetHour(d.worldTimeHours);
        public void CaptureSaveData(SaveData d) => d.worldTimeHours = _hours;
    }
}
