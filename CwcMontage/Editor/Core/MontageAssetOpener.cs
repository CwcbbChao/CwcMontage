using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// Asset-open interceptor for montage data assets.
    /// When a MontageSequenceSO asset is double-clicked in the Unity Project window, automatically opens MontageEditorWindow and loads that asset's tab.
    /// </summary>
    public static class MontageAssetOpener
    {
        [OnOpenAsset(1)]
        public static bool OnOpenAsset(int instanceID, int line)
        {
            var unityObj = EditorUtility.InstanceIDToObject(instanceID);

            // 1. 新版资产与派生类（如 SkillMontageAsset）：打开时间轴编辑器
            if (unityObj is MontageSequenceAsset newAsset)
            {
                MontageEditorWindow.OpenAsset(newAsset);
                return true;
            }

            // 2. 旧版资产：停止支持时间轴编辑，弹窗引导用户在 Inspector 中迁移
            if (unityObj is MontageSequenceSO legacySO)
            {
                EditorUtility.DisplayDialog(
                    "旧版资产只读提示",
                    $"资产 '{legacySO.name}' 为旧版 MontageSequenceSO，已停止支持时间轴编辑。\n\n" +
                    "游戏运行时仍保持完全兼容。如需进行可视化时间轴编辑，请在 Inspector 中将其数据迁移到新版资产（MontageSequenceAsset 或其子类）后再编辑。",
                    "前往迁移");

                Selection.activeObject = legacySO;
                EditorGUIUtility.PingObject(legacySO);
                return true;
            }

            return false;
        }
    }
}
