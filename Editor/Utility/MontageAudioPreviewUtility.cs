using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 编辑器非运行模式下的音频预览驱动工具。
    /// 通过安全反射 UnityEditor.AudioUtil 底层接口，在编辑器 3D 视口预览中实时播放与停止 AudioClip。
    /// 自动在 Unity 编辑器启动或重编译时向 Runtime 层的 AudioActionBlock 注册预览驱动钩子。
    /// </summary>
    [InitializeOnLoad]
    public static class MontageAudioPreviewUtility
    {
        #region 私有静态字段

        private static MethodInfo s_playPreviewClipMethod;
        private static MethodInfo s_stopAllPreviewClipsMethod;
        private static bool s_isInitialized;

        #endregion

        #region 静态构造函数

        static MontageAudioPreviewUtility()
        {
            EnsureInitialized();
            AudioActionBlock.PreviewAudioPlayHandler = (clip, loop) => PlayClip(clip, 0, loop);
            AudioActionBlock.PreviewAudioStopHandler = StopAllClips;
        }

        #endregion

        #region 初始化

        private static void EnsureInitialized()
        {
            if (s_isInitialized) return;

            var audioUtilType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AudioUtil");
            if (audioUtilType != null)
            {
                s_playPreviewClipMethod = audioUtilType.GetMethod(
                    "PlayPreviewClip",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(AudioClip), typeof(int), typeof(bool) },
                    null);

                s_stopAllPreviewClipsMethod = audioUtilType.GetMethod(
                    "StopAllPreviewClips",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }

            s_isInitialized = true;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 在编辑器中即时播放指定的 AudioClip 预览。
        /// </summary>
        /// <param name="clip">音频片段</param>
        /// <param name="startSample">起始采样点</param>
        /// <param name="loop">是否循环播放</param>
        public static void PlayClip(AudioClip clip, int startSample = 0, bool loop = false)
        {
            if (clip == null) return;

            EnsureInitialized();

            if (s_playPreviewClipMethod != null)
            {
                try
                {
                    s_playPreviewClipMethod.Invoke(null, new object[] { clip, startSample, loop });
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MontageAudioPreviewUtility] 播放预览音频失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 停止编辑器中正在播放的所有预览音频。
        /// </summary>
        public static void StopAllClips()
        {
            EnsureInitialized();

            if (s_stopAllPreviewClipsMethod != null)
            {
                try
                {
                    s_stopAllPreviewClipsMethod.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MontageAudioPreviewUtility] 停止预览音频失败: {ex.Message}");
                }
            }
        }

        #endregion
    }
}
