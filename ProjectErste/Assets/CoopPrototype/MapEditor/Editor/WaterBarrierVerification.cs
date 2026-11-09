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
    public static class WaterBarrierVerification
    {
        const string Key = "ShapeCoop.WaterBarrierVerification";
        const string Prefab = MapGimmickInstaller.Folder + "/수압 장벽.prefab";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static bool ready;
        static WaterBarrierVerification()
        {
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) ready = true; };
            EditorApplication.update += Tick;
        }
        static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        public static void Run()
        {
            try { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); VerifySection(); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void VerifySection()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/__WaterBarrierVerification.prefab");
            var section = new GameObject("water section"); var map = new GameObject("assembled map");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(prefab != null, "water prefab missing");
                section.AddComponent<MapSection>(); var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab); placed.transform.SetParent(section.transform, false);
                PrefabUtility.SaveAsPrefabAsset(section, path, out bool saved); Check(saved, "could not export water section");
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MapSection>();
                Check(MapSectionAssembler.Build(new[] { new MapSectionPlacement { prefab = asset, position = new Vector2(4, 2) } }, map.transform, null, out _, out _), "could not assemble water section");
                var visual = map.GetComponentInChildren<WaterBarrierVisual>(); Check(visual != null && visual.streamFrames.Length == 4 && visual.splitFrames.Length == 4, "assembled section lost water artwork");
            }
            finally { Object.DestroyImmediate(section); Object.DestroyImmediate(map); AssetDatabase.DeleteAsset(path); }
        }
        static void Step(MapGimmick g, int count, PrototypePlayer player = null)
        {
            var update = typeof(MapGimmick).GetMethod("FixedUpdate", Private); var playerUpdate = typeof(PrototypePlayer).GetMethod("FixedUpdate", Private);
            Physics2D.SyncTransforms();
            for (int i = 0; i < count; i++) { if (player != null) playerUpdate.Invoke(player, null); update.Invoke(g, null); Physics2D.Simulate(.02f); }
        }
        static PrototypePlayer Player(PrototypeSettings settings, int shape, Vector2 position)
        {
            var p = new GameObject("test player").AddComponent<PrototypePlayer>();
            p.Initialize(settings, new PlayerData { shape = shape, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, position, true); p.body.gravityScale = 0; return p;
        }
        static Rigidbody2D Ball(Vector2 position)
        {
            var obj = new GameObject("test goal ball"); var rb = obj.AddComponent<Rigidbody2D>(); rb.position = position; rb.gravityScale = 0; rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            obj.AddComponent<CircleCollider2D>().radius = .6f; obj.AddComponent<MapGoalBall>(); return rb;
        }
        static void Tick()
        {
            if (!ready || !EditorApplication.isPlaying) return;
            ready = false; var mode = Physics2D.simulationMode;
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); var host = Object.Instantiate(prefab).GetComponent<MapGimmick>(); host.Initialize(true);
                var remote = Object.Instantiate(prefab, new Vector3(10, 0), Quaternion.identity).GetComponent<MapGimmick>(); remote.Initialize(false);
                var area = host.GetComponent<BoxCollider2D>(); var visual = host.visual.GetComponent<WaterBarrierVisual>(); var other = remote.visual.GetComponent<WaterBarrierVisual>();
                var main = host.visual.Find("Water"); var left = host.visual.Find("Water deflected left (runtime)"); var right = host.visual.Find("Water deflected right (runtime)");
                var stream = main.Find("Stream art").GetComponent<SpriteRenderer>();
                Check(area != null && area.isTrigger && area.size == new Vector2(2, 5) && area.offset == new Vector2(0, -2.5f) && host.force == 24 && host.GetComponentsInChildren<Collider2D>(true).Length == 1, "water physics changed");
                Check(host.visual.Find("Pipe").GetComponent<SpriteRenderer>().sprite.name == "WaterBarrier_Pipe" && visual != null && visual.streamFrames.Length == 4 && visual.splashFrames.Length == 3 && visual.splitFrames.Length == 4, "water artwork missing");
                Check(stream.sprite == visual.streamFrames[0] && visual.splash.sprite == null && visual.split.sprite == null && !left.gameObject.activeSelf && !right.gameObject.activeSelf, "unshielded water appearance incorrect");
                for (int i = 0; i < 4; i++)
                {
                    visual.Show((i + .1f) / visual.framesPerSecond, false, 0, 0, 0, 0);
                    Check(stream.sprite == visual.streamFrames[i] && left.Find("Stream art").GetComponent<SpriteRenderer>().sprite == visual.streamFrames[i] && right.Find("Stream art").GetComponent<SpriteRenderer>().sprite == visual.streamFrames[i], "water frames do not animate together");
                }
                host.ResetGimmick(); Check(stream.sprite == visual.streamFrames[0], "restart did not reset water animation");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-waterCapture") >= 0) Capture("flow");
                var settings = new GameObject("settings").AddComponent<PrototypeSettings>(); settings.gravityScale = 0;
                var triangle = Player(settings, 0, new Vector2(-.2f, -1)); triangle.freezePending = true;
                var covered = Ball(new Vector2(-.55f, -2.5f)); var exposed = Ball(new Vector2(.75f, -2.5f));
                Step(host, 8, triangle); var state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                Check(state.shielded && state.shieldHeight > .9f && triangle.frozen && !triangle.stunned, "triangle shield or existing player rule changed");
                Check(exposed.linearVelocity.y < -.2f && covered.linearVelocity.y > exposed.linearVelocity.y + .1f, "triangle did not protect the ball");
                Check(visual.splash.sprite != null && visual.split.sprite != null && left.gameObject.activeSelf && right.gameObject.activeSelf, "shielded water did not split visually");
                Check(Mathf.Abs(visual.split.transform.localPosition.y - state.shieldY) < .01f && visual.split.transform.localScale.x > 0 && visual.split.transform.localScale.y > 0, "split effect is not at the triangle");
                remote.Apply(state, true); Check(other.splash.sprite == visual.splash.sprite && other.split.sprite == visual.split.sprite && Mathf.Abs(other.split.transform.localPosition.y - visual.split.transform.localPosition.y) < .01f, "client shield appearance disagrees with host");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-waterCapture") >= 0)
                {
                    triangle.body.position = new Vector2(-.2f, -2); covered.position = new Vector2(-.55f, -3.5f); exposed.position = new Vector2(.75f, -3.5f);
                    Step(host, 2, triangle); Capture("shielded");
                }
                triangle.body.position = new Vector2(-.55f, -1); Step(host, 2, triangle);
                Check(host.Capture(0).shielded && !left.gameObject.activeSelf && right.gameObject.activeSelf && visual.split.sprite != null && visual.split.transform.localPosition.x >= -1, "shield at barrier edge did not clip the split correctly");
                Object.DestroyImmediate(triangle.gameObject); Object.DestroyImmediate(covered.gameObject); Object.DestroyImmediate(exposed.gameObject); Physics2D.SyncTransforms();
                Step(host, 2); Check(!host.Capture(0).shielded && visual.splash.sprite == null && visual.split.sprite == null && !left.gameObject.activeSelf && !right.gameObject.activeSelf, "water did not restore after shield left");
                var square = Player(settings, 1, new Vector2(0, -2)); Step(host, 8, square); Check(!square.stunned, "water must not stun the player");
                host.ResetGimmick(); remote.Apply(host.Capture(0), true);
                Check(visual.splash.sprite == null && visual.split.sprite == null && stream.sprite == visual.streamFrames[0] && other.splash.sprite == null, "restart did not reset water presentation");
                Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_WATER_BARRIER_VERIFICATION_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Capture(string name)
        {
            var obj = new GameObject("water preview camera"); var camera = obj.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 3.7f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f); camera.transform.position = new Vector3(0, -2.1f, -10);
            var target = new RenderTexture(650, 900, 24); camera.targetTexture = target; camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(650, 900, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 650, 900), 0, 0); texture.Apply(); File.WriteAllBytes("Library/WaterBarrierPreview_" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(obj);
        }
    }
}
