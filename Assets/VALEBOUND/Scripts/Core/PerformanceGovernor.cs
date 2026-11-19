using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Valebound
{
    /// <summary>
    /// Device-tier detection + adaptive quality governor.
    /// Samples frame time over a rolling window; when the budget is exceeded it
    /// steps quality down (render scale, shadows, LOD bias, traffic density); when
    /// there is generous headroom it may step back up once per session.
    /// </summary>
    public class PerformanceGovernor : MonoBehaviour
    {
        public static PerformanceGovernor Instance { get; private set; }

        public enum Tier { Low = 0, Medium = 1, High = 2 }

        [Header("Detection (defaults tuned for Android mid-range)")]
        [SerializeField] private int lowRamMb = 2048;
        [SerializeField] private int highRamMb = 6144;

        [Header("Adaptive rules")]
        [SerializeField] private float sampleWindow = 4f;
        [SerializeField] private float badFrameMs = 40f;    // below ~25fps
        [SerializeField] private int badFramesToDowngrade = 3;
        [SerializeField] private float goodFrameMs = 22f;   // above ~45fps
        [SerializeField] private float goodSecondsToUpgrade = 20f;
        [SerializeField] private bool allowUpgrade = true;

        public Tier DeviceTier { get; private set; } = Tier.Medium;
        public Tier CurrentQuality { get; private set; } = Tier.Medium;

        /// <summary>Multiplier applied to NPC/traffic spawner budgets.</summary>
        public float SimulationBudget => CurrentQuality switch
        {
            Tier.Low => 0.4f,
            Tier.Medium => 0.7f,
            Tier.High => 1.0f,
            _ => 0.7f,
        };

        /// <summary>View distance multiplier for streaming & minimap.</summary>
        public float ViewBudget => CurrentQuality switch
        {
            Tier.Low => 0.55f,
            Tier.Medium => 0.8f,
            Tier.High => 1.0f,
            _ => 0.8f,
        };

        public event Action<Tier> OnQualityChanged;

        private float windowAccum;
        private int badFrames;
        private float goodSeconds;
        private bool upgradedOnce;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            DeviceTier = DetectDeviceTier();
            CurrentQuality = DeviceTier;
            ApplyTier(CurrentQuality);
            StartCoroutine(SampleLoop());
        }

        public static Tier DetectDeviceTier()
        {
            int ram = SystemInfo.systemMemorySize;
            int cores = SystemInfo.processorCount;
            if (ram <= 0) ram = 4096; // unknown -> assume medium
            if (cores <= 0) cores = 4;

            if (ram >= 6144 && cores >= 8) return Tier.High;
            if (ram <= 2048 || cores <= 4) return Tier.Low;
            return Tier.Medium;
        }

        private void ApplyTier(Tier tier)
        {
            int qualityLevel = tier == Tier.Low ? 0 : tier == Tier.Medium ? 1 : 2;
            QualitySettings.SetQualityLevel(qualityLevel, true);

            switch (tier)
            {
                case Tier.Low:
                    QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
                    QualitySettings.shadowDistance = 0f;
                    QualitySettings.pixelLightCount = 0;
                    QualitySettings.lodBias = 0.6f;
                    QualitySettings.maximumLODLevel = 1;
                    SetUrp(0.7f);
                    break;
                case Tier.Medium:
                    QualitySettings.shadows = UnityEngine.ShadowQuality.HardOnly;
                    QualitySettings.shadowDistance = 60f;
                    QualitySettings.pixelLightCount = 2;
                    QualitySettings.lodBias = 0.8f;
                    QualitySettings.maximumLODLevel = 0;
                    SetUrp(0.85f);
                    break;
                case Tier.High:
                    QualitySettings.shadows = UnityEngine.ShadowQuality.All;
                    QualitySettings.shadowDistance = 120f;
                    QualitySettings.pixelLightCount = 4;
                    QualitySettings.lodBias = 1.0f;
                    QualitySettings.maximumLODLevel = 0;
                    SetUrp(1.0f);
                    break;
            }
            OnQualityChanged?.Invoke(tier);
            Debug.Log($"[PerformanceGovernor] quality={tier} (device={DeviceTier})");
        }

        /// <summary>Adjust render scale on the active URP asset (supported at runtime).</summary>
        private static void SetUrp(float scale)
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp != null) urp.renderScale = scale;
        }

        private IEnumerator SampleLoop()
        {
            while (true)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                windowAccum += dt;
                if (dt > badFrameMs * 0.001f && windowAccum < sampleWindow * 4f) badFrames++;
                else if (dt < goodFrameMs * 0.001f) goodSeconds += dt;

                if (badFrames >= badFramesToDowngrade && CurrentQuality > Tier.Low)
                {
                    ApplyTier(CurrentQuality - 1);
                    badFrames = 0; goodSeconds = 0;
                }
                else if (allowUpgrade && !upgradedOnce && goodSeconds >= goodSecondsToUpgrade
                         && CurrentQuality < Tier.High && CurrentQuality < DeviceTier)
                {
                    ApplyTier(CurrentQuality + 1);
                    upgradedOnce = true; // only one bounce-back per session
                    badFrames = 0; goodSeconds = 0;
                }

                if (windowAccum >= sampleWindow)
                {
                    windowAccum = 0f;
                    badFrames = Mathf.Max(0, badFrames - 1); // decay
                }
            }
        }
    }
}
