using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    [InitializeOnLoad]
    public static class OilArtworkVerification
    {
        const string Key = "ShapeCoop.OilArtworkVerification";
        const string Prefab = MapGimmickInstaller.Folder + "/기름 웅덩이.prefab";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static bool ready;
        static OilArtworkVerification()
        {
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) ready = true; };
            EditorApplication.update += Tick;
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Run()
        {
            try { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); VerifySectionAssembly(); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        static void VerifySectionAssembly()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/__OilArtworkVerification.prefab");
            var root = new GameObject("oil section"); var assembled = new GameObject("assembled map");
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(source != null, "oil prefab missing");
                root.AddComponent<MapSection>(); var placed = (GameObject)PrefabUtility.InstantiatePrefab(source); placed.transform.SetParent(root.transform, false);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved); Check(saved, "could not export oil section");
                var section = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MapSection>();
                Check(MapSectionAssembler.Build(new[] { new MapSectionPlacement { prefab = section, position = new Vector2(8, 2) } }, assembled.transform, null, out _, out _), "could not assemble oil section");
                var visual = assembled.GetComponentInChildren<OilSurfaceVisual>();
                Check(visual != null && visual.area != null && visual.middle != null && visual.middle.sprite != null && visual.middle.sprite.name == "OilSurface_Middle", "assembled section lost oil artwork or trigger");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(assembled); AssetDatabase.DeleteAsset(path); }
        }
        static void Tick()
        {
            if (!ready || !EditorApplication.isPlaying) return;
            ready = false; var mode = Physics2D.simulationMode;
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(prefab != null, "oil prefab missing");
                var obj = Object.Instantiate(prefab); var g = obj.GetComponent<MapGimmick>(); var visual = obj.GetComponent<OilSurfaceVisual>();
                var area = obj.GetComponent<BoxCollider2D>(); Check(area.isTrigger && area.size == new Vector2(4, .65f) && area.offset == new Vector2(0, .3f), "oil trigger changed");
                Check(visual != null && visual.area == area && visual.left.sprite.name == "OilSurface_Left" && visual.middle.sprite.name == "OilSurface_Middle" && visual.right.sprite.name == "OilSurface_Right" && visual.isolated.sprite.name == "OilSurface_Small", "oil tile sprites missing");
                visual.Refresh(); Check(visual.left.enabled && visual.right.enabled && visual.middle.enabled && !visual.isolated.enabled && visual.middle.drawMode == SpriteDrawMode.Tiled && visual.middle.transform.localScale == Vector3.one && Mathf.Abs(visual.middle.size.x - 2) < .001f, "long puddle tiling incorrect");
                area.size = new Vector2(6, .65f); area.offset = new Vector2(.4f, .3f); typeof(OilSurfaceVisual).GetMethod("LateUpdate", Private).Invoke(visual, null);
                Check(Mathf.Abs(visual.left.transform.localPosition.x - (-2.1f)) < .001f && Mathf.Abs(visual.right.transform.localPosition.x - 2.9f) < .001f && Mathf.Abs(visual.middle.size.x - 4) < .001f, "visuals did not match resized trigger");
                area.size = new Vector2(1.3f, .65f); typeof(OilSurfaceVisual).GetMethod("LateUpdate", Private).Invoke(visual, null); Check(visual.isolated.enabled && !visual.left.enabled && !visual.right.enabled && !visual.middle.enabled && Mathf.Abs(visual.isolated.transform.localScale.x - 1.3f) < .001f, "short puddle sprite incorrect");
                area.size = new Vector2(4, .65f); area.offset = new Vector2(0, .3f); visual.Refresh();
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-oilCapture") >= 0) Capture(prefab, obj);
                g.Initialize(true);
                var settings = new GameObject("settings").AddComponent<PrototypeSettings>();
                var player = new GameObject("player").AddComponent<PrototypePlayer>();
                player.Initialize(settings, new PlayerData { shape = 1, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, new Vector2(-1, .4f), true);
                var ball = new GameObject("ball"); ball.transform.position = new Vector3(1, .5f, 0);
                ball.AddComponent<Rigidbody2D>(); var ballCollider = ball.AddComponent<CircleCollider2D>(); ballCollider.radius = .5f; var goal = ball.AddComponent<MapGoalBall>();
                Physics2D.SyncTransforms(); for (int i = 0; i < 3; i++) Physics2D.Simulate(.02f);
                Check(player.shapeCollider.sharedMaterial != null && player.shapeCollider.sharedMaterial.friction == 0 && ballCollider.sharedMaterial != null && ballCollider.sharedMaterial.friction == 0, "player or ball did not receive oil effect");
                player.dashPending = true; typeof(PrototypePlayer).GetMethod("FixedUpdate", Private).Invoke(player, null);
                float remaining = (float)typeof(PrototypePlayer).GetField("dashRemaining", Private).GetValue(player);
                Check(remaining > settings.dashDuration * 2, "oil dash extension was lost");
                player.ResetBody(); goal.ResetEffects(); Check(player.shapeCollider.sharedMaterial == null && ballCollider.sharedMaterial == null, "restart did not restore materials");
                Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_OIL_ARTWORK_VERIFICATION_OK"); EditorApplication.Exit(0);
            }
            catch (Exception error) { Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.LogException(error); EditorApplication.Exit(1); }
        }
        static void Capture(GameObject prefab, GameObject original)
        {
            original.transform.position = new Vector3(-2.4f, 0, 0);
            var small = Object.Instantiate(prefab, new Vector3(2, 0, 0), Quaternion.identity);
            small.GetComponent<BoxCollider2D>().size = new Vector2(1.3f, .65f); small.GetComponent<OilSurfaceVisual>().Refresh();
            var floor = new GameObject("preview floor"); floor.transform.position = new Vector3(0, -.25f, 0); floor.transform.localScale = new Vector3(9, .5f, 1);
            var renderer = floor.AddComponent<SpriteRenderer>(); renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(MapEditorInstaller.BasePath + "/Textures/Square.png"); renderer.color = new Color(.38f, .4f, .42f);
            var cameraObj = new GameObject("oil preview camera"); var camera = cameraObj.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 1.5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f); camera.transform.position = new Vector3(0, .3f, -10);
            var target = new RenderTexture(1200, 450, 24); camera.targetTexture = target; camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(1200, 450, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1200, 450), 0, 0); texture.Apply(); File.WriteAllBytes("Library/OilSurfacePreview.png", texture.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObj); Object.DestroyImmediate(floor); Object.DestroyImmediate(small); original.transform.position = Vector3.zero;
        }
    }
}
