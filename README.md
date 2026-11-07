# CwcMontage - High-Performance Playables Animation Montage System

[![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-blue.svg)](https://unity.com/)
[![License](https://img.shields.io/badge/License-Custom%20(Free%20for%20Games)-blue.svg)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/CwcbbChao/CwcMontage/pulls)
[![Docs](https://img.shields.io/badge/Documentation-Online-brightgreen.svg)](https://cwcbbchao.github.io/CwcMontage/)

> **Official Online Documentation**: [https://cwcbbchao.github.io/CwcMontage/](https://cwcbbchao.github.io/CwcMontage/)  
> Visit the online documentation for architectural breakdowns, API references, live GIFs, and advanced tutorials.

---

## Repository Structure

This repository is organized into a clean wrapper structure:

- **`.github/`**: GitHub Actions workflows (including automatic VitePress documentation deployment to GitHub Pages) and issue templates.
- **`docs/`**: VitePress documentation source code.
- **`LICENSE`**: Core source-available license governing this GitHub repository.
- **`CwcMontage/`**: The core Unity Package / Asset Store delivery directory. Contains Runtime, Editor, Demo, and Documentation.

---

## Overview

`CwcMontage` is a lightweight, high-performance, presentation-layer animation montage system tailored for modern action games (ACT / ARPG / Top-Down) in Unity.

Built natively on Unity's **Playables API**, it decouples the **animation presentation layer from gameplay combat logic**: external state machines or ability systems drive gameplay decisions and state transitions, while `CwcMontage` orchestrates precise multi-track timeline execution, smooth crossfading, section speed adjustments, and safe Root Motion delegation.

---

## Key Features

1. **Decoupled Presentation & Combat Logic**: Core runtime does not hardcode gameplay or skill rules.
2. **0-GC Runtime State Separation**: Sequences are 100% immutable read-only assets safe for multi-character concurrency.
3. **Pre-allocated Double-Buffered Slots**: Native ping-pong CrossFade transitions without dynamic graph reconstruction.
4. **Half-Open Interval Sweep**: Guarantees strictly paired OnEnter and OnExit triggers even across severe frame drops.
5. **Action Sections & Dynamic Speed Warping**: Slice clips into logical phases and dynamically scale section durations from C# code.
6. **Multi-Layer Blending**: Dedicated UpperBody, FullBody, and Additive layers for concurrent locomotion blending and impact flinches.
7. **Upper-Body Spine Decoupling**: Dynamically counter-rotates and blends the Spine local rotation to eliminate pelvis sway.
8. **Safe Root Motion Delegation**: Filter planar/vertical displacement and delegate to character controllers via `IMontageRootMotionReceiver`.

---

## License & Third-Party Notices

- The core source code in this repository is released under the [Cwc Tools Public License (Source-Available)](LICENSE):
  - **End Products (Commercial & Free Games)**: Free to integrate, compile, and distribute within interactive games and applications without royalty fees.
  - **Tool Redistribution Restrictions**: You may not redistribute, resell, or sublicense the Software as a standalone development tool, plugin, SDK, or asset pack.
- Demo 3D humanoid character models and animations are provided by [Quaternius](https://quaternius.com) under the **CC0 1.0 Universal (Public Domain Dedication)** license.
