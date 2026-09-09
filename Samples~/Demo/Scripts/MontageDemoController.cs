using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage.Demo
{
    /// <summary>
    /// 蒙太奇运行时演示控制驱动组件。
    /// 负责与 MontageCoordinator 协同，响应 UI 或快捷键输入，驱动蒙太奇播放、暂停、调速、跳转分段及自适应时钟缩放。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MontageCoordinator))]
    public class MontageDemoController : MonoBehaviour
    {
        #region 常量定义

        private const float DEFAULT_BLEND_OUT_TIME = 0.2f;

        #endregion

        #region Inspector 字段

        [Header("演示蒙太奇资产列表")]
        [Tooltip("可供演示切换的蒙太奇配置资产")]
        [SerializeField] private List<MontageSequenceSO> _demoMontages = new();

        [Header("播放配置")]
        [Tooltip("启动场景时是否自动播放第一个蒙太奇")]
        [SerializeField] private bool _playOnStart = true;

        [Tooltip("是否启用键盘快捷键控制 (数字键1-9切换, 空格暂停, S停止, Tab跳分段)")]
        [SerializeField] private bool _enableKeyboardShortcuts = true;

        #endregion

        #region 私有字段

        private MontageCoordinator _coordinator;
        private MontageHandle _activeHandle;
        private int _currentMontageIndex = -1;
        private float _currentSpeed = 1.0f;

        #endregion

        #region 属性

        /// <summary>
        /// 当前正在播放或控制的蒙太奇智能句柄。
        /// </summary>
        public MontageHandle ActiveHandle => _activeHandle;

        /// <summary>
        /// 演示蒙太奇资产列表只读视图。
        /// </summary>
        public IReadOnlyList<MontageSequenceSO> DemoMontages => _demoMontages;

        /// <summary>
        /// 当前正在播放的蒙太奇索引。若无则返回 -1。
        /// </summary>
        public int CurrentMontageIndex => _currentMontageIndex;

        /// <summary>
        /// 当前设定的播放倍速。
        /// </summary>
        public float CurrentSpeed => _currentSpeed;

        /// <summary>
        /// 关联的驱动协调器组件。
        /// </summary>
        public MontageCoordinator Coordinator => _coordinator;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            _coordinator = GetComponent<MontageCoordinator>();
            if (_coordinator == null)
            {
                Debug.LogError($"[MontageDemoController] 在物体 '{gameObject.name}' 上未找到 MontageCoordinator 组件！", this);
            }
        }

        private void Start()
        {
            if (_playOnStart && _demoMontages.Count > 0 && _demoMontages[0] != null)
            {
                PlayMontage(0);
            }
        }

        private void Update()
        {
            if (_enableKeyboardShortcuts)
            {
                HandleKeyboardInput();
            }
        }

        #endregion

        #region 公共控制方法

        /// <summary>
        /// 播放指定索引的蒙太奇资产。
        /// </summary>
        /// <param name="index">蒙太奇在列表中的索引</param>
        /// <param name="customBlendInTime">自定义平滑淡入时长（秒）</param>
        public void PlayMontage(int index, float? customBlendInTime = null)
        {
            if (_coordinator == null)
            {
                Debug.LogWarning($"[MontageDemoController] Coordinator 未初始化，无法播放！", this);
                return;
            }

            if (index < 0 || index >= _demoMontages.Count)
            {
                Debug.LogWarning($"[MontageDemoController] 索引 {index} 超出有效范围 (0 ~ {_demoMontages.Count - 1})！", this);
                return;
            }

            var targetMontage = _demoMontages[index];
            if (targetMontage == null)
            {
                Debug.LogWarning($"[MontageDemoController] 索引 {index} 对应的蒙太奇资产为空！", this);
                return;
            }

            _currentMontageIndex = index;
            _activeHandle = _coordinator.Play(targetMontage, customBlendInTime);

            if (_activeHandle.IsValid && Mathf.Abs(_currentSpeed - 1.0f) > 0.001f)
            {
                _activeHandle.SetPlaybackRate(_currentSpeed);
            }
        }

        /// <summary>
        /// 切换当前蒙太奇的暂停与恢复状态。
        /// </summary>
        public void TogglePause()
        {
            if (!_activeHandle.IsValid) return;

            _activeHandle.SetPaused(!_activeHandle.IsPaused);
        }

        /// <summary>
        /// 停止当前正在播放的蒙太奇。
        /// </summary>
        /// <param name="blendOutTime">淡出过渡时长</param>
        public void StopCurrent(float blendOutTime = DEFAULT_BLEND_OUT_TIME)
        {
            if (_activeHandle.IsValid)
            {
                _activeHandle.Stop(blendOutTime);
            }
            _currentMontageIndex = -1;
        }

        /// <summary>
        /// 设定播放速率倍率。
        /// </summary>
        /// <param name="speed">目标倍速</param>
        public void SetSpeed(float speed)
        {
            _currentSpeed = Mathf.Max(0.01f, speed);
            if (_activeHandle.IsValid)
            {
                _activeHandle.SetPlaybackRate(_currentSpeed);
            }
        }

        /// <summary>
        /// 瞬间跳转到指定物理分段起始时间。
        /// </summary>
        /// <param name="sectionIndex">物理分段索引</param>
        public void JumpToSection(int sectionIndex)
        {
            if (!_activeHandle.IsValid) return;

            if (sectionIndex >= 0 && sectionIndex < _activeHandle.SectionCount)
            {
                _activeHandle.JumpToSection(sectionIndex);
            }
            else
            {
                Debug.LogWarning($"[MontageDemoController] 分段索引 {sectionIndex} 超出范围 (0 ~ {_activeHandle.SectionCount - 1})！", this);
            }
        }

        /// <summary>
        /// 循环跳转到下一个物理分段。
        /// </summary>
        public void JumpToNextSection()
        {
            if (!_activeHandle.IsValid || _activeHandle.SectionCount <= 0) return;

            int current = _activeHandle.CurrentSectionIndex;
            int next = (current + 1) % _activeHandle.SectionCount;
            _activeHandle.JumpToSection(next);
        }

        /// <summary>
        /// 动态调整当前所处物理分段的目标物理时长（自适应时钟缩放）。
        /// 消除美术动画原有时长与数值策划要求的冲突。
        /// </summary>
        /// <param name="targetDuration">目标实际播放时长（秒）</param>
        public void SyncCurrentSectionDuration(float targetDuration)
        {
            if (!_activeHandle.IsValid) return;

            int currentSection = _activeHandle.CurrentSectionIndex;
            if (currentSection >= 0)
            {
                _activeHandle.SyncSectionDuration(currentSection, targetDuration);
            }
        }

        #endregion

        #region 私有辅助方法

        private void HandleKeyboardInput()
        {
            // 数字键 1-9 切换蒙太奇
            for (int i = 0; i < 9 && i < _demoMontages.Count; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    PlayMontage(i);
                    return;
                }
            }

            // 空格键：暂停 / 恢复
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TogglePause();
                return;
            }

            // S 键：平滑停止
            if (Input.GetKeyDown(KeyCode.S))
            {
                StopCurrent();
                return;
            }

            // Tab 键：跳转到下一个分段
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                JumpToNextSection();
                return;
            }

            // Q 键：加速 (2.0x), E 键：慢速 (0.5x), R 键：恢复正常 (1.0x)
            if (Input.GetKeyDown(KeyCode.Q))
            {
                SetSpeed(2.0f);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                SetSpeed(0.5f);
            }
            else if (Input.GetKeyDown(KeyCode.R))
            {
                SetSpeed(1.0f);
            }
        }

        #endregion
    }
}
