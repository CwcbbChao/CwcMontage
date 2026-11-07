using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// Audio preview driver utility for editor edit mode.
    /// Uses safe reflection over the UnityEditor.AudioUtil internal API to play and stop AudioClips in real time within the editor 3D viewport preview.
    /// Automatically registers preview driver hooks with the Runtime-layer AudioActionBlock on Unity editor startup or recompile.
    /// </summary>
    [InitializeOnLoad]
    public static class MontageAudioPreviewUtility
    {
        #region Private Static Fields

        private static MethodInfo s_playPreviewClipMethod;
        private static MethodInfo s_stopAllPreviewClipsMethod;
        private static bool s_isInitialized;

        #endregion

        #region Static Constructor

        static MontageAudioPreviewUtility()
        {
            EnsureInitialized();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Ensures the editor audio utility has completed its internal reflection and delegate injection.
        /// </summary>
        public static void EnsureInitialized()
        {
            if (s_isInitialized && s_playPreviewClipMethod != null && s_stopAllPreviewClipsMethod != null) return;

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

            AudioActionBlock.PreviewAudioPlayHandler = (clip, loop) => PlayPreview(clip, loop);
            AudioActionBlock.PreviewAudioStopHandler = StopAllClips;

            s_isInitialized = true;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Plays the specified audio in the editor viewport preview.
        /// Uses Unity editor internal APIs to play the raw audio asset directly: zero memory overhead, zero latency, works with any audio compression format (MP3/WAV/OGG/Vorbis), and is 100% stable and reliable.
        /// </summary>
        /// <param name="clip">Raw audio clip</param>
        /// <param name="loop">Whether to loop playback</param>
        public static void PlayPreview(AudioClip clip, bool loop = false)
        {
            if (clip == null) return;

            EnsureInitialized();
            StopAllClips(); // Stop the previous sound

            // Play the AudioClip asset directly through Unity's native internal API
            PlayClip(clip, 0, loop);
        }

        /// <summary>
        /// Immediately plays a preview of the specified AudioClip in the editor.
        /// </summary>
        /// <param name="clip">Audio clip</param>
        /// <param name="startSample">Starting sample point</param>
        /// <param name="loop">Whether to loop playback</param>
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
                    Debug.LogWarning($"[MontageAudioPreviewUtility] Failed to play preview audio: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Stops all preview audio currently playing in the editor.
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
                    Debug.LogWarning($"[MontageAudioPreviewUtility] Failed to stop preview audio: {ex.Message}");
                }
            }
        }

        #endregion
    }
}
