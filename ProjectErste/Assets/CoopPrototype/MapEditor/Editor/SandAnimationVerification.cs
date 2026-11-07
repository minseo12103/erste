using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    public static class SandAnimationVerification
    {
        const string Prefab = MapGimmickInstaller.Folder + "/무너지는 모래 상자.prefab";
        static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Run()
        {
            GameObject hostObject = null, clientObject = null;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(prefab != null, "sand prefab missing");
                hostObject = Object.Instantiate(prefab); clientObject = Object.Instantiate(prefab); clientObject.transform.position = new Vector3(8, 0, 0);
                var host = hostObject.GetComponent<MapGimmick>(); var client = clientObject.GetComponent<MapGimmick>();
                host.Initialize(true); client.Initialize(false);
                var hostRenderer = host.visual.Find("Sand").GetComponent<SpriteRenderer>(); var clientRenderer = client.visual.Find("Sand").GetComponent<SpriteRenderer>();
                var hostCollider = host.GetComponent<BoxCollider2D>(); var clientCollider = client.GetComponent<BoxCollider2D>();
                Check(host.sandFrames.Length == 8 && hostRenderer.sprite == host.sandFrames[0] && hostRenderer.color == Color.white, "initial image or frames missing");
                Check(Mathf.Abs(hostCollider.size.y - .64f) < .001f && Mathf.Abs(hostCollider.offset.y + hostCollider.size.y * .5f - .5f) < .001f, "platform collision must match bag while preserving top edge");
                typeof(MapGimmick).GetField("triggered", Private).SetValue(host, true);
                var fixedUpdate = typeof(MapGimmick).GetMethod("FixedUpdate", Private);
                var seen = new bool[8]; bool vanished = false, brokeAtDelay = false;
                for (int step = 0; step < 130; step++)
                {
                    fixedUpdate.Invoke(host, null);
                    var sent = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                    client.Apply(sent, true);
                    Check(hostCollider.enabled == !sent.broken && clientCollider.enabled == !sent.broken, "collision disagrees with host state");
                    Check(hostRenderer.sprite == clientRenderer.sprite, "client animation frame differs from host snapshot");
                    if (!sent.broken) Check(sent.progress < host.activationDelay && hostRenderer.color.a > .99f, "platform disappeared before the collapse");
                    else if (!brokeAtDelay) { Check(sent.progress >= host.activationDelay && sent.progress < host.activationDelay + .04f, "collapse timing changed"); brokeAtDelay = true; }
                    if (hostRenderer.sprite != null)
                    {
                        int frame = Array.IndexOf(host.sandFrames, hostRenderer.sprite); Check(frame >= 0, "unexpected frame");
                        if (!seen[frame] && Array.IndexOf(Environment.GetCommandLineArgs(), "-sandCapture") >= 0) Capture(frame);
                        seen[frame] = true;
                    }
                    else if (sent.broken) vanished = true;
                }
                for (int i = 0; i < seen.Length; i++) Check(seen[i], "animation frame not shown: " + i);
                Check(brokeAtDelay && vanished && hostRenderer.sprite == null && clientRenderer.sprite == null, "burst did not finish on host and client");
                host.ResetGimmick(); client.Apply(JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0))), true);
                Check(hostCollider.enabled && clientCollider.enabled && hostRenderer.sprite == host.sandFrames[0] && clientRenderer.sprite == client.sandFrames[0], "restart did not restore platform");
                Debug.Log("SHAPE_SAND_ANIMATION_OK"); Object.DestroyImmediate(hostObject); Object.DestroyImmediate(clientObject); EditorApplication.Exit(0);
            }
            catch (Exception error) { if (hostObject != null) Object.DestroyImmediate(hostObject); if (clientObject != null) Object.DestroyImmediate(clientObject); Debug.LogException(error); EditorApplication.Exit(1); }
        }
        static void Capture(int frame)
        {
            var obj = new GameObject("Sand preview camera"); var camera = obj.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1.75f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f);
            camera.transform.position = new Vector3(0, .3f, -10);
            var target = new RenderTexture(480, 480, 24); camera.targetTexture = target; camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(480, 480, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 480, 480), 0, 0); texture.Apply();
            File.WriteAllBytes("Library/SandPreview" + frame + ".png", texture.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(obj);
        }
    }
}
