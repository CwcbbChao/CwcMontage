using System;
using System.Collections.Generic;
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 统一时间轴磁吸与离散帧对齐工具类（MontageTimelineSnappingUtility）。
    /// 提供类似 Unity Timeline / Unreal Sequencer 的工业级多级磁吸对齐逻辑：
    /// 支持吸附到 0 点、物理分段点（SplitTimestamps）、当前播放头位置、动画片段起止点以及跨轨道动作块起止点。
    /// </summary>
    public static class MontageTimelineSnappingUtility
    {
        #region 公共常量

        public const float DEFAULT_SNAP_PIXEL_THRESHOLD = 8f;

        #endregion

        #region 公共方法

        /// <summary>
        /// 将浮点秒数时间严格离散吸附到最近的整数帧时间点。
        /// </summary>
        public static float SnapToFrame(float time, float frameRate)
        {
            float fps = Mathf.Max(1f, frameRate);
            float frameInterval = 1f / fps;
            int frameIndex = Mathf.RoundToInt(time * fps);
            return Mathf.Max(0f, frameIndex * frameInterval);
        }

        /// <summary>
        /// 将整数帧序号换算为秒数。
        /// </summary>
        public static float FrameToSeconds(int frame, float frameRate)
        {
            float fps = Mathf.Max(1f, frameRate);
            return Mathf.Max(0f, frame / fps);
        }

        /// <summary>
        /// 将秒数换算为四舍五入的整数帧序号。
        /// </summary>
        public static int SecondsToFrame(float seconds, float frameRate)
        {
            float fps = Mathf.Max(1f, frameRate);
            return Mathf.Max(0, Mathf.RoundToInt(seconds * fps));
        }

        /// <summary>
        /// 全局多级磁吸对齐算法：
        /// 自动搜寻屏幕像素阈值范围内的吸附目标：
        /// 1. 时间原点 0s；
        /// 2. 当前播放头刻度线（Playhead Time）；
        /// 3. 物理分段标记点（SplitTimestamps）；
        /// 4. 动画轨道上所有片段的起止边缘；
        /// 5. 各动作轨道上所有动作块的起止边缘。
        /// </summary>
        /// <param name="candidateTime">待校验的目标时间（秒）</param>
        /// <param name="pps">当前缩放下每秒实际像素宽度</param>
        /// <param name="targetAsset">当前编辑的蒙太奇资产</param>
        /// <param name="playheadTime">当前时间轴播放头时间（若不需要可传 -1）</param>
        /// <param name="isStart">当前对齐的是否为片段起点（为 true 时才吸附 0 点）</param>
        /// <param name="excludeSelfStartTime">排除自身初始起点（避免自身吸附自身）</param>
        /// <param name="excludeSelfEndTime">排除自身初始终点（避免自身吸附自身）</param>
        /// <param name="snapPixelThreshold">屏幕像素吸附容差</param>
        /// <returns>吸附后的最终目标时间</returns>
        public static float ApplyMagneticSnapping(
            float candidateTime,
            float pps,
            MontageSequenceSO targetAsset,
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

            // 1. 尝试对齐 0 点
            if (isStart)
            {
                TrySnap(0f);
            }

            // 2. 尝试对齐当前播放头时间
            if (playheadTime >= 0f)
            {
                TrySnap(playheadTime);
            }

            if (targetAsset != null)
            {
                // 3. 尝试对齐物理分段点 (SplitTimestamps)
                if (targetAsset.SplitTimestamps != null)
                {
                    for (int i = 0; i < targetAsset.SplitTimestamps.Count; i++)
                    {
                        TrySnap(targetAsset.SplitTimestamps[i]);
                    }
                }

                // 4. 尝试对齐动画轨道片段的所有起止边界
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

                // 5. 尝试对齐所有动作轨道上的动作块起止边界
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
