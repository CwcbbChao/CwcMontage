using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇内置轻量高性能对象池。
    /// 开箱即用、零外部依赖，提供特效/预制件/音频源的统一分配与回收，
    /// 支持运行时与编辑器 3D 视口预览环境的严格隔离，杜绝主场景污染与内存泄漏。
    /// </summary>
    public static class MontageObjectPool
    {
        #region 常量与静态字段

        private const string RUNTIME_ROOT_NAME = "[Global_Montage_ObjectPool]";

        private static Transform _runtimeRoot;
        private static bool _isSceneEventSubscribed;

        private static readonly Dictionary<int, Stack<GameObject>> _runtimePrefabPools = new(32);
        private static readonly Dictionary<int, int> _instanceToPrefabMap = new(128);
        private static readonly Dictionary<int, Vector3> _prefabInitialScaleMap = new(32);
        private static readonly Queue<AudioSource> _audioSourcePool = new(16);

        #endregion

        #region 公共属性

        /// <summary>
        /// 运行时对象池根节点（常驻场景）。
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

        #region 公共方法 (GameObject 生成与回收 - 运行时对象池)

        /// <summary>
        /// 从对象池中生成或复用一个 Prefab 实例（运行时专用）。
        /// </summary>
        /// <param name="prefab">目标预制件</param>
        /// <param name="position">世界坐标</param>
        /// <param name="rotation">世界旋转</param>
        /// <param name="parent">目标父级 Transform（可选）</param>
        /// <param name="scaleMultiplier">缩放倍率</param>
        /// <returns>实例化或取出的 GameObject 实例</returns>
        public static GameObject Spawn(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null,
            float scaleMultiplier = 1.0f)
        {
            if (prefab == null)
            {
                Debug.LogWarning("[MontageObjectPool] 无法生成对象：prefab 为空。");
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

            // 应用准确缩放
            instance.transform.localScale = initialScale * Mathf.Max(0.0001f, scaleMultiplier);

            // 触发可能挂载的 IMontagePoolable 接口
            var poolables = instance.GetComponentsInChildren<IMontagePoolable>(true);
            for (int i = 0; i < poolables.Length; i++)
            {
                poolables[i].OnSpawnFromMontagePool();
            }

            return instance;
        }

        /// <summary>
        /// 将实例归还回对象池（运行时专用）。
        /// </summary>
        /// <param name="instance">要回收的 GameObject 实例</param>
        public static void Recycle(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            // 触发 IMontagePoolable 接口
            var poolables = instance.GetComponentsInChildren<IMontagePoolable>(true);
            for (int i = 0; i < poolables.Length; i++)
            {
                poolables[i].OnRecycleToMontagePool();
            }

            // 粒子系统重置
            var particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystems[i].Clear(true);
            }

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

        #region 公共方法 (AudioSource 通道池)

        /// <summary>
        /// 从池中获取或创建一个专用的 AudioSource 音频播放组件。
        /// </summary>
        /// <param name="position">播放位置</param>
        /// <param name="parent">挂载父级（可选）</param>
        /// <returns>可用的 AudioSource 实例</returns>
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
        /// 回收 AudioSource 播放通道。
        /// </summary>
        /// <param name="src">AudioSource 实例</param>
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
        /// 彻底清空对象池中的所有缓存对象与映射关系，并安全销毁池中所有暂存的 GameObject 与 AudioSource 实例。
        /// 在场景卸载切换、返回主菜单或收到重大内存告警时自动或手动触发。
        /// </summary>
        public static void Clear()
        {
            // 1. 彻底销毁所有缓存的 Prefab 实例
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

            // 2. 彻底销毁所有缓存的 AudioSource 通道实例
            while (_audioSourcePool.Count > 0)
            {
                var src = _audioSourcePool.Dequeue();
                if (src != null && src.gameObject != null)
                {
                    UnityEngine.Object.Destroy(src.gameObject);
                }
            }
            _audioSourcePool.Clear();

            // 3. 保底清理：若根节点下仍有残余挂载的未激活子节点，彻底安全销毁
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
        /// 定向清理指定预制件在对象池中的所有缓存实例（为后续分级与白名单清理策略预留支持）。
        /// </summary>
        /// <param name="prefab">目标预制件</param>
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

        #region 私有方法 (场景事件监听与生命周期挂钩)

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
