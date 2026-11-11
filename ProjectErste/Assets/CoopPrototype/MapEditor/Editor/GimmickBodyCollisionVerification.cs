using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    [InitializeOnLoad]
    public static class GimmickBodyCollisionVerification
    {
        const string Key = "ShapeCoop.GimmickBodyCollisionVerification";
        static bool ready;
        static GimmickBodyCollisionVerification()
        {
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) ready = true; };
            EditorApplication.update += Tick;
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Run()
        {
            try { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); VerifySection(); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void VerifySection()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/__GimmickBodyCollisionVerification.prefab");
            var section = new GameObject("body collision section"); var map = new GameObject("assembled map");
            try
            {
                section.AddComponent<MapSection>(); int i = 0;
                foreach (string name in new[] { "공기 환풍구", "도르래 연동 발판", "용접 스파크 구역", "움직이는 타워크레인 빔", "수압 장벽", "산업용 대형 자석", "액체 질소 가이저" })
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(MapGimmickInstaller.Folder + "/" + name + ".prefab"); Check(asset != null, "missing " + name);
                    var placed = (GameObject)PrefabUtility.InstantiatePrefab(asset); placed.transform.SetParent(section.transform, false); placed.transform.localPosition = Vector3.right * (i++ * 10);
                }
                PrefabUtility.SaveAsPrefabAsset(section, path, out bool saved); Check(saved, "could not save collision section");
                var assetSection = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MapSection>();
                Check(MapSectionAssembler.Build(new[] { new MapSectionPlacement { prefab = assetSection, position = new Vector2(7, 3) } }, map.transform, null, out _, out _), "could not assemble collision section");
                var gimmicks = map.GetComponentsInChildren<MapGimmick>(true); Check(gimmicks.Length == 7, "assembled section lost gimmicks");
                foreach (var g in gimmicks) Check(g.transform.Find("Body colliders (physics)") != null, "assembled section lost body colliders on " + g.name);
            }
            finally { Object.DestroyImmediate(section); Object.DestroyImmediate(map); AssetDatabase.DeleteAsset(path); }
        }
        static GameObject Prefab(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapGimmickInstaller.Folder + "/" + name + ".prefab"); Check(prefab != null, "missing " + name); return Object.Instantiate(prefab);
        }
        static void Probe(GameObject device, Collider2D target, bool player)
        {
            Check(target != null && target.enabled && !target.isTrigger, "solid body collider missing on " + device.name);
            Physics2D.SyncTransforms(); var bounds = target.bounds;
            var test = new GameObject(player ? "collision player" : "collision ball"); Rigidbody2D body; Collider2D collider; PrototypeSettings settings = null;
            Vector2 start = new Vector2(bounds.center.x, bounds.max.y + .8f);
            if (player)
            {
                settings = new GameObject("collision settings").AddComponent<PrototypeSettings>(); settings.gravityScale = 0;
                var p = test.AddComponent<PrototypePlayer>(); p.Initialize(settings, new PlayerData { shape = 1, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, start, true);
                body = p.body; collider = p.shapeCollider;
            }
            else
            {
                body = test.AddComponent<Rigidbody2D>(); body.position = start; body.gravityScale = 0;
                var circle = test.AddComponent<CircleCollider2D>(); circle.radius = .6f; collider = circle; test.AddComponent<MapGoalBall>();
            }
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous; body.linearVelocity = Vector2.down * 4;
            foreach (var other in device.GetComponentsInChildren<Collider2D>(true)) if (other != null && other != target && other.enabled) Physics2D.IgnoreCollision(collider, other);
            Physics2D.SyncTransforms(); bool hit = false;
            for (int i = 0; i < 30; i++) { Physics2D.Simulate(.02f); if (collider.IsTouching(target)) { hit = true; break; } }
            Check(hit, (player ? "player" : "ball") + " passed through " + device.name + "/" + target.name);
            Object.DestroyImmediate(test); if (settings != null) Object.DestroyImmediate(settings.gameObject); Physics2D.SyncTransforms();
        }
        static void CheckDevice(string name, string[] parts, bool trigger)
        {
            var go = Prefab(name); var g = go.GetComponent<MapGimmick>(); var rootCollider = go.GetComponent<Collider2D>();
            if (trigger) Check(rootCollider != null && rootCollider.isTrigger, name + " effect trigger was changed");
            foreach (var part in parts)
            {
                var t = go.transform.Find(part); Check(t != null, name + " missing " + part);
                var collider = t.GetComponent<Collider2D>(); Probe(go, collider, true); Probe(go, collider, false);
            }
            if (trigger) { g.Initialize(true); Check(go.GetComponent<Collider2D>() == rootCollider && rootCollider.isTrigger, "body collider replaced effect trigger"); }
            Object.DestroyImmediate(go); Physics2D.SyncTransforms();
        }
        static void Tick()
        {
            if (!ready || !EditorApplication.isPlaying) return;
            ready = false; var mode = Physics2D.simulationMode;
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                const string body = "Body colliders (physics)/";
                CheckDevice("공기 환풍구", new[] { body + "Vent housing" }, true);
                CheckDevice("도르래 연동 발판", new[] { body + "Pulley wheel 0", body + "Pulley wheel 1", body + "Pulley mount 0", body + "Pulley bracket 0", "Left platform", "Right platform" }, false);
                CheckDevice("용접 스파크 구역", new[] { body + "Welder housing", body + "Welder nozzle" }, false);
                CheckDevice("수압 장벽", new[] { body + "Water pipe" }, true);
                CheckDevice("산업용 대형 자석", new[] { body + "Magnet housing", body + "Magnet mount" }, true);
                CheckDevice("액체 질소 가이저", new[] { body + "Nitrogen pipe horizontal", body + "Nitrogen pipe vertical", body + "Nitrogen valve" }, true);
                CheckDevice("움직이는 타워크레인 빔", new[] { body + "Tower foot", body + "Tower left leg", body + "Tower right leg", body + "Tower crossbar", "Rotating beam/Center gear/Gear collision", "Rotating beam/Visual/Spoke collision", "Rotating beam/Spoke 60/Spoke collision", "Rotating beam/Spoke 120/Spoke collision", "Moving platform 01/Platform art/Hanger left collision", "Moving platform 01/Platform art/Hanger right collision" }, false);
                foreach (string name in new[] { "무너지는 모래 상자", "무거운 시멘트 포대", "부실공사 벽돌 벽", "철골 시소 발판" })
                {
                    var go = Prefab(name); Check(go.GetComponent<Collider2D>() != null && !go.GetComponent<Collider2D>().isTrigger, name + " lost original solid body"); Object.DestroyImmediate(go);
                }
                var oil = Prefab("기름 웅덩이"); var oilColliders = oil.GetComponentsInChildren<Collider2D>(true);
                Check(oilColliders.Length == 1 && oilColliders[0].isTrigger, "oil puddle must remain a non-solid surface effect"); Object.DestroyImmediate(oil);
                var crane = Prefab("움직이는 타워크레인 빔"); var g = crane.GetComponent<MapGimmick>(); var wheel = crane.GetComponent<RotatingCranePlatforms>();
                g.Initialize(true); var spoke = g.rotatingBeam.transform.Find("Visual/Spoke collision").GetComponent<BoxCollider2D>();
                var hanger = wheel.platforms[0].transform.Find("Platform art/Hanger left collision").GetComponent<BoxCollider2D>();
                Check(spoke.attachedRigidbody == g.rotatingBeam && hanger.attachedRigidbody == wheel.platforms[0], "crane collision does not follow moving bodies");
                wheel.SetDimensions(3.1f, new Vector2(1.68f, .38f));
                Check(Mathf.Abs(spoke.size.x - 6.42f) < .001f && Mathf.Abs(hanger.transform.parent.localScale.x - 1.5f) < .001f && Mathf.Abs(hanger.transform.parent.localScale.y - 2f) < .001f, "crane size adjustment did not update body collisions");
                var client = Object.Instantiate(crane, Vector3.right * 20, Quaternion.identity).GetComponent<MapGimmick>(); client.Initialize(false);
                client.Apply(JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(g.Capture(0))), true);
                Check(!client.rotatingBeam.simulated && Mathf.Abs(client.rotatingBeam.transform.Find("Visual/Spoke collision").GetComponent<BoxCollider2D>().size.x - 6.42f) < .001f, "client crane collider configuration differs");
                Object.DestroyImmediate(client.gameObject); Object.DestroyImmediate(crane);
                Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_GIMMICK_BODY_COLLISIONS_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
