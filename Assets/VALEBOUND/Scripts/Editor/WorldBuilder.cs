#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Valebound.Systems;

namespace Valebound.EditorTools
{
    /// <summary>
    /// VALEBOUND WorldBuilder — one-click deterministic world generation.
    ///
    /// Generates:
    ///  - 4x4 km terrain (heightmap from layered noise, sea + lakes carved)
    ///  - 3 cities + village on flattened pads
    ///  - road network (grid streets in cities, spline highways between)
    ///  - buildings, props, streetlights, trees, POIs from curated packs
    ///  - NavMesh, road graph asset, POI table, shard & race data
    ///  - minimap texture bake
    ///
    /// Everything is seeded: same seed = same world.
    /// </summary>
    public class WorldBuilder : EditorWindow
    {
        // ---------------- config ----------------
        private const int WorldSize = 6144;          // meters (6km x 6km)
        private const int HeightmapRes = 513;
        private const float MaxTerrainHeight = 220f;
        private const int CityGridStep = 24;         // meters between street nodes in city
        private const float StreetTileSize = 2f;     // quaternius street tile = 2m
        private const int Seed = 20261114;
        private const string PrefabDirRel = "Assets/VALEBOUND/Prefabs";

        private static readonly string[] CityNames = { "Port Meridian", "Foundry Flats", "Willowbrook" };

        // core cities (original triangle)
        private static readonly Vector2[] CityCenters =
        {
            new Vector2(0f, 700f),      // Port Meridian (north-center)
            new Vector2(700f, -700f),   // Foundry Flats (south-east)
            new Vector2(-700f, 700f),   // Willowbrook (north-west)
        };
        private static readonly float[] CityRadii = { 300f, 240f, 260f };
        private static readonly int[] CityGridHalf = { 12, 10, 11 };   // half-extent in grid steps

        private static readonly Vector2 VillageCenter = new Vector2(-650f, -650f);
        private const float VillageRadius = 110f;

        // ---- biome cities (expansion) ----
        // Petal Vale (cherry blossom, spring), Mirage Basin (desert), Frostpeak (winter),
        // Emberfall (volcano). Centers match DataAssetBuilder biome mission positions.
        private static readonly (string name, Vector2 center, float radius, int gridHalf, BiomeZone.Biome biome, string theme)[] BiomeCities =
        {
            ("Petal Vale",   new Vector2(1800f, 300f),   260f, 10, BiomeZone.Biome.Petal,  "blossom"),
            ("Mirage Basin", new Vector2(-1900f, -1400f), 280f, 11, BiomeZone.Biome.Desert, "desert"),
            ("Frostpeak",    new Vector2(-1900f, 1400f),  240f,  9, BiomeZone.Biome.Frost,  "winter"),
            ("Emberfall",    new Vector2(1900f, -1500f),  260f, 10, BiomeZone.Biome.Ember,  "volcano"),
        };

        // ---------------- UI ----------------
        private int seed = Seed;
        private bool generateTerrain = true;
        private bool generateRoads = true;
        private bool generateBuildings = true;
        private bool generateNature = true;
        private bool generatePois = true;
        private bool bakeNavMesh = true;
        private bool bakeMinimap = true;
        private bool buildDataAssets = true;

        [MenuItem("VALEBOUND/World Builder")]
        public static void Open() => GetWindow<WorldBuilder>("World Builder");

        /// <summary>Non-interactive entry for BuildScript (skips confirm dialog).</summary>
        public void GenerateFromBuildScript()
        {
            GenerateAll(skipConfirm: true);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("VALEBOUND World Builder", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Generates the full Aurora Valley world into the open scene.\n" +
                "Run on an empty scene. Deterministic for a given seed.",
                MessageType.Info);
            seed = EditorGUILayout.IntField("Seed", seed);
            generateTerrain = EditorGUILayout.Toggle("Terrain", generateTerrain);
            generateRoads = EditorGUILayout.Toggle("Roads & Highways", generateRoads);
            generateBuildings = EditorGUILayout.Toggle("Buildings & Props", generateBuildings);
            generateNature = EditorGUILayout.Toggle("Nature (trees, rocks, water)", generateNature);
            generatePois = EditorGUILayout.Toggle("POIs / Shards / Races", generatePois);
            bakeNavMesh = EditorGUILayout.Toggle("Bake NavMesh", bakeNavMesh);
            bakeMinimap = EditorGUILayout.Toggle("Bake Minimap Texture", bakeMinimap);
            buildDataAssets = EditorGUILayout.Toggle("Build Data Assets", buildDataAssets);

            EditorGUILayout.Space();
            if (GUILayout.Button("GENERATE WORLD", GUILayout.Height(36)))
            {
                GenerateAll();
            }
        }

        // ================= main =================

