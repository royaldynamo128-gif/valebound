#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;

namespace Valebound.EditorTools
{
    /// <summary>
    /// Builds all gameplay prefabs from imported models + creates data assets:
    /// player, 6 vehicles, traffic variants, pedestrians, mission giver NPCs.
    /// </summary>
    public static class PrefabBuilder
    {
        private const string PrefabDir = "Assets/VALEBOUND/Prefabs";
        private const string DataDir = "Assets/VALEBOUND/Data";

        [MenuItem("VALEBOUND/Build Prefabs")]
        public static void BuildAll()
        {
            EnsureDirs();
            BuildPlayer();
            BuildVehicles();
            BuildPedestrians();
            BuildMissionGivers();
            BuildVehicleDefs();
            BuildAudioRegistry();
            AssetDatabase.SaveAssets();
            Debug.Log("[PrefabBuilder] all prefabs built.");
        }

        private static void EnsureDirs()
        {
            foreach (var d in new[]
            {
                PrefabDir, $"{PrefabDir}/Characters", $"{PrefabDir}/Vehicles", $"{PrefabDir}/World",
                $"{DataDir}", "Assets/VALEBOUND/Settings/Generated",
            })
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
        }

        // ---------------- player ----------------

        private static void BuildPlayer()
        {
            // Use the Quaternius animated human as the player rig
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/VALEBOUND/Art/Models/Characters/Quaternius Animated Human/Animated Human.fbx");
            if (model == null)
            {
                Debug.LogWarning("[PrefabBuilder] player model missing; skipping player prefab.");
                return;
            }

            var go = new GameObject("Player");
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var child = (GameObject)PrefabUtility.InstantiatePrefab(model, go.transform);
            child.transform.localPosition = Vector3.zero;

            var pc = go.AddComponent<PlayerController>();
            var inter = go.AddComponent<PlayerInteraction>();

            // animation: build a controller from the FBX clips (idle/walk/run/jump/fall)
            var anim = child.GetComponentInChildren<Animator>(true);
            if (anim != null)
            {
                var controller = BuildPlayerAnimator(model);
                if (controller != null)
                {
                    anim.runtimeAnimatorController = controller;
                    // Avatar must come from the FBX for humanoid retargeting
                    var importer = (ModelImporter)AssetImporter.GetAtPath(
                        AssetDatabase.GetAssetPath(model));
                    if (importer != null && importer.animationType == ModelImporterAnimationType.Generic)
                        anim.applyRootMotion = false;
                }
            }

            // camera rig (scene-level, but prefab includes for convenience)
            var rigGo = new GameObject("PlayerCameraRig");
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(rigGo.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 58f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            var audioListener = camGo.AddComponent<AudioListener>();
            rigGo.AddComponent<PlayerCameraRig>();

            SavePrefab(go, $"{PrefabDir}/Characters/Player.prefab");
            SavePrefab(rigGo, $"{PrefabDir}/Characters/PlayerCameraRig.prefab");
        }

        // ---------------- player animation ----------------

        /// <summary>
        /// Builds an AnimatorController (saved as an asset) driving Idle/Walk/Run/Jump/Fall
        /// from the Quaternius Animated Human clips, parameterized exactly as PlayerController
        /// expects: MoveX, MoveY, Speed (0..1 sprint-scaled), IsGrounded, Jump, Grounded.
        /// </summary>
        private static RuntimeAnimatorController BuildPlayerAnimator(GameObject model)
        {
            const string path = "Assets/VALEBOUND/Settings/Generated/PlayerAnimator.overrideController";
            if (!Directory.Exists("Assets/VALEBOUND/Settings/Generated"))
                Directory.CreateDirectory("Assets/VALEBOUND/Settings/Generated");

            var clips = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(model))
                .OfType<AnimationClip>().ToList();
            AnimationClip Find(string contains) =>
                clips.FirstOrDefault(c => c.name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0);

            var idle = Find("idle") ?? clips.FirstOrDefault();
            var walk = Find("walk");
            var run = Find("run");
            var jump = Find("jump");
            var fall = Find("fall") ?? Find("air") ?? jump;

            if (idle == null) { Debug.LogWarning("[PrefabBuilder] no clips found; player unanimated."); return null; }

            var controller = new AnimatorController();
            controller.name = "PlayerAnimator";
            AssetDatabase.CreateAsset(controller, path);

            // parameters
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);

            var root = controller.layers[0].stateMachine;

            AnimatorState NewState(string name, AnimationClip clip, float speed = 1f)
            {
                var st = root.AddState(name);
                st.motion = clip;
                st.speed = speed;
                if (AssetDatabase.Contains(clip))
                    AssetDatabase.AddObjectToAsset(st, controller);
                return st;
            }

