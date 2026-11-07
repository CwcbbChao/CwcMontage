using UnityEditor;
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 新版现代蒙太奇配置资产（MontageSequenceAsset）的自定义 Inspector。
    /// 提供快捷“Open in Montage Editor”按钮与数据折叠展示。
    /// </summary>
    [CustomEditor(typeof(MontageSequenceAsset), true)]
    public class MontageSequenceAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var targetAsset = (MontageSequenceAsset)target;

            EditorGUILayout.Space(6);
            GUI.backgroundColor = new Color(0.2f, 0.5f, 0.85f);
            if (GUILayout.Button("Open in Montage Editor", GUILayout.Height(34)))
            {
                MontageEditorWindow.OpenAsset(targetAsset);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.Space(6);

            // 基础状态统计
            if (targetAsset != null)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("蒙太奇资产概览", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"总时长: {targetAsset.TotalDuration:F2}s  |  帧率: {targetAsset.FrameRate:F0}fps  |  总帧数: {targetAsset.TotalFrames}");
                EditorGUILayout.LabelField($"分段数量: {targetAsset.SectionCount}  |  动作轨道数: {(targetAsset.Tracks != null ? targetAsset.Tracks.Count : 0)}");
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(6);
            }

            DrawDefaultInspector();
        }
    }
}
