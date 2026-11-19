using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound.Vehicle
{
    /// <summary>
    /// Paint skin for one vehicle: captures body renderers at build time and
    /// tints them with the selected factory color. Persisted per vehicle id.
    /// </summary>
    public class VehicleSkin : MonoBehaviour
    {
        [Serializable]
        public class SkinOption
        {
            public string id;
            public string displayName;
            public Color color;
            public int price;          // 0 = free factory colors
        }

        [Header("Captured body renderers (set at build)")]
        [SerializeField] private Renderer[] bodyRenderers;

        [Header("Factory palette (filled from VehicleRosterBuilder.Paints)")]
        [SerializeField] private SkinOption[] paints = Array.Empty<SkinOption>();

        [Header("State")]
        [SerializeField] private string currentPaintId = "";

        public string CurrentPaintId => currentPaintId;
        public SkinOption[] Paints => paints;
        public event Action<string> OnPaintChanged;

        private readonly Dictionary<string, SkinOption> byId = new Dictionary<string, SkinOption>();
        private Color[] originalColors;

        private void Awake()
        {
            RebuildLookup();
            if (originalColors == null) CacheOriginals();
        }

        /// <summary>Called by VehicleRosterBuilder right after model instantiation.</summary>
        public void CaptureBodyRenderers(Transform modelRoot)
        {
            var list = new List<Renderer>();
            foreach (var r in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                // skip wheels & tiny parts: paint only applies to the largest meshes
                if (r.name.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                list.Add(r);
            }
            bodyRenderers = list.ToArray();
            CacheOriginals();

            paints = new SkinOption[EditorPalette.Length];
            for (int i = 0; i < EditorPalette.Length; i++)
                paints[i] = new SkinOption
                {
                    id = $"paint_{i}",
                    displayName = EditorPalette[i].name,
                    color = EditorPalette[i].c,
                    price = i >= 12 ? 450 : 0,      // last two are premium
                };
            RebuildLookup();
        }

        /// <summary>Palette mirrored from the editor builder (runtime-accessible).</summary>
        public static readonly (string name, Color c)[] EditorPalette =
        {
            ("Signal Red",     new Color(0.78f, 0.16f, 0.14f)),
            ("Sunset Orange",  new Color(0.92f, 0.45f, 0.12f)),
            ("Amber Glow",     new Color(0.95f, 0.72f, 0.18f)),
            ("Limelight",      new Color(0.62f, 0.82f, 0.20f)),
            ("Fern Green",     new Color(0.16f, 0.48f, 0.28f)),
            ("Petrol Teal",    new Color(0.10f, 0.48f, 0.50f)),
            ("Sky Blue",       new Color(0.24f, 0.56f, 0.86f)),
            ("Midnight Blue",  new Color(0.10f, 0.16f, 0.38f)),
            ("Violet Dusk",    new Color(0.42f, 0.22f, 0.62f)),
            ("Magenta Pulse",  new Color(0.82f, 0.18f, 0.52f)),
            ("Rose Petal",     new Color(0.94f, 0.52f, 0.62f)),
            ("Snow White",     new Color(0.92f, 0.94f, 0.96f)),
            ("Graphite",       new Color(0.16f, 0.17f, 0.19f)),
            ("Aurora Gold",    new Color(0.85f, 0.66f, 0.22f)),
        };

        private void CacheOriginals()
        {
            if (bodyRenderers == null) return;
            originalColors = new Color[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
                if (bodyRenderers[i] != null)
                    originalColors[i] = bodyRenderers[i].material.color;
        }

        private void RebuildLookup()
        {
            byId.Clear();
            if (paints == null) return;
            foreach (var p in paints) if (p != null) byId[p.id] = p;
        }

        public bool TryGetPaint(string id, out SkinOption opt) => byId.TryGetValue(id, out opt);

        /// <summary>Apply a paint by id. Returns false for unknown ids.</summary>
        public bool ApplyPaint(string paintId)
        {
            if (!byId.TryGetValue(paintId, out var opt)) return false;
            if (bodyRenderers == null) return false;
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                var r = bodyRenderers[i];
                if (r == null) continue;
                // keep per-part brightness of the model: tint by ratio to original
                Color orig = originalColors != null && i < originalColors.Length ? originalColors[i] : Color.white;
                float lum = Mathf.Max(0.35f, (orig.r + orig.g + orig.b) / 3f);
                r.material.color = opt.color * (lum / Mathf.Max(0.35f, opt.color.r + opt.color.g + opt.color.b) * 1.05f);
            }
            currentPaintId = paintId;
            OnPaintChanged?.Invoke(paintId);
            return true;
        }

        public void RestoreOriginal()
        {
            if (bodyRenderers == null || originalColors == null) return;
            for (int i = 0; i < bodyRenderers.Length; i++)
                if (bodyRenderers[i] != null)
                    bodyRenderers[i].material.color = originalColors[i];
            currentPaintId = "";
        }

        /// <summary>Garage quick-apply without payment (preview).</summary>
        public void PreviewPaint(SkinOption opt)
        {
            if (opt == null || bodyRenderers == null) return;
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                var r = bodyRenderers[i];
                if (r == null) continue;
                Color orig = originalColors != null && i < originalColors.Length ? originalColors[i] : Color.white;
                float lum = Mathf.Max(0.35f, (orig.r + orig.g + orig.b) / 3f);
                r.material.color = opt.color * (lum / 0.55f);
            }
        }
    }
}
