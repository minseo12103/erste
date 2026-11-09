using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    [InitializeOnLoad]
    public static class SeesawVerification
    {
        const string Key = "ShapeCoop.SeesawVerification";
        const string Prefab = MapGimmickInstaller.Folder + "/철골 시소 발판.prefab";
        static bool ready;
        static PrototypeSettings settings;
        static SeesawVerification()
        {
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) ready = true; };
            EditorApplication.update += Tick;
        }
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        public static void Run()
        {
            try { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); VerifySectionAssembly(); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        static void VerifySectionAssembly()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/__SeesawVerification.prefab");
            var root = new GameObject("seesaw section");
            var assembled = new GameObject("assembled map");
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(source != null, "seesaw source prefab missing");
                root.AddComponent<MapSection>();
                var placed = (GameObject)PrefabUtility.InstantiatePrefab(source); placed.transform.SetParent(root.transform, false);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved); Check(saved, "could not export seesaw section");
                var section = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MapSection>();
                Check(MapSectionAssembler.Build(new[] { new MapSectionPlacement { prefab = section, position = new Vector2(8, 2) } }, assembled.transform, null, out _, out _), "could not assemble seesaw section");
                var result = assembled.GetComponentInChildren<MapGimmick>();
                Check(result != null && result.kind == MapGimmickKind.Seesaw && result.rotatingBeam.GetComponent<HingeJoint2D>().connectedBody == result.GetComponent<Rigidbody2D>(), "assembled seesaw lost its pivot connection");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(assembled); AssetDatabase.DeleteAsset(path); }
        }
        static MapGimmick Make(Vector2 position, float rotation = 0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(prefab != null, "seesaw prefab missing");
            var instance = Object.Instantiate(prefab, position, Quaternion.Euler(0, 0, rotation));
            var gimmick = instance.GetComponent<MapGimmick>(); gimmick.Initialize(true); return gimmick;
        }
        static Rigidbody2D Load(float x, bool ball = false)
        {
            var go = new GameObject(ball ? "Goal ball" : "Player");
            var position = new Vector2(x, ball ? .93f : .83f);
            Rigidbody2D rb;
            if (ball) { go.transform.position = position; rb = go.AddComponent<Rigidbody2D>(); rb.mass = settings.ballMass; rb.gravityScale = settings.gravityScale; go.AddComponent<CircleCollider2D>().radius = .6f; go.AddComponent<MapGoalBall>(); }
            else { var player = go.AddComponent<PrototypePlayer>(); player.Initialize(settings, new PlayerData { shape = 1, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, position, true); rb = player.body; }
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            return rb;
        }
        static void Simulate(int steps) { Physics2D.SyncTransforms(); for (int i = 0; i < steps; i++) Physics2D.Simulate(.02f); }
        static float Sample(float left, float right, int steps = 18)
        {
            var seesaw = Make(Vector2.zero);
            Rigidbody2D a = null, b = null;
            if (!float.IsNaN(left)) a = Load(left);
            if (!float.IsNaN(right)) b = Load(right, true);
            Simulate(steps); float angle = seesaw.Capture(0).angle;
            if (a != null) Object.DestroyImmediate(a.gameObject); if (b != null) Object.DestroyImmediate(b.gameObject);
            Object.DestroyImmediate(seesaw.gameObject); Physics2D.SyncTransforms(); return angle;
        }
        static void Tick()
        {
            if (!ready || !EditorApplication.isPlaying) return;
            ready = false; var previousMode = Physics2D.simulationMode;
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                settings = new GameObject("Settings").AddComponent<PrototypeSettings>(); settings.gravityScale = 2;
                Check(Mathf.Approximately(settings.playerMass, 1) && Mathf.Approximately(settings.ballMass, 1), "default masses must be one");
                var system = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CoopPrototype/Prefabs/PrototypeSystem.prefab");
                Check(system != null && Mathf.Approximately(system.GetComponent<PrototypeSettings>().ballMass, 1), "game prefab ball mass must be one");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(prefab != null, "seesaw prefab missing");
                Check(MapEditorWindow.FindPaletteItems().Any(item => item.prefab == prefab) && MapPaletteTags.GetTags(prefab).Contains("기능 오브젝트"), "seesaw missing from palette");
                var seesaw = Make(new Vector2(12, 3), 20); var beam = seesaw.rotatingBeam; var hinge = beam.GetComponent<HingeJoint2D>();
                Check(beam.bodyType == RigidbodyType2D.Dynamic && hinge != null && hinge.connectedBody == seesaw.GetComponent<Rigidbody2D>() && hinge.useLimits, "seesaw is not a dynamic hinged platform");
                Check(beam.GetComponentInChildren<SpriteRenderer>().sprite.name == "Seesaw_Beam" && seesaw.visual.GetComponentInChildren<SpriteRenderer>().sprite.name == "Seesaw_Support", "provided seesaw artwork not assigned");
                Check(Mathf.Abs(seesaw.Capture(0).angle) < .01f, "rotated placement should start at its authored angle");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-seesawCapture") >= 0) { var preview = Make(Vector2.zero); Capture(preview); Object.DestroyImmediate(preview.gameObject); }
                beam.angularVelocity = 50; Simulate(15); var state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(seesaw.Capture(0)));
                Check(Mathf.Abs(state.angle) > 1 && Mathf.Abs(state.angle) < 27, "host angle did not reflect hinged physics");
                var clientObject = Object.Instantiate(prefab, new Vector3(18, 3, 0), Quaternion.Euler(0, 0, 20)); var client = clientObject.GetComponent<MapGimmick>(); client.Initialize(false); client.Apply(state, true);
                Check(!client.rotatingBeam.simulated && Mathf.Abs(Mathf.DeltaAngle(client.rotatingBeam.transform.localEulerAngles.z, state.angle)) < .1f, "client seesaw angle did not match host");
                seesaw.ResetGimmick(); client.Apply(seesaw.Capture(0), true);
                Check(Mathf.Abs(seesaw.Capture(0).angle) < .05f && beam.linearVelocity.sqrMagnitude < .0001f && Mathf.Abs(beam.angularVelocity) < .01f && Mathf.Abs(Mathf.DeltaAngle(client.rotatingBeam.transform.localEulerAngles.z, 0)) < .1f, "seesaw restart did not reset angle and velocity");
                Object.DestroyImmediate(clientObject); Object.DestroyImmediate(seesaw.gameObject); Physics2D.SyncTransforms();
                float near = Sample(float.NaN, 1f), far = Sample(float.NaN, 3f), left = Sample(-3f, float.NaN), balanced = Sample(-2.5f, 2.5f), uneven = Sample(-1f, 3f);
                Debug.Log("SHAPE_SEESAW_MOMENTS near=" + near + " far=" + far + " left=" + left + " balanced=" + balanced + " uneven=" + uneven);
                Check(near < -.05f && far < near * 1.4f && left > .05f, "tilt should follow side and distance from the pivot");
                Check(Mathf.Abs(balanced) < 2f && uneven < -.3f, "equal masses should balance at equal distances and tip toward the longer lever");
                Object.DestroyImmediate(settings.gameObject); Physics2D.simulationMode = previousMode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_SEESAW_VERIFICATION_OK"); EditorApplication.Exit(0);
            }
            catch (Exception error) { Physics2D.simulationMode = previousMode; SessionState.SetBool(Key, false); Debug.LogException(error); EditorApplication.Exit(1); }
        }
        static void Capture(MapGimmick seesaw)
        {
            var obj = new GameObject("Seesaw preview camera"); var camera = obj.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 3.3f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f);
            camera.transform.position = seesaw.transform.position + new Vector3(0, -.6f, -10);
            var target = new RenderTexture(1000, 550, 24); camera.targetTexture = target; camera.Render();
            var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(1000, 550, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1000, 550), 0, 0); texture.Apply(); File.WriteAllBytes("Library/SeesawPreview.png", texture.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(obj);
            Physics2D.SyncTransforms();
        }
    }
}
