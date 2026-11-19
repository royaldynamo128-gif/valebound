using System;
using UnityEngine;

namespace Valebound.Systems
{
    /// <summary>
    /// Biome weather zones: large trigger volumes around each biome city that
    /// override the global weather with biome ambiance — falling cherry petals,
    /// snow flurries, volcanic embers, desert dust — plus lighting/fog tints.
    /// One BiomeZoneSystem ticks the active zone; zones are trigger volumes.
    /// </summary>
    public class BiomeZone : MonoBehaviour
    {
        public enum Biome { None, Petal, Desert, Frost, Ember }

        [Header("Identity")]
        public Biome biome = Biome.None;
        public string displayName = "";

        [Header("Ambient particles (built at runtime, no assets needed)")]
        [SerializeField] private float particleRate = 160f;
        [SerializeField] private float particleSize = 0.14f;
        [SerializeField] private float fallSpeed = 1.4f;
        [SerializeField] private float swirl = 0.6f;

        [Header("Atmosphere")]
        [SerializeField] private Color fogTint = new Color(0.78f, 0.82f, 0.86f);
        [SerializeField] private float fogDensity = 0.004f;
        [SerializeField] private Color sunTint = Color.white;

        private ParticleSystem particles;
        private BiomeZoneSystem system;

        private static readonly Color PetalColor = new Color(1.00f, 0.72f, 0.80f);
        private static readonly Color SnowColor = new Color(0.95f, 0.97f, 1.00f);
        private static readonly Color EmberColor = new Color(1.00f, 0.45f, 0.15f);
        private static readonly Color DustColor = new Color(0.85f, 0.72f, 0.50f);

        private void Awake()
        {
            var col = GetComponent<Collider>();
            if (col == null)
            {
                var b = gameObject.AddComponent<BoxCollider>();
                b.isTrigger = true;
                b.size = new Vector3(700f, 220f, 700f);
                b.center = new Vector3(0f, 90f, 0f);
            }
            else col.isTrigger = true;
        }

        private void Start()
        {
            system = BiomeZoneSystem.Instance;
            BuildParticles();
        }

        private void BuildParticles()
        {
            var go = new GameObject("Ambience");
            go.transform.SetParent(transform, false);
            // particle volume above the city, drifting down
            go.transform.localPosition = new Vector3(0f, 60f, 0f);
            particles = go.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            float life = 60f / Mathf.Max(0.3f, fallSpeed);
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(fallSpeed * 0.7f, fallSpeed * 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(particleSize * 0.7f, particleSize * 1.3f);
            main.startColor = ParticleColor(biome);
            main.gravityModifier = 0.12f;
            main.maxParticles = 900;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = particles.emission;
            emission.rateOverTime = particleRate;

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(560f, 4f, 560f);   // wide, thin sheet above the city
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var vel = particles.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(-swirl, swirl);
            vel.z = new ParticleSystem.MinMaxCurve(-swirl, swirl);

            var sizeOverLife = particles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, 0.6f);

            // fade in/out over lifetime so sheets don't pop
            var colorOverLife = particles.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(ParticleColor(biome), 0f), new GradientColorKey(ParticleColor(biome), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0.9f, 0.8f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // soft round sprite generated at runtime
            renderer.material = MakeParticleMaterial(ParticleColor(biome));
        }

        private static Color ParticleColor(Biome b) => b switch
        {
            Biome.Petal => PetalColor,
            Biome.Frost => SnowColor,
            Biome.Ember => EmberColor,
            Biome.Desert => DustColor,
            _ => Color.white,
        };

        private static Material MakeParticleMaterial(Color c)
        {
            // built-in particle shader; fall back to sprites/default
            var shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 2f);
            mat.EnableKeyword("_ALPHABLEND_ON");
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            return mat;
        }

        private void OnTriggerEnter(Collider other)
        {
            var pc = other.GetComponentInParent<PlayerController>();
            if (pc != null && system != null) system.NotifyPlayerInZone(this);
        }

        private void OnTriggerExit(Collider other)
        {
            var pc = other.GetComponentInParent<PlayerController>();
            if (pc != null && system != null) system.NotifyPlayerLeftZone(this);
        }

        /// <summary>Zone ambiance strength while active (system lerps this).</summary>
        public void ApplyAtmosphere(float t)
        {
            if (particles != null)
            {
                var em = particles.emission;
                em.rateOverTime = particleRate * t;
            }
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, fogTint, t * 0.5f);
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, fogDensity, t * 0.5f);
        }

        /// <summary>UI label when entering a zone.</summary>
        public string ZoneLabel => biome switch
        {
            Biome.Petal => "Petal Vale — Cherry Blossom Season",
            Biome.Desert => "Mirage Basin — High Sun",
            Biome.Frost => "Frostpeak — Snowfields",
            Biome.Ember => "Emberfall — Ash & Cinder",
            _ => displayName,
        };
    }

    /// <summary>
    /// Singleton tracker: which biome zone the player is inside; applies zone
    /// atmosphere and shows the zone banner once per entry.
    /// </summary>
    public class BiomeZoneSystem : MonoBehaviour
    {
        public static BiomeZoneSystem Instance { get; private set; }

        private readonly System.Collections.Generic.List<BiomeZone> inside = new System.Collections.Generic.List<BiomeZone>();
        private readonly System.Collections.Generic.HashSet<BiomeZone.Biome> visitedBiomes = new System.Collections.Generic.HashSet<BiomeZone.Biome>();
        private BiomeZone current;
        private float blend;
        private float blendTarget;
        private bool announced;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void NotifyPlayerInZone(BiomeZone z)
        {
            if (!inside.Contains(z)) inside.Add(z);
            if (!visitedBiomes.Contains(z.biome))
            {
                visitedBiomes.Add(z.biome);
                if (visitedBiomes.Count >= 4)
                    AchievementSystem.Instance?.Unlock("biome_tourist");
            }
            if (current == null) SetCurrent(z);
        }

        public void NotifyPlayerLeftZone(BiomeZone z)
        {
            inside.Remove(z);
            if (current == z)
                SetCurrent(inside.Count > 0 ? inside[inside.Count - 1] : null);
        }

        private void SetCurrent(BiomeZone z)
        {
            current = z;
            blendTarget = z != null ? 1f : 0f;
            announced = false;
            if (z != null)
            {
                UI.UIManager.Instance?.ShowNotification(z.ZoneLabel, "Weather shifting with the terrain.", 3f);
                announced = true;
                // biome zones suspend global weather transitions while inside
                WeatherSystem.Instance?.ForceWeather(WeatherSystem.WeatherType.Clear);
            }
            else
            {
                RenderSettings.fogDensity = 0.0015f;
            }
        }

        private void Update()
        {
            if (current == null) return;
            blend = Mathf.MoveTowards(blend, blendTarget, Time.deltaTime * 0.5f);
            current.ApplyAtmosphere(blend);
            if (!announced && blend > 0.5f)
            {
                announced = true;
                GameEvents.RaisePOIDiscovered("biome_" + current.biome);
            }
        }
    }
}
