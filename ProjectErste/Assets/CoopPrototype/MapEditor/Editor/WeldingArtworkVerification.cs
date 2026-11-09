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
    public static class WeldingArtworkVerification
    {
        const string Key = "ShapeCoop.WeldingArtworkVerification";
        const string Prefab = MapGimmickInstaller.Folder + "/용접 스파크 구역.prefab";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static bool ready;
        static WeldingArtworkVerification()
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
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/__WeldingVerification.prefab");
            var section = new GameObject("welding section"); var map = new GameObject("assembled map");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(prefab != null, "welding prefab missing");
                section.AddComponent<MapSection>(); var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab); placed.transform.SetParent(section.transform, false);
                PrefabUtility.SaveAsPrefabAsset(section, path, out bool saved); Check(saved, "could not export welding section");
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MapSection>();
                Check(MapSectionAssembler.Build(new[] { new MapSectionPlacement { prefab = asset, position = new Vector2(4, 2) } }, map.transform, null, out _, out _), "could not assemble welding section");
                var art = map.GetComponentInChildren<WeldingSparkVisual>(); Check(art != null && art.arcFrames.Length == 6 && art.impactFrames.Length == 4 && art.deviceOn != null, "assembled section lost welding artwork");
            }
            finally { Object.DestroyImmediate(section); Object.DestroyImmediate(map); AssetDatabase.DeleteAsset(path); }
        }
        static void Step(MapGimmick g, int count)
        {
            var update = typeof(MapGimmick).GetMethod("FixedUpdate", Private); Physics2D.SyncTransforms();
            for (int i = 0; i < count; i++) { update.Invoke(g, null); Physics2D.Simulate(.02f); }
        }
        static PrototypePlayer Player(PrototypeSettings settings, Vector2 position)
        {
            var p = new GameObject("shield test player").AddComponent<PrototypePlayer>();
            p.Initialize(settings, new PlayerData { shape = 1, hasCharacterLayout = true, characterSize = 1, helmetSize = 1 }, position, true); p.body.gravityScale = 0; return p;
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
                var art = host.visual.GetComponent<WeldingSparkVisual>(); var other = remote.visual.GetComponent<WeldingSparkVisual>();
                Check(art != null && art.device.sprite == art.deviceOff && !host.beamVisual.gameObject.activeSelf && art.impact.sprite == null, "welding should start off");
                Check(art.arcFrames.Length == 6 && art.impactFrames.Length == 4 && host.beamRange == 5 && host.effectSeconds == 1.5f, "artwork or gameplay defaults changed");
                for (int i = 0; i < 6; i++) Check(art.arcFrames[i].name == "Welding_Arc_" + i.ToString("00"), "arc frame order incorrect");
                for (int i = 0; i < 4; i++) Check(art.impactFrames[i].name == "Welding_Impact_" + i.ToString("00"), "impact frame order incorrect");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-weldingCapture") >= 0) Capture("off", 0);
                var settings = new GameObject("settings").AddComponent<PrototypeSettings>(); settings.gravityScale = 0;
                var shield = Player(settings, new Vector2(0, -2)); var behind = Player(settings, new Vector2(0, -4));
                host.inactiveSeconds = .02f; host.activeSeconds = 10; Step(host, 4);
                var state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                Check(state.active && state.sparkHit && state.beamLength > 1.3f && state.beamLength < 1.7f && shield.stunned && !behind.stunned, "first player did not block spark and receive stun");
                Check(host.beamVisual.gameObject.activeSelf && art.device.sprite == art.deviceOn && art.arc.sprite != null && art.impact.sprite != null && Mathf.Abs(art.impact.transform.localPosition.y + state.beamLength) < .01f, "active artwork does not reach first contact");
                Check(Mathf.Abs(art.arc.sprite.bounds.size.y * art.arc.transform.localScale.y * host.beamVisual.localScale.y - state.beamLength) < .02f, "visual beam length disagrees with raycast");
                remote.Apply(state, true); Check(other.device.sprite == other.deviceOn && other.arc.sprite == art.arc.sprite && other.impact.sprite == art.impact.sprite && Mathf.Abs(other.impact.transform.localPosition.y + state.beamLength) < .01f, "client state or phase did not match host");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-weldingCapture") >= 0) Capture("contact", -1);
                Object.DestroyImmediate(shield.gameObject); Object.DestroyImmediate(behind.gameObject); Physics2D.SyncTransforms(); Step(host, 1);
                state = host.Capture(0); Check(!state.sparkHit && Mathf.Abs(state.beamLength - host.beamRange) < .01f && art.impact.sprite == null, "impact sparks should disappear when nothing is hit");
                remote.Apply(state, false); Check(other.impact.sprite == null && Mathf.Abs(remote.beamVisual.localScale.y - host.beamRange) < .01f, "client did not update unblocked beam");
                host.transform.rotation = Quaternion.Euler(0, 0, 30); host.transform.localScale = new Vector3(1.3f, .8f, 1);
                var ground = new GameObject("scaled placement test ground"); ground.transform.position = new Vector3(0, -3, 0); ground.AddComponent<BoxCollider2D>().size = new Vector2(1, .5f);
                Step(host, 1); state = host.Capture(0);
                Vector3 expectedEnd = host.transform.position + Vector3.down * state.beamLength;
                Check(state.sparkHit && (art.impact.transform.position - expectedEnd).sqrMagnitude < .0001f && (host.beamVisual.TransformPoint(Vector3.up * .5f) - host.transform.position).sqrMagnitude < .0001f && (host.beamVisual.TransformPoint(Vector3.down * .5f) - expectedEnd).sqrMagnitude < .0001f, "scaled or rotated welding artwork did not follow the world-space raycast");
                Object.DestroyImmediate(ground); host.transform.rotation = Quaternion.identity; host.transform.localScale = Vector3.one;
                for (int i = 0; i < 12; i++)
                {
                    art.Show(true, true, new Vector3(0, -3, 0), (i + .1f) / art.framesPerSecond);
                    Check(art.arc.sprite == art.arcFrames[i % 6] && art.impact.sprite == art.impactFrames[i % 4], "welding frames did not loop");
                }
                host.ResetGimmick(); remote.Apply(host.Capture(0), true);
                Check(art.device.sprite == art.deviceOff && art.arc.sprite == null && art.impact.sprite == null && other.device.sprite == other.deviceOff && other.impact.sprite == null, "restart did not restore off state");
                Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_WELDING_ARTWORK_VERIFICATION_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Capture(string name, float centerY)
        {
            var obj = new GameObject("welding preview camera"); var camera = obj.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 2.8f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f); camera.transform.position = new Vector3(0, centerY, -10);
            var target = new RenderTexture(720, 850, 24); camera.targetTexture = target; camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(720, 850, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 720, 850), 0, 0); texture.Apply(); File.WriteAllBytes("Library/WeldingPreview_" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(obj);
        }
    }
}
