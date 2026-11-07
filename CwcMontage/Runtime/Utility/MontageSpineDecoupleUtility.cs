using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Decoupled pose trajectory data for an animation clip's reference bone (Spine) relative to the character root (Root).
    /// Stores the true composed orientation of the reference bone relative to the root for every frame of the source animation in its clean state, used to fully eliminate lower-body pelvis tilt contamination.
    /// </summary>
    public class MontageDecoupledSpineTrack
    {
        #region Private Fields

        private readonly float _duration;
        private readonly float _frameRate;
        private readonly Quaternion[] _samples;

        #endregion

        #region Properties

        public float Duration => _duration;
        public float FrameRate => _frameRate;
        public int SampleCount => _samples != null ? _samples.Length : 0;

        #endregion

        #region Constructors

        public MontageDecoupledSpineTrack(float duration, float frameRate, Quaternion[] samples)
        {
            _duration = Mathf.Max(0.001f, duration);
            _frameRate = Mathf.Max(1f, frameRate);
            _samples = samples ?? Array.Empty<Quaternion>();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Evaluates the composed orientation of the reference bone relative to the root in the source animation at the given sample time (with spherical linear interpolation).
        /// </summary>
        /// <param name="time">Animation sample timestamp in seconds.</param>
        /// <returns>Composed quaternion of the reference bone relative to the root.</returns>
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
    /// Dynamic upper-body reference bone decoupling utility for montage.
    /// Lazily extracts the true composed rotation of the reference bone (Spine) relative to the character root (Root) at each moment of the source animation on demand,
    /// and builds a global weak-reference cache in runtime memory to avoid configuration state inconsistency caused by offline baking.
    /// </summary>
    public static class MontageSpineDecoupleUtility
    {
        #region Private Static Fields

        private const float DEFAULT_SAMPLE_FPS = 30f;
        private static readonly Dictionary<AnimationClip, MontageDecoupledSpineTrack> _trackCache = new();
        private static readonly Dictionary<AnimationClip, MontageDecoupledSpineTrack> _additiveTrackCache = new();

        private static GameObject _cachedSamplerDummy;
        private static Animator _cachedDummyAnimator;
        private static Transform _cachedDummySpine;
        private static Quaternion _cachedDummyBaseLocalSpine;

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets or dynamically samples the decoupled pose trajectory of the reference bone relative to the root for the specified animation clip on demand.
        /// Samples dynamically in memory on first request (about 0.2 ms); subsequent playback hits the in-memory cache in nanoseconds.
        /// </summary>
        /// <param name="clip">Source animation clip.</param>
        /// <param name="referenceAnimator">Reference Animator (provides the correct Humanoid Avatar bone mapping).</param>
        /// <returns>Decoupled pose trajectory data, or null if invalid.</returns>
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
        /// Gets or dynamically samples the rotation delta trajectory of the reference bone relative to the reference pose for the specified additive animation clip on demand.
        /// Used to compose the additive pose delta on top of the UpperBody decoupled world orientation, eliminating spine discontinuity.
        /// </summary>
        /// <param name="clip">Additive animation clip.</param>
        /// <param name="referenceAnimator">Reference Animator.</param>
        /// <returns>Local rotation delta trajectory data of the reference bone, or null if invalid.</returns>
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
        /// Manually clears the decoupled pose cache for a specific animation clip (for example, when the asset is reimported).
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
        /// Clears all cached decoupled pose trajectories and the global singleton sampling dummy.
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

        #region Private Methods

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

                // Disable all Renderers to avoid unnecessary rendering and view changes.
                var renderers = _cachedSamplerDummy.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    renderers[r].enabled = false;
                }

                // Remove colliders and potential script components.
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

                    // Use rotation local to the Dummy root to avoid sampling contamination from the source animation's built-in root motion rotation.
                    samples[i] = Quaternion.Inverse(_cachedSamplerDummy.transform.rotation) * _cachedDummySpine.rotation;
                }

                graph.Destroy();

                _cachedSamplerDummy.transform.position = Vector3.zero;
                _cachedSamplerDummy.transform.rotation = Quaternion.identity;

                return new MontageDecoupledSpineTrack(duration, fps, samples);
            }

            // Safety fallback: if Humanoid sampling is unavailable, build a fallback trajectory based on identity orientation.
            Debug.LogWarning($"[MontageSpineDecoupleUtility] No standard Dummy model with a complete Humanoid skeleton was found; using fallback orientation for animation clip '{clip.name}'.");
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

                    // Extract the pure delta increment applied by the additive animation on top of the reference pose.
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
