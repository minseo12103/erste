using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    public static class PulleyArtworkVerification
    {
        const string Prefab = MapGimmickInstaller.Folder + "/도르래 연동 발판.prefab";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        static void Tick(MapGimmick g, int steps)
        {
            var physics = typeof(MapGimmick).GetMethod("FixedUpdate", Private);
            var visual = typeof(MapGimmick).GetMethod("LateUpdate", Private);
            Physics2D.SyncTransforms();
            for (int i = 0; i < steps; i++) { physics.Invoke(g, null); Physics2D.Simulate(.02f); visual.Invoke(g, null); }
        }
        public static void Run()
        {
            var oldMode = Physics2D.simulationMode;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Physics2D.simulationMode = SimulationMode2D.Script;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(asset != null, "pulley prefab missing");
                var host = Object.Instantiate(asset).GetComponent<MapGimmick>(); host.Initialize(true);
                var remote = Object.Instantiate(asset, new Vector3(12, 0), Quaternion.identity).GetComponent<MapGimmick>(); remote.Initialize(false);
                Check(host.platformA.bodyType == RigidbodyType2D.Kinematic && host.platformA.GetComponent<BoxCollider2D>().size == new Vector2(2, .35f), "platform physics changed");
                Check(host.platformA.transform.Find("Visual").GetComponent<SpriteRenderer>().sprite.name == "Pulley_Platform" && host.platformB.transform.Find("Visual").GetComponent<SpriteRenderer>().sprite.name == "Pulley_Platform", "plate artwork missing");
                Check(host.visual.Find("Top").GetComponent<SpriteRenderer>().sprite.name == "Pulley_Wheel" && host.visual.Find("Right wheel").GetComponent<SpriteRenderer>().flipX && host.visual.Find("Connecting rope").GetComponent<SpriteRenderer>().sprite.name == "Pulley_RopeHorizontal", "pulley artwork missing");
                var leftRope = host.ropeA.GetComponent<SpriteRenderer>(); var rightRope = host.ropeB.GetComponent<SpriteRenderer>();
                Check(leftRope.sprite.name == "Pulley_RopeVertical" && rightRope.sprite == leftRope.sprite && leftRope.drawMode == SpriteDrawMode.Tiled, "rope artwork missing");
                Check(host.ropeA.localScale == Vector3.one && host.ropeB.localScale == Vector3.one, "rope tiling has an unexpected scale");
                var settings = new GameObject("settings").AddComponent<PrototypeSettings>();
                var player = new GameObject("player").AddComponent<PrototypePlayer>();
                player.Initialize(settings, new PlayerData { shape = 1, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, new Vector2(-1.5f, .68f), true);
                Tick(host, 20);
                float offset = host.Capture(0).offset;
                Check(offset > .05f && host.platformA.position.y < 0 && host.platformB.position.y > 0, "player did not move both platforms in opposite directions");
                Check(leftRope.size.y > rightRope.size.y && Math.Abs((host.ropeA.localPosition.y - leftRope.size.y / 2) - (host.platformA.transform.localPosition.y + .42f)) < .02f, "ropes did not follow platforms");
                remote.Apply(JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0))), true);
                Check(Mathf.Abs(remote.platformA.transform.localPosition.y - host.platformA.transform.localPosition.y) < .02f && Mathf.Abs(remote.platformB.transform.localPosition.y - host.platformB.transform.localPosition.y) < .02f, "client did not apply host platform positions");
                Object.DestroyImmediate(player.gameObject); host.ResetGimmick(); remote.Apply(host.Capture(0), true);
                Check(Mathf.Abs(host.platformA.transform.localPosition.y) < .01f && Mathf.Abs(host.platformB.transform.localPosition.y) < .01f && Mathf.Abs(remote.platformA.transform.localPosition.y) < .01f, "restart did not restore platforms");
                var ball = new GameObject("ball"); ball.transform.position = new Vector3(1.5f, .78f, 0);
                var ballBody = ball.AddComponent<Rigidbody2D>(); ballBody.mass = 1; ball.AddComponent<CircleCollider2D>().radius = .6f; ball.AddComponent<MapGoalBall>();
                Tick(host, 20); Check(host.Capture(0).offset < -.05f && host.platformB.position.y < 0 && host.platformA.position.y > 0, "ball did not move both platforms in opposite directions");
                Object.DestroyImmediate(ball); host.ResetGimmick(); typeof(MapGimmick).GetMethod("LateUpdate", Private).Invoke(host, null);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pulleyCapture") >= 0) Capture();
                Object.DestroyImmediate(settings.gameObject); Object.DestroyImmediate(host.gameObject); Object.DestroyImmediate(remote.gameObject);
                Physics2D.simulationMode = oldMode; Debug.Log("SHAPE_PULLEY_ARTWORK_VERIFICATION_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = oldMode; Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Capture()
        {
            var obj = new GameObject("pulley preview camera"); var camera = obj.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 3; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f);
            camera.transform.position = new Vector3(0, 1.3f, -10);
            var target = new RenderTexture(750, 650, 24); camera.targetTexture = target; camera.Render();
            var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(750, 650, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 750, 650), 0, 0); texture.Apply(); File.WriteAllBytes("Library/PulleyArtworkPreview.png", texture.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(obj);
        }
    }
}
