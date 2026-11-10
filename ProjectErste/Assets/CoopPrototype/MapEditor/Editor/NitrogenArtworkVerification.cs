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
    public static class NitrogenArtworkVerification
    {
        const string Key = "ShapeCoop.NitrogenArtworkVerification";
        const string Prefab = MapGimmickInstaller.Folder + "/액체 질소 가이저.prefab";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static bool ready;
        static NitrogenArtworkVerification()
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
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/__NitrogenVerification.prefab");
            var section = new GameObject("nitrogen section"); var map = new GameObject("assembled map");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(prefab != null, "nitrogen prefab missing");
                section.AddComponent<MapSection>(); var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab); placed.transform.SetParent(section.transform, false);
                PrefabUtility.SaveAsPrefabAsset(section, path, out bool saved); Check(saved, "could not export nitrogen section");
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MapSection>();
                Check(MapSectionAssembler.Build(new[] { new MapSectionPlacement { prefab = asset, position = new Vector2(4, 2) } }, map.transform, null, out _, out _), "could not assemble nitrogen section");
                var visual = map.GetComponentInChildren<NitrogenGeyserVisual>(); Check(visual != null && visual.frames.Length == 6 && visual.area != null, "assembled section lost nitrogen artwork");
            }
            finally { Object.DestroyImmediate(section); Object.DestroyImmediate(map); AssetDatabase.DeleteAsset(path); }
        }
        static void Step(MapGimmick g, int count)
        {
            var update = typeof(MapGimmick).GetMethod("FixedUpdate", Private); Physics2D.SyncTransforms();
            for (int i = 0; i < count; i++) { update.Invoke(g, null); Physics2D.Simulate(.02f); }
        }
        static PrototypePlayer Player(PrototypeSettings settings, int shape, Vector2 position, bool host)
        {
            var player = new GameObject("player").AddComponent<PrototypePlayer>();
            player.Initialize(settings, new PlayerData { shape = shape, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, position, host); player.body.gravityScale = 0; return player;
        }
        static SpriteRenderer Ice(PrototypePlayer player)
        {
            var visual = player.GetComponentInChildren<CharacterIceVisual>(); Check(visual != null, "player ice visual missing");
            typeof(CharacterIceVisual).GetMethod("LateUpdate", Private).Invoke(visual, null);
            var image = visual.transform.Find("Nitrogen ice (visual only)"); Check(image != null, "ice sprite resource missing"); return image.GetComponent<SpriteRenderer>();
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
                var image = host.visual.GetComponent<NitrogenGeyserVisual>(); var other = remote.visual.GetComponent<NitrogenGeyserVisual>(); var area = host.GetComponent<BoxCollider2D>();
                Check(area.isTrigger && area.size == new Vector2(3, 4) && area.offset == new Vector2(0, 2) && host.inactiveSeconds == 2.4f && host.activeSeconds == .6f && host.effectSeconds == 2 && host.GetComponentsInChildren<Collider2D>(true).Length == 1, "nitrogen physics changed");
                Check(image != null && image.frames.Length == 6 && image.gas.sprite == null && image.activeArea.sprite == null && host.transform.Find("Pipe").GetComponent<SpriteRenderer>().sprite.name == "Nitrogen_Pipe", "initial nitrogen artwork missing");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-nitrogenCapture") >= 0) Capture("off");
                host.inactiveSeconds = .1f; host.activeSeconds = .4f; host.effectSeconds = 1;
                var settings = new GameObject("settings").AddComponent<PrototypeSettings>(); settings.gravityScale = 0;
                var player = Player(settings, 1, new Vector2(-.35f, 1.7f), true); var client = Player(settings, 1, new Vector2(10, 2), false);
                var ballObject = new GameObject("goal ball"); var ball = ballObject.AddComponent<Rigidbody2D>(); ball.position = new Vector2(1.25f, 3); ball.gravityScale = 0; ballObject.AddComponent<CircleCollider2D>().radius = .3f; ballObject.AddComponent<MapGoalBall>();
                Step(host, 4); Check(!player.forcedFrozen && !Ice(player).enabled && !host.Capture(0).active, "inactive geyser froze player");
                Step(host, 8); var state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                Check(state.active && Mathf.Abs(state.nitrogenActiveSeconds - .4f) < .001f && player.forcedFrozen && player.frozen && player.body.bodyType == RigidbodyType2D.Kinematic && ball.linearVelocity.sqrMagnitude < .001f, "geyser force-freeze changed");
                var ice = Ice(player); Check(ice.enabled && ice.sprite.name == "Nitrogen_Ice" && player.GetComponentsInChildren<Collider2D>(true).Length == 1, "forced freeze did not show visual-only ice");
                remote.Apply(state, true); client.Apply(JsonUtility.FromJson<BodyData>(JsonUtility.ToJson(player.Capture())), true);
                Check(other.gas.sprite == image.gas.sprite && other.activeArea.sprite != null && Ice(client).enabled, "client freeze or gas visual disagrees with host");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-nitrogenCapture") >= 0) Capture("active");
                for (int i = 0; i < 6; i++) { image.Show(true, .4f * (i + .1f) / 6, .4f); Check(image.gas.sprite == image.frames[i] && image.activeArea.sprite != null, "wrong gas frame or active-area indicator"); }
                area.size = new Vector2(2, 3); area.offset = new Vector2(.5f, 1.5f); image.RefreshGeometry();
                Check(image.gas.transform.localPosition == new Vector3(.5f, 1.5f, 0) && Vector2.Distance(Vector2.Scale(image.gas.sprite.bounds.size, image.gas.transform.localScale), area.size) < .001f, "gas artwork does not follow trigger");
                area.size = new Vector2(3, 4); area.offset = new Vector2(0, 2); Step(host, 16);
                state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0))); Check(!state.active && image.gas.sprite == null && image.activeArea.sprite == null, "gas did not stop");
                remote.Apply(state, false); Check(other.gas.sprite == null && other.activeArea.sprite == null, "client gas did not stop");
                typeof(PrototypePlayer).GetField("forcedFreezeUntil", Private).SetValue(player, Time.time - 1); typeof(PrototypePlayer).GetMethod("FixedUpdate", Private).Invoke(player, null);
                Check(!player.forcedFrozen && !player.frozen && !Ice(player).enabled && player.body.bodyType == RigidbodyType2D.Dynamic, "auto thaw did not restore player and remove ice");
                player.freezePending = true; typeof(PrototypePlayer).GetMethod("FixedUpdate", Private).Invoke(player, null); Check(player.frozen && !Ice(player).enabled, "manual freeze incorrectly shows nitrogen ice");
                player.ForceFreeze(1); Check(Ice(player).enabled, "forced freeze over manual hold did not show ice");
                typeof(PrototypePlayer).GetField("forcedFreezeUntil", Private).SetValue(player, Time.time - 1); typeof(PrototypePlayer).GetMethod("FixedUpdate", Private).Invoke(player, null);
                Check(player.frozen && !player.forcedFrozen && !Ice(player).enabled, "thaw did not preserve manual hold");
                player.ForceFreeze(1); player.ResetBody(); client.Apply(player.Capture(), true); host.ResetGimmick(); remote.Apply(host.Capture(0), true);
                Check(!Ice(player).enabled && !Ice(client).enabled && image.gas.sprite == null && other.gas.sprite == null, "restart did not reset nitrogen or player ice");
                for (int shape = 0; shape < 3; shape++)
                {
                    var position = new Vector2((shape - 1) * 1.8f, 8); var p = Player(settings, shape, position, true);
                    p.SetCharacterLayout(new CharacterShapeLayout { size = shape == 0 ? 1.2f : 1f, helmetSize = 1 });
                    p.body.interpolation = RigidbodyInterpolation2D.None; p.transform.position = position; p.ForceFreeze(1);
                    var frozen = Ice(p); var spriteSize = frozen.sprite.bounds.size; var visualScale = frozen.transform.lossyScale;
                    Check(frozen.enabled && p.GetComponentsInChildren<Collider2D>(true).Length == 1, "ice missing or collidable on a character shape");
                    Check(Mathf.Abs(spriteSize.x * visualScale.x - 1.62f) < .001f && Mathf.Abs(spriteSize.y * visualScale.y - 1.62f) < .001f, "ice world size differs between character shapes");
                    p.SetCharacterLayout(new CharacterShapeLayout { size = .8f, helmetSize = 1 }); visualScale = frozen.transform.lossyScale;
                    Check(Mathf.Abs(spriteSize.x * visualScale.x - 1.62f) < .001f && Mathf.Abs(spriteSize.y * visualScale.y - 1.62f) < .001f, "ice size changed with character layout");
                }
                Physics2D.SyncTransforms();
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-nitrogenCapture") >= 0) Capture("characters", 0, 8, 2.4f);
                Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_NITROGEN_VERIFICATION_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Capture(string name, float x = -.5f, float y = 1.6f, float size = 3.7f)
        {
            var obj = new GameObject("nitrogen preview camera"); var camera = obj.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = size;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f); camera.transform.position = new Vector3(x, y, -10);
            var target = new RenderTexture(700, 800, 24); camera.targetTexture = target; camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(700, 800, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 700, 800), 0, 0); texture.Apply(); File.WriteAllBytes("Library/NitrogenPreview_" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; RenderTexture.active = old; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(obj);
        }
    }
}
