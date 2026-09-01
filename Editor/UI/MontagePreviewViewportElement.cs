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
    /// 纯净 3D 预览视口元素（MontagePreviewViewportElement）。
    /// 封装完整的 PreviewRenderUtility 渲染管线、FPS 漫游相机、轨道观察相机与地面参考网格。
    /// </summary>
    public class MontagePreviewViewportElement : VisualElement
    {
        #region 私有字段

        private MontageSequenceSO _targetAsset;
        private PreviewRenderUtility _renderUtility;
        private RenderTexture _previewTexture;
        private Image _previewImage;
        private GameObject _previewObjectInstance;

        // 相机参数
        private float _cameraDistance = 4.5f;
        private Vector3 _cameraTarget = new Vector3(0f, 1f, 0f);
        private float _cameraYaw = 180f;
        private float _cameraPitch = 0f;
        private bool _isRightMouseButtonHeld;
        private bool _isMiddleMouseButtonHeld;
        private readonly HashSet<KeyCode> _pressedKeys = new();

        private readonly List<Renderer> _cachedRenderers = new();

        // 视口光照与地面网格
        private ObjectField _modelObjectField;
        private Button _rootMotionBtn;
        private Button _footIKBtn;
        private Button _toggleUnlitBtn;
        private Mesh _gridMesh;
        private Material _gridMaterial;
        private bool _showGroundGrid = true;
        private bool _isUnlitMode = false;

        #endregion

        #region 公共事件

        /// <summary>
        /// 当用户在视口中切换或拖入新的预览模型时触发。
        /// </summary>
        public event Action<GameObject> OnPreviewModelChanged;

        /// <summary>
        /// 当用户切换预览根运动（Root Motion）时触发。
        /// </summary>
        public event Action<bool> OnRootMotionToggled;

        /// <summary>
        /// 当用户切换预览足部 IK（Foot IK）时触发。
        /// </summary>
        public event Action<bool> OnFootIKToggled;

        #endregion

        #region 公共属性

        public PreviewRenderUtility RenderUtility => _renderUtility;
        public GameObject PreviewObjectInstance => _previewObjectInstance;
        public bool IsInteracting => _isRightMouseButtonHeld || _isMiddleMouseButtonHeld || _pressedKeys.Count > 0;

        #endregion

        #region 构造方法

        public MontagePreviewViewportElement()
        {
            AddToClassList("montage-viewport-container");

            _previewImage = new Image();
            _previewImage.AddToClassList("montage-preview-image");
            _previewImage.scaleMode = ScaleMode.StretchToFill;
            _previewImage.focusable = true;
            _previewImage.tooltip = "Right Click + Drag: Rotate Camera\nRight Click + WASD: FPS Fly Camera\nMiddle Click + Drag: Pan Camera\nScroll: Zoom";
            Add(_previewImage);

            // 右上角覆盖快捷工具条
            var overlayBar = new VisualElement();
            overlayBar.AddToClassList("montage-viewport-overlay");

            // 1. 预览模型快捷选择框
            var modelBox = new VisualElement();
            modelBox.AddToClassList("montage-overlay-model-box");

            var modelLbl = new Label("Model");
            modelLbl.AddToClassList("montage-overlay-model-label");
            modelBox.Add(modelLbl);

            _modelObjectField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false
            };
            _modelObjectField.AddToClassList("montage-overlay-model-field");
            _modelObjectField.tooltip = "Preview Model Prefab (Stored in EditorPrefs, shared across all montages)";
            _modelObjectField.RegisterValueChangedCallback(evt =>
            {
                var newModel = evt.newValue as GameObject;
                OnPreviewModelChanged?.Invoke(newModel);
            });
            modelBox.Add(_modelObjectField);

            overlayBar.Add(modelBox);

            // 2. 动画设置按钮组 (Root Motion & Foot IK)
            var animSeparator = new VisualElement();
            animSeparator.AddToClassList("montage-overlay-separator");
            overlayBar.Add(animSeparator);

            _rootMotionBtn = new Button(ToggleRootMotion) { text = "Root" };
            _rootMotionBtn.AddToClassList("montage-overlay-btn");
            _rootMotionBtn.tooltip = "Root Motion (Preview only: Toggle root displacement in 3D viewport)";
            overlayBar.Add(_rootMotionBtn);

            _footIKBtn = new Button(ToggleFootIK) { text = "Foot IK" };
            _footIKBtn.AddToClassList("montage-overlay-btn");
            _footIKBtn.tooltip = "Foot IK (Preview only: Toggle feet ground adaptation in 3D viewport)";
            overlayBar.Add(_footIKBtn);

            // 3. 相机与视图控制按钮组
            var vpSeparator = new VisualElement();
            vpSeparator.AddToClassList("montage-overlay-separator");
            overlayBar.Add(vpSeparator);

            var resetCamBtn = new Button(() => ResetCamera(true)) { text = "Cam" };
            resetCamBtn.AddToClassList("montage-overlay-btn");
            resetCamBtn.tooltip = "Reset Camera Position";
            overlayBar.Add(resetCamBtn);

            var toggleGridBtn = new Button(ToggleGroundGrid) { text = "Grid" };
            toggleGridBtn.AddToClassList("montage-overlay-btn");
            toggleGridBtn.tooltip = "Toggle Ground Grid";
            overlayBar.Add(toggleGridBtn);

            _toggleUnlitBtn = new Button(ToggleUnlit) { text = "Lit" };
            _toggleUnlitBtn.AddToClassList("montage-overlay-btn");
            _toggleUnlitBtn.tooltip = "Toggle Studio Lit / Pure Flat Lighting";
            overlayBar.Add(_toggleUnlitBtn);

            Add(overlayBar);

            // 注册视口交互
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

        #region 公共生命周期与方法

        /// <summary>
        /// 设置当前视口模型选择框展示的 Prefab 引用（不触发回调通知）。
        /// </summary>
        public void SetSelectedModelPrefab(GameObject prefab)
        {
            _modelObjectField?.SetValueWithoutNotify(prefab);
        }

        /// <summary>
        /// 更新根运动（Root Motion）按钮的高亮激活外观。
        /// </summary>
        public void SetRootMotionState(bool enabled)
        {
            if (_rootMotionBtn == null) return;
            if (enabled)
            {
                _rootMotionBtn.AddToClassList("montage-overlay-btn-active");
            }
            else
            {
                _rootMotionBtn.RemoveFromClassList("montage-overlay-btn-active");
            }
        }

        /// <summary>
        /// 更新足部 IK（Foot IK）按钮的高亮激活外观。
        /// </summary>
        public void SetFootIKState(bool enabled)
        {
            if (_footIKBtn == null) return;
            if (enabled)
            {
                _footIKBtn.AddToClassList("montage-overlay-btn-active");
            }
            else
            {
                _footIKBtn.RemoveFromClassList("montage-overlay-btn-active");
            }
        }

        /// <summary>
        /// 初始化 3D 渲染视口与预览模型。
        /// </summary>
        public void Initialize(MontageSequenceSO asset, GameObject previewModelPrefab, ref GameObject outPreviewObject)
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
            cam.backgroundColor = new Color(0.14f, 0.145f, 0.155f, 1f); // Blender 经典中性深炭灰 (#242528)
            cam.allowHDR = true;
            cam.allowMSAA = true;
            cam.renderingPath = RenderingPath.Forward;

            _renderUtility.ambientColor = new Color(0.26f, 0.27f, 0.29f, 1f);
            foreach (var light in _renderUtility.lights)
            {
                light.enabled = true;
            }

            // 主光 (Key Light)
            if (_renderUtility.lights.Length > 0)
            {
                Light keyLight = _renderUtility.lights[0];
                keyLight.intensity = 1.15f;
                keyLight.color = new Color(0.96f, 0.96f, 0.98f);
                keyLight.transform.rotation = Quaternion.Euler(38f, 135f, 0f);
            }

            // 轮廓背光 (Rim Light)
            if (_renderUtility.lights.Length > 1)
            {
                Light rimLight = _renderUtility.lights[1];
                rimLight.intensity = 0.55f;
                rimLight.color = new Color(0.70f, 0.80f, 0.95f);
                rimLight.transform.rotation = Quaternion.Euler(-20f, -45f, 0f);
            }

            EnsureGridResources();

            // 实例化预览模型
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
        /// 每帧更新相机平移与渲染贴图。
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
        /// 立即重绘视口。
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
        /// 清理渲染资源。
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

        #endregion

        #region 私有相机与渲染逻辑

        private void ResetCamera(bool resetTarget)
        {
            if (resetTarget)
            {
                _cameraTarget = new Vector3(0f, 1f, 0f);
                _cameraDistance = 4.5f;
                _cameraYaw = 180f;
                _cameraPitch = 0f;
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
            if (_rootMotionBtn == null) return;
            bool newState = !_rootMotionBtn.ClassListContains("montage-overlay-btn-active");
            SetRootMotionState(newState);
            OnRootMotionToggled?.Invoke(newState);
        }

        private void ToggleFootIK()
        {
            if (_footIKBtn == null) return;
            bool newState = !_footIKBtn.ClassListContains("montage-overlay-btn-active");
            SetFootIKState(newState);
            OnFootIKToggled?.Invoke(newState);
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
                if (_toggleUnlitBtn != null)
                {
                    _toggleUnlitBtn.text = "Flat";
                    _toggleUnlitBtn.tooltip = "Current: Flat / Unlit Mode";
                }
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
                if (_toggleUnlitBtn != null)
                {
                    _toggleUnlitBtn.text = "Lit";
                    _toggleUnlitBtn.tooltip = "Current: Studio Lit Mode";
                }
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
            if (evt.button == 1)
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
