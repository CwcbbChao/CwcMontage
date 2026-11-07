using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Lightweight, high-performance object pool built into Montage.
    /// Works out of the box with zero external dependencies, providing unified allocation and recycling for effects, prefabs, and audio sources.
    /// Strictly isolates the runtime environment from the editor 3D viewport preview, preventing main-scene pollution and memory leaks.
    /// </summary>
    public static class MontageObjectPool
    {
        #region Constants & Static Fields

        private const string RUNTIME_ROOT_NAME = "[Global_Montage_ObjectPool]";

        private static Transform _runtimeRoot;
        private static bool _isSceneEventSubscribed;

        private static readonly Dictionary<int, Stack<GameObject>> _runtimePrefabPools = new(32);
        private static readonly Dictionary<int, int> _instanceToPrefabMap = new(128);
        private static readonly Dictionary<int, Vector3> _prefabInitialScaleMap = new(32);
        private static readonly Queue<AudioSource> _audioSourcePool = new(16);
        private static readonly List<IMontagePoolable> _tempPoolables = new(8);
        private static readonly List<ParticleSystem> _tempParticleSystems = new(16);

        #endregion

        #region Properties

        /// <summary>
        /// Root node of the runtime object pool (persists across scenes).
        /// </summary>
        public static Transform RuntimeRoot
        {
            get
            {
                if (_runtimeRoot == null)
                {
                    var go = new GameObject(RUNTIME_ROOT_NAME);
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.DontDestroyOnLoad(go);
                    }
                    _runtimeRoot = go.transform;
                }
                EnsureSceneHook();
                return _runtimeRoot;
            }
        }

        #endregion

        #region Public Methods (GameObject Pool)

        /// <summary>
        /// Spawns or reuses a prefab instance from the object pool (runtime only).
        /// </summary>
        /// <param name="prefab">Target prefab</param>
        /// <param name="position">World position</param>
        /// <param name="rotation">World rotation</param>
        /// <param name="parent">Optional target parent Transform</param>
        /// <param name="scaleMultiplier">Scale multiplier</param>
        /// <returns>The instantiated or reused GameObject instance</returns>
        public static GameObject Spawn(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null,
            float scaleMultiplier = 1.0f)
        {
            if (prefab == null)
            {
                Debug.LogWarning("[MontageObjectPool] Cannot spawn object: prefab is null.");
                return null;
            }

            int prefabId = prefab.GetInstanceID();

            if (!_runtimePrefabPools.TryGetValue(prefabId, out var stack))
            {
                stack = new Stack<GameObject>(16);
                _runtimePrefabPools[prefabId] = stack;
            }

            if (!_prefabInitialScaleMap.TryGetValue(prefabId, out Vector3 initialScale))
            {
                initialScale = prefab.transform.localScale;
                _prefabInitialScaleMap[prefabId] = initialScale;
            }

            GameObject instance = null;
            while (stack.Count > 0)
            {
                instance = stack.Pop();
                if (instance != null)
                {
                    break;
                }
            }

            Transform targetParent = parent != null ? parent : RuntimeRoot;

            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(prefab, position, rotation, targetParent);
                instance.name = prefab.name;
                _instanceToPrefabMap[instance.GetInstanceID()] = prefabId;
            }
            else
            {
                if (instance.transform.parent != targetParent)
                {
                    instance.transform.SetParent(targetParent, false);
                }

                instance.transform.SetPositionAndRotation(position, rotation);
                instance.SetActive(true);
            }

            // Apply the exact scale
            instance.transform.localScale = initialScale * Mathf.Max(0.0001f, scaleMultiplier);

            // Trigger the IMontagePoolable interface if attached (zero GC allocation)
            _tempPoolables.Clear();
            instance.GetComponentsInChildren(true, _tempPoolables);
            for (int i = 0; i < _tempPoolables.Count; i++)
            {
                _tempPoolables[i].OnSpawnFromMontagePool();
            }
            _tempPoolables.Clear();

            return instance;
        }

        /// <summary>
        /// Returns the instance to the object pool (runtime only).
        /// </summary>
        /// <param name="instance">GameObject instance to recycle</param>
        public static void Recycle(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            // Trigger the IMontagePoolable interface (zero GC allocation)
            _tempPoolables.Clear();
            instance.GetComponentsInChildren(true, _tempPoolables);
            for (int i = 0; i < _tempPoolables.Count; i++)
            {
                _tempPoolables[i].OnRecycleToMontagePool();
            }
            _tempPoolables.Clear();

            // Particle system reset (zero GC allocation)
            _tempParticleSystems.Clear();
            instance.GetComponentsInChildren(true, _tempParticleSystems);
            for (int i = 0; i < _tempParticleSystems.Count; i++)
            {
                _tempParticleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _tempParticleSystems[i].Clear(true);
            }
            _tempParticleSystems.Clear();

            int instanceId = instance.GetInstanceID();

            if (_instanceToPrefabMap.TryGetValue(instanceId, out int prefabId) &&
                _runtimePrefabPools.TryGetValue(prefabId, out var stack))
            {
                if (_prefabInitialScaleMap.TryGetValue(prefabId, out Vector3 initialScale))
                {
                    instance.transform.localScale = initialScale;
                }

                instance.SetActive(false);
                if (instance.transform.parent != RuntimeRoot)
                {
                    instance.transform.SetParent(RuntimeRoot, false);
                }

                stack.Push(instance);
            }
            else
            {
                UnityEngine.Object.Destroy(instance);
            }
        }

        #endregion

        #region Public Methods (AudioSource Pool)

        /// <summary>
        /// Gets or creates a dedicated AudioSource component from the pool.
        /// </summary>
        /// <param name="position">Playback position</param>
        /// <param name="parent">Optional parent to attach to</param>
        /// <returns>An available AudioSource instance</returns>
        public static AudioSource GetAudioSource(Vector3 position, Transform parent = null)
        {
            AudioSource src = null;
            while (_audioSourcePool.Count > 0)
            {
                src = _audioSourcePool.Dequeue();
                if (src != null)
                {
                    break;
                }
            }

            if (src == null)
            {
                var go = new GameObject("Montage_AudioSource_Channel");
                go.transform.SetParent(RuntimeRoot, false);
                src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
            }

            Transform targetParent = parent != null ? parent : RuntimeRoot;
            if (src.transform.parent != targetParent)
            {
                src.transform.SetParent(targetParent, false);
            }

            src.transform.position = position;
            src.gameObject.SetActive(true);
            return src;
        }

        /// <summary>
        /// Recycles the AudioSource playback channel.
        /// </summary>
        /// <param name="src">AudioSource instance</param>
        public static void RecycleAudioSource(AudioSource src)
        {
            if (src == null)
            {
                return;
            }

            src.Stop();
            src.clip = null;
            src.gameObject.SetActive(false);

            if (_runtimeRoot != null && src.transform.parent != _runtimeRoot)
            {
                src.transform.SetParent(_runtimeRoot, false);
            }

            _audioSourcePool.Enqueue(src);
        }

        /// <summary>
        /// Completely clears all cached objects and mappings in the object pool, and safely destroys all pooled GameObject and AudioSource instances.
        /// Triggered automatically or manually on scene unload/switch, when returning to the main menu, or on a major memory warning.
        /// </summary>
        public static void Clear()
        {
            // 1. Completely destroy all cached prefab instances
            foreach (var kvp in _runtimePrefabPools)
            {
                var stack = kvp.Value;
                if (stack == null) continue;

                while (stack.Count > 0)
                {
                    var go = stack.Pop();
                    if (go != null)
                    {
                        UnityEngine.Object.Destroy(go);
                    }
                }
            }
            _runtimePrefabPools.Clear();
            _instanceToPrefabMap.Clear();
            _prefabInitialScaleMap.Clear();

            // 2. Completely destroy all cached AudioSource channel instances
            while (_audioSourcePool.Count > 0)
            {
                var src = _audioSourcePool.Dequeue();
                if (src != null && src.gameObject != null)
                {
                    UnityEngine.Object.Destroy(src.gameObject);
                }
            }
            _audioSourcePool.Clear();

            // 3. Fallback cleanup: safely destroy any remaining inactive children still attached under the root
            if (_runtimeRoot != null)
            {
                int childCount = _runtimeRoot.childCount;
                for (int i = childCount - 1; i >= 0; i--)
                {
                    var child = _runtimeRoot.GetChild(i);
                    if (child != null && child.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(child.gameObject);
                    }
                }
            }
        }

        /// <summary>
        /// Purges all cached instances of the specified prefab from the object pool (reserved for future tiered and whitelist-based cleanup policies).
        /// </summary>
        /// <param name="prefab">Target prefab</param>
        public static void PurgePrefab(GameObject prefab)
        {
            if (prefab == null) return;

            int prefabId = prefab.GetInstanceID();
            if (_runtimePrefabPools.TryGetValue(prefabId, out var stack))
            {
                while (stack.Count > 0)
                {
                    var go = stack.Pop();
                    if (go != null)
                    {
                        _instanceToPrefabMap.Remove(go.GetInstanceID());
                        UnityEngine.Object.Destroy(go);
                    }
                }
                _runtimePrefabPools.Remove(prefabId);
            }
            _prefabInitialScaleMap.Remove(prefabId);
        }

        #endregion

        #region Private Methods (Scene Lifecycle Hooks)

        /// <summary>
        /// Standard lifecycle reset for Unity 6 / Fast Enter Play Mode (domain reload disabled).
        /// Completely clears static caches and scene state when entering play mode, preventing cross-session state leakage.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFieldsOnEnterPlayMode()
        {
            _runtimeRoot = null;
            _isSceneEventSubscribed = false;
            _runtimePrefabPools.Clear();
            _instanceToPrefabMap.Clear();
            _prefabInitialScaleMap.Clear();
            _audioSourcePool.Clear();
            _tempPoolables.Clear();
            _tempParticleSystems.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeSceneEvents()
        {
            EnsureSceneHook();
        }

        private static void EnsureSceneHook()
        {
            if (!_isSceneEventSubscribed)
            {
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                _isSceneEventSubscribed = true;
            }
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            Clear();
        }

        #endregion
    }
}
