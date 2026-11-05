using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZombieCoop.EditorTools
{
    /// При первом открытии проекта создаёт пустую сцену Assets/Scenes/Game.unity и добавляет её
    /// в список сборки. Сама игра запускается кодом (GameRoot) в любой сцене, сцена нужна только
    /// для того, чтобы было что открыть и что собрать.
    [InitializeOnLoad]
    static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Game.unity";

        static ProjectSetup()
        {
            EditorApplication.delayCall += EnsureScene;
            EditorApplication.delayCall += EnsureUrp;
        }

        const string UrpPath = "Assets/Settings/URP.asset";
        const string RendererPath = "Assets/Settings/URP-Renderer.asset";

        /// Создаёт ассеты URP (если их нет) и назначает их графике и всем уровням качества.
        static void EnsureUrp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath);
            if (asset != null && GraphicsSettings.defaultRenderPipeline == asset) return;
            SetupUrp();
        }

        [MenuItem("Zombie Coop/Настроить URP")]
        static void SetupUrp()
        {
            Directory.CreateDirectory("Assets/Settings");
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath);
            if (asset == null)
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                if (data == null)
                {
                    data = ScriptableObject.CreateInstance<UniversalRendererData>();
                    // Forward+: нет предела в 8 ламп на объект, в длинных коридорах их много.
                    data.renderingMode = RenderingMode.ForwardPlus;
                    AssetDatabase.CreateAsset(data, RendererPath);
                }
                asset = UniversalRenderPipelineAsset.Create(data);
                AssetDatabase.CreateAsset(asset, UrpPath);
            }

            var so = new SerializedObject(asset);
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetBool(so, "m_AdditionalLightShadowsSupported", true);
            SetInt(so, "m_AdditionalLightsRenderingMode", 1); // попиксельно
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetInt(so, "m_AdditionalLightsShadowmapResolution", 2048);
            SetFloat(so, "m_ShadowDistance", 25f);
            SetBool(so, "m_SoftShadowsSupported", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
            Debug.Log("Zombie Coop: рендер переведён на URP (" + UrpPath + ").");
        }

        static void SetBool(SerializedObject so, string name, bool v)
        {
            var p = so.FindProperty(name);
            if (p != null && p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v;
        }

        static void SetInt(SerializedObject so, string name, int v)
        {
            var p = so.FindProperty(name);
            if (p != null && (p.propertyType == SerializedPropertyType.Integer || p.propertyType == SerializedPropertyType.Enum))
                p.intValue = v;
        }

        static void SetFloat(SerializedObject so, string name, float v)
        {
            var p = so.FindProperty(name);
            if (p != null && p.propertyType == SerializedPropertyType.Float) p.floatValue = v;
        }

        [MenuItem("Zombie Coop/Создать сцену и настроить сборку")]
        static void EnsureScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
                Debug.Log("Zombie Coop: создана сцена " + ScenePath + ". Нажмите Play.");
            }
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes)
                if (s.path == ScenePath) return;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
