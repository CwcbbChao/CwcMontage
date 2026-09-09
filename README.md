# CwcMontage - 高性能纯表现层动作蒙太奇系统

[![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-blue.svg)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/CwcbbChao/CwcMontage/pulls)
[![Docs](https://img.shields.io/badge/Documentation-Online-brightgreen.svg)](https://cwcbbchao.github.io/CwcMontage/)

[English](README_EN.md) | **简体中文**

> **官方在线文档**：[https://cwcbbchao.github.io/CwcMontage/](https://cwcbbchao.github.io/CwcMontage/)  
> 查阅更详尽的架构深度解析、API 索引、动图演示与进阶开发教程。

---

## 插件简介

`CwcMontage` 是一个专为现代高品质动作游戏（ACT / ARPG / TopDown）量身定制的**轻量化、高性能、纯表现层**动作蒙太奇系统。

系统基于 Unity **Playables API** 构建，采用**逻辑与表现双轨分离（Dual-Track Pipeline）**设计理念：外部逻辑层（如 Gameplay 技能系统、状态机）绝对主导动作判定与时钟流向，而 `CwcMontage` 负责精确的动画采样、平滑混音、多轨道视听触发以及 Root Motion 委托分发。

---

## 视觉与交互演示

### 1. 蒙太奇时间轴编辑器 (Montage Editor)
实时视口洗牌（Scrubbing）采样、零开销视听效果预览与物理分段编辑：

![Editor Overview](docs/public/images/editor_overview.gif)

### 2. 运行时平滑混音与分段控制 (Runtime Showcase)
双缓冲槽 Ping-Pong 交叉淡化、自适应时钟缩放与事件成对触发：

![Runtime Demo](docs/public/images/runtime_demo.gif)

---

## 核心设计特性

1. **纯表现层与开闭原则 (OCP)**
   - 核心运行时完全解耦具体业务逻辑，零侵入硬编码。
   - 提供 `MontageActionBlockBase` 与 `MontageSpatialActionBlockBase`，开发者可自由扩展音效、特效、顿帧、相机震动或受击盒判定。
2. **区间扫掠无漏帧算法 (Interval Sweep Sampling)**
   - 采用增量半开区间 `(LastTime, CurrentTime]` 判定，彻底杜绝在极端低帧率卡顿下跳帧漏事件的问题。
   - 保证所有动作块的 `OnEnter -> OnUpdate -> OnExit` 严格成对触发，原生支持倒放与瞬移 Seek。
3. **去语义化物理分段 (Native Physical Sections)**
   - 仅做客观时间切分，不预设前摇/后摇/击发点等硬编码业务语义。
   - 提供 $O(1)$ 零 GC 分段时长查询与范围检测。
4. **分段自适应时钟缩放 (Adaptive Time Warping)**
   - 外部调用 `handle.SyncSectionDuration(sectionIndex, targetDuration)`，底层根据原始动作几何时长自适应换算并平滑驱动 Playable 播放速率，化解策划数值与动作美术资产的冲突。
5. **固定双缓冲槽 CrossFade 混音拓扑 (Dual-Slot Ping-Pong Mixer)**
   - 采用固定 2 槽双缓冲结构进行 CrossFade 平滑过渡，彻底淘汰动态断连与数组移位，PlayableGraph 终身稳定且零 GC。
6. **Root Motion 委托化解耦分发**
   - 掩码过滤水平/垂直/旋转分量后，通过 `IMontageRootMotionReceiver` 接口或事件抛出，不污染角色现有的物理移动控制器（如 CharacterController 或 KCC）。

---

## 安装方式

### 方式 A：通过 Unity Package Manager (Git URL 推荐)
1. 打开 Unity 编辑器菜单栏：`Window` -> `Package Manager`。
2. 点击左上角 `+` 号 -> 选择 **Add package from git URL...**。
3. 输入仓库地址：
   ```
   https://github.com/CwcbbChao/CwcMontage.git
   ```
4. 点击 **Add** 即可完成自动安装。

### 方式 B：通过 OpenUPM 安装 (CLI)
```bash
openupm add com.cwcbb.cwcmontage
```

---

## 快速上手 (Quick Start)

### 1. 播放蒙太奇并动态同步分段时长
```csharp
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

public class HeroCombatController : MonoBehaviour
{
    [SerializeField] private MontageCoordinator _coordinator;
    [SerializeField] private MontageSequenceSO _swordAttackMontage;

    public void PerformAttack(float windupDuration, float activeDuration, float recoveryDuration)
    {
        // 1. 启动蒙太奇播放，获取智能安全句柄 (MontageHandle，16 字节值类型，零 GC)
        MontageHandle handle = _coordinator.Play(_swordAttackMontage);

        // 2. 根据玩法数值，自适应缩放各物理分段的目标物理时长
        if (handle.IsValid)
        {
            handle.SyncSectionDuration(0, windupDuration);    // 前摇段自适应
            handle.SyncSectionDuration(1, activeDuration);    // 攻击判定段自适应
            handle.SyncSectionDuration(2, recoveryDuration);  // 后摇段自适应
        }
    }
}
```

### 2. 派生自定义动作块 (例如顿帧/震屏)
```csharp
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

[MontageCategory("Combat")]
[MontageColor("#e74c3c")]
[MontageDisplayName("Hit Stop / Freeze Frame")]
public class HitStopActionBlock : MontageActionBlockBase
{
    [SerializeField] private float _timeScale = 0.05f;

    public override void OnEnter(in MontageActionContext context)
    {
        base.OnEnter(context);
        // 执行顿帧逻辑
    }

    public override void OnExit(in MontageActionContext context)
    {
        base.OnExit(context);
        // 恢复时间流速
    }
}
```

---

## 演示场景与 Samples

本插件包含开箱即用的完整战斗动作演示工程：
- **场景路径**：`Demo/Scenes/MontageDemoScene.unity`。
- **操作方式**：
  - **1 - 9**：切换播放各类招式（三连斩、翻滚、连拳等）。
  - **Space**：暂停 / 恢复当前播放。
  - **Tab**：强制跳转至下一个物理分段。
  - **Q / E / R**：慢放 (0.5x) / 正常 (1.0x) / 加速 (1.5x)。
  - 界面左上角配备实时 Playables 监控面板，可观察混音槽权重与分段进度。
- **安全删除**：生产部署时可安全删除 `Demo` 目录，核心 `Runtime` 与 `Editor` 模块零外部依赖。

---

## 核心架构与类职责

| 类名 | 命名空间 | 职责定位 |
| :--- | :--- | :--- |
| `MontageSequenceSO` | `Cwcbb.Tools.CwcMontage` | 蒙太奇配置资产 (ScriptableObject)，持有动画、曲线、物理分段与轨道数据 |
| `MontageActionBlockBase` | `Cwcbb.Tools.CwcMontage` | 自定义视听动作块的抽象基类 |
| `MontageHandle` | `Cwcbb.Tools.CwcMontage` | 智能结构体句柄（带代际校验与悬挂检测，零 GC 驱动与查询） |
| `MontagePlayer` | `Cwcbb.Tools.CwcMontage` | 纯 C# 运行时播放器，执行增量时间采样、时钟对齐与分段状态推进 |
| `MontageCoordinator` | `Cwcbb.Tools.CwcMontage` | 挂载在角色上的 MonoBehaviour，管理 Playables 混音图与 Root Motion 广播 |
| `IMontageRootMotionReceiver` | `Cwcbb.Tools.CwcMontage` | 根运动接收者接口，解耦外部物理移动系统 |

---

## 开源协议与第三方资产许可

- 本项目核心源码采用 [MIT License](LICENSE) 开源许可。
- 内置演示角色模型与动画源自 [Quaternius](https://quaternius.com) 的 Universal Animation Library，遵循 **CC0 1.0 Universal (Public Domain Dedication)** 协议，允许无限制商业使用与二次分发。
- 完整第三方声明详见 [Third-Party Notices.txt](Third-Party%20Notices.txt)。
