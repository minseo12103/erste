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
    public static class CementArtworkVerification
    {
        const string Key = "ShapeCoop.CementArtworkVerification";
        const string Prefab = MapGimmickInstaller.Folder + "/무거운 시멘트 포대.prefab";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static bool ready;
        static CementArtworkVerification()
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
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/__CementVerification.prefab");
            var section = new GameObject("cement section"); var map = new GameObject("assembled map");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(prefab != null, "cement prefab missing");
                section.AddComponent<MapSection>(); var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab); placed.transform.SetParent(section.transform, false);
                PrefabUtility.SaveAsPrefabAsset(section, path, out bool saved); Check(saved, "could not export cement section");
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MapSection>();
                Check(MapSectionAssembler.Build(new[] { new MapSectionPlacement { prefab = asset, position = new Vector2(4, 2) } }, map.transform, null, out _, out _), "could not assemble cement section");
                var artwork = map.GetComponentInChildren<CementImpactVisual>(); Check(artwork != null && artwork.frames.Length == 8 && artwork.pile.sprite == artwork.frames[0], "assembled section lost cement artwork");
            }
            finally { Object.DestroyImmediate(section); Object.DestroyImmediate(map); AssetDatabase.DeleteAsset(path); }
        }
        static void AdvanceVisual(CementImpactVisual visual, float age)
        {
            typeof(CementImpactVisual).GetField("started", Private).SetValue(visual, Time.time - age);
            typeof(CementImpactVisual).GetMethod("Update", Private).Invoke(visual, null);
        }
        static void Tick()
        {
            if (!ready || !EditorApplication.isPlaying) return;
            ready = false; var mode = Physics2D.simulationMode;
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); var host = Object.Instantiate(prefab).GetComponent<MapGimmick>(); host.Initialize(true);
                var remote = Object.Instantiate(prefab, new Vector3(8, 0), Quaternion.identity).GetComponent<MapGimmick>(); remote.Initialize(false);
                var image = host.visual.GetComponent<CementImpactVisual>(); var other = remote.visual.GetComponent<CementImpactVisual>();
                Check(host.GetComponent<BoxCollider2D>().size == new Vector2(2, 1) && image != null && image.frames.Length == 8 && image.pile.sprite == image.frames[0] && image.dust.sprite == null, "cement geometry or initial sprite changed");
                for (int i = 0; i < 8; i++) Check(image.frames[i] != null && image.frames[i].name == "CementPile_" + i.ToString("00"), "wrong cement frame order");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-cementCapture") >= 0) Capture("idle");
                var ball = new GameObject("goal ball"); ball.transform.position = new Vector3(0, 1.5f, 0);
                var rb = ball.AddComponent<Rigidbody2D>(); rb.gravityScale = 0; rb.linearVelocity = Vector2.down * 8; rb.angularVelocity = 35;
                ball.AddComponent<CircleCollider2D>().radius = .5f; ball.AddComponent<MapGoalBall>(); Physics2D.SyncTransforms();
                int initialSequence = host.Capture(0).cementSequence;
                for (int i = 0; i < 10; i++) Physics2D.Simulate(.02f);
                var state = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                Check(rb.linearVelocity.magnitude < .01f && Mathf.Abs(rb.angularVelocity) < .01f, "cement did not stop goal ball");
                Check(state.cementSequence > initialSequence && state.cementAge >= 0 && Mathf.Abs(state.cementY - .5f) < .1f, "collision did not report impact position");
                remote.Apply(state, true); Check(other.dust.sprite == other.frames[2] && image.dust.sprite == image.frames[2], "impact was not synchronized to client");
                Check(Mathf.Abs(image.dust.transform.localPosition.y - (state.cementY + 59f / 80f * .615f)) < .01f, "dust did not start at contact");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-cementCapture") >= 0) Capture("impact");
                for (int i = 0; i < 6; i++)
                {
                    float age = (i + .2f) * image.frameSeconds; AdvanceVisual(image, age); AdvanceVisual(other, age);
                    Check(image.dust.sprite == image.frames[i + 2] && other.dust.sprite == other.frames[i + 2], "impact frame order incorrect");
                    if (i == 3 && Array.IndexOf(Environment.GetCommandLineArgs(), "-cementCapture") >= 0) Capture("dust");
                }
                AdvanceVisual(image, image.frameSeconds * 7); AdvanceVisual(other, other.frameSeconds * 7);
                Check(image.dust.sprite == null && image.pile.sprite == image.frames[0] && other.dust.sprite == null, "impact did not finish");
                Object.DestroyImmediate(ball); Physics2D.SyncTransforms(); ball = new GameObject("second ball"); ball.transform.position = new Vector3(-1.8f, 0, 0);
                rb = ball.AddComponent<Rigidbody2D>(); rb.gravityScale = 0; rb.linearVelocity = Vector2.right * 8; ball.AddComponent<CircleCollider2D>().radius = .5f; ball.AddComponent<MapGoalBall>(); Physics2D.SyncTransforms();
                for (int i = 0; i < 8; i++) Physics2D.Simulate(.02f);
                var second = host.Capture(0); Check(second.cementSequence > state.cementSequence && second.cementX < -.9f, "second or side impact not registered");
                remote.Apply(JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(second)), false); Check(other.dust.sprite == other.frames[2], "second impact did not replay on client");
                host.ResetGimmick(); remote.Apply(host.Capture(0), true); Check(image.dust.sprite == null && other.dust.sprite == null && image.pile.sprite == image.frames[0], "reset did not clear impact");
                Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.Log("SHAPE_CEMENT_ARTWORK_VERIFICATION_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = mode; SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Capture(string name)
        {
            var obj = new GameObject("cement preview camera"); var camera = obj.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 2.3f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f); camera.transform.position = new Vector3(0, .3f, -10);
            var target = new RenderTexture(720, 600, 24); camera.targetTexture = target; camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(720, 600, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 720, 600), 0, 0); texture.Apply(); File.WriteAllBytes("Library/CementPreview_" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(obj);
        }
    }
}
