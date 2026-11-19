using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound.Systems
{
    /// <summary>
    /// Road network baked by WorldBuilder: nodes at intersections, directed lane
    /// edges with spline points. Used by traffic AI and the mission router.
    /// Serialized to a plain data asset so it survives scene load without recompute.
    /// </summary>
    [Serializable]
    public class RoadNetworkData : ScriptableObject
    {
        [Serializable]
        public class Node
        {
            public int id;
            public float x, y, z;
            public bool isIntersection;
            public bool isHighway;
            public List<int> connections = new List<int>();
        }

        [Serializable]
        public class Edge
        {
            public int from, to;
            public float length;
            public float speedLimitKph = 50f;
            public bool isHighway;
            public List<Vector3> curve = new List<Vector3>(); // sparse spline (from -> to)
        }

        public List<Node> nodes = new List<Node>();
        public List<Edge> edges = new List<Edge>();
        public Bounds worldBounds;

        public Vector3 NodePos(int id) =>
            nodes[id] != null ? new Vector3(nodes[id].x, nodes[id].y, nodes[id].z) : Vector3.zero;

        public int NearestNode(Vector3 pos, bool highwaysOnly = false)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (highwaysOnly && !nodes[i].isHighway) continue;
                float d = (NodePos(i) - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>A* over the graph. Returns node path (excludes start node).</summary>
        public bool FindPath(int start, int goal, List<int> result)
        {
            result.Clear();
            if (start < 0 || goal < 0 || start >= nodes.Count || goal >= nodes.Count) return false;

            var open = new List<int> { start };
            var g = new Dictionary<int, float> { [start] = 0f };
            var f = new Dictionary<int, float> { [start] = Heuristic(start, goal) };
            var cameFrom = new Dictionary<int, int>();

            int guard = 0;
            while (open.Count > 0 && guard++ < 4000)
            {
                // pop lowest f
                int current = open[0];
                for (int i = 1; i < open.Count; i++)
                    if (f[open[i]] < f[current]) current = open[i];
                if (current == goal)
                {
                    Reconstruct(cameFrom, current, result);
                    return true;
                }
                open.Remove(current);

                foreach (var edge in edges)
                {
                    int neighbor = -1;
                    if (edge.from == current) neighbor = edge.to;
                    else if (edge.to == current && IsTwoWay(edge)) neighbor = edge.from;
                    if (neighbor < 0) continue;

                    float ng = g[current] + edge.length;
                    if (!g.ContainsKey(neighbor) || ng < g[neighbor])
                    {
                        cameFrom[neighbor] = current;
                        g[neighbor] = ng;
                        f[neighbor] = ng + Heuristic(neighbor, goal);
                        if (!open.Contains(neighbor)) open.Add(neighbor);
                    }
                }
            }
            return false;
        }

        private bool IsTwoWay(Edge e) => !e.isHighway; // city streets two-way, highways one-way lanes

        private float Heuristic(int a, int b) => Vector3.Distance(NodePos(a), NodePos(b));

        private void Reconstruct(Dictionary<int, int> cameFrom, int current, List<int> result)
        {
            result.Add(current);
            while (cameFrom.TryGetValue(current, out int prev))
            {
                current = prev;
                result.Add(current);
            }
            result.Reverse();
            if (result.Count > 0 && result[0] == start_dummy) { }
        }

        private int start_dummy = -1;

        public void FindPathFromTo(Vector3 fromPos, Vector3 toPos, List<int> result)
        {
            int a = NearestNode(fromPos);
            int b = NearestNode(toPos);
            if (a < 0 || b < 0 || !FindPath(a, b, result)) result.Clear();
        }
    }

    /// <summary>
    /// Runtime singleton wrapper providing the baked RoadNetworkData to traffic and missions.
    /// </summary>
    public class RoadNetwork : MonoBehaviour
    {
        public static RoadNetwork Instance { get; private set; }
        [SerializeField] private RoadNetworkData data;
        public RoadNetworkData Data => data;
        public void SetData(RoadNetworkData d) => data = d;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
