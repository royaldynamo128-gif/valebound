#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Valebound.EditorTools
{
    /// <summary>
    /// One-click pipeline: configure imports (models readable off, colliders,
    /// compression), generate procedural ground textures, tag & layer setup,
    /// quality tiers, and URP assets.
    /// </summary>
    public static class ImportConfigurator
    {
        [MenuItem("VALEBOUND/Configure Project")]
        public static void ConfigureAll()
        {
            ConfigureModelImports();
            ConfigureAudioImports();
            ConfigureTextureImports();
            GenerateGroundTextures();
            SetupTagsAndLayers();
            Debug.Log("[ImportConfigurator] project configured.");
        }

        // ---------------- importers ----------------

        private static void ConfigureModelImports()
        {
            int changed = 0;
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { "Assets/VALEBOUND/Art/Models" })
                         .Select(AssetDatabase.GUIDToAssetPath))
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                bool dirty = false;

                if (importer.isReadable) { importer.isReadable = false; dirty = true; }
                if (importer.importCameras) { importer.importCameras = false; dirty = true; }
                if (importer.importLights) { importer.importLights = false; dirty = true; }
                if (!importer.addCollider)
                {
                    // static world models get mesh colliders; vehicles/characters don't
                    bool isWorld = path.Contains("Buildings") || path.Contains("Streets") || path.Contains("Roads")
                                   || path.Contains("Nature") || path.Contains("Industrial") || path.Contains("Landmarks")
                                   || path.Contains("Props");
                    if (isWorld)
                    {
                        importer.addCollider = true;
                        dirty = true;
                    }
                }
                // vehicles/characters: keep animation, no collider
                if (path.Contains("Vehicles") || path.Contains("Characters"))
                {
                    if (!importer.importAnimation) { importer.importAnimation = true; dirty = true; }
                }
                if (dirty) { importer.SaveAndReimport(); changed++; }
            }
            Debug.Log($"[ImportConfigurator] models reconfigured: {changed}");
        }

        private static void ConfigureAudioImports()
        {
            foreach (var path in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/VALEBOUND/Audio" })
                         .Select(AssetDatabase.GUIDToAssetPath))
            {
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
            Debug.Log("[ImportConfigurator] audio configured (Vorbis compressed).");
        }

        private static void ConfigureTextureImports()
        {
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/VALEBOUND/Art" })
                         .Select(AssetDatabase.GUIDToAssetPath))
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                var android = importer.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.format = TextureImporterFormat.ASTC_6x6;
                android.maxTextureSize = 512;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
            Debug.Log("[ImportConfigurator] textures configured (ASTC 6x6, 512 max for props).");
        }

        // ---------------- generated ground textures ----------------

        private static void GenerateGroundTextures()
        {
            string dir = "Assets/VALEBOUND/Art/Textures";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            MakeNoiseTexture($"{dir}/T_Grass.png", new Color(0.36f, 0.55f, 0.28f), 0.06f, 256);
            MakeNoiseTexture($"{dir}/T_Rock.png", new Color(0.52f, 0.5f, 0.48f), 0.08f, 256);
            MakeNoiseTexture($"{dir}/T_Sand.png", new Color(0.78f, 0.7f, 0.5f), 0.05f, 256);
            MakeNoiseTexture($"{dir}/T_Snow.png", new Color(0.88f, 0.92f, 0.96f), 0.04f, 256);
            MakeNoiseTexture($"{dir}/T_Ash.png", new Color(0.24f, 0.22f, 0.22f), 0.06f, 256);
            AssetDatabase.Refresh();
            Debug.Log("[ImportConfigurator] ground textures generated.");
        }

        private static void MakeNoiseTexture(string path, Color baseCol, float variation, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rng = new System.Random(path.GetHashCode());
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = (float)rng.NextDouble() * variation * 2f - variation;
                tex.SetPixel(x, y, new Color(
                    Mathf.Clamp01(baseCol.r + n),
                    Mathf.Clamp01(baseCol.g + n),
                    Mathf.Clamp01(baseCol.b + n), 1f));
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // ---------------- tags & layers ----------------

        private static void SetupTagsAndLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            void SetLayer(int idx, string name)
            {
                var sp = layers.GetArrayElementAtIndex(idx);
                if (string.IsNullOrEmpty(sp.stringValue)) sp.stringValue = name;
            }
            SetLayer(6, "Player");
            SetLayer(7, "Vehicle");
            SetLayer(8, "NPC");
            SetLayer(9, "Interactable");
            SetLayer(10, "Traffic");
            tagManager.ApplyModifiedProperties();

            // tags
            var tags = tagManager.FindProperty("tags");
            foreach (var tag in new[] { "PlayerSpawn", "MissionGiver", "VehicleSpawn" })
            {
                bool exists = false;
                for (int i = 0; i < tags.arraySize; i++)
                    if (tags.GetArrayElementAtIndex(i).stringValue == tag) { exists = true; break; }
                if (!exists)
                {
                    tags.InsertArrayElementAtIndex(tags.arraySize);
                    tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
                }
            }
            tagManager.ApplyModifiedProperties();
            Debug.Log("[ImportConfigurator] tags & layers configured.");
        }
    }
}
#endif
