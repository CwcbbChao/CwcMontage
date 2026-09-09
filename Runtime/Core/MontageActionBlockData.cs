using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇动作块序列化数据容器。
    /// 包含动作块在时间轴上的起止时间、起止帧以及多态的动作逻辑实例。
    /// </summary>
    [Serializable]
    public class MontageActionBlockData
    {
        #region Inspector 字段

        [Tooltip("动作块起始时间（秒）。")]
        [SerializeField] private float _startTime;

        [Tooltip("动作块结束时间（秒）。")]
        [SerializeField] private float _endTime;

        [Tooltip("动作块起始帧数。")]
        [SerializeField] private int _startFrame;

        [Tooltip("动作块结束帧数。")]
        [SerializeField] private int _endFrame;

        [Tooltip("多态动作实例。")]
        [SerializeReference] private MontageActionBlockBase _action;

        #endregion

        #region 公共属性

        /// <summary>
        /// 动作块起始时间（秒）。
        /// </summary>
        public float StartTime
        {
            get => _startTime;
            set => _startTime = value;
        }

        /// <summary>
        /// 动作块结束时间（秒）。
        /// </summary>
        public float EndTime
        {
            get => _endTime;
            set => _endTime = value;
        }

        /// <summary>
        /// 动作块起始帧数。
        /// </summary>
        public int StartFrame
        {
            get => _startFrame;
            set => _startFrame = value;
        }

        /// <summary>
        /// 动作块结束帧数。
        /// </summary>
        public int EndFrame
        {
            get => _endFrame;
            set => _endFrame = value;
        }

        /// <summary>
        /// 动作块时长（秒）。
        /// </summary>
        public float Duration => Mathf.Max(0f, _endTime - _startTime);

        /// <summary>
        /// 多态动作实例。
        /// </summary>
        public MontageActionBlockBase Action
        {
            get => _action;
            set => _action = value;
        }

        /// <summary>
        /// 该动作块是否已启用。
        /// </summary>
        public bool IsEnabled => _action == null || _action.IsEnabled;

        /// <summary>
        /// 该动作块是否被禁用（兼容属性）。
        /// </summary>
        public bool IsDisabled => _action != null && !_action.IsEnabled;

        /// <summary>
        /// 运行时克隆对象所对应的原始资产数据源引用（非序列化）。
        /// </summary>
        [NonSerialized] public MontageActionBlockData SourceData;

        #endregion

        #region 构造方法

        public MontageActionBlockData()
        {
        }

        public MontageActionBlockData(float startTime, float endTime, int startFrame, int endFrame, MontageActionBlockBase action)
        {
            _startTime = startTime;
            _endTime = Mathf.Max(endTime, startTime + 0.0001f);
            _startFrame = startFrame;
            _endFrame = Mathf.Max(endFrame, startFrame + 1);
            _action = action;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 校验并规范化动作块的帧数与时间范围，强制保证至少持续 1 帧，杜绝零长度或倒置数据。
        /// </summary>
        /// <param name="frameRate">动画基准帧率</param>
        public void EnsureValid(float frameRate)
        {
            float safeFps = frameRate > 0.01f ? frameRate : 30.0f;
            float minDuration = 1.0f / safeFps;

            if (_startFrame < 0)
            {
                _startFrame = 0;
            }

            if (_endFrame <= _startFrame)
            {
                _endFrame = _startFrame + 1;
            }

            _startTime = Mathf.Max(0.0f, _startTime);
            if (_endTime < _startTime + minDuration)
            {
                _endTime = _startTime + minDuration;
            }

            if (_action != null)
            {
                _action.BlockDuration = Duration;
            }
        }

        /// <summary>
        /// 创建该动作块数据的独立克隆副本。
        /// </summary>
        /// <returns>深拷贝后的动作块数据实例</returns>
        public MontageActionBlockData Clone()
        {
            var cloned = new MontageActionBlockData
            {
                _startTime = _startTime,
                _endTime = _endTime,
                _startFrame = _startFrame,
                _endFrame = _endFrame,
                _action = _action != null ? _action.Clone() : null
            };
            cloned.SourceData = this;
            if (cloned._action != null)
            {
                cloned._action.BlockDuration = cloned.Duration;
            }
            return cloned;
        }

        #endregion
    }
}
