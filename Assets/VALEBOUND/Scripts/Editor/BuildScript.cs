#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Valebound.EditorTools
{
    /// <summary>
    /// CI build entry points used by release/build_android.sh.
    /// Ensures scenes are configured, runs the full generation pipeline if the
    /// world is not yet baked, and produces APK/AAB.
    /// </summary>
    public static class BuildScript
    {
        private const string SceneDir = "Assets/VALEBOUND/Scenes";
        private static readonly string[] SceneList =
        {
            $"{SceneDir}/Boot.unity",
            $"{SceneDir}/Main.unity",
        };

        [MenuItem("VALEBOUND/Build/Prepare Project (full pipeline)")]
        public static void PrepareProject()
        {
            // 1. import configuration (models, audio, textures, tags)
            ImportConfigurator.ConfigureAll();

            // 2. prefabs (player, base NPCs, audio registry)
            PrefabBuilder.BuildAll();

            // 2b. expanded vehicle roster (17 vehicles incl. bikes, with paint skins)
            VehicleRosterBuilder.BuildAll();

            // 3. world generation (terrain, cities, roads, POIs) in a fresh scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, $"{SceneDir}/Main.unity");
            // WorldBuilder generation happens in the open scene:
            var wb = EditorWindow.GetWindow<WorldBuilder>();
            wb.GenerateFromBuildScript();

            // 4. UI + systems assembly
            SceneBuilder.BuildAll();

            // 5. data assets (missions, achievements)
            DataAssetBuilder.BuildAll();

            AssetDatabase.SaveAssets();
            File.WriteAllText($"{SceneDir}/.pipeline_v{PipelineVersion}", PipelineVersion.ToString());
            Debug.Log("[BuildScript] project prepared.");
        }

        [MenuItem("VALEBOUND/Build/Android Debug APK")]
        public static void BuildDebugApk()
        {
            EnsureWorld();
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.development = true;
            EditorUserBuildSettings.connectProfiler = false;
            Build("APK", EditorUserBuildSettings.buildAppBundle);
        }

        public static void BuildAndRun()
        {
            EnsureWorld();
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.development = true;
            Build("APK", EditorUserBuildSettings.buildAppBundle, runOnDevice: true);
        }

        [MenuItem("VALEBOUND/Build/Android Release AAB")]
        public static void BuildReleaseAab()
        {
            EnsureWorld();
            EditorUserBuildSettings.buildAppBundle = true;
            EditorUserBuildSettings.development = false;
            // keystore values come from Player Settings (set once in editor):
            //   PlayerSettings.Android.keystoreName / .keystorePass / .keyaliasName / .keyaliasPass
            if (string.IsNullOrEmpty(PlayerSettings.Android.keystoreName))
            {
                Debug.LogWarning("[BuildScript] Release keystore not configured in Player Settings. " +
                                 "Set Edit > Project Settings > Player > Publishing Settings before uploading to Play.");
            }
            Build("AAB", EditorUserBuildSettings.buildAppBundle);
        }

        [MenuItem("VALEBOUND/Build/Linux Standalone")]
        public static void BuildLinux()
        {
            EnsureWorld();
            var opts = new BuildPlayerOptions
            {
                scenes = SceneList,
                locationPathName = Path.Combine("build", "Linux", "VALEBOUND.x86_64"),
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.Development,
            };
            Directory.CreateDirectory(Path.Combine("build", "Linux"));
            var report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[BuildScript] Linux build FAILED: {report.summary.result} ({report.summary.totalErrors} errors)");
                EditorApplication.Exit(1);
            }
            Debug.Log($"[BuildScript] Linux build OK: {report.summary.totalSize / 1024 / 1024} MB in {report.summary.totalTime.TotalSeconds:F0}s");
        }

        // Bump when world/UI generation logic changes so EnsureWorld regenerates
        // instead of repackaging a stale baked scene.
        private const int PipelineVersion = 2;

        private static void EnsureWorld()
        {
            bool missing = !File.Exists($"{SceneDir}/Main.unity");
            string markerPath = $"{SceneDir}/.pipeline_v{PipelineVersion}";
            bool stale = !File.Exists(markerPath);
            if (missing || stale)
            {
                Debug.Log($"[BuildScript] Main scene {(missing ? "missing" : "stale (pipeline v{PipelineVersion})")} — running full prepare pipeline.");
                PrepareProject();
                File.WriteAllText(markerPath, PipelineVersion.ToString());
            }
            if (EditorBuildSettings.scenes.Length == 0)
            {
                EditorBuildSettings.scenes = SceneList.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
            }
        }

        private static void Build(string tag, bool appBundle, bool runOnDevice = false)
        {
            var opts = new BuildPlayerOptions
            {
                scenes = SceneList,
                locationPathName = appBundle
                    ? Path.Combine("build", "VALEBOUND-release.aab")
                    : Path.Combine("build", "VALEBOUND-debug.apk"),
                target = BuildTarget.Android,
                options = runOnDevice ? BuildOptions.AutoRunPlayer : BuildOptions.None,
            };
            Directory.CreateDirectory("build");

            var report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[BuildScript] {tag} build FAILED: {report.summary.result} " +
                               $"({report.summary.totalErrors} errors)");
                EditorApplication.Exit(1);
            }
            Debug.Log($"[BuildScript] {tag} build OK: {report.summary.totalSize / 1024 / 1024} MB " +
                      $"in {report.summary.totalTime.TotalSeconds:F0}s");
        }
    }
}
#endif
