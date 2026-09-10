# 路线图与 Pro 版规划

`CwcMontage` 遵循 **Open-Core**（核心开源 + 高级特性扩展）的发展战略。

核心运行时引擎（包括 Playables 混音、区间扫掠无漏帧算法、物理分段自适应时钟与时间轴编辑器）保持 **永久源码开放，并支持游戏商业化免费集成（免版税）**。进阶特化功能与企业级工业化扩展将在后续作为独立扩展包或 Pro 版提供。

---

## 社区开源版演进路线 (Community Roadmap)

### v1.1.0 (计划中)
- [ ] **曲线参数轨道 (Curve Parameter Track)**：支持通过自定义 AnimationCurve 随时间轴连续驱动材质 Shader 属性、后处理曝光度或音量渐变。
- [ ] **时间轴快捷操作与多选编辑**：支持框选多个 ActionBlock 整体移动、对齐与缩放。
- [ ] **OpenUPM 自动发版集成**：配置 GitHub Release 自动触发 OpenUPM 索引更新。

### v1.2.0 (规划中)
- [ ] **Unity 6 完全兼容与 UIElements 渲染加速**：针对 Unity 6000.x 的 UI Toolkit 进行全深度优化，提升在复杂成百上千轨道下的滚动流畅度。
- [ ] **音效波形预览图 (Audio Waveform Display)**：在 Audio Track 的 ActionBlock 背景上直接绘制音频波形纹理，辅助毫秒级对齐动作打击点。

---

## 商业化 Pro 版进阶特性展望 (Pro Edition Preview)

未来面向高要求商业项目，将规划推出包含以下工业级特性的 Pro 版：

```
+-------------------------------------------------------------+
|                     CwcMontage Pro 核心矩阵                  |
+-------------------------------------------------------------+
|  [连招状态图 (Combo Node Graph)]  : 可视化连招判定树与派生分支  |
|  [网络回滚适配 (Rollback Sync)]   : 预测帧重演与时间回退补偿   |
|  [受击布娃娃混合 (Hit & Ragdoll)] : 局部物理碰撞与受击停顿矩阵 |
|  [多角色协同蒙太奇 (Sync Montage)]: 处决技与双人 QTE 姿态对齐 |
+-------------------------------------------------------------+
```

1. **可视化连招状态图 (Combo Node Graph)**：
   - 基于 NodeGraphProcessor 打造的连招编辑器。
   - 可视化连线配置轻击、重击、蓄力派生、浮空连击的条件转移与取消窗口（Cancel Window），逻辑层与蒙太奇无缝双向绑定。
2. **网络预测与回滚同步适配器 (Network Rollback & Prediction)**：
   - 针对 GGPO / Lockstep / Server-Authoritative 架构设计。
   - 彻底解耦表现层内部状态快照，支持任意帧的时间回溯、重模拟与姿态快照平滑重对齐。
3. **高级受击与物理布娃娃混合 (Dynamic Hit Reaction & Ragdoll)**：
   - 局部受击物理反冲（Hit Reaction Profile）。
   - 从动力学动画到物理 Ragdoll 的平滑混入与受身起立无缝衔接。
4. **多角色协同蒙太奇 (Synchronized Co-op Montage)**：
   - 专用于动作游戏中的“背刺/处决/投技/双人处决”。
   - 双角色空间相对位姿锚定与时间轴绝对帧对齐。

---

## 商业赞助与定制咨询

如果你对现有开源功能有改进建议，或希望对特定的项目架构进行定制化扩展，欢迎通过 GitHub Issues 提交提议。
