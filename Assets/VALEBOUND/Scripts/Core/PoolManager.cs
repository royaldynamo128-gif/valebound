using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound
{
    /// <summary>
    /// Generic typed object pool for pooled instances (traffic cars, pedestrians,
    /// particles, sounds). Prewarms on demand, caps growth, releases unused over time.
    /// </summary>
    public class GameObjectPool
    {
        private readonly GameObject prefab;
        private readonly Transform parent;
        private readonly Stack<GameObject> free = new Stack<GameObject>();
        private readonly List<GameObject> all = new List<GameObject>();
        private readonly int maxHard;

        public int ActiveCount { get; private set; }
        public int TotalCount => all.Count;

        public GameObjectPool(GameObject prefab, Transform parent, int prewarm, int maxHard)
        {
            this.prefab = prefab;
            this.parent = parent;
            this.maxHard = Mathf.Max(maxHard, prewarm);
            for (int i = 0; i < prewarm; i++)
            {
                var go = UnityEngine.Object.Instantiate(prefab, parent);
                go.SetActive(false);
                free.Push(go);
                all.Add(go);
            }
        }

        public GameObject Get(Vector3 pos, Quaternion rot)
        {
            GameObject go = null;
            while (free.Count > 0 && go == null)
                go = free.Pop();
            if (go == null)
            {
                if (all.Count >= maxHard)
                {
                    // steal the oldest active instance instead of growing
                    go = all[0];
                }
                else
                {
                    go = UnityEngine.Object.Instantiate(prefab, parent);
                    all.Add(go);
                }
            }
            go.transform.SetPositionAndRotation(pos, rot);
            go.SetActive(true);
            ActiveCount++;
            return go;
        }

        public void Release(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            if (!free.Contains(go)) free.Push(go);
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
        }

        public void ReleaseAll()
        {
            foreach (var go in all)
            {
                if (go != null && go.activeSelf) Release(go);
            }
            ActiveCount = 0;
        }

        public void DestroyAll()
        {
            foreach (var go in all)
                if (go != null) UnityEngine.Object.Destroy(go);
            free.Clear();
            all.Clear();
            ActiveCount = 0;
        }
    }

    /// <summary>
    /// Registry of every runtime pool. Central place for budgets per performance tier.
    /// </summary>
    public class PoolManager : MonoBehaviour
    {
        public static PoolManager Instance { get; private set; }

        private readonly Dictionary<string, GameObjectPool> pools =
            new Dictionary<string, GameObjectPool>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public GameObjectPool GetOrCreate(string key, GameObject prefab, int prewarm, int maxHard)
        {
            if (pools.TryGetValue(key, out var pool)) return pool;
            var holder = new GameObject($"Pool_{key}");
            holder.transform.SetParent(transform, false);
            pool = new GameObjectPool(prefab, holder.transform, prewarm, maxHard);
            pools[key] = pool;
            return pool;
        }

        public GameObjectPool Get(string key) =>
            pools.TryGetValue(key, out var p) ? p : null;

        public void ReleaseAll()
        {
            foreach (var p in pools.Values) p.ReleaseAll();
        }

        public void DestroyAll()
        {
            foreach (var p in pools.Values) p.DestroyAll();
            pools.Clear();
        }
    }
}