            var sIdle = NewState("Idle", idle);
            var sWalk = walk != null ? NewState("Walk", walk) : null;
            var sRun = run != null ? NewState("Run", run, 1.1f) : null;
            var sJump = jump != null ? NewState("Jump", jump) : null;
            var sFall = fall != null ? NewState("Fall", fall) : null;

            // blend Idle/Walk/Run on Speed (PlayerController already writes 0..1 sprint-scaled)
            var blend = new BlendTree
            {
                name = "Locomotion",
                hideFlags = HideFlags.HideInHierarchy,
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false,
            };
            var children = new List<ChildMotion> { new ChildMotion { motion = sIdle.motion, timeScale = 1f, threshold = 0f, position = Vector2.zero } };
            if (sWalk != null) children.Add(new ChildMotion { motion = sWalk.motion, timeScale = 1f, threshold = 0.45f, position = Vector2.zero });
            if (sRun != null) children.Add(new ChildMotion { motion = sRun.motion, timeScale = 1.1f, threshold = 1f, position = Vector2.zero });
            blend.children = children.ToArray();
            AssetDatabase.AddObjectToAsset(blend, controller);

            var sLoco = root.AddState("Locomotion");
            sLoco.motion = blend;

            // default state
            root.defaultState = sLoco;

            // grounded transitions
            void AddTrans(AnimatorState from, AnimatorState to, System.Action<AnimatorStateTransition> setup)
            {
                var t = from.AddTransition(to);
                setup(t);
            }
            AddTrans(sLoco, sJump != null ? sJump : sLoco, t => { t.AddCondition(AnimatorConditionMode.If, 0f, "Jump"); t.duration = 0.05f; });
            AddTrans(sJump != null ? sJump : sLoco, sFall != null ? sFall : sLoco, t => { t.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrounded"); t.duration = 0.1f; t.exitTime = 0.7f; });
            AddTrans(sFall != null ? sFall : sLoco, sLoco, t => { t.AddCondition(AnimatorConditionMode.If, 0f, "IsGrounded"); t.duration = 0.1f; });

            AssetDatabase.SaveAssets();
            return controller;
        }

        // ---------------- vehicles ----------------

        private static readonly (string id, string name, Vehicle.VehicleClass cls, float maxKph, float accel, int price, int unlockAt)[] VehicleSpecs =
        {
            ("sedan",      "Valley Sedan",   Vehicle.VehicleClass.Sedan,     128f,  9f,     0, -1),
            ("van",        "Cargo Van",      Vehicle.VehicleClass.Van,       108f,  7f,  2500,  1),
            ("hatchback",  "Springs Hatch",  Vehicle.VehicleClass.Sedan,     118f,  9f,  1800, -1),
            ("sedan-sports","Aurora GT",     Vehicle.VehicleClass.Sports,    172f, 13f,  6500,  4),
            ("suv",        "Ridge SUV",      Vehicle.VehicleClass.Pickup,    122f,  8f,  3200,  2),
            ("race",       "Comet Racer",    Vehicle.VehicleClass.Sports,    186f, 14f,  9500,  7),
        };

        private static void BuildVehicles()
        {
            string carDir = "Assets/VALEBOUND/Art/Models/Vehicles/Kenney Car Kit";
            foreach (var spec in VehicleSpecs)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{carDir}/{spec.name}.fbx");
                if (model == null)
                {
                    // try alternates
                    string alt = spec.id switch
                    {
                        "hatchback" => "hatchback-sports.fbx",
                        "sedan-sports" => "sedan-sports.fbx",
                        "suv" => "suv.fbx",
                        _ => spec.id + ".fbx",
                    };
                    model = AssetDatabase.LoadAssetAtPath<GameObject>($"{carDir}/{alt}");
                }
                if (model == null)
                {
                    Debug.LogWarning($"[PrefabBuilder] car model '{spec.name}' not found, skipped.");
                    continue;
                }

                var go = new GameObject($"Vehicle_{spec.id}");
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 1200f;
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(1.9f, 1.1f, 4.3f);
                box.center = new Vector3(0f, 0.6f, 0f);

                var child = (GameObject)PrefabUtility.InstantiatePrefab(model, go.transform);
                child.transform.localPosition = new Vector3(0f, 0.35f, 0f);

                var vc = go.AddComponent<Vehicle.VehicleController>();
                var pv = go.AddComponent<Vehicle.PlayerVehicle>();
                var agent = go.AddComponent<Systems.TrafficAIAgent>();

                // audio
                var engine = go.AddComponent<AudioSource>();
                engine.playOnAwake = false;
                engine.loop = true;
                engine.spatialBlend = 1f;
                engine.minDistance = 6f;
                engine.maxDistance = 60f;

                SavePrefab(go, $"{PrefabDir}/Vehicles/Vehicle_{spec.id}.prefab");
            }

            // traffic pool prefabs (simplified variants reuse same prefabs)
        }

