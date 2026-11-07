using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;
using Object = UnityEngine.Object;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// Clean 3D preview viewport element (MontagePreviewViewportElement).
    /// Wraps the full PreviewRenderUtility rendering pipeline, FPS fly camera, orbit camera, and ground reference grid.
    /// </summary>
    public class MontagePreviewViewportElement : VisualElement
    {
        #region Private Fields

        private MontageSequenceAsset _targetAsset;
        private PreviewRenderUtility _renderUtility;
        private RenderTexture _previewTexture;
        private Image _previewImage;
        private GameObject _previewObjectInstance;

        // Camera parameters
        private float _cameraDistance = 4.5f;
        private Vector3 _cameraTarget = new Vector3(0f, 1f, 0f);
        private float _cameraYaw = 225f; // 45-degree front view (180° + 45°)
        private float _cameraPitch = 12f; // Slight downward tilt (12° pitch, enhances 3D depth perception)
        private bool _isRightMouseButtonHeld;
        private bool _isMiddleMouseButtonHeld;
        private readonly HashSet<KeyCode> _pressedKeys = new();

        private readonly List<Renderer> _cachedRenderers = new();

        private Button _previewMenuBtn;
        private VisualElement _previewSettingsPanel;
        private Toggle _overrideModelToggle;
        private ObjectField _modelObjectField;
        private ObjectField _baseMotionObjectField;
        private ObjectField _upperBodyMaskObjectField;

        private Button _viewMenuBtn;
        private VisualElement _viewSettingsPanel;
        private Toggle _rootMotionToggle;
        private Toggle _footIKToggle;
        private Toggle _gridToggle;
        private Toggle _litToggle;

        private Mesh _gridMesh;
        private Material _gridMaterial;
        private bool _showGroundGrid = true;
        private bool _isUnlitMode = false;

        #endregion

        #region Public Events

        /// <summary>
        /// Raised when the user switches or drags in a new preview model in the viewport.
        /// </summary>
        public event Action<GameObject> OnPreviewModelChanged;

        /// <summary>
        /// Raised when the user switches or drags in a new base locomotion motion (Base Locomotion Motion) in the viewport.
        /// </summary>
        public event Action<AnimationClip> OnPreviewBaseMotionChanged;

        /// <summary>
        /// Raised when the user switches or assigns a new custom upper-body mask (UpperBody Mask) in the viewport advanced settings.
        /// </summary>
        public event Action<AvatarMask> OnPreviewUpperBodyMaskChanged;

        /// <summary>
        /// Raised when the user toggles asset-specific preview model binding (Override Preview Model).
        /// </summary>
        public event Action<bool> OnOverrideModelToggled;

        /// <summary>
        /// Raised when the user toggles preview root motion (Root Motion).
        /// </summary>
        public event Action<bool> OnRootMotionToggled;

        /// <summary>
        /// Raised when the user toggles preview foot IK (Foot IK).
        /// </summary>
        public event Action<bool> OnFootIKToggled;

        #endregion

        #region Public Properties

        public PreviewRenderUtility RenderUtility => _renderUtility;
        public GameObject PreviewObjectInstance => _previewObjectInstance;
        public bool IsInteracting => _isRightMouseButtonHeld || _isMiddleMouseButtonHeld || _pressedKeys.Count > 0;

        #endregion

        #region Constructors

        public MontagePreviewViewportElement()
        {
            AddToClassList("montage-viewport-container");

            _previewImage = new Image();
            _previewImage.AddToClassList("montage-preview-image");
            _previewImage.scaleMode = ScaleMode.StretchToFill;
            _previewImage.focusable = true;
            _previewImage.tooltip = "Right Click + Drag: Rotate Camera\nRight Click + WASD: FPS Fly Camera\nMiddle Click + Drag: Pan Camera\nScroll: Zoom";
            Add(_previewImage);

            // Top-right overlay quick toolbar (dual collapsible dropdown card architecture)
            var overlayBar = new VisualElement();
            overlayBar.AddToClassList("montage-viewport-overlay");

            // 1. Preview assets collapsible menu (Model, Base Motion, UpperBody Mask, Override)
            _previewMenuBtn = new Button(TogglePreviewSettingsPanel) { text = "Preview ▾" };
            _previewMenuBtn.AddToClassList("montage-overlay-btn");
            _previewMenuBtn.tooltip = "Preview Assets (Model, Base Motion, UpperBody Mask, Asset Override)";
            overlayBar.Add(_previewMenuBtn);

            var separator = new VisualElement();
            separator.AddToClassList("montage-overlay-separator");
            overlayBar.Add(separator);

            // 2. View display multi-select toggle list (Root Motion, Foot IK, Grid, Lit, Camera)
            _viewMenuBtn = new Button(ToggleViewSettingsPanel) { text = "View ▾" };
            _viewMenuBtn.AddToClassList("montage-overlay-btn");
            _viewMenuBtn.tooltip = "Viewport Display Options (Root Motion, Foot IK, Grid, Shading, Camera)";
            overlayBar.Add(_viewMenuBtn);

            Add(overlayBar);

            // 3. Build the two collapsible floating panels
            BuildPreviewSettingsPanel();
            Add(_previewSettingsPanel);

            BuildViewSettingsPanel();
            Add(_viewSettingsPanel);

            // Register viewport interactions
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<MouseMoveEvent>(OnMouseMove);
            RegisterCallback<MouseUpEvent>(OnMouseUp);
            RegisterCallback<MouseLeaveEvent>(OnMouseLeave);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<KeyUpEvent>(OnKeyUp);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        #endregion

        #region Public Lifecycle and Methods

        /// <summary>
        /// Sets the Prefab reference shown in the viewport model selector (does not trigger change callbacks).
        /// </summary>
        public void SetSelectedModelPrefab(GameObject prefab)
        {
            _modelObjectField?.SetValueWithoutNotify(prefab);
        }

        /// <summary>
        /// Updates the root motion (Root Motion) preview toggle state.
        /// </summary>
        public void SetRootMotionState(bool enabled)
        {
            _rootMotionToggle?.SetValueWithoutNotify(enabled);
        }

        /// <summary>
        /// Updates the foot IK (Foot IK) preview toggle state.
        /// </summary>
        public void SetFootIKState(bool enabled)
        {
            _footIKToggle?.SetValueWithoutNotify(enabled);
        }

        /// <summary>
        /// Initializes the 3D render viewport and preview model.
        /// </summary>
        public void Initialize(MontageSequenceAsset asset, GameObject previewModelPrefab, ref GameObject outPreviewObject)
        {
            Cleanup();
            _targetAsset = asset;
            SetSelectedModelPrefab(previewModelPrefab);

            _renderUtility = new PreviewRenderUtility
            {
                camera = { nearClipPlane = 0.01f, farClipPlane = 100f, fieldOfView = 30f }
            };

            var cam = _renderUtility.camera;
            cam.cameraType = CameraType.Game;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.14f, 0.145f, 0.155f, 1f); // Blender classic neutral dark charcoal (#242528)
            cam.allowHDR = true;
            cam.allowMSAA = true;
            cam.renderingPath = RenderingPath.Forward;

            _renderUtility.ambientColor = new Color(0.26f, 0.27f, 0.29f, 1f);
            foreach (var light in _renderUtility.lights)
            {
                light.enabled = true;
            }

            // Key light
            if (_renderUtility.lights.Length > 0)
            {
                Light keyLight = _renderUtility.lights[0];
                keyLight.intensity = 1.15f;
                keyLight.color = new Color(0.96f, 0.96f, 0.98f);
                keyLight.transform.rotation = Quaternion.Euler(38f, 135f, 0f);
            }

            // Rim light
            if (_renderUtility.lights.Length > 1)
            {
                Light rimLight = _renderUtility.lights[1];
                rimLight.intensity = 0.55f;
                rimLight.color = new Color(0.70f, 0.80f, 0.95f);
                rimLight.transform.rotation = Quaternion.Euler(-20f, -45f, 0f);
            }

            EnsureGridResources();

            // Instantiate the preview model
            if (previewModelPrefab != null)
            {
                _previewObjectInstance = _renderUtility.InstantiatePrefabInScene(previewModelPrefab);
                _previewObjectInstance.hideFlags = HideFlags.HideAndDontSave;

                foreach (var smr in _previewObjectInstance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    smr.updateWhenOffscreen = true;
                    smr.forceMatrixRecalculationPerRender = true;
                }

                _cachedRenderers.Clear();
                _cachedRenderers.AddRange(_previewObjectInstance.GetComponentsInChildren<Renderer>(true));
            }

            ApplyLightingMode(_isUnlitMode);
            ResetCamera(true);
            ReinitializeTexture();
            RenderImmediate();

            outPreviewObject = _previewObjectInstance;
        }

        /// <summary>
        /// Updates camera translation and the render texture every frame.
        /// </summary>
        public void RenderFrame()
        {
            if (_renderUtility == null || _previewTexture == null)
            {
                return;
            }

            HandleFPSMovement();
            UpdateCameraTransform();

            if (_showGroundGrid && _gridMesh != null && _gridMaterial != null)
            {
                Graphics.DrawMesh(_gridMesh, Matrix4x4.identity, _gridMaterial, 0, _renderUtility.camera);
            }

            _renderUtility.camera.Render();
            _previewImage.MarkDirtyRepaint();
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Redraws the viewport immediately.
        /// </summary>
        public void RenderImmediate()
        {
            if (_renderUtility == null || _previewTexture == null)
            {
                return;
            }

            UpdateCameraTransform();

            if (_showGroundGrid && _gridMesh != null && _gridMaterial != null)
            {
                Graphics.DrawMesh(_gridMesh, Matrix4x4.identity, _gridMaterial, 0, _renderUtility.camera);
            }

            _renderUtility.camera.Render();
            _previewImage.MarkDirtyRepaint();
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Cleans up rendering resources.
        /// </summary>
        public void Cleanup()
        {
            if (_previewTexture != null)
            {
                _previewImage.image = null;
                _previewTexture.Release();
                Object.DestroyImmediate(_previewTexture);
                _previewTexture = null;
            }

            _cachedRenderers.Clear();

            if (_previewObjectInstance != null)
            {
                Object.DestroyImmediate(_previewObjectInstance);
                _previewObjectInstance = null;
            }

            if (_gridMesh != null)
            {
                Object.DestroyImmediate(_gridMesh);
                _gridMesh = null;
            }

            if (_gridMaterial != null)
            {
                Object.DestroyImmediate(_gridMaterial);
                _gridMaterial = null;
            }

            _renderUtility?.Cleanup();
            _renderUtility = null;
        }

        /// <summary>
        /// Sets the currently selected base locomotion clip (without raising change events).
        /// </summary>
        public void SetSelectedBaseMotion(AnimationClip clip)
        {
            if (_baseMotionObjectField != null)
            {
                _baseMotionObjectField.SetValueWithoutNotify(clip);
            }
        }

        /// <summary>
        /// Sets the independent model override toggle state (without raising change events).
        /// </summary>
        public void SetOverrideModelState(bool isOverride)
        {
            _overrideModelToggle?.SetValueWithoutNotify(isOverride);
            if (_modelObjectField != null)
            {
                _modelObjectField.tooltip = isOverride
                    ? "Preview Model Prefab [Asset Override Enabled: Bound to this Montage SO]"
                    : "Preview Model Prefab [Global Shared Model across all montages]";
            }
        }

        /// <summary>
        /// Sets the currently selected preview upper-body mask asset (without raising change events).
        /// </summary>
        public void SetSelectedUpperBodyMask(AvatarMask mask)
        {
            if (_upperBodyMaskObjectField != null)
            {
                _upperBodyMaskObjectField.SetValueWithoutNotify(mask);
            }
        }

        #endregion

        #region Private View Settings Panel Logic

        private void BuildPreviewSettingsPanel()
        {
            _previewSettingsPanel = new VisualElement();
            _previewSettingsPanel.AddToClassList("montage-viewport-view-panel");
            _previewSettingsPanel.style.display = DisplayStyle.None;

            // Item 1: asset override toggle
            var overrideRow = new VisualElement();
            overrideRow.AddToClassList("montage-view-panel-row");
            var overrideLbl = new Label("Asset Override");
            overrideLbl.AddToClassList("montage-view-panel-label");
            overrideLbl.tooltip = "Enable per-asset model binding. When checked, the preview model and upper-body mask are bound to this Montage asset rather than shared globally.";
            overrideRow.Add(overrideLbl);

            _overrideModelToggle = new Toggle();
            _overrideModelToggle.tooltip = "Toggle between Global Shared Model and Asset-Specific Model";
            _overrideModelToggle.RegisterValueChangedCallback(evt =>
            {
                OnOverrideModelToggled?.Invoke(evt.newValue);
            });
            overrideRow.Add(_overrideModelToggle);
            _previewSettingsPanel.Add(overrideRow);

            // Divider
            var divider = new VisualElement();
            divider.AddToClassList("montage-view-panel-divider");
            _previewSettingsPanel.Add(divider);

            // Item 2: Model preview model
            var modelRow = new VisualElement();
            modelRow.AddToClassList("montage-view-panel-row");
            var modelLbl = new Label("Model");
            modelLbl.AddToClassList("montage-view-panel-label");
            modelLbl.tooltip = "Preview Model Prefab";
            modelRow.Add(modelLbl);

            _modelObjectField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false
            };
            _modelObjectField.AddToClassList("montage-view-panel-field");
            _modelObjectField.RegisterValueChangedCallback(evt =>
            {
                var newModel = evt.newValue as GameObject;
                OnPreviewModelChanged?.Invoke(newModel);
            });
            modelRow.Add(_modelObjectField);
            _previewSettingsPanel.Add(modelRow);

            // Item 3: Base Motion base locomotion
            var baseMotionRow = new VisualElement();
            baseMotionRow.AddToClassList("montage-view-panel-row");
            var baseMotionLbl = new Label("Base Motion");
            baseMotionLbl.AddToClassList("montage-view-panel-label");
            baseMotionLbl.tooltip = "Base Locomotion Animation (Looping Idle/Run/Walk under upper-body montages. Set to None for static T-Pose)";
            baseMotionRow.Add(baseMotionLbl);

            _baseMotionObjectField = new ObjectField
            {
                objectType = typeof(AnimationClip),
                allowSceneObjects = false
            };
            _baseMotionObjectField.AddToClassList("montage-view-panel-field");
            _baseMotionObjectField.RegisterValueChangedCallback(evt =>
            {
                var newClip = evt.newValue as AnimationClip;
                OnPreviewBaseMotionChanged?.Invoke(newClip);
            });
            baseMotionRow.Add(_baseMotionObjectField);
            _previewSettingsPanel.Add(baseMotionRow);

            // Item 4: UpperBody Mask
            var maskRow = new VisualElement();
            maskRow.AddToClassList("montage-view-panel-row");
            var maskLbl = new Label("Upper Mask");
            maskLbl.AddToClassList("montage-view-panel-label");
            maskLbl.tooltip = "Custom AvatarMask for UpperBody channel. Optional. If set, procedural spine decoupling will be bypassed.";
            maskRow.Add(maskLbl);

            _upperBodyMaskObjectField = new ObjectField
            {
                objectType = typeof(AvatarMask),
                allowSceneObjects = false
            };
            _upperBodyMaskObjectField.AddToClassList("montage-view-panel-field");
            _upperBodyMaskObjectField.RegisterValueChangedCallback(evt =>
            {
                var newMask = evt.newValue as AvatarMask;
                OnPreviewUpperBodyMaskChanged?.Invoke(newMask);
            });
            maskRow.Add(_upperBodyMaskObjectField);
            _previewSettingsPanel.Add(maskRow);
        }

        private void BuildViewSettingsPanel()
        {
            _viewSettingsPanel = new VisualElement();
            _viewSettingsPanel.AddToClassList("montage-viewport-view-panel");
            _viewSettingsPanel.style.minWidth = 140;
            _viewSettingsPanel.style.display = DisplayStyle.None;

            // Multi-select toggle list similar to the Scene view
            _rootMotionToggle = new Toggle("Root Motion");
            _rootMotionToggle.AddToClassList("montage-view-toggle-item");
            _rootMotionToggle.tooltip = "Toggle root displacement preview in 3D viewport";
            _rootMotionToggle.RegisterValueChangedCallback(evt =>
            {
                OnRootMotionToggled?.Invoke(evt.newValue);
            });
            _viewSettingsPanel.Add(_rootMotionToggle);

            _footIKToggle = new Toggle("Foot IK");
            _footIKToggle.value = true;
            _footIKToggle.AddToClassList("montage-view-toggle-item");
            _footIKToggle.tooltip = "Toggle feet ground adaptation preview in 3D viewport";
            _footIKToggle.RegisterValueChangedCallback(evt =>
            {
                OnFootIKToggled?.Invoke(evt.newValue);
            });
            _viewSettingsPanel.Add(_footIKToggle);

            _gridToggle = new Toggle("Ground Grid");
            _gridToggle.value = _showGroundGrid;
            _gridToggle.AddToClassList("montage-view-toggle-item");
            _gridToggle.tooltip = "Toggle ground reference grid in 3D viewport";
            _gridToggle.RegisterValueChangedCallback(evt =>
            {
                _showGroundGrid = evt.newValue;
                RenderImmediate();
            });
            _viewSettingsPanel.Add(_gridToggle);

            _litToggle = new Toggle("Lit Shading");
            _litToggle.value = !_isUnlitMode;
            _litToggle.AddToClassList("montage-view-toggle-item");
            _litToggle.tooltip = "Toggle between studio lighting and flat unlit shading";
            _litToggle.RegisterValueChangedCallback(evt =>
            {
                _isUnlitMode = !evt.newValue;
                ApplyLightingMode(_isUnlitMode);
                RenderImmediate();
            });
            _viewSettingsPanel.Add(_litToggle);

            // Divider
            var divider = new VisualElement();
            divider.AddToClassList("montage-view-panel-divider");
            _viewSettingsPanel.Add(divider);

            // Reset camera button
            var resetCamBtn = new Button(() => ResetCamera(true)) { text = "Reset Camera" };
            resetCamBtn.AddToClassList("montage-view-panel-button");
            resetCamBtn.tooltip = "Reset camera position and framing to focus on model";
            _viewSettingsPanel.Add(resetCamBtn);
        }

        private void TogglePreviewSettingsPanel()
        {
            if (_previewSettingsPanel == null) return;
            bool isVisible = _previewSettingsPanel.style.display == DisplayStyle.Flex;
            if (isVisible)
            {
                CloseAllPanels();
            }
            else
            {
                CloseAllPanels();
                _previewSettingsPanel.style.display = DisplayStyle.Flex;
                _previewMenuBtn?.AddToClassList("montage-overlay-btn-active");
            }
        }

        private void ToggleViewSettingsPanel()
        {
            if (_viewSettingsPanel == null) return;
            bool isVisible = _viewSettingsPanel.style.display == DisplayStyle.Flex;
            if (isVisible)
            {
                CloseAllPanels();
            }
            else
            {
                CloseAllPanels();
                _viewSettingsPanel.style.display = DisplayStyle.Flex;
                _viewMenuBtn?.AddToClassList("montage-overlay-btn-active");
            }
        }

        private void CloseAllPanels()
        {
            if (_previewSettingsPanel != null)
            {
                _previewSettingsPanel.style.display = DisplayStyle.None;
            }
            if (_viewSettingsPanel != null)
            {
                _viewSettingsPanel.style.display = DisplayStyle.None;
            }
            _previewMenuBtn?.RemoveFromClassList("montage-overlay-btn-active");
            _viewMenuBtn?.RemoveFromClassList("montage-overlay-btn-active");
        }

        #endregion

        #region Private Camera and Rendering Logic

        private void ResetCamera(bool resetTarget)
        {
            if (resetTarget)
            {
                _cameraTarget = new Vector3(0f, 1f, 0f);
                _cameraDistance = 4.5f;
                _cameraYaw = 225f; // 45-degree front view (180° + 45°)
                _cameraPitch = 12f; // Slight downward tilt (12° pitch, enhances 3D depth perception)
            }
            RenderImmediate();
        }

        private void UpdateCameraTransform()
        {
            if (_renderUtility == null)
            {
                return;
            }

            Quaternion rot = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
            Vector3 offset = rot * new Vector3(0f, 0f, -_cameraDistance);
            Vector3 pos = _cameraTarget + offset;

            _renderUtility.camera.transform.SetPositionAndRotation(pos, rot);
        }

        private void HandleFPSMovement()
        {
            if (!_isRightMouseButtonHeld || _pressedKeys.Count == 0 || _renderUtility == null)
            {
                return;
            }

            Quaternion rot = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
            Vector3 move = Vector3.zero;

            if (_pressedKeys.Contains(KeyCode.W)) move += rot * Vector3.forward;
            if (_pressedKeys.Contains(KeyCode.S)) move += rot * Vector3.back;
            if (_pressedKeys.Contains(KeyCode.A)) move += rot * Vector3.left;
            if (_pressedKeys.Contains(KeyCode.D)) move += rot * Vector3.right;
            if (_pressedKeys.Contains(KeyCode.Space)) move += Vector3.up;
            if (_pressedKeys.Contains(KeyCode.LeftControl)) move += Vector3.down;

            if (move != Vector3.zero)
            {
                _cameraTarget += move.normalized * (3.5f * 0.016f);
            }
        }

        private void ToggleRootMotion()
        {
            if (_rootMotionToggle == null) return;
            _rootMotionToggle.value = !_rootMotionToggle.value;
        }

        private void ToggleFootIK()
        {
            if (_footIKToggle == null) return;
            _footIKToggle.value = !_footIKToggle.value;
        }

        private void ToggleGroundGrid()
        {
            _showGroundGrid = !_showGroundGrid;
            RenderImmediate();
        }

        private void EnsureGridResources()
        {
            if (_gridMesh == null)
            {
                _gridMesh = CreateGroundGridMesh(20f, 1.0f);
            }

            if (_gridMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _gridMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    _gridMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _gridMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _gridMaterial.SetInt("_ZWrite", 0);
                    _gridMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                }
            }
        }

        private Mesh CreateGroundGridMesh(float size = 20f, float step = 1.0f)
        {
            var mesh = new Mesh { name = "GroundGridMesh", hideFlags = HideFlags.HideAndDontSave };
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();

            int halfCount = Mathf.RoundToInt((size * 0.5f) / step);
            float actualHalfSize = halfCount * step;

            Color subGridColor = new Color(0.42f, 0.42f, 0.42f, 0.20f);
            Color mainGridColor = new Color(0.65f, 0.65f, 0.65f, 0.45f);
            Color xAxisColor = new Color(0.92f, 0.35f, 0.35f, 0.75f);
            Color zAxisColor = new Color(0.35f, 0.60f, 0.95f, 0.75f);

            int vertexIndex = 0;

            for (int i = -halfCount; i <= halfCount; i++)
            {
                float z = i * step;
                Color lineCol = (i == 0) ? xAxisColor : ((i % 5 == 0) ? mainGridColor : subGridColor);

                float distRatio = Mathf.Abs(z) / actualHalfSize;
                float alphaMult = Mathf.Clamp01(1f - distRatio * 0.35f);

                Color startCol = lineCol; startCol.a *= 0.08f;
                Color midCol = lineCol; midCol.a *= alphaMult;

                vertices.Add(new Vector3(-actualHalfSize, 0f, z)); colors.Add(startCol);
                vertices.Add(new Vector3(-actualHalfSize * 0.5f, 0f, z)); colors.Add(midCol);
                vertices.Add(new Vector3(actualHalfSize * 0.5f, 0f, z)); colors.Add(midCol);
                vertices.Add(new Vector3(actualHalfSize, 0f, z)); colors.Add(startCol);

                indices.Add(vertexIndex); indices.Add(vertexIndex + 1);
                indices.Add(vertexIndex + 1); indices.Add(vertexIndex + 2);
                indices.Add(vertexIndex + 2); indices.Add(vertexIndex + 3);
                vertexIndex += 4;
            }

            for (int i = -halfCount; i <= halfCount; i++)
            {
                float x = i * step;
                Color lineCol = (i == 0) ? zAxisColor : ((i % 5 == 0) ? mainGridColor : subGridColor);

                float distRatio = Mathf.Abs(x) / actualHalfSize;
                float alphaMult = Mathf.Clamp01(1f - distRatio * 0.35f);

                Color startCol = lineCol; startCol.a *= 0.08f;
                Color midCol = lineCol; midCol.a *= alphaMult;

                vertices.Add(new Vector3(x, 0f, -actualHalfSize)); colors.Add(startCol);
                vertices.Add(new Vector3(x, 0f, -actualHalfSize * 0.5f)); colors.Add(midCol);
                vertices.Add(new Vector3(x, 0f, actualHalfSize * 0.5f)); colors.Add(midCol);
                vertices.Add(new Vector3(x, 0f, actualHalfSize)); colors.Add(startCol);

                indices.Add(vertexIndex); indices.Add(vertexIndex + 1);
                indices.Add(vertexIndex + 1); indices.Add(vertexIndex + 2);
                indices.Add(vertexIndex + 2); indices.Add(vertexIndex + 3);
                vertexIndex += 4;
            }

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
            return mesh;
        }

        private void ToggleUnlit()
        {
            _isUnlitMode = !_isUnlitMode;
            if (_litToggle != null) _litToggle.SetValueWithoutNotify(!_isUnlitMode);
            ApplyLightingMode(_isUnlitMode);
            RenderImmediate();
        }

        private void ApplyLightingMode(bool unlit)
        {
            if (_renderUtility == null) return;

            if (unlit)
            {
                foreach (var light in _renderUtility.lights) light.enabled = false;
                _renderUtility.ambientColor = Color.white;
            }
            else
            {
                if (_renderUtility.lights.Length > 0)
                {
                    Light keyLight = _renderUtility.lights[0];
                    keyLight.enabled = true;
                    keyLight.intensity = 1.15f;
                    keyLight.color = new Color(0.96f, 0.96f, 0.98f);
                    keyLight.transform.rotation = Quaternion.Euler(38f, 135f, 0f);
                }

                if (_renderUtility.lights.Length > 1)
                {
                    Light rimLight = _renderUtility.lights[1];
                    rimLight.enabled = true;
                    rimLight.intensity = 0.55f;
                    rimLight.color = new Color(0.70f, 0.80f, 0.95f);
                    rimLight.transform.rotation = Quaternion.Euler(-20f, -45f, 0f);
                }

                _renderUtility.ambientColor = new Color(0.26f, 0.27f, 0.29f, 1f);
            }
        }

        private void ReinitializeTexture()
        {
            if (_previewTexture != null)
            {
                _previewImage.image = null;
                _previewTexture.Release();
                Object.DestroyImmediate(_previewTexture);
                _previewTexture = null;
            }

            if (_renderUtility == null) return;

            float w = _previewImage.contentRect.width;
            float h = _previewImage.contentRect.height;
            if (w <= 0 || h <= 0 || float.IsNaN(w) || float.IsNaN(h))
            {
                w = 400;
                h = 300;
            }

            float ppp = EditorGUIUtility.pixelsPerPoint;
            int targetWidth = Mathf.Max(64, Mathf.RoundToInt(w * ppp));
            int targetHeight = Mathf.Max(64, Mathf.RoundToInt(h * ppp));

            _previewTexture = new RenderTexture(targetWidth, targetHeight, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear
            };

            _previewImage.image = _previewTexture;
            _renderUtility.camera.targetTexture = _previewTexture;
            _renderUtility.camera.aspect = (float)targetWidth / targetHeight;
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            ReinitializeTexture();
            RenderImmediate();
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button == 0)
            {
                bool isAnyPanelOpen = (_previewSettingsPanel != null && _previewSettingsPanel.style.display == DisplayStyle.Flex) ||
                                      (_viewSettingsPanel != null && _viewSettingsPanel.style.display == DisplayStyle.Flex);
                if (isAnyPanelOpen)
                {
                    bool insidePreview = _previewSettingsPanel != null && _previewSettingsPanel.worldBound.Contains(evt.mousePosition);
                    bool insideView = _viewSettingsPanel != null && _viewSettingsPanel.worldBound.Contains(evt.mousePosition);
                    bool insidePreviewBtn = _previewMenuBtn != null && _previewMenuBtn.worldBound.Contains(evt.mousePosition);
                    bool insideViewBtn = _viewMenuBtn != null && _viewMenuBtn.worldBound.Contains(evt.mousePosition);

                    if (!insidePreview && !insideView && !insidePreviewBtn && !insideViewBtn)
                    {
                        CloseAllPanels();
                    }
                }
            }
            else if (evt.button == 1)
            {
                _isRightMouseButtonHeld = true;
                _previewImage.Focus();
                this.CaptureMouse();
                RenderImmediate();
                evt.StopPropagation();
            }
            else if (evt.button == 2)
            {
                _isMiddleMouseButtonHeld = true;
                this.CaptureMouse();
                RenderImmediate();
                evt.StopPropagation();
            }
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (_isRightMouseButtonHeld)
            {
                _cameraYaw += evt.mouseDelta.x * 0.25f;
                _cameraPitch = Mathf.Clamp(_cameraPitch + evt.mouseDelta.y * 0.25f, -89f, 89f);
                RenderImmediate();
                evt.StopPropagation();
            }
            else if (_isMiddleMouseButtonHeld && _renderUtility != null)
            {
                Vector3 camForward = (_cameraTarget - _renderUtility.camera.transform.position).normalized;
                Vector3 camRight = Vector3.Cross(Vector3.up, camForward).normalized;
                Vector3 camUp = Vector3.Cross(camForward, camRight).normalized;
                _cameraTarget += (-camRight * evt.mouseDelta.x + camUp * evt.mouseDelta.y) * 0.005f;
                RenderImmediate();
                evt.StopPropagation();
            }
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (evt.button == 1) _isRightMouseButtonHeld = false;
            if (evt.button == 2) _isMiddleMouseButtonHeld = false;
            if (!_isRightMouseButtonHeld && !_isMiddleMouseButtonHeld && this.HasMouseCapture())
            {
                this.ReleaseMouse();
            }
            evt.StopPropagation();
        }

        private void OnMouseLeave(MouseLeaveEvent evt)
        {
            if (!_isRightMouseButtonHeld && !_isMiddleMouseButtonHeld)
            {
                _pressedKeys.Clear();
                if (this.HasMouseCapture())
                {
                    this.ReleaseMouse();
                }
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            _cameraDistance = Mathf.Clamp(_cameraDistance + evt.delta.y * 0.15f, 0.5f, 50f);
            RenderImmediate();
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            _pressedKeys.Add(evt.keyCode);
        }

        private void OnKeyUp(KeyUpEvent evt)
        {
            _pressedKeys.Remove(evt.keyCode);
        }

        #endregion
    }
}
