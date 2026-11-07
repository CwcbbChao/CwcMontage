using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Montage action track data container.
    /// Holds a collection of action blocks and provides track muting and locking states.
    /// </summary>
    [Serializable]
    public class MontageTrackData
    {
        #region Serialized Fields

        [Tooltip("Display name of the track.")]
        [SerializeField] private string _trackName = "Track";

        [Tooltip("Whether the track is muted (muted tracks do not execute).")]
        [SerializeField] private bool _isMuted;

        [Tooltip("Whether the track is locked in the editor to prevent edits.")]
        [SerializeField] private bool _isLocked;

        [Tooltip("List of action blocks contained in this track.")]
        [SerializeField] private List<MontageActionBlockData> _actionBlocks = new();

        #endregion

        #region Properties

        /// <summary>
        /// Display name of the track.
        /// </summary>
        public string TrackName
        {
            get => _trackName;
            set => _trackName = value;
        }

        /// <summary>
        /// Whether the track is muted.
        /// </summary>
        public bool IsMuted
        {
            get => _isMuted;
            set => _isMuted = value;
        }

        /// <summary>
        /// Whether the track is locked in the editor.
        /// </summary>
        public bool IsLocked
        {
            get => _isLocked;
            set => _isLocked = value;
        }

        /// <summary>
        /// Action blocks contained in this track.
        /// </summary>
        public List<MontageActionBlockData> ActionBlocks => _actionBlocks;

        #endregion

        #region Constructors

        public MontageTrackData()
        {
        }

        public MontageTrackData(string trackName)
        {
            _trackName = trackName;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates an independent clone of this track data.
        /// </summary>
        /// <returns>Cloned track data</returns>
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
