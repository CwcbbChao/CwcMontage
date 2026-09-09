# 快速上手 (5分钟)

本指南将带你在 5 分钟内完成 `CwcMontage` 的安装、配置并播放你的第一个动作蒙太奇。

---

## 1. 安装插件

### 方式一：Unity Package Manager (推荐)
1. 打开 Unity 编辑器菜单：`Window` -> `Package Manager`。
2. 点击左上角的 `+` 按钮，选择 **Add package from git URL...**。
3. 输入 Git 地址：
   ```
   https://github.com/CwcbbChao/CwcMontage.git
   ```
4. 点击 **Add** 确认。Unity 将自动下载并加载包。

### 方式二：OpenUPM CLI
```bash
openupm add com.cwcbb.cwcmontage
```

---

## 2. 角色准备：挂载驱动组件

在你的角色 GameObject（挂载了 `Animator` 的对象）上，添加 `MontageCoordinator` 组件：

1. 选中角色对象。
2. 点击 Inspector 下方的 **Add Component**。
3. 搜索并添加 `MontageCoordinator`。
4. 组件将自动绑定当层的 `Animator`。如果角色是 Humanoid 骨骼，系统将自动识别骨骼层级。

---

## 3. 创建第一个蒙太奇资产 (MontageSequenceSO)

1. 在 Unity Project 窗口中，右键点击任意文件夹：
   - 选择 `Create` -> `Cwcbb` -> `Montage` -> `Montage Sequence`。
2. 将新建的资产命名为 `Attack_Slash`。
3. 选中该资产，在 Inspector 中点击 **Open Montage Editor** 打开专用的时间轴编辑器窗口。

---

## 4. 编辑器配置：动画与动作块

1. **指定基础动画**：在 Montage Editor 顶部或 Inspector 的 `Animation Clip` 槽位拖入你的攻击动画片段（例如 `Sword_Slash.anim`）。
2. **切分物理分段**：
   - 将播放指针拖动到前摇结束处，按 **Ctrl + B** 或点击添加切分标记。
   - 此时时间轴将被分割为 `Section 0`（前摇）、`Section 1`（判定执行）和 `Section 2`（收招后摇）。
3. **添加表现轨道**：
   - 点击 **Add Track** -> 选择 **Audio Track** 或 **VFX Track**。
   - 在轨道上双击创建 ActionBlock，将其拖曳对齐到挥刀的最快瞬间。
   - 选中该 ActionBlock，在右侧面板为其指定挥刀音效 (`AudioClip`) 或刀光预制体 (`Prefab`)。

---

## 5. 编写播放代码

创建一个简单的测试脚本 `PlayerAttackTest.cs`：

```csharp
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

public class PlayerAttackTest : MonoBehaviour
{
    [SerializeField] private MontageCoordinator _coordinator;
    [SerializeField] private MontageSequenceSO _attackMontage;

    private void Update()
    {
        // 按下 J 键触发攻击
        if (Input.GetKeyDown(KeyCode.J))
        {
            TriggerAttack();
        }
    }

    private void TriggerAttack()
    {
        // 播放蒙太奇并返回轻量句柄
        MontageHandle handle = _coordinator.Play(_attackMontage, crossFadeDuration: 0.15f);

        if (handle.IsValid)
        {
            // 可选：将第 0 分段（前摇）自适应缩放到 0.2 秒
            handle.SyncSectionDuration(0, 0.2f);

            // 监听蒙太奇播放结束
            handle.OnCompleted += () =>
            {
                Debug.Log("攻击动作播放完毕");
            };
        }
    }
}
```

将脚本挂载到场景中，拖入 `_coordinator` 与 `_attackMontage`，运行游戏按下 `J` 键即可查看流畅的动作播放与视听同步！
