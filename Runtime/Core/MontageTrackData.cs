using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇动作轨道数据。
    /// 承载同一表现层级下的多个动作块集合，支持轨道重命名、静音与锁定。
    /// </summary>
    [Serializable]
    public class MontageTrackData
    {
        #region Inspector 字段

        [Tooltip("轨道名称。")]
        [SerializeField] private string _trackName = "Track";

        [Tooltip("轨道是否被静音（静音后所有块不执行）。")]
        [SerializeField] private bool _isMuted;

        [Tooltip("轨道是否在编辑器中被锁定（防误触）。")]
        [SerializeField] private bool _isLocked;

        [Tooltip("该轨道下包含的所有动作块数据列表。")]
        [SerializeField] private List<MontageActionBlockData> _actionBlocks = new();

        #endregion

        #region 公共属性

        /// <summary>
        /// 轨道名称。
        /// </summary>
        public string TrackName
        {
            get => _trackName;
            set => _trackName = value;
        }

        /// <summary>
        /// 轨道是否被静音。
        /// </summary>
        public bool IsMuted
        {
            get => _isMuted;
            set => _isMuted = value;
        }

        /// <summary>
        /// 轨道是否被锁定。
        /// </summary>
        public bool IsLocked
        {
            get => _isLocked;
            set => _isLocked = value;
        }

        /// <summary>
        /// 该轨道下包含的所有动作块数据列表。
        /// </summary>
        public List<MontageActionBlockData> ActionBlocks => _actionBlocks;

        #endregion

        #region 构造方法

        public MontageTrackData()
        {
        }

        public MontageTrackData(string trackName)
        {
            _trackName = trackName;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 创建该轨道数据的独立克隆副本。
        /// </summary>
        /// <returns>克隆后的轨道数据</returns>
        public MontageTrackData Clone()
        {
            var cloned = new MontageTrackData
            {
                _trackName = _trackName,
                _isMuted = _isMuted,
                _isLocked = _isLocked,
                _actionBlocks = new List<MontageActionBlockData>(_actionBlocks.Count)
            };

            for (int i = 0; i < _actionBlocks.Count; i++)
            {
                if (_actionBlocks[i] != null)
                {
                    cloned._actionBlocks.Add(_actionBlocks[i].Clone());
                }
            }

            return cloned;
        }

        #endregion
    }
}
