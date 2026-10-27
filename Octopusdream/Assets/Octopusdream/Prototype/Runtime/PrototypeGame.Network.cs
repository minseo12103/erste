using System;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Octopusdream
{
    public sealed partial class PrototypeGame
    {
        const int MaxMessageBytes = 200000;

        void SessionReady()
        {
            notice = "";
            snapshotSequence = 0;
            lastWorldSequence = -1;
            inputSequence = 0;
            if (nickname == "문어" && transport.SteamAvailable) nickname = CleanName(transport.GetPeerName(transport.LocalId));
            Phase = RoundPhase.Lobby;
            remaining = 0f;
            editing = false;
            cursorFree = true;
            if (transport.IsHost)
            {
                AddPlayer(transport.LocalId, nickname);
                handshaken.Add(transport.LocalId);
            }
            else
            {
                helloAt = Time.unscaledTime + 2f;
                SendToHost(new PrototypeMessage { kind = "hello", text = nickname });
            }
        }

        void SessionEnded()
        {
            foreach (var p in players.Values) if (p != null) Destroy(p.gameObject);
            players.Clear();
            handshaken.Clear();
            Phase = RoundPhase.Lobby;
            editing = false;
            cursorFree = true;
            remaining = 0f;
            result = "";
        }

        void PeerJoined(ulong id) { if (transport.IsHost) notice = "참가자 연결 중…"; }
        void PeerLeft(ulong id)
        {
            if (!transport.IsHost) return;
            RemovePlayer(id);
            if (IsRound)
            {
                if (!players.Values.Any(p => p.State.diver && !p.State.caught)) Finish("잠수부가 모두 나갔습니다. 문어 팀 승리");
                else CheckCaptureWin();
            }
            BroadcastWorld();
        }

        byte[] Encode(PrototypeMessage message)
        {
            message.round = roundNumber;
            return Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
        }

        void SendToHost(PrototypeMessage message)
        {
            message.round = roundNumber;
            if (transport.IsHost) HandleCommand(transport.LocalId, message);
            else if (transport.Connected) transport.Send(transport.HostId, Encode(message), message.kind != "input");
        }

        void Send(ulong id, PrototypeMessage message, bool reliable = true)
        {
            byte[] payload = Encode(message);
            if (payload.Length <= MaxMessageBytes) transport.Send(id, payload, reliable);
        }

        void Broadcast(PrototypeMessage message, bool reliable = true)
        {
            byte[] payload = Encode(message);
            if (payload.Length <= MaxMessageBytes) transport.Broadcast(payload, reliable);
        }

        void Receive(ulong sender, byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > MaxMessageBytes) return;
            PrototypeMessage message;
            try { message = JsonUtility.FromJson<PrototypeMessage>(Encoding.UTF8.GetString(payload)); }
            catch (Exception) { return; }
            if (message == null || message.protocol != 1 || string.IsNullOrEmpty(message.kind)) return;
            if (transport.IsHost) { HandleCommand(sender, message); return; }
            if (sender != transport.HostId) return;

            if (message.kind == "world" && message.world != null) ApplyWorld(message.world);
            else if (message.kind == "reject")
            {
                notice = message.text;
                transport.Leave();
            }
            else if (message.kind == "fx") ApplyEffect(message);
            else if (players.TryGetValue(message.id, out var p))
            {
                switch (message.kind)
                {
                    case "brush":
                        p.Appearance.Paint(message.vector, message.color, message.value);
                        break;
                    case "fill": p.Appearance.SetBaseColor(message.color); break;
                    case "appearance": p.Appearance.ImportPaint(message.text); break;
                }
            }
        }

        void HandleCommand(ulong sender, PrototypeMessage m)
        {
            if (m.kind == "hello")
            {
                if (!players.ContainsKey(sender))
                {
                    if (Phase != RoundPhase.Lobby || players.Count >= maxPlayers)
                    {
                        Send(sender, new PrototypeMessage { kind = "reject", text = "라운드 진행 중이거나 방이 가득 찼습니다." });
                        return;
                    }
                    AddPlayer(sender, m.text);
                }
                bool firstHandshake = handshaken.Add(sender);
                Send(sender, WorldMessage());
                if (firstHandshake)
                    foreach (var p in players.Values)
                        Send(sender, new PrototypeMessage { kind = "appearance", id = p.State.id, text = p.Appearance.ExportPaint() });
                BroadcastWorld();
                return;
            }
            if (!players.TryGetValue(sender, out var actor) || m.round != roundNumber) return;
            if (Time.unscaledTime - actor.MessageWindowStart >= 1f) { actor.MessageWindowStart = Time.unscaledTime; actor.MessagesInWindow = 0; }
            if (++actor.MessagesInWindow > 150) return;
            var s = actor.State;
            if (m.kind == "input")
            {
                if (!Finite(m.vector) || !Finite(m.yaw) || !Finite(m.pitch) || m.sequence <= actor.LastInputSequence) return;
                actor.LastInputSequence = m.sequence;
                actor.InputMove = Vector3.ClampMagnitude(m.vector, 1f);
                actor.InputAt = Time.unscaledTime;
                s.yaw = Mathf.Repeat(m.yaw, 360f);
                s.pitch = Mathf.Clamp(m.pitch, -80f, 80f);
                return;
            }
            if (m.kind == "paint")
            {
                if (!CanEdit(actor) || !s.editing || !Finite(m.vector) || !ValidColor(m.color) || !Finite(m.value)) return;
                actor.PaintAt = Time.unscaledTime;
                m.vector = m.vector.normalized;
                m.value = Mathf.Clamp(m.value, .04f, .45f);
                m.color = ClampedColor(m.color);
                actor.Appearance.Paint(m.vector, m.color, m.value);
                m.kind = "brush"; m.id = sender;
                Broadcast(m);
                return;
            }
            if (m.kind == "soft")
            {
                if (!CanEdit(actor) || !s.editing || !Finite(m.value)) return;
                actor.SoftnessAt = Time.unscaledTime;
                s.softness = Mathf.Clamp(m.value, 0f, 10f);
                actor.Appearance.SetSoftness(s.softness);
                return;
            }
            actor.CommandAt = Time.unscaledTime;
            if (m.kind == "shot" || m.kind == "skill")
            {
                if (!Finite(m.yaw) || !Finite(m.pitch)) return;
                if (m.sequence >= actor.LastInputSequence)
                {
                    actor.LastInputSequence = m.sequence;
                    s.yaw = Mathf.Repeat(m.yaw, 360f);
                    s.pitch = Mathf.Clamp(m.pitch, -80f, 80f);
                }
            }
            switch (m.kind)
            {
                case "edit": if (CanEdit(actor)) s.editing = m.flag; break;
                case "sample": if (CanEdit(actor)) SampleUnder(actor); break;
                case "clear":
                    if (CanEdit(actor) && s.editing && ValidColor(m.color))
                    {
                        m.color = ClampedColor(m.color);
                        actor.Appearance.SetBaseColor(m.color);
                        Broadcast(new PrototypeMessage { kind = "fill", id = sender, color = m.color });
                    }
                    break;
                case "volunteer":
                    if (Phase == RoundPhase.Lobby && Vector3.Distance(actor.transform.position, lobbyPosition) < 3.5f)
                    {
                        s.volunteer = !s.volunteer;
                        s.diver = s.volunteer;
                        s.editing = false;
                        actor.Appearance.SetRole(s.diver);
                        BroadcastWorld();
                    }
                    break;
                case "start": if (sender == transport.LocalId && Phase == RoundPhase.Lobby) StartRound(); break;
                case "return": if (sender == transport.LocalId && Phase == RoundPhase.Finished) ReturnToLobby(); break;
                case "skill": if (s.diver) Scan(actor); else Ink(actor); break;
                case "shot": Shoot(actor); break;
            }
        }

        bool CanEdit(PrototypePlayer p) => !p.State.diver && !p.State.caught && Phase != RoundPhase.Finished;
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static bool ValidColor(Color c) => Finite(c.r) && Finite(c.g) && Finite(c.b);
        static Color ClampedColor(Color c) => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);

        PrototypeMessage WorldMessage()
        {
            return new PrototypeMessage { kind = "world", world = new PrototypeWorldState
            {
                round = roundNumber, phase = (int)Phase, remaining = remaining, hunters = hunterCount, sequence = ++snapshotSequence,
                hideDuration = hideDuration, huntDuration = huntDuration, result = result,
                players = players.Values.Select(p => p.State).ToArray()
            }};
        }

        void BroadcastWorld() { if (transport.IsHost && transport.Connected) Broadcast(WorldMessage(), true); }

        void ApplyWorld(PrototypeWorldState world)
        {
            if (world.players == null || world.players.Length > 20 || world.phase < 0 || world.phase > 3) return;
            if (world.sequence <= lastWorldSequence) return;
            lastWorldSequence = world.sequence;
            bool newRound = roundNumber != world.round;
            bool changedPhase = Phase != (RoundPhase)world.phase;
            roundNumber = world.round;
            Phase = (RoundPhase)world.phase;
            remaining = world.remaining;
            hunterCount = world.hunters;
            hideDuration = world.hideDuration;
            huntDuration = world.huntDuration;
            result = world.result;
            foreach (ulong id in players.Keys.ToArray()) if (!world.players.Any(s => s.id == id)) RemovePlayer(id);
            foreach (var s in world.players)
            {
                bool isNew = !players.ContainsKey(s.id);
                var p = isNew ? AddPlayer(s.id, s.name) : players[s.id];
                p.Apply(s, isNew || newRound);
            }
            if (newRound || changedPhase)
            {
                editing = false;
                cursorFree = Phase == RoundPhase.Lobby || Phase == RoundPhase.Finished;
                if (Local != null) { yaw = Local.State.yaw; pitch = Local.State.pitch; }
            }
        }
    }
}
