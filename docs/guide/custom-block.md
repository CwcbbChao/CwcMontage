# 自定义 ActionBlock 扩展教程

`CwcMontage` 遵循面向对象的开闭原则（Open-Closed Principle）：核心播放内核完全不需要了解具体的业务表现细节。所有的音效、粒子、相机震屏、受击盒激活均作为独立的 **ActionBlock** 实现。

系统采用 **0-GC 纯只读配置与运行时状态外置架构**：资产类只保存静态只读配置，所有运行时的临时变量由专用的 State 类持有，由内核通过类型对象池自动复用，做到单次播放绝对 0-GC 且支持任意多角色并发播放。

---

## 核心基类与架构选型

在扩展动作块时，根据表现类型选择继承的泛型基类：

| 动作块基类 | 状态约束 | 适用场景 |
| :--- | :--- | :--- |
| `MontageActionBlockBase<TState>` | `where TState : class, IMontageBlockState, new()` | 纯逻辑表现（相机震屏、顿帧、音效、材质变色、后处理等） |
| `MontageSpatialActionBlockBase<TState>` | `where TState : MontageSpatialBlockState, new()` | 空间位置型表现（特效粒子、生成预制体模型、武器残影等） |

---

## 1. 运行时状态接口 (IMontageBlockState)

每个动作块的可变状态必须实现 `IMontageBlockState` 接口：

```csharp
namespace Cwcbb.Tools.CwcMontage
{
    public interface IMontageBlockState
    {
        /// <summary>
        /// 当离开动作块区间、动画被打断或回池时调用，将所有可变变量重置归零。
        /// </summary>
        void Reset();
    }
}
```

内核对象池 `MontageBlockStatePool` 会在动作块离开区间或动画结束时自动调用 `Reset()`，并将该状态实例安全保存在专属栈中，供下次播放复用。

---

## 2. 实战示例：实现相机震屏动作块 (CameraShakeBlock)

```csharp
using System;
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

// 1. 定义运行时状态类（随槽位对象池常驻复用，杜绝装箱拆箱）
public class CameraShakeState : IMontageBlockState
{
    public float ElapsedTime;

    public void Reset()
    {
        ElapsedTime = 0f;
    }
}

// 2. 继承强类型泛型基类（动作块自身保持 100% 只读配置单例）
[Serializable]
[MontageCategory("Visual")]
[MontageColor("#9b59b6")]
[MontageDisplayName("Camera Shake")]
public class CameraShakeBlock : MontageActionBlockBase<CameraShakeState>
{
    #region Inspector 只读配置

    [SerializeField] private float _intensity = 0.5f;
    [SerializeField] private float _frequency = 25.0f;

    #endregion

    #region 强类型生命周期

    protected override void OnEnter(in MontageActionContext context, CameraShakeState state)
    {
        state.ElapsedTime = 0f;
        // 触发相机系统初始震冲（例如 Cinemachine Impulse）
    }

    protected override void OnUpdate(in MontageActionContext context, CameraShakeState state, float deltaTime)
    {
        state.ElapsedTime += deltaTime;
        // 随帧采样震动衰减...
    }

    protected override void OnExit(in MontageActionContext context, CameraShakeState state)
    {
        // 离开区间或被强行打断时平滑重置相机
    }

    #endregion
}
```

---

## 3. 空间型动作块 (MontageSpatialActionBlockBase)

如果表现需要跟随人形骨骼挂点（如左手、武器、胸口）或在触发时保持世界坐标固定：
继承 `MontageSpatialActionBlockBase<TState>`，且状态继承自 `MontageSpatialBlockState`，系统会自动处理目标骨骼解析与世界坐标跟随矩阵换算，且杜绝挂载为子节点造成的角色非等比缩放畸变。

```csharp
using System;
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

public class SparkEffectBlock : MontageSpatialActionBlockBase<SparkEffectBlock.SparkState>
{
    public class SparkState : MontageSpatialBlockState
    {
        public GameObject SpawnedInstance;

        public override void Reset()
        {
            base.Reset();
            SpawnedInstance = null;
        }
    }

    [SerializeField] private GameObject _sparkPrefab;

    protected override void OnEnter(in MontageActionContext context, SparkState state)
    {
        if (_sparkPrefab == null) return;

        // 1. 基类无 GC 解析骨骼与世界坐标换算（结果存入 state）
        ResolveTargetBone(context, state);
        CalculateWorldTransform(state, out Vector3 worldPos, out Quaternion worldRot);
        Transform attachParent = GetSpawnParent(context, isPreview: false);

        // 2. 从内置对象池生成实例并暂存至 state
        state.SpawnedInstance = MontageObjectPool.Spawn(_sparkPrefab, worldPos, worldRot, attachParent);
    }

    protected override void OnUpdate(in MontageActionContext context, SparkState state, float deltaTime)
    {
        // 外部动态跟随骨骼或按配置保持世界原地不动
        UpdateSpatialTransform(state.SpawnedInstance, in context, state);
    }

    protected override void OnExit(in MontageActionContext context, SparkState state)
    {
        if (state.SpawnedInstance != null)
        {
            MontageObjectPool.Recycle(state.SpawnedInstance);
            state.SpawnedInstance = null;
        }
    }
}
```

---

## 4. 编辑器时间轴视口透明兼容

使用泛型基类 `MontageActionBlockBase<TState>` 编写的自定义动作块，在 Unity 编辑器中打开 Montage Editor 时间轴时：
- 右键点击轨道直接自动识别并列入新建菜单；
- 基类已自动内置编辑器视口专用预览状态，时间轴拖拽与播放头 Scrubbing 即刻生效，无需任何额外的编辑器适配代码。
