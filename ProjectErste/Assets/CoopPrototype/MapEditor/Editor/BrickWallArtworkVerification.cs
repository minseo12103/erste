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
    public static class BrickWallArtworkVerification
    {
        const string Key = "ShapeCoop.BrickWallArtworkVerification";
        const string Prefab = MapGimmickInstaller.Folder + "/부실공사 벽돌 벽.prefab";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static bool ready;
        static BrickWallArtworkVerification()
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
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/__BrickWallVerification.prefab");
            var section = new GameObject("brick section"); var map = new GameObject("assembled map");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(prefab != null, "brick prefab missing");
                section.AddComponent<MapSection>(); var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab); placed.transform.SetParent(section.transform, false);
                PrefabUtility.SaveAsPrefabAsset(section, path, out bool saved); Check(saved, "could not export brick section");
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MapSection>();
                Check(MapSectionAssembler.Build(new[] { new MapSectionPlacement { prefab = asset, position = new Vector2(4, 2) } }, map.transform, null, out _, out _), "could not assemble brick section");
                var visual = map.GetComponentInChildren<BrickWallVisual>(); Check(visual != null && visual.burstFrames.Length == 6 && visual.wall.sprite == visual.intact, "assembled section lost brick artwork");
            }
            finally { Object.DestroyImmediate(section); Object.DestroyImmediate(map); AssetDatabase.DeleteAsset(path); }
        }
        static void Step(MapGimmick g, int count, PrototypePlayer player = null)
        {
            var update = typeof(MapGimmick).GetMethod("FixedUpdate", Private);
            var playerUpdate = typeof(PrototypePlayer).GetMethod("FixedUpdate", Private); Physics2D.SyncTransforms();
            for (int i = 0; i < count; i++) { if (player != null) playerUpdate.Invoke(player, null); update.Invoke(g, null); Physics2D.Simulate(.02f); }
        }
        static Rigidbody2D Ball(float velocity)
        {
            var obj = new GameObject("goal ball"); var rb = obj.AddComponent<Rigidbody2D>(); rb.position = new Vector2(-1.5f, 0); rb.gravityScale = 0; rb.linearVelocity = Vector2.right * velocity; rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            obj.AddComponent<CircleCollider2D>().radius = .5f; obj.AddComponent<MapGoalBall>(); return rb;
        }
        static PrototypePlayer Player(PrototypeSettings settings, int shape)
        {
            var obj = new GameObject("brick test player"); var player = obj.AddComponent<PrototypePlayer>();
            player.Initialize(settings, new PlayerData { id = "brick-test", name = "test", shape = shape, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, new Vector2(-1.3f, 0), true);
            return player;
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
                var image = host.visual.GetComponent<BrickWallVisual>(); var other = remote.visual.GetComponent<BrickWallVisual>(); var collider = host.GetComponent<BoxCollider2D>();
                Check(collider.size == new Vector2(.8f, 3) && host.impactSpeed == 8 && host.GetComponentsInChildren<Collider2D>(true).Length == 1, "brick wall physics changed");
                Check(image != null && image.intact != null && image.cracked != null && image.rubbleSprite != null && image.burstFrames.Length == 6 && image.wall.sprite == image.intact && image.rubble.sprite == null, "initial brick artwork missing");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-brickCapture") >= 0) Capture("intact");
                var settings = new GameObject("brick test settings").AddComponent<PrototypeSettings>(); settings.gravityScale = 0;
                var walking = Player(settings, 1); walking.move = 1; Step(host, 30, walking);
                Check(walking.shapeCollider.IsTouching(collider) && !host.Capture(0).broken && collider.enabled, "walking player should touch but not break wall");
                walking.dashPending = true; Step(host, 2, walking);
                Check(host.Capture(0).broken && !collider.enabled, "dashing while touching wall did not break it");
                host.ResetGimmick();
                Object.DestroyImmediate(walking.gameObject);
                for (int shape = 0; shape < 3; shape++)
                {
                    var player = Player(settings, shape); player.move = 1; player.dashPending = true; Step(host, 4, player);
                    var playerState = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                    Check(playerState.broken && !collider.enabled && image.wall.sprite == image.cracked, "dashing player did not break wall: " + shape);
                    remote.Apply(playerState, true);
                    Check(!remote.GetComponent<Collider2D>().enabled && other.wall.sprite == other.cracked, "client did not show player-broken wall: " + shape);
                    Object.DestroyImmediate(player.gameObject); host.ResetGimmick(); remote.Apply(host.Capture(0), true);
                    Check(collider.enabled && remote.GetComponent<Collider2D>().enabled, "player-broken wall did not reset: " + shape);
                }
                Object.DestroyImmediate(settings.gameObject);
                var unrelated = Ball(20); Object.DestroyImmediate(unrelated.GetComponent<MapGoalBall>()); Step(host, 3);
                Check(unrelated.GetComponent<Collider2D>().IsTouching(collider) && !host.Capture(0).broken && collider.enabled, "unrelated body broke wall");
                Object.DestroyImmediate(unrelated.gameObject);
                var slow = Ball(4); Step(host, 15); Check(!host.Capture(0).broken && collider.enabled && image.wall.sprite == image.intact, "slow ball broke wall"); Object.DestroyImmediate(slow.gameObject);
                var fast = Ball(20); Step(host, 3); var state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                Check(state.broken && !collider.enabled && image.wall.sprite == image.cracked, "fast ball did not crack and open wall");
                remote.Apply(state, true); Check(!remote.GetComponent<Collider2D>().enabled && other.wall.sprite == other.cracked, "client did not show cracked open wall");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-brickCapture") >= 0) Capture("cracked");
                Step(host, 6); state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0))); remote.Apply(state, false);
                Check(image.wall.sprite == null && image.rubble.sprite == image.rubbleSprite && image.burst.sprite != null && other.burst.sprite == image.burst.sprite, "burst or rubble did not synchronize");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-brickCapture") >= 0) Capture("burst");
                for (int i = 0; i < 6; i++) { image.Show(true, image.frameSeconds * (i + 1.1f)); Check(image.burst.sprite == image.burstFrames[i], "wrong burst frame order"); }
                Step(host, 40); state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0))); remote.Apply(state, false);
                Check(image.wall.sprite == null && image.burst.sprite == null && image.rubble.sprite == image.rubbleSprite && other.burst.sprite == null && !collider.enabled, "finished wall did not retain non-solid rubble");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-brickCapture") >= 0) Capture("rubble");
                host.ResetGimmick(); remote.Apply(host.Capture(0), true);
                Check(collider.enabled && remote.GetComponent<Collider2D>().enabled && image.wall.sprite == image.intact && other.wall.sprite == other.intact && image.rubble.sprite == null && other.burst.sprite == null, "restart did not restore wall on both peers");
                Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_BRICK_WALL_VERIFICATION_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Capture(string name)
        {
            var obj = new GameObject("brick preview camera"); var camera = obj.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 2.5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f); camera.transform.position = new Vector3(0, 0, -10);
            var target = new RenderTexture(600, 700, 24); camera.targetTexture = target; camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(600, 700, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 600, 700), 0, 0); texture.Apply(); File.WriteAllBytes("Library/BrickWallPreview_" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(obj);
        }
    }
}
