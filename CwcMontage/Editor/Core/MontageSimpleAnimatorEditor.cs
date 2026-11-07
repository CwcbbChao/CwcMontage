using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// MontageSimpleAnimator 自定义 Inspector 绘制器。
    /// 将 [枚举状态 -> 蒙太奇资产] 的映射列表紧凑合并在同一行绘制，
    /// 提供可视化拖拽排序、快速增删以及运行期实时状态监控面板。
    /// </summary>
    [CustomEditor(typeof(MontageSimpleAnimatorBase), true)]
    [CanEditMultipleObjects]
    public class MontageSimpleAnimatorEditor : UnityEditor.Editor
    {
        #region 1. 常量与样式字段

        protected const float ARROW_WIDTH = 24f;
        protected const float STATE_WIDTH_RATIO = 0.38f;

        // 剪贴板缓存（支持行间复制与粘贴）
        protected static int s_clipboardStateIndex = -1;
        protected static UnityEngine.Object s_clipboardMontage = null;
        protected static float s_clipboardRate = 1.0f;
        protected static bool s_hasClipboardData = false;

        protected static GUIStyle s_arrowStyle;
        protected static GUIStyle ArrowStyle
        {
            get
            {
                if (s_arrowStyle == null)
                {
                    s_arrowStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 1f) }
                    };
                }
                return s_arrowStyle;
            }
        }

        #endregion

        #region 2. 字段定义

        protected SerializedProperty _controllerProp;
        protected SerializedProperty _montageBindingsProp;
        protected SerializedProperty _clipBindingsProp;
        protected ReorderableList _montageList;
        protected ReorderableList _clipList;

        // 编辑器状态测试字段
        protected int _testStateIndex = 0;
        protected float _testDuration = 1.0f;
        protected int _testPriority = 0;
        protected int _lastSelectedStateIndex = -1;
        protected bool _showTestingFold = true;

        #endregion

        #region 3. Unity 生命周期

        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }

        protected virtual void OnEnable()
        {
            _controllerProp = serializedObject.FindProperty("_montageController");
            _montageBindingsProp = serializedObject.FindProperty("_montageBindings");
            _clipBindingsProp = serializedObject.FindProperty("_clipBindings");

            if (_montageBindingsProp != null)
            {
                SetupMontageList();
            }

            if (_clipBindingsProp != null)
            {
                SetupClipList();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 1. 运行期调试与状态测试面板
            if (target is MontageSimpleAnimatorBase animator)
            {
                DrawTestingPanel(animator);
                EditorGUILayout.Space(6);
            }

            // 2. 核心调度器引用
            if (_controllerProp != null)
            {
                EditorGUILayout.PropertyField(_controllerProp);
                EditorGUILayout.Space(4);
            }

            // 3. 蒙太奇资产映射列表（优先级 1：优先匹配）
            if (_montageList != null)
            {
                _montageList.DoLayoutList();
                EditorGUILayout.Space(6);
            }
            else if (_montageBindingsProp != null)
            {
                EditorGUILayout.PropertyField(_montageBindingsProp, true);
                EditorGUILayout.Space(6);
            }

            // 4. 基础动画切片映射列表（优先级 2：保底表现）
            if (_clipList != null)
            {
                _clipList.DoLayoutList();
                EditorGUILayout.Space(6);
            }
            else if (_clipBindingsProp != null)
            {
                EditorGUILayout.PropertyField(_clipBindingsProp, true);
                EditorGUILayout.Space(6);
            }

            // 5. 绘制派生子类可能声明的其他属性，排除已自定义绘制的基础属性
            DrawPropertiesExcluding(serializedObject, "m_Script", "_skeletonType", "_montageController", "_montageBindings", "_clipBindings");

            serializedObject.ApplyModifiedProperties();
        }

        #endregion

        #region 4. 受保护列表配置与绘制方法

        /// <summary>
        /// 构建蒙太奇动画资产映射列表（ReorderableList）。
        /// 支持拖入新版 MontageSequenceAsset 或旧版 MontageSequenceSO。
        /// </summary>
        protected virtual void SetupMontageList()
        {
            _montageList = new ReorderableList(serializedObject, _montageBindingsProp, true, true, true, true)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 4f
            };

            _montageList.drawHeaderCallback = (Rect rect) =>
            {
                int count = _montageBindingsProp.arraySize;
                EditorGUI.LabelField(rect, $"Montage Asset Bindings [{count}] (Priority 1: Enum  ->  Montage Asset)", EditorStyles.boldLabel);

                Event evt = Event.current;
                if (evt.type == EventType.ContextClick && rect.Contains(evt.mousePosition))
                {
                    evt.Use();
                    GenericMenu headerMenu = new GenericMenu();
                    headerMenu.AddItem(new GUIContent("Clear All Montage Bindings"), false, () => ClearAllElements(_montageBindingsProp));
                    headerMenu.ShowAsContext();
                }
            };

            _montageList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index < 0 || index >= _montageBindingsProp.arraySize) return;

                SerializedProperty elementProp = _montageBindingsProp.GetArrayElementAtIndex(index);
                SerializedProperty stateProp = elementProp.FindPropertyRelative("State");
                SerializedProperty montageProp = elementProp.FindPropertyRelative("Montage");

                EditorGUI.BeginProperty(rect, GUIContent.none, elementProp);

                Event evt = Event.current;
                if (evt.type == EventType.ContextClick && rect.Contains(evt.mousePosition))
                {
                    evt.Use();
                    ShowMontageElementContextMenu(index);
                }

                rect.y += 2f;
                rect.height = EditorGUIUtility.singleLineHeight;

                float totalWidth = rect.width;
                float stateWidth = Mathf.Max(90f, (totalWidth - ARROW_WIDTH) * STATE_WIDTH_RATIO);
                float montageWidth = totalWidth - stateWidth - ARROW_WIDTH;

                Rect stateRect = new Rect(rect.x, rect.y, stateWidth, rect.height);
                Rect arrowRect = new Rect(rect.x + stateWidth, rect.y, ARROW_WIDTH, rect.height);
                Rect montageRect = new Rect(rect.x + stateWidth + ARROW_WIDTH, rect.y, montageWidth, rect.height);

                if (stateProp != null)
                {
                    EditorGUI.PropertyField(stateRect, stateProp, GUIContent.none);
                }

                GUI.Label(arrowRect, "->", ArrowStyle);

                if (montageProp != null)
                {
                    EditorGUI.PropertyField(montageRect, montageProp, GUIContent.none);
                }

                EditorGUI.EndProperty();
            };
        }

        /// <summary>
        /// 构建基础动画切片保底映射列表（ReorderableList）。
        /// 直接拖入原生 AnimationClip。
        /// </summary>
        protected virtual void SetupClipList()
        {
            _clipList = new ReorderableList(serializedObject, _clipBindingsProp, true, true, true, true)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 4f
            };

            _clipList.drawHeaderCallback = (Rect rect) =>
            {
                int count = _clipBindingsProp.arraySize;
                EditorGUI.LabelField(rect, $"Animation Clip Fallback Bindings [{count}] (Priority 2: Enum  ->  Clip)", EditorStyles.boldLabel);

                Event evt = Event.current;
                if (evt.type == EventType.ContextClick && rect.Contains(evt.mousePosition))
                {
                    evt.Use();
                    GenericMenu headerMenu = new GenericMenu();
                    headerMenu.AddItem(new GUIContent("Clear All Clip Bindings"), false, () => ClearAllElements(_clipBindingsProp));
                    headerMenu.ShowAsContext();
                }
            };

            _clipList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index < 0 || index >= _clipBindingsProp.arraySize) return;

                SerializedProperty elementProp = _clipBindingsProp.GetArrayElementAtIndex(index);
                SerializedProperty stateProp = elementProp.FindPropertyRelative("State");
                SerializedProperty clipProp = elementProp.FindPropertyRelative("Clip");
                SerializedProperty rateProp = elementProp.FindPropertyRelative("PlayRate");

                EditorGUI.BeginProperty(rect, GUIContent.none, elementProp);

                Event evt = Event.current;
                if (evt.type == EventType.ContextClick && rect.Contains(evt.mousePosition))
                {
                    evt.Use();
                    ShowClipElementContextMenu(index);
                }

                rect.y += 2f;
                rect.height = EditorGUIUtility.singleLineHeight;

                float totalWidth = rect.width;
                float rateWidth = 52f;
                float stateWidth = Mathf.Max(80f, (totalWidth - ARROW_WIDTH - rateWidth - 4f) * STATE_WIDTH_RATIO);
                float clipWidth = totalWidth - stateWidth - ARROW_WIDTH - rateWidth - 4f;

                Rect stateRect = new Rect(rect.x, rect.y, stateWidth, rect.height);
                Rect arrowRect = new Rect(rect.x + stateWidth, rect.y, ARROW_WIDTH, rect.height);
                Rect clipRect = new Rect(rect.x + stateWidth + ARROW_WIDTH, rect.y, clipWidth, rect.height);
                Rect rateRect = new Rect(rect.x + stateWidth + ARROW_WIDTH + clipWidth + 4f, rect.y, rateWidth, rect.height);

                if (stateProp != null)
                {
                    EditorGUI.PropertyField(stateRect, stateProp, GUIContent.none);
                }

                GUI.Label(arrowRect, "->", ArrowStyle);

                if (clipProp != null)
                {
                    EditorGUI.PropertyField(clipRect, clipProp, GUIContent.none);
                }

                if (rateProp != null)
                {
                    if (rateProp.floatValue <= 0.001f)
                    {
                        rateProp.floatValue = 1.0f;
                    }
                    EditorGUI.LabelField(new Rect(rateRect.x, rateRect.y, 12f, rateRect.height), "x");
                    Rect numRect = new Rect(rateRect.x + 12f, rateRect.y, rateRect.width - 12f, rateRect.height);
                    rateProp.floatValue = Mathf.Max(0.01f, EditorGUI.FloatField(numRect, GUIContent.none, rateProp.floatValue));
                }

                EditorGUI.EndProperty();
            };
        }

        protected virtual void ShowMontageElementContextMenu(int index)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Duplicate"), false, () => DuplicateElement(_montageBindingsProp, index));
            menu.AddItem(new GUIContent("Delete"), false, () => DeleteElement(_montageBindingsProp, index));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy"), false, () => CopyMontageElement(index));
            if (s_hasClipboardData)
            {
                menu.AddItem(new GUIContent("Paste"), false, () => PasteMontageElement(index));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste"));
            }
            menu.AddItem(new GUIContent("Insert Below"), false, () => InsertElement(_montageBindingsProp, index + 1));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Clear All"), false, () => ClearAllElements(_montageBindingsProp));
            menu.ShowAsContext();
        }

        protected virtual void ShowClipElementContextMenu(int index)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Duplicate"), false, () => DuplicateElement(_clipBindingsProp, index));
            menu.AddItem(new GUIContent("Delete"), false, () => DeleteElement(_clipBindingsProp, index));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy"), false, () => CopyClipElement(index));
            if (s_hasClipboardData)
            {
                menu.AddItem(new GUIContent("Paste"), false, () => PasteClipElement(index));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste"));
            }
            menu.AddItem(new GUIContent("Insert Below"), false, () => InsertElement(_clipBindingsProp, index + 1));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Clear All"), false, () => ClearAllElements(_clipBindingsProp));
            menu.ShowAsContext();
        }

        protected virtual void DuplicateElement(SerializedProperty listProp, int index)
        {
            serializedObject.Update();
            listProp.InsertArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void DeleteElement(SerializedProperty listProp, int index)
        {
            serializedObject.Update();
            listProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void InsertElement(SerializedProperty listProp, int targetIndex)
        {
            serializedObject.Update();
            int clampedIndex = Mathf.Clamp(targetIndex, 0, listProp.arraySize);
            listProp.InsertArrayElementAtIndex(clampedIndex);
            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void ClearAllElements(SerializedProperty listProp)
        {
            serializedObject.Update();
            listProp.ClearArray();
            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void CopyMontageElement(int index)
        {
            if (index < 0 || index >= _montageBindingsProp.arraySize) return;

            SerializedProperty elementProp = _montageBindingsProp.GetArrayElementAtIndex(index);
            SerializedProperty stateProp = elementProp.FindPropertyRelative("State");
            SerializedProperty montageProp = elementProp.FindPropertyRelative("Montage");

            s_clipboardStateIndex = stateProp != null ? stateProp.enumValueIndex : -1;
            s_clipboardMontage = montageProp != null ? montageProp.objectReferenceValue : null;
            s_hasClipboardData = true;
        }

        protected virtual void PasteMontageElement(int index)
        {
            if (!s_hasClipboardData || index < 0 || index >= _montageBindingsProp.arraySize) return;

            serializedObject.Update();
            SerializedProperty elementProp = _montageBindingsProp.GetArrayElementAtIndex(index);
            SerializedProperty stateProp = elementProp.FindPropertyRelative("State");
            SerializedProperty montageProp = elementProp.FindPropertyRelative("Montage");

            if (stateProp != null && s_clipboardStateIndex >= 0)
            {
                stateProp.enumValueIndex = s_clipboardStateIndex;
            }

            if (montageProp != null)
            {
                if (s_clipboardMontage is MontageSequenceAsset pasteAsset)
                {
                    montageProp.objectReferenceValue = pasteAsset;
                }
                else if (s_clipboardMontage == null)
                {
                    montageProp.objectReferenceValue = null;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void CopyClipElement(int index)
        {
            if (index < 0 || index >= _clipBindingsProp.arraySize) return;

            SerializedProperty elementProp = _clipBindingsProp.GetArrayElementAtIndex(index);
            SerializedProperty stateProp = elementProp.FindPropertyRelative("State");
            SerializedProperty clipProp = elementProp.FindPropertyRelative("Clip");
            SerializedProperty rateProp = elementProp.FindPropertyRelative("PlayRate");

            s_clipboardStateIndex = stateProp != null ? stateProp.enumValueIndex : -1;
            s_clipboardMontage = clipProp != null ? clipProp.objectReferenceValue : null;
            s_clipboardRate = (rateProp != null && rateProp.floatValue > 0.001f) ? rateProp.floatValue : 1.0f;
            s_hasClipboardData = true;
        }

        protected virtual void PasteClipElement(int index)
        {
            if (!s_hasClipboardData || index < 0 || index >= _clipBindingsProp.arraySize) return;

            serializedObject.Update();
            SerializedProperty elementProp = _clipBindingsProp.GetArrayElementAtIndex(index);
            SerializedProperty stateProp = elementProp.FindPropertyRelative("State");
            SerializedProperty clipProp = elementProp.FindPropertyRelative("Clip");
            SerializedProperty rateProp = elementProp.FindPropertyRelative("PlayRate");

            if (stateProp != null && s_clipboardStateIndex >= 0)
            {
                stateProp.enumValueIndex = s_clipboardStateIndex;
            }

            if (s_clipboardMontage is AnimationClip pasteClip)
            {
                if (clipProp != null) clipProp.objectReferenceValue = pasteClip;
            }
            else if (s_clipboardMontage == null)
            {
                if (clipProp != null) clipProp.objectReferenceValue = null;
            }

            if (rateProp != null)
            {
                rateProp.floatValue = s_clipboardRate > 0.001f ? s_clipboardRate : 1.0f;
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 绘制状态监控与编辑器测试交互面板。
        /// </summary>
        protected virtual void DrawTestingPanel(MontageSimpleAnimatorBase animator)
        {
            _showTestingFold = EditorGUILayout.Foldout(_showTestingFold, "Runtime Testing Tools", true, EditorStyles.foldoutHeader);
            if (!_showTestingFold) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // 1. 活跃状态监控栏
                string stateName = animator.BaseHasActiveState ? animator.BaseCurrentStateName : "None";
                GUI.color = (Application.isPlaying && animator.BaseHasActiveState) ? new Color(0.3f, 1f, 0.4f) : Color.white;
                EditorGUILayout.LabelField($"Active State: {stateName}", EditorStyles.boldLabel);
                GUI.color = Color.white;

                if (!Application.isPlaying)
                {
                    EditorGUILayout.HelpBox("Enter Play Mode to test Timed Trigger and Sustained State transitions in real-time.", MessageType.Info);
                }

                // 非 PlayMode 时测试按钮置灰
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    IReadOnlyList<string> stateNames = animator.BaseGetStateNames();
                    if (stateNames == null || stateNames.Count == 0)
                    {
                        EditorGUILayout.LabelField("No states available for testing.", EditorStyles.miniLabel);
                        return;
                    }

                    // 构建显示标签（内嵌优先级数值与是否有绑定资产标注）
                    string[] displayOptions = new string[stateNames.Count];
                    for (int i = 0; i < stateNames.Count; i++)
                    {
                        bool hasBinding = animator.BaseHasBindingForState(i);
                        int priority = animator.BaseGetStateDefaultPriority(i);
                        displayOptions[i] = hasBinding 
                            ? $"{stateNames[i]} [{priority}]" 
                            : $"{stateNames[i]} [{priority}] (No Binding)";
                    }

                    if (_testStateIndex < 0 || _testStateIndex >= stateNames.Count)
                    {
                        _testStateIndex = 0;
                    }

                    // 状态选择 Popup 与持续激活指示（紧凑并入单行）
                    bool isSustainedActive = animator.BaseIsSustainedStateActive(_testStateIndex);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        int newIndex = EditorGUILayout.Popup("Target State", _testStateIndex, displayOptions);
                        if (newIndex != _testStateIndex || _lastSelectedStateIndex != newIndex)
                        {
                            _testStateIndex = newIndex;
                            _lastSelectedStateIndex = newIndex;
                            _testPriority = animator.BaseGetStateDefaultPriority(_testStateIndex);
                        }

                        if (isSustainedActive)
                        {
                            GUI.color = new Color(0.3f, 1f, 0.4f);
                            GUILayout.Label("[Sustained Active]", EditorStyles.miniBoldLabel, GUILayout.Width(110));
                            GUI.color = Color.white;
                        }
                    }

                    EditorGUILayout.Space(2);

                    // 固定时长触发测试按钮
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _testDuration = EditorGUILayout.FloatField(new GUIContent("Duration (s)", "持续时间（秒）。填 0 则按动作原始物理时长播放"), _testDuration);
                        if (_testDuration < 0f) _testDuration = 0f;

                        GUI.backgroundColor = new Color(0.35f, 0.85f, 0.5f);
                        if (GUILayout.Button(new GUIContent("Trigger Timed", "触发固定时长的瞬态动作（按枚举自身优先级仲裁，到期后自动平滑切回）"), GUILayout.Height(22)))
                        {
                            animator.BaseTriggerTimedState(_testStateIndex, _testDuration);
                        }
                        GUI.backgroundColor = Color.white;
                    }

                    EditorGUILayout.Space(2);

                    // 持续状态进入/退出测试按钮
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.backgroundColor = isSustainedActive ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.4f, 0.75f, 1f);
                        if (GUILayout.Button(new GUIContent("Enter State", "将当前状态作为持续状态激活（按枚举自身优先级仲裁，如待机、移动）"), GUILayout.Height(22)))
                        {
                            animator.BaseSetSustainedState(_testStateIndex, true);
                        }

                        GUI.backgroundColor = isSustainedActive ? new Color(1f, 0.5f, 0.5f) : new Color(0.8f, 0.8f, 0.8f);
                        if (GUILayout.Button(new GUIContent("Exit State", "退出当前持续状态"), GUILayout.Height(22)))
                        {
                            animator.BaseSetSustainedState(_testStateIndex, false);
                        }
                        GUI.backgroundColor = Color.white;
                    }

                    EditorGUILayout.Space(2);

                    // 一键全部停止/重置按钮
                    if (GUILayout.Button(new GUIContent("Stop All States", "停止所有活跃状态池与动作，重置为无状态"), GUILayout.Height(20)))
                    {
                        animator.BaseStopAllStates();
                    }
                }
            }
        }

        #endregion
    }
}
