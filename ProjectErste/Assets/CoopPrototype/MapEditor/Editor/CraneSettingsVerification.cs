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
    public static class CraneSettingsVerification
    {
        const string Key = "ShapeCoop.CraneSettingsVerification";
        const string Prefab = MapGimmickInstaller.Folder + "/움직이는 타워크레인 빔.prefab";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static bool ready;
        static CraneSettingsVerification()
        {
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) ready = true; };
            EditorApplication.update += Tick;
        }
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static bool Near(float a, float b) => Mathf.Abs(a - b) < .001f;
        static void CheckDimensions(RotatingCranePlatforms c, float radius, Vector2 size, float speed)
        {
            var g = c.GetComponent<MapGimmick>();
            Check(Near(c.Radius, radius) && (c.PlatformSize - size).sqrMagnitude < .00001f && Near(g.rotationSpeed, speed), "crane parameters incorrect");
            Check(Near(g.rotatingBeam.transform.Find("Visual").GetComponent<SpriteRenderer>().size.x, radius * 2 + .88f), "spoke length did not follow radius");
            foreach (var platform in c.platforms)
            {
                var hitbox = platform.GetComponent<BoxCollider2D>(); var art = platform.transform.Find("Platform art");
                Check((hitbox.size - size).sqrMagnitude < .00001f && Near(hitbox.offset.y + hitbox.size.y * .5f, art.localPosition.y), "plate art and collider dimensions diverged");
                Check(Near(art.localScale.x, size.x / 1.12f) && Near(art.localScale.y, size.y / .19f), "plate artwork did not resize");
            }
        }
        static void VerifyEditor()
        {
            MapSection section = null; string directory = null;
            try
            {
                section = MapEditorWindow.CreateSection("__CraneSettingsCheck", false);
                Check(section != null, "could not create test map section"); directory = Path.GetDirectoryName(section.gameObject.scene.path).Replace('\\', '/');
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(source != null, "crane prefab missing");
                var first = MapEditorWindow.PlacePrefab(section, source, Vector3.zero, 0).GetComponent<RotatingCranePlatforms>();
                var second = MapEditorWindow.PlacePrefab(section, source, new Vector3(12, 0), 0).GetComponent<RotatingCranePlatforms>();
                var inspector = UnityEditor.Editor.CreateEditor(first);
                try { Check(inspector is CraneSettingsEditor, "crane settings inspector is not registered"); }
                finally { Object.DestroyImmediate(inspector); }
                Selection.activeGameObject = first.gameObject;
                Check(Selection.activeTransform.GetComponentInParent<RotatingCranePlatforms>() == first && first.transform.IsChildOf(section.transform), "map editor selection cannot find crane settings");
                CheckDimensions(first, 2.15f, new Vector2(1.12f, .19f), -45f);
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                CraneSettingsGUI.Apply(first, -70f, 3.4f, new Vector2(1.8f, .25f)); Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                CheckDimensions(first, 3.4f, new Vector2(1.8f, .25f), -70f);
                Check((first.platforms[0].transform.localPosition - Vector3.right * 3.4f).sqrMagnitude < .0001f, "scene preview did not update");
                CheckDimensions(second, 2.15f, new Vector2(1.12f, .19f), -45f);
                Undo.PerformUndo(); CheckDimensions(first, 2.15f, new Vector2(1.12f, .19f), -45f);
                Undo.PerformRedo(); CheckDimensions(first, 3.4f, new Vector2(1.8f, .25f), -70f);
                Check(MapEditorWindow.ExportSection(section, false), "section export failed");
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(section.prefabPath).GetComponent<MapSection>();
                var built = new GameObject("test assembled map");
                try
                {
                    Check(MapSectionAssembler.Build(new[] { new MapSectionPlacement { prefab = asset, position = Vector2.zero } }, built.transform, null, out _, out _), "section assembly failed");
                    var cranes = built.GetComponentsInChildren<RotatingCranePlatforms>(); Check(cranes.Length == 2, "independent crane instances missing");
                    CheckDimensions(cranes[0], 3.4f, new Vector2(1.8f, .25f), -70f); CheckDimensions(cranes[1], 2.15f, new Vector2(1.12f, .19f), -45f);
                }
                finally { Object.DestroyImmediate(built); }
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!string.IsNullOrEmpty(directory)) AssetDatabase.DeleteAsset(directory);
            }
        }
        public static void Run()
        {
            try { VerifyEditor(); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Step(MapGimmick g, int count, PrototypePlayer player = null)
        {
            var tick = typeof(MapGimmick).GetMethod("FixedUpdate", Private); var playerTick = typeof(PrototypePlayer).GetMethod("FixedUpdate", Private);
            Physics2D.SyncTransforms(); for (int i = 0; i < count; i++) { if (player != null) playerTick.Invoke(player, null); tick.Invoke(g, null); Physics2D.Simulate(.02f); }
        }
        static void Tick()
        {
            if (!ready || !EditorApplication.isPlaying) return; ready = false; var mode = Physics2D.simulationMode;
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
                var host = Object.Instantiate(source).GetComponent<MapGimmick>(); var remote = Object.Instantiate(source, new Vector3(12, 0), Quaternion.identity).GetComponent<MapGimmick>();
                var wheel = host.GetComponent<RotatingCranePlatforms>(); var remoteWheel = remote.GetComponent<RotatingCranePlatforms>();
                wheel.SetDimensions(3.4f, new Vector2(1.8f, .25f)); host.rotationSpeed = -70; host.Initialize(true); remote.Initialize(false);
                Step(host, 25); var state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0))); remote.Apply(state, true);
                Check(state.hasCraneDimensions && Near(state.angle, -35) && Near(state.craneRadius, 3.4f), "host speed or settings not captured");
                CheckDimensions(remoteWheel, 3.4f, new Vector2(1.8f, .25f), -70f);
                for (int i = 0; i < 6; i++)
                {
                    float a = (state.angle + i * 60) * Mathf.Deg2Rad; Vector2 expected = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.4f;
                    Check((wheel.platforms[i].position - expected).sqrMagnitude < .001f && ((Vector2)remoteWheel.platforms[i].transform.position - expected - Vector2.right * 12).sqrMagnitude < .001f, "custom radius was not synchronized");
                    Check(Mathf.Abs(Mathf.DeltaAngle(remoteWheel.platforms[i].transform.eulerAngles.z, 0)) < .01f, "platform lost horizontal orientation");
                }
                host.ResetGimmick(); remote.Apply(host.Capture(0), true); CheckDimensions(wheel, 3.4f, new Vector2(1.8f, .25f), -70f); CheckDimensions(remoteWheel, 3.4f, new Vector2(1.8f, .25f), -70f);
                host.rotationSpeed = 30; Step(host, 10); Check(Near(host.Capture(0).angle, 6), "positive rotation speed did not turn counterclockwise");
                remote.Apply(host.Capture(0), true); Check(Near(remote.rotationSpeed, 30), "client did not receive changed speed");
                host.ResetGimmick(); host.rotationSpeed = -70; remote.Apply(host.Capture(0), true);
                var settings = new GameObject("settings").AddComponent<PrototypeSettings>(); settings.gravityScale = 2;
                var rider = new GameObject("rider").AddComponent<PrototypePlayer>(); var platform = wheel.platforms[1];
                host.rotationSpeed = 0; rider.Initialize(settings, new PlayerData { shape = 1, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, platform.position + Vector2.up * (.49f * (.25f / .19f) + .53f), true);
                Step(host, 30, rider); Check(rider.shapeCollider.IsTouching(platform.GetComponent<Collider2D>()), "rider did not land on resized platform");
                host.rotationSpeed = -70; for (int i = 0; i < 100; i++) { Step(host, 1, rider); Check(Mathf.Abs(rider.body.position.x - platform.position.x) < .95f, "rider fell from resized platform"); }
                Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_CRANE_SETTINGS_VERIFICATION_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
