using UnityEditor;
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// MontageSequenceSO 自定义 Inspector 编辑器。
    /// 提供醒目的 "Open in Montage Editor" 快捷操作按钮。
    /// </summary>
    [CustomEditor(typeof(MontageSequenceSO), true)]
    public class MontageSequenceSOEditor : UnityEditor.Editor
    {
        #region Unity 生命周期

        public override void OnInspectorGUI()
        {
            var targetSO = (MontageSequenceSO)target;

            EditorGUILayout.Space(6);
            GUI.backgroundColor = new Color(0.2f, 0.5f, 0.85f);
            if (GUILayout.Button("Open in Montage Editor (打开蒙太奇编辑器)", GUILayout.Height(34)))
            {
                MontageEditorWindow.OpenAsset(targetSO);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.Space(6);

            DrawDefaultInspector();
        }

        #endregion
    }
}
