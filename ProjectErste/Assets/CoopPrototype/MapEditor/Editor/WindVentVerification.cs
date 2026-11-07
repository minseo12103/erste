using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    public static class WindVentVerification
    {
        const string Prefab = MapGimmickInstaller.Folder + "/공기 환풍구.prefab";
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Run()
        {
            GameObject hostObject = null, clientObject = null;
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); Check(asset != null, "vent prefab missing");
                hostObject = Object.Instantiate(asset); clientObject = Object.Instantiate(asset); clientObject.transform.position = Vector3.right * 10;
                var host = hostObject.GetComponent<MapGimmick>(); var client = clientObject.GetComponent<MapGimmick>();
                host.Initialize(true); client.Initialize(false);
                var trigger = host.GetComponent<BoxCollider2D>();
                Check(trigger != null && trigger.isTrigger && trigger.size == new Vector2(2, 5) && trigger.offset == new Vector2(0, 2.5f) && Mathf.Approximately(host.force, 32), "vent physics changed");
                var body = host.visual.Find("Vent").GetComponent<SpriteRenderer>();
                var air = host.visual.Find("Air").GetComponent<SpriteRenderer>(); var effect = air.GetComponent<WindVentEffect>();
                var remoteAir = client.visual.Find("Air").GetComponent<SpriteRenderer>(); var remoteEffect = remoteAir.GetComponent<WindVentEffect>();
                Check(body.sprite != null && body.sprite.name == "Vent_Floor" && body.color == Color.white, "vent artwork not assigned");
                Check(effect != null && remoteEffect != null && effect.frames.Length == 8 && air.sprite == effect.frames[0] && air.color == Color.white, "wind frames not assigned");
                Check(effect.GetComponent<Collider2D>() == null && Mathf.Abs(air.sprite.bounds.size.x * air.transform.localScale.x - trigger.size.x) < .01f && Mathf.Abs(air.sprite.bounds.size.y * air.transform.localScale.y - trigger.size.y) < .01f, "wind visual does not match trigger area or added collision");
                var advance = typeof(WindVentEffect).GetMethod("Advance", BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < 8; i++)
                {
                    Check(air.sprite == effect.frames[i] && remoteAir.sprite == remoteEffect.frames[i] && effect.frames[i].name == "Wind_" + i.ToString("00"), "incorrect wind animation frame " + i);
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-windCapture") >= 0) Capture(i);
                    advance.Invoke(effect, new object[] { 1f / effect.framesPerSecond }); advance.Invoke(remoteEffect, new object[] { 1f / remoteEffect.framesPerSecond });
                }
                Check(air.sprite == effect.frames[0] && remoteAir.sprite == remoteEffect.frames[0], "wind animation did not loop");
                advance.Invoke(effect, new object[] { .25f }); advance.Invoke(remoteEffect, new object[] { .25f });
                Check(air.sprite == effect.frames[2], "wind did not advance before restart");
                host.ResetGimmick(); client.Apply(JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0))), true);
                Check(air.sprite == effect.frames[0] && remoteAir.sprite == remoteEffect.frames[0], "restart did not reset wind on host and client");
                Debug.Log("SHAPE_WIND_VENT_OK"); Object.DestroyImmediate(hostObject); Object.DestroyImmediate(clientObject); EditorApplication.Exit(0);
            }
            catch (Exception error) { if (hostObject != null) Object.DestroyImmediate(hostObject); if (clientObject != null) Object.DestroyImmediate(clientObject); Debug.LogException(error); EditorApplication.Exit(1); }
        }
        static void Capture(int frame)
        {
            var obj = new GameObject("Wind preview camera"); var camera = obj.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 3.3f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .22f);
            camera.transform.position = new Vector3(0, 2.1f, -10);
            var target = new RenderTexture(480, 600, 24); camera.targetTexture = target; camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(480, 600, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 480, 600), 0, 0); texture.Apply();
            File.WriteAllBytes("Library/WindVentPreview" + frame + ".png", texture.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(obj);
        }
    }
}
