# 版本演进与开发路线图 (Roadmap)

`CwcMontage` 严格坚持**纯表现层双轨分离（Dual-Track Pipeline）**设计哲学，专注于动作动画采样、分层混音、视听特效协同与时间轴编辑体验的极致打磨。

所有核心运行时引擎与编辑器套件保持 **永久源码开放，并支持游戏作品商业化免费集成（免版税）**。

---

## 里程碑与演进路线

### v1.0.0 (已发布 / Released)
- [x] **Playables 核心混音引擎**：固定双缓冲槽 Ping-Pong CrossFade 拓扑结构，彻底避免动态增删节点与 GC 开销。
- [x] **区间扫掠无漏帧算法 (Interval Sweep Sampling)**：增量半开区间 `(LastTime, CurrentTime]` 判定，彻底杜绝极端低帧率掉帧导致的跳帧漏事件。
- [x] **去语义化物理分段 (Native Physical Sections)**：客观几何时间切分，$O(1)$ 零 GC 分段时长查询。
- [x] **分段自适应时钟缩放 (Adaptive Time Warping)**：通过句柄 `SyncSectionDuration` 动态缩放 Playable 速率，无缝对齐策划数值。
- [x] **交互式时间轴编辑器**：Scene 视口洗牌（Scrubbing）实时采样、视听动作块拖拽对齐与零运行时开销预览。
- [x] **委托化 Root Motion 分发**：解耦水平/垂直/旋转增量，零污染角色控制器。

### v1.1.0 (已发布 / Current Version)
- [x] **多轨道与分层混音架构 (Multi-Track Pipeline)**：
  - **全身轨道 (Full Body Track)**：主导强霸体攻击、大招与完全覆盖全身的动作。
  - **上半身轨道 (Upper Body Track)**：支持移动施法、走打与瞄准姿态混合。
  - **叠加动画轨道 (Additive Track)**：实时叠加受击抖动、开火后坐力或微动细节。
- [x] **上半身腰部实时解耦旋转算法 (Spine Decoupled Root Rotation)**：
  - 攻克移动中上半身出招姿态被骨盆摇摆前倾污染的业界痛点。
  - 告别传统方案对 DCC 专用腰部骨骼的破坏性魔改，利用轻量 Dummy 骨架瞬态采样与四元数逆变换，现成通用 Humanoid 动作 100% 零修改即插即用。
- [x] **编辑器底层移动循环模拟 (Locomotion & Controller Preview)**：
  - 在时间轴编辑器中直接设置待机/跑步等底层动画循环，所见即所得调试移动出招手感与混合形态。
- [x] **片段级独立平滑过渡与打断锁存**：
  - 连续衔接动作自动跳过边缘衰减，解决多片段连续播放的权重塌陷；
  - 任意时刻打断播放基于内部自洽时钟锁存插值，彻底消除生硬抽帧切回。

### v1.2.0 (演进中 / Planned)
- [ ] **曲线参数驱动轨道 (Curve Parameter Track)**：
  - 支持通过自定义 `AnimationCurve` 随时间轴连续驱动材质 Shader Float 属性、后处理 Exposure、音量渐变或光源强度，进一步丰富表现层扩展。
- [ ] **时间轴快捷操作与多选编辑**：
  - 支持在时间轴内框选多个 ActionBlock 整体拖动、吸附关键帧与批量比例缩放。
- [ ] **Unity 6 LTS (6000.x) 深度适配**：
  - 针对 Unity 6 的 UI Toolkit 进行全深度渲染调优，提升复杂海量多轨道下的界面滚动帧率。

### 后续规划方向 (Future Considerations)
- **多遮罩与精细骨骼过滤 (Custom AvatarMask)**：支持除标准上半身外更细粒度的局部骨骼遮罩配置。
- **音频波形时间轴视图 (Audio Waveform Display)**：在音频轨道动作块上直接绘制音频波形纹理，辅助毫秒级对齐动作打击点。
- **轻量标记点 (Event Markers / Tags)**：支持时间轴内放置轻量标签，方便快速对齐时间节点。

---

## 社区反馈与贡献

如果你在实际项目中有新的表现层需求或发现任何问题，欢迎通过 [GitHub Issues](https://github.com/CwcbbChao/CwcMontage/issues) 或 Pull Requests 进行交流！
