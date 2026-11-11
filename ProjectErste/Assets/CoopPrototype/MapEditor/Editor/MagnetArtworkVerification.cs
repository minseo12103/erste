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
    public static class MagnetArtworkVerification
    {
        const string Key = "ShapeCoop.MagnetArtworkVerification";
        const string Prefab = MapGimmickInstaller.Folder + "/산업용 대형 자석.prefab";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static bool ready;
        static MagnetArtworkVerification()
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
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/__MagnetVerification.prefab");
            var section = new GameObject("magnet section"); var map = new GameObject("assembled map");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(prefab != null, "magnet prefab missing");
                section.AddComponent<MapSection>(); var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab); placed.transform.SetParent(section.transform, false);
                PrefabUtility.SaveAsPrefabAsset(section, path, out bool saved); Check(saved, "could not export magnet section");
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MapSection>();
                Check(MapSectionAssembler.Build(new[] { new MapSectionPlacement { prefab = asset, position = new Vector2(4, 2) } }, map.transform, null, out _, out _), "could not assemble magnet section");
                var visual = map.GetComponentInChildren<MagnetFieldVisual>(); Check(visual != null && visual.fieldFrames.Length == 4 && visual.deviceOff != null && visual.area != null, "assembled section lost magnet artwork");
            }
            finally { Object.DestroyImmediate(section); Object.DestroyImmediate(map); AssetDatabase.DeleteAsset(path); }
        }
        static void Step(MapGimmick g, int count)
        {
            var update = typeof(MapGimmick).GetMethod("FixedUpdate", Private); Physics2D.SyncTransforms();
            for (int i = 0; i < count; i++) { update.Invoke(g, null); Physics2D.Simulate(.02f); }
        }
        static Rigidbody2D Ball(Vector2 position)
        {
            var obj = new GameObject("goal ball"); var rb = obj.AddComponent<Rigidbody2D>(); rb.position = position; rb.gravityScale = 0;
            obj.AddComponent<CircleCollider2D>().radius = .5f; obj.AddComponent<MapGoalBall>(); return rb;
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
                var image = host.visual.GetComponent<MagnetFieldVisual>(); var other = remote.visual.GetComponent<MagnetFieldVisual>(); var area = host.GetComponent<BoxCollider2D>();
                Check(area.isTrigger && area.size == new Vector2(4, 5) && area.offset == new Vector2(0, -2.5f) && host.force == 28 && host.activeSeconds == 1 && host.inactiveSeconds == 2 && host.transform.Find("Body colliders (physics)/Magnet housing").GetComponent<BoxCollider2D>().isTrigger == false, "magnet trigger or housing incorrect");
                Check(image != null && image.device.sprite == image.deviceOff && image.field.sprite == null && image.fieldFrames.Length == 4 && image.deviceOnFrames.Length == 2, "initial magnet artwork missing");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-magnetCapture") >= 0) Capture("off");
                host.inactiveSeconds = .1f; host.activeSeconds = .4f;
                var ball = Ball(new Vector2(-.8f, -2)); var outside = Ball(new Vector2(3.5f, -2));
                var settings = new GameObject("settings").AddComponent<PrototypeSettings>(); settings.gravityScale = 0;
                var player = new GameObject("player").AddComponent<PrototypePlayer>();
                player.Initialize(settings, new PlayerData { shape = 1, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, new Vector2(1.1f,-2), true); player.body.gravityScale = 0;
                Step(host, 4); Check(!host.Capture(0).active && ball.linearVelocity.sqrMagnitude < .001f && player.body.linearVelocity.sqrMagnitude < .001f, "inactive magnet applied force");
                Step(host, 4); var state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                Check(state.active && state.progress > 0 && ball.linearVelocity.y > 0 && player.body.linearVelocity.y > 0 && outside.linearVelocity.sqrMagnitude < .001f, "magnet force or range incorrect");
                Check(!player.frozen && !player.forcedFrozen && !player.stunned && image.field.sprite != null && image.device.sprite != image.deviceOff, "active visual or player state incorrect");
                remote.Apply(state, true); Check(other.field.sprite == image.field.sprite && other.device.sprite == image.device.sprite, "client magnet visual disagrees with host");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-magnetCapture") >= 0) Capture("active");
                for (int i = 0; i < 4; i++) { image.Show(true, (i + .1f) / image.framesPerSecond); Check(image.field.sprite == image.fieldFrames[i] && image.device.sprite == image.deviceOnFrames[i % 2], "wrong magnetic field animation frame"); }
                area.size = new Vector2(3, 4); area.offset = new Vector2(.5f, -2.5f); image.RefreshGeometry();
                Check(image.field.transform.localPosition == new Vector3(.5f, -2.5f, 0) && Vector2.Distance(Vector2.Scale(image.field.sprite.bounds.size, image.field.transform.localScale), area.size) < .001f, "field does not follow adjusted trigger");
                area.size = new Vector2(4, 5); area.offset = new Vector2(0, -2.5f); Step(host, 20);
                state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0))); Check(!state.active && image.field.sprite == null && image.device.sprite == image.deviceOff, "deactivation did not hide field");
                remote.Apply(state, false); Check(other.field.sprite == null && other.device.sprite == other.deviceOff, "client deactivation incorrect");
                host.ResetGimmick(); remote.Apply(host.Capture(0), true); Check(!host.Capture(0).active && image.field.sprite == null && other.field.sprite == null && image.device.sprite == image.deviceOff, "restart did not reset magnet");
                Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_MAGNET_VERIFICATION_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Capture(string name)
        {
            var obj = new GameObject("magnet preview camera"); var camera = obj.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 4.8f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f); camera.transform.position = new Vector3(0, -1.7f, -10);
            var target = new RenderTexture(650, 900, 24); camera.targetTexture = target; camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(650, 900, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 650, 900), 0, 0); texture.Apply(); File.WriteAllBytes("Library/MagnetPreview_" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(obj);
        }
    }
}
