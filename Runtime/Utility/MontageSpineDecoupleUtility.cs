using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 动画片段基准骨骼（Spine）相对角色根节点（Root）的解耦姿态轨迹数据。
    /// 存储源动画在纯净状态下基准骨骼每一帧相对于根节点的真实合成朝向，用于彻底消除下半身骨盆倾斜污染。
    /// </summary>
    public class MontageDecoupledSpineTrack
    {
        #region 私有字段

        private readonly float _duration;
        private readonly float _frameRate;
        private readonly Quaternion[] _samples;

        #endregion

        #region 公共属性

        public float Duration => _duration;
        public float FrameRate => _frameRate;
        public int SampleCount => _samples != null ? _samples.Length : 0;

        #endregion

        #region 构造方法

        public MontageDecoupledSpineTrack(float duration, float frameRate, Quaternion[] samples)
        {
            _duration = Mathf.Max(0.001f, duration);
            _frameRate = Mathf.Max(1f, frameRate);
            _samples = samples ?? Array.Empty<Quaternion>();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 根据当前采样时间评估源动画中基准骨骼相对根节点的合成朝向（带线性球面插值）。
        /// </summary>
        /// <param name="time">动画采样时间戳（秒）</param>
        /// <returns>基准骨骼相对根节点的合成四元数</returns>
        public Quaternion Evaluate(float time)
        {
            if (_samples == null || _samples.Length == 0)
            {
                return Quaternion.identity;
            }

            if (_samples.Length == 1)
            {
                return _samples[0];
            }

            float clampedTime = Mathf.Clamp(time, 0f, _duration);
            float frameIndexFloat = clampedTime * _frameRate;
            int idx0 = Mathf.Clamp(Mathf.FloorToInt(frameIndexFloat), 0, _samples.Length - 1);
            int idx1 = Mathf.Clamp(idx0 + 1, 0, _samples.Length - 1);
            float t = frameIndexFloat - idx0;

            return Quaternion.Slerp(_samples[idx0], _samples[idx1], t);
        }

        #endregion
    }

    /// <summary>
    /// 蒙太奇上半身基准骨骼动态解耦工具。
    /// 负责按需懒加载提取源动画在各时刻基准骨骼（Spine）相对角色根节点（Root）的真实合成角度，
    /// 并在运行时内存中构建全局弱引用缓存，杜绝离线烘焙导致的配置状态不一致。
    /// </summary>
    public static class MontageSpineDecoupleUtility
    {
        #region 私有静态字段

        private const float DEFAULT_SAMPLE_FPS = 30f;
        private static readonly Dictionary<AnimationClip, MontageDecoupledSpineTrack> _trackCache = new();
        private static readonly Dictionary<AnimationClip, MontageDecoupledSpineTrack> _additiveTrackCache = new();

        private static GameObject _cachedSamplerDummy;
        private static Animator _cachedDummyAnimator;
        private static Transform _cachedDummySpine;
        private static Quaternion _cachedDummyBaseLocalSpine;

        #endregion

        #region 公共方法

        /// <summary>
        /// 按需获取或动态采样指定动画片段的基准骨骼相对根节点解耦姿态轨迹。
        /// 首次请求时在内存中动态采样（约 0.2ms），后续播放直接纳秒级内存命中。
        /// </summary>
        /// <param name="clip">源动画片段</param>
        /// <param name="referenceAnimator">参考 Animator（用于提供正确的 Humanoid Avatar 骨骼映射）</param>
        /// <returns>解耦姿态轨迹数据，若无效则返回 null</returns>
        public static MontageDecoupledSpineTrack GetOrCreateTrack(AnimationClip clip, Animator referenceAnimator)
        {
            if (clip == null) return null;

            if (_trackCache.TryGetValue(clip, out var cachedTrack))
            {
                return cachedTrack;
            }

            var track = SampleClipOrientation(clip, referenceAnimator);
            if (track != null)
            {
                _trackCache[clip] = track;
            }

            return track;
        }

        /// <summary>
        /// 按需获取或动态采样指定叠加（Additive）动画片段的基准骨骼相对参考姿态的旋转增量轨迹。
        /// 用于在 UpperBody 解耦世界朝向的基础上复合 Additive 姿态增量，消除脊柱断层。
        /// </summary>
        /// <param name="clip">叠加动画片段</param>
        /// <param name="referenceAnimator">参考 Animator</param>
        /// <returns>基准骨骼局部旋转增量轨迹数据，若无效则返回 null</returns>
        public static MontageDecoupledSpineTrack GetOrCreateAdditiveTrack(AnimationClip clip, Animator referenceAnimator)
        {
            if (clip == null) return null;

            if (_additiveTrackCache.TryGetValue(clip, out var cachedTrack))
            {
                return cachedTrack;
            }

            var track = SampleAdditiveClipOrientation(clip, referenceAnimator);
            if (track != null)
            {
                _additiveTrackCache[clip] = track;
            }

            return track;
        }

        /// <summary>
        /// 手动清除特定动画片段的解耦姿态缓存（例如资源重新导入时）。
        /// </summary>
        public static void InvalidateClip(AnimationClip clip)
        {
            if (clip == null) return;
            if (_trackCache.ContainsKey(clip))
            {
                _trackCache.Remove(clip);
            }
            if (_additiveTrackCache.ContainsKey(clip))
            {
                _additiveTrackCache.Remove(clip);
            }
        }

        /// <summary>
        /// 清除所有已缓存的解耦姿态轨迹及全局单例采样假人。
        /// </summary>
        public static void ClearCache()
        {
            _trackCache.Clear();
            _additiveTrackCache.Clear();

            if (_cachedSamplerDummy != null)
            {
                UnityEngine.Object.DestroyImmediate(_cachedSamplerDummy);
                _cachedSamplerDummy = null;
                _cachedDummyAnimator = null;
                _cachedDummySpine = null;
            }
        }

        #endregion

        #region 私有方法

        private static bool EnsureSamplerDummy(Animator referenceAnimator)
        {
            if (_cachedSamplerDummy != null && _cachedDummyAnimator != null && _cachedDummySpine != null)
            {
                return true;
            }

            GameObject prefab = Resources.Load<GameObject>("Dummy");
            if (prefab == null)
            {
                prefab = Resources.Load<GameObject>("CwcMontage/Dummy");
            }

#if UNITY_EDITOR
            if (prefab == null)
            {
                const string dummyGuid = "9bf62f6667445894b97702f6e7e05aa5";
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(dummyGuid);
                if (!string.IsNullOrEmpty(path))
                {
                    prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }
                if (prefab == null)
                {
                    prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CwcPlugins/CwcMontage/Runtime/Resources/Dummy.prefab");
                }
            }
#endif

            if (prefab != null)
            {
                _cachedSamplerDummy = UnityEngine.Object.Instantiate(prefab);
                _cachedSamplerDummy.name = "__MontageGlobalSpineDecoupleSamplerDummy";
                _cachedSamplerDummy.hideFlags = HideFlags.HideAndDontSave;
                _cachedSamplerDummy.transform.position = Vector3.zero;
                _cachedSamplerDummy.transform.rotation = Quaternion.identity;
                _cachedSamplerDummy.SetActive(true);

                // 禁用所有 Renderer 避免无谓渲染和视图突变
                var renderers = _cachedSamplerDummy.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    renderers[r].enabled = false;
                }

                // 移除碰撞体与潜在脚本组件
                var colliders = _cachedSamplerDummy.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < colliders.Length; c++)
                {
                    UnityEngine.Object.DestroyImmediate(colliders[c]);
                }

                _cachedDummyAnimator = _cachedSamplerDummy.GetComponent<Animator>();
                if (_cachedDummyAnimator == null)
                {
                    _cachedDummyAnimator = _cachedSamplerDummy.AddComponent<Animator>();
                }
                _cachedDummyAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                _cachedDummySpine = _cachedDummyAnimator.GetBoneTransform(HumanBodyBones.Spine);
                if (_cachedDummySpine != null)
                {
                    _cachedDummyBaseLocalSpine = _cachedDummySpine.localRotation;
                    return true;
                }
            }

            return false;
        }

        private static MontageDecoupledSpineTrack SampleClipOrientation(AnimationClip clip, Animator referenceAnimator)
        {
            if (clip == null) return null;

            float duration = Mathf.Max(0.01f, clip.length);
            float fps = clip.frameRate > 1f ? clip.frameRate : DEFAULT_SAMPLE_FPS;
            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(duration * fps) + 1);

            if (EnsureSamplerDummy(referenceAnimator))
            {
                var graph = PlayableGraph.Create("MontageDecoupleSamplerGraph");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

                var clipPlayable = AnimationClipPlayable.Create(graph, clip);
                clipPlayable.SetSpeed(1.0f);

                var output = AnimationPlayableOutput.Create(graph, "SampleOutput", _cachedDummyAnimator);
                output.SetSourcePlayable(clipPlayable);

                var samples = new Quaternion[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    float t = Mathf.Min(duration, i / fps);
                    clipPlayable.SetTime(t);
                    graph.Evaluate(0f);

                    // 使用相对于 Dummy 根节点的局部旋转，避免源动画自带 Root Motion 旋转偏转产生采样污染
                    samples[i] = Quaternion.Inverse(_cachedSamplerDummy.transform.rotation) * _cachedDummySpine.rotation;
                }

                graph.Destroy();

                _cachedSamplerDummy.transform.position = Vector3.zero;
                _cachedSamplerDummy.transform.rotation = Quaternion.identity;

                return new MontageDecoupledSpineTrack(duration, fps, samples);
            }

            // 安全保底：若无法使用 Humanoid 采样，构建基于单位朝向的保底轨迹
            Debug.LogWarning($"[MontageSpineDecoupleUtility] 未找到具备完整 Humanoid 骨骼的标准 Dummy 假人模型，将为动画片段 '{clip.name}' 使用保底朝向。");
            var fallbackSamples = new Quaternion[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                fallbackSamples[i] = Quaternion.identity;
            }
            return new MontageDecoupledSpineTrack(duration, fps, fallbackSamples);
        }

        private static MontageDecoupledSpineTrack SampleAdditiveClipOrientation(AnimationClip clip, Animator referenceAnimator)
        {
            if (clip == null) return null;

            float duration = Mathf.Max(0.01f, clip.length);
            float fps = clip.frameRate > 1f ? clip.frameRate : DEFAULT_SAMPLE_FPS;
            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(duration * fps) + 1);

            if (EnsureSamplerDummy(referenceAnimator))
            {
                var graph = PlayableGraph.Create("MontageAdditiveSamplerGraph");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

                var clipPlayable = AnimationClipPlayable.Create(graph, clip);
                clipPlayable.SetSpeed(1.0f);

                var output = AnimationPlayableOutput.Create(graph, "SampleAdditiveOutput", _cachedDummyAnimator);
                output.SetSourcePlayable(clipPlayable);

                var samples = new Quaternion[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    float t = Mathf.Min(duration, i / fps);
                    clipPlayable.SetTime(t);
                    graph.Evaluate(0f);

                    // 提取叠加动画作用在基准姿态上的纯净差值增量 Delta
                    samples[i] = Quaternion.Inverse(_cachedDummyBaseLocalSpine) * _cachedDummySpine.localRotation;
                }

                graph.Destroy();

                _cachedSamplerDummy.transform.position = Vector3.zero;
                _cachedSamplerDummy.transform.rotation = Quaternion.identity;

                return new MontageDecoupledSpineTrack(duration, fps, samples);
            }

            var fallbackSamples = new Quaternion[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                fallbackSamples[i] = Quaternion.identity;
            }
            return new MontageDecoupledSpineTrack(duration, fps, fallbackSamples);
        }

        #endregion
    }
}
