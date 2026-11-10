using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    [InitializeOnLoad]
    public static class CharacterAppearanceVerification
    {
        const string Key = "ShapeCoop.CharacterAppearanceVerification";
        static PrototypePlayer subject, remoteSubject;
        static PrototypeCharacterAppearance appearance;
        static Transform left, right;
        static PrototypeSettings settings;
        static Vector3 leftStart, rightStart;
        static int phase;
        static double until;
        static CharacterAppearanceVerification()
        {
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) { phase = 1; until = EditorApplication.timeSinceStartup + .2; } };
            EditorApplication.update += Tick;
        }
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        public static void Run()
        {
            try { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void RunComparison()
        {
            CharacterComparisonWindow window = null;
            string temporaryLayout = null;
            try
            {
                var active = SceneManager.GetActiveScene();
                window = ScriptableObject.CreateInstance<CharacterComparisonWindow>();
                var field = typeof(CharacterComparisonWindow).GetField("players", BindingFlags.NonPublic | BindingFlags.Instance);
                var players = (PrototypePlayer[])field.GetValue(window);
                Check(players.Length == 3 && SceneManager.GetActiveScene() == active, "comparison changed active scene");
                for (int i = 0; i < players.Length; i++)
                {
                    var player = players[i];
                    Check(player != null && player.shape == i && !player.body.simulated && player.gameObject.scene != active, "comparison did not isolate preview player " + i);
                    Check(player.GetComponentInChildren<PrototypeCharacterAppearance>() != null && player.GetComponentInChildren<PrototypeCharacterAppearance>().transform.Find("Helmet") != null, "comparison character missing artwork");
                    Check((i == 0 && player.shapeCollider is PolygonCollider2D) || (i == 1 && player.shapeCollider is BoxCollider2D) || (i == 2 && player.shapeCollider is CircleCollider2D), "comparison collider mismatch");
                }
                var reviewField = typeof(CharacterComparisonWindow).GetField("iceReviewSprite", BindingFlags.NonPublic | BindingFlags.Instance);
                var review = (Sprite)reviewField.GetValue(window);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-requireIceReview") >= 0) Check(review != null, "optional ice review image failed to load");
                var draw = typeof(CharacterComparisonWindow).GetMethod("ShouldDrawRenderer", BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < players.Length; i++)
                {
                    var root = players[i].GetComponentInChildren<PrototypeCharacterAppearance>().transform;
                    var ice = root.Find("Nitrogen ice (visual only)").GetComponent<SpriteRenderer>();
                    Check(!(bool)draw.Invoke(null, new object[] { ice, true, false }) && (bool)draw.Invoke(null, new object[] { ice, true, true }), "comparison ice toggle ignored");
                    var stun = root.Find("Stun outline core").GetComponent<SpriteRenderer>();
                    Check(!stun.enabled && !(bool)draw.Invoke(null, new object[] { stun, true, true }), "disabled status artwork shown in comparison");
                    Check(Mathf.Abs(ice.sprite.bounds.size.x * ice.transform.lossyScale.x - 1.62f) < .001f && Mathf.Abs(ice.sprite.bounds.size.y * ice.transform.lossyScale.y - 1.62f) < .001f, "comparison ice sizes differ");
                    if (review != null) Check(ice.sprite == review, "comparison did not show the review artwork");
                }
                var useReview = typeof(CharacterComparisonWindow).GetField("showIceReview", BindingFlags.NonPublic | BindingFlags.Instance);
                useReview.SetValue(window, false); typeof(CharacterComparisonWindow).GetMethod("UpdatePreview", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, null);
                foreach (var player in players)
                {
                    var ice = player.GetComponentInChildren<PrototypeCharacterAppearance>().transform.Find("Nitrogen ice (visual only)").GetComponent<SpriteRenderer>();
                    Check(ice.sprite != null && ice.sprite.name == "Nitrogen_Ice", "comparison could not restore current game artwork");
                }
                useReview.SetValue(window, true); typeof(CharacterComparisonWindow).GetMethod("UpdatePreview", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, null);
                if (review != null)
                    foreach (var player in players) Check(player.GetComponentInChildren<PrototypeCharacterAppearance>().transform.Find("Nitrogen ice (visual only)").GetComponent<SpriteRenderer>().sprite == review, "comparison did not switch back to the review artwork");
                var applied = AssetDatabase.LoadAssetAtPath<CharacterLayout>(CharacterComparisonWindow.LayoutPath);
                var before = applied == null ? CharacterShapeLayout.Default : applied.ForShape(0);
                var draftField = typeof(CharacterComparisonWindow).GetField("draft", BindingFlags.NonPublic | BindingFlags.Instance);
                var draft = (CharacterShapeLayout[])draftField.GetValue(window);
                Check(Mathf.Approximately(new CharacterShapeLayout { size = 1 }.Safe().helmetSize, 1), "legacy layout did not retain default helmet size");
                draft[0] = new CharacterShapeLayout { size = 1.45f, helmetOffset = new Vector2(.2f, -.1f), helmetSize = 1.4f };
                typeof(CharacterComparisonWindow).GetMethod("UpdatePreview", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, null);
                var preview = players[0];
                var helmet = preview.GetComponentInChildren<PrototypeCharacterAppearance>().transform.Find("Helmet");
                Check(Mathf.Approximately(preview.transform.localScale.x, 1.45f) && Mathf.Approximately(preview.shapeCollider.transform.lossyScale.x, 1.45f), "preview size did not scale the collider");
                Check(Vector2.Distance(helmet.localPosition, new Vector2(.2f, .635f)) < .001f && helmet.GetComponent<Collider2D>() == null, "preview helmet offset or collision incorrect");
                Check(Mathf.Abs(helmet.GetComponent<SpriteRenderer>().sprite.bounds.size.x * helmet.localScale.x - 1.12f * 1.4f) < .001f, "preview helmet size incorrect");
                preview.SetCharacterLayout(draft[0]);
                Check(Mathf.Abs(helmet.GetComponent<SpriteRenderer>().sprite.bounds.size.x * helmet.localScale.x - 1.12f * 1.4f) < .001f, "helmet size compounded on repeated updates");
                Check(applied == null ? AssetDatabase.LoadAssetAtPath<CharacterLayout>(CharacterComparisonWindow.LayoutPath) == null : Mathf.Approximately(applied.ForShape(0).size, before.size), "preview unexpectedly changed applied settings");
                temporaryLayout = "Assets/Resources/ShapeCoop/__CharacterLayoutVerification_" + Guid.NewGuid().ToString("N") + ".asset";
                Check(CharacterComparisonWindow.SaveLayout(temporaryLayout, draft), "could not save character settings");
                var saved = AssetDatabase.LoadAssetAtPath<CharacterLayout>(temporaryLayout).ForShape(0);
                Check(Mathf.Approximately(saved.size, 1.45f) && Mathf.Approximately(saved.helmetSize, 1.4f) && Vector2.Distance(saved.helmetOffset, draft[0].helmetOffset) < .001f, "saved layout differs from preview");
                var packet = JsonUtility.FromJson<PlayerData>(JsonUtility.ToJson(preview.CapturePlayerData()));
                var remoteObject = new GameObject("Remote layout check");
                SceneManager.MoveGameObjectToScene(remoteObject, preview.gameObject.scene);
                var remote = remoteObject.AddComponent<PrototypePlayer>();
                var settingsObject = (GameObject)typeof(CharacterComparisonWindow).GetField("settingsObject", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                remote.Initialize(settingsObject.GetComponent<PrototypeSettings>(), packet, Vector2.zero, false);
                Check(Mathf.Approximately(remote.CurrentCharacterLayout.size, 1.45f) && Mathf.Approximately(remote.CurrentCharacterLayout.helmetSize, 1.4f) && Vector2.Distance(remote.CurrentCharacterLayout.helmetOffset, draft[0].helmetOffset) < .001f, "host layout did not survive network serialization");
                var remoteHelmet = remote.GetComponentInChildren<PrototypeCharacterAppearance>().transform.Find("Helmet");
                Check(Mathf.Abs(remoteHelmet.GetComponent<SpriteRenderer>().sprite.bounds.size.x * remoteHelmet.localScale.x - 1.12f * 1.4f) < .001f, "remote helmet size incorrect");
                Object.DestroyImmediate(remote.gameObject);
                Check(AssetDatabase.DeleteAsset(temporaryLayout), "could not remove temporary verification asset"); temporaryLayout = null;
                Object.DestroyImmediate(window); window = null;
                Check(SceneManager.GetActiveScene() == active, "closing comparison changed active scene");
                Debug.Log("SHAPE_CHARACTER_COMPARISON_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { if (temporaryLayout != null) AssetDatabase.DeleteAsset(temporaryLayout); if (window != null) Object.DestroyImmediate(window); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Preview(string name = "CharacterAppearancePreview.png")
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-characterCapture") < 0) return;
            var camObject = new GameObject("Preview camera"); var cam = camObject.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 1.7f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f,.2f,.24f); cam.transform.position = new Vector3(0,.3f,-10);
            var target = new RenderTexture(1100, 500, 24); cam.targetTexture = target; cam.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0,0,target.width,target.height), 0, 0); texture.Apply();
            Directory.CreateDirectory("Library"); File.WriteAllBytes("Library/" + name, texture.EncodeToPNG());
            RenderTexture.active = previous; cam.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(camObject);
            Debug.Log("SHAPE_CHARACTER_PREVIEW_CAPTURED");
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || phase == 0 || EditorApplication.timeSinceStartup < until) return;
            try
            {
                if (phase == 1)
                {
                    var sheet = Resources.Load<Texture2D>("ShapeCoop/character"); Check(sheet != null && sheet.width == 1536 && sheet.height == 1024, "source artwork not imported at expected size");
                    settings = new GameObject("Settings").AddComponent<PrototypeSettings>(); settings.gravityScale = 0; settings.idleLookMinDelay = settings.idleLookMaxDelay = 10;
                    for (int shape = 0; shape < 3; shape++)
                    {
                        var player = new GameObject("Player " + shape).AddComponent<PrototypePlayer>();
                        var data = new PlayerData { id = "p" + shape, name = "P", shape = shape, color = shape };
                        if (shape == 0) { data.hasCharacterLayout = true; data.characterSize = 1.45f; data.helmetOffsetX = .2f; data.helmetOffsetY = -.1f; data.helmetSize = 1.4f; }
                        player.Initialize(settings, data, new Vector2((shape - 1) * 2.8f, 0), true);
                        if (shape == 0)
                        {
                            Physics2D.SyncTransforms();
                            Check(Mathf.Abs(player.shapeCollider.bounds.size.x - 1.45f) < .01f, "runtime collider did not use configured size");
                            Check(Vector2.Distance(player.GetComponentInChildren<PrototypeCharacterAppearance>().transform.Find("Helmet").localPosition, new Vector2(.2f, .635f)) < .001f, "runtime helmet offset incorrect");
                            var hat = player.GetComponentInChildren<PrototypeCharacterAppearance>().transform.Find("Helmet");
                            Check(Mathf.Abs(hat.GetComponent<SpriteRenderer>().sprite.bounds.size.x * hat.localScale.x - 1.12f * 1.4f) < .001f, "runtime helmet size incorrect");
                        }
                        player.body.constraints = RigidbodyConstraints2D.FreezeRotation;
                        var visual = player.GetComponentInChildren<PrototypeCharacterAppearance>();
                        Check(visual != null && visual.transform.Find("Body") != null && visual.transform.Find("Helmet") != null && visual.transform.Find("Pupil Left") != null && visual.transform.Find("Pupil Right") != null, "missing separated character layers for shape " + shape);
                        Check(visual.GetComponentsInChildren<Collider2D>(true).Length == 0, "character artwork must not add any collision");
                        Check(visual.transform.Find("Body").GetComponent<SpriteRenderer>().sprite.texture == sheet, "body does not use supplied image");
                        if (shape == 1) { subject = player; appearance = visual; left = visual.transform.Find("Pupil Left"); right = visual.transform.Find("Pupil Right"); leftStart = left.localPosition; rightStart = right.localPosition; }
                    }
                    phase = 5; until = EditorApplication.timeSinceStartup + .2; return;
                }
                if (phase == 5) { Preview(); subject.move = 1; subject.body.linearVelocity = Vector2.right * 3; phase = 2; until = EditorApplication.timeSinceStartup + .35; return; }
                if (phase == 2)
                {
                    Check(left.localPosition.x < leftStart.x - .025f && right.localPosition.x > rightStart.x + .025f && left.localPosition.y < leftStart.y - .01f && right.localPosition.y > rightStart.y + .01f, "pupils did not move in opposite directions when moving right");
                    subject.move = -1; subject.body.linearVelocity = Vector2.left * 3; phase = 6; until = EditorApplication.timeSinceStartup + .35; return;
                }
                if (phase == 6)
                {
                    Check(left.localPosition.x < leftStart.x - .025f && right.localPosition.x > rightStart.x + .025f && left.localPosition.y > leftStart.y + .01f && right.localPosition.y < rightStart.y - .01f, "pupils did not react in opposite directions to moving left");
                    subject.move = 0; subject.body.linearVelocity = Vector2.zero; phase = 3; until = EditorApplication.timeSinceStartup + .6; return;
                }
                if (phase == 3)
                {
                    Check(Vector3.Distance(left.localPosition, leftStart) < .01f && Vector3.Distance(right.localPosition, rightStart) < .01f, "pupils did not return to center");
                    settings.idleLookMinDelay = settings.idleLookMaxDelay = .12f; appearance.ResetEyes(); phase = 4; until = EditorApplication.timeSinceStartup + .4; return;
                }
                if (phase == 4)
                {
                    Check(appearance.IdleEyes && left.localPosition.x < leftStart.x - .02f && right.localPosition.x > rightStart.x + .02f, "idle expression did not look outward");
                    var body = subject.Capture(); Check(body.idleEyes, "idle expression missing from host state");
                    remoteSubject = new GameObject("Client").AddComponent<PrototypePlayer>(); remoteSubject.Initialize(settings, new PlayerData { id = "remote", name = "P", shape = 1 }, Vector2.zero, false); remoteSubject.Apply(body, true);
                    Check(remoteSubject.RemoteIdleEyes && remoteSubject.AppearanceVelocity == subject.AppearanceVelocity, "visual state not applied to client");
                    Preview("CharacterAppearanceIdlePreview.png"); phase = 8; until = EditorApplication.timeSinceStartup + 1.4; return;
                }
                if (phase == 8)
                {
                    var remoteAppearance = remoteSubject.GetComponentInChildren<PrototypeCharacterAppearance>();
                    Check(appearance.IdleEyes && remoteAppearance.IdleEyes && left.localPosition.x < leftStart.x - .02f && right.localPosition.x > rightStart.x + .02f && remoteAppearance.transform.Find("Pupil Left").localPosition.x < leftStart.x - .02f && remoteAppearance.transform.Find("Pupil Right").localPosition.x > rightStart.x + .02f, "idle expression was not held on host and client");
                    subject.body.rotation = 180; subject.move = 1; subject.body.linearVelocity = Vector2.right * 3; phase = 7; until = EditorApplication.timeSinceStartup + .35; return;
                }
                if (phase == 7)
                {
                    Check(!appearance.IdleEyes && left.localPosition.x < leftStart.x - .02f && right.localPosition.x > rightStart.x + .02f, "moving did not end idle expression or preserve mirrored motion when rotated");
                    remoteSubject.Apply(subject.Capture(), true);
                    subject.body.rotation = 0; subject.move = 0; subject.body.linearVelocity = Vector2.up * 3; phase = 9; until = EditorApplication.timeSinceStartup + .35; return;
                }
                Check(left.localPosition.y < leftStart.y - .02f && right.localPosition.y > rightStart.y + .02f, "pupils did not move oppositely during vertical movement");
                Check(!remoteSubject.GetComponentInChildren<PrototypeCharacterAppearance>().IdleEyes, "client did not end idle expression after host moved");
                Object.DestroyImmediate(remoteSubject.gameObject); remoteSubject = null;
                Debug.Log("SHAPE_CHARACTER_VERIFICATION_OK"); SessionState.SetBool(Key, false); phase = 0; EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); SessionState.SetBool(Key, false); phase = 0; EditorApplication.Exit(1); }
        }
    }
}
