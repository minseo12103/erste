using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    [InitializeOnLoad]
    public static class EmoteVerification
    {
        const string Key = "ShapeCoop.EmoteVerification";
        static PrototypePlayer host, client;
        static SpriteRenderer hand, remoteHand;
        static int phase;
        static double until;
        static EmoteVerification()
        {
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) { phase = 1; until = EditorApplication.timeSinceStartup + .2; } };
            EditorApplication.update += Tick;
        }
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        static void CapturePreview(string name)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-emoteCapture") < 0) return;
            var cameraObject = new GameObject("Emote preview camera"); var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1.65f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.18f, .2f, .24f);
            camera.transform.position = new Vector3(host.transform.position.x, host.transform.position.y + .2f, -10);
            client.gameObject.SetActive(false);
            var target = new RenderTexture(960, 640, 24); camera.targetTexture = target; camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes("Library/" + name, image.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null; client.gameObject.SetActive(true); Object.Destroy(image); Object.Destroy(target); Object.Destroy(cameraObject);
        }
        static void VerifyInput()
        {
            var background = InputSystem.settings.backgroundBehavior;
            var editorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var keyboard = InputSystem.AddDevice<Keyboard>(); var mouse = InputSystem.AddDevice<Mouse>();
            var cameraObject = new GameObject("Input verification camera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -10);
                var input = new EmoteInputReader();
                InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width * .85f, Screen.height * .5f) });
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Digit1)); InputSystem.Update();
                input.Read(keyboard, camera, Vector3.zero, true);
                Check(input.requested == 1 && input.sequence == 1 && input.held && input.direction.x > .9f, "digit 1 or mouse aiming input failed: requested=" + input.requested + " sequence=" + input.sequence + " held=" + input.held + " direction=" + input.direction + " screen=" + Screen.width + "x" + Screen.height);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); input.Read(keyboard, camera, Vector3.zero, true);
                Check(!input.held && input.sequence == 1, "releasing an emote key did not clear held state");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Digit2)); InputSystem.Update(); input.Read(keyboard, camera, Vector3.zero, true);
                Check(input.requested == 2 && input.sequence == 2 && input.held, "digit 2 input failed");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Digit3)); InputSystem.Update(); input.Read(keyboard, camera, Vector3.zero, true);
                Check(input.requested == 3 && input.sequence == 3 && input.held, "digit 3 input failed");
            }
            finally { InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse); InputSystem.settings.backgroundBehavior = background; InputSystem.settings.editorInputBehaviorInPlayMode = editorBehavior; Object.Destroy(cameraObject); }
        }
        public static void Run()
        {
            try { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || phase == 0 || EditorApplication.timeSinceStartup < until) return;
            try
            {
                if (phase == 1)
                {
                    VerifyInput();
                    var config = new GameObject("Settings").AddComponent<PrototypeSettings>(); config.gravityScale = 0;
                    host = new GameObject("Host").AddComponent<PrototypePlayer>(); host.Initialize(config, new PlayerData { id = "host", shape = 0 }, Vector2.zero, true);
                    client = new GameObject("Client").AddComponent<PrototypePlayer>(); client.Initialize(config, new PlayerData { id = "host", shape = 0 }, new Vector2(3, 0), false);
                    hand = host.transform.Find("Emote hand (visual only)").GetComponent<SpriteRenderer>(); remoteHand = client.transform.Find("Emote hand (visual only)").GetComponent<SpriteRenderer>();
                    Check(hand.GetComponent<Collider2D>() == null && !hand.enabled, "emote should start invisible and have no collider");
                    host.SetEmoteInput(1, 1, true, Vector2.right); phase = 2; until = EditorApplication.timeSinceStartup + .1; return;
                }
                if (phase == 2)
                {
                    Check(hand.enabled && hand.sprite.rect.x > 700 && hand.sprite.rect.y > 600, "pointing sprite missing");
                    Check(Vector2.Dot(hand.transform.up, Vector2.right) > .99f && !hand.flipX, "pointing hand did not aim right");
                    // Runtime-created sprites can be destroyed while static fields survive a play-mode transition.
                    Object.DestroyImmediate(hand.sprite);
                    typeof(CharacterEmote).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host.GetComponent<CharacterEmote>(), null);
                    Check(hand.enabled && hand.sprite != null, "active emote did not recover a destroyed cached sprite");
                    Object.DestroyImmediate(hand.sprite);
                    var probe = new GameObject("Sprite cache reinitialization probe").AddComponent<PrototypePlayer>();
                    probe.Initialize(Object.FindFirstObjectByType<PrototypeSettings>(), new PlayerData { id = "probe", shape = 1 }, Vector2.zero, true);
                    typeof(CharacterEmote).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host.GetComponent<CharacterEmote>(), null);
                    Check(hand.sprite != null, "new player did not rebuild a destroyed static sprite cache");
                    Object.Destroy(probe.gameObject);
                    CapturePreview("EmotePointPreview.png");
                    host.body.rotation = 145; host.SetEmoteInput(1, 1, true, Vector2.up);
                    client.Apply(JsonUtility.FromJson<BodyData>(JsonUtility.ToJson(host.Capture())), true);
                    phase = 3; until = EditorApplication.timeSinceStartup + .15; return;
                }
                if (phase == 3)
                {
                    Check(Vector2.Dot(hand.transform.up, Vector2.up) > .99f && remoteHand.enabled && Vector2.Dot(remoteHand.transform.up, Vector2.up) > .99f, "aim was not shared or depended on body rotation");
                    host.body.rotation = 0;
                    host.SetEmoteInput(2, 2, false, Vector2.left); client.Apply(JsonUtility.FromJson<BodyData>(JsonUtility.ToJson(host.Capture())), true);
                    phase = 4; until = EditorApplication.timeSinceStartup + .12; return;
                }
                if (phase == 4)
                {
                    Check(hand.enabled && hand.sprite.rect.x < 200 && hand.sprite.rect.y < 100 && remoteHand.enabled && remoteHand.sprite.rect.y < 100, "praise did not appear immediately");
                    Check(hand.flipX && remoteHand.flipX && hand.transform.up.y > .99f && remoteHand.transform.up.y > .99f, "praise should mirror on the left and stay perpendicular to aim");
                    host.SetEmoteInput(2, 2, false, new Vector2(-1, 1)); client.Apply(JsonUtility.FromJson<BodyData>(JsonUtility.ToJson(host.Capture())), true);
                    phase = 5; until = EditorApplication.timeSinceStartup + .12; return;
                }
                if (phase == 5)
                {
                    Check(hand.enabled && hand.sprite.rect.x < 200 && hand.sprite.rect.y < 100 && remoteHand.sprite.rect.y < 100, "praise gesture missing");
                    var diagonal = new Vector2(-1, 1).normalized;
                    Check(hand.flipX && remoteHand.flipX && Mathf.Abs(Vector2.Dot(hand.transform.up, diagonal)) < .01f && Mathf.Abs(Vector2.Dot(remoteHand.transform.up, diagonal)) < .01f, "praise should be perpendicular and mirrored on host and client");
                    CapturePreview("EmotePraisePreview.png");
                    host.SetEmoteInput(2, 2, false, Vector2.right); client.Apply(JsonUtility.FromJson<BodyData>(JsonUtility.ToJson(host.Capture())), true);
                    phase = 10; until = EditorApplication.timeSinceStartup + .12; return;
                }
                if (phase == 10)
                {
                    Check(hand.sprite.rect.x < 200 && !hand.flipX && !remoteHand.flipX && hand.transform.up.y > .99f, "praise did not unmirror while moving across the player");
                    host.SetEmoteInput(3, 3, true, Vector2.right); client.Apply(JsonUtility.FromJson<BodyData>(JsonUtility.ToJson(host.Capture())), true);
                    phase = 6; until = EditorApplication.timeSinceStartup + .12; return;
                }
                if (phase == 6)
                {
                    Check(hand.enabled && hand.sprite.rect.x > 700 && hand.sprite.rect.y < 100 && remoteHand.sprite.rect.x > 700 && remoteHand.sprite.rect.y < 100, "third gesture did not appear immediately");
                    Check(!hand.flipX && !remoteHand.flipX && hand.transform.up.y > .99f && remoteHand.transform.up.y > .99f, "third gesture should be perpendicular and unmirrored on the right");
                    host.SetEmoteInput(3, 3, true, Vector2.left); client.Apply(JsonUtility.FromJson<BodyData>(JsonUtility.ToJson(host.Capture())), true);
                    phase = 11; until = EditorApplication.timeSinceStartup + .12; return;
                }
                if (phase == 11)
                {
                    Check(hand.flipX && remoteHand.flipX && hand.transform.up.y > .99f, "third gesture did not mirror while moving across the player");
                    host.SetEmoteInput(3, 3, true, Vector2.down); client.Apply(JsonUtility.FromJson<BodyData>(JsonUtility.ToJson(host.Capture())), true);
                    phase = 7; until = EditorApplication.timeSinceStartup + 1.55; return;
                }
                if (phase == 7)
                {
                    Check(hand.enabled, "held emote did not remain visible");
                    Check(Mathf.Abs(Vector2.Dot(hand.transform.up, Vector2.down)) < .01f && hand.transform.position.y < host.transform.position.y, "downward aim should keep the hand perpendicular below the character");
                    host.SetEmoteInput(3, 3, false, Vector2.down); phase = 8; until = EditorApplication.timeSinceStartup + .1; return;
                }
                Check(!hand.enabled, "released emote did not end");
                host.SetEmoteInput(4, 1, false, Vector2.right); host.ResetBody(); client.Apply(host.Capture(), true);
                Check(host.Capture().emoteType == 0 && client.Capture().emoteType == 0, "restart did not clear emotes");
                Object.Destroy(host.gameObject); Object.Destroy(client.gameObject);
                Debug.Log("SHAPE_EMOTE_VERIFICATION_OK"); SessionState.SetBool(Key, false); phase = 0; EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); SessionState.SetBool(Key, false); phase = 0; EditorApplication.Exit(1); }
        }
    }

    [InitializeOnLoad]
    public static class EmoteReentryVerification
    {
        const string Key = "ShapeCoop.EmoteReentryVerification";
        static int phase;
        static double until;
        static SpriteRenderer hand;
        static EmoteReentryVerification()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                int run = SessionState.GetInt(Key, 0);
                if (run == 0) return;
                if (state == PlayModeStateChange.EnteredPlayMode) { phase = 1; until = EditorApplication.timeSinceStartup + .15; }
                if (state == PlayModeStateChange.EnteredEditMode && run == 2) EditorApplication.delayCall += EditorApplication.EnterPlaymode;
            };
            EditorApplication.update += Tick;
        }
        public static void Run()
        {
            try
            {
                if (!EditorSettings.enterPlayModeOptionsEnabled || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0) throw new Exception("Reentry test requires domain reload disabled");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionState.SetInt(Key, 1); EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { SessionState.SetInt(Key, 0); Debug.LogException(error); EditorApplication.Exit(1); }
        }
        static void Tick()
        {
            int run = SessionState.GetInt(Key, 0);
            if (run == 0 || !EditorApplication.isPlaying || phase == 0 || EditorApplication.timeSinceStartup < until) return;
            try
            {
                if (phase == 1)
                {
                    var config = new GameObject("Settings").AddComponent<PrototypeSettings>(); config.gravityScale = 0;
                    var player = new GameObject("Player").AddComponent<PrototypePlayer>();
                    player.Initialize(config, new PlayerData { id = "player", shape = 1 }, Vector2.zero, true);
                    hand = player.transform.Find("Emote hand (visual only)").GetComponent<SpriteRenderer>();
                    player.SetEmoteInput(1, 1, true, Vector2.right);
                    phase = 2; until = EditorApplication.timeSinceStartup + .25; return;
                }
                if (hand == null || !hand.enabled || hand.sprite == null) throw new Exception("Emote sprite missing on play session " + run);
                phase = 0;
                if (run == 1) { SessionState.SetInt(Key, 2); EditorApplication.ExitPlaymode(); return; }
                SessionState.SetInt(Key, 0); Debug.Log("SHAPE_EMOTE_REENTRY_OK"); EditorApplication.Exit(0);
            }
            catch (Exception error) { SessionState.SetInt(Key, 0); Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
