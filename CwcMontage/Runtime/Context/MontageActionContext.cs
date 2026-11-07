using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Read-only execution context passed to montage action blocks on every evaluation.
    /// Provides frame time, section progress, target GameObject, Animator, and runtime mode.
    /// </summary>
    public readonly struct MontageActionContext
    {
        /// <summary>
        /// Target host GameObject (character root).
        /// </summary>
        public readonly GameObject TargetObject;

        /// <summary>
        /// Target Animator component.
        /// </summary>
        public readonly Animator Animator;

        /// <summary>
        /// Bound controller component (can be null in editor preview).
        /// </summary>
        public readonly MontageController Controller;

        /// <summary>
        /// Backward-compatibility alias for Controller.
        /// </summary>
        [Obsolete("Use Controller instead.")]
        public MontageController Coordinator => Controller;

        /// <summary>
        /// Current playback time on the montage timeline in seconds.
        /// </summary>
        public readonly float CurrentTime;

        /// <summary>
        /// Total sequence duration in seconds.
        /// </summary>
        public readonly float TotalDuration;

        /// <summary>
        /// Current active section index (0-based).
        /// </summary>
        public readonly int CurrentSectionIndex;

        /// <summary>
        /// Normalized progress within the current section [0.0, 1.0].
        /// </summary>
        public readonly float SectionProgress;

        /// <summary>
        /// Normalized overall progress across the entire sequence [0.0, 1.0].
        /// </summary>
        public readonly float NormalizedProgress;

        /// <summary>
        /// Current effective playback rate multiplier.
        /// </summary>
        public readonly float PlaybackRate;

        /// <summary>
        /// Whether running in the editor non-playmode preview window.
        /// </summary>
        public readonly bool IsPreview;

        public MontageActionContext(
            GameObject targetObject,
            Animator animator,
            float currentTime,
            float totalDuration,
            int currentSectionIndex,
            float sectionProgress,
            float normalizedProgress,
            float playbackRate,
            bool isPreview,
            MontageController controller = null)
        {
            TargetObject = targetObject;
            Animator = animator;
            Controller = controller;
            CurrentTime = currentTime;
            TotalDuration = totalDuration;
            CurrentSectionIndex = currentSectionIndex;
            SectionProgress = sectionProgress;
            NormalizedProgress = normalizedProgress;
            PlaybackRate = playbackRate;
            IsPreview = isPreview;
        }

        /// <summary>
        /// Extremely fast, safe access to a core target bone Transform (absolutely zero GC allocation).
        /// Reads directly from the Coordinator's array cache when available; without a Coordinator it falls back to static resolution, and never returns null.
        /// </summary>
        /// <param name="targetBone">Core bone enum</param>
        /// <returns>The found bone Transform (guaranteed fallback to TargetObject.transform)</returns>
        public Transform GetTargetBone(MontageTargetBone targetBone)
        {
            if (Controller != null)
            {
                return Controller.GetTargetBone(targetBone);
            }
            return MontageBoneUtility.ResolveBone(TargetObject, Animator, targetBone);
        }
    }
}
