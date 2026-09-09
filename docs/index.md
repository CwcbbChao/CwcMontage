---
layout: home

hero:
  name: "CwcMontage"
  text: "高性能纯表现层动作蒙太奇系统"
  tagline: "基于 Unity Playables API 构建。逻辑与表现双轨分离，区间扫掠无漏帧，双缓冲平滑混音，原生支持 Root Motion 解耦。"
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
    title: 逻辑与表现双轨分离
    details: 核心运行时不绑定任何具体技能或数值逻辑，由外部系统全权主导状态流转，专注于极致的动作表现采样与视听调度。
  - icon: 🎯
    title: 区间扫掠无漏帧算法
    details: 采用增量半开区间 (LastTime, CurrentTime] 判定，在 10 FPS 极端丢帧卡顿下依然确保所有动作块成对触发，杜绝漏事件。
  - icon: 🔄
    title: 固定双缓冲槽 Ping-Pong 混音
    details: 摒弃动态销毁与数组重排，采用常驻双节点交叉淡化（CrossFade）混音拓扑，PlayableGraph 终身稳定且零 GC。
  - icon: ⏱️
    title: 去语义化分段与自适应时钟
    details: 资产仅做客观几何切分，外部逻辑调用 SyncSectionDuration 即可自适应缩放 Playable 速率，消除美术动作与数值时长的冲突。
  - icon: 🏃
    title: Root Motion 委托化解耦
    details: 掩码过滤位移与旋转分量，通过 IMontageRootMotionReceiver 委托抛出，零侵入现有物理角色移动控制器。
  - icon: 🛠️
    title: 可视化时间轴多轨编辑器
    details: 完整的 Unity 编辑器窗口，支持多轨道拖拽、视口实时洗牌（Scrubbing）采样预览、吸附对齐工具与零开销调试。
---
