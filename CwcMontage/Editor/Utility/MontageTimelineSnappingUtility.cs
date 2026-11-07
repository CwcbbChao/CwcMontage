using System;
using System.Collections.Generic;
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// Unified timeline magnetic snapping and discrete frame alignment utility (MontageTimelineSnappingUtility).
    /// Provides industrial-grade multi-level snapping logic similar to Unity Timeline / Unreal Sequencer:
    /// Supports snapping to time 0, physical split points (SplitTimestamps), the current playhead position, animation segment start/end points, and action block start/end points across tracks.
    /// </summary>
    public static class MontageTimelineSnappingUtility
    {
        #region Public Constants

        public const float DEFAULT_SNAP_PIXEL_THRESHOLD = 8f;

        #endregion

        #region Public Methods

        /// <summary>
        /// Strictly snaps a float time in seconds to the nearest integer frame time point.
        /// </summary>
        public static float SnapToFrame(float time, float frameRate)
        {
            float fps = Mathf.Max(1f, frameRate);
            float frameInterval = 1f / fps;
            int frameIndex = Mathf.RoundToInt(time * fps);
            return Mathf.Max(0f, frameIndex * frameInterval);
        }

        /// <summary>
        /// Converts an integer frame index to seconds.
        /// </summary>
        public static float FrameToSeconds(int frame, float frameRate)
        {
            float fps = Mathf.Max(1f, frameRate);
            return Mathf.Max(0f, frame / fps);
        }

        /// <summary>
        /// Converts seconds to a rounded integer frame index.
        /// </summary>
        public static int SecondsToFrame(float seconds, float frameRate)
        {
            float fps = Mathf.Max(1f, frameRate);
            return Mathf.Max(0, Mathf.RoundToInt(seconds * fps));
        }

        /// <summary>
        /// Global multi-level magnetic snapping algorithm:
        /// Automatically searches for snap targets within the screen-pixel threshold:
        /// 1. Time origin 0s;
        /// 2. Current playhead marker line (Playhead Time);
        /// 3. Physical split marker points (SplitTimestamps);
        /// 4. Start/end edges of all segments on the animation track;
        /// 5. Start/end edges of all action blocks on every action track.
        /// </summary>
        /// <param name="candidateTime">Target time to validate (seconds)</param>
        /// <param name="pps">Actual pixel width per second at the current zoom level</param>
        /// <param name="targetAsset">Montage asset currently being edited</param>
        /// <param name="playheadTime">Current timeline playhead time (pass -1 if not needed)</param>
        /// <param name="isStart">Whether the current alignment is a segment start point (only snaps to 0 when true)</param>
        /// <param name="excludeSelfStartTime">Exclude the self start point (avoid snapping to itself)</param>
        /// <param name="excludeSelfEndTime">Exclude the self end point (avoid snapping to itself)</param>
        /// <param name="snapPixelThreshold">Screen-pixel snap tolerance</param>
        /// <returns>Final target time after snapping</returns>
        public static float ApplyMagneticSnapping(
            float candidateTime,
            float pps,
            MontageSequenceAsset targetAsset,
            float playheadTime = -1f,
            bool isStart = false,
            float excludeSelfStartTime = -1f,
            float excludeSelfEndTime = -1f,
            float snapPixelThreshold = DEFAULT_SNAP_PIXEL_THRESHOLD)
        {
            if (pps <= 0.001f)
            {
                return candidateTime;
            }

            float snapTimeThreshold = snapPixelThreshold / pps;
            float bestTime = candidateTime;
            float minDelta = float.MaxValue;

            void TrySnap(float targetTime)
            {
                if (targetTime < 0f) return;
                if (excludeSelfStartTime >= 0f && Mathf.Abs(targetTime - excludeSelfStartTime) < 0.0001f) return;
                if (excludeSelfEndTime >= 0f && Mathf.Abs(targetTime - excludeSelfEndTime) < 0.0001f) return;

                float delta = Mathf.Abs(candidateTime - targetTime);
                if (delta <= snapTimeThreshold && delta < minDelta)
                {
                    minDelta = delta;
                    bestTime = targetTime;
                }
            }

            // 1. Attempt to align to time 0
            if (isStart)
            {
                TrySnap(0f);
            }

            // 2. Attempt to align to the current playhead time
            if (playheadTime >= 0f)
            {
                TrySnap(playheadTime);
            }

            if (targetAsset != null)
            {
                // 3. Attempt to align to physical split points (SplitTimestamps)
                if (targetAsset.SplitTimestamps != null)
                {
                    for (int i = 0; i < targetAsset.SplitTimestamps.Count; i++)
                    {
                        TrySnap(targetAsset.SplitTimestamps[i]);
                    }
                }

                // 4. Attempt to align to all start/end boundaries of animation track segments
                if (targetAsset.AnimationSegments != null)
                {
                    for (int i = 0; i < targetAsset.AnimationSegments.Count; i++)
                    {
                        var seg = targetAsset.AnimationSegments[i];
                        if (seg == null) continue;
                        TrySnap(seg.StartTime);
                        TrySnap(seg.EndTime);
                    }
                }

                // 5. Attempt to align to action block start/end boundaries on all action tracks
                if (targetAsset.Tracks != null)
                {
                    for (int t = 0; t < targetAsset.Tracks.Count; t++)
                    {
                        var track = targetAsset.Tracks[t];
                        if (track?.ActionBlocks == null) continue;

                        for (int b = 0; b < track.ActionBlocks.Count; b++)
                        {
                            var block = track.ActionBlocks[b];
                            if (block == null) continue;
                            TrySnap(block.StartTime);
                            TrySnap(block.EndTime);
                        }
                    }
                }
            }

            return bestTime;
        }

        #endregion
    }
}
