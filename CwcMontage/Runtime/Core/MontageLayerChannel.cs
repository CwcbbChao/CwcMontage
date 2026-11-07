using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Animation layer channel channels for humanoid characters.
    /// Priority ascends from bottom to top: UpperBody -> FullBody -> Additive.
    /// </summary>
    public enum MontageLayerChannel
    {
        /// <summary>
        /// Upper-body override layer blended over base locomotion using humanoid AvatarMask.
        /// Layer Index: 1
        /// </summary>
        UpperBody = 0,

        /// <summary>
        /// Full-body override layer for complete animations (rolls, heavy strikes, executions).
        /// Layer Index: 2
        /// </summary>
        FullBody = 1,

        /// <summary>
        /// Additive layer for flinches, recoil, or micro-reactions.
        /// Layer Index: 3
        /// </summary>
        Additive = 2
    }
}
