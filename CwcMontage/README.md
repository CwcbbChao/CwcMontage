# CwcMontage - Playables Animation Montage System

[![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-blue.svg)](https://unity.com/)
[![License](https://img.shields.io/badge/License-Unity%20Asset%20Store%20EULA-blue.svg)](https://unity.com/legal/as-terms)
[![Documentation](https://img.shields.io/badge/Documentation-PDF-brightgreen.svg)](Documentation/User_Manual.pdf)

`CwcMontage` is a lightweight, high-performance presentation-layer animation montage system for modern action games (ACT / ARPG / Top-Down) in Unity.

Built natively on Unity's **Playables API**, it decouples the animation presentation layer from gameplay combat logic: your state machine or ability system decides what happens, while `CwcMontage` handles multi-track timeline execution, crossfading, section speed changes and root motion delegation.

---

## Overview

| | |
| :--- | :--- |
| **Animation layers** | Locomotion, Upper Body, Full Body, Additive |
| **Sections** | Geometric split points with runtime duration warping |
| **Blending** | Per-segment blend-in / blend-out curves, dual-slot crossfade |
| **Events** | Audio, VFX and prefab action blocks on action tracks |
| **Root motion** | Filtered deltas delegated through `IMontageRootMotionReceiver` |
| **Editor** | UI Toolkit timeline with 3D preview and clipboard |

---

## Key Features

1. **Decoupled presentation and combat logic**
   The runtime contains no combat rules. Derive from `MontageActionBlockBase` or `MontageSpatialActionBlockBase` to add custom audio, VFX, hit-stops, camera shakes or hitboxes.

2. **Multi-layer mixer topology**
   Full Body, Upper Body and Additive layers run concurrently with independent weights, so a character can cast while running or flinch during an attack.

3. **Upper-body spine decoupling**
   Counter-rotates the spine against pelvis sway, so generic humanoid animations keep aiming straight without editing the rig.

4. **Sections and time warping**
   Split a montage with timestamps, then stretch or compress a section at runtime with `handle.SyncSectionDuration(...)` without re-exporting clips.

5. **Guaranteed paired events**
   Action blocks are sampled over half-open `(LastTime, CurrentTime]` intervals, so `OnEnter` and `OnExit` stay paired even when frames are dropped.

6. **Pooled runtime state**
   Mutable per-character state lives in `IMontageBlockState` objects served by `MontageBlockStatePool`; montage assets stay immutable and shareable.

7. **Pre-allocated slots**
   Each layer keeps two alternating mixer slots, so crossfades never rebuild the graph.

8. **Generational handles**
   `MontageHandle` is a 16-byte readonly struct. Expired handles fail their validity check instead of touching recycled slots.

9. **Root motion delegation**
   Horizontal, vertical and rotational deltas are filtered per asset and forwarded to your movement controller.

10. **UI Toolkit timeline editor**
    Multi-track timeline with trimming, magnetic snapping, section markers, cross-asset clipboard and a 3D preview that also scrubs particles.

---

## Installation

### Option A: Import from the Asset Store

1. Open **Window > Package Manager** and select **My Assets**.
2. Find **CwcMontage** in the list and click **Download**, then **Import**.

### Option B: Copy the folder

Copy the `CwcMontage` folder into your project's `Assets` directory, for example `Assets/CwcPlugins/CwcMontage`.

Full instructions, requirements and troubleshooting are in [Documentation/User_Manual.pdf](Documentation/User_Manual.pdf).

---

## Quick Start

### Play a montage and synchronise section timing

```csharp
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

public class HeroCombatController : MonoBehaviour
{
    [SerializeField] private MontageController _controller;
    [SerializeField] private MontageSequenceSO _swordAttackMontage;

    public void PerformAttack(float windupDuration, float activeDuration, float recoveryDuration)
    {
        // 1. Start the montage and receive a generation-checked handle.
        MontageHandle handle = _controller.Play(_swordAttackMontage);

        // 2. Fit the sections to the values required by gameplay.
        if (handle.IsValid)
        {
            handle.SyncSectionDuration(0, windupDuration);    // Windup
            handle.SyncSectionDuration(1, activeDuration);    // Active hitbox
            handle.SyncSectionDuration(2, recoveryDuration);  // Recovery
        }
    }
}
```

### Create a custom action block

```csharp
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

[MontageCategory("Combat")]
[MontageColor("#E74C3C")]
[MontageDisplayName("Hit Stop")]
public class HitStopActionBlock : MontageActionBlockBase
{
    [SerializeField] private float _timeScale = 0.05f;

    public override void OnEnter(in MontageActionContext context)
    {
        base.OnEnter(context);
        // Freeze the frame.
    }

    public override void OnExit(in MontageActionContext context)
    {
        base.OnExit(context);
        // Restore the time scale.
    }
}
```

---

## Demo Scene

`Demo/Scenes/MontageDemoScene.unity` is ready to play after import.

| Input | Action |
| :--- | :--- |
| `1` - `9` | Play a sample montage |
| `Space` | Pause / resume |
| `S` | Smooth stop |
| `Tab` | Jump to the next section |
| `Q` / `E` / `R` | Fast (2.0x) / slow (0.5x) / normal (1.0x) |

The overlay shows the active layer, slot, weight, section and progress. The demo is self-contained: its assembly does not depend on your project code.

> The demo particle materials use URP shaders. In a Built-in Render Pipeline project they render magenta; install URP or replace the two demo materials to view the demo there. The runtime package itself is render-pipeline agnostic.

---

## Core Architecture

| Class | Responsibility |
| :--- | :--- |
| `MontageSequenceSO` | ScriptableObject asset: segments, sections, tracks, blends, root motion filters |
| `MontageController` | MonoBehaviour that owns the PlayableGraph, layers and root motion dispatch |
| `MontagePlayer` | Pure C# playback clock, section stepping and interval sweep |
| `MontageHandle` | Readonly struct for controlling and querying one playback instance |
| `MontageActionBlockBase` | Base class for custom timeline events (typed state variants available) |
| `MontageSpineDecoupleUtility` | Samples a dummy skeleton to counter-rotate the spine |
| `IMontageRootMotionReceiver` | Interface for receiving root motion deltas |

---

## Roadmap

- [x] **1.0.0** - Playables core, dual-slot crossfade, sections, root motion, timeline editor
- [x] **1.1.0** - Multi-layer channels, spine decoupling, per-segment blend envelopes
- [x] **1.2.0** - Pooled block state, typed state pools, baked action blocks
- [x] **1.2.1** - Virtual hooks for subclassing and third-party Playables integration
- [x] **1.2.2** - Asset preview overrides, custom upper-body masks, skeleton mismatch warnings
- [ ] **1.3.0** - Curve parameter track, multi-selection and batch scaling, Unity 6 profiling

---

## License

This package and all of its contents are distributed under the [Unity Asset Store End User License Agreement](https://unity.com/legal/as-terms).

The demo character and animations use the Universal Animation Library by Quaternius (CC0 1.0).