        private static void BuildVehicleDefs()
        {
            foreach (var spec in VehicleSpecs)
            {
                var def = ScriptableObject.CreateInstance<Vehicle.VehicleDef>();
                def.vehicleId = spec.id;
                def.displayName = spec.name;
                def.vehicleClass = spec.cls;
                def.maxSpeedKph = spec.maxKph;
                def.acceleration = spec.accel;
                def.price = spec.price;
                def.unlockMissionIndex = spec.unlockAt;
                def.steeringDegPerSec = spec.cls == Vehicle.VehicleClass.Sports ? 85f : 70f;
                AssetDatabase.CreateAsset(def, $"{DataDir}/VehicleDef_{spec.id}.asset");
            }
        }

        // ---------------- pedestrians ----------------

        private static void BuildPedestrians()
        {
            string dir = "Assets/VALEBOUND/Art/Models/Characters/Quaternius Posed Humans";
            string[] models =
            {
                "Male_Walking.fbx", "Female_Walking.fbx", "Male_Standing.fbx",
                "Female_Standing.fbx", "Male_Running.fbx", "Female_Running.fbx",
            };
            int built = 0;
            foreach (var m in models)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{m}");
                if (model == null) continue;
                var go = new GameObject("Ped_" + Path.GetFileNameWithoutExtension(m));
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.height = 1.75f;
                capsule.radius = 0.3f;
                capsule.center = new Vector3(0f, 0.88f, 0f);
                var agent = go.AddComponent<NavMeshAgent>();
                agent.radius = 0.3f;
                agent.height = 1.75f;
                agent.speed = 1.35f;

                var child = (GameObject)PrefabUtility.InstantiatePrefab(model, go.transform);
                child.transform.localPosition = Vector3.zero;

                var ped = go.AddComponent<Systems.PedestrianNPC>();

                // name tag
                var tagGo = new GameObject("NameTag");
                tagGo.transform.SetParent(go.transform, false);
                tagGo.transform.localPosition = new Vector3(0f, 2.1f, 0f);
                var tm = tagGo.AddComponent<TextMesh>();
                tm.fontSize = 26;
                tm.characterSize = 0.12f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = Color.white;
                tagGo.AddComponent<Systems.BillboardNameTag>();

                SavePrefab(go, $"{PrefabDir}/Characters/Ped_{Path.GetFileNameWithoutExtension(m)}.prefab");
                built++;
            }
            Debug.Log($"[PrefabBuilder] pedestrians built: {built}");

