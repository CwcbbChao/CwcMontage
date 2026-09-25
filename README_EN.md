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

`CwcMontage` is a lightweight, high-performance, pure presentation-layer animation montage system tailored for modern action games (ACT / ARPG / Top-Down) in Unity.

Built natively upon Unity's **Playables API**, it adheres strictly to the **Dual-Track Pipeline** philosophy: external gameplay logic (e.g. State Machines, Gameplay Ability Systems) retains authoritative control over timing and gameplay state transitions, while `CwcMontage` orchestrates precise animation sampling, smooth cross-fading, multi-track audio/VFX dispatching, and Root Motion delegation.

---

## Visual Showcase

### 1. Interactive Montage Timeline Editor
Scrub sampling in real-time within the Scene View, zero-allocation preview, and intuitive physical section slicing:

![Editor Overview](docs/public/images/editor_overview.gif)

### 2. Runtime Cross-Fading & Section Control
Dual-slot ping-pong cross-fade mixer, adaptive time warping, and guaranteed paired event execution:

![Runtime Demo](docs/public/images/runtime_demo.gif)

---

## Key Features

1. **Pure Presentation Layer & Open-Closed Principle (OCP)**
   - The core runtime does not hardcode any gameplay or audio/VFX behaviors.
   - Extend `MontageActionBlockBase` or `MontageSpatialActionBlockBase` to create custom hitboxes, freeze frames, audio clips, particle spawners, or camera shakes.
2. **Multi-Track & Additive Pipeline (v1.1.0)**
   - Native support for **Full Body**, **Upper Body**, and **Additive** concurrent tracks with independent mixer weights.
   - Enables moving attacks, casting while running, and smoothly compounding hit flinches or firearm recoil over ongoing animations.
3. **Real-time Spine Decoupled Root Rotation (v1.1.0)**
   - Eliminates the industry pain point where upper-body casting is tilted by lower-body hip swaying/leaning.
   - Requires zero DCC bone modifications. Utilizing a lightweight Dummy skeleton transient sampler and quaternion inverse transformations, any generic Humanoid asset works out of the box with 100% plug-and-play compatibility.
4. **Editor Locomotion & Controller Preview (v1.1.0)**
   - Built-in idle/run locomotion loop simulation directly inside the Timeline Editor, allowing WYSIWYG tuning of moving combat feel in the Scene View without entering Play mode.
5. **Independent Clip Fade & Latched Stop (v1.1.0)**
   - Clip-level independent crossfade that automatically bypasses edge fade for seamless continuous stitching;
   - Interrupting playback latches internally computed self-consistent weights to guarantee smooth exit transitions without frame snapping.
6. **Interval Sweep Sampling**
   - Employs incremental half-open intervals `(LastTime, CurrentTime]` to evaluate active spans, eliminating missed events during severe frame-rate drops.
   - Guarantees strict `OnEnter -> OnUpdate -> OnExit` lifecycle pairing with native support for scrubbing and seeking.
7. **Native Geometric Physical Sections**
   - Objectively geometric timestamp slicing without arbitrary semantic coupling (e.g. startup / active / recovery).
   - $O(1)$ zero-allocation section duration queries and range testing.
8. **Adaptive Time Warping**
   - Drive target section durations dynamically via `handle.SyncSectionDuration(sectionIndex, targetDuration)`. The engine automatically scales the Playable playback rate to harmonize animation visual assets with design timing.
9. **Fixed Dual-Slot Ping-Pong Mixer Topology**
   - Features a permanent 2-slot cross-fade mixer graph. Eliminates dynamic node reconnections and array shifting for lifelong PlayableGraph stability and zero runtime GC allocations.
10. **Root Motion Delegation**
    - Dispatches horizontal, vertical, and rotational delta components via `IMontageRootMotionReceiver` or C# events, avoiding intrusive dependencies on existing movement controllers (e.g., CharacterController, KCC).

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
