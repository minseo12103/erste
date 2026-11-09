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
    public static class CharacterStunVerification
    {
        const string Key = "ShapeCoop.CharacterStunVerification";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static bool ready;
        static CharacterStunVerification()
        {
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) ready = true; };
            EditorApplication.update += Tick;
        }
        static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        public static void Run()
        {
            try { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void VisualStep(PrototypePlayer player)
        {
            var appearance = player.GetComponentInChildren<PrototypeCharacterAppearance>();
            typeof(PrototypeCharacterAppearance).GetMethod("Update", Private).Invoke(appearance, null);
            typeof(CharacterStunVisual).GetMethod("LateUpdate", Private).Invoke(appearance.GetComponent<CharacterStunVisual>(), null);
        }
        static PrototypePlayer Player(PrototypeSettings settings, int shape, Vector2 position, bool host)
        {
            var player = new GameObject("Player " + shape).AddComponent<PrototypePlayer>();
            player.Initialize(settings, new PlayerData { id = "test", name = "test", shape = shape, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, position, host);
            player.body.gravityScale = 0; return player;
        }
        static void Tick()
        {
            if (!ready || !EditorApplication.isPlaying) return;
            ready = false; var mode = Physics2D.simulationMode;
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                var config = Resources.Load<CharacterStunArt>("ShapeCoop/CharacterStunArt");
                Check(config != null && config.arcs.Length == 6 && config.impacts.Length == 4 && config.arcs[0].name == "Welding_Arc_00", "supplied welding artwork not connected");
                var settings = new GameObject("settings").AddComponent<PrototypeSettings>(); settings.gravityScale = 0;
                var host = Player(settings, 1, new Vector2(0, -2), true); var client = Player(settings, 1, new Vector2(10, -2), false);
                var visual = host.GetComponentInChildren<CharacterStunVisual>(); var remoteVisual = client.GetComponentInChildren<CharacterStunVisual>();
                var root = visual.transform; var body = root.Find("Body").GetComponent<SpriteRenderer>(); var helmet = root.Find("Helmet").GetComponent<SpriteRenderer>();
                var left = root.Find("Pupil Left").GetComponent<SpriteRenderer>(); var right = root.Find("Pupil Right").GetComponent<SpriteRenderer>();
                var arc = root.Find("Stun outline core").GetComponent<SpriteRenderer>(); var flash = root.Find("Electric impact").GetComponent<SpriteRenderer>();
                Color normalBody = body.color, normalHat = helmet.color; float mass = host.body.mass;
                Check(left.enabled && right.enabled && !arc.enabled && !flash.enabled && visual.GetComponentsInChildren<Collider2D>(true).Length == 0 && root.Find("Electric arc left") == null && root.Find("Electric arc right") == null && arc.sharedMaterial != null && arc.sharedMaterial.shader.isSupported, "idle art or visual-only separation incorrect");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapGimmickInstaller.Folder + "/용접 스파크 구역.prefab"); var hazard = Object.Instantiate(prefab).GetComponent<MapGimmick>(); hazard.inactiveSeconds = .02f; hazard.activeSeconds = 10; hazard.Initialize(true);
                Physics2D.SyncTransforms(); var fixedStep = typeof(MapGimmick).GetMethod("FixedUpdate", Private);
                for (int i = 0; i < 3; i++) { fixedStep.Invoke(hazard, null); Physics2D.Simulate(.02f); }
                Check(host.stunned, "welding hazard did not stun player"); VisualStep(host);
                var state = JsonUtility.FromJson<BodyData>(JsonUtility.ToJson(host.Capture())); client.Apply(state, true); VisualStep(client);
                Check(!left.enabled && !right.enabled && arc.enabled && arc.sprite == body.sprite && flash.enabled && body.color != normalBody && helmet.color != normalHat && host.body.mass == mass, "stun visual missing or changed physics");
                Check(remoteVisual.transform.Find("Stun outline core").GetComponent<SpriteRenderer>().enabled && !remoteVisual.transform.Find("Pupil Left").GetComponent<SpriteRenderer>().enabled, "client did not show stun state");
                host.move = 1; host.dashPending = true; typeof(PrototypePlayer).GetMethod("FixedUpdate", Private).Invoke(host, null);
                Check(!host.dashPending && Mathf.Abs(host.body.linearVelocity.x) < .01f, "stunned controls were not blocked");
                var startField = typeof(CharacterStunVisual).GetField("started", Private); float oldStart = Time.time - 1; startField.SetValue(visual, oldStart); host.Stun(1.5f); VisualStep(host);
                Check(!flash.enabled && Mathf.Abs((float)startField.GetValue(visual) - oldStart) < .01f && arc.enabled, "repeated stun should not keep restarting initial flash");
                host.ResetBody(); Check(left.enabled && right.enabled && !arc.enabled && !flash.enabled && body.color == normalBody && helmet.color == normalHat, "restart did not immediately restore appearance");
                state.stunned = false; client.Apply(state, true); VisualStep(client);
                Check(remoteVisual.transform.Find("Pupil Left").GetComponent<SpriteRenderer>().enabled && !remoteVisual.transform.Find("Stun outline core").GetComponent<SpriteRenderer>().enabled, "client did not clear stun appearance");
                Object.DestroyImmediate(hazard.gameObject); Object.DestroyImmediate(host.gameObject); Object.DestroyImmediate(client.gameObject);
                var shapes = new PrototypePlayer[3];
                for (int i = 0; i < shapes.Length; i++)
                {
                    shapes[i] = Player(settings, i, new Vector2((i - 1) * 1.65f, 0), true); shapes[i].Stun(1); VisualStep(shapes[i]);
                    shapes[i].transform.position = new Vector3((i - 1) * 1.65f, 0, 0);
                    var s = shapes[i].GetComponentInChildren<CharacterStunVisual>(); var outline = s.transform.Find("Stun outline core").GetComponent<SpriteRenderer>();
                    Check(s != null && outline.enabled && outline.sprite == s.transform.Find("Body").GetComponent<SpriteRenderer>().sprite, "missing shape-matched outline on shape " + i);
                }
                Physics2D.SyncTransforms(); if (Array.IndexOf(Environment.GetCommandLineArgs(), "-stunCapture") >= 0) Capture();
                shapes[1].SetCharacterLayout(new CharacterShapeLayout { size = 1.3f, helmetSize = 1.4f, helmetOffset = new Vector2(.15f, .1f) }); VisualStep(shapes[1]);
                var middle = shapes[1].GetComponentInChildren<CharacterStunVisual>().transform; var hat = middle.Find("Helmet").GetComponent<SpriteRenderer>();
                float top = hat.transform.localPosition.y + hat.sprite.bounds.size.y * Mathf.Abs(hat.transform.localScale.y) * .5f;
                Check(Mathf.Abs(middle.Find("Electric impact").localPosition.y - top) < .01f && Mathf.Abs(middle.Find("Electric impact").localPosition.x - hat.transform.localPosition.x) < .01f, "impact did not follow custom helmet layout");
                typeof(PrototypePlayer).GetField("stunnedUntil", Private).SetValue(shapes[0], Time.time - .01f); typeof(PrototypePlayer).GetMethod("FixedUpdate", Private).Invoke(shapes[0], null); VisualStep(shapes[0]);
                Check(!shapes[0].stunned && !shapes[0].GetComponentInChildren<CharacterStunVisual>().transform.Find("Stun outline core").GetComponent<SpriteRenderer>().enabled, "effect did not clear when stun expired");
                Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_CHARACTER_STUN_VERIFICATION_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Capture()
        {
            var obj = new GameObject("stun preview camera"); var camera = obj.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 1.8f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f); camera.transform.position = new Vector3(0, .35f, -10);
            var target = new RenderTexture(1000, 500, 24); camera.targetTexture = target; camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(1000, 500, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1000, 500), 0, 0); texture.Apply(); File.WriteAllBytes("Library/CharacterStunPreview.png", texture.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(obj);
        }
    }
}
