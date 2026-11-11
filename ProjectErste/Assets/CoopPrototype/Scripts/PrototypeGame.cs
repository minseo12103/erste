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
        [Tooltip("Optional authored sections, placed in order. Leave empty to use the original test map.")]
        public MapSectionPlacement[] mapSections;
        [Tooltip("Follow the goal ball while playing authored sections.")]
        public bool followBallInAuthoredMaps = true;
        SteamSession session; PrototypeSettings settings;
        readonly Dictionary<string, PrototypePlayer> players = new Dictionary<string, PrototypePlayer>();
        GameObject world; Rigidbody2D ball; Vector2 ballTarget; float ballAngle;
        MapGimmick[] gimmicks = Array.Empty<MapGimmick>();
        MapDynamicBodies mapBodies;
        const float BallStartX = -3.8f, BallStartY = 1.1f;
        Vector2 ballSpawn;
        bool authoredMap;
        Vector3 initialCameraPosition;
        readonly PrototypeClock clock = new PrototypeClock();
        double elapsed, nextSnapshot; bool completed; int epoch, snapshotSequence, lastSnapshot;
        int jump, dash, freeze; float inputMove;
        readonly GameFrontEnd frontEnd = new GameFrontEnd();
        readonly EmoteInputReader emoteInput = new EmoteInputReader();
        double pendingDashAt = -1;
        PhysicsMaterial2D ballMaterial;
        GUIStyle title, label;
        void Awake() { settings = GetComponent<PrototypeSettings>(); session = GetComponent<SteamSession>(); session.Received += Receive; if (Camera.main != null) initialCameraPosition = Camera.main.transform.position; }
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
                    BroadcastMapBodies(false);
                }
            }
            else if (ball != null)
            {
                float t = 1f - Mathf.Exp(-settings.interpolationSpeed * Time.deltaTime);
                ball.transform.position = Vector3.Lerp(ball.transform.position, ballTarget, t);
                ball.transform.rotation = Quaternion.Lerp(ball.transform.rotation, Quaternion.Euler(0,0,ballAngle), t);
                mapBodies?.Interpolate(t);
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
            if (players.TryGetValue(session.Self.m_SteamID.ToString(), out var localPlayer)) emoteInput.Read(keyboard, Camera.main, localPlayer.transform.position, !overlay);
            var input = new Packet { kind = "input", epoch = epoch, move = inputMove, jump = jump, jumpHeld = !overlay && keyboard.spaceKey.isPressed, dash = dash, freeze = freeze, emoteSequence = emoteInput.sequence, emoteRequested = emoteInput.requested, emoteHeld = emoteInput.held, emoteX = emoteInput.direction.x, emoteY = emoteInput.direction.y };
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
        void LateUpdate()
        {
            if (!session.Playing || !authoredMap || !followBallInAuthoredMaps || ball == null || Camera.main == null) return;
            var p = ball.transform.position; Camera.main.transform.position = new Vector3(p.x, p.y + 1f, initialCameraPosition.z);
        }
        void ApplyInput(CSteamID from, Packet p)
        {
            if (!session.IsHost || p.epoch != epoch || !players.TryGetValue(from.m_SteamID.ToString(), out var player)) return;
            player.move = Mathf.Clamp(p.move, -1, 1);
            player.jumpHeld = p.jumpHeld;
            player.SetEmoteInput(p.emoteSequence, p.emoteRequested, p.emoteHeld, new Vector2(p.emoteX, p.emoteY));
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
            if (p.kind == "map_bodies")
            {
                if (session.Playing && p.epoch == epoch) mapBodies?.Apply(p.mapBodyOffset, p.mapBodies, p.sequence, false);
                return;
            }
            if (p.kind == "start" && !session.Playing)
            {
                session.Playing = true; epoch = p.epoch; emoteInput.Reset(); BuildWorld(p.players, false); ApplyState(p, true); return;
            }
            if (!session.Playing || (p.kind != "state" && p.kind != "reset" && p.kind != "complete")) return;
            if (p.epoch < epoch || (p.epoch == epoch && p.sequence <= lastSnapshot)) return;
            bool snap = p.epoch != epoch || p.kind == "reset";
            if (p.epoch != epoch) { epoch = p.epoch; jump = dash = freeze = 0; inputMove = 0; pendingDashAt = -1; emoteInput.Reset(); }
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
            session.Playing = true; epoch++; snapshotSequence = lastSnapshot = 0; jump = dash = freeze = 0; emoteInput.Reset();
            BuildWorld(data, true); completed = false; elapsed = 0; clock.Begin(Time.realtimeSinceStartupAsDouble);
            session.Broadcast(Snapshot("start")); BroadcastMapBodies(true);
        }
        void BuildWorld(PlayerData[] data, bool host)
        {
            ClearWorld(); world = new GameObject("Prototype World");
            Vector2 playerStart = new Vector2(-9.7f, .65f);
            ballSpawn = new Vector2(BallStartX, BallStartY);
            bool haveSections = MapSectionAssembler.Build(mapSections, world.transform, BallReachedGoal, out Vector2? playerMarker, out Vector2? ballMarker);
            gimmicks = world.GetComponentsInChildren<MapGimmick>(true);
            foreach (var gimmick in gimmicks) gimmick.Initialize(host);
            mapBodies = new MapDynamicBodies(world.transform, gimmicks, host);
            if (playerMarker.HasValue) playerStart = playerMarker.Value;
            if (ballMarker.HasValue) ballSpawn = ballMarker.Value;
            authoredMap = haveSections;
            if (!haveSections) BuildDefaultTerrain();
            else
            {
                if (!playerMarker.HasValue) Debug.LogWarning("Authored map has no Player Start marker; using the prototype default.");
                if (!ballMarker.HasValue) Debug.LogWarning("Authored map has no Ball Start marker; using the prototype default.");
            }
            var ballGo = new GameObject("Goal ball"); ballGo.transform.SetParent(world.transform);
            ball = ballGo.AddComponent<Rigidbody2D>(); ball.mass = settings.ballMass; ball.gravityScale = settings.gravityScale; ball.interpolation = RigidbodyInterpolation2D.Interpolate; ball.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var ballCollider = ballGo.AddComponent<CircleCollider2D>(); ballCollider.radius = .6f;
            ballMaterial = CreateBallMaterial(settings);
            ballCollider.sharedMaterial = ballMaterial;
            ballGo.AddComponent<MapGoalBall>().SetNormalMaterial(ballMaterial);
            PrototypeVisuals.Shape(ballGo.transform, 2, new Color(1f,.8f,.08f), new Vector2(1.2f,1.2f), 1);
            var center = PrototypeVisuals.Shape(ballGo.transform, 2, new Color(.35f,.22f,.05f), new Vector2(.25f,.25f), 2);
            if (!host) ball.simulated = false;
            float spawnOffset = 0, previousSize = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float size = CharacterLayout.Resolve(data[i]).size;
                if (i > 0) spawnOffset += (previousSize + size) * .5f + .25f;
                var obj = new GameObject("Player " + data[i].name); obj.transform.SetParent(world.transform);
                var player = obj.AddComponent<PrototypePlayer>(); player.Initialize(settings, data[i], playerStart + new Vector2(spawnOffset, (size - 1f) * .5f), host);
                players.Add(data[i].id, player);
                previousSize = size;
            }
            ResetPositions();
        }
        void BuildDefaultTerrain()
        {
            PrototypeVisuals.Box(world.transform, "Left ground", new Vector2(-6, -.5f), new Vector2(12, 1), new Color(.32f,.42f,.46f));
            PrototypeVisuals.Box(world.transform, "Right ground", new Vector2(8, -.5f), new Vector2(10, 1), new Color(.32f,.42f,.46f));
            PrototypeVisuals.Box(world.transform, "Left boundary", new Vector2(-12.2f, 2), new Vector2(.4f, 5), new Color(.32f,.42f,.46f));
            PrototypeVisuals.Box(world.transform, "Right boundary", new Vector2(13.2f, 2), new Vector2(.4f, 5), new Color(.32f,.42f,.46f));
            var goal = new GameObject("Ball goal"); goal.transform.SetParent(world.transform); goal.transform.position = new Vector3(10,1,0);
            var trigger = goal.AddComponent<BoxCollider2D>(); trigger.isTrigger = true; trigger.size = new Vector2(1.6f,2f);
            PrototypeVisuals.Shape(goal.transform, 1, new Color(.25f,.9f,.38f,.3f), trigger.size, 0);
            var goalScript = goal.AddComponent<PrototypeGoal>(); goalScript.game = this;
        }
        void ClearWorld() { if (world != null) Destroy(world); if (ballMaterial != null) Destroy(ballMaterial); if (authoredMap && Camera.main != null) Camera.main.transform.position = initialCameraPosition; authoredMap = false; ballMaterial = null; world = null; players.Clear(); ball = null; gimmicks = Array.Empty<MapGimmick>(); mapBodies = null; pendingDashAt = -1; }
        public static PhysicsMaterial2D CreateBallMaterial(PrototypeSettings s) => new PhysicsMaterial2D("Goal ball bounce") { bounciness = Mathf.Clamp01(s.ballBounciness), friction = .2f };
        void ResetPositions()
        {
            foreach (var player in players.Values) player.ResetBody();
            foreach (var gimmick in gimmicks) gimmick.ResetGimmick();
            mapBodies?.Reset();
            if (ball != null) ball.GetComponent<MapGoalBall>()?.ResetEffects();
            if (session.IsHost) { ball.position = ballSpawn; ball.rotation = 0; ball.linearVelocity = Vector2.zero; ball.angularVelocity = 0; }
            else { ball.transform.position = ballSpawn; ball.transform.rotation = Quaternion.identity; }
            ballTarget = ballSpawn; ballAngle = 0;
        }
        void Restart()
        {
            if (!session.IsHost || !session.Playing) return;
            epoch++; snapshotSequence = 0; jump = dash = freeze = 0; inputMove = 0; pendingDashAt = -1; emoteInput.Reset(); ResetPositions();
            session.Broadcast(Snapshot("reset")); BroadcastMapBodies(true);
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
            foreach (var p in players.Values) data[i++] = p.CapturePlayerData();
            var states = new GimmickData[gimmicks.Length]; for (int j = 0; j < gimmicks.Length; j++) states[j] = gimmicks[j].Capture(j);
            return new Packet { kind = kind, epoch = epoch, sequence = ++snapshotSequence, elapsed = elapsed, completed = completed, players = data, ball = new BodyData { x = ball.position.x, y = ball.position.y, angle = ball.rotation }, gimmicks = states };
        }
        void BroadcastMapBodies(bool reliable)
        {
            if (mapBodies == null) return;
            for (int offset = 0; offset < mapBodies.Count; offset += MapDynamicBodies.ChunkSize)
                session.Broadcast(new Packet { kind = "map_bodies", epoch = epoch, sequence = snapshotSequence, mapBodyOffset = offset, mapBodies = mapBodies.Capture(offset) }, reliable);
        }
        void ApplyState(Packet p, bool snap)
        {
            lastSnapshot = p.sequence; elapsed = p.elapsed; completed = p.completed;
            if (p.kind == "reset") mapBodies?.Reset();
            var present = new HashSet<string>();
            if (p.players != null) foreach (var item in p.players) { present.Add(item.id); if (players.TryGetValue(item.id, out var player)) player.Apply(item.body, snap); }
            var remove = new List<string>(); foreach (var id in players.Keys) if (!present.Contains(id)) remove.Add(id);
            foreach (var id in remove) { Destroy(players[id].gameObject); players.Remove(id); }
            if (p.ball != null) { ballTarget = new Vector2(p.ball.x, p.ball.y); ballAngle = p.ball.angle; if (snap) { ball.transform.position = ballTarget; ball.transform.rotation = Quaternion.Euler(0,0,ballAngle); } }
            if (p.gimmicks != null) foreach (var state in p.gimmicks) if (state != null && state.index >= 0 && state.index < gimmicks.Length) gimmicks[state.index].Apply(state, snap);
        }
        string Clock(double seconds) { var t = TimeSpan.FromSeconds(Math.Max(0, seconds)); return string.Format("{0:00}:{1:00}.{2:00}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds / 10); }
        void OnGUI()
        {
            if (!session.Playing)
            {
                frontEnd.Draw(session, StartGame);
                return;
            }
            if (title == null) { title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold }; label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true }; }
            GUI.Box(new Rect(10, 10, 420, session.Playing ? 185 : Mathf.Min(Screen.height - 20, 600)), "");
            GUILayout.BeginArea(new Rect(22, 18, 396, Screen.height - 36));
            if (session.Playing)
            {
                GUILayout.Label(completed ? "COMPLETE  " + Clock(elapsed) : "TIME  " + Clock(elapsed), title);
                GUILayout.Label("A / D move   SPACE jump   SHIFT dash   F freeze", label);
                GUILayout.Label("1 / 2 / 3 emotes   Mouse: aim", label);
                if (GUILayout.Button("Restart from start", GUILayout.Height(32))) { if (session.IsHost) Restart(); else session.Send(session.Host, new Packet { kind = "restart" }); }
                if (GUILayout.Button("Leave room", GUILayout.Height(27))) session.Leave();
            }
            GUILayout.EndArea();
            if (session.Playing && Camera.main != null)
            {
                foreach (var p in players.Values)
                {
                    var layout = p.CurrentCharacterLayout;
                    Vector3 screen = Camera.main.WorldToScreenPoint(p.transform.position + Vector3.up * (Mathf.Max(.5f, .735f + .34f * layout.helmetSize + layout.helmetOffset.y) * layout.size + .225f));
                    var previousColor = GUI.color; GUI.color = PrototypeVisuals.PlayerColor(p.colorIndex);
                    GUI.Label(new Rect(screen.x - 65, Screen.height - screen.y - 15, 150, 23), p.playerName + (p.forcedFrozen ? " [ICE]" : p.frozen ? " [F]" : p.stunned ? " [STUN]" : ""));
                    GUI.color = previousColor;
                }
            }
        }
        void OnDestroy() { frontEnd.Dispose(); }
    }
    public sealed class PrototypeGoal : MonoBehaviour
    {
        public PrototypeGame game;
        void OnTriggerEnter2D(Collider2D other) { game.BallReachedGoal(other); }
    }
}
