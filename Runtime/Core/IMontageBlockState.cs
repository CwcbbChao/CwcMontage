using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇动作块运行时动态状态接口。
    /// 必须由 class 实现，随槽位复用，杜绝装箱拆箱。
    /// </summary>
    public interface IMontageBlockState
    {
        /// <summary>
        /// 当离开动作块区间、动画被打断或回池时调用，将所有可变变量重置归零。
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// 具备空间挂载与骨骼追踪能力的动作块运行时状态基类。
    /// </summary>
    public class MontageSpatialBlockState : IMontageBlockState
    {
        #region 公共字段

        public Transform CachedTargetBone;

        #endregion

        #region 公共方法

        public virtual void Reset()
        {
            CachedTargetBone = null;
        }

        #endregion
    }
}
