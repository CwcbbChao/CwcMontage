# 自定义 ActionBlock 扩展教程

`CwcMontage` 遵循面向对象的开闭原则（Open-Closed Principle）：核心播放内核完全不需要了解具体的业务表现细节。所有的音效、粒子、相机震屏、受击盒激活均作为独立的 **ActionBlock** 实现。

你可以零侵入核心源码，派生创建符合你项目专属需求的动作块。

---

## ActionBlock 生命周期机制

每个动作块继承自 `MontageActionBlockBase`，其运行时生命周期受到内核**区间扫掠采样器**的严格保证：

```
时间推进 ---> [进入 Block] --------------> [区间内推进] --------------> [离开 Block]
                OnEnter()                   OnUpdate()                   OnExit()
```

- **`OnEnter(in MontageActionContext context)`**：播放时间进入该 Block 的有效区间时触发（保证当次播放只触发一次）。
- **`OnUpdate(in MontageActionContext context)`**：在 Block 时间范围内随帧更新推进。
- **`OnExit(in MontageActionContext context)`**：播放时间离开 Block 区间，或者蒙太奇被外部强行打断/提前终止时触发（执行关键清理，防泄漏）。
- **`OnSample(in MontageActionContext context)`**：在编辑器时间轴 Scrubbing 洗牌时执行的无副作用预览采样。

---

## 上下文参数 (MontageActionContext)

生命周期函数均接收一个只读结构体 `MontageActionContext`：
- `context.TargetObject`：执行蒙太奇的宿主 GameObject（即挂载了 `MontageCoordinator` 的角色）。
- `context.CurrentTime`：当前蒙太奇推进到的局部时间戳（秒）。
- `context.NormalizedProgress`：当前 Block 内部的归一化完成度 `[0.0, 1.0]`。
- `context.IsScrubbing`：布尔值，标识当前是处于编辑器拖拽预览模式还是运行时真实播放。

---

## 实战示例：实现一个相机震屏动作块 (CameraShakeBlock)

```csharp
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

// 配置编辑器特性外观
[MontageCategory("Visual")]
[MontageColor("#9b59b6")]
[MontageDisplayName("Camera Shake")]
public class CameraShakeBlock : MontageActionBlockBase
{
    [SerializeField] private float _intensity = 0.5f;
    [SerializeField] private float _frequency = 25.0f;

    public override void OnEnter(in MontageActionContext context)
    {
        base.OnEnter(context);
        
        // 运行时触发你的相机系统震动（例如 Cinemachine Impulse）
        if (!context.IsScrubbing)
        {
            // CinemachineImpulseSource.GenerateImpulse(_intensity);
            Debug.Log($"[CameraShakeBlock] 触发相机震动，强度: {_intensity}");
        }
    }

    public override void OnExit(in MontageActionContext context)
    {
        base.OnExit(context);
        // 清理或平滑停止震动
    }
}
```

---

## 空间型动作块 (MontageSpatialActionBlockBase)

如果你的表现需要绑定角色的特定骨骼（如右手武器插槽、左脚足底发射粒子）：
继承 `MontageSpatialActionBlockBase` 即可天然获得骨骼挂点识别与相对位姿计算：

```csharp
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

[MontageCategory("Combat")]
[MontageColor("#e67e22")]
[MontageDisplayName("Spawn Weapon Spark")]
public class WeaponSparkBlock : MontageSpatialActionBlockBase
{
    [SerializeField] private GameObject _sparkPrefab;

    public override void OnEnter(in MontageActionContext context)
    {
        base.OnEnter(context);

        // GetSocketTransform 自动根据你在 Inspector 配置的 SocketName/HumanoidBone 寻找对应挂点
        Transform socketTransform = GetSocketTransform(context.TargetObject);
        if (socketTransform != null && _sparkPrefab != null)
        {
            GameObject spark = Object.Instantiate(_sparkPrefab, socketTransform.position, socketTransform.rotation);
            Object.Destroy(spark, 1.0f);
        }
    }
}
```

编写完毕后，回到 Unity 编辑器中打开 Montage Editor，在添加 Track 的下拉列表中即可立即看到你自定义的分类与动作块！
