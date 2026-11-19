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
    /// Builds the expanded vehicle roster (17 vehicles incl. 2 bikes) with paint
    /// skin support, and the rider seat rig for bikes. Run after Configure Project
    /// (models imported) and before Build Scenes.
    /// </summary>
    public static class VehicleRosterBuilder
    {
        private const string PrefabDir = "Assets/VALEBOUND/Prefabs/Vehicles";
        private const string DataDir = "Assets/VALEBOUND/Data";
        private const string CarDir = "Assets/VALEBOUND/Art/Models/Vehicles/Kenney Car Kit";
        private const string PubDir = "Assets/VALEBOUND/Art/Models/Vehicles/Quaternius Public Transport";
        private const string RiderModel = "Assets/VALEBOUND/Art/Models/Characters/Quaternius Posed Humans/Male_Sitting.fbx";

        // id, display, class, model file, maxKph, accel, steer, price, unlockStory(-1=start)
        private static readonly (string id, string name, Vehicle.VehicleClass cls, string model,
            float maxKph, float accel, float steer, int price, int unlockAt)[] Specs =
        {
            // ---- starter & cheap tier ----
            ("sedan",        "Valley Sedan",    Vehicle.VehicleClass.Sedan,   "sedan.fbx",             128f,  9f, 72f,     0, -1),
            ("hatchback",    "Springs Hatch",   Vehicle.VehicleClass.Sedan,   "hatchback-sports.fbx",  118f,  9f, 76f,  1800, -1),
            ("bike",         "Trailblazer",     Vehicle.VehicleClass.Motorcycle, "Bicycle.fbx",        62f,  5f, 95f,   900, -1),
            ("bike-sport",   "Swift Line",      Vehicle.VehicleClass.Motorcycle, "SquareFrameBicycle.fbx", 78f, 6f, 100f, 2400, 3),
            // ---- mid tier ----
            ("van",          "Cargo Van",       Vehicle.VehicleClass.Van,     "van.fbx",               108f,  7f, 64f,  2500,  1),
            ("suv",          "Ridge SUV",       Vehicle.VehicleClass.Pickup,  "suv.fbx",               122f,  8f, 68f,  3200,  2),
            ("taxi",         "Meridian Cab",    Vehicle.VehicleClass.Sedan,   "taxi.fbx",              124f,  9f, 72f,  2600, -1),
            ("delivery",     "Boxline Delivery",Vehicle.VehicleClass.Van,     "delivery.fbx",          104f,  6f, 60f,  2800,  4),
            ("garbage",      "Binhorse",        Vehicle.VehicleClass.Van,     "garbage-truck.fbx",      92f,  5f, 56f,  3400,  6),
            // ---- emergency / special ----
            ("police",       "Patrol Interceptor", Vehicle.VehicleClass.Sedan, "police.fbx",           148f, 11f, 78f,  4800,  5),
            ("ambulance",    "Rescue Rig",      Vehicle.VehicleClass.Van,    "ambulance.fbx",         126f,  8f, 62f,  4200,  5),
            ("firetruck",    "Emberwatch Pumper", Vehicle.VehicleClass.Bus,  "firetruck.fbx",         118f,  7f, 54f,  5600,  8),
            // ---- trucks ----
            ("truck",        "Haulmaster",      Vehicle.VehicleClass.Pickup,  "truck.fbx",             106f,  6f, 54f,  4000,  6),
            ("suv-luxury",   "Foundry Executive", Vehicle.VehicleClass.Pickup, "suv-luxury.fbx",      138f, 10f, 70f,  5200,  7),
            // ---- sports / endgame ----
            ("sedan-sports", "Aurora GT",       Vehicle.VehicleClass.Sports,  "sedan-sports.fbx",      172f, 13f, 85f,  6500,  4),
            ("race",         "Comet Racer",     Vehicle.VehicleClass.Sports,  "race.fbx",              186f, 14f, 88f,  9500,  7),
            ("race-future",  "Ion X",           Vehicle.VehicleClass.Sports,  "race-future.fbx",       204f, 16f, 92f, 14000, 9),
        };

        /// <summary>Palette of 14 factory paint options shown in the garage.</summary>
        public static readonly (string name, Color c)[] Paints =
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

        [MenuItem("VALEBOUND/Build Expanded Vehicle Roster")]
        public static void BuildAll()
        {
            if (!Directory.Exists(PrefabDir)) Directory.CreateDirectory(PrefabDir);
            if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
            int built = 0, skipped = 0;

            foreach (var s in Specs)
            {
                var model = LoadModel(s.model);
                if (model == null)
                {
                    Debug.LogWarning($"[VehicleRoster] model '{s.model}' missing, skipped {s.id}.");
                    skipped++;
                    continue;
                }
                bool isBike = s.cls == Vehicle.VehicleClass.Motorcycle;

                // ---------- vehicle root ----------
                var go = new GameObject($"Vehicle_{s.id}");
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = isBike ? 140f
                        : s.cls == Vehicle.VehicleClass.Bus ? 6000f
                        : s.cls == Vehicle.VehicleClass.Van ? 2400f : 1200f;
                var box = go.AddComponent<BoxCollider>();
                (box.size, box.center) = isBike
                    ? (new Vector3(0.7f, 1.1f, 1.9f), new Vector3(0f, 0.55f, 0f))
                    : s.cls == Vehicle.VehicleClass.Bus
                        ? (new Vector3(2.4f, 2.6f, 7.6f), new Vector3(0f, 1.3f, 0f))
                        : s.cls == Vehicle.VehicleClass.Van
                            ? (new Vector3(2.2f, 2.0f, 5.2f), new Vector3(0f, 1.0f, 0f))
                            : (new Vector3(1.9f, 1.1f, 4.3f), new Vector3(0f, 0f, 0f) + new Vector3(0f, 0.6f, 0f));

                var child = (GameObject)PrefabUtility.InstantiatePrefab(model, go.transform);
                child.transform.localPosition = new Vector3(0f, isBike ? 0.34f : 0.35f, 0f);

                var vc = go.AddComponent<Vehicle.VehicleController>();
                var pv = go.AddComponent<Vehicle.PlayerVehicle>();
                go.AddComponent<Systems.TrafficAIAgent>();

                // ---------- paint skin target ----------
                var skin = go.AddComponent<Vehicle.VehicleSkin>();
                skin.CaptureBodyRenderers(child.transform);

                // ---------- rider rig (bikes) ----------
                if (isBike) BuildBikeRider(go, pv);

                // ---------- audio ----------
                var engine = go.AddComponent<AudioSource>();
                engine.playOnAwake = false;
                engine.loop = true;
                engine.spatialBlend = 1f;
                engine.minDistance = 6f;
                engine.maxDistance = 60f;

                PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/Vehicle_{s.id}.prefab");
                UnityEngine.Object.DestroyImmediate(go);
                built++;
            }

            // ---------- VehicleDefs (data assets) ----------
            foreach (var s in Specs)
            {
                var def = ScriptableObject.CreateInstance<Vehicle.VehicleDef>();
                def.vehicleId = s.id;
                def.displayName = s.name;
                def.vehicleClass = s.cls;
                def.maxSpeedKph = s.maxKph;
                def.acceleration = s.accel;
                def.steeringDegPerSec = s.steer;
                def.price = s.price;
                def.unlockMissionIndex = s.unlockAt;
                def.isBike = s.cls == Vehicle.VehicleClass.Motorcycle;
                string path = $"{DataDir}/VehicleDef_{s.id}.asset";
                if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(def, path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[VehicleRoster] built {built} vehicles ({skipped} skipped), {Specs.Length} defs.");
        }

        /// <summary>Bikes get a Seat child holding a visible-only-when-ridden character model.</summary>
        private static void BuildBikeRider(GameObject go, Vehicle.PlayerVehicle pv)
        {
            var seatGo = new GameObject("Seat");
            seatGo.transform.SetParent(go.transform, false);
            // rider sits on the saddle, hands reaching the bars
            seatGo.transform.localPosition = new Vector3(0f, 0.98f, -0.08f);

            var riderModel = AssetDatabase.LoadAssetAtPath<GameObject>(RiderModel);
            if (riderModel != null)
            {
                var riderChild = (GameObject)PrefabUtility.InstantiatePrefab(riderModel, seatGo.transform);
                riderChild.transform.localPosition = Vector3.zero;
                riderChild.transform.localRotation = Quaternion.identity;
                riderChild.transform.localScale = Vector3.one * 0.95f;
            }
            var riderCtrl = seatGo.AddComponent<Vehicle.BikeRiderController>();
            pv.SetRiderSlot(seatGo.transform);
            pv.SetIsBike(true);
        }

        private static GameObject LoadModel(string file)
        {
            string p = File.Exists($"{CarDir}/{file}") ? $"{CarDir}/{file}"
                     : File.Exists($"{PubDir}/{file}") ? $"{PubDir}/{file}" : null;
            return p == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(p);
        }
    }
}
#endif
