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
                client.activationDelay = .35f; client.sandRespawnSeconds = .75f;
                host.Initialize(true); client.Initialize(false);
                var hostRenderer = host.visual.Find("Sand").GetComponent<SpriteRenderer>(); var clientRenderer = client.visual.Find("Sand").GetComponent<SpriteRenderer>();
                var hostCollider = host.GetComponent<BoxCollider2D>(); var clientCollider = client.GetComponent<BoxCollider2D>();
                Check(host.sandFrames.Length == 8 && hostRenderer.sprite == host.sandFrames[0] && hostRenderer.color == Color.white, "initial image or frames missing");
                Check(Mathf.Approximately(host.sandRespawnSeconds, 5f), "existing sand prefab did not receive the default respawn delay");
                Check(Mathf.Abs(hostCollider.size.y - .64f) < .001f && Mathf.Abs(hostCollider.offset.y + hostCollider.size.y * .5f - .5f) < .001f, "platform collision must match bag while preserving top edge");
                typeof(MapGimmick).GetField("triggered", Private).SetValue(host, true);
                var fixedUpdate = typeof(MapGimmick).GetMethod("FixedUpdate", Private);
                var seen = new bool[8]; bool vanished = false, brokeAtDelay = false; int brokeStep = -1;
                for (int step = 0; step < 130; step++)
                {
                    fixedUpdate.Invoke(host, null);
                    var sent = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                    client.Apply(sent, true);
                    Check(hostCollider.enabled == !sent.broken && clientCollider.enabled == !sent.broken, "collision disagrees with host state");
                    Check(hostRenderer.sprite == clientRenderer.sprite, "client animation frame differs from host snapshot");
                    if (!sent.broken) Check(sent.progress < host.activationDelay && hostRenderer.color.a > .99f, "platform disappeared before the collapse");
                    else if (!brokeAtDelay) { Check(sent.progress >= host.activationDelay && sent.progress < host.activationDelay + .04f, "collapse timing changed"); brokeAtDelay = true; brokeStep = step; }
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
                bool respawned = false, testedPrediction = false; int expectedReverseFrame = 7, previousReverseFrame = -1;
                var reverseFrameSteps = new int[8];
                for (int step = 130; step < 130 + Mathf.CeilToInt(host.sandRespawnSeconds / Time.fixedDeltaTime) + 5; step++)
                {
                    fixedUpdate.Invoke(host, null);
                    var sent = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                    client.Apply(sent, true);
                    if (!sent.broken)
                    {
                        float elapsed = (step - brokeStep) * Time.fixedDeltaTime;
                        Check(Mathf.Abs(elapsed - host.sandRespawnSeconds) <= Time.fixedDeltaTime * 1.5f, "sand respawn timing changed: " + elapsed);
                        Check(sent.progress == 0 && hostCollider.enabled && clientCollider.enabled &&
                            hostRenderer.sprite == host.sandFrames[0] && clientRenderer.sprite == client.sandFrames[0],
                            "sand did not restore its appearance and collision on host and client");
                        respawned = true; break;
                    }
                    Check(!hostCollider.enabled && !clientCollider.enabled && hostRenderer.sprite == clientRenderer.sprite,
                        "sand reconstruction appearance or collision disagrees between peers");
                    if (hostRenderer.sprite != null)
                    {
                        int frame = Array.IndexOf(host.sandFrames, hostRenderer.sprite);
                        Check(frame >= 1 && frame <= 7, "unexpected sand reconstruction frame: " + frame);
                        reverseFrameSteps[frame]++;
                        if (frame != previousReverseFrame)
                        {
                            Check(frame == expectedReverseFrame, "sand reconstruction frame out of order: " + frame);
                            expectedReverseFrame--; previousReverseFrame = frame;
                        }
                        if (!testedPrediction)
                        {
                            typeof(MapGimmick).GetField("progressReceivedAt", Private).SetValue(client, Time.time - 2f);
                            typeof(MapGimmick).GetMethod("Update", Private).Invoke(client, null);
                            Check(clientRenderer.sprite == client.sandFrames[1] && !clientCollider.enabled,
                                "client reconstructed a solid platform before host confirmation");
                            client.Apply(sent, true); testedPrediction = true;
                        }
                    }
                    else Check(previousReverseFrame == -1, "sand disappeared during reconstruction");
                }
                Check(respawned && testedPrediction && expectedReverseFrame == 0, "sand did not play all reverse frames before respawning");
                var reverseDurations = new[] { 0f, host.activationDelay * .3f / 1.95f, host.activationDelay * .35f / 1.95f,
                    host.activationDelay * .4f / 1.95f, .08f, .10f, .16f, .24f };
                for (int frame = 1; frame < reverseDurations.Length; frame++)
                    Check(Mathf.Abs(reverseFrameSteps[frame] * Time.fixedDeltaTime - reverseDurations[frame]) <= Time.fixedDeltaTime * 1.5f,
                        "sand reverse frame duration changed: " + frame);

                host.sandRespawnSeconds = 1.2f;
                typeof(MapGimmick).GetField("triggered", Private).SetValue(host, true);
                bool brokeAgain = false, respawnedAgain = false; int secondBreak = -1;
                for (int step = 0; step < 200; step++)
                {
                    fixedUpdate.Invoke(host, null);
                    var sent = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                    client.Apply(sent, true);
                    Check(hostCollider.enabled == clientCollider.enabled && hostRenderer.sprite == clientRenderer.sprite,
                        "custom sand timing did not synchronize between peers");
                    if (sent.broken && !brokeAgain) { brokeAgain = true; secondBreak = step; }
                    if (!sent.broken && brokeAgain)
                    {
                        Check(Mathf.Abs((step - secondBreak) * Time.fixedDeltaTime - 1.2f) <= Time.fixedDeltaTime * 1.5f,
                            "custom respawn delay was not applied");
                        Check(hostCollider.enabled && clientCollider.enabled && hostRenderer.sprite == host.sandFrames[0] &&
                            clientRenderer.sprite == client.sandFrames[0], "second respawn was not synchronized");
                        respawnedAgain = true; break;
                    }
                }
                Check(brokeAgain && respawnedAgain, "sand could not collapse and respawn twice");
                host.sandRespawnSeconds = .58f;
                typeof(MapGimmick).GetField("triggered", Private).SetValue(host, true);
                bool shortBreak = false, shortRespawn = false; int expectedShortFrame = 7, previousShortFrame = -1;
                for (int step = 0; step < 200; step++)
                {
                    fixedUpdate.Invoke(host, null);
                    var sent = JsonUtility.FromJson<GimmickData>(JsonUtility.ToJson(host.Capture(0)));
                    client.Apply(sent, true);
                    Check(hostCollider.enabled == clientCollider.enabled && hostRenderer.sprite == clientRenderer.sprite,
                        "short sand timing did not synchronize between peers");
                    if (sent.broken)
                    {
                        shortBreak = true;
                        if (sent.progress - host.activationDelay >= .29f)
                        {
                            int frame = Array.IndexOf(host.sandFrames, hostRenderer.sprite);
                            if (frame != previousShortFrame)
                            {
                                Check(frame == expectedShortFrame, "short reconstruction frame out of order: " + frame);
                                expectedShortFrame--; previousShortFrame = frame;
                            }
                            Check(!hostCollider.enabled && !clientCollider.enabled, "short reconstruction became solid too soon");
                        }
                    }
                    else if (shortBreak) { shortRespawn = true; break; }
                }
                Check(shortRespawn && expectedShortFrame == 0, "minimum respawn delay skipped reverse frames: respawn=" + shortRespawn + " next=" + expectedShortFrame + " last=" + previousShortFrame + " progress=" + host.Capture(0).progress + " delay=" + host.activationDelay + " wait=" + host.sandRespawnSeconds + " dt=" + Time.fixedDeltaTime);
                host.sandRespawnSeconds = 3.25f;
                GimmickSettingsGUI.CopySettings(host);
                Check(GimmickSettingsGUI.PasteSettings(client) && Mathf.Approximately(client.sandRespawnSeconds, 3.25f),
                    "sand respawn delay was not copied with gimmick settings");
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
