using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// In-memory clipboard for the montage editor.
    /// Supports copying and deep-copy pasting of action blocks (MontageActionBlockData) and tracks (MontageTrackData) across animation assets (MontageSequenceSO).
    /// </summary>
    public static class MontageClipboard
    {
        #region Private Static Fields

        private static MontageActionBlockData s_copiedActionBlock;
        private static MontageTrackData s_copiedTrack;
        private static MontageAnimationSegment s_copiedSegment;

        #endregion

        #region Public Properties

        /// <summary>
        /// Whether an action block has been copied to the clipboard.
        /// </summary>
        public static bool HasCopiedBlock => s_copiedActionBlock != null;

        /// <summary>
        /// Whether a track has been copied to the clipboard.
        /// </summary>
        public static bool HasCopiedTrack => s_copiedTrack != null;

        /// <summary>
        /// Whether an animation segment has been copied to the clipboard.
        /// </summary>
        public static bool HasCopiedSegment => s_copiedSegment != null;

        #endregion

        #region Public Methods

        /// <summary>
        /// Deep-copies the specified action block into the clipboard.
        /// </summary>
        /// <param name="block">Source action block data</param>
        public static void CopyActionBlock(MontageActionBlockData block)
        {
            if (block == null)
            {
                s_copiedActionBlock = null;
                return;
            }

            s_copiedActionBlock = block.Clone();
        }

        /// <summary>
        /// Gets a deep-copied action block from the clipboard.
        /// </summary>
        /// <returns>Action block clone instance</returns>
        public static MontageActionBlockData GetClonedBlock()
        {
            return s_copiedActionBlock?.Clone();
        }

        /// <summary>
        /// Deep-copies the specified track into the clipboard.
        /// </summary>
        /// <param name="track">Source track data</param>
        public static void CopyTrack(MontageTrackData track)
        {
            if (track == null)
            {
                s_copiedTrack = null;
                return;
            }

            s_copiedTrack = track.Clone();
        }

        /// <summary>
        /// Gets a deep-copied track from the clipboard.
        /// </summary>
        /// <returns>Track clone instance</returns>
        public static MontageTrackData GetClonedTrack()
        {
            return s_copiedTrack?.Clone();
        }

        /// <summary>
        /// Deep-copies the specified animation segment into the clipboard.
        /// </summary>
        /// <param name="segment">Source animation segment data</param>
        public static void CopySegment(MontageAnimationSegment segment)
        {
            if (segment == null)
            {
                s_copiedSegment = null;
                return;
            }

            s_copiedSegment = segment.Clone();
        }

        /// <summary>
        /// Gets a deep-copied animation segment from the clipboard.
        /// </summary>
        /// <returns>Animation segment clone instance</returns>
        public static MontageAnimationSegment GetClonedSegment()
        {
            return s_copiedSegment?.Clone();
        }

        /// <summary>
        /// Clears the clipboard contents.
        /// </summary>
        public static void Clear()
        {
            s_copiedActionBlock = null;
            s_copiedTrack = null;
            s_copiedSegment = null;
        }

        #endregion
    }
}
