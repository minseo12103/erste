#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Octopusdream
{
    /// <summary>
    /// Explicit opt-in development player checks. Normal player launches do not create
    /// this component. LAN checks execute the real game command/snapshot handlers.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class PrototypeAutomation : MonoBehaviour
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Port = 27026;
        PrototypeGame game;
        PrototypeTransport transport;
        string mode, reportPath, capturePath, baselinePaint, clientPaintHash;
        double began, stageAt, nextInput;
        int stage, sequence;
        ulong remoteId;
        Vector3 initialPosition;
        bool paintSeen, softnessSeen, movementSeen, clientDone, clientFinal, hostAck, finishing;
        DateTime captureRequestedUtc;
        int captureWidth, captureHeight, captureDistinctColors;
        float captureNonblackFraction, captureLuminanceVariance;
        float visualSoftness = 7f;
        bool contactShowcase;
        GameObject contactFixture;
        readonly List<Material> contactMaterials = new List<Material>();
        readonly List<string> checks = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!args.Contains("--od-test-host") && !args.Contains("--od-test-client") && !args.Contains("--od-test-visual")) return;
            if (FindFirstObjectByType<PrototypeAutomation>() == null)
                new GameObject("Explicit development verification").AddComponent<PrototypeAutomation>();
        }

        void Start()
        {
            began = Time.realtimeSinceStartupAsDouble;
            string[] args = Environment.GetCommandLineArgs();
            mode = args.Contains("--od-test-host") ? "host" : args.Contains("--od-test-client") ? "client" : "visual";
            string outputRoot = Path.GetDirectoryName(Application.dataPath);
            reportPath = Argument(args, "--od-report") ?? Path.Combine(outputRoot, "Automation-" + mode + ".json");
            capturePath = Argument(args, "--od-capture") ?? Path.Combine(outputRoot, "Automation-visual.png");
            try
            {
                if (!Path.IsPathRooted(reportPath) || !Path.IsPathRooted(capturePath)) throw new ArgumentException("Automation output paths must be absolute.");
                game = FindFirstObjectByType<PrototypeGame>();
                if (game == null || game.Transport == null) throw new InvalidOperationException("Prepared park scene has no initialized PrototypeGame.");
                transport = game.Transport;
                transport.Received += OnReceived;
                transport.PeerLeft += OnPeerLeft;
                if (mode == "visual")
                {
                    Set("practice", true);
                    transport.StartOffline();
                    ConfigureVisual();
                }
                else
                {
                    // Its delegate-based network handlers stay active; only keyboard and
                    // automatic Update are disabled. The harness drives host simulation.
                    game.enabled = false;
                    Set("practice", false);
                    if (mode == "host") transport.HostLan(Port);
                    else transport.JoinLan("127.0.0.1", Port);
                }
                stageAt = Time.realtimeSinceStartupAsDouble;
            }
            catch (Exception ex) { Finish(false, ex.ToString()); }
        }

        static string Argument(string[] args, string flag)
        {
            int index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        void Update()
        {
            if (finishing || game == null) return;
            try
            {
                if (Time.realtimeSinceStartupAsDouble - began > 60) throw new TimeoutException("Automation timed out at stage " + stage + ": " + transport.Status);
                if (mode == "visual") TickVisual();
                else if (mode == "host") TickHost();
                else TickClient();
            }
            catch (Exception ex) { Finish(false, ex.ToString()); }
        }

        void TickHost()
        {
            if (transport.Connected) CallGame("HostUpdate", Time.unscaledDeltaTime);
            if (stage == 0 && game.Players.Count == 2)
            {
                checks.Add("Two actual player processes completed gameplay hello and roster handshake");
                remoteId = game.Players.Keys.First(id => id != transport.LocalId);
                game.Players[transport.LocalId].State.volunteer = true;
                game.Players[remoteId].State.volunteer = false;
                game.hideDuration = .5f;
                game.huntDuration = 45f;
                game.hunterCount = 1;
                game.spawnPoints = new[] { ClearPosition(0), ClearPosition(1) };
                CallGame("StartRound");
                Require(game.Players[transport.LocalId].State.diver && !game.Players[remoteId].State.diver, "Volunteer role assignment failed");
                baselinePaint = game.Players[remoteId].Appearance.ExportPaint();
                initialPosition = game.Players[remoteId].State.position;
                checks.Add("Host volunteer became diver and remote client became octopus");
                stage = 1;
            }
            if (stage >= 1 && game.Players.TryGetValue(remoteId, out var remote))
            {
                paintSeen |= remote.Appearance.ExportPaint() != baselinePaint && GreenPixel(remote.Appearance.PaintTexture);
                softnessSeen |= Mathf.Abs(remote.State.softness - 9f) < .001f;
                movementSeen |= Vector3.Distance(initialPosition, remote.State.position) > .3f;
            }
            if (stage == 1 && clientDone && paintSeen && softnessSeen && movementSeen)
            {
                var confirmedRemote = game.Players[remoteId];
                Require(PaintHash(confirmedRemote.Appearance.ExportPaint()) == clientPaintHash, "Host/client paint texture hashes differ");
                checks.Add("Client edit, seam-crossing green brush and SOFTNESS 9 reached authoritative host");
                checks.Add("Host and client exported identical paint textures");
                checks.Add("Sequenced remote movement advanced authoritative position");
                SendAutomation("automation-host-ack", remoteId);
                stage = 2;
                stageAt = Time.realtimeSinceStartupAsDouble;
            }
            if (stage == 2 && clientFinal)
            {
                checks.Add("Client acknowledged world/appearance verification");
                Finish(true, null);
            }
        }

        void TickClient()
        {
            if (stage == 4)
            {
                // The host may quit immediately after the final acknowledgement.
                if (Time.realtimeSinceStartupAsDouble - stageAt > .4) Finish(true, null);
                return;
            }
            if (!transport.Connected || !game.Players.TryGetValue(transport.LocalId, out var local)) return;
            if (stage == 0 && game.Phase == RoundPhase.Hunting)
            {
                Require(game.Players.Count == 2 && !local.State.diver, "Client roster or round role mismatch");
                checks.Add("Received authoritative roster, role and Hunting phase snapshot");
                baselinePaint = local.Appearance.ExportPaint();
                initialPosition = local.State.position;
                Command(new PrototypeMessage { kind = "edit", flag = true });
                Command(new PrototypeMessage { kind = "paint", vector = Vector3.right, color = Color.green, value = .3f });
                Command(new PrototypeMessage { kind = "soft", value = 9f });
                stage = 1;
                stageAt = Time.realtimeSinceStartupAsDouble;
            }
            if (stage == 1 && local.State.editing && Mathf.Abs(local.State.softness - 9f) < .001f &&
                local.Appearance.ExportPaint() != baselinePaint && GreenPixel(local.Appearance.PaintTexture))
            {
                checks.Add("Authoritative brush echo and softness snapshot applied to local appearance");
                Command(new PrototypeMessage { kind = "edit", flag = false });
                stage = 2;
                stageAt = Time.realtimeSinceStartupAsDouble;
            }
            if (stage == 2)
            {
                if (Time.realtimeSinceStartupAsDouble >= nextInput)
                {
                    nextInput = Time.realtimeSinceStartupAsDouble + .05;
                    Command(new PrototypeMessage { kind = "input", vector = Vector3.right, yaw = 90f, sequence = ++sequence });
                }
                if (Time.realtimeSinceStartupAsDouble - stageAt > 1.2 && Vector3.Distance(initialPosition, local.State.position) > .3f)
                {
                    Command(new PrototypeMessage { kind = "input", vector = Vector3.zero, yaw = 90f, sequence = ++sequence });
                    checks.Add("Received moved position from authoritative host after sequenced input");
                    clientPaintHash = PaintHash(local.Appearance.ExportPaint());
                    SendAutomation("automation-client-done", transport.HostId, clientPaintHash);
                    stage = 3;
                }
            }
            if (stage == 3 && hostAck)
            {
                checks.Add("Host confirmed matching texture hash, softness and movement");
                SendAutomation("automation-client-final", transport.HostId);
                stage = 4;
                stageAt = Time.realtimeSinceStartupAsDouble;
            }
        }

        Vector3 ClearPosition(int index)
        {
            Vector3 min = game.arenaCenter - game.arenaSize * .5f;
            Vector3 max = game.arenaCenter + game.arenaSize * .5f;
            int found = 0;
            for (int y = 0; y < 3; y++)
                for (int z = 1; z < 6; z++)
                    for (int x = 1; x < 6; x++)
                    {
                        Vector3 candidate = new Vector3(Mathf.Lerp(min.x, max.x, x / 7f), max.y - 3.5f - y * 2.5f, Mathf.Lerp(min.z, max.z, z / 7f));
                        if (Physics.CheckSphere(candidate + Vector3.up * .8f, 2.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                        if (found++ == index) return candidate;
                    }
            throw new InvalidOperationException("Could not find clear in-arena air space for deterministic movement check.");
        }

        static bool GreenPixel(Texture2D texture)
        {
            // +X lies at the UV seam: this also exercises wrap-aware paint replication.
            Color c = texture.GetPixelBilinear(0f, .5f);
            return c.g > .8f && c.r < .2f && c.b < .2f;
        }

        void ConfigureVisual()
        {
            var local = game.Players[transport.LocalId];
            string[] args = Environment.GetCommandLineArgs();
            string softnessArgument = Argument(args, "--od-softness");
            if (!string.IsNullOrEmpty(softnessArgument))
            {
                Require(float.TryParse(softnessArgument, NumberStyles.Float, CultureInfo.InvariantCulture, out visualSoftness) &&
                    !float.IsNaN(visualSoftness) && !float.IsInfinity(visualSoftness), "--od-softness must be a finite number.");
                visualSoftness = Mathf.Clamp(visualSoftness, 0f, 10f);
            }
            contactShowcase = args.Contains("--od-contact-showcase");
            if (contactShowcase) CreateContactShowcase(local);
            local.Appearance.SetBaseColor(new Color(.08f, .42f, .46f));
            local.Appearance.Paint(Vector3.back, new Color(1f, .58f, .18f), .44f);
            local.Appearance.Paint((Vector3.back + Vector3.left * .48f + Vector3.up * .45f).normalized, new Color(.9f, .24f, .45f), .26f);
            local.Appearance.Paint((Vector3.back + Vector3.right * .55f - Vector3.up * .2f).normalized, new Color(.3f, .82f, 1f), .3f);
            local.State.softness = visualSoftness;
            local.Appearance.SetSoftness(visualSoftness);
            CallGame("ToggleEdit");
            Set("editSoftness", visualSoftness);
            Set("lastSoftSent", visualSoftness);
            Set("yaw", contactShowcase ? -35f : 0f);
            Set("pitch", contactShowcase ? 24f : 8f);
            Set("paintColor", new Color(1f, .58f, .18f));
            checks.Add("Offline practice initialized with editable painted octopus and SOFTNESS " + visualSoftness.ToString("0.0", CultureInfo.InvariantCulture));
        }

        void CreateContactShowcase(PrototypePlayer local)
        {
            contactFixture = new GameObject("Temporary softness staircase showcase");
            CreateContactStep("Upper ledge", new Vector3(999f, 9.5f, 0f), new Vector3(2f, 1f, 3f), new Color(.63f, .54f, .43f));
            CreateContactStep("Middle step", new Vector3(1000.8f, 9f, 0f), new Vector3(1.6f, .9f, 3f), new Color(.72f, .65f, .55f));
            CreateContactStep("Lower step", new Vector3(1002.4f, 8.45f, 0f), new Vector3(1.6f, .9f, 3f), new Color(.61f, .54f, .46f));
            CreateContactStep("Showcase floor", new Vector3(1000f, 8.15f, 0f), new Vector3(12f, .4f, 10f), new Color(.25f, .35f, .39f));
            game.arenaCenter = new Vector3(1000f, 15f, 0f);
            game.arenaSize = new Vector3(20f, 20f, 20f);
            local.Teleport(new Vector3(999.7f, 10.05f, 0f));
            local.State.position = local.transform.position;
            local.LastVisualPosition = local.transform.position;
            Physics.SyncTransforms();
            checks.Add("Runtime-only staircase with 0.55m drop; octopus centered 0.30m behind its ledge");
        }

        void CreateContactStep(string title, Vector3 position, Vector3 size, Color color)
        {
            GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = title;
            step.transform.SetParent(contactFixture.transform, false);
            step.transform.position = position;
            step.transform.localScale = size;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .18f);
            step.GetComponent<Renderer>().sharedMaterial = material;
            contactMaterials.Add(material);
        }
        void TickVisual()
        {
            if (stage == 0 && Time.realtimeSinceStartupAsDouble - stageAt > (contactShowcase ? 3f : 1.5f))
            {
                stage = 1;
                stageAt = Time.realtimeSinceStartupAsDouble;
                Directory.CreateDirectory(Path.GetDirectoryName(capturePath));
                StartCoroutine(CaptureAfterFrame());
            }
            if (stage == 2 && ValidateRenderedCapture())
            {
                checks.Add("Rendered PNG is 1280x720 with " + captureDistinctColors + " quantized colors, " +
                    captureNonblackFraction.ToString("P1") + " nonblack pixels and luminance variance " + captureLuminanceVariance.ToString("F6"));
                checks.Add("Validated rendered frame captured to " + capturePath);
                Finish(true, null);
            }
        }

        bool ValidateRenderedCapture()
        {
            if (!File.Exists(capturePath)) return false;
            // Do not accidentally accept a previous run's file while CaptureScreenshot
            // is still writing the new frame asynchronously.
            if (File.GetLastWriteTimeUtc(capturePath) < captureRequestedUtc) return false;
            byte[] png;
            try { png = File.ReadAllBytes(capturePath); }
            catch (IOException) { return false; }
            if (png.Length < 24) return false;
            int end = png.Length;
            if (png[end - 8] != (byte)'I' || png[end - 7] != (byte)'E' ||
                png[end - 6] != (byte)'N' || png[end - 5] != (byte)'D') return false;

            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Require(ImageConversion.LoadImage(image, png, false), "Screenshot PNG could not be decoded.");
                captureWidth = image.width;
                captureHeight = image.height;
                Require(captureWidth == 1280 && captureHeight == 720,
                    "Screenshot must be 1280x720, got " + captureWidth + "x" + captureHeight + ". Screen is " + Screen.width + "x" + Screen.height + ".");
                Color32[] pixels = image.GetPixels32();
                var colors = new HashSet<int>();
                int samples = 0, nonblack = 0;
                double luminanceSum = 0, luminanceSquaredSum = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    Color32 color = pixels[i];
                    samples++;
                    if (color.a > 25 && (color.r > 12 || color.g > 12 || color.b > 12)) nonblack++;
                    colors.Add(((color.r >> 3) << 10) | ((color.g >> 3) << 5) | (color.b >> 3));
                    double luminance = (.2126 * color.r + .7152 * color.g + .0722 * color.b) / 255.0;
                    luminanceSum += luminance;
                    luminanceSquaredSum += luminance * luminance;
                }
                captureDistinctColors = colors.Count;
                captureNonblackFraction = samples == 0 ? 0f : (float)nonblack / samples;
                double mean = samples == 0 ? 0 : luminanceSum / samples;
                captureLuminanceVariance = samples == 0 ? 0f : (float)Math.Max(0, luminanceSquaredSum / samples - mean * mean);
                Require(captureDistinctColors > 32 && captureNonblackFraction > .1f && captureLuminanceVariance > .00005f,
                    "Screenshot is blank or insufficiently rendered: colors=" + captureDistinctColors +
                    ", nonblack=" + captureNonblackFraction.ToString("F4") + ", variance=" + captureLuminanceVariance.ToString("F7") +
                    ", graphics=" + SystemInfo.graphicsDeviceType + ".");
                return true;
            }
            finally { Destroy(image); }
        }

        IEnumerator CaptureAfterFrame()
        {
            yield return new WaitForEndOfFrame();
            captureRequestedUtc = DateTime.UtcNow;
            ScreenCapture.CaptureScreenshot(capturePath);
            yield return null;
            stage = 2;
        }

        void Command(PrototypeMessage message) { CallGame("SendToHost", message); }
        void SendAutomation(string kind, ulong target, string text = "")
        {
            var message = new PrototypeMessage { kind = kind, text = text };
            transport.Send(target, Encoding.UTF8.GetBytes(JsonUtility.ToJson(message)), true);
        }

        void OnReceived(ulong sender, byte[] data)
        {
            try
            {
                var message = JsonUtility.FromJson<PrototypeMessage>(Encoding.UTF8.GetString(data));
                if (mode == "host" && sender == remoteId)
                {
                    if (message.kind == "automation-client-done") { clientDone = true; clientPaintHash = message.text; }
                    if (message.kind == "automation-client-final") clientFinal = true;
                }
                if (mode == "client" && sender == transport.HostId && message.kind == "automation-host-ack") hostAck = true;
            }
            catch (ArgumentException) { }
        }

        void OnPeerLeft(ulong peer) { if (mode == "host" && stage == 2 && peer == remoteId) clientFinal = true; }

        void Set(string field, object value)
        {
            var info = typeof(PrototypeGame).GetField(field, Private);
            if (info == null) throw new MissingFieldException(typeof(PrototypeGame).Name, field);
            info.SetValue(game, value);
        }

        object CallGame(string method, params object[] arguments)
        {
            var info = typeof(PrototypeGame).GetMethod(method, Private);
            if (info == null) throw new MissingMethodException(typeof(PrototypeGame).Name, method);
            return info.Invoke(game, arguments);
        }

        static string PaintHash(string text)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        void Finish(bool passed, string error)
        {
            if (finishing) return;
            finishing = true;
            var report = new AutomationReport
            {
                mode = mode, passed = passed, error = error ?? "", checks = checks.ToArray(),
                seconds = (float)(Time.realtimeSinceStartupAsDouble - began),
                playerCount = game == null ? 0 : game.Players.Count,
                phase = game == null ? "uninitialized" : game.Phase.ToString(),
                paintHash = clientPaintHash ?? "", steamVerified = false,
                screenWidth = Screen.width, screenHeight = Screen.height,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
                captureWidth = captureWidth, captureHeight = captureHeight, captureDistinctColors = captureDistinctColors,
                captureNonblackFraction = captureNonblackFraction, captureLuminanceVariance = captureLuminanceVariance,
                softness = visualSoftness, staircaseShowcase = contactShowcase
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                File.WriteAllText(reportPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            }
            catch (Exception writeError) { Debug.LogError("Automation report write failed: " + writeError); passed = false; }
            Debug.Log("[Octopus Automation] " + (passed ? "PASS " : "FAIL ") + mode + " " + error);
            Application.Quit(passed ? 0 : 1);
        }

        void OnDestroy()
        {
            if (transport != null) { transport.Received -= OnReceived; transport.PeerLeft -= OnPeerLeft; }
            if (contactFixture != null) Destroy(contactFixture);
            foreach (Material material in contactMaterials) if (material != null) Destroy(material);
        }

        [Serializable]
        sealed class AutomationReport
        {
            public string mode, error, phase, paintHash, graphicsApi, graphicsDevice;
            public bool passed, steamVerified;
            public float seconds;
            public int playerCount;
            public int screenWidth, screenHeight, captureWidth, captureHeight, captureDistinctColors;
            public float captureNonblackFraction, captureLuminanceVariance;
            public float softness;
            public bool staircaseShowcase;
            public string[] checks;
        }
    }
}
#endif
