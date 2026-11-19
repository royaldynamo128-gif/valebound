#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Obj = System.ValueTuple<Valebound.ObjectiveType, string, UnityEngine.Vector3, float, float, string, int>;

namespace Valebound.EditorTools
{
    /// <summary>
    /// Builds the 10 story missions + 4 side arcs as MissionDef assets, and the
    /// full UI canvas prefab (menus, HUD, touch controls, map, dialogue).
    /// </summary>
    public static class DataAssetBuilder
    {
        private const string DataDir = "Assets/VALEBOUND/Data";

        [MenuItem("VALEBOUND/Build Data Assets")]
        public static void BuildAll()
        {
            if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
            BuildStoryMissions();
            BuildBiomeSideMissions();
            BuildAchievements();
            BuildGarageEntries();
            AssetDatabase.SaveAssets();
            Debug.Log("[DataAssetBuilder] data assets built.");
        }

        // ---------------- garage entries (17-vehicle roster) ----------------

        private static void BuildGarageEntries()
        {
            // garage entries live on the VehicleGarage component in the scene;
            // DataAssetBuilder creates a manifest the SceneBuilder/WorldBuilder injects.
            var defs = AssetDatabase.FindAssets("t:VehicleDef", new[] { DataDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .ToList();
            var lines = new List<string> { "vehicleId,displayName,price,unlockStory" };
            foreach (var path in defs)
            {
                var def = AssetDatabase.LoadAssetAtPath<Vehicle.VehicleDef>(path);
                if (def == null) continue;
                lines.Add($"{def.vehicleId},{def.displayName},{def.price},{def.unlockMissionIndex}");
            }
            File.WriteAllLines($"{DataDir}/garage_roster.csv", lines);
            Debug.Log($"[DataAssetBuilder] garage roster manifest: {lines.Count - 1} vehicles.");
        }

        // ---------------- biome side missions ----------------

        private class BiomeMissionSpec
        {
            public string id, title, intro, done, giver;
            public int story;
            public (ObjectiveType type, string desc, Vector3 pos, float radius, float time, string vehicle, int count)[] objectives;
            public int credits, xp;
            public string unlockVehicle, achievement;
        }

        // biome city centers — MUST match WorldBuilder BiomeCenters
        private static readonly Vector3 PETAL_CENTER = new Vector3(1800f, 0f, 300f);
        private static readonly Vector3 MIRAGE_CENTER = new Vector3(-1900f, 0f, -1400f);
        private static readonly Vector3 FROST_CENTER = new Vector3(-1900f, 0f, 1400f);
        private static readonly Vector3 EMBER_CENTER = new Vector3(1900f, 0f, -1500f);
        private static readonly Vector3 HQ_BIOME = new Vector3(-60f, 0f, 740f);

        private static void BuildBiomeSideMissions()
        {
            var specs = new[]
            {
                new BiomeMissionSpec
                {
                    id = "s01_petals", title = "Petals on the Wind", giver = "mara", story = -1,
                    intro = "Petal Vale's spring festival needs flowers delivered before the wind scatters them. A bike is the only thing quick enough for the narrow lanes.",
                    done = "The festival queen crowns you 'Honorary Petal' — it's a hat made of blossoms. You keep it.",
                    objectives = new Obj[]
                    {
                        (ObjectiveType.EnterVehicle, "Hop on the guild bike", PETAL_CENTER + new Vector3(0f, 0f, 130f), 8f, 0f, "bike", 1),
                        (ObjectiveType.Deliver, "Deliver flower baskets to the festival stage", PETAL_CENTER, 10f, 0f, null, 1),
                        (ObjectiveType.Collect, "Gather drifting petals for the garland", PETAL_CENTER + new Vector3(20f, 0f, 20f), 14f, 90f, null, 6),
                    },
                    credits = 260, xp = 130, unlockVehicle = null, achievement = null,
                },
                new BiomeMissionSpec
                {
                    id = "s02_dunes", title = "Mirage Run", giver = "cass", story = -1,
                    intro = "Cass needs engine parts from Mirage Basin before the noon heat warps them. Cacti, dunes, and one very straight highway.",
                    done = "Parts delivered. Cass wipes sweat off the last crate: 'Desert driving — you're getting good at this.'",
                    objectives = new Obj[]
                    {
                        (ObjectiveType.DriveTo, "Drive to Mirage Basin depot", MIRAGE_CENTER + new Vector3(0f, 0f, 90f), 12f, 150f, null, 1),
                        (ObjectiveType.Deliver, "Hand the parts to the depot foreman", MIRAGE_CENTER + new Vector3(0f, 0f, 100f), 8f, 0f, null, 1),
                    },
                    credits = 300, xp = 150, unlockVehicle = null, achievement = null,
                },
                new BiomeMissionSpec
                {
                    id = "s03_frost", title = "Cold Chain", giver = "dana", story = -1,
                    intro = "Frostpeak's clinic is out of plasma. Officer Dana clears you an escort run over the ice fields — steady hands, steady wheels.",
                    done = "The clinic director signs the manifest and offers you cocoa. You accept. It's that cold.",
                    objectives = new Obj[]
                    {
                        (ObjectiveType.Meet, "Meet the medic at Frostpeak gate", FROST_CENTER + new Vector3(0f, 0f, 110f), 8f, 0f, null, 1),
                        (ObjectiveType.Deliver, "Deliver plasma to the clinic", FROST_CENTER, 10f, 120f, null, 1),
                        (ObjectiveType.DriveTo, "Return via the frozen lake road", FROST_CENTER + new Vector3(-140f, 0f, -60f), 12f, 0f, null, 1),
                    },
                    credits = 320, xp = 160, unlockVehicle = null, achievement = null,
                },
                new BiomeMissionSpec
                {
                    id = "s04_embers", title = "Ember Watch", giver = "vo", story = -1,
                    intro = "Emberfall's volcano rumbles; the observatory needs sensor data cards swapped before the next tremor. Ash falls like grey snow.",
                    done = "Data cards swapped. The seismograph draws a jagged line — Vo says that means 'not today'.",
                    objectives = new Obj[]
                    {
                        (ObjectiveType.DriveTo, "Drive up the ash road to the observatory", EMBER_CENTER + new Vector3(0f, 0f, -110f), 12f, 0f, null, 1),
                        (ObjectiveType.Collect, "Swap the sensor data cards", EMBER_CENTER + new Vector3(10f, 0f, -120f), 12f, 100f, null, 4),
                        (ObjectiveType.DriveTo, "Get clear of the ash zone", EMBER_CENTER + new Vector3(0f, 0f, 100f), 14f, 90f, null, 1),
                    },
                    credits = 380, xp = 180, unlockVehicle = "bike-sport", achievement = null,
                },
                new BiomeMissionSpec
                {
                    id = "s05_blossom_picnic", title = "The Blossom Picnic", giver = "hale", story = -1,
                    intro = "Dr. Hale wants soil samples from all four biome cities for her climate survey. A scenic, and very long, drive.",
                    done = "Four samples, four biomes, one very complete climate dataset. Hale is thrilled.",
                    objectives = new Obj[]
                    {
                        (ObjectiveType.Collect, "Sample Petal Vale's blossom soil", PETAL_CENTER, 12f, 0f, null, 1),
                        (ObjectiveType.Collect, "Sample Mirage Basin's dune sand", MIRAGE_CENTER, 12f, 0f, null, 1),
                        (ObjectiveType.Collect, "Sample Frostpeak's permafrost", FROST_CENTER, 12f, 0f, null, 1),
                        (ObjectiveType.Collect, "Sample Emberfall's volcanic ash", EMBER_CENTER, 12f, 0f, null, 1),
                    },
                    credits = 500, xp = 240, unlockVehicle = null, achievement = "biome_survey",
                },
                new BiomeMissionSpec
                {
                    id = "s06_founders", title = "The Founders' Relay", giver = "mara", story = -1,
                    intro = "The valley's founding charter must visit every city hall in one day — Port Meridian, Petal Vale, Mirage Basin, Frostpeak, Emberfall. A relay for the history books.",
                    done = "The charter is home. Mara pins the relay medal on your jacket: 'The valley remembers.'",
                    objectives = new Obj[]
                    {
                        (ObjectiveType.Deliver, "Show the charter at Bounders HQ", HQ_BIOME, 10f, 0f, null, 1),
                        (ObjectiveType.Deliver, "Petal Vale city hall", PETAL_CENTER, 12f, 0f, null, 1),
                        (ObjectiveType.Deliver, "Mirage Basin city hall", MIRAGE_CENTER, 12f, 0f, null, 1),
                        (ObjectiveType.Deliver, "Frostpeak city hall", FROST_CENTER, 12f, 0f, null, 1),
                        (ObjectiveType.Deliver, "Emberfall city hall", EMBER_CENTER, 12f, 0f, null, 1),
                    },
                    credits = 900, xp = 420, unlockVehicle = "race-future", achievement = "founders_relay",
                },
            };

            foreach (var s in specs)
            {
                var def = ScriptableObject.CreateInstance<MissionDef>();
                def.missionId = s.id;
                def.title = s.title;
                def.introText = s.intro;
                def.completeText = s.done;
                def.giverNpcId = s.giver;
                def.storyIndex = s.story;
                def.isMainStory = false;
                def.reward.credits = s.credits;
                def.reward.xp = s.xp;
                def.reward.unlockVehicleId = s.unlockVehicle;
                def.reward.achievementId = s.achievement;
                def.requiredPlayerLevel = 2;
                def.objectives = s.objectives.Select(o => new MissionObjective
                {
                    type = o.type,
                    description = o.desc,
                    targetPos = o.pos,
                    radius = o.radius,
                    timeLimit = o.time,
                    vehicleId = o.vehicle,
                    count = o.count,
                }).ToArray();
                string path = $"{DataDir}/{s.id}.asset";
                if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(def, path);
            }
            Debug.Log($"[DataAssetBuilder] built {specs.Length} biome side missions.");
        }

        // ---------------- story missions ----------------

        private class MissionSpec
        {
            public string id, title, intro, done, giver;
            public int story;
            public (ObjectiveType type, string desc, Vector3 pos, float radius, float time, string vehicle, int count)[] objectives;
            public int credits, xp;
            public string unlockVehicle, achievement;
        }

        private static readonly Vector3 HQ = new Vector3(-60f, 0f, 740f);       // guild hq (Port Meridian)
        private static readonly Vector3 PLAZA = new Vector3(0f, 0f, 700f);
        private static readonly Vector3 DINER = new Vector3(-140f, 0f, 820f);
        private static readonly Vector3 STATION = new Vector3(40f, 0f, 880f);
        private static readonly Vector3 STEEL = new Vector3(700f, 0f, -700f);
        private static readonly Vector3 DEPOT = new Vector3(580f, 0f, -760f);
        private static readonly Vector3 YARD = new Vector3(800f, 0f, -620f);
        private static readonly Vector3 PARK = new Vector3(-700f, 0f, 700f);
        private static readonly Vector3 SCHOOL = new Vector3(-780f, 0f, 620f);
        private static readonly Vector3 LAB = new Vector3(860f, 0f, -800f);
        private static readonly Vector3 TOWER = new Vector3(80f, 0f, 640f);
        private static readonly Vector3 DOCKS = new Vector3(-200f, 0f, 540f);
        private static readonly Vector3 RADIO = new Vector3(1200f, 0f, 1200f);
        private static readonly Vector3 LAKE = new Vector3(550f, 0f, 250f);
        private static readonly Vector3 VILLAGE = new Vector3(-650f, 0f, -650f);

        private static MissionSpec[] Specs() => new[]
        {
            new MissionSpec
            {
                id = "m01_homecoming", title = "Homecoming", giver = "mara", story = 0,
                intro = "Rin returns to the old guild hall. Mara is drowning in parcels since Theo vanished. Start with the morning round on foot.",
                done = "First deliveries done. Mara notices Theo's handwriting on the last parcel label.",
                objectives = new Obj[]
                {
                    (ObjectiveType.Goto, "Meet Mara at Bounders HQ", HQ, 6f, 0f, null, 1),
                    (ObjectiveType.Deliver, "Deliver the parcel to the Harbor Diner", DINER, 6f, 0f, null, 1),
                    (ObjectiveType.Deliver, "Deliver a parcel to Grand Plaza", PLAZA, 8f, 0f, null, 1),
                },
                credits = 120, xp = 60, unlockVehicle = null, achievement = "story_1",
            },
            new MissionSpec
            {
                id = "m02_keys", title = "Keys to the Valley", giver = "mara", story = 1,
                intro = "The round is too big on foot. Mara hands you Theo's old sedan and a route card for Willowbrook.",
                done = "The old sedan still runs strong. Mara smiles for the first time in weeks.",
                objectives = new Obj[]
                {
                    (ObjectiveType.EnterVehicle, "Enter Theo's sedan at the guild garage", HQ + new Vector3(14f, 0, 8f), 8f, 0f, "sedan", 1),
                    (ObjectiveType.DriveTo, "Drive to Willowbrook Park", PARK, 10f, 0f, null, 1),
                    (ObjectiveType.Deliver, "Deliver school supplies to Maple Elementary", SCHOOL, 8f, 0f, null, 1),
                },
                credits = 180, xp = 90, unlockVehicle = "hatchback", achievement = "story_2",
            },
            new MissionSpec
            {
                id = "m03_first_contract", title = "The First Contract", giver = "mara", story = 2,
                intro = "A big client wants same-hour freight runs between the station and the steelworks. Prove the guild still has legs.",
                done = "Contract secured. Meridian's courier division just lost its easiest win.",
                objectives = new Obj[]
                {
                    (ObjectiveType.Deliver, "Pick up the freight manifest at Central Station", STATION, 8f, 0f, null, 1),
                    (ObjectiveType.DriveTo, "Drive the freight to Aurora Steelworks", STEEL, 12f, 180f, null, 1),
                    (ObjectiveType.Deliver, "Get the signature at the Rail Depot office", DEPOT, 8f, 0f, null, 1),
                },
                credits = 260, xp = 120, unlockVehicle = "van", achievement = "story_3",
            },
            new MissionSpec
            {
                id = "m04_squeeze", title = "The Squeeze", giver = "cass", story = 3,
                intro = "Meridian is undercutting every guild contract. Cass has a plan: win the Foundry Flats street race and the dock crews will stay loyal.",
                done = "Checkered flag! The dock crews chant 'Bounders' as you coast in.",
                objectives = new Obj[]
                {
                    (ObjectiveType.Meet, "Meet Cass at Silva Salvage", new Vector3(760f, 0f, -560f), 8f, 0f, null, 1),
                    (ObjectiveType.Race, "Win the Foundry Circuit race", STEEL, 14f, 150f, null, 1),
                    (ObjectiveType.DriveTo, "Celebrate at the Container Yard", YARD, 10f, 0f, null, 1),
                },
                credits = 320, xp = 150, unlockVehicle = null, achievement = "story_4",
            },
            new MissionSpec
            {
                id = "m05_raid", title = "Warehouse Raid", giver = "mara", story = 4,
                intro = "A stack of guild parcels has been 'misrouted' to a Meridian warehouse at the lab annex. Get them back before the audit.",
                done = "Parcels recovered — and one of them is addressed to Theo, unopened, from six years ago.",
                objectives = new Obj[]
                {
                    (ObjectiveType.DriveTo, "Drive to the Meridian Lab Annex", LAB, 12f, 0f, null, 1),
                    (ObjectiveType.Collect, "Recover the misrouted guild parcels", LAB + new Vector3(0f, 0f, 10f), 10f, 120f, null, 5),
                    (ObjectiveType.Deliver, "Return the parcels to Bounders HQ", HQ, 10f, 0f, null, 1),
                },
                credits = 380, xp = 170, unlockVehicle = "suv", achievement = "story_5",
            },
            new MissionSpec
            {
                id = "m06_warning", title = "A Friendly Warning", giver = "hale", story = 5,
                intro = "Dr. Imogen Hale slips you a note at the plaza: Meridian's 'Valebound Initiative' is not a logistics project. She wants to talk at the lake.",
                done = "Hale shows you satellite photos: the 'hub' is a land lock. And Theo was investigating it when he vanished.",
                objectives = new Obj[]
                {
                    (ObjectiveType.Meet, "Meet Dr. Hale at Horseshoe Lake", LAKE, 10f, 0f, null, 1),
                    (ObjectiveType.Goto, "Walk with Hale to the overlook", LAKE + new Vector3(0f, 0f, 40f), 6f, 0f, null, 1),
                    (ObjectiveType.Deliver, "Deliver Hale's dossier to the police post", new Vector3(-680f, 0f, 560f), 8f, 0f, null, 1),
                },
                credits = 420, xp = 190, unlockVehicle = null, achievement = "story_6",
            },
            new MissionSpec
            {
                id = "m07_patrol", title = "Officer Ruiz's Favor", giver = "dana", story = 6,
                intro = "Dana needs an extra pair of wheels for community patrol day in Willowbrook: check the school crossing and the market.",
                done = "Patrol complete. Dana owes you one — and mentions Meridian trucks parked illegally at the docks at night.",
                objectives = new Obj[]
                {
                    (ObjectiveType.Meet, "Meet Officer Ruiz at the police post", new Vector3(-680f, 0f, 560f), 8f, 0f, null, 1),
                    (ObjectiveType.DriveTo, "Check the Maple Elementary crossing", SCHOOL, 10f, 0f, null, 1),
                    (ObjectiveType.DriveTo, "Check the Sunday Market stalls", new Vector3(-610f, 0f, 760f), 10f, 0f, null, 1),
                },
                credits = 300, xp = 160, unlockVehicle = null, achievement = "story_7",
            },
            new MissionSpec
            {
                id = "m08_night_run", title = "The Night Run", giver = "vo", story = 7,
                intro = "Vo knows where Theo's last parcel was delivered. Payment: one very illegal, very fast night drive to the radio tower.",
                done = "You deliver a sealed crate to a radio engineer. He taps the box: 'This is Theo's broadcast key.'",
                objectives = new Obj[]
                {
                    (ObjectiveType.Meet, "Meet Vo at the Harbor Docks", DOCKS, 8f, 0f, null, 1),
                    (ObjectiveType.DriveTo, "Night drive to the Radio Tower", RADIO, 14f, 240f, null, 1),
                    (ObjectiveType.Deliver, "Hand the crate to the engineer", RADIO + new Vector3(0f, 0f, 12f), 6f, 0f, null, 1),
                },
                credits = 520, xp = 240, unlockVehicle = "sedan-sports", achievement = "story_8",
            },
            new MissionSpec
            {
                id = "m09_broadcast", title = "The Broadcast", giver = "hale", story = 8,
                intro = "Hale has the evidence. Theo's key can push it over every speaker in the valley — but the tower is a two-person job.",
                done = "Static. Then Theo's voice, timestamped yesterday: 'If you hear this, the valley still has Bounders. Come find me at the old mill.'",
                objectives = new Obj[]
                {
                    (ObjectiveType.Meet, "Pick up Dr. Hale at the lake", LAKE, 8f, 0f, null, 1),
                    (ObjectiveType.DriveTo, "Drive to the Radio Tower with Hale", RADIO, 14f, 200f, null, 1),
                    (ObjectiveType.SurviveTimer, "Hold the line while the broadcast uploads", RADIO, 20f, 45f, null, 45),
                },
                credits = 650, xp = 300, unlockVehicle = null, achievement = "story_9",
            },
            new MissionSpec
            {
                id = "m10_finale", title = "Valebound", giver = "mara", story = 9,
                intro = "Theo is at the old mill by Millbrook Village. Meridian's recovery squad is already on the way. This is the last delivery.",
                done = "Theo is safe, the evidence is public, and the valley stays Valebound. Epilogue unlocked — free roam forever.",
                objectives = new Obj[]
                {
                    (ObjectiveType.Meet, "Meet Mara and the convoy at the village", VILLAGE, 12f, 0f, null, 1),
                    (ObjectiveType.DriveTo, "Race Meridian to the Old Mill", new Vector3(-570f, 0f, -710f), 12f, 160f, null, 1),
                    (ObjectiveType.Deliver, "Bring Theo home to Bounders HQ", HQ, 12f, 0f, null, 1),
                },
                credits = 1200, xp = 600, unlockVehicle = "race", achievement = "story_10",
            },
        };

        private static void BuildStoryMissions()
        {
            foreach (var s in Specs())
            {
                var def = ScriptableObject.CreateInstance<MissionDef>();
                def.missionId = s.id;
                def.title = s.title;
                def.introText = s.intro;
                def.completeText = s.done;
                def.giverNpcId = s.giver;
                def.storyIndex = s.story;
                def.isMainStory = true;
                def.reward.credits = s.credits;
                def.reward.xp = s.xp;
                def.reward.unlockVehicleId = s.unlockVehicle;
                def.reward.achievementId = s.achievement;
                def.requiredPlayerLevel = Mathf.Clamp(1 + s.story / 2, 1, 8);
                def.requiresMissionId = s.story > 0 ? $"m{s.story:00}_" : null;
                def.objectives = s.objectives.Select(o => new MissionObjective
                {
                    type = o.type,
                    description = o.desc,
                    targetPos = o.pos,
                    radius = o.radius,
                    timeLimit = o.time,
                    vehicleId = o.vehicle,
                    count = o.count,
                }).ToArray();
                AssetDatabase.CreateAsset(def, $"{DataDir}/{s.id}.asset");
            }
            Debug.Log($"[DataAssetBuilder] built {Specs().Length} story missions.");
        }

        // ---------------- achievements ----------------

        private static void BuildAchievements()
        {
            // achievements list is a field on the AchievementSystem component in the scene;
            // we generate a JSON manifest for reference and the SceneBuilder injects it.
            var list = new[]
            {
                ("story_1", "Homecoming", "Complete the first story mission."),
                ("story_5", "Raiders of the Lost Parcels", "Recover the misrouted guild parcels."),
                ("story_10", "Valebound", "Finish the story."),
                ("courier_5", "Regular Route", "Complete 5 courier jobs."),
                ("courier_25", "Valley Legend", "Complete 25 courier jobs."),
                ("race_first", "Checkered Flag", "Win any race."),
                ("race_sub90", "Street Legal", "Finish a race under 90 seconds."),
                ("race_sub75", "Hot Lap", "Finish a race under 75 seconds."),
                ("race_sub60", "Valley Record", "Finish a race under 60 seconds."),
                ("shard_10", "Signal Seeker", "Collect 10 signal shards."),
                ("shard_30", "Signal Hunter", "Collect 30 signal shards."),
                ("shard_all", "Full Signal", "Collect all 60 signal shards."),
                ("explorer_20", "Local Guide", "Discover 20 locations."),
                ("explorer_45", "Cartographer", "Discover 45 locations."),
                ("photographer", "Snapshot", "Photograph a vista."),
                ("drive_marathon", "Marathon Driver", "Drive 42.2 km total."),
                ("level_10", "Professional", "Reach level 10."),
                ("rich", "Guild Banker", "Hold 10,000 credits at once."),
                ("biome_survey", "Climate Study", "Complete Dr. Hale's four-biome soil survey."),
                ("founders_relay", "The Founders' Relay", "Carry the charter to all five city halls."),
                ("taxi_10", "Valley Cabbie", "Complete 10 taxi passenger jobs."),
                ("big_air", "Big Air", "Land a jump with more than 6 m of drop."),
                ("airtime_30", "CloudSurfer", "Bank 30 seconds of total air time."),
                ("daily_triple", "Clean Sweep", "Complete all three daily challenges."),
                ("biome_tourist", "Continental Tourist", "Visit all four biome cities."),
            };
            string json = string.Join("\n", list.Select(a => $"\"{a.Item1}\",\"{a.Item2}\",\"{a.Item3}\""));
            File.WriteAllText($"{DataDir}/achievements.csv", "id,title,description\n" + json.Replace("\n", "\n"));
            Debug.Log($"[DataAssetBuilder] achievements manifest: {list.Length} entries.");
        }
    }
}
#endif