            // taxi-capable pedestrians (wave you down for rides)
            string[] taxiModels = { "Female_Waving.fbx", "Male_Waving.fbx" };
            int taxis = 0;
            foreach (var m in taxiModels)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{m}");
                if (model == null)
                {
                    // fallback: any waving/standing pose
                    model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/Male_Standing_Waving.fbx")
                         ?? AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/Female_Standing.fbx");
                }
                if (model == null) continue;
                var go = new GameObject("TaxiPed_" + Path.GetFileNameWithoutExtension(m));
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.height = 1.75f; capsule.radius = 0.3f; capsule.center = new Vector3(0f, 0.88f, 0f);
                var agent = go.AddComponent<NavMeshAgent>();
                agent.radius = 0.3f; agent.height = 1.75f; agent.speed = 1.3f;
                var child = (GameObject)PrefabUtility.InstantiatePrefab(model, go.transform);
                child.transform.localPosition = Vector3.zero;
                go.AddComponent<Systems.PedestrianNPC>();
                go.AddComponent<Valebound.NPC.TaxiPassengerNPC>();
                SavePrefab(go, $"{PrefabDir}/Characters/TaxiPed_{Path.GetFileNameWithoutExtension(m)}.prefab");
                taxis++;
            }
            Debug.Log($"[PrefabBuilder] taxi pedestrians built: {taxis}");
        }

        // ---------------- mission givers ----------------

        private static void BuildMissionGivers()
        {
            string dir = "Assets/VALEBOUND/Art/Models/Characters/Quaternius Posed Humans";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/Male_Standing_Waving.fbx");
            if (model == null) model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/Female_Standing.fbx");

            (string id, string name)[] givers =
            {
                ("mara", "Mara Vex"),
                ("cass", "Cass Silva"),
                ("dana", "Officer Dana Ruiz"),
                ("hale", "Dr. Imogen Hale"),
                ("vo", "Fixer Vo"),
            };
            foreach (var (id, name) in givers)
            {
                var go = new GameObject($"Giver_{id}");
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.height = 1.75f; capsule.radius = 0.35f; capsule.center = new Vector3(0f, 0.88f, 0f);
                if (model != null)
                {
                    var child = (GameObject)PrefabUtility.InstantiatePrefab(model, go.transform);
                    child.transform.localPosition = Vector3.zero;
                }
                var giver = go.AddComponent<Valebound.NPC.MissionGiverNPC>();

                var tagGo = new GameObject("NameTag");
                tagGo.transform.SetParent(go.transform, false);
                tagGo.transform.localPosition = new Vector3(0f, 2.15f, 0f);
                var tm = tagGo.AddComponent<TextMesh>();
                tm.fontSize = 30; tm.characterSize = 0.13f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = new Color(1f, 0.84f, 0.3f);

                SavePrefab(go, $"{PrefabDir}/Characters/Giver_{id}.prefab");
            }
        }

        // ---------------- audio registry ----------------

        private static void BuildAudioRegistry()
        {
            var am = new GameObject("AudioManager").AddComponent<Systems.AudioManager>();
            var entries = new List<Systems.AudioManager.ClipEntry>();

            void AddDir(string dir, string prefix, float vol = 1f)
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, "*.ogg").Concat(Directory.GetFiles(dir, "*.mp3")))
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(f);
                    if (clip == null) continue;
                    entries.Add(new Systems.AudioManager.ClipEntry
                    {
                        id = prefix + Path.GetFileNameWithoutExtension(f).ToLowerInvariant(),
                        clip = clip,
                        volume = vol,
                    });
                }
            }

            AddDir("Assets/VALEBOUND/Audio/SFX/Interface", "sfx/ui/", 0.8f);
            AddDir("Assets/VALEBOUND/Audio/SFX/Vehicles", "sfx/vehicle/", 0.9f);
            AddDir("Assets/VALEBOUND/Audio/SFX/Loops", "sfx/loop/", 0.7f);
            AddDir("Assets/VALEBOUND/Audio/SFX/kenney_rpg_sounds", "sfx/rpg/", 0.7f);
            AddDir("Assets/VALEBOUND/Audio/Music", "music/", 0.8f);
            AddDir("Assets/VALEBOUND/Audio/Ambience", "amb/", 0.8f);

            // canonical short ids used in code
            Map(entries, "sfx/ui/", "confirmation", "sfx/ui/confirmation_001");
            Map(entries, "sfx/ui/", "pickup", "sfx/ui/confirmation_002");
            Map(entries, "sfx/ui/", "objective_next", "sfx/ui/confirmation_001");
            Map(entries, "sfx/ui/", "mission_complete", "sfx/ui/confirmation_004");
            Map(entries, "sfx/ui/", "fail", "sfx/ui/error_004");
            Map(entries, "sfx/ui/", "levelup", "sfx/ui/confirmation_006");
            Map(entries, "sfx/ui/", "achievement", "sfx/ui/confirmation_007");
            Map(entries, "sfx/ui/", "discover", "sfx/ui/question_004");
            Map(entries, "sfx/ui/", "gate", "sfx/ui/click_004");
            Map(entries, "sfx/ui/", "shard", "sfx/rpg/sound_46");
            Map(entries, "sfx/ui/", "camera", "sfx/rpg/sound_41");
            Map(entries, "sfx/ui/", "fasttravel", "sfx/ui/click_002");
            Map(entries, "sfx/ui/", "race_start", "sfx/rpg/sound_35");
            Map(entries, "sfx/vehicle/", "crash", "sfx/vehicle/car_crash_01");
            Map(entries, "sfx/vehicle/", "horn", "sfx/vehicle/car_horn_01");
            Map(entries, "sfx/vehicle/", "door", "sfx/vehicle/car_door_close_01");
            Map(entries, "sfx/vehicle/", "engine", "sfx/vehicle/car_engine_loop_01");
            Map(entries, "sfx/footstep_", "footstep_a", "sfx/rpg/sound_01");
            Map(entries, "sfx/footstep_", "footstep_b", "sfx/rpg/sound_02");

            var ser = new SerializedObject(am);
            ser.FindProperty("clips").arraySize = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                ser.FindProperty("clips").GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue = entries[i].id;
                ser.FindProperty("clips").GetArrayElementAtIndex(i).FindPropertyRelative("clip").objectReferenceValue = entries[i].clip;
                ser.FindProperty("clips").GetArrayElementAtIndex(i).FindPropertyRelative("volume").floatValue = entries[i].volume;
            }
            ser.ApplyModifiedProperties();
            SavePrefab(am.gameObject, $"{PrefabDir}/Systems/AudioManager.prefab");
        }

        private static void Map(List<Systems.AudioManager.ClipEntry> entries, string unused, string shortId, string fullId)
        {
            var full = entries.Find(e => e.id == fullId);
            if (full != null)
                entries.Add(new Systems.AudioManager.ClipEntry { id = shortId, clip = full.clip, volume = full.volume });
        }

        private static void SavePrefab(GameObject go, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
#endif
