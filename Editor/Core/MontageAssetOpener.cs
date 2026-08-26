using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 蒙太奇数据资产打开拦截器。
    /// 当在 Unity Project 窗口中双击 MontageSequenceSO 资产时，自动打开 MontageEditorWindow 并载入该资产标签页。
    /// </summary>
    public static class MontageAssetOpener
    {
        [OnOpenAsset(1)]
        public static bool OnOpenAsset(int instanceID, int line)
        {
            var obj = EditorUtility.InstanceIDToObject(instanceID) as MontageSequenceSO;
            if (obj != null)
            {
                MontageEditorWindow.OpenAsset(obj);
                return true;
            }

            return false;
        }
    }
}
