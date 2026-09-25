# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
