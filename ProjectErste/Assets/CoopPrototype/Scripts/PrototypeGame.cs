using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShapeCoop
{
    [RequireComponent(typeof(PrototypeSettings), typeof(SteamSession))]
    public sealed class PrototypeGame : MonoBehaviour
    {
        SteamSession session; PrototypeSettings settings;
        readonly Dictionary<string, PrototypePlayer> players = new Dictionary<string, PrototypePlayer>();
        GameObject world; Rigidbody2D ball; Vector2 ballTarget; float ballAngle;
        const float BallStartX = -3.8f, BallStartY = 1.1f;
        readonly PrototypeClock clock = new PrototypeClock();
        double elapsed, nextSnapshot; bool completed; int epoch, snapshotSequence, lastSnapshot;
        int jump, dash, freeze; float inputMove; Vector2 roomScroll;
        double pendingDashAt = -1;
        PhysicsMaterial2D ballMaterial;
        GUIStyle title, label;
        void Awake() { settings = GetComponent<PrototypeSettings>(); session = GetComponent<SteamSession>(); session.Received += Receive; }
        void Update()
        {
            if (!session.Playing) { if (world != null) ClearWorld(); return; }
            if (session.IsHost)
            {
                elapsed = clock.Elapsed(Time.realtimeSinceStartupAsDouble);
                RemoveDisconnected();
                if (Time.realtimeSinceStartupAsDouble >= nextSnapshot)
                {
                    nextSnapshot = Time.realtimeSinceStartupAsDouble + 1.0 / Mathf.Max(1, settings.snapshotsPerSecond);
                    session.Broadcast(Snapshot("state"), false);
                }
            }
            else if (ball != null)
            {
                float t = 1f - Mathf.Exp(-settings.interpolationSpeed * Time.deltaTime);
                ball.transform.position = Vector3.Lerp(ball.transform.position, ballTarget, t);
                ball.transform.rotation = Quaternion.Lerp(ball.transform.rotation, Quaternion.Euler(0,0,ballAngle), t);
            }
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            bool overlay = session.OverlayActive;
            inputMove = overlay ? 0 : (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
            if (!overlay && keyboard.spaceKey.wasPressedThisFrame) jump++;
            if (!overlay && (keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame)) pendingDashAt = Time.realtimeSinceStartupAsDouble + .15;
            if (overlay || keyboard.tabKey.isPressed) pendingDashAt = -1;
            if (pendingDashAt >= 0 && Time.realtimeSinceStartupAsDouble >= pendingDashAt) { dash++; pendingDashAt = -1; }
            if (!overlay && keyboard.fKey.wasPressedThisFrame) freeze++;
            var input = new Packet { kind = "input", epoch = epoch, move = inputMove, jump = jump, jumpHeld = !overlay && keyboard.spaceKey.isPressed, dash = dash, freeze = freeze };
            if (session.IsHost) ApplyInput(session.Self, input); else session.Send(session.Host, input);
        }
        void RemoveDisconnected()
        {
            var members = session.Members(); var remove = new List<string>();
            foreach (var pair in players)
            {
                if (!ulong.TryParse(pair.Key, out ulong value)) continue;
                var id = new CSteamID(value);
                if (id != session.Self && (!members.Contains(id) || !session.IsConnected(id))) remove.Add(pair.Key);
            }
            foreach (string id in remove) { Destroy(players[id].gameObject); players.Remove(id); }
        }
        void ApplyInput(CSteamID from, Packet p)
        {
            if (!session.IsHost || p.epoch != epoch || !players.TryGetValue(from.m_SteamID.ToString(), out var player)) return;
            player.move = Mathf.Clamp(p.move, -1, 1);
            player.jumpHeld = p.jumpHeld;
            if (p.jump > player.lastJump) { player.lastJump = p.jump; player.jumpPending = true; }
            if (p.dash > player.lastDash) { player.lastDash = p.dash; player.dashPending = true; }
            if (p.freeze > player.lastFreeze) { player.lastFreeze = p.freeze; player.freezePending = true; }
        }
        void Receive(CSteamID from, Packet p)
        {
            if (session.IsHost)
            {
                if (p.kind == "input" && session.Playing) ApplyInput(from, p);
                if (p.kind == "restart" && session.Playing && players.ContainsKey(from.m_SteamID.ToString())) Restart();
                return;
            }
            if (from != session.Host) return;
            if (p.kind == "start" && !session.Playing)
            {
                session.Playing = true; epoch = p.epoch; BuildWorld(p.players, false); ApplyState(p, true); return;
            }
            if (!session.Playing || (p.kind != "state" && p.kind != "reset" && p.kind != "complete")) return;
            if (p.epoch < epoch || (p.epoch == epoch && p.sequence <= lastSnapshot)) return;
            bool snap = p.epoch != epoch || p.kind == "reset";
            if (p.epoch != epoch) { epoch = p.epoch; jump = dash = freeze = 0; inputMove = 0; pendingDashAt = -1; }
            ApplyState(p, snap);
        }
        void StartGame()
        {
            if (!session.CanStart()) return;
            var members = session.Members().FindAll(session.IsConnected);
            if (members.Count == 0 || members.Count > 3) return;
            int[] shapes = { 0, 1, 2 };
            for (int i = shapes.Length - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (shapes[i], shapes[j]) = (shapes[j], shapes[i]); }
            var data = new PlayerData[members.Count];
            for (int i = 0; i < members.Count; i++) data[i] = new PlayerData { id = members[i].m_SteamID.ToString(), name = SteamFriends.GetFriendPersonaName(members[i]), shape = shapes[i], color = i };
            SteamMatchmaking.SetLobbyJoinable(session.Lobby, false);
            session.Playing = true; epoch++; snapshotSequence = lastSnapshot = 0; jump = dash = freeze = 0;
            BuildWorld(data, true); completed = false; elapsed = 0; clock.Begin(Time.realtimeSinceStartupAsDouble);
            session.Broadcast(Snapshot("start"));
        }
        void BuildWorld(PlayerData[] data, bool host)
        {
            ClearWorld(); world = new GameObject("Prototype World");
            PrototypeVisuals.Box(world.transform, "Left ground", new Vector2(-6, -.5f), new Vector2(12, 1), new Color(.32f,.42f,.46f));
            PrototypeVisuals.Box(world.transform, "Right ground", new Vector2(8, -.5f), new Vector2(10, 1), new Color(.32f,.42f,.46f));
            PrototypeVisuals.Box(world.transform, "Left boundary", new Vector2(-12.2f, 2), new Vector2(.4f, 5), new Color(.32f,.42f,.46f));
            PrototypeVisuals.Box(world.transform, "Right boundary", new Vector2(13.2f, 2), new Vector2(.4f, 5), new Color(.32f,.42f,.46f));
            var goal = new GameObject("Ball goal"); goal.transform.SetParent(world.transform); goal.transform.position = new Vector3(10,1,0);
            var trigger = goal.AddComponent<BoxCollider2D>(); trigger.isTrigger = true; trigger.size = new Vector2(1.6f,2f);
            PrototypeVisuals.Shape(goal.transform, 1, new Color(.25f,.9f,.38f,.3f), trigger.size, 0);
            var goalScript = goal.AddComponent<PrototypeGoal>(); goalScript.game = this;
            var ballGo = new GameObject("Goal ball"); ballGo.transform.SetParent(world.transform);
            ball = ballGo.AddComponent<Rigidbody2D>(); ball.mass = settings.ballMass; ball.gravityScale = settings.gravityScale; ball.interpolation = RigidbodyInterpolation2D.Interpolate; ball.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var ballCollider = ballGo.AddComponent<CircleCollider2D>(); ballCollider.radius = .6f;
            ballMaterial = CreateBallMaterial(settings);
            ballCollider.sharedMaterial = ballMaterial;
            PrototypeVisuals.Shape(ballGo.transform, 2, new Color(1f,.8f,.08f), new Vector2(1.2f,1.2f), 1);
            var center = PrototypeVisuals.Shape(ballGo.transform, 2, new Color(.35f,.22f,.05f), new Vector2(.25f,.25f), 2);
            if (!host) ball.simulated = false;
            for (int i = 0; i < data.Length; i++)
            {
                var obj = new GameObject("Player " + data[i].name); obj.transform.SetParent(world.transform);
                var player = obj.AddComponent<PrototypePlayer>(); player.Initialize(settings, data[i], new Vector2(-9.7f + i * 1.25f, .65f), host);
                players.Add(data[i].id, player);
            }
            ResetPositions();
        }
        void ClearWorld() { if (world != null) Destroy(world); if (ballMaterial != null) Destroy(ballMaterial); ballMaterial = null; world = null; players.Clear(); ball = null; pendingDashAt = -1; }
        public static PhysicsMaterial2D CreateBallMaterial(PrototypeSettings s) => new PhysicsMaterial2D("Goal ball bounce") { bounciness = Mathf.Clamp01(s.ballBounciness), friction = .2f };
        void ResetPositions()
        {
            foreach (var player in players.Values) player.ResetBody();
            if (session.IsHost) { ball.position = new Vector2(BallStartX, BallStartY); ball.rotation = 0; ball.linearVelocity = Vector2.zero; ball.angularVelocity = 0; }
            else { ball.transform.position = new Vector2(BallStartX, BallStartY); ball.transform.rotation = Quaternion.identity; }
            ballTarget = new Vector2(BallStartX, BallStartY); ballAngle = 0;
        }
        void Restart()
        {
            if (!session.IsHost || !session.Playing) return;
            epoch++; snapshotSequence = 0; jump = dash = freeze = 0; inputMove = 0; pendingDashAt = -1; ResetPositions();
            session.Broadcast(Snapshot("reset"));
        }
        public void BallReachedGoal(Collider2D other)
        {
            if (!session.IsHost || !session.Playing || completed || ball == null || other.gameObject != ball.gameObject) return;
            clock.Complete(Time.realtimeSinceStartupAsDouble); elapsed = clock.Elapsed(Time.realtimeSinceStartupAsDouble); completed = true;
            session.Broadcast(Snapshot("complete"));
        }
        Packet Snapshot(string kind)
        {
            var data = new PlayerData[players.Count]; int i = 0;
            foreach (var p in players.Values) data[i++] = new PlayerData { id = p.playerId, name = p.playerName, shape = p.shape, color = p.colorIndex, body = p.Capture() };
            return new Packet { kind = kind, epoch = epoch, sequence = ++snapshotSequence, elapsed = elapsed, completed = completed, players = data, ball = new BodyData { x = ball.position.x, y = ball.position.y, angle = ball.rotation } };
        }
        void ApplyState(Packet p, bool snap)
        {
            lastSnapshot = p.sequence; elapsed = p.elapsed; completed = p.completed;
            var present = new HashSet<string>();
            if (p.players != null) foreach (var item in p.players) { present.Add(item.id); if (players.TryGetValue(item.id, out var player)) player.Apply(item.body, snap); }
            var remove = new List<string>(); foreach (var id in players.Keys) if (!present.Contains(id)) remove.Add(id);
            foreach (var id in remove) { Destroy(players[id].gameObject); players.Remove(id); }
            if (p.ball != null) { ballTarget = new Vector2(p.ball.x, p.ball.y); ballAngle = p.ball.angle; if (snap) { ball.transform.position = ballTarget; ball.transform.rotation = Quaternion.Euler(0,0,ballAngle); } }
        }
        string Clock(double seconds) { var t = TimeSpan.FromSeconds(Math.Max(0, seconds)); return string.Format("{0:00}:{1:00}.{2:00}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds / 10); }
        void OnGUI()
        {
            if (title == null) { title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold }; label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true }; }
            GUI.Box(new Rect(10, 10, 420, session.Playing ? 165 : Mathf.Min(Screen.height - 20, 600)), "");
            GUILayout.BeginArea(new Rect(22, 18, 396, Screen.height - 36));
            if (session.Playing)
            {
                GUILayout.Label(completed ? "COMPLETE  " + Clock(elapsed) : "TIME  " + Clock(elapsed), title);
                GUILayout.Label("A / D move   SPACE jump   SHIFT dash   F freeze", label);
                if (GUILayout.Button("Restart from start", GUILayout.Height(32))) { if (session.IsHost) Restart(); else session.Send(session.Host, new Packet { kind = "restart" }); }
                if (GUILayout.Button("Leave room", GUILayout.Height(27))) session.Leave();
            }
            else
            {
                GUILayout.Label("SHAPE CO-OP", title); GUILayout.Label(session.Status, label);
                if (session.Available) GUILayout.Label(session.OverlayReady ? "Steam overlay: ready (Shift + Tab)" : "Steam overlay unavailable or loading. Enable it in Steam and launch the build through Steam.", label);
                if (!session.InLobby)
                {
                    GUI.enabled = session.Available;
                    if (GUILayout.Button("Create room", GUILayout.Height(34))) session.CreateRoom();
                    if (GUILayout.Button("Find rooms", GUILayout.Height(34))) session.FindRooms();
                    roomScroll = GUILayout.BeginScrollView(roomScroll, GUILayout.Height(200));
                    foreach (var room in session.Rooms)
                    {
                        string roomName = SteamMatchmaking.GetLobbyData(room, "name");
                        if (GUILayout.Button(roomName + "  " + SteamMatchmaking.GetNumLobbyMembers(room) + "/3", GUILayout.Height(30))) session.Join(room);
                    }
                    GUILayout.EndScrollView(); GUI.enabled = true;
                    if (GUILayout.Button("Quit", GUILayout.Height(30))) Application.Quit();
                }
                else
                {
                    var members = session.Members(); GUILayout.Label("Players " + members.Count + "/3", title);
                    foreach (var member in members)
                    {
                        string state = session.DisplayState(member);
                        GUILayout.Label(SteamFriends.GetFriendPersonaName(member) + (member == session.Host ? " [HOST]" : "") + " - " + state, label);
                    }
                    GUI.enabled = session.OverlayReady;
                    if (GUILayout.Button("Invite Steam friends", GUILayout.Height(30))) session.InviteFriends();
                    GUI.enabled = true;
                    if (session.IsHost) { GUI.enabled = session.CanStart(); if (GUILayout.Button("Start game", GUILayout.Height(36))) StartGame(); GUI.enabled = true; }
                    else GUILayout.Label("Waiting for host", label);
                    if (GUILayout.Button("Leave room", GUILayout.Height(30))) session.Leave();
                }
            }
            GUILayout.EndArea();
            if (session.Playing && Camera.main != null)
            {
                foreach (var p in players.Values)
                {
                    Vector3 screen = Camera.main.WorldToScreenPoint(p.transform.position + Vector3.up * .85f);
                    GUI.Label(new Rect(screen.x - 65, Screen.height - screen.y - 15, 150, 23), p.playerName + (p.frozen ? " [F]" : ""));
                }
            }
        }
    }
    public sealed class PrototypeGoal : MonoBehaviour
    {
        public PrototypeGame game;
        void OnTriggerEnter2D(Collider2D other) { game.BallReachedGoal(other); }
    }
}
