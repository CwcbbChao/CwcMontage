using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;
using Cwcbb.Tools.CwcMontage.Editor;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 现代化多标签页蒙太奇编辑器窗口（MontageEditorWindow）。
    /// 支持多个 MontageSequenceSO 资产同时打开、标签切换、从工程拖拽添加与跨资产剪贴板复制。
    /// </summary>
    public class MontageEditorWindow : EditorWindow
    {
        #region 私有常量

        private const int OBJECT_PICKER_CONTROL_ID = 202699;
        private const string USS_GUID = "1ac56d601272aa740b6f49bbcc195f4f";

        #endregion

        #region 私有字段

        [SerializeField] private List<MontageSequenceSO> _openedTabs = new();
        [SerializeField] private int _activeTabIndex = -1;

        private VisualElement _topMenuBar;
        private VisualElement _tabBar;
        private Label _tabCountLabel;
        private VisualElement _contentContainer;
        private MontageEditorUI _activeUI;

        #endregion

        #region Unity 生命周期

        [MenuItem("Window/Cwc/Montage Editor (动作蒙太奇编辑器)")]
        public static void OpenWindow()
        {
            var window = GetWindow<MontageEditorWindow>();
            window.titleContent = new GUIContent("Montage Editor");
            window.minSize = new Vector2(850, 520);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Montage Editor");
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            _activeUI?.Dispose();
            _activeUI = null;
        }

        private void OnGUI()
        {
            var currentEvent = Event.current;
            if (currentEvent != null && currentEvent.type == EventType.ExecuteCommand)
            {
                if (currentEvent.commandName == "ObjectSelectorUpdated" || currentEvent.commandName == "ObjectSelectorClosed")
                {
                    bool isClosed = currentEvent.commandName == "ObjectSelectorClosed";
                    if (EditorGUIUtility.GetObjectPickerControlID() == OBJECT_PICKER_CONTROL_ID)
                    {
                        var picked = EditorGUIUtility.GetObjectPickerObject() as MontageSequenceSO;
                        if (picked != null)
                        {
                            OpenTab(picked);
                        }
                    }
                    else if (EditorGUIUtility.GetObjectPickerControlID() == 202688)
                    {
                        var pickedClip = EditorGUIUtility.GetObjectPickerObject() as AnimationClip;
                        _activeUI?.HandleAnimationPickerResult(pickedClip, isClosed);
                    }
                }
            }
        }

        private void CreateGUI()
        {
            BuildWindowRoot();
        }

        private void Update()
        {
            if (_activeUI != null && (_activeUI.IsPlaying || _activeUI.IsViewportInteracting))
            {
                Repaint();
            }
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is MontageSequenceSO montage)
            {
                if (_openedTabs.Count == 0)
                {
                    OpenTab(montage);
                }
            }
        }

        #endregion

        #region 公共静态方法

        /// <summary>
        /// 打开并激活指定的蒙太奇资产标签页。
        /// </summary>
        /// <param name="asset">要编辑的蒙太奇资产</param>
        public static void OpenAsset(MontageSequenceSO asset)
        {
            if (asset == null)
            {
                return;
            }

            var window = GetWindow<MontageEditorWindow>();
            window.titleContent = new GUIContent("Montage Editor");
            window.minSize = new Vector2(850, 520);
            window.Show();
            window.OpenTab(asset);
        }

        #endregion

        #region 窗口构建与 Tab 管理

        private void BuildWindowRoot()
        {
            rootVisualElement.Clear();

            var root = new VisualElement();
            root.AddToClassList("montage-editor-root");
            root.style.flexGrow = 1;

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(USS_GUID));
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // 1. 第 1 行：Top Menu Bar
            _topMenuBar = BuildTopMenuBar();
            root.Add(_topMenuBar);

            // 2. 第 2 行：Document Tab Bar
            _tabBar = new VisualElement();
            _tabBar.AddToClassList("montage-tab-bar");

            // 支持从 Project 拖入资产
            _tabBar.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            _tabBar.RegisterCallback<DragPerformEvent>(OnDragPerform);

            root.Add(_tabBar);

            // 3. 主内容容器
            _contentContainer = new VisualElement();
            _contentContainer.style.flexGrow = 1;
            root.Add(_contentContainer);

            rootVisualElement.Add(root);

            RebuildTabBar();
            LoadActiveTabContent();
        }

        private VisualElement BuildTopMenuBar()
        {
            var topBar = new VisualElement();
            topBar.AddToClassList("montage-window-top-bar");

            var leftGroup = new VisualElement();
            leftGroup.AddToClassList("montage-window-top-left");

            // Open Asset 按钮
            var openBtn = new Button(OpenAssetPicker);
            openBtn.AddToClassList("montage-top-bar-btn");
            openBtn.tooltip = "Open MontageSequenceSO Asset with Native Searchable Picker";

            var folderIcon = EditorGUIUtility.IconContent("FolderOpened Icon") ?? EditorGUIUtility.IconContent("d_Project");
            if (folderIcon != null && folderIcon.image != null)
            {
                var iconImg = new Image { image = folderIcon.image, pickingMode = PickingMode.Ignore };
                iconImg.style.width = 14;
                iconImg.style.height = 14;
                openBtn.Add(iconImg);
            }
            openBtn.Add(new Label("Open Asset"));
            leftGroup.Add(openBtn);

            // Save 按钮
            var saveBtn = new Button(() =>
            {
                AssetDatabase.SaveAssets();
                if (_activeTabIndex >= 0 && _activeTabIndex < _openedTabs.Count && _openedTabs[_activeTabIndex] != null)
                {
                    titleContent = new GUIContent($"{_openedTabs[_activeTabIndex].name} - Montage Editor");
                }
            });
            saveBtn.AddToClassList("montage-top-bar-btn");
            saveBtn.tooltip = "Save all modified assets (Ctrl+S)";

            var saveIcon = EditorGUIUtility.IconContent("d_SaveAs") ?? EditorGUIUtility.IconContent("SaveAs");
            if (saveIcon != null && saveIcon.image != null)
            {
                var saveImg = new Image { image = saveIcon.image, pickingMode = PickingMode.Ignore };
                saveImg.style.width = 14;
                saveImg.style.height = 14;
                saveBtn.Add(saveImg);
            }
            saveBtn.Add(new Label("Save"));
            leftGroup.Add(saveBtn);

            // Locate in Project 按钮
            var pingBtn = new Button(() =>
            {
                if (_activeTabIndex >= 0 && _activeTabIndex < _openedTabs.Count && _openedTabs[_activeTabIndex] != null)
                {
                    EditorGUIUtility.PingObject(_openedTabs[_activeTabIndex]);
                }
            });
            pingBtn.AddToClassList("montage-top-bar-btn");
            pingBtn.tooltip = "Highlight current asset in Project window";
            pingBtn.Add(new Label("Locate"));
            leftGroup.Add(pingBtn);

            topBar.Add(leftGroup);

            // 右侧信息指示
            var rightGroup = new VisualElement();
            rightGroup.AddToClassList("montage-window-top-right");

            _tabCountLabel = new Label("0 Tabs");
            _tabCountLabel.AddToClassList("montage-top-bar-info");
            rightGroup.Add(_tabCountLabel);

            topBar.Add(rightGroup);

            return topBar;
        }

        private void RebuildTabBar()
        {
            _tabBar.Clear();

            var tabScroll = new ScrollView(ScrollViewMode.Horizontal);
            tabScroll.AddToClassList("montage-tab-scroll");
            tabScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            for (int i = 0; i < _openedTabs.Count; i++)
            {
                int tabIndex = i;
                var asset = _openedTabs[i];
                if (asset == null) continue;

                var tabItem = new VisualElement();
                tabItem.AddToClassList("montage-tab-item");
                if (tabIndex == _activeTabIndex)
                {
                    tabItem.AddToClassList("montage-tab-item-active");
                }

                var label = new Label(asset.name);
                label.AddToClassList("montage-tab-label");
                tabItem.Add(label);

                var closeBtn = new Button(() => CloseTab(tabIndex)) { text = "x" };
                closeBtn.AddToClassList("montage-tab-close-btn");
                tabItem.Add(closeBtn);

                tabItem.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button == 0)
                    {
                        SwitchToTab(tabIndex);
                        evt.StopPropagation();
                    }
                });

                tabScroll.Add(tabItem);
            }

            _tabBar.Add(tabScroll);

            if (_tabCountLabel != null)
            {
                _tabCountLabel.text = $"{_openedTabs.Count} Tab{(_openedTabs.Count > 1 ? "s" : "")}";
            }
        }

        private void OpenAssetPicker()
        {
            EditorGUIUtility.ShowObjectPicker<MontageSequenceSO>(null, false, "", OBJECT_PICKER_CONTROL_ID);
        }

        private void OpenTab(MontageSequenceSO asset)
        {
            if (asset == null) return;

            int existingIndex = _openedTabs.IndexOf(asset);
            if (existingIndex >= 0)
            {
                SwitchToTab(existingIndex);
                return;
            }

            _openedTabs.Add(asset);
            _activeTabIndex = _openedTabs.Count - 1;

            RebuildTabBar();
            LoadActiveTabContent();
        }

        private void SwitchToTab(int index)
        {
            if (index < 0 || index >= _openedTabs.Count || index == _activeTabIndex)
            {
                return;
            }

            _activeTabIndex = index;
            RebuildTabBar();
            LoadActiveTabContent();
        }

        private void CloseTab(int index)
        {
            if (index < 0 || index >= _openedTabs.Count)
            {
                return;
            }

            _openedTabs.RemoveAt(index);

            if (_activeTabIndex >= _openedTabs.Count)
            {
                _activeTabIndex = _openedTabs.Count - 1;
            }

            RebuildTabBar();
            LoadActiveTabContent();
        }

        private void LoadActiveTabContent()
        {
            _activeUI?.Dispose();
            _activeUI = null;

            if (_contentContainer == null)
            {
                BuildWindowRoot();
                return;
            }

            _contentContainer.Clear();

            if (_activeTabIndex < 0 || _activeTabIndex >= _openedTabs.Count || _openedTabs[_activeTabIndex] == null)
            {
                var emptyContainer = new VisualElement();
                emptyContainer.style.flexGrow = 1;
                emptyContainer.style.justifyContent = Justify.Center;
                emptyContainer.style.alignItems = Align.Center;

                var emptyCard = new VisualElement();
                emptyCard.AddToClassList("montage-empty-tracks-card");
                emptyCard.style.alignItems = Align.Center;
                emptyCard.style.paddingTop = 24;
                emptyCard.style.paddingBottom = 24;
                emptyCard.style.paddingLeft = 24;
                emptyCard.style.paddingRight = 24;

                var emptyLabel = new Label("No Montage Sequence Opened");
                emptyLabel.AddToClassList("montage-empty-tracks-title");
                emptyLabel.style.marginBottom = 8;
                emptyCard.Add(emptyLabel);

                var emptyDesc = new Label("Double click a MontageSequenceSO asset in Project, or click below to search and open.");
                emptyDesc.AddToClassList("montage-empty-tracks-desc");
                emptyDesc.style.marginBottom = 16;
                emptyCard.Add(emptyDesc);

                var openAssetBtn = new Button(OpenAssetPicker) { text = "Open Montage Asset..." };
                openAssetBtn.AddToClassList("montage-toolbar-btn");
                openAssetBtn.AddToClassList("montage-primary-btn");
                openAssetBtn.style.height = 30;
                openAssetBtn.style.paddingLeft = 16;
                openAssetBtn.style.paddingRight = 16;
                emptyCard.Add(openAssetBtn);

                emptyContainer.Add(emptyCard);
                _contentContainer.Add(emptyContainer);

                titleContent = new GUIContent("Montage Editor");
                return;
            }

            var currentAsset = _openedTabs[_activeTabIndex];
            var serializedAsset = new SerializedObject(currentAsset);

            _activeUI = new MontageEditorUI(currentAsset, serializedAsset);
            _activeUI.OnRepaintRequested += Repaint;
            _activeUI.OnAssetModified += () =>
            {
                titleContent = new GUIContent($"{currentAsset.name} - Montage Editor");
            };

            _contentContainer.Add(_activeUI.Root);
            titleContent = new GUIContent($"{currentAsset.name} - Montage Editor");
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (DragAndDrop.objectReferences.Length > 0)
            {
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is MontageSequenceSO)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.StopPropagation();
                        return;
                    }
                }
            }
            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            DragAndDrop.AcceptDrag();
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is MontageSequenceSO montage)
                {
                    OpenTab(montage);
                }
            }
            evt.StopPropagation();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                _openedTabs.RemoveAll(t => t == null);
                if (_activeTabIndex >= _openedTabs.Count)
                {
                    _activeTabIndex = _openedTabs.Count - 1;
                }

                if (_contentContainer == null || rootVisualElement == null || rootVisualElement.childCount == 0)
                {
                    BuildWindowRoot();
                }
                else
                {
                    RebuildTabBar();
                    LoadActiveTabContent();
                }
            }
        }

        #endregion
    }
}
