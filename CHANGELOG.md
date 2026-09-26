# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-09-26

### Added
- **0-GC Runtime State Separation (0-GC 运行时状态解耦架构)**: Introduced `IMontageBlockState` and `MontageSpatialBlockState`, completely separating transient runtime mutable data from action block assets.
- **Anti-Boxing Generic Base Classes (强类型防装箱泛型基类)**: Added `MontageActionBlockBase<TState>` and `MontageSpatialActionBlockBase<TState>` with `where TState : class, IMontageBlockState, new()`, ensuring 100% immutable asset singletons and type-safe 0-GC dispatch.
- **Typed Block State Pool (类型化状态对象池)**: Implemented `MontageBlockStatePool` with per-`Type` stack caching, eliminating type thrashing GC allocations when characters alternate between different skill montages.
- **Baked Immutable Action Blocks (只读烘焙运行时列表)**: Added `MontageSequenceSO.BakedRuntimeActionBlocks` to cache sorted, active blocks from non-muted tracks, completely eliminating `blockData.Clone()` and `action.Clone()`.
- **Per-Coordinator Micro Player Pool (角色级微型播放器池)**: `MontageCoordinator` now maintains an on-demand micro player pool (capacity 2~3) per character with in-place reuse, eliminating per-play `MontagePlayer` allocations.

### Changed
- **Interruption Lifetime Protection**: Refactored `MontageCoordinator.CheckAndReleaseSlot` so players are only recycled after layer slot weights smoothly decay to 0 and all channel references are cleared, preserving C0/C1 dynamic continuity and preventing bind pose snaps upon interruption.
- **Auditing Built-in Blocks**: Refactored `VFXActionBlock`, `AudioActionBlock`, and `PrefabSpawnActionBlock` to inherit from generic bases and encapsulate runtime instances into reusable inner states (`VFXState`, `AudioState`, `PrefabSpawnState`).
- **Penetration Sweep Safeguard**: Preserved strict paired `OnEnter` -> `OnExit` execution for single-frame block penetration during frame drops in `MontagePlayer.SweepInterval`.

## [1.1.0] - 2026-09-25

### Added
- **Multi-Layer Channel Architecture**: Built-in 4-tier Playables mixing topology (Locomotion -> UpperBody -> FullBody -> Additive) with standard Humanoid UpperBody AvatarMask for out-of-the-box torso action playback while moving.
- **Upper-Body Spine Decoupling**: Dynamic procedural orientation decoupling tool (`MontageSpineDecoupleUtility`) preventing lower-body locomotion pelvic tilt/sway from contaminating upper-body attack strikes and aiming.
- **Independent Segment Blend In/Out**: Individual clip-level fade-in and fade-out envelope curves (`BlendInTime`, `BlendOutTime`, `BlendInCurve`, `BlendOutCurve`) allowing non-contiguous segments to blend smoothly across timeline gaps.
- **Built-in Standalone Dummy Prefab**: Added lightweight, pure-skeleton Humanoid dummy (`Dummy.prefab`) in `Runtime/Resources/` for safe, zero-allocation runtime decouple trajectory sampling.

### Changed
- **Segment Blending Continuity**: Refactored `MontageSequenceSO.EvaluateSegments` with sequence continuity analysis (`HasContinuousPreviousSegment`, `HasContinuousNextSegment`), preventing Crossfade overlaps and abutting clips from double-fading and eliminating layer weight dips.
- **Persistent Singleton Sampling Dummy**: Replaced runtime cloning of gameplay character entities with a global persistent pure-skeleton dummy, eliminating GC spikes and completely preventing business MonoBehaviour `Awake()`/`OnEnable()` side effects.
- **Layer Weight Isolation**: Decoupled `MontagePlayer.Stop()` blend-out latching from multi-layer slot weight updates via `CalculateTargetWeight()`, guaranteeing smooth decay and preventing hard cuts on interruption.
- **Accurate Handle Weight Query**: `MontageHandle.CurrentWeight` now retrieves the target layer slot's true evaluation weight instead of the global player weight.

## [1.0.0] - 2026-09-09

### Added
- **Core Playables Pipeline**: Pure presentation-layer animation montage player built entirely on Unity's Playables API.
- **Dual-Slot Ping-Pong Mixer**: Fixed two-slot Mixer topology for cross-fading, achieving lifetime PlayableGraph stability and zero runtime GC allocations.
- **Interval Sweep Sampling**: Dynamic `(LastTime, CurrentTime]` sweep-based event sampling, guaranteeing zero skipped events and paired `OnEnter/OnUpdate/OnExit` lifecycles even during extreme frame drops.
- **Native Physical Sections**: Objectively geometric split timestamps providing $O(1)$ zero-allocation section duration lookup and range detection.
- **Adaptive Time Warping**: External-driven section time scaling allowing seamless alignment between design numerical timing and visual animation lengths.
- **External Progress & Jump API**: Comprehensive handle-based API (`JumpToSection`, `JumpToTime`, `EvaluateSectionProgress`) enabling external ability systems to lead playback flow.
- **Root Motion Delegation**: Masked filtering for horizontal, vertical, and rotational delta components dispatched via `IMontageRootMotionReceiver` without intruding into character movement controllers.
- **Custom Action Blocks**: Extensible abstract base classes (`MontageActionBlockBase`, `MontageSpatialActionBlockBase`) with built-in Audio, VFX, and Prefab spawning blocks.
- **Montage Timeline Editor**: Interactive Unity Editor window featuring multi-track scrubbing, snap utilities, visual section markers, real-time Scene view preview, and track element inspectors.
- **Standalone Demo Showcase**: Self-contained humanoid combat demo with sample combo sequences, dynamic VFX generators, and real-time Playables monitor overlay HUD.
