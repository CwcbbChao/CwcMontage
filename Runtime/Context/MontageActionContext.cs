using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇动作块执行上下文。
    /// 只读结构体，传递当前动画帧的绝对时间、分段进度、目标对象与运行模式等环境信息。
    /// </summary>
    public readonly struct MontageActionContext
    {
        /// <summary>
        /// 目标宿主对象（角色根 GameObject）。
        /// </summary>
        public readonly GameObject TargetObject;

        /// <summary>
        /// 目标 Animator 组件引用。
        /// </summary>
        public readonly Animator Animator;

        /// <summary>
        /// 当前动画播放进行到的绝对时间（秒）。
        /// </summary>
        public readonly float CurrentTime;

        /// <summary>
        /// 动画总时长（秒）。
        /// </summary>
        public readonly float TotalDuration;

        /// <summary>
        /// 当前所处的物理分段索引（从 0 开始）。
        /// </summary>
        public readonly int CurrentSectionIndex;

        /// <summary>
        /// 当前物理分段内的归一化进度 [0.0, 1.0]。
        /// </summary>
        public readonly float SectionProgress;

        /// <summary>
        /// 整段动画的全局归一化进度 [0.0, 1.0]。
        /// </summary>
        public readonly float NormalizedProgress;

        /// <summary>
        /// 当前分段或全局的播放速率倍率。
        /// </summary>
        public readonly float PlaybackRate;

        /// <summary>
        /// 是否处于编辑器非运行模式预览环境。
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
            bool isPreview)
        {
            TargetObject = targetObject;
            Animator = animator;
            CurrentTime = currentTime;
            TotalDuration = totalDuration;
            CurrentSectionIndex = currentSectionIndex;
            SectionProgress = sectionProgress;
            NormalizedProgress = normalizedProgress;
            PlaybackRate = playbackRate;
            IsPreview = isPreview;
        }
    }
}
