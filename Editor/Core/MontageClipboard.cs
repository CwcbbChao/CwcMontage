using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 蒙太奇编辑器内存剪贴板。
    /// 支持跨动画资产（MontageSequenceSO）复制与深拷贝粘贴动作块（MontageActionBlockData）与轨道（MontageTrackData）。
    /// </summary>
    public static class MontageClipboard
    {
        #region 私有静态字段

        private static MontageActionBlockData s_copiedActionBlock;
        private static MontageTrackData s_copiedTrack;

        #endregion

        #region 公共属性

        /// <summary>
        /// 剪贴板中是否存在已复制的动作块。
        /// </summary>
        public static bool HasCopiedBlock => s_copiedActionBlock != null;

        /// <summary>
        /// 剪贴板中是否存在已复制的轨道。
        /// </summary>
        public static bool HasCopiedTrack => s_copiedTrack != null;

        #endregion

        #region 公共方法

        /// <summary>
        /// 将指定动作块深拷贝存入剪贴板。
        /// </summary>
        /// <param name="block">源动作块数据</param>
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
        /// 从剪贴板获取深拷贝的动作块副本。
        /// </summary>
        /// <returns>动作块克隆实例</returns>
        public static MontageActionBlockData GetClonedBlock()
        {
            return s_copiedActionBlock?.Clone();
        }

        /// <summary>
        /// 将指定轨道深拷贝存入剪贴板。
        /// </summary>
        /// <param name="track">源轨道数据</param>
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
        /// 从剪贴板获取深拷贝的轨道副本。
        /// </summary>
        /// <returns>轨道克隆实例</returns>
        public static MontageTrackData GetClonedTrack()
        {
            return s_copiedTrack?.Clone();
        }

        /// <summary>
        /// 清空剪贴板内容。
        /// </summary>
        public static void Clear()
        {
            s_copiedActionBlock = null;
            s_copiedTrack = null;
        }

        #endregion
    }
}
