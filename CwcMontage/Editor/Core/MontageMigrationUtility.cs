using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 蒙太奇配置资产迁移工具（MontageMigrationUtility）。
    /// 支持将旧版 MontageSequenceSO（包括派生类如 SkillMontageSO）一键深度克隆为现代 MontageSequenceAsset（或 SkillMontageAsset）。
    /// </summary>
    public static class MontageMigrationUtility
    {
        /// <summary>
        /// 将旧版 MontageSequenceSO 的配置数据完整拷贝覆盖到指定的 MontageSequenceAsset 目标实例（支持 SkillMontageAsset 等任意派生类）。
        /// </summary>
        /// <param name="legacy">源旧版资产</param>
        /// <param name="target">目标新版资产</param>
        public static bool CopyLegacyDataToTargetAsset(MontageSequenceSO legacy, MontageSequenceAsset target)
        {
            if (legacy == null || target == null)
            {
                Debug.LogError("[MontageMigration] 拷贝失败：源资产或目标资产为空。");
                return false;
            }

            Undo.RecordObject(target, "Sync Montage Data from Legacy SO");

            // 深度克隆数据并注入
            var clonedData = new MontageSequenceData(legacy);
            var dataField = typeof(MontageSequenceAsset).GetField("_data", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (dataField != null)
            {
                dataField.SetValue(target, clonedData);
            }

            target.EnsureSegmentsValid();
            target.ValidateActionBlocks();
            target.InvalidateBakedBlocks();

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();

            Debug.Log($"[MontageMigration] 成功将 '{legacy.name}' 的配置数据完整同步至目标资产 '{target.name}' (类型: {target.GetType().Name})。");
            return true;
        }

        /// <summary>
        /// 将指定的旧版 MontageSequenceSO 转换为新版 MontageSequenceAsset 资产。
        /// </summary>
        /// <param name="legacy">旧版蒙太奇资产实例</param>
        /// <param name="targetPath">保存路径（若为空则在同目录下自动生成）</param>
        /// <returns>生成的新版资产实例</returns>
        public static MontageSequenceAsset ConvertLegacyToNewAsset(MontageSequenceSO legacy, string targetPath = null)
        {
            if (legacy == null)
            {
                Debug.LogError("[MontageMigration] 无法转换：输入的旧版蒙太奇资产为空。");
                return null;
            }

            string sourcePath = AssetDatabase.GetAssetPath(legacy);
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogError($"[MontageMigration] 无法定位资产 '{legacy.name}' 的磁盘路径。");
                return null;
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                string dir = Path.GetDirectoryName(sourcePath);
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
                string newFileName = $"{fileNameWithoutExt}_Asset.asset";
                targetPath = Path.Combine(dir, newFileName).Replace("\\", "/");
                targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
            }

            // 1. 判断是否为业务层派生类型（如 SkillMontageSO -> SkillMontageAsset）
            MontageSequenceAsset newAsset;
            Type legacyType = legacy.GetType();
            Type targetAssetType = typeof(MontageSequenceAsset);

            // 尝试通过反射动态查找 SkillMontageAsset（避免 CwcMontage 插件对业务层代码产生逆向强依赖）
            if (legacyType.Name.Contains("SkillMontage"))
            {
                var skillAssetType = Type.GetType("Cwcbb.GameSystem.SkillMontageAsset, Assembly-CSharp");
                if (skillAssetType != null)
                {
                    targetAssetType = skillAssetType;
                }
            }

            newAsset = (MontageSequenceAsset)ScriptableObject.CreateInstance(targetAssetType);

            // 2. 将旧版数据 100% 深度克隆注入新版纯 C# 模型
            var clonedData = new MontageSequenceData(legacy);
            var dataField = typeof(MontageSequenceAsset).GetField("_data", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (dataField != null)
            {
                dataField.SetValue(newAsset, clonedData);
            }

            // 3. 保存新资产到磁盘
            AssetDatabase.CreateAsset(newAsset, targetPath);
            EditorUtility.SetDirty(newAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[MontageMigration] 成功转换蒙太奇资产: '{sourcePath}' -> '{targetPath}' (类型: {targetAssetType.Name})");
            EditorGUIUtility.PingObject(newAsset);
            return newAsset;
        }

        /// <summary>
        /// 批量转换指定目录下的所有旧版蒙太奇资产。
        /// </summary>
        /// <param name="folderPath">目标文件夹路径（例如 'Assets/Data/Skills/Presentations/Montages'）</param>
        /// <returns>成功转换的数量</returns>
        public static int BatchConvertDirectory(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                Debug.LogError($"[MontageMigration] 目录不存在: {folderPath}");
                return 0;
            }

            string[] guids = AssetDatabase.FindAssets("t:MontageSequenceSO", new[] { folderPath });
            int convertedCount = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var legacy = AssetDatabase.LoadAssetAtPath<MontageSequenceSO>(path);
                    if (legacy != null)
                    {
                        EditorUtility.DisplayProgressBar("Batch Converting Montages", $"Converting {legacy.name} ({i + 1}/{guids.Length})...", (float)i / guids.Length);
                        ConvertLegacyToNewAsset(legacy);
                        convertedCount++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[MontageMigration] 批量转换完成，共成功迁移 {convertedCount} 个蒙太奇资产。");
            return convertedCount;
        }

        /// <summary>
        /// 将全局工程中引用旧资产的 YAML 文本 GUID 批量替换为新资产的 GUID。
        /// </summary>
        public static int RedirectGlobalGuidReferences(MontageSequenceSO oldAsset, MontageSequenceAsset newAsset)
        {
            if (oldAsset == null || newAsset == null) return 0;

            string oldGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(oldAsset));
            string newGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(newAsset));

            if (string.IsNullOrEmpty(oldGuid) || string.IsNullOrEmpty(newGuid) || oldGuid == newGuid)
            {
                return 0;
            }

            string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
            int modifiedCount = 0;

            for (int i = 0; i < allAssetPaths.Length; i++)
            {
                string path = allAssetPaths[i];
                if (!path.StartsWith("Assets/")) continue;
                if (!path.EndsWith(".prefab") && !path.EndsWith(".unity") && !path.EndsWith(".asset")) continue;

                // 跳过自身
                if (path == AssetDatabase.GetAssetPath(oldAsset) || path == AssetDatabase.GetAssetPath(newAsset)) continue;

                string fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) continue;

                string text = File.ReadAllText(fullPath);
                if (text.Contains(oldGuid))
                {
                    text = text.Replace(oldGuid, newGuid);
                    File.WriteAllText(fullPath, text);
                    modifiedCount++;
                    Debug.Log($"[MontageMigration] 已将资产 '{path}' 中对旧资产的引用更新为新资产 GUID。");
                }
            }

            if (modifiedCount > 0)
            {
                AssetDatabase.Refresh();
            }

            return modifiedCount;
        }
    }
}
