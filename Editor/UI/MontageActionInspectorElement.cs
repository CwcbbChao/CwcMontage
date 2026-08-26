using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 独立动作块与资产属性检查器面板（MontageActionInspectorElement）。
    /// 将 Action 字段从 3D 视口彻底剥离，在侧边栏提供现代 Unity 风格的可折叠卡片属性检查体验。
    /// </summary>
    public class MontageActionInspectorElement : VisualElement
    {
        #region 私有字段

        private MontageSequenceSO _targetAsset;
        private SerializedObject _serializedAsset;
        private MontageActionBlockElement _currentSelectedBlock;

        private ScrollView _scrollView;
        private VisualElement _actionContainer;
        private VisualElement _assetSettingsContainer;
        private TabView _tabView;

        #endregion

        #region 公共事件

        public event Action OnDataModified;
        public event Action OnAnimationClipChanged;
        public event Action OnAssetSettingsModified;

        #endregion

        #region 构造方法

        public MontageActionInspectorElement()
        {
            AddToClassList("montage-inspector-sidebar");

            var header = new VisualElement();
            header.AddToClassList("montage-inspector-header");

            var title = new Label("Inspector");
            title.AddToClassList("montage-inspector-title");
            header.Add(title);
            Add(header);

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scrollView.AddToClassList("montage-inspector-body");
            Add(_scrollView);

            _tabView = new TabView();
            _scrollView.Add(_tabView);

            // Tab 1: 选中的动作块
            var actionTab = new Tab("Action Block");
            _actionContainer = new VisualElement();
            actionTab.Add(_actionContainer);
            _tabView.Add(actionTab);

            // Tab 2: 资产全局设置
            var assetTab = new Tab("Asset Settings");
            _assetSettingsContainer = new VisualElement();
            assetTab.Add(_assetSettingsContainer);
            _tabView.Add(assetTab);

            ShowEmptyActionHint();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 绑定当前编辑的蒙太奇资产。
        /// </summary>
        public void BindAsset(MontageSequenceSO asset, SerializedObject serializedAsset)
        {
            _targetAsset = asset;
            _serializedAsset = serializedAsset;
            _currentSelectedBlock = null;

            RebuildAssetSettingsTab();
            ShowEmptyActionHint();
        }

        /// <summary>
        /// 选中并展示指定 ActionBlock 的详细字段。
        /// </summary>
        public void InspectActionBlock(MontageActionBlockElement blockElement)
        {
            _currentSelectedBlock = blockElement;
            _actionContainer.Clear();

            if (blockElement?.Data == null || blockElement.Data.Action == null || _serializedAsset == null)
            {
                ShowEmptyActionHint();
                return;
            }

            _serializedAsset.Update();

            var data = blockElement.Data;
            var action = data.Action;
            string actionType = action.GetType().Name;

            // 1. 顶部操作 Header Banner
            var banner = new VisualElement();
            banner.AddToClassList("montage-inspector-banner");

            var typeLabel = new Label(actionType);
            typeLabel.AddToClassList("montage-inspector-banner-title");
            banner.Add(typeLabel);

            var editScriptBtn = new Button(blockElement.OpenScript) { text = "Edit Script" };
            editScriptBtn.AddToClassList("montage-toolbar-btn");
            editScriptBtn.tooltip = "Open action block source code in IDE (Z)";
            banner.Add(editScriptBtn);

            _actionContainer.Add(banner);

            // 2. Timing 可折叠卡片
            var timingFoldout = new Foldout { text = "Timing", value = true };
            timingFoldout.AddToClassList("montage-inspector-foldout");

            // 帧范围输入行
            var frameRow = new VisualElement();
            frameRow.AddToClassList("montage-inspector-row");

            var startFrameCol = new VisualElement();
            startFrameCol.AddToClassList("montage-inspector-col");
            var startFrameField = new IntegerField("Start Frame") { value = data.StartFrame };
            startFrameField.AddToClassList("montage-timing-field");
            startFrameCol.Add(startFrameField);
            frameRow.Add(startFrameCol);

            var endFrameCol = new VisualElement();
            endFrameCol.AddToClassList("montage-inspector-col");
            var endFrameField = new IntegerField("End Frame") { value = data.EndFrame };
            endFrameField.AddToClassList("montage-timing-field");
            endFrameCol.Add(endFrameField);
            frameRow.Add(endFrameCol);

            timingFoldout.Add(frameRow);

            // 秒数提示信息
            float frameRate = _targetAsset?.AnimationClip != null ? Mathf.Max(1f, _targetAsset.AnimationClip.frameRate) : 30f;
            var timeHint = new Label($"Time: {data.StartFrame / frameRate:F2}s - {data.EndFrame / frameRate:F2}s (Duration: {(data.EndFrame - data.StartFrame) / frameRate:F2}s / {data.EndFrame - data.StartFrame} frames)");
            timeHint.AddToClassList("montage-inspector-time-hint");
            timingFoldout.Add(timeHint);

            startFrameField.RegisterValueChangedCallback(evt =>
            {
                data.StartFrame = Mathf.Max(0, evt.newValue);
                data.StartTime = data.StartFrame / frameRate;
                timeHint.text = $"Time: {data.StartFrame / frameRate:F2}s - {data.EndFrame / frameRate:F2}s (Duration: {(data.EndFrame - data.StartFrame) / frameRate:F2}s / {data.EndFrame - data.StartFrame} frames)";
                blockElement.UpdateVisual();
                EditorUtility.SetDirty(_targetAsset);
                OnDataModified?.Invoke();
            });

            endFrameField.RegisterValueChangedCallback(evt =>
            {
                data.EndFrame = Mathf.Max(data.StartFrame + 1, evt.newValue);
                data.EndTime = data.EndFrame / frameRate;
                timeHint.text = $"Time: {data.StartFrame / frameRate:F2}s - {data.EndFrame / frameRate:F2}s (Duration: {(data.EndFrame - data.StartFrame) / frameRate:F2}s / {data.EndFrame - data.StartFrame} frames)";
                blockElement.UpdateVisual();
                EditorUtility.SetDirty(_targetAsset);
                OnDataModified?.Invoke();
            });

            _actionContainer.Add(timingFoldout);

            // 3. Action Parameters 可折叠卡片 (直接扁平展开子属性)
            var actionFoldout = new Foldout { text = "Action Parameters", value = true };
            actionFoldout.AddToClassList("montage-inspector-foldout");

            string propPath = $"_tracks.Array.data[{blockElement.TrackIndex}]._actionBlocks.Array.data[{blockElement.BlockIndex}]._action";
            var actionProp = _serializedAsset.FindProperty(propPath);

            if (actionProp != null)
            {
                var endProp = actionProp.GetEndProperty();
                var childProp = actionProp.Copy();
                bool enterChildren = true;

                while (childProp.NextVisible(enterChildren) && !SerializedProperty.EqualContents(childProp, endProp))
                {
                    enterChildren = false;
                    var field = new PropertyField(childProp);
                    actionFoldout.Add(field);

                    field.RegisterValueChangeCallback(evt =>
                    {
                        _serializedAsset.ApplyModifiedProperties();
                        EditorUtility.SetDirty(_targetAsset);
                        blockElement.UpdateVisual();
                        OnDataModified?.Invoke();
                    });
                }

                actionFoldout.Bind(_serializedAsset);
            }
            else
            {
                var errorLabel = new Label("Unable to bind action serialized property.");
                errorLabel.style.color = Color.red;
                actionFoldout.Add(errorLabel);
            }

            _actionContainer.Add(actionFoldout);

            _tabView.selectedTabIndex = 0; // 自动切换到 Action Tab
        }

        /// <summary>
        /// 清空选中的动作块检查器。
        /// </summary>
        public void ClearActionInspect()
        {
            _currentSelectedBlock = null;
            ShowEmptyActionHint();
        }

        #endregion

        #region 私有方法

        private void ShowEmptyActionHint()
        {
            _actionContainer.Clear();
            var hint = new Label("Select an Action Block on the timeline to inspect its parameters.");
            hint.AddToClassList("montage-empty-hint");
            _actionContainer.Add(hint);
        }

        private void RebuildAssetSettingsTab()
        {
            _assetSettingsContainer.Clear();
            if (_serializedAsset == null)
            {
                return;
            }

            _serializedAsset.Update();

            void BindSettingField(PropertyField pf)
            {
                pf.RegisterValueChangeCallback(evt =>
                {
                    _serializedAsset.ApplyModifiedProperties();
                    EditorUtility.SetDirty(_targetAsset);
                    OnAssetSettingsModified?.Invoke();
                });
            }

            // 1. Animation Base Foldout
            var foldoutGeneral = new Foldout { text = "Animation Base", value = true };
            foldoutGeneral.AddToClassList("montage-inspector-foldout");

            var clipProp = _serializedAsset.FindProperty("_animationClip");
            var layerProp = _serializedAsset.FindProperty("_animationLayer");
            var rateProp = _serializedAsset.FindProperty("_basePlayRate");
            var loopProp = _serializedAsset.FindProperty("_isLooping");
            var ikProp = _serializedAsset.FindProperty("_isFootIK");

            if (clipProp != null)
            {
                var f = new PropertyField(clipProp);
                f.RegisterValueChangeCallback(evt =>
                {
                    _serializedAsset.ApplyModifiedProperties();
                    EditorUtility.SetDirty(_targetAsset);
                    OnAnimationClipChanged?.Invoke();
                });
                foldoutGeneral.Add(f);
            }
            if (layerProp != null) { var f = new PropertyField(layerProp); BindSettingField(f); foldoutGeneral.Add(f); }
            if (rateProp != null) { var f = new PropertyField(rateProp); BindSettingField(f); foldoutGeneral.Add(f); }
            if (loopProp != null) { var f = new PropertyField(loopProp); BindSettingField(f); foldoutGeneral.Add(f); }
            if (ikProp != null) { var f = new PropertyField(ikProp); BindSettingField(f); foldoutGeneral.Add(f); }
            _assetSettingsContainer.Add(foldoutGeneral);

            // 2. Blending Settings Foldout
            var foldoutBlending = new Foldout { text = "Blending Settings", value = true };
            foldoutBlending.AddToClassList("montage-inspector-foldout");

            var blendInTime = _serializedAsset.FindProperty("_defaultBlendInTime");
            var blendInCurve = _serializedAsset.FindProperty("_blendInCurve");
            var blendOutTime = _serializedAsset.FindProperty("_defaultBlendOutTime");
            var blendOutCurve = _serializedAsset.FindProperty("_blendOutCurve");
            var blendOutOffset = _serializedAsset.FindProperty("_blendOutOffset");

            if (blendInTime != null) { var f = new PropertyField(blendInTime); BindSettingField(f); foldoutBlending.Add(f); }
            if (blendInCurve != null) { var f = new PropertyField(blendInCurve); BindSettingField(f); foldoutBlending.Add(f); }
            if (blendOutTime != null) { var f = new PropertyField(blendOutTime); BindSettingField(f); foldoutBlending.Add(f); }
            if (blendOutCurve != null) { var f = new PropertyField(blendOutCurve); BindSettingField(f); foldoutBlending.Add(f); }
            if (blendOutOffset != null) { var f = new PropertyField(blendOutOffset); BindSettingField(f); foldoutBlending.Add(f); }
            _assetSettingsContainer.Add(foldoutBlending);

            // 3. Root Motion Settings Foldout
            var foldoutRootMotion = new Foldout { text = "Root Motion Settings", value = true };
            foldoutRootMotion.AddToClassList("montage-inspector-foldout");

            var applyH = _serializedAsset.FindProperty("_applyHorizontalRootMotion");
            var applyV = _serializedAsset.FindProperty("_applyVerticalRootMotion");
            var applyR = _serializedAsset.FindProperty("_applyRotationRootMotion");

            if (applyH != null) { var f = new PropertyField(applyH); BindSettingField(f); foldoutRootMotion.Add(f); }
            if (applyV != null) { var f = new PropertyField(applyV); BindSettingField(f); foldoutRootMotion.Add(f); }
            if (applyR != null) { var f = new PropertyField(applyR); BindSettingField(f); foldoutRootMotion.Add(f); }
            _assetSettingsContainer.Add(foldoutRootMotion);

            // 4. Physical Sections Foldout
            var foldoutSections = new Foldout { text = "Physical Sections (去语义化物理分段)", value = true };
            foldoutSections.AddToClassList("montage-inspector-foldout");

            var splitsProp = _serializedAsset.FindProperty("_splitTimestamps");
            if (splitsProp != null)
            {
                var f = new PropertyField(splitsProp);
                BindSettingField(f);
                foldoutSections.Add(f);
            }

            // 详细分段与代码索引对齐清单
            if (_targetAsset != null)
            {
                int count = _targetAsset.SectionCount;
                var listContainer = new VisualElement();
                listContainer.style.marginTop = 6;
                listContainer.style.paddingTop = 6;
                listContainer.style.borderTopWidth = 1;
                listContainer.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f, 0.6f);

                var listHeader = new Label($"Calculated Sections ({count} total):");
                listHeader.style.fontSize = 11;
                listHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                listHeader.style.color = new Color(0.7f, 0.85f, 1f);
                listHeader.style.marginBottom = 4;
                listContainer.Add(listHeader);

                for (int s = 0; s < count; s++)
                {
                    var (start, end) = _targetAsset.GetSectionRange(s);
                    float dur = Mathf.Max(0f, end - start);
                    var secRow = new Label($"  Section {s + 1} [Index: {s}] -> {start:F2}s ~ {end:F2}s ({dur:F2}s)");
                    secRow.style.fontSize = 10;
                    secRow.style.color = new Color(0.85f, 0.85f, 0.85f);
                    listContainer.Add(secRow);
                }

                foldoutSections.Add(listContainer);
            }

            _assetSettingsContainer.Add(foldoutSections);

            _assetSettingsContainer.Bind(_serializedAsset);
        }

        #endregion
    }
}
