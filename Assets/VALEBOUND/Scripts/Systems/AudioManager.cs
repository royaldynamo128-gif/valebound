using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound.Systems
{
    /// <summary>
    /// Central audio: addressable-by-name clip lookup (loaded from Resources-like
    /// registry populated at build time), music crossfade radio stations, ambience
    /// by district, pooled one-shots with distance culling.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Serializable]
        public class ClipEntry
        {
            public string id;
            public AudioClip clip;
            public float volume = 1f;
        }

        [Header("Registry")]
        [SerializeField] private ClipEntry[] clips;

        [Header("Buses")]
        [SerializeField] private AudioSource musicSourceA;
        [SerializeField] private AudioSource musicSourceB;
        [SerializeField] private AudioSource ambienceSource;

        [Header("One-shots")]
        [SerializeField] private int oneShotVoices = 10;
        [SerializeField] private float oneShotCullDistance = 60f;

        private readonly Dictionary<string, ClipEntry> lookup = new Dictionary<string, ClipEntry>();
        private AudioSource[] voices;
        private int voiceIndex;
        private AudioSource activeMusic;
        private string currentStation;
        private readonly Dictionary<string, List<AudioClip>> stations = new Dictionary<string, List<AudioClip>>();

        private GameSettings settings;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            voices = new AudioSource[oneShotVoices];
            for (int i = 0; i < voices.Length; i++)
            {
                var go = new GameObject($"Voice_{i}");
                go.transform.SetParent(transform, false);
                voices[i] = go.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].spatialBlend = 1f;
                voices[i].minDistance = 4f;
                voices[i].maxDistance = oneShotCullDistance;
                voices[i].rolloffMode = AudioRolloffMode.Linear;
            }
            foreach (var c in clips)
                if (c != null && c.clip != null) lookup[c.id] = c;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            settings = SaveSystem.LoadSettings();
            ApplyVolumes();
        }

        public void ApplyVolumes()
        {
            if (settings == null) settings = SaveSystem.LoadSettings();
            if (musicSourceA != null) musicSourceA.volume = settings.musicVolume;
            if (musicSourceB != null) musicSourceB.volume = 0f;
            if (ambienceSource != null) ambienceSource.volume = settings.ambienceVolume;
            foreach (var v in voices) v.volume = settings.sfxVolume;
        }

        // ---------------- one shots ----------------

        public void PlayOneShot(string id, float volume = 1f)
        {
            if (!lookup.TryGetValue(id, out var e) || e.clip == null) return;
            if (settings == null) settings = SaveSystem.LoadSettings();
            AudioSource src = NextVoice();
            src.spatialBlend = 0f;
            src.PlayOneShot(e.clip, e.volume * volume * settings.sfxVolume);
        }

        public void PlayOneShot3D(string id, Vector3 pos, float volume = 1f)
        {
            if (!lookup.TryGetValue(id, out var e) || e.clip == null) return;
            // distance cull
            var listener = Camera.main;
            if (listener != null && (listener.transform.position - pos).sqrMagnitude
                > oneShotCullDistance * oneShotCullDistance) return;
            if (settings == null) settings = SaveSystem.LoadSettings();
            AudioSource src = NextVoice();
            src.transform.position = pos;
            src.spatialBlend = 1f;
            src.PlayOneShot(e.clip, e.volume * volume * settings.sfxVolume);
        }

        private AudioSource NextVoice()
        {
            var v = voices[voiceIndex];
            voiceIndex = (voiceIndex + 1) % voices.Length;
            return v;
        }

        // ---------------- music / radio ----------------

        public void RegisterStation(string stationId, AudioClip[] tracks)
        {
            stations[stationId] = new List<AudioClip>(tracks);
        }

        public void PlayStation(string stationId)
        {
            if (currentStation == stationId) return;
            if (!stations.TryGetValue(stationId, out var tracks) || tracks.Count == 0) return;
            currentStation = stationId;

            var next = activeMusic == musicSourceA ? musicSourceB : musicSourceA;
            var old = activeMusic == musicSourceA ? musicSourceA : musicSourceB;
            if (old == null) old = musicSourceA;

            next.clip = tracks[UnityEngine.Random.Range(0, tracks.Count)];
            next.loop = tracks.Count == 1;
            if (settings == null) settings = SaveSystem.LoadSettings();
            next.volume = 0f;
            next.Play();

            activeMusic = next;
            StartCoroutine(Crossfade(next, old, 1.2f));
        }

        private IEnumerator Crossfade(AudioSource to, AudioSource from, float duration)
        {
            float t = 0f;
            float targetVol = settings != null ? settings.musicVolume : 0.8f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = t / duration;
                if (to != null) to.volume = Mathf.Lerp(0f, targetVol, k);
                if (from != null) from.volume = Mathf.Lerp(targetVol, 0f, k);
                yield return null;
            }
            if (from != null) { from.Stop(); from.volume = 0f; }
        }

        public void SetMusicVolume(float v)
        {
            if (settings == null) settings = SaveSystem.LoadSettings();
            settings.musicVolume = v;
            if (activeMusic != null) activeMusic.volume = v;
            SaveSystem.SaveSettings(settings);
        }

        public void SetSfxVolume(float v)
        {
            if (settings == null) settings = SaveSystem.LoadSettings();
            settings.sfxVolume = v;
            SaveSystem.SaveSettings(settings);
            ApplyVolumes();
        }

        public void SetAmbienceVolume(float v)
        {
            if (settings == null) settings = SaveSystem.LoadSettings();
            settings.ambienceVolume = v;
            if (ambienceSource != null) ambienceSource.volume = v;
            SaveSystem.SaveSettings(settings);
        }

        // ---------------- ambience ----------------

        public void SetAmbience(AudioClip clip, float fade = 1.5f)
        {
            if (ambienceSource == null) return;
            if (ambienceSource.clip == clip) return;
            StartCoroutine(AmbienceFade(clip, fade));
        }

        private IEnumerator AmbienceFade(AudioClip clip, float duration)
        {
            float startVol = ambienceSource.volume;
            float t = 0f;
            while (t < duration && ambienceSource.clip != clip)
            {
                t += Time.unscaledDeltaTime;
                ambienceSource.volume = Mathf.Lerp(startVol, 0f, t / duration);
                yield return null;
            }
            ambienceSource.clip = clip;
            ambienceSource.loop = true;
            ambienceSource.Play();
            t = 0f;
            float target = settings != null ? settings.ambienceVolume : 0.8f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                ambienceSource.volume = Mathf.Lerp(0f, target, t / duration);
                yield return null;
            }
        }

        /// <summary>Footstep helper used by player & NPCs (surface-agnostic for now).</summary>
        public void PlayFootstep(Vector3 pos, float intensity)
        {
            PlayOneShot3D(UnityEngine.Random.value < 0.5f ? "sfx/footstep_a" : "sfx/footstep_b",
                pos, 0.5f * intensity);
        }
    }
}
