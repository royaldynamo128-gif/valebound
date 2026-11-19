using System;
using System.Collections;
using UnityEngine;

namespace Valebound.Systems
{
    /// <summary>
    /// Dynamic weather: clear -> cloudy -> rain -> fog (+ storm at night) with smooth
    /// transitions, rain particle system, fog density, wet-road audio ducking, and
    /// thunder flashes. State machine on a timer with weighted random next states.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        public enum WeatherType { Clear, Cloudy, Rain, Fog, Storm }

        public static WeatherSystem Instance { get; private set; }

        [Header("Cycle")]
        [SerializeField] private WeatherType current = WeatherType.Clear;
        [SerializeField] private float minDuration = 120f;
        [SerializeField] private float maxDuration = 300f;
        [SerializeField, Range(0f, 1f)] private float badWeatherChance = 0.45f;

        [Header("Rain")]
        [SerializeField] private ParticleSystem rainParticles;
        [SerializeField] private float rainEmissionClear = 0f;
        [SerializeField] private float rainEmissionRain = 420f;
        [SerializeField] private float rainEmissionStorm = 800f;

        [Header("Fog")]
        [SerializeField] private Color fogColorDay = new Color(0.78f, 0.82f, 0.86f);
        [SerializeField] private Color fogColorNight = new Color(0.1f, 0.12f, 0.18f);
        [SerializeField] private float fogDensityClear = 0.0015f;
        [SerializeField] private float fogDensityFog = 0.028f;
        [SerializeField] private float fogDensityRain = 0.008f;

        [Header("Storm")]
        [SerializeField] private Light lightningLight;
        [SerializeField] private float lightningMinInterval = 7f;
        [SerializeField] private float lightningMaxInterval = 20f;

        [Header("Audio")]
        [SerializeField] private AudioSource rainAudio;
        [SerializeField] private float rainAudioMax = 0.65f;

        public WeatherType Current => current;
        public float Wetness { get; private set; }   // 0 dry - 1 soaked (for shader / audio)
        public bool IsRaining => current == WeatherType.Rain || current == WeatherType.Storm;

        private float stateTimer;
        private float targetFogDensity;
        private float targetRainEmission;
        private float lightningTimer;
        private bool transitioning;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            ApplyImmediate(current);
            stateTimer = UnityEngine.Random.Range(minDuration, maxDuration);
            if (rainAudio != null) rainAudio.volume = 0f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // state timer
            if (!transitioning)
            {
                stateTimer -= dt;
                if (stateTimer <= 0f) PickNextWeather();
            }

            // smooth transitions
            if (rainParticles != null)
            {
                var em = rainParticles.emission;
                float rate = Mathf.Lerp(em.rateOverTime.constant, targetRainEmission, dt * 0.5f);
                em.rateOverTime = rate;
            }
            if (rainAudio != null)
                rainAudio.volume = Mathf.Lerp(rainAudio.volume, IsRaining ? rainAudioMax : 0f, dt * 0.4f);

            bool night = WorldClock.Instance != null && WorldClock.Instance.IsNight;
            RenderSettings.fogColor = Color.Lerp(fogColorDay, fogColorNight, night ? 1f : 0f);
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, targetFogDensity, dt * 0.3f);

            Wetness = Mathf.MoveTowards(Wetness, IsRaining ? 1f : 0f, dt / (IsRaining ? 30f : 90f));

            // lightning
            if (current == WeatherType.Storm)
            {
                lightningTimer -= dt;
                if (lightningTimer <= 0f)
                {
                    StartCoroutine(LightningFlash());
                    lightningTimer = UnityEngine.Random.Range(lightningMinInterval, lightningMaxInterval);
                }
            }
        }

        private void PickNextWeather()
        {
            WeatherType next;
            float roll = UnityEngine.Random.value;
            bool badAllowed = UnityEngine.Random.value < badWeatherChance;
            switch (current)
            {
                case WeatherType.Clear:
                    next = badAllowed ? (roll < 0.7f ? WeatherType.Cloudy : WeatherType.Rain) : WeatherType.Clear;
                    break;
                case WeatherType.Cloudy:
                    next = roll < 0.4f ? WeatherType.Clear : roll < 0.75f ? WeatherType.Rain : WeatherType.Fog;
                    break;
                case WeatherType.Rain:
                    next = roll < 0.45f ? WeatherType.Cloudy
                        : (WorldClock.Instance != null && WorldClock.Instance.IsNight ? WeatherType.Storm : WeatherType.Fog);
                    break;
                case WeatherType.Fog:
                    next = roll < 0.6f ? WeatherType.Cloudy : WeatherType.Clear;
                    break;
                case WeatherType.Storm:
                    next = WeatherType.Rain;
                    break;
                default:
                    next = WeatherType.Clear;
                    break;
            }
            if (next == current)
            {
                stateTimer = UnityEngine.Random.Range(minDuration, maxDuration);
                return;
            }
            current = next;
            ApplyImmediate(current);
            stateTimer = UnityEngine.Random.Range(minDuration, maxDuration);
            GameEvents.RaiseWeatherChanged();
        }

        private void ApplyImmediate(WeatherType type)
        {
            switch (type)
            {
                case WeatherType.Clear:
                    targetFogDensity = fogDensityClear;
                    targetRainEmission = rainEmissionClear;
                    break;
                case WeatherType.Cloudy:
                    targetFogDensity = fogDensityClear * 1.6f;
                    targetRainEmission = rainEmissionClear;
                    break;
                case WeatherType.Rain:
                    targetFogDensity = fogDensityRain;
                    targetRainEmission = rainEmissionRain;
                    break;
                case WeatherType.Fog:
                    targetFogDensity = fogDensityFog;
                    targetRainEmission = rainEmissionClear;
                    break;
                case WeatherType.Storm:
                    targetFogDensity = fogDensityRain * 1.4f;
                    targetRainEmission = rainEmissionStorm;
                    break;
            }
            RenderSettings.fog = true;
        }

        private IEnumerator LightningFlash()
        {
            if (lightningLight == null) yield break;
            lightningLight.enabled = true;
            lightningLight.intensity = 2.6f;
            yield return new WaitForSeconds(0.08f);
            lightningLight.intensity = 0.7f;
            yield return new WaitForSeconds(0.06f);
            lightningLight.intensity = 2.2f;
            yield return new WaitForSeconds(0.1f);
            lightningLight.enabled = false;
            lightningLight.intensity = 0f;
        }

        public void ForceWeather(WeatherType type)
        {
            current = type;
            ApplyImmediate(type);
            GameEvents.RaiseWeatherChanged();
        }

        public void ApplySaveData(SaveData d)
        {
            current = (WeatherType)d.weatherIndex;
            ApplyImmediate(current);
        }

        public void CaptureSaveData(SaveData d) => d.weatherIndex = (int)current;
    }
}
