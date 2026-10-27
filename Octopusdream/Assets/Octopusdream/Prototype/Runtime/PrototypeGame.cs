using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Octopusdream
{
    public sealed partial class PrototypeGame : MonoBehaviour
    {
        [Header("Prepared park map")]
        public Vector3 arenaCenter = new Vector3(-5f, 6f, 5f);
        public Vector3 arenaSize = new Vector3(38f, 16f, 38f);
        public Vector3 lobbyPosition = Vector3.zero;
        public Vector3[] spawnPoints = Array.Empty<Vector3>();
        [Header("Room / round settings")]
        public int maxPlayers = 20;
        public int hunterCount = 1;
        public float hideDuration = 30f, huntDuration = 180f;
        public float swimSpeed = 4f, harpoonRange = 25f, scanRange = 22f;
        public float scanCooldown = 20f, scanDuration = 3.5f, inkRadius = 5f, inkDuration = 5f;
        public float hiddenScorePerSecond = 14f;

        public RoundPhase Phase { get; private set; } = RoundPhase.Lobby;
        public IReadOnlyDictionary<ulong, PrototypePlayer> Players => players;
        public PrototypeTransport Transport => transport;
        public float Remaining => remaining;

        readonly Dictionary<ulong, PrototypePlayer> players = new Dictionary<ulong, PrototypePlayer>();
        readonly HashSet<ulong> handshaken = new HashSet<ulong>();
        PrototypeTransport transport;
        Camera viewCamera;
        Transform harpoonStand;
        float remaining, snapshotAt, inputAt, helloAt, yaw, pitch = 12f, brushRadius = .17f, lastPaintAt;
        int roundNumber, spectatorIndex;
        int snapshotSequence, lastWorldSequence = -1;
        int inputSequence;
        bool practice, editing, cursorFree = true, advanced, help = true;
        string result = "", notice = "", lobbyInput = "", lanAddress = "127.0.0.1", nickname = "", lastTransportStatus;
        Color paintColor = new Color(.25f, .8f, .75f, 1f);
        Vector2 lobbyScroll;
        Vector3 previousCameraTarget;
        Font uiFont;
        GUIStyle titleStyle, textStyle, smallStyle, buttonStyle, headingStyle;
        float guiScale = 1f;
        readonly Color panelColor = new Color(.025f, .075f, .095f, .94f);
        PrototypePlayer Local => transport != null && players.TryGetValue(transport.LocalId, out var p) ? p : null;
        bool CanPlay => transport != null && transport.Connected && Local != null;
        bool IsRound => Phase == RoundPhase.Hiding || Phase == RoundPhase.Hunting;

        void Awake()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 90;
            maxPlayers = Mathf.Clamp(maxPlayers, 2, 20);
            nickname = PlayerPrefs.GetString("Octopusdream.Nickname", "문어");
            foreach (Camera c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (AudioListener a in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) a.enabled = false;
            var cameraObject = new GameObject("Octopusdream Player Camera");
            cameraObject.tag = "MainCamera";
            viewCamera = cameraObject.AddComponent<Camera>();
            viewCamera.nearClipPlane = .05f;
            viewCamera.farClipPlane = 220f;
            viewCamera.fieldOfView = 72f;
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = new Color(.03f, .16f, .2f);
            cameraObject.AddComponent<AudioListener>();
            viewCamera.transform.position = lobbyPosition + new Vector3(0f, 7f, -12f);
            viewCamera.transform.LookAt(lobbyPosition + Vector3.up);
            RenderSettings.fog = true;
            RenderSettings.fogColor = viewCamera.backgroundColor;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .013f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.42f, .58f, .62f);
            transport = gameObject.AddComponent<PrototypeTransport>();
            transport.Received += Receive;
            transport.SessionReady += SessionReady;
            transport.SessionEnded += SessionEnded;
            transport.PeerJoined += PeerJoined;
            transport.PeerLeft += PeerLeft;
            BuildHarpoonStand();
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 16);
        }

        void Update()
        {
            if (CanPlay)
            {
                ReadControls();
                if (transport.IsHost) HostUpdate(Time.deltaTime);
                else
                {
                    foreach (var p in players.Values)
                    {
                        if (p == Local && !p.State.caught && (p.transform.position - p.TargetPosition).sqrMagnitude < 9f)
                            p.transform.position = Vector3.Lerp(p.transform.position, p.TargetPosition, Time.deltaTime * 2f);
                        else
                        {
                            Vector3 target = p.TargetPosition;
                            p.Teleport(Vector3.Lerp(p.transform.position, target, Time.deltaTime * 14f));
                            p.TargetPosition = target;
                        }
                        p.State.revealed = Mathf.Max(0f, p.State.revealed - Time.deltaTime);
                        p.State.blinded = Mathf.Max(0f, p.State.blinded - Time.deltaTime);
                    }
                    if (IsRound) remaining = Mathf.Max(0f, remaining - Time.deltaTime);
                }
                foreach (var p in players.Values)
                {
                    if (!transport.IsHost) p.transform.rotation = Quaternion.Euler(0f, p.State.yaw, 0f);
                    Vector3 visualVelocity = Vector3.ClampMagnitude((p.transform.position - p.LastVisualPosition) / Mathf.Max(.001f, Time.deltaTime), 20f);
                    p.LastVisualPosition = p.transform.position;
                    p.Appearance.Animate(visualVelocity);
                    bool ownFirstPerson = p == Local && p.State.diver && !p.State.caught;
                    p.Appearance.SetVisible(!p.State.caught && !ownFirstPerson);
                    p.Appearance.SetHighlighted(Local.State.diver && p.State.revealed > 0f && !p.State.diver && CanSee(Local, p, scanRange, 80f));
                }
            }
            else if (transport.Connected && !transport.IsHost && Time.unscaledTime > helloAt)
            {
                helloAt = Time.unscaledTime + 2f;
                SendToHost(new PrototypeMessage { kind = "hello", text = nickname });
            }
            bool shouldLock = CanPlay && !editing && !cursorFree && Phase != RoundPhase.Finished && !(Phase == RoundPhase.Hiding && Local.State.diver);
            Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !shouldLock;
        }

        void LateUpdate() { UpdateView(); }

        void BuildHarpoonStand()
        {
            var root = new GameObject("Lobby harpoon - E volunteer");
            harpoonStand = root.transform;
            harpoonStand.position = lobbyPosition;
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.transform.SetParent(root.transform, false);
            shaft.transform.localPosition = Vector3.up * 1.4f;
            shaft.transform.localScale = new Vector3(.08f, 1.3f, .08f);
            shaft.GetComponent<Renderer>().material = Lit(new Color(.8f, .65f, .3f));
            Destroy(shaft.GetComponent<Collider>());
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.transform.SetParent(root.transform, false);
            tip.transform.localPosition = Vector3.up * 2.7f;
            tip.transform.localScale = new Vector3(.27f, .5f, .1f);
            tip.GetComponent<Renderer>().material = Lit(new Color(.4f, 1f, .9f));
            Destroy(tip.GetComponent<Collider>());
            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.transform.SetParent(root.transform, false);
            pedestal.transform.localPosition = Vector3.up * .08f;
            pedestal.transform.localScale = new Vector3(1.5f, .08f, 1.5f);
            pedestal.GetComponent<Renderer>().material = Lit(new Color(.08f, .2f, .25f));
            Destroy(pedestal.GetComponent<Collider>());
        }

        static Material Lit(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .35f);
            return material;
        }

        Vector3 Spawn(int index)
        {
            if (spawnPoints != null && spawnPoints.Length > 0) return spawnPoints[index % spawnPoints.Length];
            return lobbyPosition + Quaternion.Euler(0f, index * 137.5f, 0f) * Vector3.forward * (3f + index * .2f);
        }

        PrototypePlayer AddPlayer(ulong id, string name, bool bot = false)
        {
            if (players.TryGetValue(id, out var existing)) return existing;
            var state = new PrototypePlayerState { id = id, name = CleanName(name), position = Spawn(players.Count), bot = bot };
            var p = new GameObject("Player").AddComponent<PrototypePlayer>();
            p.Initialize(state);
            players.Add(id, p);
            return p;
        }

        static string CleanName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "Diver";
            return new string(input.Where(c => !char.IsControl(c) && c != '<' && c != '>').Take(20).ToArray());
        }

        void RemovePlayer(ulong id)
        {
            if (!players.TryGetValue(id, out var p)) return;
            players.Remove(id);
            Destroy(p.gameObject);
            handshaken.Remove(id);
        }

        void OnDestroy()
        {
            ReleasePainterResources();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (transport != null)
            {
                transport.Received -= Receive;
                transport.SessionReady -= SessionReady;
                transport.SessionEnded -= SessionEnded;
                transport.PeerJoined -= PeerJoined;
                transport.PeerLeft -= PeerLeft;
            }
        }
    }
}
