# 核心 API 参考

本文档汇总了 `CwcMontage` 运行时的核心公共类、句柄结构体与关键接口。

---

## 1. MontageHandle (句柄结构体)

`MontageHandle` 是外部逻辑与表现层交互的核心媒介。它是一个 16 字节的值类型结构体，内置代际验证（Generation Check），能彻底杜绝因动作已结束而导致的悬挂引用。

```csharp
namespace Cwcbb.Tools.CwcMontage
{
    public readonly struct MontageHandle
    {
        // 状态属性
        public bool IsValid { get; }          // 当前句柄是否依然指向有效的播放实例
        public bool IsPlaying { get; }        // 是否正在播放
        public bool IsPaused { get; }         // 是否处于暂停状态
        public float CurrentTime { get; }     // 当前播放局部时间（秒）
        public int CurrentSection { get; }    // 当前所处的物理分段索引 (0-based)

        // 时钟与分段控制
        public bool SyncSectionDuration(int sectionIndex, float targetDuration);
        public bool JumpToSection(int targetSectionIndex);
        public bool JumpToTime(float targetTime);
        public bool EvaluateSectionProgress(int sectionIndex, float normalizedProgress);

        // 播放控制
        public void Pause();
        public void Resume();
        public void Stop(float fadeOutDuration = 0.1f);

        // 事件监听
        public event System.Action OnCompleted;
    }
}
```

---

## 2. MontageCoordinator (角色驱动组件)

挂载于角色 GameObject 上的 MonoBehaviour，负责管理整个 Playables 混音图与 Root Motion 广播。

```csharp
namespace Cwcbb.Tools.CwcMontage
{
    public class MontageCoordinator : MonoBehaviour
    {
        // 启动播放
        public MontageHandle Play(MontageSequenceSO sequence, float crossFadeDuration = 0.15f);

        // 全局控制
        public void StopAll(float fadeOutDuration = 0.1f);
        public void PauseAll();
        public void ResumeAll();

        // 状态与事件
        public bool IsAnyPlaying { get; }
        public event System.Action<MontageHandle, int> OnSectionChanged;
        public event System.Action<Vector3, Quaternion> OnRootMotionDelta;
    }
}
```

---

## 3. IMontageRootMotionReceiver (接口)

供角色物理移动组件实现的零 GC 根运动接收接口：

```csharp
namespace Cwcbb.Tools.CwcMontage
{
    public interface IMontageRootMotionReceiver
    {
        /// <summary>
        /// 当蒙太奇在当帧产生根运动位移/旋转增量时由驱动器直接调用
        /// </summary>
        /// <param name="deltaPosition">当帧位移增量（已过滤掩码）</param>
        /// <param name="deltaRotation">当帧旋转增量（已过滤掩码）</param>
        void OnMontageRootMotion(Vector3 deltaPosition, Quaternion deltaRotation);
    }
}
```

---

## 4. MontageActionBlockBase (动作块基类)

派生自定义视听表现的核心基类：

```csharp
namespace Cwcbb.Tools.CwcMontage
{
    public abstract class MontageActionBlockBase
    {
        public float StartTime { get; set; }
        public float Duration { get; set; }

        public virtual void OnEnter(in MontageActionContext context) { }
        public virtual void OnUpdate(in MontageActionContext context) { }
        public virtual void OnExit(in MontageActionContext context) { }
        public virtual void OnSample(in MontageActionContext context) { }
    }
}
```