        private void GenerateAll(bool skipConfirm = false)
        {
            if (!skipConfirm && !EditorUtility.DisplayDialog("Generate World",
                "This will build the entire Aurora Valley world in the current scene.\nContinue?",
                "Generate", "Cancel")) return;

            try
            {
                EditorUtility.DisplayProgressBar("WorldBuilder", "Preparing...", 0f);
                var rand = new System.Random(seed);
                var root = new GameObject("WORLD");
                var terrainRoot = new GameObject("Terrain");
                terrainRoot.transform.SetParent(root.transform, false);
                var roadsRoot = new GameObject("Roads");
                roadsRoot.transform.SetParent(root.transform, false);
                var cityRoot = new GameObject("Cities");
                cityRoot.transform.SetParent(root.transform, false);
                var natureRoot = new GameObject("Nature");
                natureRoot.transform.SetParent(root.transform, false);
                var poiRoot = new GameObject("POIs");
                poiRoot.transform.SetParent(root.transform, false);
                var sysRoot = new GameObject("Systems");
                sysRoot.transform.SetParent(root.transform, false);

                Terrain terrain = null;
                RoadNetworkData net = null;

                if (generateTerrain)
                {
                    EditorUtility.DisplayProgressBar("WorldBuilder", "Terrain...", 0.05f);
                    terrain = GenerateTerrain(terrainRoot, seed);
                }

                if (generateRoads)
                {
                    EditorUtility.DisplayProgressBar("WorldBuilder", "Road network graph...", 0.15f);
                    net = ScriptableObject.CreateInstance<RoadNetworkData>();
                    BuildCityGrids(net);
                    BuildHighways(net);
                    net.worldBounds = new Bounds(Vector3.zero, new Vector3(WorldSize, 400f, WorldSize));

                    EditorUtility.DisplayProgressBar("WorldBuilder", "Laying road tiles...", 0.25f);
                    LayRoadTiles(net, roadsRoot.transform);
                }

                if (generateBuildings)
                {
                    EditorUtility.DisplayProgressBar("WorldBuilder", "Cities...", 0.45f);
                    foreach (int i in new[] { 0, 1, 2 }) GenerateCity(i, cityRoot.transform, rand);
                    GenerateVillage(cityRoot.transform, rand);
                    for (int i = 0; i < BiomeCities.Length; i++) GenerateBiomeCity(i, cityRoot.transform, rand);
                }

                if (generateNature)
                {
                    EditorUtility.DisplayProgressBar("WorldBuilder", "Nature...", 0.65f);
                    GenerateNature(natureRoot.transform, rand, terrain);
                }

                if (generatePois)
                {
                    EditorUtility.DisplayProgressBar("WorldBuilder", "POIs...", 0.75f);
                    GeneratePOIs(poiRoot.transform, net, rand);
                }

                EditorUtility.DisplayProgressBar("WorldBuilder", "Systems...", 0.85f);
                SetupSystems(sysRoot.transform, net);

                if (buildDataAssets)
                {
                    EditorUtility.DisplayProgressBar("WorldBuilder", "Data assets...", 0.9f);
                    DataAssetBuilder.BuildAll();
                }

                if (bakeNavMesh)
                {
                    EditorUtility.DisplayProgressBar("WorldBuilder", "NavMesh (this takes a while)...", 0.93f);
                    BakeNavMesh();
                }

                if (bakeMinimap)
                {
                    EditorUtility.DisplayProgressBar("WorldBuilder", "Minimap...", 0.97f);
                    BakeMinimapTexture();
                }

                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Debug.Log("[WorldBuilder] World generation complete.");
        }

        // ================= terrain =================

        private Terrain GenerateTerrain(GameObject parent, int s)
        {
            var data = new TerrainData
            {
                heightmapResolution = HeightmapRes,
                size = new Vector3(WorldSize, MaxTerrainHeight, WorldSize),
                alphamapResolution = 256,
                baseMapResolution = 512,
            };

            // splat: grass, rock, sand, snow, ash — biome-aware palette
            data.splatPrototypes = new[]
            {
                new SplatPrototype { texture = LoadTex("Art/Textures/T_Grass"), tileSize = new Vector2(24, 24) },
                new SplatPrototype { texture = LoadTex("Art/Textures/T_Rock"), tileSize = new Vector2(30, 30) },
                new SplatPrototype { texture = LoadTex("Art/Textures/T_Sand"), tileSize = new Vector2(20, 20) },
                new SplatPrototype { texture = LoadTex("Art/Textures/T_Snow"), tileSize = new Vector2(26, 26) },
                new SplatPrototype { texture = LoadTex("Art/Textures/T_Ash"), tileSize = new Vector2(22, 22) },
            };
            data.treePrototypes = Array.Empty<TreePrototype>();
            data.detailPrototypes = Array.Empty<DetailPrototype>();

            float[,] heights = new float[HeightmapRes, HeightmapRes];
            var rng = new System.Random(s);
            // layered value noise
            float[,] noise = GenerateValueNoise(HeightmapRes, 12, 0.55f, rng);
            float[,] detail = GenerateValueNoise(HeightmapRes, 60, 0.3f, rng);
            float[,] ridge = RidgedNoise(HeightmapRes, 7, rng);

            // world-space helpers
            float Half = WorldSize * 0.5f;
            for (int z = 0; z < HeightmapRes; z++)
            {
                for (int x = 0; x < HeightmapRes; x++)
                {
                    float wx = Mathf.Lerp(-Half, Half, x / (float)(HeightmapRes - 1));
                    float wz = Mathf.Lerp(-Half, Half, z / (float)(HeightmapRes - 1));

                    float baseH = noise[z, x] * 0.55f + detail[z, x] * 0.12f;
                    // mountains ring the rim
                    float rim = Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz)) / Half; // 0 center -> 1 edge
                    float mountainMask = Mathf.SmoothStep(0.78f, 0.98f, rim);
                    baseH = Mathf.Lerp(baseH * 0.18f, Mathf.Max(baseH, ridge[z, x]), mountainMask);

                    // biome terrain shaping:
                    // Emberfall: volcanic cone rises AROUND the town (town disk stays low;
                    // the city pad below flattens the inside, so the cone reads as surrounding ridges)
                    var ec = BiomeCities[3].center;
                    float dEmber = Dist(wx, wz, ec.x, ec.y);
                    if (dEmber < 620f && dEmber > BiomeCities[3].radius)
                    {
                        float ring = Mathf.Sin(Mathf.Clamp01((dEmber - BiomeCities[3].radius) / (620f - BiomeCities[3].radius)) * Mathf.PI);
                        baseH += ring * 0.42f + ridge[z, x] * 0.10f * ring;
                    }
                    // Frostpeak: ridged peaks NW, but the town disk itself stays at 0
                    var fc = BiomeCities[2].center;
                    float dFrost = Dist(wx, wz, fc.x, fc.y);
                    if (dFrost < 700f && dFrost > BiomeCities[2].radius)
                    {
                        float t = Mathf.SmoothStep(700f, BiomeCities[2].radius, dFrost);
                        baseH = Mathf.Max(baseH, (0.18f + ridge[z, x] * 0.32f) * t);
                    }
                    // Mirage Basin: wide flat dune bowl SW
                    var mc = BiomeCities[1].center;
                    float dMirage = Dist(wx, wz, mc.x, mc.y);
                    if (dMirage < 800f)
                    {
                        float dune = (Mathf.Sin(wx * 0.012f) + Mathf.Cos(wz * 0.014f)) * 0.02f;
                        baseH = Mathf.Lerp(baseH, 0.045f + dune, Mathf.SmoothStep(800f, 400f, dMirage));
                    }
                    // Petal Vale: gentle rolling meadow E
                    var pc = BiomeCities[0].center;
                    float dPetal = Dist(wx, wz, pc.x, pc.y);
                    if (dPetal < 700f)
                    {
                        baseH = Mathf.Lerp(baseH, 0.05f + detail[z, x] * 0.05f, Mathf.SmoothStep(700f, 300f, dPetal));
                    }

                    // flatten city pads & highway corridors
                    float h = FlattenNearCities(baseH, wx, wz);

                    // sea in the far south-east corner + lakes
                    float lakeA = Dist(wx, wz, 550f, 250f) - 190f;  // Horseshoe Lake
                    float lakeB = Dist(wx, wz, -250f, -250f) - 120f; // Cedar Pond
                    float sea = wz - (Half - 260f);                    // sea south edge
                    float water = Mathf.Min(Mathf.Min(lakeA, lakeB), sea);
                    if (water < 0f) h = Mathf.Min(h, -0.06f);

                    heights[z, x] = h * 0.9f;
                }
            }
            data.SetHeights(0, 0, heights);

            // splatmap: biome-aware ground cover
            float[,,] alphamaps = new float[256, 256, 5];
            for (int z = 0; z < 256; z++)
            for (int x = 0; x < 256; x++)
            {
                float h = heights[z * 2, x * 2];
                float wx = Mathf.Lerp(-Half, Half, x / 255f);
                float wz = Mathf.Lerp(-Half, Half, z / 255f);
                float rock = Mathf.SmoothStep(0.45f, 0.75f, h);
                float sand = Mathf.SmoothStep(-0.02f, -0.1f, h);
                float grass = 1f - Mathf.Max(rock, sand);

                // biome overrides (zone-based so flat town disks still read correctly)
                float snow = 0f, ash = 0f;
                float dMirage = Dist(wx, wz, BiomeCities[1].center.x, BiomeCities[1].center.y);
                float dFrost = Dist(wx, wz, BiomeCities[2].center.x, BiomeCities[2].center.y);
                float dEmber = Dist(wx, wz, BiomeCities[3].center.x, BiomeCities[3].center.y);
                // frost: snow covers the whole region (not height-keyed — town disk is flat)
                snow = Mathf.SmoothStep(650f, 350f, dFrost);
                // ember: ash around the volcano region
                ash = Mathf.SmoothStep(650f, 300f, dEmber) * 0.9f;
                // mirage: sand overrides grass across the desert region
                float mirageSand = Mathf.SmoothStep(750f, 350f, dMirage);
                // petal: warmer grass handled by texture only

                float w0 = grass * (1f - snow) * (1f - ash) * (1f - mirageSand);
                float w1 = rock * (1f - snow) * (1f - ash);
                float w2 = Mathf.Max(sand, mirageSand * (1f - ash));
                float w3 = snow * (1f - ash);
                float w4 = ash;
                float total = w0 + w1 + w2 + w3 + w4;
                if (total < 0.01f) { w0 = 1f; total = 1f; }
                alphamaps[z, x, 0] = w0 / total;
                alphamaps[z, x, 1] = w1 / total;
                alphamaps[z, x, 2] = w2 / total;
                alphamaps[z, x, 3] = w3 / total;
                alphamaps[z, x, 4] = w4 / total;
            }
            data.SetAlphamaps(0, 0, alphamaps);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "AuroraValley_Terrain";
            go.transform.SetParent(parent.transform, false);
            go.transform.position = new Vector3(-Half, 0f, -Half);

