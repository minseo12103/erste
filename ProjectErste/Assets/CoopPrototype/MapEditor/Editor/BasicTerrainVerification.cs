using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    public static class BasicTerrainVerification
    {
        const string Folder = MapEditorInstaller.PalettePath + "/기본 오브젝트";
        const string Sheet = "Assets/CoopPrototype/MapEditor/Textures/BasicObject.png";
        static readonly string[] Names = { "사각형 기본", "삼각형 기본", "사각형 녹슨", "삼각형 녹슨" };
        static readonly string[] Sprites = { "Square_Clean", "Triangle_Clean", "Square_Rusty", "Triangle_Rusty" };
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Run()
        {
            GameObject root = null;
            string temporary = "Assets/CoopPrototype/MapEditor/Textures/__BasicTerrainTest_" + Guid.NewGuid().ToString("N") + ".prefab";
            try
            {
                var sprites = AssetDatabase.LoadAllAssetsAtPath(Sheet).OfType<Sprite>().ToArray();
                Check(sprites.Length == 4 && Sprites.All(name => sprites.Any(sprite => sprite.name == name)), "four sliced sprites were not imported");
                var items = MapEditorWindow.FindPaletteItems();
                root = new GameObject("Basic terrain validation"); root.transform.position = new Vector3(10000, 10000, 0);
                for (int i = 0; i < Names.Length; i++)
                {
                    string path = Folder + "/" + Names[i] + ".prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    Check(prefab != null && items.Any(item => item.prefab == prefab && item.category == "기본 오브젝트" && item.label == Names[i]), "palette item missing: " + Names[i]);
                    Check(MapPaletteTags.GetTags(prefab).Contains("배치용"), "placement tag missing: " + Names[i]);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    instance.transform.SetParent(root.transform, false); instance.transform.localPosition = new Vector3(i * 1.5f, 0, 0);
                    var renderer = instance.GetComponent<SpriteRenderer>(); var collider = instance.GetComponent<Collider2D>();
                    Check(renderer != null && renderer.sprite != null && renderer.sprite.name == Sprites[i] && renderer.color == Color.white, "incorrect sprite or tint: " + Names[i]);
                    Check(Mathf.Abs(renderer.sprite.bounds.size.x - 1f) < .001f && Mathf.Abs(renderer.sprite.bounds.size.y - 1f) < .001f, "sprite should be one unit square");
                    Check(collider != null && !collider.isTrigger && instance.GetComponent<Rigidbody2D>() == null, "terrain must have static collision");
                    Check(i % 2 == 0 ? collider is BoxCollider2D : collider is PolygonCollider2D, "collider shape mismatch: " + Names[i]);
                    Physics2D.SyncTransforms();
                    Check(collider.OverlapPoint(instance.transform.TransformPoint(new Vector3(.3f, -.3f))) && (i % 2 == 0 || !collider.OverlapPoint(instance.transform.TransformPoint(new Vector3(-.3f, .3f)))), "collider does not match image: " + Names[i]);
                    if (i % 2 != 0)
                    {
                        instance.transform.localScale = new Vector3(-1, 1, 1); Physics2D.SyncTransforms();
                        Check(collider.OverlapPoint(instance.transform.TransformPoint(new Vector3(.3f, -.3f))) && !collider.OverlapPoint(instance.transform.TransformPoint(new Vector3(-.3f, .3f))), "mirrored triangle collision mismatch: " + Names[i]);
                        instance.transform.localScale = Vector3.one;
                    }
                }
                Check(PrefabUtility.SaveAsPrefabAsset(root, temporary) != null, "could not save section-style prefab");
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(temporary);
                var savedRenderers = saved.GetComponentsInChildren<SpriteRenderer>(true);
                Check(savedRenderers.Length == 4 && Sprites.All(name => savedRenderers.Any(renderer => renderer.sprite != null && renderer.sprite.name == name)), "sprites did not survive prefab export");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-basicTerrainCapture") >= 0) Capture(root);
                Debug.Log("SHAPE_BASIC_TERRAIN_OK");
                Object.DestroyImmediate(root); root = null; AssetDatabase.DeleteAsset(temporary); EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                if (root != null) Object.DestroyImmediate(root);
                if (AssetDatabase.LoadMainAssetAtPath(temporary) != null) AssetDatabase.DeleteAsset(temporary);
                Debug.LogException(error); EditorApplication.Exit(1);
            }
        }
        static void Capture(GameObject root)
        {
            var cameraObject = new GameObject("Basic terrain verification camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 1.15f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.14f, .16f, .2f);
            camera.transform.position = root.transform.position + new Vector3(2.25f, 0, -10);
            var target = new RenderTexture(1440, 460, 24); camera.targetTexture = target; camera.Render();
            var old = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes("Library/BasicTerrainPreview.png", image.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject);
        }
    }
}
