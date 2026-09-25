---
layout: home

hero:
  name: "CwcMontage"
  text: "高性能纯表现层动作蒙太奇系统"
  tagline: "专为 Unity 动作游戏打造的轻量级动画蒙太奇插件。基于 Playables API 构建，表现层与战斗逻辑彻底解耦，支持预分配双插槽平滑过渡与 Root Motion 安全派发。"
  image:
    src: /images/editor_overview.gif
    alt: CwcMontage Overview
  actions:
    - theme: brand
      text: 快速上手 →
      link: /guide/getting-started
    - theme: alt
      text: 核心架构解析
      link: /architecture/interval-sweep
    - theme: alt
      text: 查看 GitHub
      link: https://github.com/CwcbbChao/CwcMontage

features:
  - icon: ⚡
    title: 表现层与战斗逻辑解耦
    details: 核心系统专注于动画采样与视听打点，由外部状态机或技能系统驱动动作流转与判定，架构职责清晰。
  - icon: 🎯
    title: 半开区间扫掠防漏帧
    details: 采用 (LastTime, CurrentTime] 区间判定，低帧率或卡顿跨越事件块时保证 OnEnter 与 OnExit 严格成对触发，杜绝特效残留。
  - icon: 🔄
    title: 预分配双插槽平滑过渡
    details: 每个图层预分配两个 Slot 处理 CrossFade，运行时无需频繁增删 Playable 节点，无拓扑重建卡顿且保证 0 GC。
  - icon: ⏱️
    title: 动作分段与动态调速
    details: 资产仅划分时间区间，外部可通过 SyncSectionDuration 动态调整分段播放速率或以进度驱动蓄力，无需美术反复重导动画。
  - icon: 🏃
    title: Root Motion 委托派发
    details: 支持水平、垂直与旋转分量独立过滤，位移增量交由外部物理移动组件处理，避免直接修改 Transform 导致穿墙。
  - icon: 🛠️
    title: 可视化时间轴多轨编辑器
    details: 基于 UI Toolkit 开发，支持多轨道拖拽、动画片段调速与修剪、多级磁吸对齐，以及 3D 视口粒子绝对时间切片预览。
---