            // terrain collider comes with the terrain
            return go.GetComponent<Terrain>();
        }

        private float FlattenNearCities(float h, float wx, float wz)
        {
            // city pads — flatten to 0 so the whole settlement (placed at y=0) is grounded
            for (int i = 0; i < 3; i++)
            {
                float d = Dist(wx, wz, CityCenters[i].x, CityCenters[i].y);
                float pad = Mathf.SmoothStep(CityRadii[i] + 90f, CityRadii[i], d);
                h = Mathf.Lerp(h, 0f, pad);
            }
            // biome city pads — all flatten to 0 so roads/buildings at y=0 sit ON the ground
            for (int i = 0; i < BiomeCities.Length; i++)
            {
                var bc = BiomeCities[i];
                float d = Dist(wx, wz, bc.center.x, bc.center.y);
                float pad = Mathf.SmoothStep(bc.radius + 110f, bc.radius, d);
                h = Mathf.Lerp(h, 0f, pad);
            }
            // village pad
            float dv = Dist(wx, wz, VillageCenter.x, VillageCenter.y);
            h = Mathf.Lerp(h, 0f, Mathf.SmoothStep(VillageRadius + 60f, VillageRadius, dv));
            // highway corridors M1 (x=0) & M2 (z=0 partially) — flatten gently
            float m1 = Mathf.Abs(wx) < 26f && Mathf.Abs(wz) < 1200f
                ? Mathf.SmoothStep(26f, 8f, Mathf.Abs(wx)) : 0f;
            h = Mathf.Lerp(h, 0.035f, m1);
            return h;
        }

