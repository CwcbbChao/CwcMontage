using UnityEditor;
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// Custom Inspector editor for MontageSequenceSO.
    /// Provides a prominent "Open in Montage Editor" shortcut button.
    /// </summary>
    [CustomEditor(typeof(MontageSequenceSO), true)]
    public class MontageSequenceSOEditor : UnityEditor.Editor
    {
        #region Private Fields

        private MontageSequenceAsset _targetNewAsset;

        #endregion

        #region Unity Lifecycle

        public override void OnInspectorGUI()
        {
            var targetSO = (MontageSequenceSO)target;

            EditorGUILayout.Space(6);

            // 只读与升级提示面板
            EditorGUILayout.HelpBox(
                "【旧版蒙太奇资产（只读兼容模式）】\n" +
                "此资产已停止支持在时间轴编辑器中打开编辑（游戏运行时仍 100% 保持完全兼容）。\n" +
                "如需进行可视化编辑或后续扩展，请将配置数据拷贝迁移至新版资产（MontageSequenceAsset 或 SkillMontageAsset 等派生类）。",
                MessageType.Warning);

            EditorGUILayout.Space(4);

            // 模式 1：同步到指定的新版资产实例（支持具体子类）
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("配置拷贝与迁移", EditorStyles.boldLabel);

            _targetNewAsset = (MontageSequenceAsset)EditorGUILayout.ObjectField(
                new GUIContent("目标新版资产", "请拖入已创建好的新版资产（如 SkillMontageAsset 或 MontageSequenceAsset）"),
                _targetNewAsset,
                typeof(MontageSequenceAsset),
                false);

            GUI.enabled = _targetNewAsset != null;
            GUI.backgroundColor = new Color(0.2f, 0.65f, 0.95f);
            if (GUILayout.Button("将当前旧配置完整拷贝到指定新资产", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog("确认覆盖", $"确定要将旧资产 '{targetSO.name}' 的所有轨道、切片和动作块数据覆盖同步到 '{_targetNewAsset.name}' 吗？", "确定覆盖", "取消"))
                {
                    if (MontageMigrationUtility.CopyLegacyDataToTargetAsset(targetSO, _targetNewAsset))
                    {
                        EditorUtility.DisplayDialog("同步成功", $"已成功将旧配置数据同步到新资产 '{_targetNewAsset.name}'！\n您现在可以双击该新资产在时间轴编辑器中进行编辑。", "确定");
                        Selection.activeObject = _targetNewAsset;
                    }
                }
            }
            GUI.enabled = true;
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(2);

            // 模式 2：一键自动创建并迁移为标准新资产
            if (GUILayout.Button("自动创建同名新资产并迁移数据", GUILayout.Height(26)))
            {
                var newAsset = MontageMigrationUtility.ConvertLegacyToNewAsset(targetSO);
                if (newAsset != null)
                {
                    _targetNewAsset = newAsset;
                    Selection.activeObject = newAsset;
                }
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            DrawDefaultInspector();
        }

        #endregion
    }
}
