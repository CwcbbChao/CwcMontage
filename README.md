# CwcMontage - 纯表现层动作蒙太奇插件

`CwcMontage` 是一个面向现代动作游戏（ACT / ARPG / TopDown）的高性能、轻量级、纯表现层动画蒙太奇系统。

它基于 Unity **Playables API** 构建，采用**逻辑与表现双轨分离（Dual-Track Pipeline）**设计理念，与能力系统（如 `CwcAbilitySystem`）无缝协同。

---

## 核心设计特性

1. **纯表现层与开闭原则 (OCP)**
   - 核心系统不知道任何具体的业务表现（不硬编码音效、特效或顿帧），纯粹依赖 `MontageActionBlockBase` 抽象。
   - 开发者可自由派生创建自定义的表现 ActionBlock，零侵入核心源码。

2. **去语义化物理分段 (Native Physical Sections)**
   - 资产内生支持 `_splitTimestamps` 切分点列表，仅做客观几何切分，不预设任何具体的“前摇/后摇”业务语义。
   - 提供 $O(1)$ 零 GC 分段时长查询与范围获取。

3. **区间扫掠时间采样 (Interval Sweep / Overlap Sampling)**
   - 采用增量区间 `(LastTime, CurrentTime]` 判定，彻底杜绝在极端掉帧卡顿下跳帧漏事件的问题。
   - 确保所有动作块的 `OnEnter -> OnUpdate -> OnExit` 严格成对触发，并天然支持时间倒放与瞬间 Seek。

4. **分段自适应时钟缩放 (Adaptive Time Warping)**
   - 外部逻辑仅需调用 `player.SyncSection(index, targetDuration)`，表现层自动根据原始物理时长换算速率并驱动底层 Playable，消除美术动作时长与策划数值的配置冲突。

5. **外部主动分段跳转与进度驱动 (Section Jump & Progress Drive)**
   - 彻底废弃易导致对齐失真的内部自动循环，由外部逻辑通过 `JumpToSection`、`JumpToTime` 或 `EvaluateSectionProgress` 绝对主导分段跳转与蓄力/引导进度驱动。

6. **固定双缓冲槽 CrossFade 混音拓扑 (Dual-Slot Ping-Pong Mixer)**
   - 动作层采用固定 2 槽双缓冲结构进行 CrossFade 平滑过渡，彻底淘汰动态断连与数组移位，PlayableGraph 终身稳定且零 GC。

7. **Root Motion 委托化解耦分发**
   - 掩码过滤水平/垂直/旋转分量后，通过 `OnRootMotionDelta` 事件与 `IMontageRootMotionReceiver` 接口委托抛出，不侵入角色的物理移动控制器。

---

## 核心类概览

| 类名 | 职责 |
| :--- | :--- |
| `MontageSequenceSO` | 蒙太奇配置资产 (ScriptableObject)，包含单动画、曲线、物理分段与多轨道数据 |
| `MontageActionBlockBase` | 自定义视听表现动作块的抽象基类 |
| `MontageActionContext` | 传递当帧时间、分段进度、宿主对象与模式的只读上下文结构体 |
| `MontageHandle` | 智能结构体句柄（值类型，16 字节），提供代际安全校验、防悬挂引用与零 GC 播放控制 |
| `MontagePlayer` | 纯 C# 运行时播放器，负责时间推进、区间扫掠采样、时钟对齐与跳转响应 |
| `MontageCoordinator` | 挂载在角色上的驱动组件 (MonoBehaviour)，管理 Playables 混音图与 Root Motion 广播 |
| `IMontageRootMotionReceiver` | 根运动接收者接口，供外部移动系统按需实现 |

---

## 快速上手示例

### 1. 自定义一个表现动作块 (例如播放音效)
```csharp
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

[MontageCategory("Audio")]
[MontageColor("#007acc")]
[MontageDisplayName("Play Audio")]
public class PlaySoundBlock : MontageActionBlockBase
{
    [SerializeField] private AudioClip _clip;
    [SerializeField] private float _volume = 1.0f;

    public override void OnEnter(in MontageActionContext context)
    {
        base.OnEnter(context);
        if (_clip != null && context.TargetObject != null)
        {
            AudioSource.PlayClipAtPoint(_clip, context.TargetObject.transform.position, _volume);
        }
    }
}
```

### 2. 在角色上播放并联动分段时钟
```csharp
public class CharacterAttackExample : MonoBehaviour
{
    [SerializeField] private MontageCoordinator _montageCoordinator;
    [SerializeField] private MontageSequenceSO _attackMontage;

    public void ExecuteAttack(float windupSec, float activeSec, float recoverySec)
    {
        // 1. 启动蒙太奇并获取智能句柄 (MontageHandle)
        MontageHandle handle = _montageCoordinator.Play(_attackMontage);

        // 2. 方式 A：在第 0 帧一次性预设所有物理分段的目标物理时长（纯配置，不产生误跳转）
        if (handle.IsValid)
        {
            handle.SyncSectionDuration(0, windupSec);
            handle.SyncSectionDuration(1, activeSec);
            handle.SyncSectionDuration(2, recoverySec);
        }

        // 方式 B：或监听分段跨越事件，在进入时动态调整
        // _montageCoordinator.OnSectionChanged += (p, sectionIndex) =>
        // {
        //     if (sectionIndex == 1) handle.SyncSectionDuration(1, activeSec);
        // };
    }
}
```