        private static float Dist(float x, float z, float cx, float cz) =>
            Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));

        private static float[,] GenerateValueNoise(int res, int cells, float persistence, System.Random rng)
        {
            // multi-octave value noise on a grid
            int octaves = 5;
            float[,] result = new float[res, res];
            float amp = 1f, total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                int c = cells << o;
                float[,] grid = new float[c + 1, c + 1];
                for (int z = 0; z <= c; z++)
                    for (int x = 0; x <= c; x++)
                        grid[z, x] = (float)rng.NextDouble();
                for (int z = 0; z < res; z++)
                {
                    float gz = z / (res - 1f) * c;
                    int z0 = Mathf.FloorToInt(gz), z1 = Mathf.Min(c, z0 + 1);
                    float fz = Mathf.SmoothStep(0, 1, gz - z0);
                    for (int x = 0; x < res; x++)
                    {
                        float gx = x / (res - 1f) * c;
                        int x0 = Mathf.FloorToInt(gx), x1 = Mathf.Min(c, x0 + 1);
                        float fx = Mathf.SmoothStep(0, 1, gx - x0);
                        float v = Mathf.Lerp(
                            Mathf.Lerp(grid[z0, x0], grid[z0, x1], fx),
                            Mathf.Lerp(grid[z1, x0], grid[z1, x1], fx), fz);
                        result[z, x] += v * amp;
                    }
                }
                total += amp;
                amp *= persistence;
            }
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    result[z, x] /= total;
            return result;
        }

        private static float[,] RidgedNoise(int res, int cells, System.Random rng)
        {
            float[,] n = GenerateValueNoise(res, cells, 0.5f, rng);
            float[,] r = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    r[z, x] = 1f - Mathf.Abs(n[z, x] * 2f - 1f); // sharp ridges
            return r;
        }

        // ================= roads =================

        private void BuildCityGrids(RoadNetworkData net)
        {
            for (int c = 0; c < 3 + BiomeCities.Length; c++)
            {
                int half;
                float cx, cz;
                if (c < 3)
                {
                    half = CityGridHalf[c];
                    cx = CityCenters[c].x; cz = CityCenters[c].y;
                }
                else
                {
                    var bc = BiomeCities[c - 3];
                    half = bc.gridHalf;
                    cx = bc.center.x; cz = bc.center.y;
                }
                int step = CityGridStep;
                int[,] idMap = new int[half * 2 + 1, half * 2 + 1];
                for (int gz = -half; gz <= half; gz++)
                for (int gx = -half; gx <= half; gx++)
                {
                    var node = new RoadNetworkData.Node
                    {
                        id = net.nodes.Count,
                        x = cx + gx * step,
                        z = cz + gz * step,
                        y = 0.2f,
                        isIntersection = (gx % 3 == 0 && gz % 3 == 0),
                        isHighway = false,
                        connections = new List<int>(),
                    };
                    idMap[gx + half, gz + half] = node.id;
                    net.nodes.Add(node);
                }
                // connect grid neighbors
                for (int gz = -half; gz <= half; gz++)
                for (int gx = -half; gx <= half; gx++)
                {
                    int id = idMap[gx + half, gz + half];
                    if (gx < half) Connect(net, id, idMap[gx + 1 + half, gz + half], 30f, false);
                    if (gz < half) Connect(net, id, idMap[gx + half, gz + 1 + half], 30f, false);
                }
            }
        }

        private void BuildHighways(RoadNetworkData net)
        {
            // M1: north-south at x = 0, z from -1600 to 1600
            int prev = -1;
            for (float z = -1600f; z <= 1600f; z += 80f)
            {
                int id = net.nodes.Count;
                net.nodes.Add(new RoadNetworkData.Node
                {
                    id = id, x = 0f, z = z, y = 0.35f, isIntersection = false, isHighway = true,
                    connections = new List<int>(),
                });
                if (prev >= 0) Connect(net, prev, id, 90f, true);
                prev = id;
            }
            // M2: east-west at z = -100 (Foundry spur), x -1600..1600
            prev = -1;
            for (float x = -1600f; x <= 1600f; x += 80f)
            {
                int id = net.nodes.Count;
                net.nodes.Add(new RoadNetworkData.Node
                {
                    id = id, x = x, z = -100f, y = 0.35f, isIntersection = false, isHighway = true,
                    connections = new List<int>(),
                });
                if (prev >= 0) Connect(net, prev, id, 90f, true);
                prev = id;
            }
            // spurs: connect each city center to nearest highway node
            foreach (var cc in CityCenters)
            {
                int cityNode = net.NearestNode(new Vector3(cc.x, 0f, cc.y));
                int hwNode = net.NearestNode(new Vector3(cc.x, 0f, cc.y), highwaysOnly: true);
                if (cityNode >= 0 && hwNode >= 0) Connect(net, cityNode, hwNode, 70f, true);
            }
            // biome ring road: M3 loop hitting all four biome cities
            // Petal (E) -> Ember (SE) -> Mirage (SW) -> Frost (NW) -> back to M1 north
            Vector2[] loop =
            {
                new Vector2(0f, 1000f),                       // join M1 north
                BiomeCities[0].center + new Vector2(-300f, 300f), // Petal approach
                BiomeCities[0].center,
                new Vector2(1500f, -600f),
                BiomeCities[3].center + new Vector2(-300f, 300f), // Ember approach
                BiomeCities[3].center,
                new Vector2(0f, -1500f),                      // south join
                BiomeCities[1].center + new Vector2(300f, 300f), // Mirage approach
                BiomeCities[1].center,
                new Vector2(-1500f, 0f),
                BiomeCities[2].center + new Vector2(300f, -300f), // Frost approach
                BiomeCities[2].center,
                new Vector2(-1000f, 1000f),
                new Vector2(0f, 1000f),                       // close loop
            };
            int prevLoop = -1;
            foreach (var pt in loop)
            {
                int id = net.nodes.Count;
                net.nodes.Add(new RoadNetworkData.Node
                {
                    id = id, x = pt.x, z = pt.y, y = 0.35f, isIntersection = false, isHighway = true,
                    connections = new List<int>(),
                });
                if (prevLoop >= 0) Connect(net, prevLoop, id, 90f, true);
                prevLoop = id;
            }
        }

        private static void Connect(RoadNetworkData net, int a, int b, float speed, bool highway)
        {
            float len = Vector3.Distance(net.NodePos(a), net.NodePos(b));
            net.edges.Add(new RoadNetworkData.Edge { from = a, to = b, length = len, speedLimitKph = speed, isHighway = highway });
            net.nodes[a].connections.Add(b);
            if (!highway) net.nodes[b].connections.Add(a);
        }

        private void LayRoadTiles(RoadNetworkData net, Transform parent)
        {
            // Combine all street tile meshes into a few big combined meshes per material:
            // massively fewer draw calls than placing tile prefabs.
            // For simplicity & determinism we place prefab references and rely on static batching.
            var straight = LoadModel("Art/Models/Streets/Quaternius Street Pack/Street_Straight");
            var curve = LoadModel("Art/Models/Streets/Quaternius Street Pack/Street_Curve");
            var highwayStraight = LoadModel("Art/Models/Roads/Kenney City Kit Roads/road-straight"); // fallback names resolved in ImportConfigurator
            var fourWay = LoadModel("Art/Models/Streets/Quaternius Street Pack/Street_4Way");
            var light = LoadModel("Art/Models/Streets/Quaternius Street Pack/Streetlight_Single");

            if (straight == null)
            {
                Debug.LogWarning("[WorldBuilder] street tiles not found; skipping tile laying (graph still built).");
                return;
            }

            var tilesRoot = new GameObject("StreetTiles").transform;
            tilesRoot.SetParent(parent, false);
            int placed = 0;

            // parked-car prefab pool (light traffic variety, static dressing only)
            var parkedPrefabs = new List<GameObject>();
            foreach (var id in new[] { "sedan", "hatchback", "van", "suv", "taxi", "police" })
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/VALEBOUND/Prefabs/Vehicles/Vehicle_{id}.prefab");
                if (pf != null) parkedPrefabs.Add(pf);
            }

            foreach (var e in net.edges)
            {
                Vector3 a = net.NodePos(e.from);
                Vector3 b = net.NodePos(e.to);
                Vector3 dir = b - a; dir.y = 0f;
                float len = dir.magnitude;
                if (len < 0.01f) continue;
                dir.Normalize();
                Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

                int tileCount = Mathf.Max(1, Mathf.RoundToInt(len / StreetTileSize));
                var proto = e.isHighway ? (highwayStraight != null ? highwayStraight : straight) : straight;

                for (int i = 0; i < tileCount; i++)
                {
                    Vector3 pos = a + dir * (i + 0.5f) * (len / tileCount);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(proto, tilesRoot);
                    go.transform.SetPositionAndRotation(pos + Vector3.up * 0.02f, rot);
                    go.isStatic = true;
                    placed++;
                }

                // streetlights every ~24 m on city streets
                if (!e.isHighway && light != null)
                {
                    for (float d = 8f; d < len; d += 24f)
                    {
                        Vector3 pos = a + dir * d + new Vector3(-dir.z, 0f, dir.x) * 2.2f;
                        var lgo = (GameObject)PrefabUtility.InstantiatePrefab(light, tilesRoot);
                        lgo.transform.SetPositionAndRotation(pos, rot);
                        lgo.isStatic = true;
                    }
                }

                // parked cars: kerbside slots on ~30% of city street edges
                if (!e.isHighway && parkedPrefabs.Count > 0)
                {
                    var rngPark = new System.Random(e.from * 7919 + e.to * 104729);
                    if (rngPark.NextDouble() < 0.30)
                    {
                        for (float d = 6f; d < len - 6f; d += 7f)
                        {
                            if (rngPark.NextDouble() < 0.45) continue;   // gaps
                            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
                            Vector3 pos = a + dir * d + side * 2.9f;
                            var carProto = parkedPrefabs[rngPark.Next(parkedPrefabs.Count)];
                            var pgo = (GameObject)PrefabUtility.InstantiatePrefab(carProto, tilesRoot);
                            pgo.transform.SetPositionAndRotation(pos + Vector3.up * 0.02f, rot);
                            // strip physics/AI: these are static dressing
                            foreach (var comp in pgo.GetComponentsInChildren<Component>(true))
                            {
                                if (comp is Rigidbody || comp is Vehicle.VehicleController
                                    || comp is Vehicle.PlayerVehicle || comp is Systems.TrafficAIAgent
                                    || comp is Vehicle.VehicleSkin || comp is AudioSource)
                                    UnityEngine.Object.DestroyImmediate(comp, true);
                            }
                            pgo.isStatic = true;
                        }
                    }
                }
            }
            Debug.Log($"[WorldBuilder] placed {placed} street tiles.");
        }

        // ================= cities =================

        private void GenerateCity(int index, Transform parent, System.Random rand)
        {
            string name = CityNames[index];
            var cityT = new GameObject(name).transform;
            cityT.SetParent(parent, false);
            cityT.position = new Vector3(CityCenters[index].x, 0f, CityCenters[index].y);

            float radius = CityRadii[index];
            int half = CityGridHalf[index];
            float step = CityGridStep;

            // per-city model pools
            var models = CityModelPool(index);

            int placed = 0;
            for (int gz = -half; gz <= half; gz++)
            for (int gx = -half; gx <= half; gx++)
            {
                // place buildings in blocks, skip street lines (streets on multiples of 3)
                bool onStreetLine = gx % 3 == 0 || gz % 3 == 0;
                if (onStreetLine) continue;

                Vector3 local = new Vector3(gx * step, 0f, gz * step);
                float distFromCenter = local.magnitude;
                if (distFromCenter > radius) continue;

                if (rand.NextDouble() < 0.22) continue; // breathing room

                var model = models[rand.Next(models.Count)];
                if (model == null) continue;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, cityT);
                go.transform.localPosition = local + new Vector3(
                    (float)(rand.NextDouble() - 0.5) * 1.2f, 0f, (float)(rand.NextDouble() - 0.5) * 1.2f);
                // street-frontage: face the nearest street line (rows between multiples of 3)
                float frontYaw = Mathf.Abs(gx % 3) == 1 ? 90f : 0f;
                go.transform.localRotation = Quaternion.Euler(0f, frontYaw + (float)(rand.NextDouble() - 0.5) * 6f, 0f);
                go.isStatic = true;
                placed++;
            }
            Debug.Log($"[WorldBuilder] {name}: {placed} buildings.");
        }

        private List<GameObject> CityModelPool(int cityIndex)
        {
            var pool = new List<GameObject>();
            switch (cityIndex)
            {
                case 0: // Port Meridian — downtown: modular buildings + skyscraper-ish
                    pool.AddRange(LoadModels("Art/Models/Buildings/Kenney Modular Buildings", "building"));
                    pool.AddRange(LoadModels("Art/Models/Buildings/Kenney Building Kit", "building"));
                    break;
                case 1: // Foundry Flats — industrial
                    pool.AddRange(LoadModels("Art/Models/Industrial/Kenney Train Kit", "warehouse"));
                    pool.AddRange(LoadModels("Art/Models/Industrial/Kenney Train Kit", "factory"));
                    pool.AddRange(LoadModels("Art/Models/Buildings/Kenney Modular Buildings", "industrial"));
                    break;
                case 2: // Willowbrook — suburban
                    pool.AddRange(LoadModels("Art/Models/Buildings/Kenney City Kit Suburban", "building"));
                    pool.AddRange(LoadModels("Art/Models/Buildings/Kenney City Kit Suburban", "house"));
                    break;
            }
            // fallback: any building
            if (pool.Count == 0) pool.AddRange(LoadModels("Art/Models/Buildings/Kenney Building Kit", ""));
            return pool;
        }

        // ================= biome cities =================

        /// <summary>Themed prop pools per biome (all from imported Kenney packs).</summary>
        private static readonly string[][] BiomePropPools =
        {
            // Petal Vale: blossom bushes, flowers, lanterns-ish props
            new[] { "plant_bushLarge", "flower_yellow", "flower_purple", "flower_red", "plant_flatShort", "tree_default" },
            // Mirage Basin: cacti, rocks, dead logs
            new[] { "cactus_short", "cactus_tall", "rock_large", "log", "stump_square" },
            // Frostpeak: pines + rocks
            new[] { "tree_pine", "tree_cone", "rock_large", "rock_mid", "stump_round" },
            // Emberfall: scorched rocks + cliffs
            new[] { "cliff_block_stone", "rock_large", "cliff_blockDiagonal_stone", "tree_dead" },
        };

        private void GenerateBiomeCity(int idx, Transform parent, System.Random rand)
        {
            var (name, center, radius, gridHalf, biome, theme) = BiomeCities[idx];
            var cityT = new GameObject(name).transform;
            cityT.SetParent(parent, false);
            cityT.position = new Vector3(center.x, 0f, center.y);

            // street grid nodes (into shared net later — here just visual tiles via city grid)
            var models = BiomeBuildingPool(idx);
            var props = BiomePropPool(idx);

            int placed = 0;
            float step = CityGridStep;
            for (int gz = -gridHalf; gz <= gridHalf; gz++)
            for (int gx = -gridHalf; gx <= gridHalf; gx++)
            {
                bool onStreetLine = gx % 3 == 0 || gz % 3 == 0;
                if (onStreetLine) continue;
                Vector3 local = new Vector3(gx * step, 0f, gz * step);
                if (local.magnitude > radius) continue;
                if (rand.NextDouble() < 0.24) continue; // breathing room

                var model = models[rand.Next(models.Count)];
                if (model == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, cityT);
                // street-frontage: buildings face the nearest street line (grid multiple of 3)
                float frontYaw = Mathf.Abs(gx % 3) == 1 ? 90f : 0f;
                go.transform.localPosition = local + new Vector3(
                    (float)(rand.NextDouble() - 0.5) * 1.2f, 0f, (float)(rand.NextDouble() - 0.5) * 1.2f);
                go.transform.localRotation = Quaternion.Euler(0f, frontYaw + (float)(rand.NextDouble() - 0.5) * 6f, 0f);
                go.isStatic = true;
                placed++;
            }

            // themed scatter inside the city (sidewalk florals / cacti / snow rocks / ash stones)
            int propCount = Mathf.RoundToInt(radius * 0.75f);
            for (int i = 0; i < propCount; i++)
            {
                Vector2 off = RandomInCircle(rand) * radius;
                var prop = props[rand.Next(props.Count)];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prop, cityT);
                go.transform.localPosition = new Vector3(off.x, 0f, off.y);
                go.transform.localRotation = Quaternion.Euler(0f, (float)rand.NextDouble() * 360f, 0f);
                float sc = 0.8f + (float)rand.NextDouble() * 0.8f;
                go.transform.localScale = Vector3.one * sc;
                AddLodIfMissing(go);
                go.isStatic = true;
            }

            // biome weather zone trigger
            var zoneGo = new GameObject(name + "_Zone");
            zoneGo.transform.SetParent(cityT, false);
            var zone = zoneGo.AddComponent<Valebound.Systems.BiomeZone>();
            zone.biome = biome;
            zone.displayName = name;
            var zc = zoneGo.AddComponent<BoxCollider>();
            zc.isTrigger = true;
            zc.size = new Vector3(radius * 2.6f, 240f, radius * 2.6f);
            zc.center = new Vector3(0f, 100f, 0f);

            // biome garage + fast travel POI handled in GeneratePOIs
            Debug.Log($"[WorldBuilder] {name}: {placed} buildings, {propCount} props, biome {biome}.");
        }

        private List<GameObject> BiomeBuildingPool(int idx)
        {
            var pool = new List<GameObject>();
            switch (BiomeCities[idx].biome)
            {
                case Valebound.Systems.BiomeZone.Biome.Petal:
                    // cherry-blossom town: suburban houses + blossoming trees
                    pool.AddRange(LoadModels("Art/Models/Buildings/Kenney City Kit Suburban", "building"));
                    pool.AddRange(LoadModels("Art/Models/Buildings/Kenney City Kit Suburban", "house"));
                    break;
                case Valebound.Systems.BiomeZone.Biome.Desert:
                    // adobe: building kit bases
                    pool.AddRange(LoadModels("Art/Models/Buildings/Kenney Building Kit", "building"));
                    break;
                case Valebound.Systems.BiomeZone.Biome.Frost:
                    // alpine: modular + suburban mix
                    pool.AddRange(LoadModels("Art/Models/Buildings/Kenney Modular Buildings", "building"));
                    pool.AddRange(LoadModels("Art/Models/Buildings/Kenney City Kit Suburban", "building"));
                    break;
                case Valebound.Systems.BiomeZone.Biome.Ember:
                    // industrial observatory town
                    pool.AddRange(LoadModels("Art/Models/Industrial/Kenney Train Kit", "warehouse"));
                    pool.AddRange(LoadModels("Art/Models/Buildings/Kenney Building Kit", "building"));
                    break;
            }
            if (pool.Count == 0) pool.AddRange(LoadModels("Art/Models/Buildings/Kenney Building Kit", ""));
            return pool;
        }

        private List<GameObject> BiomePropPool(int idx)
        {
            var pool = new List<GameObject>();
            foreach (var n in BiomePropPools[idx])
            {
                var found = LoadModel("Art/Models/Nature/Kenney Nature Kit", n)
                         ?? LoadModel("Art/Models/Buildings/Kenney Building Kit", n);
                if (found != null) pool.Add(found);
            }
            if (pool.Count == 0)
                pool.AddRange(LoadModels("Art/Models/Nature/Kenney Nature Kit", "rock"));
            return pool;
        }

        private void GenerateVillage(Transform parent, System.Random rand)
        {
            var villageT = new GameObject("Millbrook Village").transform;
            villageT.SetParent(parent, false);
            villageT.position = new Vector3(VillageCenter.x, 0f, VillageCenter.y);

            var houses = LoadModels("Art/Models/Buildings/Kenney City Kit Suburban", "house");
            if (houses.Count == 0) houses.AddRange(LoadModels("Art/Models/Buildings/Kenney Building Kit", "building"));

            int placed = 0;
            if (houses.Count > 0)
            {
                for (int ring = 1; ring <= 3 && placed < 26; ring++)
                {
                    int count = ring * 6;
                    for (int i = 0; i < count && placed < 26; i++)
                    {
                        float ang = (i / (float)count) * Mathf.PI * 2f;
                        Vector3 pos = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (ring * 30f);
                        var model = houses[rand.Next(houses.Count)];
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(model, villageT);
                        go.transform.localPosition = pos;
                        go.transform.localRotation = Quaternion.Euler(0f, Mathf.Rad2Deg * -ang + 90f, 0f);
                        go.isStatic = true;
                        placed++;
                    }
                }
            }
            // landmark: castle ruin on the hill edge
            var castle = LoadModel("Art/Models/Landmarks/Kenney Castle Kit/tower");
            if (castle != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(castle, villageT);
                go.transform.localPosition = new Vector3(0f, 0f, 0f);
                go.isStatic = true;
            }
            Debug.Log($"[WorldBuilder] Village: {placed} houses.");
        }

        // ================= nature =================

        private void GenerateNature(Transform parent, System.Random rand, Terrain terrain)
        {
            var natureRoot = new GameObject("Forest & Rocks").transform;
            natureRoot.SetParent(parent, false);

            var trees = LoadModels("Art/Models/Nature/Kenney Nature Kit", "tree");
            var pines = LoadModels("Art/Models/Nature/Kenney Nature Kit", "pine");
            var rocks = LoadModels("Art/Models/Nature/Kenney Nature Kit", "rock");
            var bushes = LoadModels("Art/Models/Props/Quaternius Rpg Pack", "");  // reuse props; foliage pack is 2D sprites

            var forestCenters = new List<Vector2>
            {
                new Vector2(-1200f, 200f),   // Cedar Forest west
                new Vector2(900f, 900f),     // NE woods
                new Vector2(300f, -1100f),   // south woods
                new Vector2(-1000f, -1000f), // SW woods
            };

            int placed = 0;
            // forests
            foreach (var fc in forestCenters)
            {
                int target = 60;
                for (int i = 0; i < target; i++)
                {
                    Vector2 off = RandomInCircle(rand) * (60f + (float)rand.NextDouble() * 160f);
                    Vector3 pos = new Vector3(fc.x + off.x, 0f, fc.y + off.y);
                    if (InsideAnyCity(pos)) continue;
                    var list = trees.Count > 0 ? trees : pines;
                    if (list.Count == 0) break;
                    var model = list[rand.Next(list.Count)];
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(model, natureRoot);
                    go.transform.position = pos;
                    go.transform.rotation = Quaternion.Euler(0f, (float)rand.NextDouble() * 360f, 0f);
                    float s = 0.8f + (float)rand.NextDouble() * 0.7f;
                    go.transform.localScale = Vector3.one * s;
                    AddLodIfMissing(go);
                    go.isStatic = true;
                    placed++;
                }
            }
            // scattered trees & rocks in countryside
            for (int i = 0; i < 420; i++)
            {
                Vector3 pos = new Vector3(
                    (float)(rand.NextDouble() * 2 - 1) * 1900f, 0f,
                    (float)(rand.NextDouble() * 2 - 1) * 1900f);
                if (InsideAnyCity(pos) || InWater(pos)) continue;
                var model = rocks[rand.Next(rocks.Count)];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, natureRoot);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(0f, (float)rand.NextDouble() * 360f, 0f);
                float s = 0.6f + (float)rand.NextDouble() * 1.4f;
                go.transform.localScale = Vector3.one * s;
                go.isStatic = true;
                placed++;
            }
            Debug.Log($"[WorldBuilder] nature: {placed} objects.");
        }

        private static Vector2 RandomInCircle(System.Random rand)
        {
            double a = rand.NextDouble() * Math.PI * 2;
            double r = Math.Sqrt(rand.NextDouble());
            return new Vector2((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r));
        }

        private static bool InsideAnyCity(Vector3 pos)
        {
            for (int i = 0; i < 3; i++)
                if (Dist(pos.x, pos.z, CityCenters[i].x, CityCenters[i].y) < CityRadii[i] + 40f) return true;
            foreach (var bc in BiomeCities)
                if (Dist(pos.x, pos.z, bc.center.x, bc.center.y) < bc.radius + 40f) return true;
            return Dist(pos.x, pos.z, VillageCenter.x, VillageCenter.y) < VillageRadius + 40f;
        }

        private static bool InWater(Vector3 pos) =>
            Dist(pos.x, pos.z, 550f, 250f) < 200f ||
            Dist(pos.x, pos.z, -250f, -250f) < 130f ||
            pos.z < -1790f;

        // ================= POIs =================

        private void GeneratePOIs(Transform parent, RoadNetworkData net, System.Random rand)
        {
            var poiRootT = parent;
            var shards = new List<CollectibleManager.Shard>();
            var races = new List<RaceActivity.RaceDef>();
            var pois = new List<POI>();
            var npcZones = new List<NpcSystem.SpawnZone>();

            // --- named POIs per GDD ---
            void AddPOI(string id, string name, POIType type, Vector3 pos)
            {
                pois.Add(new POI { poiId = id, displayName = name, type = type, worldPos = pos, discovered = false });
                if (type == POIType.FastTravel)
                    npcZones.Add(new NpcSystem.SpawnZone { zoneId = id + "_npc", center = pos, radius = 24f, weight = 1f });
            }

            AddPOI("guild_hq", "Bounders Guild HQ", POIType.MissionGiver, new Vector3(CityCenters[0].x - 60f, 0f, CityCenters[0].y + 40f));
            AddPOI("meridian_tower", "Meridian Tower", POIType.Landmark, new Vector3(CityCenters[0].x + 80f, 0f, CityCenters[0].y - 60f));
            AddPOI("plaza", "Grand Plaza", POIType.CityCenter, new Vector3(CityCenters[0].x, 0f, CityCenters[0].y));
            AddPOI("diner", "Harbor Diner", POIType.Shop, new Vector3(CityCenters[0].x - 140f, 0f, CityCenters[0].y + 120f));
            AddPOI("station", "Central Station", POIType.FastTravel, new Vector3(CityCenters[0].x + 40f, 0f, CityCenters[0].y + 180f));

            AddPOI("steelworks", "Aurora Steelworks", POIType.Landmark, new Vector3(CityCenters[1].x, 0f, CityCenters[1].y));
            AddPOI("containeryard", "Container Yard", POIType.Shop, new Vector3(CityCenters[1].x + 100f, 0f, CityCenters[1].y + 80f));
            AddPOI("raildepot", "Rail Depot", POIType.FastTravel, new Vector3(CityCenters[1].x - 120f, 0f, CityCenters[1].y - 60f));
            AddPOI("junkyard", "Silva Salvage (Cass)", POIType.MissionGiver, new Vector3(CityCenters[1].x + 60f, 0f, CityCenters[1].y + 140f));

            AddPOI("park", "Willowbrook Park", POIType.Landmark, new Vector3(CityCenters[2].x, 0f, CityCenters[2].y));
            AddPOI("school", "Maple Elementary", POIType.Shop, new Vector3(CityCenters[2].x - 80f, 0f, CityCenters[2].y - 80f));
            AddPOI("market", "Sunday Market", POIType.Shop, new Vector3(CityCenters[2].x + 90f, 0f, CityCenters[2].y + 60f));
            AddPOI("policepost", "Police Post (Dana)", POIType.MissionGiver, new Vector3(CityCenters[2].x + 20f, 0f, CityCenters[2].y - 140f));

            AddPOI("village", "Millbrook Village", POIType.CityCenter, new Vector3(VillageCenter.x, 0f, VillageCenter.y));
            AddPOI("oldmill", "Old Mill", POIType.Landmark, new Vector3(VillageCenter.x + 80f, 0f, VillageCenter.y - 60f));
            AddPOI("lighthouse", "Cape Light", POIType.Vista, new Vector3(900f, 0f, -1750f));
            AddPOI("vista_n", "Vista Point", POIType.Vista, new Vector3(-300f, 0f, 1500f));
            AddPOI("radio", "Radio Tower", POIType.FastTravel, new Vector3(1200f, 0f, 1200f));
            AddPOI("winery", "Aurora Winery", POIType.Shop, new Vector3(-1200f, 0f, -600f));
            AddPOI("lake", "Horseshoe Lake", POIType.Vista, new Vector3(550f, 0f, 250f));
            AddPOI("garage_pm", "Garage (Port Meridian)", POIType.Garage, new Vector3(CityCenters[0].x + 100f, 0f, CityCenters[0].y + 100f));
            AddPOI("garage_ff", "Garage (Foundry Flats)", POIType.Garage, new Vector3(CityCenters[1].x - 60f, 0f, CityCenters[1].y + 100f));
            AddPOI("docks", "Harbor Docks (Vo)", POIType.MissionGiver, new Vector3(CityCenters[0].x - 200f, 0f, CityCenters[0].y - 160f));
            AddPOI("guild_wb", "Guild Office (Willowbrook)", POIType.FastTravel, new Vector3(CityCenters[2].x + 60f, 0f, CityCenters[2].y + 120f));
            AddPOI("guild_mb", "Guild Office (Millbrook)", POIType.FastTravel, new Vector3(VillageCenter.x - 60f, 0f, VillageCenter.y + 60f));
            AddPOI("meridian_lab", "Meridian Lab Annex", POIType.Landmark, new Vector3(CityCenters[1].x + 160f, 0f, CityCenters[1].y - 100f));

            // biome cities
            AddPOI("petal_plaza", "Petal Vale Plaza", POIType.CityCenter, new Vector3(BiomeCities[0].center.x, 0f, BiomeCities[0].center.y));
            AddPOI("petal_garage", "Petal Vale Garage", POIType.Garage, new Vector3(BiomeCities[0].center.x + 90f, 0f, BiomeCities[0].center.y + 60f));
            AddPOI("petal_guild", "Guild Office (Petal Vale)", POIType.FastTravel, new Vector3(BiomeCities[0].center.x - 60f, 0f, BiomeCities[0].center.y - 80f));
            AddPOI("mirage_plaza", "Mirage Basin Depot", POIType.CityCenter, new Vector3(BiomeCities[1].center.x, 0f, BiomeCities[1].center.y));
            AddPOI("mirage_garage", "Mirage Basin Garage", POIType.Garage, new Vector3(BiomeCities[1].center.x + 90f, 0f, BiomeCities[1].center.y + 60f));
            AddPOI("mirage_oasis", "The Oasis", POIType.Vista, new Vector3(BiomeCities[1].center.x + 240f, 0f, BiomeCities[1].center.y - 200f));
            AddPOI("frost_plaza", "Frostpeak Clinic Square", POIType.CityCenter, new Vector3(BiomeCities[2].center.x, 0f, BiomeCities[2].center.y));
            AddPOI("frost_garage", "Frostpeak Garage", POIType.Garage, new Vector3(BiomeCities[2].center.x + 80f, 0f, BiomeCities[2].center.y + 60f));
            AddPOI("frost_lake", "Frozen Lake", POIType.Vista, new Vector3(BiomeCities[2].center.x - 140f, 0f, BiomeCities[2].center.y - 160f));
            AddPOI("ember_plaza", "Emberfall Observatory", POIType.CityCenter, new Vector3(BiomeCities[3].center.x, 0f, BiomeCities[3].center.y));
            AddPOI("ember_garage", "Emberfall Garage", POIType.Garage, new Vector3(BiomeCities[3].center.x + 80f, 0f, BiomeCities[3].center.y + 60f));
            AddPOI("ember_crater", "Cinder Crater Rim", POIType.Vista, new Vector3(BiomeCities[3].center.x, 0f, BiomeCities[3].center.y - 240f));

            // biome city spawn zones (pedestrians + taxi peds in every biome city)
            for (int i = 0; i < BiomeCities.Length; i++)
                npcZones.Add(new NpcSystem.SpawnZone
                {
                    zoneId = $"biome{i}_npc",
                    center = new Vector3(BiomeCities[i].center.x, 0f, BiomeCities[i].center.y),
                    radius = 60f,
                    weight = 0.8f,
                });

            // --- vista markers ---
            var vistaGo = new GameObject("Vistas");
            vistaGo.transform.SetParent(poiRootT, false);
            foreach (var p in pois)
            {
                if (p.type != POIType.Vista) continue;
                var vp = new GameObject("Vista_" + p.poiId);
                vp.transform.SetParent(vistaGo.transform, false);
                vp.transform.position = p.worldPos;
                var box = vp.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(4f, 3f, 4f);
                var vista = vp.AddComponent<Valebound.NPC.VistaPoint>();
                vista.SetFields(p.displayName);
            }

            // --- shards: 60, deterministic positions ---
            var shardGo = new GameObject("SignalShards");
            shardGo.transform.SetParent(poiRootT, false);
            var shardModel = LoadModel("Art/Models/Props/Quaternius Rpg Pack", "Gems");
            for (int i = 0; i < 60; i++)
            {
                Vector3 pos;
                int guard = 0;
                do
                {
                    pos = new Vector3(
                        (float)(rand.NextDouble() * 2 - 1) * 1800f, 1.2f,
                        (float)(rand.NextDouble() * 2 - 1) * 1800f);
                    guard++;
                } while (guard < 40 && (InWater(pos) || Dist(pos.x, pos.z, 0, 0) > 1850f));

                var go = shardModel != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(shardModel, shardGo.transform)
                    : new GameObject("shard_" + i, typeof(BoxCollider));
                go.name = "SignalShard_" + i;
                go.transform.SetParent(shardGo.transform, false);
                go.transform.position = pos;
                var col = go.GetComponent<Collider>();
                if (col == null) col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                var shard = go.AddComponent<Valebound.NPC.SignalShard>();
                shard.SetIndex(i);
                shards.Add(new CollectibleManager.Shard { index = i, pos = pos, collected = false });
            }

            // --- races: 3 routes along road graph ---
            for (int r = 0; r < 3; r++)
            {
                var gates = new List<RaceActivity.Gate>();
                var route = PickRaceRoute(net, rand);
                foreach (var node in route)
                {
                    Vector3 p = net.NodePos(node);
                    gates.Add(new RaceActivity.Gate { pos = p, radius = 12f, passed = false });
                }
                races.Add(new RaceActivity.RaceDef
                {
                    raceId = "race_" + r,
                    raceName = $"Aurora Circuit {r + 1}",
                    gates = gates.ToArray(),
                    timeLimitSeconds = 100f + r * 25f,
                    rewardCredits = 220 + r * 90,
                });
            }

            // persist into scene systems
            var mapSys = GameObject.Find("Systems")?.AddComponent<WorldMapSystem>();
            var collSys = GameObject.Find("Systems")?.AddComponent<CollectibleManager>();
            var raceSys = GameObject.Find("Systems")?.AddComponent<RaceActivity>();
            var npcSys = GameObject.Find("Systems")?.AddComponent<NpcSystem>();
            // serialize arrays via the scene objects (values visible in inspector)
            mapSys.SetPOIs(pois.ToArray());
            collSys.SetShards(shards.ToArray());
            raceSys.SetRaces(races.ToArray());
            npcSys.SetZones(npcZones.ToArray());

            Debug.Log($"[WorldBuilder] POIs: {pois.Count}, shards: {shards.Count}, races: {races.Count}, npc zones: {npcZones.Count}");
        }

        private List<int> PickRaceRoute(RoadNetworkData net, System.Random rand)
        {
            // random walk on graph: 10-14 nodes, reasonably spread
            var route = new List<int>();
            int current = rand.Next(net.nodes.Count);
            route.Add(current);
            for (int i = 0; i < 12; i++)
            {
                var conns = net.nodes[current].connections;
                if (conns == null || conns.Count == 0) break;
                int next = conns[rand.Next(conns.Count)];
                if (route.Count > 2 && route[route.Count - 2] == next) continue;
                route.Add(next);
                current = next;
            }
            return route;
        }

        // ================= systems setup =================

        private void SetupSystems(Transform parent, RoadNetworkData net)
        {
            var go = parent.gameObject;
            if (go.GetComponent<GameManager>() == null) go.AddComponent<GameManager>();
            if (go.GetComponent<PoolManager>() == null) go.AddComponent<PoolManager>();
            if (go.GetComponent<PerformanceGovernor>() == null) go.AddComponent<PerformanceGovernor>();
            if (go.GetComponent<WorldClock>() == null) go.AddComponent<WorldClock>();
            if (go.GetComponent<WeatherSystem>() == null) go.AddComponent<WeatherSystem>();
            if (go.GetComponent<RoadNetwork>() == null) go.AddComponent<RoadNetwork>();
            if (go.GetComponent<TrafficSystem>() == null) go.AddComponent<TrafficSystem>();
            if (go.GetComponent<NpcSystem>() == null) go.AddComponent<NpcSystem>();
            if (go.GetComponent<AudioManager>() == null) go.AddComponent<AudioManager>();
            if (go.GetComponent<MissionManager>() == null) go.AddComponent<MissionManager>();
            if (go.GetComponent<CourierGenerator>() == null) go.AddComponent<CourierGenerator>();
            if (go.GetComponent<EconomyManager>() == null) go.AddComponent<EconomyManager>();
            if (go.GetComponent<AchievementSystem>() == null) go.AddComponent<AchievementSystem>();
            if (go.GetComponent<FastTravelSystem>() == null) go.AddComponent<FastTravelSystem>();
            if (go.GetComponent<VehicleGarage>() == null) go.AddComponent<VehicleGarage>();
            if (go.GetComponent<TouchInputHub>() == null) go.AddComponent<TouchInputHub>();
            if (go.GetComponent<Systems.BiomeZoneSystem>() == null) go.AddComponent<Systems.BiomeZoneSystem>();
            if (go.GetComponent<Systems.DailyChallengeSystem>() == null) go.AddComponent<Systems.DailyChallengeSystem>();
            if (go.GetComponent<Systems.TaxiJobSystem>() == null) go.AddComponent<Systems.TaxiJobSystem>();
            if (go.GetComponent<Systems.StuntTracker>() == null) go.AddComponent<Systems.StuntTracker>();

            // traffic variety: all 17 vehicle prefabs
            var traffic = go.GetComponent<TrafficSystem>();
            var trafficPool = new List<GameObject>();
            foreach (var id in new[] { "sedan", "hatchback", "van", "suv", "taxi", "delivery", "garbage", "truck" })
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/VALEBOUND/Prefabs/Vehicles/Vehicle_{id}.prefab");
                if (pf != null) trafficPool.Add(pf);
            }
            if (trafficPool.Count > 0)
            {
                var ser = new SerializedObject(traffic);
                ser.FindProperty("trafficPrefabs").arraySize = trafficPool.Count;
                for (int i = 0; i < trafficPool.Count; i++)
                    ser.FindProperty("trafficPrefabs").GetArrayElementAtIndex(i).objectReferenceValue = trafficPool[i];
                ser.ApplyModifiedProperties();
            }

            // pedestrian + taxi prefab pools
            var npc = go.GetComponent<NpcSystem>();
            var pedPool = new List<GameObject>();
            foreach (var f in Directory.GetFiles($"{PrefabDirRel}/Characters", "Ped_*.prefab"))
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(f);
                if (pf != null) pedPool.Add(pf);
            }
            foreach (var f in Directory.GetFiles($"{PrefabDirRel}/Characters", "TaxiPed_*.prefab"))
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(f);
                if (pf != null) pedPool.Add(pf);
            }
            if (pedPool.Count > 0)
            {
                var ser = new SerializedObject(npc);
                ser.FindProperty("pedestrianPrefabs").arraySize = pedPool.Count;
                for (int i = 0; i < pedPool.Count; i++)
                    ser.FindProperty("pedestrianPrefabs").GetArrayElementAtIndex(i).objectReferenceValue = pedPool[i];
                ser.ApplyModifiedProperties();
            }

            // garage roster from VehicleDef assets
            var garage = go.GetComponent<VehicleGarage>();
            var defPaths = AssetDatabase.FindAssets("t:VehicleDef", new[] { "Assets/VALEBOUND/Data" });
            var entries = new List<VehicleGarage.GarageEntry>();
            foreach (var g in defPaths)
            {
                var def = AssetDatabase.LoadAssetAtPath<Vehicle.VehicleDef>(AssetDatabase.GUIDToAssetPath(g));
                if (def == null) continue;
                entries.Add(new VehicleGarage.GarageEntry
                {
                    vehicleId = def.vehicleId,
                    def = def,
                    unlocked = def.price == 0,
                    owned = def.price == 0,
                });
            }
            var gSer = new SerializedObject(garage);
            gSer.FindProperty("entries").arraySize = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = gSer.FindProperty("entries").GetArrayElementAtIndex(i);
                e.FindPropertyRelative("vehicleId").stringValue = entries[i].vehicleId;
                e.FindPropertyRelative("def").objectReferenceValue = entries[i].def;
                e.FindPropertyRelative("unlocked").boolValue = entries[i].unlocked;
                e.FindPropertyRelative("owned").boolValue = entries[i].owned;
            }
            gSer.ApplyModifiedProperties();

            // player + UI prefab refs (GameManager needs both to spawn anything)
            var gmSer = new SerializedObject(go.GetComponent<GameManager>());
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDirRel}/Characters/Player.prefab");
            if (playerPrefab != null) gmSer.FindProperty("playerPrefab").objectReferenceValue = playerPrefab;
            var uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDirRel}/UI/UICanvas.prefab");
            if (uiPrefab != null) gmSer.FindProperty("uiPrefab").objectReferenceValue = uiPrefab;
            gmSer.ApplyModifiedProperties();

            // save road net asset
            if (net != null)
            {
                string dir = "Assets/VALEBOUND/Settings/Generated";
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string path = $"{dir}/RoadNetwork.asset";
                AssetDatabase.CreateAsset(net, path);
                AssetDatabase.SaveAssets();
                go.GetComponent<RoadNetwork>().SetData(net);
            }
        }

        private void BakeNavMesh()
        {
            try
            {
                UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
                Debug.Log("[WorldBuilder] NavMesh baked via UnityEditor.AI.NavMeshBuilder.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WorldBuilder] NavMesh bake note: " + ex.Message);
            }
        }

        private void BakeMinimapTexture()
        {
            // render top-down ortho snapshot of the scene into a texture
            int res = 1024;
            var camGo = new GameObject("MinimapBakeCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = WorldSize * 0.5f;
            cam.transform.position = new Vector3(0f, 500f, 0f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.65f, 0.5f);
            cam.cullingMask = ~LayerMask.GetMask("UI");
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 1000f;
            cam.targetTexture = new RenderTexture(res, res, 16);

            cam.Render();

            Texture2D tex = new Texture2D(res, res, TextureFormat.RGB24, false);
            RenderTexture.active = cam.targetTexture;
            tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            string dir = "Assets/VALEBOUND/Art/UI/Generated";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes($"{dir}/MinimapMap.png", tex.EncodeToPNG());

            cam.targetTexture.Release();
            DestroyImmediate(camGo);

            AssetDatabase.ImportAsset($"{dir}/MinimapMap.png");
            var importer = (TextureImporter)AssetImporter.GetAtPath($"{dir}/MinimapMap.png");
            importer.textureType = TextureImporterType.Default;
            importer.SaveAndReimport();
            Debug.Log("[WorldBuilder] minimap baked to Art/UI/Generated/MinimapMap.png");
        }

        // ================= helpers =================

        private static Texture2D LoadTex(string path)
        {
            // solid-color ground textures (generated by ImportConfigurator; may be replaced)
            // call sites use short paths like "Art/Textures/T_Grass" -> resolve both forms
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path + ".png");
            if (tex == null && !path.StartsWith("Assets/"))
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VALEBOUND/" + path + ".png");
            return tex;
        }

        private static GameObject LoadModel(string folder, string name = null)
        {
            var found = LoadModels(folder, name);
            return found.Count > 0 ? found[0] : null;
        }

        private static List<GameObject> LoadModels(string folder, string nameContains)
        {
            var result = new List<GameObject>();
            // call sites use short paths like "Art/Models/..." -> resolve to Assets/VALEBOUND/...
            if (!folder.StartsWith("Assets/") && !Directory.Exists(folder))
                folder = "Assets/VALEBOUND/" + folder;
            if (!Directory.Exists(folder)) return result;
            foreach (var f in Directory.GetFiles(folder, "*.fbx"))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                if (!string.IsNullOrEmpty(nameContains) &&
                    n.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(f);
                if (asset != null) result.Add(asset);
            }
            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            return result;
        }

        private static void AddLodIfMissing(GameObject go)
        {
            if (go.GetComponent<LODGroup>() != null) return;
            var lod = go.AddComponent<LODGroup>();
            var renderers = go.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return;
            lod.SetLODs(new[]
            {
                new LOD(0.4f, renderers),          // full detail until 60% of lod range
                new LOD(0.05f, new Renderer[0]),   // culled after
            });
            lod.RecalculateBounds();
        }
    }
}
#endif
