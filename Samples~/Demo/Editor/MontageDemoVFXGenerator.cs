using System.IO;
using UnityEditor;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage.Demo.Editor
{
    /// <summary>
    /// 蒙太奇演示视效 (VFX) 与道具预制件自动生成器。
    /// 使用纯 Unity 原生粒子系统（ParticleSystem）与几何体构建，
    /// 零外部第三方侵权素材依赖，跨渲染管线（URP / Built-in / HDRP）自适应 Shader，彻底杜绝粉红材质。
    /// </summary>
    public static class MontageDemoVFXGenerator
    {
        #region 动态路径解析

        private static string _cachedBaseDemoDir;

        private static string BaseDemoDir
        {
            get
            {
                if (string.IsNullOrEmpty(_cachedBaseDemoDir))
                {
                    _cachedBaseDemoDir = ResolveDemoRootDir();
                }
                return _cachedBaseDemoDir;
            }
        }

        private static string PrefabDir => $"{BaseDemoDir}/Prefabs";
        private static string MatDir => $"{BaseDemoDir}/Materials";

        private static string ResolveDemoRootDir()
        {
            string[] guids = AssetDatabase.FindAssets("MontageDemoVFXGenerator t:MonoScript");
            if (guids.Length > 0)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                string editorDir = Path.GetDirectoryName(scriptPath)?.Replace('\\', '/');
                string demoDir = Path.GetDirectoryName(editorDir)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(demoDir))
                {
                    return demoDir;
                }
            }
            return "Assets/CwcPlugins/CwcMontage/Demo";
        }

        #endregion

        #region 菜单入口与自动初始化

        [MenuItem("Tools/CwcMontage/Generate Demo VFX Prefabs", false, 110)]
        public static void GenerateAllMenu()
        {
            GenerateAll(true);
        }

        [InitializeOnLoadMethod]
        private static void AutoCheckAndGenerate()
        {
            // 检查关键 Prefab 是否已存在，若不存在则首次自动静默生成
            string checkFile = $"{PrefabDir}/VFX_HitSparks.prefab";
            if (!File.Exists(checkFile))
            {
                EditorApplication.delayCall += () => GenerateAll(false);
            }
        }

        #endregion

        #region 公共构建方法

        /// <summary>
        /// 全量构建演示用 VFX 预制件与武器道具。
        /// </summary>
        public static void GenerateAll(bool logFeedback = true)
        {
            EnsureDirectories();
            var (matAdditive, matAlpha, matSword) = EnsureMaterials();

            CreateHitSparksPrefab(matAdditive);
            CreateSlashArcPrefab(matAdditive);
            CreateDustPuffPrefab(matAlpha);
            CreatePunchImpactPrefab(matAdditive);
            CreateSwordPropPrefab(matSword);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (logFeedback)
            {
                Debug.Log($"<color=#44ff88>[CwcMontage] 演示特效与武器 Prefab 已成功生成至：{PrefabDir}</color>");
            }
        }

        #endregion

        #region 资源目录与材质初始化

        private static void EnsureDirectories()
        {
            if (!Directory.Exists(BaseDemoDir))
            {
                Directory.CreateDirectory(BaseDemoDir);
            }
            if (!Directory.Exists(PrefabDir))
            {
                Directory.CreateDirectory(PrefabDir);
            }
            if (!Directory.Exists(MatDir))
            {
                Directory.CreateDirectory(MatDir);
            }
            AssetDatabase.Refresh();
        }

        private static (Material additive, Material alpha, Material sword) EnsureMaterials()
        {
            // 智能查找兼容的粒子 Shader
            Shader particleUnlitShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                                         ?? Shader.Find("Particles/Standard Unlit")
                                         ?? Shader.Find("Sprites/Default");

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit")
                               ?? Shader.Find("Standard");

            // 1. 发光叠加材质 (Additive)
            string addPath = $"{MatDir}/Mat_Demo_VFX_Additive.mat";
            var matAdd = AssetDatabase.LoadAssetAtPath<Material>(addPath);
            if (matAdd == null)
            {
                matAdd = new Material(particleUnlitShader);
                ConfigureAdditiveMaterial(matAdd);
                AssetDatabase.CreateAsset(matAdd, addPath);
            }

            // 2. 半透明烟雾材质 (AlphaBlended)
            string alphaPath = $"{MatDir}/Mat_Demo_VFX_AlphaBlended.mat";
            var matAlpha = AssetDatabase.LoadAssetAtPath<Material>(alphaPath);
            if (matAlpha == null)
            {
                matAlpha = new Material(particleUnlitShader);
                ConfigureAlphaMaterial(matAlpha);
                AssetDatabase.CreateAsset(matAlpha, alphaPath);
            }

            // 3. 长剑金属材质 (Sword)
            string swordPath = $"{MatDir}/Mat_Demo_Prop_Sword.mat";
            var matSword = AssetDatabase.LoadAssetAtPath<Material>(swordPath);
            if (matSword == null)
            {
                matSword = new Material(litShader);
                matSword.color = new Color(0.85f, 0.88f, 0.92f);
                if (matSword.HasProperty("_Metallic")) matSword.SetFloat("_Metallic", 0.85f);
                if (matSword.HasProperty("_Smoothness")) matSword.SetFloat("_Smoothness", 0.75f);
                AssetDatabase.CreateAsset(matSword, swordPath);
            }

            return (matAdd, matAlpha, matSword);
        }

        private static void ConfigureAdditiveMaterial(Material mat)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1); // Transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1); // Additive
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 2); // Built-in Additive
            mat.SetColor("_Color", new Color(1f, 0.95f, 0.8f, 1f));
        }

        private static void ConfigureAlphaMaterial(Material mat)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1); // Transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0); // Alpha
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 0); // Built-in Cutout/Fade
            mat.SetColor("_Color", new Color(0.85f, 0.85f, 0.85f, 0.5f));
        }

        #endregion

        #region 特效预制件生成

        private static void CreateHitSparksPrefab(Material mat)
        {
            string path = $"{PrefabDir}/VFX_HitSparks.prefab";
            var go = new GameObject("VFX_HitSparks");
            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();

            // Main
            var main = ps.main;
            main.duration = 0.35f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.28f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.gravityModifier = 1.0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;

            // Emission (爆发式)
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 25) });

            // Shape (圆锥发散)
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 40f;
            shape.radius = 0.05f;

            // Color Over Lifetime (亮白 -> 明黄 -> 橙红 -> 渐隐)
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.8f, 0.1f), 0.4f), new GradientColorKey(new Color(1f, 0.25f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = grad;

            // Size Over Lifetime (由大到尖细)
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            // Renderer (拉伸火星飞线)
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.12f;
            renderer.lengthScale = 2.0f;
            renderer.sharedMaterial = mat;

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        private static void CreateSlashArcPrefab(Material mat)
        {
            string path = $"{PrefabDir}/VFX_SlashArc.prefab";
            var go = new GameObject("VFX_SlashArc");
            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();

            // Main
            var main = ps.main;
            main.duration = 0.35f;
            main.loop = false;
            main.startLifetime = 0.22f;
            main.startSpeed = 0.5f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = true;

            // Emission (圆弧瞬间刷出)
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

            // Shape (120度圆弧刀光)
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.0f;
            shape.arc = 120f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

            // Color Over Lifetime (青蓝微光或金黄剑气)
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.8f, 0.95f, 1f), 0f), new GradientColorKey(new Color(0.2f, 0.75f, 1f), 0.6f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = grad;

            // Size Over Lifetime
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f),
                new Keyframe(0.4f, 1.2f),
                new Keyframe(1f, 0f)
            ));

            renderer.sharedMaterial = mat;

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        private static void CreateDustPuffPrefab(Material mat)
        {
            string path = $"{PrefabDir}/VFX_DustPuff.prefab";
            var go = new GameObject("VFX_DustPuff");
            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();

            // Main
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
            main.gravityModifier = -0.08f; // 轻微向上浮起
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;

            // Emission
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });

            // Shape (半球向四周扩散)
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.25f;

            // Color Over Lifetime (灰白尘土柔和淡出)
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.78f, 0.75f, 0.72f), 0f), new GradientColorKey(new Color(0.65f, 0.62f, 0.6f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.4f, 0.25f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = grad;

            // Size Over Lifetime (尘土扩散变大)
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.3f, 1f, 1.4f));

            renderer.sharedMaterial = mat;

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        private static void CreatePunchImpactPrefab(Material mat)
        {
            string path = $"{PrefabDir}/VFX_PunchImpact.prefab";
            var go = new GameObject("VFX_PunchImpact");
            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();

            // Main
            var main = ps.main;
            main.duration = 0.25f;
            main.loop = false;
            main.startLifetime = 0.18f;
            main.startSpeed = 0.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = true;

            // Emission
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

            // Shape (小球核心)
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;

            // Color Over Lifetime (强暖色冲击)
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.05f), 0.5f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = grad;

            // Size Over Lifetime (从拳心极速炸开)
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.2f),
                new Keyframe(1f, 1.6f)
            ));

            renderer.sharedMaterial = mat;

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        private static void CreateSwordPropPrefab(Material mat)
        {
            string path = $"{PrefabDir}/Prop_Sword.prefab";
            var root = new GameObject("Prop_Sword");

            // 1. 剑身 (Blade)
            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Blade";
            blade.transform.SetParent(root.transform);
            blade.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            blade.transform.localRotation = Quaternion.identity;
            blade.transform.localScale = new Vector3(0.035f, 0.75f, 0.015f);
            Object.DestroyImmediate(blade.GetComponent<Collider>());
            blade.GetComponent<Renderer>().sharedMaterial = mat;

            // 2. 护手 (Crossguard)
            var crossguard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crossguard.name = "Crossguard";
            crossguard.transform.SetParent(root.transform);
            crossguard.transform.localPosition = new Vector3(0f, 0.075f, 0f);
            crossguard.transform.localRotation = Quaternion.identity;
            crossguard.transform.localScale = new Vector3(0.18f, 0.025f, 0.04f);
            Object.DestroyImmediate(crossguard.GetComponent<Collider>());
            crossguard.GetComponent<Renderer>().sharedMaterial = mat;

            // 3. 剑柄 (Hilt)
            var hilt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hilt.name = "Hilt";
            hilt.transform.SetParent(root.transform);
            hilt.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            hilt.transform.localRotation = Quaternion.identity;
            hilt.transform.localScale = new Vector3(0.025f, 0.1f, 0.025f);
            Object.DestroyImmediate(hilt.GetComponent<Collider>());
            hilt.GetComponent<Renderer>().sharedMaterial = mat;

            // 4. 剑首配重球 (Pommel)
            var pommel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pommel.name = "Pommel";
            pommel.transform.SetParent(root.transform);
            pommel.transform.localPosition = new Vector3(0f, -0.16f, 0f);
            pommel.transform.localRotation = Quaternion.identity;
            pommel.transform.localScale = new Vector3(0.045f, 0.045f, 0.045f);
            Object.DestroyImmediate(pommel.GetComponent<Collider>());
            pommel.GetComponent<Renderer>().sharedMaterial = mat;

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        #endregion
    }
}
