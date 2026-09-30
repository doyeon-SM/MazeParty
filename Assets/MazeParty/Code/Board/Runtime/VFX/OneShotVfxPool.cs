using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Client-local pool for authored one-shot VFX. The pool contains no
    /// gameplay authority and never creates or synchronizes NetworkObjects.
    /// </summary>
    public static class OneShotVfxPool
    {
        public const int HardInstanceLimitPerPrefab = 24;
        public const int RetainedInactiveLimitPerPrefab = 8;

        private static readonly Dictionary<EntityId, Pool> Pools =
            new Dictionary<EntityId, Pool>();
        private static Transform _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Pools.Clear();
            _root = null;
        }

        public static void Prewarm(GameObject prefab, int count)
        {
            if (!Application.isPlaying || prefab == null || count <= 0)
            {
                return;
            }

            var pool = GetOrCreatePool(prefab);
            if (pool == null)
            {
                return;
            }

            var targetCount = Mathf.Min(
                count,
                RetainedInactiveLimitPerPrefab);
            while (pool.TotalCount < targetCount)
            {
                if (CreateInstance(pool) == null)
                {
                    break;
                }
            }
        }

        public static PooledOneShotVfx Play(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            float uniformScale = 1f)
        {
            if (!Application.isPlaying || prefab == null)
            {
                return null;
            }

            var pool = GetOrCreatePool(prefab);
            if (pool == null)
            {
                return null;
            }

            EnsurePoolRootActive(pool);
            var effect = TakeInactive(pool);
            if (effect == null)
            {
                CreateInstance(pool);
                effect = TakeInactive(pool);
            }
            if (effect == null)
            {
                // Presentation is deliberately dropped when this prefab's
                // bounded pool is saturated. Recycling an active effect would
                // cut off its authored particle tail.
                return null;
            }

            var effectTransform = effect.transform;
            effectTransform.SetParent(pool.Root, false);
            effectTransform.SetPositionAndRotation(position, rotation);
            effectTransform.localScale = pool.AuthoredScale *
                                         Mathf.Max(0.01f, uniformScale);
            pool.Active.Add(effect);
            effect.enabled = true;
            effect.gameObject.SetActive(true);
            effect.PlayFromPool();
            return effect;
        }

        private static Pool GetOrCreatePool(GameObject prefab)
        {
            var key = prefab.GetEntityId();
            if (Pools.TryGetValue(key, out var existing))
            {
                if (existing.Prefab == prefab && existing.Root != null)
                {
                    return existing;
                }

                Pools.Remove(key);
            }

            var contract = prefab.GetComponent<PooledOneShotVfx>();
            if (contract == null)
            {
                Debug.LogError(
                    "[VFX] Prefab '" + prefab.name +
                    "' must have PooledOneShotVfx on its root.",
                    prefab);
                return null;
            }

            EnsureRoot();
            var poolRoot = new GameObject(prefab.name + " Pool").transform;
            poolRoot.SetParent(_root, false);
            var pool = new Pool(
                key,
                prefab,
                poolRoot,
                prefab.transform.localScale);
            Pools[key] = pool;
            return pool;
        }

        private static PooledOneShotVfx CreateInstance(Pool pool)
        {
            PruneDestroyedActive(pool);
            if (pool.TotalCount >= HardInstanceLimitPerPrefab)
            {
                return null;
            }

            var instance = Object.Instantiate(
                pool.Prefab,
                pool.Root,
                false);
            instance.name = pool.Prefab.name;
            var effect = instance.GetComponent<PooledOneShotVfx>();
            if (effect == null)
            {
                Object.Destroy(instance);
                return null;
            }

            effect.PrepareForPool(item => Release(pool.Key, item));
            pool.TotalCount++;
            pool.Inactive.Push(effect);
            return effect;
        }

        private static PooledOneShotVfx TakeInactive(Pool pool)
        {
            while (pool.Inactive.Count > 0)
            {
                var effect = pool.Inactive.Pop();
                if (effect != null)
                {
                    return effect;
                }

                pool.TotalCount = Mathf.Max(0, pool.TotalCount - 1);
            }

            return null;
        }

        private static void Release(EntityId key, PooledOneShotVfx effect)
        {
            if (effect == null)
            {
                return;
            }

            if (!Pools.TryGetValue(key, out var pool))
            {
                if (effect.gameObject.activeSelf)
                {
                    effect.gameObject.SetActive(false);
                }
                Object.Destroy(effect.gameObject);
                return;
            }

            if (!pool.Active.Remove(effect))
            {
                return;
            }

            effect.transform.SetParent(pool.Root, false);
            if (effect.gameObject.activeSelf)
            {
                effect.gameObject.SetActive(false);
            }

            if (pool.Inactive.Count < RetainedInactiveLimitPerPrefab)
            {
                pool.Inactive.Push(effect);
                return;
            }

            pool.TotalCount = Mathf.Max(0, pool.TotalCount - 1);
            Object.Destroy(effect.gameObject);
        }

        private static void EnsurePoolRootActive(Pool pool)
        {
            EnsureRoot();
            if (!pool.Root.gameObject.activeSelf)
            {
                pool.Root.gameObject.SetActive(true);
            }
        }

        private static void PruneDestroyedActive(Pool pool)
        {
            var removed = pool.Active.RemoveWhere(item => item == null);
            if (removed > 0)
            {
                pool.TotalCount = Mathf.Max(0, pool.TotalCount - removed);
            }
        }

        private static void EnsureRoot()
        {
            if (_root != null)
            {
                if (!_root.gameObject.activeSelf)
                {
                    _root.gameObject.SetActive(true);
                }
                return;
            }

            var rootObject = new GameObject("MazeParty One-Shot VFX Pools");
            Object.DontDestroyOnLoad(rootObject);
            _root = rootObject.transform;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // One-shots are presentation for the scene that emitted them. The
            // pool itself survives scene transitions, but active particles must
            // never bleed into the next board/minigame presentation.
            foreach (var pool in Pools.Values)
            {
                if (pool.Active.Count == 0)
                {
                    continue;
                }

                var active = new List<PooledOneShotVfx>(pool.Active);
                for (var index = 0; index < active.Count; index++)
                {
                    active[index]?.StopAndReturnToPool();
                }
            }
        }

        private sealed class Pool
        {
            public Pool(
                EntityId key,
                GameObject prefab,
                Transform root,
                Vector3 authoredScale)
            {
                Key = key;
                Prefab = prefab;
                Root = root;
                AuthoredScale = authoredScale;
            }

            public EntityId Key { get; }
            public GameObject Prefab { get; }
            public Transform Root { get; }
            public Vector3 AuthoredScale { get; }
            public Stack<PooledOneShotVfx> Inactive { get; } =
                new Stack<PooledOneShotVfx>();
            public HashSet<PooledOneShotVfx> Active { get; } =
                new HashSet<PooledOneShotVfx>();
            public int TotalCount { get; set; }
        }
    }
}
