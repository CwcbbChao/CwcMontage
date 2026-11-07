using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Montage configuration asset (ScriptableObject - Legacy).
    /// Holds animation segments, blend transitions, Root Motion filters, sections, and multi-track action blocks.
    /// Implements IMontageSequence for 100% backward compatibility with the new runtime engine.
    /// </summary>
    [CreateAssetMenu(fileName = "Montage_", menuName = "Cwc/Montage/Montage Sequence (Legacy)")]
    public class MontageSequenceSO : ScriptableObject, IMontageSequence
    {
        #region Serialized Fields

        [Tooltip("Enables the upper-body animation track (blended over base locomotion).")]
        [SerializeField] private bool _enableUpperBodyTrack;

        [Tooltip("Overall weight for the upper-body layer (0.0 to 1.0).")]
        [Range(0f, 1f)]
        [SerializeField] private float _upperBodyWeight = 1.0f;

        [Tooltip("Animation segments for the upper-body track.")]
        [SerializeField] private List<MontageAnimationSegment> _upperBodySegments = new();

        [Tooltip("Overall weight for the full-body layer (0.0 to 1.0).")]
        [Range(0f, 1f)]
        [SerializeField] private float _fullBodyWeight = 1.0f;

        [Tooltip("Animation segments for the full-body track (main track for rolls, heavy attacks, executions).")]
        [SerializeField] private List<MontageAnimationSegment> _animationSegments = new();

        [Tooltip("Enables the additive animation track (flinches, recoil).")]
        [SerializeField] private bool _enableAdditiveTrack;

        [Tooltip("Overall weight for the additive layer (0.0 to 1.0).")]
        [Range(0f, 1f)]
        [SerializeField] private float _additiveWeight = 1.0f;

        [Tooltip("Animation segments for the additive track.")]
        [SerializeField] private List<MontageAnimationSegment> _additiveSegments = new();

        [Tooltip("Enables upper-body Spine orientation compensation to eliminate pelvis sway during locomotion.")]
        [SerializeField] private bool _decoupleUpperBodyOrientation = true;

        [Tooltip("Optional custom AvatarMask for this montage sequence (leaves empty to use default humanoid mask).")]
        [SerializeField] private AvatarMask _customUpperBodyMask;

#if UNITY_EDITOR
        [Header("Editor Preview Settings")]
        [Tooltip("Enables a custom preview model for this montage asset in the editor window.")]
        [SerializeField] private bool _overridePreviewModel;

        [Tooltip("Custom preview model prefab for this montage asset.")]
        [SerializeField] private GameObject _previewModel;
#endif

        [HideInInspector]
        [SerializeField] private int _animationLayer = 0;

        [Tooltip("Enables Foot IK during this montage playback.")]
        [SerializeField] private bool _isFootIK;

        [Header("Root Motion Settings")]
        [Tooltip("Applies planar (X/Z) Root Motion displacement.")]
        [SerializeField] private bool _applyHorizontalRootMotion;

        [Tooltip("Applies vertical (Y) Root Motion displacement.")]
        [SerializeField] private bool _applyVerticalRootMotion;

        [Tooltip("Applies Root Motion rotation.")]
        [SerializeField] private bool _applyRotationRootMotion;

        [Header("Sections")]
        [Tooltip("Split timestamps in seconds. N split points divide the timeline into N + 1 sections.")]
        [SerializeField] private List<float> _splitTimestamps = new();

        [Header("Tracks & Action Blocks")]
        [Tooltip("List of action tracks for audiovisual and gameplay events.")]
        [SerializeField] private List<MontageTrackData> _tracks = new();

        [HideInInspector]
        [SerializeField] private List<string> _genericSampleBonePaths = new();

        #endregion

        #region Properties

        /// <summary>
        /// 泛型动作特征骨骼相对路径抽样指纹（仅在双方皆为泛型骨骼时用于快速特征比对）。
        /// </summary>
        public IReadOnlyList<string> GenericSampleBonePaths => _genericSampleBonePaths;

        /// <summary>
        /// Whether the upper-body animation track is active.
        /// </summary>
        public bool EnableUpperBodyTrack
        {
            get => _enableUpperBodyTrack;
            set => _enableUpperBodyTrack = value;
        }

        /// <summary>
        /// Shortcut alias for the upper-body track.
        /// </summary>
        public bool EnableUpperBody
        {
            get => _enableUpperBodyTrack;
            set => _enableUpperBodyTrack = value;
        }

        /// <summary>
        /// Whether to decouple the upper-body base bone orientation from the root pose.
        /// </summary>
        public bool DecoupleUpperBodyOrientation
        {
            get => _decoupleUpperBodyOrientation;
            set => _decoupleUpperBodyOrientation = value;
        }

        /// <summary>
        /// Upper-body mask bound specifically to this montage (optional).
        /// </summary>
        public AvatarMask CustomUpperBodyMask
        {
            get => _customUpperBodyMask;
            set => _customUpperBodyMask = value;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Whether to enable a dedicated preview model for this montage asset.
        /// </summary>
        public bool OverridePreviewModel
        {
            get => _overridePreviewModel;
            set => _overridePreviewModel = value;
        }

        /// <summary>
        /// Dedicated preview model prefab bound to this montage.
        /// </summary>
        public GameObject PreviewModel
        {
            get => _previewModel;
            set => _previewModel = value;
        }
#endif

        /// <summary>
        /// Upper-body animation segment list.
        /// </summary>
        public List<MontageAnimationSegment> UpperBodySegments => _upperBodySegments;

        /// <summary>
        /// Overall upper-body layer weight [0.0, 1.0].
        /// </summary>
        public float UpperBodyWeight
        {
            get => _upperBodyWeight;
            set => _upperBodyWeight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Full-body core animation segment list (main track).
        /// </summary>
        public List<MontageAnimationSegment> FullBodySegments => _animationSegments;

        /// <summary>
        /// Overall full-body core layer weight [0.0, 1.0].
        /// </summary>
        public float FullBodyWeight
        {
            get => _fullBodyWeight;
            set => _fullBodyWeight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Whether the additive animation track is active.
        /// </summary>
        public bool EnableAdditiveTrack
        {
            get => _enableAdditiveTrack;
            set => _enableAdditiveTrack = value;
        }

        /// <summary>
        /// Shortcut alias for the additive track.
        /// </summary>
        public bool EnableAdditive
        {
            get => _enableAdditiveTrack;
            set => _enableAdditiveTrack = value;
        }

        /// <summary>
        /// Additive animation segment list.
        /// </summary>
        public List<MontageAnimationSegment> AdditiveSegments => _additiveSegments;

        /// <summary>
        /// Overall additive layer weight [0.0, 1.0].
        /// </summary>
        public float AdditiveWeight
        {
            get => _additiveWeight;
            set => _additiveWeight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Full-body animation segment list (kept for backward compatibility).
        /// </summary>
        public List<MontageAnimationSegment> AnimationSegments => _animationSegments;

        /// <summary>
        /// First core animation clip (read-only convenience property; points to the clip of the first valid segment).
        /// </summary>
        public AnimationClip AnimationClip
        {
            get
            {
                if (_animationSegments != null)
                {
                    for (int i = 0; i < _animationSegments.Count; i++)
                    {
                        if (_animationSegments[i]?.Clip != null)
                        {
                            return _animationSegments[i].Clip;
                        }
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Default playback layer.
        /// </summary>
        public int AnimationLayer
        {
            get => _animationLayer;
            set => _animationLayer = Mathf.Max(0, value);
        }

        /// <summary>
        /// Whether foot IK is enabled.
        /// </summary>
        public bool IsFootIK
        {
            get => _isFootIK;
            set => _isFootIK = value;
        }

        /// <summary>
        /// Whether horizontal Root Motion is applied.
        /// </summary>
        public bool ApplyHorizontalRootMotion => _applyHorizontalRootMotion;

        /// <summary>
        /// Whether vertical Root Motion is applied.
        /// </summary>
        public bool ApplyVerticalRootMotion => _applyVerticalRootMotion;

        /// <summary>
        /// Whether rotation Root Motion is applied.
        /// </summary>
        public bool ApplyRotationRootMotion => _applyRotationRootMotion;

        /// <summary>
        /// Total animation duration (seconds; the latest end time among all animation segments, track action blocks, and split points).
        /// </summary>
        public float TotalDuration
        {
            get
            {
                float maxEndTime = 0.0f;

                // 1. Animation segments on the core animation tracks (FullBody, UpperBody, Additive)
                if (_animationSegments != null)
                {
                    for (int i = 0; i < _animationSegments.Count; i++)
                    {
                        var seg = _animationSegments[i];
                        if (seg != null && seg.EndTime > maxEndTime)
                        {
                            maxEndTime = seg.EndTime;
                        }
                    }
                }

                if (_enableUpperBodyTrack && _upperBodySegments != null)
                {
                    for (int i = 0; i < _upperBodySegments.Count; i++)
                    {
                        var seg = _upperBodySegments[i];
                        if (seg != null && seg.EndTime > maxEndTime)
                        {
                            maxEndTime = seg.EndTime;
                        }
                    }
                }

                if (_enableAdditiveTrack && _additiveSegments != null)
                {
                    for (int i = 0; i < _additiveSegments.Count; i++)
                    {
                        var seg = _additiveSegments[i];
                        if (seg != null && seg.EndTime > maxEndTime)
                        {
                            maxEndTime = seg.EndTime;
                        }
                    }
                }

                // 2. Action blocks on the presentation tracks
                if (_tracks != null)
                {
                    for (int i = 0; i < _tracks.Count; i++)
                    {
                        var track = _tracks[i];
                        if (track?.ActionBlocks == null) continue;
                        for (int j = 0; j < track.ActionBlocks.Count; j++)
                        {
                            var block = track.ActionBlocks[j];
                            if (block != null && block.EndTime > maxEndTime)
                            {
                                maxEndTime = block.EndTime;
                            }
                        }
                    }
                }

                // 3. Physical section split points
                if (_splitTimestamps != null)
                {
                    for (int i = 0; i < _splitTimestamps.Count; i++)
                    {
                        if (_splitTimestamps[i] > maxEndTime)
                        {
                            maxEndTime = _splitTimestamps[i];
                        }
                    }
                }

                return maxEndTime;
            }
        }

        /// <summary>
        /// Authoritative natural end timestamp of a single montage playback (seconds; accounts for all segment post blend-outs).
        /// </summary>
        public float NaturalEndTime
        {
            get
            {
                float maxEndTime = 0.0f;

                if (_animationSegments != null)
                {
                    for (int i = 0; i < _animationSegments.Count; i++)
                    {
                        var seg = _animationSegments[i];
                        if (seg != null && seg.PostEndTime > maxEndTime) maxEndTime = seg.PostEndTime;
                    }
                }

                if (_enableUpperBodyTrack && _upperBodySegments != null)
                {
                    for (int i = 0; i < _upperBodySegments.Count; i++)
                    {
                        var seg = _upperBodySegments[i];
                        if (seg != null && seg.PostEndTime > maxEndTime) maxEndTime = seg.PostEndTime;
                    }
                }

                if (_enableAdditiveTrack && _additiveSegments != null)
                {
                    for (int i = 0; i < _additiveSegments.Count; i++)
                    {
                        var seg = _additiveSegments[i];
                        if (seg != null && seg.PostEndTime > maxEndTime) maxEndTime = seg.PostEndTime;
                    }
                }

                if (_tracks != null)
                {
                    for (int i = 0; i < _tracks.Count; i++)
                    {
                        var track = _tracks[i];
                        if (track?.ActionBlocks == null) continue;
                        for (int j = 0; j < track.ActionBlocks.Count; j++)
                        {
                            var block = track.ActionBlocks[j];
                            if (block != null && block.EndTime > maxEndTime) maxEndTime = block.EndTime;
                        }
                    }
                }

                if (_splitTimestamps != null)
                {
                    for (int i = 0; i < _splitTimestamps.Count; i++)
                    {
                        if (_splitTimestamps[i] > maxEndTime) maxEndTime = _splitTimestamps[i];
                    }
                }

                return Mathf.Max(maxEndTime, TotalDuration);
            }
        }

        /// <summary>
        /// Base animation frame rate (prefers the sample frame rate of the first valid segment).
        /// </summary>
        public float FrameRate
        {
            get
            {
                if (_animationSegments != null)
                {
                    for (int i = 0; i < _animationSegments.Count; i++)
                    {
                        var clip = _animationSegments[i]?.Clip;
                        if (clip != null && clip.frameRate > 0.01f)
                        {
                            return clip.frameRate;
                        }
                    }
                }

                return 30.0f;
            }
        }

        /// <summary>
        /// Total animation frame count.
        /// </summary>
        public int TotalFrames => Mathf.RoundToInt(TotalDuration * FrameRate);

        /// <summary>
        /// Total number of physical sections (always split point count + 1).
        /// </summary>
        public int SectionCount => _splitTimestamps != null ? _splitTimestamps.Count + 1 : 1;

        /// <summary>
        /// Read-only list of split point timestamps.
        /// </summary>
        public IReadOnlyList<float> SplitTimestamps => _splitTimestamps;

        /// <summary>
        /// Presentation track list.
        /// </summary>
        public List<MontageTrackData> Tracks => _tracks;

        /// <summary>
        /// Read-only list of baked, flattened runtime action blocks.
        /// Filters out all muted tracks and disabled blocks, sorts by start time, and stays in memory for the lifetime,
        /// so all MontagePlayer instances can read it directly, eliminating runtime cloning and GC heap allocations.
        /// </summary>
        public IReadOnlyList<MontageActionBlockData> BakedRuntimeActionBlocks
        {
            get
            {
                if (_bakedRuntimeActionBlocks == null)
                {
                    BakeRuntimeActionBlocks();
                }
                return _bakedRuntimeActionBlocks;
            }
        }

        #endregion

        #region Private Non-Serialized Fields (Runtime Cache)

        [NonSerialized] private List<MontageActionBlockData> _bakedRuntimeActionBlocks;

        #endregion

        #region IMontageSequence 契约显式实现

        IReadOnlyList<MontageAnimationSegment> IMontageSequence.UpperBodySegments => _upperBodySegments;
        IReadOnlyList<MontageAnimationSegment> IMontageSequence.FullBodySegments => _animationSegments;
        IReadOnlyList<MontageAnimationSegment> IMontageSequence.AdditiveSegments => _additiveSegments;
        IReadOnlyList<MontageTrackData> IMontageSequence.Tracks => _tracks;

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            InvalidateBakedBlocks();
        }

        private void OnValidate()
        {
            EnsureSegmentsValid();
            ValidateActionBlocks();
            InvalidateBakedBlocks();
#if UNITY_EDITOR
            BakeGenericSampleBonePaths();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// 自动提取泛型动作的前 3 根关键特征骨骼路径指纹（用于运行时泛型模型保底匹配）。
        /// </summary>
        public virtual void BakeGenericSampleBonePaths()
        {
            _genericSampleBonePaths.Clear();
            var clip = AnimationClip;
            if (clip == null || clip.isHumanMotion) return;

            var bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip);
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < bindings.Length; i++)
            {
                string path = bindings[i].path;
                if (!string.IsNullOrEmpty(path) && seen.Add(path))
                {
                    _genericSampleBonePaths.Add(path);
                    if (_genericSampleBonePaths.Count >= 3) break;
                }
            }
        }
#endif

        #endregion

        #region Public Methods (Section & Time Warping)

        /// <summary>
        /// Gets whether the skeleton type of the first valid animation clip is humanoid.
        /// Returns null if there is no valid segment.
        /// </summary>
        public bool? GetAnchorSkeletonIsHuman()
        {
            if (_animationSegments != null)
            {
                for (int i = 0; i < _animationSegments.Count; i++)
                {
                    var clip = _animationSegments[i]?.Clip;
                    if (clip != null) return clip.isHumanMotion;
                }
            }
            if (_upperBodySegments != null)
            {
                for (int i = 0; i < _upperBodySegments.Count; i++)
                {
                    var clip = _upperBodySegments[i]?.Clip;
                    if (clip != null) return clip.isHumanMotion;
                }
            }
            if (_additiveSegments != null)
            {
                for (int i = 0; i < _additiveSegments.Count; i++)
                {
                    var clip = _additiveSegments[i]?.Clip;
                    if (clip != null) return clip.isHumanMotion;
                }
            }
            return null;
        }

        /// <summary>
        /// Invalidates the cached flattened runtime action block list and rebakes it on next access.
        /// </summary>
        public void InvalidateBakedBlocks()
        {
            _bakedRuntimeActionBlocks = null;
        }

        /// <summary>
        /// Validates and fixes action blocks on all tracks, enforcing legal start/end frames and duration (at least 1 frame).
        /// </summary>
        public void ValidateActionBlocks()
        {
            if (_tracks == null) return;

            float fps = FrameRate;
            for (int i = 0; i < _tracks.Count; i++)
            {
                var track = _tracks[i];
                if (track?.ActionBlocks == null) continue;

                for (int j = 0; j < track.ActionBlocks.Count; j++)
                {
                    track.ActionBlocks[j]?.EnsureValid(fps);
                }
            }
        }

        private void BakeRuntimeActionBlocks()
        {
            if (_bakedRuntimeActionBlocks == null)
            {
                _bakedRuntimeActionBlocks = new List<MontageActionBlockData>(16);
            }
            else
            {
                _bakedRuntimeActionBlocks.Clear();
            }

            if (_tracks == null) return;

            float fps = FrameRate;
            for (int i = 0; i < _tracks.Count; i++)
            {
                var track = _tracks[i];
                if (track == null || track.IsMuted || track.ActionBlocks == null) continue;

                for (int j = 0; j < track.ActionBlocks.Count; j++)
                {
                    var blockData = track.ActionBlocks[j];
                    if (blockData != null && blockData.IsEnabled && blockData.Action != null)
                    {
                        blockData.EnsureValid(fps);
                        _bakedRuntimeActionBlocks.Add(blockData);
                    }
                }
            }

            _bakedRuntimeActionBlocks.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        }

        /// <summary>
        /// Gets the raw physical duration of the specified physical section (seconds).
        /// </summary>
        /// <param name="sectionIndex">Section index (zero-based)</param>
        /// <returns>Section duration in seconds</returns>
        public float GetSectionDuration(int sectionIndex)
        {
            var range = GetSectionRange(sectionIndex);
            return Mathf.Max(0f, range.end - range.start);
        }

        /// <summary>
        /// Gets the start and end time range [start, end] of the specified section on the timeline.
        /// </summary>
        /// <param name="sectionIndex">Section index (zero-based)</param>
        /// <returns>Time tuple containing start and end</returns>
        public (float start, float end) GetSectionRange(int sectionIndex)
        {
            float total = TotalDuration;
            if (_splitTimestamps == null || _splitTimestamps.Count == 0)
            {
                return (0f, total);
            }

            int count = _splitTimestamps.Count;
            if (sectionIndex <= 0)
            {
                float firstSplit = Mathf.Clamp(_splitTimestamps[0], 0f, total);
                return (0f, firstSplit);
            }

            if (sectionIndex >= count)
            {
                float lastSplit = Mathf.Clamp(_splitTimestamps[count - 1], 0f, total);
                return (lastSplit, total);
            }

            float prevSplit = Mathf.Clamp(_splitTimestamps[sectionIndex - 1], 0f, total);
            float currSplit = Mathf.Clamp(_splitTimestamps[sectionIndex], 0f, total);
            return (prevSplit, currSplit);
        }

        /// <summary>
        /// Finds the physical section index that contains the given absolute timestamp.
        /// </summary>
        /// <param name="time">Timestamp (seconds)</param>
        /// <returns>Section index (zero-based)</returns>
        public int GetSectionIndexAtTime(float time)
        {
            if (_splitTimestamps == null || _splitTimestamps.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < _splitTimestamps.Count; i++)
            {
                if (time < _splitTimestamps[i])
                {
                    return i;
                }
            }

            return _splitTimestamps.Count;
        }

        /// <summary>
        /// Fills all section physical durations into an external list with zero allocations.
        /// </summary>
        /// <param name="outputList">List that receives the section durations</param>
        public void GetSectionDurations(List<float> outputList)
        {
            if (outputList == null) return;
            outputList.Clear();

            int sections = SectionCount;
            for (int i = 0; i < sections; i++)
            {
                outputList.Add(GetSectionDuration(i));
            }
        }

        /// <summary>
        /// Batch-updates split point timestamps, sorting them ascending and discarding invalid values.
        /// </summary>
        /// <param name="timestamps">Timestamp sequence</param>
        public void SetSplitTimestamps(IEnumerable<float> timestamps)
        {
            _splitTimestamps.Clear();
            if (timestamps == null) return;

            foreach (var t in timestamps)
            {
                if (t > 0.0001f)
                {
                    _splitTimestamps.Add(t);
                }
            }

            _splitTimestamps.Sort();
        }

        /// <summary>
        /// Validates and sorts the segment lists on all animation tracks by ascending timestamp.
        /// </summary>
        public void SortAnimationSegments()
        {
            SortSegmentsList(_animationSegments);
            SortSegmentsList(_upperBodySegments);
            SortSegmentsList(_additiveSegments);
        }

        /// <summary>
        /// Validates and sorts the segments of all channel animation tracks by ascending timestamp.
        /// </summary>
        public void SortAllChannelSegments() => SortAnimationSegments();

        /// <summary>
        /// Sorts the animation segments on the specified channel by ascending timestamp.
        /// </summary>
        public void SortChannelSegments(MontageLayerChannel channel)
        {
            SortSegmentsList(GetSegments(channel));
        }

        private static void SortSegmentsList(List<MontageAnimationSegment> list)
        {
            if (list == null || list.Count <= 1) return;
            list.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        }

        /// <summary>
        /// Validates the start/end times and duration of all animation segments.
        /// </summary>
        public void EnsureSegmentsValid()
        {
            ValidateSegmentsList(_animationSegments);
            ValidateSegmentsList(_upperBodySegments);
            ValidateSegmentsList(_additiveSegments);
        }

        private static void ValidateSegmentsList(List<MontageAnimationSegment> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                list[i]?.EnsureValid();
            }
        }

        /// <summary>
        /// Gets the animation segment list of the specified channel.
        /// </summary>
        public List<MontageAnimationSegment> GetSegments(MontageLayerChannel channel)
        {
            return channel switch
            {
                MontageLayerChannel.UpperBody => _upperBodySegments,
                MontageLayerChannel.FullBody => _animationSegments,
                MontageLayerChannel.Additive => _additiveSegments,
                _ => _animationSegments
            };
        }

        /// <summary>
        /// Queries whether the specified channel is enabled.
        /// </summary>
        public bool IsChannelEnabled(MontageLayerChannel channel)
        {
            return channel switch
            {
                MontageLayerChannel.UpperBody => _enableUpperBodyTrack,
                MontageLayerChannel.FullBody => true, // Full-body track is always enabled
                MontageLayerChannel.Additive => _enableAdditiveTrack,
                _ => true
            };
        }

        /// <summary>
        /// Sets whether the specified channel is enabled.
        /// </summary>
        public void SetChannelEnabled(MontageLayerChannel channel, bool enabled)
        {
            switch (channel)
            {
                case MontageLayerChannel.UpperBody:
                    _enableUpperBodyTrack = enabled;
                    break;
                case MontageLayerChannel.FullBody:
                    // Full-body track is always enabled and cannot be disabled
                    break;
                case MontageLayerChannel.Additive:
                    _enableAdditiveTrack = enabled;
                    break;
            }
        }

        /// <summary>
        /// Gets the overall blend weight [0.0, 1.0] of the specified channel layer.
        /// </summary>
        public float GetChannelWeight(MontageLayerChannel channel)
        {
            return channel switch
            {
                MontageLayerChannel.UpperBody => _upperBodyWeight,
                MontageLayerChannel.FullBody => _fullBodyWeight,
                MontageLayerChannel.Additive => _additiveWeight,
                _ => 1.0f
            };
        }

        /// <summary>
        /// Sets the overall blend weight [0.0, 1.0] of the specified channel layer.
        /// </summary>
        public void SetChannelWeight(MontageLayerChannel channel, float weight)
        {
            float clamped = Mathf.Clamp01(weight);
            switch (channel)
            {
                case MontageLayerChannel.UpperBody:
                    _upperBodyWeight = clamped;
                    break;
                case MontageLayerChannel.FullBody:
                    _fullBodyWeight = clamped;
                    break;
                case MontageLayerChannel.Additive:
                    _additiveWeight = clamped;
                    break;
            }
        }

        /// <summary>
        /// Moves an animation segment from the source channel to the target channel.
        /// </summary>
        public bool MoveSegmentChannel(MontageAnimationSegment segment, MontageLayerChannel fromChannel, MontageLayerChannel toChannel)
        {
            if (segment == null || fromChannel == toChannel) return false;
            var fromList = GetSegments(fromChannel);
            var toList = GetSegments(toChannel);
            if (fromList == null || toList == null || !fromList.Contains(segment)) return false;

            fromList.Remove(segment);
            toList.Add(segment);
            SortChannelSegments(fromChannel);
            SortChannelSegments(toChannel);
            EnsureSegmentsValid();
            return true;
        }

        /// <summary>
        /// Queries whether the specified channel currently contains any valid animation segment.
        /// </summary>
        public bool HasAnyAnimationInChannel(MontageLayerChannel channel)
        {
            if (!IsChannelEnabled(channel)) return false;
            var list = GetSegments(channel);
            if (list == null || list.Count == 0) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i]?.Clip != null) return true;
            }
            return false;
        }

        /// <summary>
        /// Evaluates the active segments, sample times, and normalized blend weights of the specified animation track at the current absolute timestamp.
        /// Core design: in animation gaps, playback simply stops (empty output list, weights naturally drop to 0) and never gets stuck on the first or last frame.
        /// </summary>
        public static void EvaluateSegments(
            List<MontageAnimationSegment> segments,
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            MontageSequenceData.EvaluateSegments(segments, timelineTime, outIndices, outSampleTimes, outWeights);
        }

        /// <summary>
        /// Evaluates the full-body core animation segments (backward-compatible main interface).
        /// </summary>
        public void EvaluateAnimationSegments(
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            EvaluateSegments(_animationSegments, timelineTime, outIndices, outSampleTimes, outWeights);
        }

        /// <summary>
        /// Evaluates the upper-body animation segments.
        /// </summary>
        public void EvaluateUpperBodySegments(
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            if (!_enableUpperBodyTrack)
            {
                outIndices?.Clear();
                outSampleTimes?.Clear();
                outWeights?.Clear();
                return;
            }
            EvaluateSegments(_upperBodySegments, timelineTime, outIndices, outSampleTimes, outWeights);
        }

        /// <summary>
        /// Evaluates the additive animation segments.
        /// </summary>
        public void EvaluateAdditiveSegments(
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            if (!_enableAdditiveTrack)
            {
                outIndices?.Clear();
                outSampleTimes?.Clear();
                outWeights?.Clear();
                return;
            }
            EvaluateSegments(_additiveSegments, timelineTime, outIndices, outSampleTimes, outWeights);
        }

        /// <summary>
        /// Evaluates the animation segments of the specified channel.
        /// </summary>
        public void EvaluateChannelSegments(
            MontageLayerChannel channel,
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            switch (channel)
            {
                case MontageLayerChannel.UpperBody:
                    EvaluateUpperBodySegments(timelineTime, outIndices, outSampleTimes, outWeights);
                    break;
                case MontageLayerChannel.FullBody:
                    EvaluateAnimationSegments(timelineTime, outIndices, outSampleTimes, outWeights);
                    break;
                case MontageLayerChannel.Additive:
                    EvaluateAdditiveSegments(timelineTime, outIndices, outSampleTimes, outWeights);
                    break;
            }
        }

        #endregion
    }
}
