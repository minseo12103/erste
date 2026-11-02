using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShapeCoop.Editor
{
    public static class PrototypeSetup
    {
        const string ScenePath = "Assets/CoopPrototype/Scenes/ShapeCoop.unity";
        const string PrefabPath = "Assets/CoopPrototype/Prefabs/PrototypeSystem.prefab";
        [MenuItem("Tools/Shape Coop/Create or refresh prototype scene")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/CoopPrototype/Scenes"); Directory.CreateDirectory("Assets/CoopPrototype/Prefabs");
            var root = new GameObject("Shape Coop System"); root.AddComponent<PrototypeSettings>(); root.AddComponent<SteamSession>(); root.AddComponent<PrototypeGame>();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); Object.DestroyImmediate(root);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(.5f, 2, -10); var c = camera.GetComponent<Camera>(); c.orthographic = true; c.orthographicSize = 8; c.backgroundColor = new Color(.09f,.13f,.18f); c.clearFlags = CameraClearFlags.SolidColor;
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            var previous = EditorBuildSettings.scenes; var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var entry in previous) if (entry.path != ScenePath) scenes.Add(entry);
            EditorBuildSettings.scenes = scenes.ToArray();
            File.WriteAllText("steam_appid.txt", "480\n");
            AssetDatabase.SaveAssets(); Debug.Log("SHAPE_COOP_SETUP_OK");
        }
        public static void BatchSetup() { Setup(); EditorApplication.Exit(0); }
    }
    public sealed class PrototypeBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 1000;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.StandaloneWindows64 || report.summary.platform == BuildTarget.StandaloneWindows)
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "steam_appid.txt"), "480\n");
        }
    }
}
