using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
