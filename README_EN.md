# CwcMontage - High-Performance Playables Animation Montage System

[![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-blue.svg)](https://unity.com/)
[![License](https://img.shields.io/badge/License-Custom%20(Free%20for%20Games)-blue.svg)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/CwcbbChao/CwcMontage/pulls)
[![Docs](https://img.shields.io/badge/Documentation-Online-brightgreen.svg)](https://cwcbbchao.github.io/CwcMontage/)

**English** | [简体中文](README.md)

> **Official Online Documentation**: [https://cwcbbchao.github.io/CwcMontage/](https://cwcbbchao.github.io/CwcMontage/)  
> Visit the online documentation for deep architectural breakdowns, API references, live GIFs, and advanced tutorials.

---

## Overview

`CwcMontage` is a lightweight, high-performance, presentation-layer animation montage system tailored for modern action games (ACT / ARPG / Top-Down) in Unity.

Built natively on Unity's **Playables API**, it decouples the **animation presentation layer from gameplay combat logic**: external state machines or ability systems drive gameplay decisions and state transitions, while `CwcMontage` orchestrates precise multi-track timeline execution, smooth crossfading, section speed adjustments, and safe Root Motion delegation.

---

## Visual Showcase

### 1. Interactive Montage Timeline Editor
Precision scrubbing, real-time particle preview with deterministic simulation, multi-clip trimming, and section editing:

![Editor Overview](docs/public/images/editor_overview.gif)

### 2. Runtime Crossfading & Section Control
Pre-allocated dual-slot crossfade transitions, dynamic section speed adjustments, and guaranteed paired event execution:

![Runtime Demo](docs/public/images/runtime_demo.gif)

---

## Key Features

1. **Decoupled Presentation & Combat Logic**
   - The core runtime does not hardcode gameplay or skill rules, keeping presentation clean and modular.
   - Inherit from `MontageActionBlockBase` or `MontageSpatialActionBlockBase` to implement custom audio, VFX, freeze-frames, camera shakes, or hitboxes.
2. **Multi-Layer Mixer Topology (FullBody / UpperBody / Additive)**
   - Organized via Playables API with native support for **Full Body**, **Upper Body**, and **Additive** concurrent layers with independent weight blending.
   - Enables moving attacks, upper-body spellcasting while running, and compounding hit flinches or firearm recoil.
3. **Upper-Body Spine Orientation Compensation**
   - Solves the common engine pain point where upper-body aim/attack posture tilts and sways with lower-body hip movement.
   - Dynamically counter-rotates and blends the Spine local rotation at runtime, allowing generic Humanoid animations to maintain accurate aiming without modifying DCC skeleton rigs.
4. **Action Sections & Dynamic Speed Warping**
   - Slices animations by simple timestamps without hardcoded stage enums, allowing gameplay logic to map phases flexibly.
   - Dynamically adjust section durations via `handle.SyncSectionDuration(sectionIndex, targetDuration)` without re-exporting FBX clips.
   - Supports external progress driving (`handle.EvaluateSectionProgress`) for charging attacks and combo cancels.
5. **Half-Open Interval Sweep (Guaranteed Paired Events)**
   - Evaluates active blocks using `(LastTime, CurrentTime]` intervals. Even across severe frame drops, `OnEnter` and `OnExit` execute in strict pairs to prevent lingering effects.
6. **Pre-allocated Double-Buffered Slots (0 GC)**
   - Each layer pre-allocates two alternating slots for smooth CrossFading, avoiding runtime graph restructuring, stalls, and heap allocations.
7. **Generational Safety Handle (MontageHandle)**
   - 16-byte readonly struct allocated on the stack (0 GC).
   - Encapsulates a Generation ID; expired or recycled slots invalidate old handles automatically, eliminating dangling references.
8. **Safe Root Motion Delegation**
   - Dispatches filtered horizontal, vertical, and rotational delta components via `IMontageRootMotionReceiver` or C# events to external movement controllers (e.g., CharacterController) without mutating Transform directly.
9. **Modern UI Toolkit Timeline Editor**
   - Multi-track timeline with clip time-stretching, edge trimming, multi-tier magnetic snapping (snap to 0, playhead, sections, and clip edges), and cross-asset clipboard.
   - Isolated 3D viewport using `PreviewRenderUtility` with deterministic particle scrubbing (`ps.Simulate`).

---

## Installation

### Option A: Install via Unity Package Manager (Git URL)
1. In Unity, open `Window` -> `Package Manager`.
2. Click the `+` icon in the top-left -> select **Add package from git URL...**.
3. Paste:
   ```
   https://github.com/CwcbbChao/CwcMontage.git
   ```
4. Click **Add**.

### Option B: Install via OpenUPM
```bash
openupm add com.cwcbb.cwcmontage
```

---

## Quick Start

### 1. Play a Montage and Synchronize Section Timing
```csharp
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

public class HeroCombatController : MonoBehaviour
{
    [SerializeField] private MontageCoordinator _coordinator;
    [SerializeField] private MontageSequenceSO _swordAttackMontage;

    public void PerformAttack(float windupDuration, float activeDuration, float recoveryDuration)
    {
        // 1. Play montage and obtain a lightweight, generation-checked MontageHandle (16 bytes, zero GC)
        MontageHandle handle = _coordinator.Play(_swordAttackMontage);

        // 2. Synchronize section durations dynamically according to combat attributes
        if (handle.IsValid)
        {
            handle.SyncSectionDuration(0, windupDuration);    // Windup section
            handle.SyncSectionDuration(1, activeDuration);    // Active hitbox section
            handle.SyncSectionDuration(2, recoveryDuration);  // Recovery section
        }
    }
}
```

### 2. Create a Custom Action Block
```csharp
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

[MontageCategory("Combat")]
[MontageColor("#e74c3c")]
[MontageDisplayName("Hit Stop")]
public class HitStopActionBlock : MontageActionBlockBase
{
    [SerializeField] private float _timeScale = 0.05f;

    public override void OnEnter(in MontageActionContext context)
    {
        base.OnEnter(context);
        // Trigger hit freeze frame
    }

    public override void OnExit(in MontageActionContext context)
    {
        base.OnExit(context);
        // Restore time scale
    }
}
```

---

## Interactive Demo Scene

A complete combat demonstration scene is provided out of the box:
- **Scene Location**: `Assets/CwcPlugins/CwcMontage/Demo/Scenes/MontageDemoScene.unity`
- **Out of the Box**: Open the scene directly after importing the package—no extra unpacking required.
- **Controls**:
  - **1 - 9**: Switch attack combos, rolls, and punches.
  - **Space**: Pause / resume playback.
  - **Tab**: Jump to next physical section immediately.
  - **Q / E / R**: Slow (0.5x) / Normal (1.0x) / Fast (1.5x) time scale.
  - Top-left HUD displays live PlayableGraph ping-pong slot weights and section progress.
- **Full Decoupling**: The demo module is entirely self-contained with its own `.asmdef` assembly and zero reverse dependencies on `Runtime` or `Editor`.

---

## Core Architecture & Roles

| Class | Namespace | Responsibility |
| :--- | :--- | :--- |
| `MontageSequenceSO` | `Cwcbb.Tools.CwcMontage` | Montage configuration asset (ScriptableObject) holding animations, curves, physical sections, and track data |
| `MontageActionBlockBase` | `Cwcbb.Tools.CwcMontage` | Abstract base class for custom visual/audio action blocks |
| `MontageHandle` | `Cwcbb.Tools.CwcMontage` | Lightweight struct handle (with generation and dangling checks, zero GC driving and querying) |
| `MontagePlayer` | `Cwcbb.Tools.CwcMontage` | Pure C# runtime player executing incremental sweep sampling, clock alignment, and section state stepping |
| `MontageCoordinator` | `Cwcbb.Tools.CwcMontage` | MonoBehaviour on the character entity managing PlayableGraph mixer topology and Root Motion dispatching |
| `MontageSpineDecoupleUtility` | `Cwcbb.Tools.CwcMontage` | Lightweight Dummy skeleton transient sampler and quaternion inverse utility for real-time spine root rotation decoupling |
| `IMontageRootMotionReceiver` | `Cwcbb.Tools.CwcMontage` | Interface for receiving delegated Root Motion deltas without polluting external physics controllers |

---

## Evolution & Roadmap

`CwcMontage` is strictly positioned as a **high-fidelity, pure presentation-layer action montage system**, focusing relentlessly on animation blending, audio/VFX synchronization, and timeline authoring:

- [x] **v1.0.0 (Core Engine Foundations)**
  - Playables mixer graph with dual-slot ping-pong crossfade topology
  - Interval sweep sampling algorithm (ensuring strictly paired events even under severe frame drops)
  - Geometric physical sections with Adaptive Time Warping
  - Interactive Timeline Editor with real-time Scene view scrubbing
  - Delegated Root Motion distribution
- [x] **v1.1.0 (Multi-Track & Decoupled Blending - Current Version)**
  - Multi-track animation pipeline: Full Body, Upper Body, and Additive layers
  - Real-time Spine Decoupled Root Rotation algorithm (quaternion inverse math, zero DCC bone modifications, 100% plug-and-play for generic Humanoid assets)
  - Built-in Editor Locomotion simulation and preview
  - Independent clip-level crossfade and self-consistent latched exit weights
- [ ] **v1.2.0 (Expressiveness & Authoring UX - Planned)**
  - Curve Parameter Track: Continuously drive Shader float properties, post-processing exposures, and audio volume via custom `AnimationCurve`
  - Timeline multi-selection and snapping: Box-select multiple action blocks, keyframe snapping, and batch proportional scaling
  - Unity 6 LTS (6000.x) deep profiling and UI Toolkit timeline scroll optimization
- [ ] **Ongoing Enhancements**
  - Custom AvatarMask bone filtering configuration
  - Audio waveform rendering directly inside audio track blocks
  - Lightweight event marker and tag system

---

## License & Third-Party Notices

- The core source code is released under the [Cwc Tools Public License (Source-Available)](LICENSE):
  - **End Products (Commercial & Free Games)**: Free to integrate, compile, and distribute within interactive games and applications without royalty fees.
  - **Tool Redistribution Restrictions**: You may not redistribute, resell, or sublicense the Software as a standalone development tool, plugin, SDK, or asset pack.
- Demo 3D humanoid character models and animations are provided by [Quaternius](https://quaternius.com) under the **CC0 1.0 Universal (Public Domain Dedication)** license.
- Detailed third-party notices can be found in [Third-Party Notices.txt](Third-Party%20Notices.txt).
