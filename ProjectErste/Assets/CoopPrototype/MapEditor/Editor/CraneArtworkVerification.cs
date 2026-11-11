using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    [InitializeOnLoad]
    public static class CraneArtworkVerification
    {
        const string Key = "ShapeCoop.CraneArtworkVerification";
        const string Prefab = MapGimmickInstaller.Folder + "/움직이는 타워크레인 빔.prefab";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static bool ready;
        static CraneArtworkVerification()
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
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/__CraneVerification.prefab");
            var section = new GameObject("crane section"); var map = new GameObject("assembled map");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(prefab != null, "crane prefab missing");
                section.AddComponent<MapSection>(); var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab); placed.transform.SetParent(section.transform, false);
                PrefabUtility.SaveAsPrefabAsset(section, path, out bool saved); Check(saved, "could not export crane section");
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MapSection>();
                Check(MapSectionAssembler.Build(new[] { new MapSectionPlacement { prefab = asset, position = new Vector2(4, 2) } }, map.transform, null, out _, out _), "could not assemble crane section");
                var g = map.GetComponentInChildren<MapGimmick>(); var wheel = map.GetComponentInChildren<RotatingCranePlatforms>();
                Check(g != null && g.rotatingBeam != null && g.rotatingBeam.transform.Find("Center gear").GetComponent<SpriteRenderer>().sprite.name == "Crane_Gear" && wheel != null && wheel.platforms.Length == 6, "assembled section lost wheel or platforms");
            }
            finally { Object.DestroyImmediate(section); Object.DestroyImmediate(map); AssetDatabase.DeleteAsset(path); }
        }
        static void Step(MapGimmick g, int count, PrototypePlayer player = null)
        {
            var gimmickTick = typeof(MapGimmick).GetMethod("FixedUpdate", Private); var playerTick = typeof(PrototypePlayer).GetMethod("FixedUpdate", Private);
            Physics2D.SyncTransforms();
            for (int i = 0; i < count; i++) { if (player != null) playerTick.Invoke(player, null); gimmickTick.Invoke(g, null); Physics2D.Simulate(.02f); }
        }
        static void Tick()
        {
            if (!ready || !EditorApplication.isPlaying) return;
            ready = false; var mode = Physics2D.simulationMode;
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(prefab != null, "crane prefab missing");
                var host = Object.Instantiate(prefab).GetComponent<MapGimmick>(); host.Initialize(true);
                var client = Object.Instantiate(prefab, new Vector3(10, 0), Quaternion.identity).GetComponent<MapGimmick>(); client.Initialize(false);
                var beam = host.rotatingBeam; var collider = beam.GetComponent<BoxCollider2D>(); var wheel = host.GetComponent<RotatingCranePlatforms>(); var remoteWheel = client.GetComponent<RotatingCranePlatforms>();
                var tower = host.visual.Find("Pivot").GetComponent<SpriteRenderer>(); var art = beam.transform.Find("Visual").GetComponent<SpriteRenderer>();
                var gear = beam.transform.Find("Center gear").GetComponent<SpriteRenderer>();
                Check(beam.bodyType == RigidbodyType2D.Kinematic && !collider.enabled && host.rotationSpeed == -45 && wheel != null && wheel.platforms.Length == 6 && remoteWheel.platforms.Length == 6, "wheel must have six platforms and no invisible beam collider");
                Check(tower.sprite.name == "Crane_Tower" && art.sprite.name == "Crane_Beam" && gear.sprite.name == "Crane_Gear" && beam.transform.Find("Spoke 60") != null && beam.transform.Find("Spoke 120") != null, "crane artwork missing");
                Check(art.drawMode == SpriteDrawMode.Sliced && Mathf.Abs(art.sprite.border.x - 60) < .01f && Mathf.Abs(art.transform.localScale.x - 1) < .01f && Mathf.Abs(art.transform.localScale.y - 1) < .01f, "spoke endpoints should keep their original proportions");
                Check(gear.transform.parent == beam.transform && !tower.transform.IsChildOf(beam.transform), "rotating or stationary parts have wrong parent");
                foreach (var platform in wheel.platforms)
                {
                    Check(platform != null && platform.bodyType == RigidbodyType2D.Kinematic && platform.GetComponent<BoxCollider2D>().enabled && platform.GetComponent<BoxCollider2D>().size == new Vector2(1.12f, .19f) && platform.transform.Find("Platform art").GetComponent<SpriteRenderer>().sprite.name == "Crane_Plate", "platform collider or artwork missing");
                }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-craneCapture") >= 0) Capture("horizontal");
                Step(host, 25); var state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                Check(state.angle < -20 && state.angle > -25 && Mathf.Abs(Mathf.DeltaAngle(beam.rotation, state.angle)) < .2f, "beam did not rotate clockwise on host");
                client.Apply(state, true); Check(!client.rotatingBeam.simulated && Mathf.Abs(Mathf.DeltaAngle(client.rotatingBeam.transform.localEulerAngles.z, state.angle)) < .1f, "client rotation does not match host");
                for (int i = 0; i < 6; i++)
                {
                    float radians = (state.angle + i * 60) * Mathf.Deg2Rad; Vector2 expected = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 2.15f;
                    Check((wheel.platforms[i].position - expected).sqrMagnitude < .001f && Mathf.Abs(Mathf.DeltaAngle(wheel.platforms[i].rotation, 0)) < .01f, "host platform did not orbit horizontally");
                    Check(!remoteWheel.platforms[i].simulated && ((Vector2)remoteWheel.platforms[i].transform.position - expected - Vector2.right * 10).sqrMagnitude < .001f && Mathf.Abs(Mathf.DeltaAngle(remoteWheel.platforms[i].transform.eulerAngles.z, 0)) < .01f, "client platform did not match host");
                }
                Check(Mathf.Abs(tower.transform.localEulerAngles.z) < .01f && Mathf.Abs(gear.transform.localEulerAngles.z) < .01f, "support or gear rotation hierarchy changed");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-craneCapture") >= 0) Capture("rotating");
                host.ResetGimmick(); client.Apply(host.Capture(0), true); Physics2D.SyncTransforms();
                Check(Mathf.Abs(host.Capture(0).angle) < .01f && Mathf.Abs(Mathf.DeltaAngle(beam.transform.localEulerAngles.z, 0)) < .1f && Mathf.Abs(Mathf.DeltaAngle(client.rotatingBeam.transform.localEulerAngles.z, 0)) < .1f, "restart did not restore rotation");
                for (int i = 0; i < 6; i++) Check(wheel.platforms[i].linearVelocity.sqrMagnitude < .0001f && Mathf.Abs(wheel.platforms[i].angularVelocity) < .001f && Mathf.Abs(Mathf.DeltaAngle(wheel.platforms[i].rotation, 0)) < .01f, "restart did not reset a platform");
                host.rotationSpeed = 0;
                var settings = new GameObject("settings").AddComponent<PrototypeSettings>(); settings.gravityScale = 2;
                for (int i = 0; i < 6; i++)
                {
                    var player = new GameObject("test player").AddComponent<PrototypePlayer>(); var platform = wheel.platforms[i];
                    player.Initialize(settings, new PlayerData { shape = 1, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, platform.position + Vector2.up * 1.02f, true);
                    Step(host, 30, player); Check(player.shapeCollider.IsTouching(platform.GetComponent<Collider2D>()), "player did not land on platform " + i);
                    Object.DestroyImmediate(player.gameObject); Physics2D.SyncTransforms();
                }
                var ball = new GameObject("test ball"); var ballBody = ball.AddComponent<Rigidbody2D>(); ballBody.position = wheel.platforms[0].position + Vector2.up * 1.12f; ballBody.gravityScale = 2; ballBody.mass = 1;
                var ballCollider = ball.AddComponent<CircleCollider2D>(); ballCollider.radius = .6f; ball.AddComponent<MapGoalBall>(); bool landed = false;
                for (int i = 0; i < 45; i++) { Step(host, 1); if (ballCollider.IsTouching(wheel.platforms[0].GetComponent<Collider2D>())) { landed = true; break; } }
                Check(landed, "ball did not contact moving platform"); Object.DestroyImmediate(ball); Physics2D.SyncTransforms();
                var rider = new GameObject("riding player").AddComponent<PrototypePlayer>(); var targetPlatform = wheel.platforms[1];
                rider.Initialize(settings, new PlayerData { shape = 1, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, targetPlatform.position + Vector2.up * 1.02f, true);
                Step(host, 30, rider); Check(rider.shapeCollider.IsTouching(targetPlatform.GetComponent<Collider2D>()), "rider did not land");
                // A rider can now collide with the crane housing; first verify the unobstructed arc with every collider active.
                host.rotationSpeed = -45;
                for (int i = 0; i < 60; i++)
                {
                    Step(host, 1, rider); float drift = Mathf.Abs(rider.body.position.x - targetPlatform.position.x);
                    Check(drift < .65f && rider.body.position.y > targetPlatform.position.y + .7f, "rider did not follow the unobstructed platform at tick " + i);
                }
                // Isolate platform carrying for a full rotation; the separate body collision test checks these parts while enabled.
                var extraColliders = new List<Collider2D>(); var towerColliderRoot = host.transform.Find("Body colliders (physics)");
                foreach (var c in host.GetComponentsInChildren<Collider2D>(true))
                    if ((towerColliderRoot != null && c.transform.IsChildOf(towerColliderRoot)) || c.name == "Spoke collision" || c.name == "Gear collision" || c.name.StartsWith("Hanger ", StringComparison.Ordinal)) { if (c.enabled) { extraColliders.Add(c); c.enabled = false; } }
                host.rotationSpeed = 0; host.ResetGimmick(); rider.ResetBody(); Step(host, 30, rider);
                Check(rider.shapeCollider.IsTouching(targetPlatform.GetComponent<Collider2D>()), "rider did not reland after isolation");
                host.rotationSpeed = -45; float maxDrift = 0;
                for (int i = 0; i < 400; i++)
                {
                    Step(host, 1, rider); float drift = Mathf.Abs(rider.body.position.x - targetPlatform.position.x); maxDrift = Mathf.Max(maxDrift, drift);
                    Check(drift < .65f && rider.body.position.y > targetPlatform.position.y + .7f, "moving platform did not carry the standing player at tick " + i + ", drift=" + drift);
                    if (i % 25 == 0)
                    {
                        var packet = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0))); client.Apply(packet, true);
                        for (int j = 0; j < 6; j++)
                            Check(((Vector2)remoteWheel.platforms[j].transform.position - wheel.platforms[j].position - Vector2.right * 10).sqrMagnitude < .001f && Mathf.Abs(Mathf.DeltaAngle(remoteWheel.platforms[j].transform.eulerAngles.z, 0)) < .01f, "client wheel diverged during rotation");
                    }
                }
                Debug.Log("SHAPE_CRANE_RIDER max drift=" + maxDrift);
                rider.move = -1; Step(host, 5, rider); Check(rider.body.linearVelocity.x < 0, "left input must move toward world-left on a moving platform");
                rider.move = 1; Step(host, 8, rider); Check(rider.body.linearVelocity.x > 0, "right input must move toward world-right on a moving platform");
                rider.move = 0; Step(host, 2, rider);
                rider.jumpPending = true; Step(host, 1, rider); Check(rider.body.linearVelocity.y > 0, "rider could not jump from the moving platform");
                rider.move = -1; Step(host, 6, rider); Check(rider.body.linearVelocity.x < 0, "left input must still move toward world-left");
                Object.DestroyImmediate(rider.gameObject); foreach (var c in extraColliders) if (c != null) c.enabled = true; host.ResetGimmick(); host.rotationSpeed = 0; Physics2D.SyncTransforms();
                var locked = new GameObject("fixed player").AddComponent<PrototypePlayer>();
                locked.Initialize(settings, new PlayerData { shape = 1, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, wheel.platforms[0].position + Vector2.up * 1.02f, true);
                Step(host, 30, locked); locked.freezePending = true; Step(host, 1, locked); Check(locked.frozen, "player could not freeze on the platform");
                Vector2 fixedPosition = locked.body.position; float fixedAngle = locked.body.rotation; host.rotationSpeed = -45; Step(host, 20, locked);
                Check((locked.body.position - fixedPosition).sqrMagnitude < .0001f && Mathf.Abs(Mathf.DeltaAngle(locked.body.rotation, fixedAngle)) < .01f, "crane moved a fixed player");
                locked.freezePending = true; Step(host, 1, locked); Check(!locked.frozen, "player could not unfreeze on the crane");
                Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_CRANE_ARTWORK_VERIFICATION_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Capture(string name)
        {
            var obj = new GameObject("crane preview camera"); var camera = obj.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 3.5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f); camera.transform.position = new Vector3(0, 0, -10);
            var target = new RenderTexture(1000, 650, 24); camera.targetTexture = target; camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(1000, 650, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1000, 650), 0, 0); texture.Apply(); File.WriteAllBytes("Library/CranePreview_" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(obj);
        }
    }
}
