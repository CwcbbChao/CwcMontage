using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Lightweight value-type handle for tracking and controlling an active montage playback instance.
    /// Contains generational validation to prevent stale references when slots or players are recycled.
    /// </summary>
    public readonly struct MontageHandle : IEquatable<MontageHandle>
    {
        #region Private Fields

        private readonly MontageController _controller;
        private readonly int _layerIndex;
        private readonly int _slotIndex;
        private readonly int _generation;

        #endregion

        #region Properties

        /// <summary>
        /// Invalid default handle.
        /// </summary>
        public static MontageHandle Invalid => default;

        /// <summary>
        /// Whether the handle is currently valid (playback is active and generation matches).
        /// </summary>
        public bool IsValid => _controller != null && _controller.IsHandleValid(_layerIndex, _slotIndex, _generation);

        /// <summary>
        /// Whether the montage is actively playing.
        /// </summary>
        public bool IsPlaying => _controller != null && _controller.IsHandlePlaying(_layerIndex, _slotIndex, _generation);

        /// <summary>
        /// Whether the montage is currently fading out due to interruption or stop.
        /// </summary>
        public bool IsStopping => _controller != null && _controller.IsHandleStopping(_layerIndex, _slotIndex, _generation);

        /// <summary>
        /// Whether the montage has finished playback or stopped completely.
        /// </summary>
        public bool IsFinished => _controller != null && _controller.IsHandleFinished(_layerIndex, _slotIndex, _generation);

        /// <summary>
        /// Whether the montage is currently paused.
        /// </summary>
        public bool IsPaused => _controller != null && _controller.IsHandlePaused(_layerIndex, _slotIndex, _generation);

        /// <summary>
        /// Current blend weight in the mixer [0.0, 1.0]. Returns 0 if invalid.
        /// </summary>
        public float CurrentWeight => _controller != null ? _controller.GetHandleWeight(_layerIndex, _slotIndex, _generation) : 0f;

        /// <summary>
        /// Total duration in seconds of the current montage sequence.
        /// </summary>
        public float TotalDuration => _controller != null ? _controller.GetHandleTotalDuration(_layerIndex, _slotIndex, _generation) : 0f;

        /// <summary>
        /// Elapsed playback time in seconds.
        /// </summary>
        public float ElapsedTime => _controller != null ? _controller.GetHandleElapsedTime(_layerIndex, _slotIndex, _generation) : 0f;

        /// <summary>
        /// Remaining playback time in seconds.
        /// </summary>
        public float RemainingTime => Mathf.Max(0f, TotalDuration - ElapsedTime);

        /// <summary>
        /// Current normalized playback progress [0.0, 1.0]. Returns 0 if the handle is invalid or the total duration is 0.
        /// </summary>
        public float NormalizedTime
        {
            get
            {
                float total = TotalDuration;
                return total > 0.0001f ? Mathf.Clamp01(ElapsedTime / total) : 0f;
            }
        }

        /// <summary>
        /// Current normalized playback progress (convenience alias for NormalizedTime) [0.0, 1.0].
        /// </summary>
        public float Progress => NormalizedTime;

        /// <summary>
        /// Index of the current physical section. Returns -1 if the handle is invalid.
        /// </summary>
        public int CurrentSectionIndex => _controller != null ? _controller.GetHandleSectionIndex(_layerIndex, _slotIndex, _generation) : -1;

        /// <summary>
        /// Total number of physical sections in the current montage. Returns 0 if the handle is invalid.
        /// </summary>
        public int SectionCount => _controller != null ? _controller.GetHandleSectionCount(_layerIndex, _slotIndex, _generation) : (Sequence != null ? Sequence.SectionCount : (SourceAsset != null ? SourceAsset.SectionCount : 0));

        /// <summary>
        /// Whether the current montage has multiple physical sections (SectionCount > 1).
        /// </summary>
        public bool HasSections => SectionCount > 1;

        /// <summary>
        /// Checks whether the specified physical section index exists in the current montage.
        /// </summary>
        /// <param name="sectionIndex">Physical section index (zero-based).</param>
        /// <returns>True if the section index is valid and within range.</returns>
        public bool HasSection(int sectionIndex)
        {
            if (_controller != null)
            {
                return _controller.HasHandleSection(_layerIndex, _slotIndex, _generation, sectionIndex);
            }
            return Sequence != null && sectionIndex >= 0 && sectionIndex < Sequence.SectionCount;
        }

        /// <summary>
        /// Associated source montage sequence contract. Returns null if the handle is invalid or has been overridden.
        /// </summary>
        public IMontageSequence Sequence => _controller != null ? _controller.GetHandleSourceSequence(_layerIndex, _slotIndex, _generation) : null;

        /// <summary>
        /// Associated source montage configuration asset. Returns null if the handle is invalid, has been overridden, or plays dynamic pure C# data.
        /// </summary>
        public MontageSequenceSO SourceAsset => _controller != null ? _controller.GetHandleSourceAsset(_layerIndex, _slotIndex, _generation) : null;

        /// <summary>
        /// The bound controller component.
        /// </summary>
        public MontageController Controller => _controller;

        /// <summary>
        /// Backward-compatibility alias for Controller.
        /// </summary>
        [Obsolete("Use Controller instead.")]
        public MontageController Coordinator => _controller;

        /// <summary>
        /// Target layer index.
        /// </summary>
        public int LayerIndex => _layerIndex;

        /// <summary>
        /// Target double-buffered slot index.
        /// </summary>
        public int SlotIndex => _slotIndex;

        /// <summary>
        /// Generation version number.
        /// </summary>
        public int Generation => _generation;

        #endregion

        #region Constructors

        internal MontageHandle(MontageController controller, int layerIndex, int slotIndex, int generation)
        {
            _controller = controller;
            _layerIndex = layerIndex;
            _slotIndex = slotIndex;
            _generation = generation;
        }

        #endregion

        #region Public Control & Query API

        /// <summary>
        /// Instantly jumps to the start time of the specified section.
        /// </summary>
        /// <param name="sectionIndex">Section index (0 to SectionCount - 1).</param>
        public void JumpToSection(int sectionIndex)
        {
            if (_controller != null)
            {
                _controller.JumpToSection(_layerIndex, _slotIndex, _generation, sectionIndex);
            }
        }

        /// <summary>
        /// Instantly jumps to the specified absolute timeline timestamp in seconds.
        /// </summary>
        /// <param name="targetTime">Target time in seconds.</param>
        public void JumpToTime(float targetTime)
        {
            if (_controller != null)
            {
                _controller.JumpToTime(_layerIndex, _slotIndex, _generation, targetTime);
            }
        }

        /// <summary>
        /// Evaluates and jumps to a normalized progress within a specific section.
        /// Maps the specified section's normalized progress [0.0, 1.0] to physical time and jumps instantly (commonly used for charging or externally controlled actions).
        /// </summary>
        /// <param name="sectionIndex">Section index.</param>
        /// <param name="progress">Normalized progress [0.0, 1.0].</param>
        public void EvaluateSectionProgress(int sectionIndex, float progress)
        {
            if (_controller != null)
            {
                _controller.EvaluateSectionProgress(_layerIndex, _slotIndex, _generation, sectionIndex, progress);
            }
        }

        /// <summary>
        /// Evaluates section progress (alias for EvaluateSectionProgress).
        /// </summary>
        public void EvaluateSectionAtProgress(int sectionIndex, float progress) => EvaluateSectionProgress(sectionIndex, progress);

        /// <summary>
        /// Dynamically scales playback rate for a specific section to match the target duration in seconds.
        /// </summary>
        /// <param name="sectionIndex">Section index.</param>
        /// <param name="targetDuration">Expected duration in seconds.</param>
        public void SyncSectionDuration(int sectionIndex, float targetDuration)
        {
            if (_controller != null)
            {
                _controller.SyncSectionDuration(_layerIndex, _slotIndex, _generation, sectionIndex, targetDuration);
            }
        }

        /// <summary>
        /// Adaptive section clock alignment.
        /// Sets the target physical duration for the specified section, optionally jumping to the section start immediately.
        /// </summary>
        /// <param name="sectionIndex">Physical section index.</param>
        /// <param name="targetDuration">Expected real duration of the section in the game, in seconds.</param>
        /// <param name="jumpImmediately">Whether to jump to the section start immediately.</param>
        public void SyncSection(int sectionIndex, float targetDuration, bool jumpImmediately = false)
        {
            if (_controller != null)
            {
                _controller.SyncSection(_layerIndex, _slotIndex, _generation, sectionIndex, targetDuration, jumpImmediately);
            }
        }

        /// <summary>
        /// Clears the custom rate configuration for the specified section, restoring the asset's base rate.
        /// </summary>
        /// <param name="sectionIndex">Physical section index.</param>
        public void ClearSectionSync(int sectionIndex)
        {
            if (_controller != null)
            {
                _controller.ClearSectionSync(_layerIndex, _slotIndex, _generation, sectionIndex);
            }
        }

        /// <summary>
        /// Sets the current playback rate multiplier.
        /// </summary>
        /// <param name="rate">Rate multiplier.</param>
        public void SetPlaybackRate(float rate)
        {
            if (_controller != null)
            {
                _controller.SetPlaybackRate(_layerIndex, _slotIndex, _generation, rate);
            }
        }

        /// <summary>
        /// Dynamically sets the playback weight multiplier [0.0, 1.0] for the current montage.
        /// </summary>
        /// <param name="weight">Weight multiplier [0.0, 1.0].</param>
        public void SetWeight(float weight)
        {
            if (_controller != null)
            {
                _controller.SetHandleWeight(_layerIndex, _slotIndex, _generation, weight);
            }
        }

        /// <summary>
        /// Pauses or resumes playback.
        /// </summary>
        /// <param name="isPaused">Whether to pause.</param>
        public void SetPaused(bool isPaused)
        {
            if (_controller != null)
            {
                _controller.SetPaused(_layerIndex, _slotIndex, _generation, isPaused);
            }
        }

        /// <summary>
        /// Externally interrupts and stops this montage playback, entering the fade-out phase and safely exiting all active action blocks.
        /// </summary>
        /// <param name="blendOutTime">Custom fade-out duration (uses the asset default if null).</param>
        public void Stop(float? blendOutTime = null)
        {
            if (_controller != null)
            {
                _controller.StopHandle(_layerIndex, _slotIndex, _generation, blendOutTime);
            }
        }

        /// <summary>
        /// Gets the original physical duration of the specified section in seconds.
        /// </summary>
        /// <param name="sectionIndex">Physical section index.</param>
        /// <returns>Section duration in seconds.</returns>
        public float GetSectionDuration(int sectionIndex)
        {
            return SourceAsset != null ? SourceAsset.GetSectionDuration(sectionIndex) : 0f;
        }

        /// <summary>
        /// Gets the start and end time range [start, end] of the specified section on the timeline.
        /// </summary>
        /// <param name="sectionIndex">Physical section index.</param>
        /// <returns>Tuple containing start and end.</returns>
        public (float start, float end) GetSectionRange(int sectionIndex)
        {
            return SourceAsset != null ? SourceAsset.GetSectionRange(sectionIndex) : (0f, 0f);
        }

        /// <summary>
        /// Gets the normalized local progress [0.0, 1.0] of the current playback position within the specified section.
        /// </summary>
        /// <param name="sectionIndex">Physical section index.</param>
        /// <returns>Normalized progress within the section.</returns>
        public float GetSectionProgress(int sectionIndex)
        {
            if (SourceAsset == null) return 0f;
            var (start, end) = SourceAsset.GetSectionRange(sectionIndex);
            float len = Mathf.Max(0.0001f, end - start);
            return Mathf.Clamp01((ElapsedTime - start) / len);
        }

        #endregion

        #region Equality & Operators

        public bool Equals(MontageHandle other) =>
            _controller == other._controller &&
            _layerIndex == other._layerIndex &&
            _slotIndex == other._slotIndex &&
            _generation == other._generation;

        public override bool Equals(object obj) => obj is MontageHandle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_controller, _layerIndex, _slotIndex, _generation);

        public static bool operator ==(MontageHandle left, MontageHandle right) => left.Equals(right);

        public static bool operator !=(MontageHandle left, MontageHandle right) => !left.Equals(right);

        public override string ToString() => $"MontageHandle(Layer: {_layerIndex}, Slot: {_slotIndex}, Gen: {_generation}, Valid: {IsValid})";

        #endregion
    }
}
