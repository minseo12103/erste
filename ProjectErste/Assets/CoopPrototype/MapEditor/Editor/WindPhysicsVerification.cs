using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Steamworks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    [InitializeOnLoad]
    public static class WindPhysicsVerification
    {
        const string Key = "ShapeCoop.WindPhysicsVerification";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static bool ready;
        static WindPhysicsVerification()
        {
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) ready = true; };
            EditorApplication.update += RunTests;
        }
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        public static void Run()
        {
            try { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static MapGimmick Gimmick(string name, Vector2 position, float angle, bool host)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapGimmickInstaller.Folder + "/" + name + ".prefab");
            Check(prefab != null, "missing " + name); var g = Object.Instantiate(prefab, position, Quaternion.Euler(0, 0, angle)).GetComponent<MapGimmick>(); g.Initialize(host); return g;
        }
        static void Step(MapGimmick wind, int count, PrototypePlayer player = null)
        {
            var tick = typeof(MapGimmick).GetMethod("FixedUpdate", Private); var playerTick = typeof(PrototypePlayer).GetMethod("FixedUpdate", Private);
            Physics2D.SyncTransforms();
            for (int i = 0; i < count; i++) { if (player != null) playerTick.Invoke(player, null); tick.Invoke(wind, null); Physics2D.Simulate(.02f); }
        }
        static Rigidbody2D Body(string name, Vector2 position, float mass, bool compound = false)
        {
            var go = new GameObject(name); var body = go.AddComponent<Rigidbody2D>(); body.position = position; body.mass = mass; body.gravityScale = 0;
            if (!compound) go.AddComponent<CircleCollider2D>().radius = .18f;
            else for (int i = 0; i < 2; i++) { var part = new GameObject("part " + i); part.transform.SetParent(go.transform, false); part.transform.localPosition = new Vector2(0, (i == 0 ? -.18f : .18f)); part.AddComponent<CircleCollider2D>().radius = .15f; }
            return body;
        }
        static PrototypePlayer Player(PrototypeSettings settings, int shape, Vector2 position)
        {
            var player = new GameObject("player " + shape).AddComponent<PrototypePlayer>();
            player.Initialize(settings, new PlayerData { shape = shape, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, position, true); return player;
        }
        static void RunTests()
        {
            if (!ready || !EditorApplication.isPlaying) return;
            ready = false; var oldMode = Physics2D.simulationMode;
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                var wind = Gimmick("공기 환풍구", Vector2.zero, 0, true);
                var settings = new GameObject("settings").AddComponent<PrototypeSettings>(); settings.gravityScale = 2;
                for (int shape = 0; shape < 3; shape++)
                {
                    var player = Player(settings, shape, new Vector2(0, 1.5f)); Step(wind, 10, player);
                    Check(player.body.linearVelocity.y > .5f, "wind did not lift player shape " + shape); Object.DestroyImmediate(player.gameObject);
                }
                var ground = new GameObject("ground"); ground.transform.position = new Vector2(0, -.5f); ground.AddComponent<BoxCollider2D>().size = new Vector2(4, 1);
                var grounded = Player(settings, 1, new Vector2(0, .51f)); Step(wind, 25, grounded);
                Check(grounded.body.position.y > .65f, "wind did not lift a player from the ground"); Object.DestroyImmediate(grounded.gameObject); Object.DestroyImmediate(ground);
                var frozen = Player(settings, 1, new Vector2(0, 1.5f)); frozen.body.rotation = 37; frozen.freezePending = true;
                typeof(PrototypePlayer).GetMethod("FixedUpdate", Private).Invoke(frozen, null); var frozenPosition = frozen.body.position; Step(wind, 10, frozen);
                Check(frozen.frozen && frozen.body.bodyType == RigidbodyType2D.Kinematic && Vector2.Distance(frozen.body.position, frozenPosition) < .001f && Mathf.Abs(frozen.body.rotation - 37) < .01f, "wind moved frozen player");
                frozen.freezePending = true; Step(wind, 10, frozen); Check(!frozen.frozen && frozen.body.linearVelocity.y > .5f, "wind did not affect unfrozen player"); Object.DestroyImmediate(frozen.gameObject);
                var light = Body("light", new Vector2(-.6f, 2), 1); var heavy = Body("heavy", new Vector2(0, 2), 4); var compound = Body("compound", new Vector2(.6f, 2), 1, true); var outside = Body("outside", new Vector2(3, 2), 1);
                Step(wind, 10);
                Check(light.linearVelocity.y > 1 && Mathf.Abs(light.linearVelocity.y / heavy.linearVelocity.y - 4) < .03f, "wind did not respect mass");
                Check(Mathf.Abs(light.linearVelocity.y - compound.linearVelocity.y) < .03f, "compound collider received wind more than once");
                Check(outside.linearVelocity.sqrMagnitude < .0001f, "wind affected a body outside its area");
                Object.DestroyImmediate(light.gameObject); Object.DestroyImmediate(heavy.gameObject); Object.DestroyImmediate(compound.gameObject); Object.DestroyImmediate(outside.gameObject);
                var turned = Gimmick("공기 환풍구", new Vector2(20, 0), 90, true); var moving = Body("rotated wind", new Vector2(18, 0), 1); Step(turned, 8);
                Check(moving.linearVelocity.x < -1 && Mathf.Abs(moving.linearVelocity.y) < .01f, "rotated wind did not follow its visual direction"); Object.DestroyImmediate(moving.gameObject); Object.DestroyImmediate(turned.gameObject);
                var seesaw = Gimmick("철골 시소 발판", new Vector2(50, 0), 0, true); seesaw.rotatingBeam.gravityScale = 0;
                var sideWind = Gimmick("공기 환풍구", new Vector2(52, -1), 0, true); Step(sideWind, 20);
                Check(seesaw.Capture(0).angle > .5f, "off-center wind did not rotate the seesaw"); Object.DestroyImmediate(sideWind.gameObject); Object.DestroyImmediate(seesaw.gameObject);
                var clientWind = Gimmick("공기 환풍구", new Vector2(80, 0), 0, false); var clientBody = Body("client body", new Vector2(80, 2), 1); Step(clientWind, 8);
                Check(clientBody.linearVelocity.sqrMagnitude < .0001f, "client applied host-authoritative wind"); Object.DestroyImmediate(clientBody.gameObject); Object.DestroyImmediate(clientWind.gameObject);
                CheckDynamicBodyNetwork();
                CheckGameIntegration();
                Object.DestroyImmediate(wind.gameObject); Object.DestroyImmediate(settings.gameObject);
                Physics2D.simulationMode = oldMode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_WIND_PHYSICS_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = oldMode; SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void CheckDynamicBodyNetwork()
        {
            var hostRoot = new GameObject("host map"); hostRoot.transform.position = new Vector2(100, 0);
            for (int i = 0; i < 11; i++) { var b = Body("map body " + i, new Vector2(100 + i * 2, 3), 1); b.transform.SetParent(hostRoot.transform, true); }
            var seesaw = Gimmick("철골 시소 발판", new Vector2(100, -5), 0, true); seesaw.transform.SetParent(hostRoot.transform, true);
            Physics2D.SyncTransforms();
            var clientRoot = Object.Instantiate(hostRoot); var clientSeesaw = clientRoot.GetComponentInChildren<MapGimmick>(); clientSeesaw.Initialize(false);
            var host = new MapDynamicBodies(hostRoot.transform, new[] { seesaw }, true); var client = new MapDynamicBodies(clientRoot.transform, new[] { clientSeesaw }, false);
            Check(host.Count == 11 && client.Count == 11 && !clientSeesaw.rotatingBeam.simulated, "map bodies include gimmick-managed bodies");
            var hostBodies = new List<Rigidbody2D>(); var clientBodies = new List<Rigidbody2D>();
            foreach (Transform t in hostRoot.transform) { var rb = t.GetComponent<Rigidbody2D>(); if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic) hostBodies.Add(rb); }
            foreach (Transform t in clientRoot.transform) { var rb = t.GetComponent<Rigidbody2D>(); if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic) clientBodies.Add(rb); }
            for (int i = 0; i < hostBodies.Count; i++) { hostBodies[i].position += new Vector2(.2f, i + 1); hostBodies[i].rotation = i * 11; }
            for (int offset = 0; offset < host.Count; offset += MapDynamicBodies.ChunkSize)
            {
                var packet = new Packet { kind = "map_bodies", epoch = 2, sequence = 3, mapBodyOffset = offset, mapBodies = host.Capture(offset) };
                var json = JsonUtility.ToJson(packet); Check(Encoding.UTF8.GetByteCount(json) < 1200, "map body packet exceeds Steam unreliable limit");
                var received = JsonUtility.FromJson<Packet>(json); client.Apply(received.mapBodyOffset, received.mapBodies, received.sequence, true);
            }
            for (int i = 0; i < hostBodies.Count; i++) Check(!clientBodies[i].simulated && Vector2.Distance(clientBodies[i].transform.position, hostBodies[i].position) < .001f && Mathf.Abs(Mathf.DeltaAngle(clientBodies[i].transform.eulerAngles.z, hostBodies[i].rotation)) < .01f, "client body snapshot mismatch " + i);
            var stale = host.Capture(0); stale[0].x += 50; client.Apply(0, stale, 2, true); Check(Vector2.Distance(clientBodies[0].transform.position, hostBodies[0].position) < .001f, "stale body update was accepted");
            var next = host.Capture(0); next[0].x += 2; client.Apply(0, next, 4, false); client.Interpolate(1); Check(Mathf.Abs(clientBodies[0].transform.position.x - next[0].x) < .001f, "client body interpolation failed");
            hostBodies[0].linearVelocity = Vector2.right * 9; hostBodies[0].angularVelocity = 33; host.Reset(); client.Reset();
            for (int i = 0; i < hostBodies.Count; i++) Check(Vector2.Distance(clientBodies[i].transform.position, hostBodies[i].position) < .001f && hostBodies[i].linearVelocity.sqrMagnitude < .001f && Mathf.Abs(hostBodies[i].angularVelocity) < .001f, "map body reset mismatch " + i + " host " + hostBodies[i].position + " client " + clientBodies[i].transform.position + " velocity " + hostBodies[i].linearVelocity + " angular " + hostBodies[i].angularVelocity);
            Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(clientRoot);
            Debug.Log("SHAPE_WIND_MAP_BODY_SYNC_OK");
        }
        static void CheckGameIntegration()
        {
            var source = new GameObject("network section").AddComponent<MapSection>(); source.transform.position = new Vector3(500, 500, 0);
            var loose = new GameObject("loose network body"); loose.transform.SetParent(source.transform, false); loose.transform.localPosition = new Vector3(2, 1, 0);
            loose.AddComponent<Rigidbody2D>().gravityScale = 0; loose.AddComponent<BoxCollider2D>();
            var data = new[] { new PlayerData { id = "1", name = "one", shape = 1, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 } };
            PrototypeGame MakeGame(bool host)
            {
                var system = new GameObject(host ? "network host" : "network client"); system.SetActive(false);
                var game = system.AddComponent<PrototypeGame>();
                typeof(PrototypeGame).GetField("settings", Private).SetValue(game, system.GetComponent<PrototypeSettings>());
                typeof(PrototypeGame).GetField("session", Private).SetValue(game, system.GetComponent<SteamSession>());
                game.mapSections = new[] { new MapSectionPlacement { prefab = source, position = new Vector2(10, 3) } };
                typeof(PrototypeGame).GetMethod("BuildWorld", Private).Invoke(game, new object[] { data, host }); return game;
            }
            var hostGame = MakeGame(true); var clientGame = MakeGame(false);
            var hostBodies = (MapDynamicBodies)typeof(PrototypeGame).GetField("mapBodies", Private).GetValue(hostGame);
            var clientBodies = (MapDynamicBodies)typeof(PrototypeGame).GetField("mapBodies", Private).GetValue(clientGame);
            Check(hostBodies.Count == 1 && clientBodies.Count == 1, "game did not discover authored dynamic body");
            var hostWorld = (GameObject)typeof(PrototypeGame).GetField("world", Private).GetValue(hostGame);
            var clientWorld = (GameObject)typeof(PrototypeGame).GetField("world", Private).GetValue(clientGame);
            Rigidbody2D hostBody = null, clientBody = null;
            foreach (var body in hostWorld.GetComponentsInChildren<Rigidbody2D>(true)) if (body.name == "loose network body") hostBody = body;
            foreach (var body in clientWorld.GetComponentsInChildren<Rigidbody2D>(true)) if (body.name == "loose network body") clientBody = body;
            Check(hostBody != null && clientBody != null, "authored body instance missing");
            Check(Vector2.Distance(hostBody.position, new Vector2(12, 4)) < .001f && !clientBody.simulated, "section offset or client authority is incorrect");
            hostBody.position = new Vector2(14, 7); hostBody.rotation = 37;
            typeof(PrototypeGame).GetField("epoch", Private).SetValue(clientGame, 7);
            clientGame.GetComponent<SteamSession>().Playing = true;
            var send = JsonUtility.ToJson(new Packet { kind = "map_bodies", epoch = 7, sequence = 2, mapBodyOffset = 0, mapBodies = hostBodies.Capture(0) });
            var receive = typeof(PrototypeGame).GetMethod("Receive", Private); receive.Invoke(clientGame, new object[] { CSteamID.Nil, JsonUtility.FromJson<Packet>(send) }); clientBodies.Interpolate(1);
            Check(Vector2.Distance(clientBody.transform.position, hostBody.position) < .001f && Mathf.Abs(Mathf.DeltaAngle(clientBody.transform.eulerAngles.z, 37)) < .01f, "game did not apply host body packet");
            var old = JsonUtility.FromJson<Packet>(send); old.epoch = 6; old.sequence = 99; old.mapBodies[0].x += 40;
            receive.Invoke(clientGame, new object[] { CSteamID.Nil, old }); clientBodies.Interpolate(1);
            Check(Vector2.Distance(clientBody.transform.position, hostBody.position) < .001f, "game accepted body packet from an old epoch");
            typeof(PrototypeGame).GetMethod("ResetPositions", Private).Invoke(hostGame, null);
            typeof(PrototypeGame).GetField("epoch", Private).SetValue(hostGame, 8);
            var reset = (Packet)typeof(PrototypeGame).GetMethod("Snapshot", Private).Invoke(hostGame, new object[] { "reset" });
            receive.Invoke(clientGame, new object[] { CSteamID.Nil, JsonUtility.FromJson<Packet>(JsonUtility.ToJson(reset)) });
            Check(Vector2.Distance(hostBody.position, new Vector2(12, 4)) < .001f && Vector2.Distance(clientBody.transform.position, hostBody.position) < .001f, "game restart did not reset map body on both peers");
            Object.DestroyImmediate(hostWorld); Object.DestroyImmediate(clientWorld); Object.DestroyImmediate(hostGame.gameObject); Object.DestroyImmediate(clientGame.gameObject);
            var marker = new GameObject("test start").AddComponent<MapMarker>(); marker.kind = MapMarkerKind.TestStart; marker.transform.SetParent(source.transform, false);
            var runnerObject = new GameObject("map test runner"); var runner = runnerObject.AddComponent<MapTestRunner>(); runner.section = source;
            typeof(MapTestRunner).GetMethod("Start", Private).Invoke(runner, null);
            var sourceBody = loose.GetComponent<Rigidbody2D>(); var initial = sourceBody.position; sourceBody.position += Vector2.up * 4; sourceBody.linearVelocity = Vector2.right * 3;
            runner.ResetTest(); Check(Vector2.Distance(sourceBody.position, initial) < .001f && sourceBody.linearVelocity.sqrMagnitude < .001f, "map test restart did not reset dynamic body");
            var testPlayer = (PrototypePlayer)typeof(MapTestRunner).GetField("player", Private).GetValue(runner);
            var testBall = (Rigidbody2D)typeof(MapTestRunner).GetField("ball", Private).GetValue(runner);
            var testCamera = (Camera)typeof(MapTestRunner).GetField("testCamera", Private).GetValue(runner);
            if (testPlayer != null) Object.DestroyImmediate(testPlayer.gameObject); if (testBall != null) Object.DestroyImmediate(testBall.gameObject);
            if (testCamera != null && testCamera.name == "Map test camera (runtime)") Object.DestroyImmediate(testCamera.gameObject);
            Object.DestroyImmediate(runnerObject); Object.DestroyImmediate(source.gameObject);
            Debug.Log("SHAPE_WIND_GAME_SYNC_OK");
        }
    }
}
