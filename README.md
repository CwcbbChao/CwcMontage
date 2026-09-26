# CwcMontage - 高性能纯表现层动作蒙太奇系统

[![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-blue.svg)](https://unity.com/)
[![License](https://img.shields.io/badge/License-Custom%20(Free%20for%20Games)-blue.svg)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/CwcbbChao/CwcMontage/pulls)
[![Docs](https://img.shields.io/badge/Documentation-Online-brightgreen.svg)](https://cwcbbchao.github.io/CwcMontage/)

[English](README_EN.md) | **简体中文**

> **官方在线文档**：[https://cwcbbchao.github.io/CwcMontage/](https://cwcbbchao.github.io/CwcMontage/)  
> 查阅更详尽的架构深度解析、API 索引、动图演示与进阶开发教程。

---

## 插件简介

`CwcMontage` 是一个专为 Unity 动作游戏（ACT / ARPG / Top-Down）打造的**轻量级、高性能、纯表现层**动作蒙太奇系统。

系统基于 Unity **Playables API** 构建，将**动画表现层与战斗玩法逻辑彻底解耦**：外部状态机或技能系统负责业务判定与节奏调度，`CwcMontage` 专注于多轨道视听打点、动画片段平滑过渡、分段变速以及 Root Motion 的安全派发。

---

## 视觉与交互演示

### 1. 蒙太奇时间轴编辑器 (Montage Editor)
支持时间轴拖拽（Scrubbing）精准采样、粒子特效实时切片预览、多片段修剪与分段编辑：

![Editor Overview](docs/public/images/editor_overview.gif)

### 2. 运行时平滑混音与分段控制 (Runtime Showcase)
预分配双插槽 CrossFade 平滑过渡、分段动态调速与事件防丢帧触发：

![Runtime Demo](docs/public/images/runtime_demo.gif)

---

## 核心设计特性

1. **专注表现层，与战斗玩法逻辑解耦**
   - 核心系统不绑定任何具体技能或数值逻辑，由外部系统驱动播放与跳转。
   - 提供 `MontageActionBlockBase` 与 `MontageSpatialActionBlockBase`，开发者可自由扩展音效、特效、顿帧、相机震动或伤害判定盒。
2. **多层级混音拓扑 (FullBody / UpperBody / Additive)**
   - 基于 Playables API 组织图层，原生支持**全身 (Full Body)**、**上半身 (Upper Body)** 与**叠加 (Additive)** 三轨并行独立混音与权重控制。
   - 满足边跑边打、移动施法，以及在奔跑或大招时叠加受击抖动、开火后坐力等战斗需求。
3. **上半身脊柱 (Spine) 朝向补偿**
   - 针对 Humanoid 角色边跑边打时上半身随骨盆剧烈晃动、劈砍瞄准歪斜的引擎痛点。
   - 运行时通过源动画 Spine 相对 Root 旋转反解并修正局部朝向，通用 Humanoid 动作无需在 DCC 建模软件中重修骨骼即可保持稳定朝向。
4. **动作分段 (Sections) 与动态调速**
   - 资产仅记录时间切分点，不写死前摇/判定/后摇枚举，由玩法逻辑按需映射。
   - 支持外部调用 `handle.SyncSectionDuration(sectionIndex, targetDuration)` 动态调整指定分段的播放速率，便于策划调整打击帧与动作节奏而无需美术重新导出动画。
   - 支持外部进度驱动（`handle.EvaluateSectionProgress`），便于实现按键蓄力与连招取消。
5. **半开区间扫掠，事件严格成对触发**
   - 采用 `(LastTime, CurrentTime]` 半开区间判定，低帧率或卡顿跨越整个事件块时自动按序触发 `OnEnter` 与 `OnExit`，彻底杜绝特效或音效常驻残留。
6. **0-GC 纯只读配置与状态外置架构 (Immutable & State Decoupled)**
   - 彻底消灭运行时 `blockData.Clone()` 与堆内存分配，动作块资产变为 100% 不可变只读配置，支持任意多角色高并发安全复用。
   - 运行时临时数据全部移入 `IMontageBlockState`，通过泛型基类 `MontageActionBlockBase<TState>` 提供强类型防装箱契约，核心调度对具体表现完全无感知（OCP）。
7. **类型化状态池与角色微型 Player 池 (Typed & Per-Coordinator Pool)**
   - 内置 `MontageBlockStatePool`，以 `Type` 为单元栈式隔离复用状态实例，彻底根除角色在不同技能交替释放时的类型抖动 GC 堆分配。
   - 角色级维护容量为 2~3 的微型 `MontagePlayer` 池，原地启动复用；严格保证在多通道姿态完全淡出衰减归零后才回池，杜绝打断姿态突变硬切。
8. **预分配双插槽 (Double-Buffered Slots) 平滑过渡**
   - 每个图层预分配两个 Slot 处理 CrossFade，运行时无需频繁增删 Playable 节点，消除拓扑重建卡顿且保证 0 GC 分配。
9. **轻量代际安全句柄 (MontageHandle)**
   - 16 字节值类型（`readonly struct`），纯栈分配，0 GC 开销。
   - 封装代际版本号（Generation ID），动画结束或槽位复用后旧句柄自动失效，杜绝野指针与串号误操作。
10. **Root Motion 委托派发**
    - 支持水平、垂直与旋转分量独立过滤，将位移增量分发给 `CharacterController` 等物理组件，不直接修改 Transform，避免穿墙或物理失效。
11. **现代 UI Toolkit 可视化时间轴编辑器**
    - 支持多轨道拖拽、动画片段调速（Time Stretch）与修剪（Trim）、多级磁吸对齐（吸附 0 点/播放头/分段点/邻近块）、跨资产复制粘贴。
    - 内置基于 `PreviewRenderUtility` 的独立 3D 视口，时间轴拖拽时粒子系统支持绝对时间切片预览（`ps.Simulate`）。

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
        // 1. 启动蒙太奇播放，获取轻量安全句柄 (MontageHandle，值类型，0 GC)
        MontageHandle handle = _coordinator.Play(_swordAttackMontage);

        // 2. 结合玩法数值，动态调整各分段的实际播放时长
        if (handle.IsValid)
        {
            handle.SyncSectionDuration(0, windupDuration);    // 动态调整前摇时长
            handle.SyncSectionDuration(1, activeDuration);    // 动态调整判定段时长
            handle.SyncSectionDuration(2, recoveryDuration);  // 动态调整收招后摇时长
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

## 演示场景 (Demo Showcase)

本插件内置开箱即用的完整战斗动作演示工程：
- **场景路径**：`Assets/CwcPlugins/CwcMontage/Demo/Scenes/MontageDemoScene.unity`。
- **开箱即用**：导入插件后可直接双击打开该场景体验，无需额外解压或导入。
- **操作方式**：
  - **1 - 9**：切换播放各类招式（三连斩、翻滚、连拳等）。
  - **Space**：暂停 / 恢复当前播放。
  - **Tab**：强制跳转至下一个物理分段。
  - **Q / E / R**：慢放 (0.5x) / 正常 (1.0x) / 加速 (1.5x)。
  - 界面左上角配备实时 Playables 监控面板，可观察混音槽权重与分段进度。
- **完全解耦**：演示模块完全自包含，拥有独立的 `.asmdef` 程序集，核心 `Runtime` 与 `Editor` 模块零反向依赖。

---

## 核心架构与类职责

| 类名 | 命名空间 | 职责定位 |
| :--- | :--- | :--- |
| `MontageSequenceSO` | `Cwcbb.Tools.CwcMontage` | 蒙太奇配置资产 (ScriptableObject)，持有动画、曲线、物理分段与轨道数据 |
| `MontageActionBlockBase` | `Cwcbb.Tools.CwcMontage` | 自定义视听动作块的抽象基类 |
| `MontageHandle` | `Cwcbb.Tools.CwcMontage` | 智能结构体句柄（带代际校验与悬挂检测，零 GC 驱动与查询） |
| `MontagePlayer` | `Cwcbb.Tools.CwcMontage` | 纯 C# 运行时播放器，执行增量时间采样、时钟对齐与分段状态推进 |
| `MontageCoordinator` | `Cwcbb.Tools.CwcMontage` | 挂载在角色上的 MonoBehaviour，管理 Playables 混音图与 Root Motion 广播 |
| `MontageSpineDecoupleUtility` | `Cwcbb.Tools.CwcMontage` | 轻量纯骨骼 Dummy 瞬态采样与四元数逆变换工具，负责上半身腰部实时解耦旋转计算 |
| `IMontageRootMotionReceiver` | `Cwcbb.Tools.CwcMontage` | 根运动接收者接口，解耦外部物理移动系统 |

---

## 版本演进与开发路线图 (Roadmap)

`CwcMontage` 严格定位为**高品质纯表现层动作蒙太奇工具**，专注于将动画采样、分层混音、视听特效协同与时间轴编辑体验做到极致：

- [x] **v1.0.0 (核心引擎建立)**
  - Playables 混音图与双缓冲 Ping-Pong 拓扑结构
  - 区间扫掠无漏帧算法（保证极端低帧率下事件成对触发）
  - 去语义化物理分段与自适应时钟缩放 (Adaptive Time Warping)
  - 交互式时间轴编辑器与 Scene 视口洗牌实时采样
  - 委托化 Root Motion 分发
- [x] **v1.1.0 (多层级与解耦混音重大升级 - 当前版本)**
  - 多层级动画轨道：全身 (Full Body)、上半身 (Upper Body)、叠加 (Additive)
  - 上半身腰部实时解耦旋转算法（四元数逆变换，告别 DCC 魔改，直接适配通用 Humanoid 动作）
  - 编辑器底层移动（Locomotion）循环模拟预览
  - 片段级独立平滑过渡（Crossfade）与打断权重自洽插值
- [ ] **v1.2.0 (表现力与编辑交互增强 - 计划中)**
  - 曲线参数驱动轨道 (Curve Parameter Track)：支持通过自定义 `AnimationCurve` 连续驱动材质 Shader Float、后处理 Exposure、音量渐变等
  - 时间轴快捷多选与吸附对齐：框选多个 ActionBlock 整体拖动、吸附关键帧与批量缩放
  - Unity 6 LTS (6000.x) 深度适配与 UI Toolkit 编辑器滚动性能调优
- [ ] **未来优化方向 (Ongoing)**
  - 多遮罩与精细骨骼过滤 (Custom AvatarMask) 拓展配置
  - 音频波形 (Audio Waveform) 时间轴直观渲染展示
  - 轻量标记点 (Event Markers / Tags) 系统

---

## 许可协议与第三方资产许可

- 本项目核心源码采用 [Cwc Tools Public License (Source-Available)](LICENSE) 许可：
  - **游戏作品发布（End Products）**：允许个人及商业游戏项目免费集成使用并发布商用，免版税（Royalty-Free）。
  - **二次分发限制（No Redistribution as Tools）**：严禁以任何形式将本插件本体或修改版本作为独立开发工具、SDK、资产包或竞品插件进行二次分发、公开镜像或转售。
- 内置演示角色模型与动画源自 [Quaternius](https://quaternius.com) 的 Universal Animation Library，遵循 **CC0 1.0 Universal (Public Domain Dedication)** 协议，允许无限制商业使用与二次分发。
- 完整第三方声明详见 [Third-Party Notices.txt](Third-Party%20Notices.txt)。
