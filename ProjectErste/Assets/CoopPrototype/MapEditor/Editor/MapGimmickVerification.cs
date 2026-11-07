using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    [InitializeOnLoad]
    public static class MapGimmickVerification
    {
        const string Key = "ShapeCoop.GimmickVerification";
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static bool run;
        static double thawDeadline;
        static PrototypePlayer autoThaw, manualHold;
        static MapGimmickVerification()
        {
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) run = true; };
            EditorApplication.update += Update;
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Run()
        {
            try { MapEditorInstaller.EnsureAssets(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Tick(MapGimmick gimmick, int n, params PrototypePlayer[] players)
        {
            for (int i = 0; i < n; i++)
            {
                foreach (var player in players) if (player != null) typeof(PrototypePlayer).GetMethod("FixedUpdate", Private).Invoke(player, null);
                typeof(MapGimmick).GetMethod("FixedUpdate", Private).Invoke(gimmick, null);
                Physics2D.Simulate(.02f);
            }
        }
        static MapGimmick Make(MapGimmickKind kind, string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(MapGimmickInstaller.Folder + "/" + name + ".prefab");
            Check(asset != null, "missing prefab: " + name);
            var gimmick = Object.Instantiate(asset).GetComponent<MapGimmick>();
            Check(gimmick != null && gimmick.kind == kind, "wrong prefab component: " + name);
            gimmick.Initialize(true); return gimmick;
        }
        static Rigidbody2D Ball(Vector2 position, Vector2 velocity)
        {
            var go = new GameObject("verification ball"); var rb = go.AddComponent<Rigidbody2D>();
            rb.position = position; rb.gravityScale = 0; rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; rb.linearVelocity = velocity;
            go.AddComponent<CircleCollider2D>().radius = .6f; go.AddComponent<MapGoalBall>(); return rb;
        }
        static PrototypePlayer Player(Vector2 position, int shape = 1)
        {
            var go = new GameObject("verification player"); var player = go.AddComponent<PrototypePlayer>();
            var config = new GameObject("verification settings").AddComponent<PrototypeSettings>(); config.gravityScale = 0;
            player.Initialize(config, new PlayerData { id = "test", name = "test", shape = shape }, position, true); return player;
        }
        static void Clear()
        {
            foreach (var obj in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)) if (obj != null && obj.transform.parent == null) Object.DestroyImmediate(obj);
        }
        static void Update()
        {
            if (thawDeadline > 0 && EditorApplication.isPlaying && EditorApplication.timeSinceStartup >= thawDeadline)
            {
                try
                {
                    Check(autoThaw != null && !autoThaw.forcedFrozen && !autoThaw.frozen && autoThaw.body.bodyType == RigidbodyType2D.Dynamic, "timed freeze did not thaw");
                    Check(manualHold != null && !manualHold.forcedFrozen && manualHold.frozen, "manual freeze should remain after gas expires");
                    manualHold.freezePending = true; typeof(PrototypePlayer).GetMethod("FixedUpdate", Private).Invoke(manualHold, null);
                    Check(!manualHold.frozen, "manual freeze could not be released after thaw");
                    Debug.Log("SHAPE_GIMMICK_VERIFICATION_OK"); SessionState.SetBool(Key, false); thawDeadline = 0; EditorApplication.Exit(0);
                }
                catch (Exception e) { Debug.LogException(e); SessionState.SetBool(Key, false); thawDeadline = 0; EditorApplication.Exit(1); }
                return;
            }
            if (!run || !EditorApplication.isPlaying) return; run = false;
            var previous = Physics2D.simulationMode; Physics2D.simulationMode = SimulationMode2D.Script;
            try
            {
                var sand = Make(MapGimmickKind.Sand, "무너지는 모래 상자"); sand.activationDelay = .12f;
                var p = Player(new Vector2(0, 1.02f)); p.body.gravityScale = 1; Physics2D.SyncTransforms(); Tick(sand, 35, p);
                Check(sand.Capture(0).broken, "sand did not collapse after player contact"); sand.ResetGimmick(); Check(!sand.Capture(0).broken && sand.GetComponent<Collider2D>().enabled, "sand restart failed"); Clear();

                sand = Make(MapGimmickKind.Sand, "무너지는 모래 상자"); sand.activationDelay = .12f;
                var sandBall = Ball(new Vector2(0, 1.12f), Vector2.down * 2); Physics2D.SyncTransforms(); Tick(sand, 35);
                Check(sand.Capture(0).broken && !sand.GetComponent<Collider2D>().enabled, "sand did not collapse after ball contact"); sand.ResetGimmick(); Check(sand.GetComponent<Collider2D>().enabled, "sand did not restore after ball contact"); Clear();

                var vent = Make(MapGimmickKind.Vent, "공기 환풍구"); var ball = Ball(new Vector2(0, 1), Vector2.zero); Physics2D.SyncTransforms(); Tick(vent, 8);
                Check(ball.linearVelocity.y > 0, "vent did not lift ball");
                var air = vent.visual.Find("Air"); var wind = air.GetComponent<WindVentEffect>(); var windRenderer = air.GetComponent<SpriteRenderer>();
                Check(wind != null && wind.frames.Length == 8 && windRenderer.sprite == wind.frames[0], "wind animation missing or initial frame incorrect");
                typeof(WindVentEffect).GetMethod("Advance", Private).Invoke(wind, new object[] { .125f });
                Check(windRenderer.sprite == wind.frames[1], "wind did not animate in play mode"); vent.ResetGimmick(); Check(windRenderer.sprite == wind.frames[0], "wind restart did not reset animation"); Clear();

                var cement = Make(MapGimmickKind.Cement, "무거운 시멘트 포대"); ball = Ball(new Vector2(0,1.5f), Vector2.down * 8); Physics2D.SyncTransforms(); Tick(cement, 6);
                Check(ball.linearVelocity.magnitude < .01f && Mathf.Abs(ball.angularVelocity) < .01f, "cement failed to stop ball"); Clear();

                var wall = Make(MapGimmickKind.BrickWall, "부실공사 벽돌 벽"); wall.impactSpeed = 8; ball = Ball(new Vector2(-1.5f,0), Vector2.right * 20); Physics2D.SyncTransforms(); Tick(wall, 5);
                Check(wall.Capture(0).broken && !wall.GetComponent<Collider2D>().enabled, "fast ball did not break wall"); wall.ResetGimmick(); Check(wall.GetComponent<Collider2D>().enabled, "wall restart failed"); Clear();

                var pulley = Make(MapGimmickKind.Pulley, "도르래 연동 발판"); p = Player(new Vector2(-1.5f,.68f)); Physics2D.SyncTransforms(); Tick(pulley, 20, p);
                Check(pulley.Capture(0).offset > .05f, "weighted pulley did not move"); Clear();

                var oil = Make(MapGimmickKind.Oil, "기름 웅덩이"); p = Player(new Vector2(0,.4f)); Physics2D.SyncTransforms(); Tick(oil, 3, p);
                Check(p.shapeCollider.sharedMaterial != null && p.shapeCollider.sharedMaterial.friction == 0, "oil friction did not apply");
                p.dashPending = true; Tick(oil, 1, p); var dash = (float)typeof(PrototypePlayer).GetField("dashRemaining", Private).GetValue(p);
                Check(dash > .2f, "oil dash duration was not extended"); p.ResetBody(); Check(p.shapeCollider.sharedMaterial == null, "oil material not reset"); Clear();

                var crane = Make(MapGimmickKind.Crane, "움직이는 타워크레인 빔"); Tick(crane, 10); Check(Mathf.Abs(crane.Capture(0).angle) > 1, "crane did not rotate"); Clear();

                var magnet = Make(MapGimmickKind.Magnet, "산업용 대형 자석"); magnet.inactiveSeconds = .02f; magnet.activeSeconds = 10;
                ball = Ball(new Vector2(0,-1), Vector2.zero); Physics2D.SyncTransforms(); Tick(magnet, 8); Check(ball.linearVelocity.y > 0, "magnet failed to pull ball upward"); Clear();

                var nitrogen = Make(MapGimmickKind.Nitrogen, "액체 질소 가이저"); nitrogen.inactiveSeconds = .02f; nitrogen.activeSeconds = 10;
                p = Player(new Vector2(0,1)); Physics2D.SyncTransforms(); Tick(nitrogen, 6, p);
                Check(p.forcedFrozen && p.body.bodyType == RigidbodyType2D.Kinematic, "nitrogen did not force freeze"); p.ResetBody(); Check(!p.frozen && !p.forcedFrozen, "forced freeze did not reset"); Clear();

                var sparks = Make(MapGimmickKind.Sparks, "용접 스파크 구역"); sparks.inactiveSeconds = .02f; sparks.activeSeconds = 10;
                var shield = Player(new Vector2(0,-1)); var behind = Player(new Vector2(0,-3)); Physics2D.SyncTransforms(); Tick(sparks, 8, shield, behind);
                Check(shield.stunned && !behind.stunned, "spark did not stop at first player");
                shield.freezePending = true; shield.dashPending = true; Tick(sparks, 1, shield, behind);
                Check(!shield.frozen && !shield.dashPending, "stunned player could start an action"); Clear();
                var fixedPlayer = Player(new Vector2(0,0)); fixedPlayer.freezePending = true;
                typeof(PrototypePlayer).GetMethod("FixedUpdate", Private).Invoke(fixedPlayer, null); fixedPlayer.Stun(1); fixedPlayer.freezePending = true;
                typeof(PrototypePlayer).GetMethod("FixedUpdate", Private).Invoke(fixedPlayer, null);
                Check(!fixedPlayer.frozen, "stun prevented releasing an existing manual freeze"); Clear();

                var water = Make(MapGimmickKind.Sprinkler, "수압 장벽"); var triangle = Player(new Vector2(-.55f,-1), 0); triangle.freezePending = true;
                var covered = Ball(new Vector2(-.55f,-2.5f), Vector2.zero); var exposed = Ball(new Vector2(.75f,-2.5f), Vector2.zero);
                Physics2D.SyncTransforms(); Tick(water, 8, triangle);
                Check(exposed.linearVelocity.y < -.2f && covered.linearVelocity.y > exposed.linearVelocity.y + .1f && water.Capture(0).shielded, "triangle did not shield ball from water"); Clear();

                var host = Make(MapGimmickKind.BrickWall, "부실공사 벽돌 벽"); var copy = Object.Instantiate(host.gameObject); var client = copy.GetComponent<MapGimmick>(); client.Initialize(false);
                var state = host.Capture(0); state.broken = true;
                var packet = JsonUtility.FromJson<Packet>(JsonUtility.ToJson(new Packet { kind = "state", gimmicks = new[] { state } }));
                Check(packet.gimmicks != null && packet.gimmicks.Length == 1 && packet.gimmicks[0].broken, "gimmick state packet did not round trip");
                client.Apply(packet.gimmicks[0], true); Check(!client.GetComponent<Collider2D>().enabled, "client did not apply broken state");
                state.broken = false; client.Apply(state, true); Check(client.GetComponent<Collider2D>().enabled, "client reset did not restore wall"); Clear();
                autoThaw = Player(new Vector2(-5,0)); autoThaw.ForceFreeze(.12f);
                manualHold = Player(new Vector2(5,0)); manualHold.freezePending = true; typeof(PrototypePlayer).GetMethod("FixedUpdate", Private).Invoke(manualHold, null); manualHold.ForceFreeze(.12f);
                Check(autoThaw.frozen && manualHold.frozen, "timed freeze did not start");
                Physics2D.simulationMode = previous; thawDeadline = EditorApplication.timeSinceStartup + .5;
            }
            catch (Exception e) { Debug.LogException(e); SessionState.SetBool(Key, false); Physics2D.simulationMode = previous; EditorApplication.Exit(1); }
        }
    }
}
