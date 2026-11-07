using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShapeCoop.Editor
{
    public static class MapEditorInstaller
    {
        public const string BasePath = "Assets/CoopPrototype/MapEditor";
        public const string PalettePath = BasePath + "/에디터배치용오브젝트";
        public const string ScenePath = BasePath + "/Scenes/MapEditor.unity";
        static readonly Color Terrain = new Color(.32f,.42f,.46f);

        [MenuItem("Tools/Shape Coop/Map Editor/Install missing palette assets")]
        public static void EnsureAssets()
        {
            Directory.CreateDirectory(PalettePath);
            Directory.CreateDirectory(BasePath + "/Textures");
            Directory.CreateDirectory(BasePath + "/Scenes");
            Directory.CreateDirectory("Assets/CoopMaps/Sections");
            AssetDatabase.Refresh();
            Sprite square = CreateSprite("Square", 1), triangle = CreateSprite("Triangle", 0), circle = CreateSprite("Circle", 2);
            CreateTerrain("Terrain_Box", square, 1);
            CreateTerrain("Terrain_Triangle", triangle, 0);
            CreateTerrain("Terrain_Circle", circle, 2);
            CreateMarker("Marker_TestStart", MapMarkerKind.TestStart);
            CreateMarker("Marker_PlayerStart", MapMarkerKind.PlayerStart);
            CreateMarker("Marker_BallStart", MapMarkerKind.BallStart);
            CreateGoal(square);
            CreateConnector("Connector_Entrance", MapConnectorKind.Entrance);
            CreateConnector("Connector_Exit", MapConnectorKind.Exit);
            MapGimmickInstaller.EnsureAssets();
            AssetDatabase.SaveAssets();
        }
        static Sprite CreateSprite(string name, int shape)
        {
            string path = BasePath + "/Textures/" + name + ".png";
            if (!File.Exists(path))
            {
                const int n = 128; var texture = new Texture2D(n, n, TextureFormat.RGBA32, false);
                var pixels = new Color32[n*n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float px = (x + .5f) / n - .5f, py = (y + .5f) / n - .5f;
                    bool inside = shape == 1 || (shape == 2 ? px*px + py*py <= .25f : Mathf.Abs(px) <= (.5f-py)*.5f);
                    pixels[y*n+x] = new Color32(255,255,255, inside ? (byte)255 : (byte)0);
                }
                texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite; importer.spritePixelsPerUnit = n;
                importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        static void CreateTerrain(string name, Sprite sprite, int shape)
        {
            string path = PalettePath + "/" + name + ".prefab";
            if (File.Exists(path)) return;
            var go = new GameObject(name); go.AddComponent<SpriteRenderer>().sprite = sprite;
            go.GetComponent<SpriteRenderer>().color = Terrain;
            if (shape == 1) go.AddComponent<BoxCollider2D>();
            else if (shape == 2) go.AddComponent<CircleCollider2D>().radius = .5f;
            else go.AddComponent<PolygonCollider2D>().points = new[] { new Vector2(-.5f,-.5f), new Vector2(.5f,-.5f), new Vector2(0,.5f) };
            PrefabUtility.SaveAsPrefabAsset(go, path); Object.DestroyImmediate(go);
        }
        static void CreateMarker(string name, MapMarkerKind kind)
        {
            string path = PalettePath + "/" + name + ".prefab";
            if (File.Exists(path)) return;
            var go = new GameObject(name); go.AddComponent<MapMarker>().kind = kind;
            PrefabUtility.SaveAsPrefabAsset(go, path); Object.DestroyImmediate(go);
        }
        static void CreateGoal(Sprite sprite)
        {
            string path = PalettePath + "/Goal_Area.prefab";
            if (File.Exists(path)) return;
            var go = new GameObject("Goal_Area");
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.color = new Color(.25f,.9f,.38f,.3f);
            var collider = go.AddComponent<BoxCollider2D>(); collider.isTrigger = true;
            go.AddComponent<MapGoalArea>(); PrefabUtility.SaveAsPrefabAsset(go, path); Object.DestroyImmediate(go);
        }
        static void CreateConnector(string name, MapConnectorKind kind)
        {
            string path = PalettePath + "/" + name + ".prefab";
            if (File.Exists(path)) return;
            var go = new GameObject(name); go.AddComponent<MapConnector>().kind = kind;
            PrefabUtility.SaveAsPrefabAsset(go, path); Object.DestroyImmediate(go);
        }
        public static void CreateWelcomeScene()
        {
            if (File.Exists(ScenePath)) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); camera.tag = "MainCamera";
            var c = camera.GetComponent<Camera>(); c.orthographic = true; c.orthographicSize = 8; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(.09f,.13f,.18f);
            camera.transform.position = new Vector3(0, 2, -10);
            new GameObject("Open Tools - Shape Coop - Map Editor - Open, then create a section");
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        public static void BatchInstall()
        {
            EnsureAssets(); CreateWelcomeScene(); AssetDatabase.SaveAssets(); Debug.Log("SHAPE_COOP_MAP_EDITOR_INSTALLED"); EditorApplication.Exit(0);
        }
    }
}
