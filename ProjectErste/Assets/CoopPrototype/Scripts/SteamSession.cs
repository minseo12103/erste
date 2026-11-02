using System;
using System.Collections.Generic;
using System.Text;
using Steamworks;
using UnityEngine;

namespace ShapeCoop
{
    public sealed class SteamSession : MonoBehaviour
    {
        const string Key = "shape_coop_project", Value = "projecterste_prototype_v1";
        public bool Available { get; private set; }
        public string Status { get; private set; } = "Initializing Steam...";
        public CSteamID Lobby { get; private set; }
        public CSteamID Self => Available ? SteamUser.GetSteamID() : CSteamID.Nil;
        public CSteamID Host => InLobby ? SteamMatchmaking.GetLobbyOwner(Lobby) : CSteamID.Nil;
        public bool InLobby => Lobby != CSteamID.Nil;
        public bool IsHost => InLobby && Host == Self;
        public readonly List<CSteamID> Rooms = new List<CSteamID>();
        public readonly Dictionary<ulong, double> Connected = new Dictionary<ulong, double>();
        readonly Dictionary<ulong, double> firstSeen = new Dictionary<ulong, double>();
        readonly Dictionary<ulong, string> published = new Dictionary<ulong, string>();
        readonly HashSet<ulong> failed = new HashSet<ulong>();
        public event Action<CSteamID, Packet> Received;
        public event Action PeerChanged;
        Callback<LobbyCreated_t> created; Callback<LobbyEnter_t> entered;
        Callback<LobbyMatchList_t> list; Callback<LobbyChatUpdate_t> members;
        Callback<P2PSessionRequest_t> request; Callback<P2PSessionConnectFail_t> failure;
        Callback<SteamServersDisconnected_t> disconnected;
        double nextPing; bool playing; CSteamID gameHost;
        public bool Playing
        {
            get => playing;
            set { if (value && !playing) gameHost = Host; playing = value; }
        }
        PrototypeSettings settings;

        void Awake()
        {
            settings = GetComponent<PrototypeSettings>();
            try
            {
                Available = SteamAPI.Init();
                if (!Available) { Status = "Steam unavailable. Start Steam and sign in, then restart."; return; }
                created = Callback<LobbyCreated_t>.Create(OnCreated);
                entered = Callback<LobbyEnter_t>.Create(OnEntered);
                list = Callback<LobbyMatchList_t>.Create(OnList);
                members = Callback<LobbyChatUpdate_t>.Create(_ => PeerChanged?.Invoke());
                request = Callback<P2PSessionRequest_t>.Create(x => { if (IsMember(x.m_steamIDRemote)) SteamNetworking.AcceptP2PSessionWithUser(x.m_steamIDRemote); });
                failure = Callback<P2PSessionConnectFail_t>.Create(x => Fail(x.m_steamIDRemote, "P2P connection failed"));
                disconnected = Callback<SteamServersDisconnected_t>.Create(_ => { Status = "Steam disconnected"; Leave(); });
                SteamNetworking.AllowP2PPacketRelay(true);
                Status = "Steam connected: " + SteamFriends.GetPersonaName();
            }
            catch (Exception e) { Available = false; Status = "Steam initialization failed: " + e.Message; Debug.LogException(e); }
        }
        public void CreateRoom() { if (!Available || InLobby) return; Status = "Creating room..."; SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 3); }
        public void FindRooms()
        {
            if (!Available || InLobby) return;
            Rooms.Clear(); Status = "Searching...";
            SteamMatchmaking.AddRequestLobbyListStringFilter(Key, Value, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.RequestLobbyList();
        }
        public void Join(CSteamID id)
        {
            if (!Available || InLobby || SteamMatchmaking.GetLobbyData(id, Key) != Value) return;
            Status = "Joining..."; SteamMatchmaking.JoinLobby(id);
        }
        void OnCreated(LobbyCreated_t x)
        {
            if (x.m_eResult != EResult.k_EResultOK) { Status = "Room creation failed: " + x.m_eResult; return; }
            Lobby = new CSteamID(x.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(Lobby, Key, Value);
            SteamMatchmaking.SetLobbyData(Lobby, "name", SteamFriends.GetPersonaName() + "'s room");
            Status = "Room created";
        }
        void OnEntered(LobbyEnter_t x)
        {
            if (x.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) { Status = "Join failed: " + x.m_EChatRoomEnterResponse; return; }
            Lobby = new CSteamID(x.m_ulSteamIDLobby);
            if (!IsHost && SteamMatchmaking.GetLobbyData(Lobby, Key) != Value) { Leave(); Status = "Room identifier mismatch"; return; }
            Connected.Clear(); firstSeen.Clear(); failed.Clear(); Status = "In room: connecting game transport";
            PeerChanged?.Invoke();
        }
        void OnList(LobbyMatchList_t x)
        {
            Rooms.Clear();
            for (int i = 0; i < x.m_nLobbiesMatching; i++) { var room = SteamMatchmaking.GetLobbyByIndex(i); if (SteamMatchmaking.GetLobbyData(room, Key) == Value) Rooms.Add(room); }
            Status = "Rooms found: " + Rooms.Count;
        }
        public List<CSteamID> Members()
        {
            var result = new List<CSteamID>(); if (!InLobby) return result;
            int count = SteamMatchmaking.GetNumLobbyMembers(Lobby);
            for (int i = 0; i < count; i++) result.Add(SteamMatchmaking.GetLobbyMemberByIndex(Lobby, i));
            return result;
        }
        bool IsMember(CSteamID id) => InLobby && Members().Contains(id);
        public bool IsConnected(CSteamID id) => id == Self || Connected.ContainsKey(id.m_SteamID);
        public bool IsFailed(CSteamID id) => failed.Contains(id.m_SteamID);
        public string DisplayState(CSteamID id)
        {
            if (!IsHost && id == Self) return IsFailed(Host) ? "connection failed" : IsConnected(Host) ? "connected" : "connecting...";
            if (IsHost || id == Self || id == Host) return IsFailed(id) ? "connection failed" : IsConnected(id) ? "connected" : "connecting...";
            string state = SteamMatchmaking.GetLobbyData(Lobby, "peer_" + id.m_SteamID);
            return state == "ready" ? "connected" : state == "failed" ? "connection failed" : "connecting...";
        }
        public bool CanStart()
        {
            if (!IsHost || Playing) return false;
            foreach (var id in Members()) if (id != Self && !IsConnected(id) && !IsFailed(id)) return false;
            return true;
        }
        public void Send(CSteamID id, Packet p, bool reliable = true)
        {
            if (!Available || id == Self || id == CSteamID.Nil) return;
            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(p));
            SteamNetworking.SendP2PPacket(id, data, (uint)data.Length, reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliableNoDelay);
        }
        public void Broadcast(Packet p, bool reliable = true) { foreach (var id in Members()) if (id != Self && IsConnected(id)) Send(id, p, reliable); }
        void Fail(CSteamID id, string reason)
        {
            Connected.Remove(id.m_SteamID); failed.Add(id.m_SteamID); Status = reason + ": " + SteamFriends.GetFriendPersonaName(id);
            if (Playing && !IsHost && id == Host) { Leave(); Status = "Host connection lost"; }
            PeerChanged?.Invoke();
        }
        void Update()
        {
            if (!Available) return;
            SteamAPI.RunCallbacks();
            while (SteamNetworking.IsP2PPacketAvailable(out uint size))
            {
                if (size > 65536) { var discard = new byte[size]; SteamNetworking.ReadP2PPacket(discard, size, out _, out _); continue; }
                byte[] bytes = new byte[size];
                if (!SteamNetworking.ReadP2PPacket(bytes, size, out uint read, out CSteamID from) || !IsMember(from)) continue;
                Packet p; try { p = JsonUtility.FromJson<Packet>(Encoding.UTF8.GetString(bytes, 0, (int)read)); } catch { continue; }
                if (p == null) continue;
                if (p.kind == "ping") { Send(from, new Packet { kind = "pong" }); continue; }
                if (p.kind == "pong") { Connected[from.m_SteamID] = Time.realtimeSinceStartupAsDouble; failed.Remove(from.m_SteamID); continue; }
                // A start packet can race the client's own pong even though the host has
                // already completed the round trip. The lobby owner is still verified here.
                if (IsConnected(from) || (!IsHost && from == Host && p.kind == "start")) Received?.Invoke(from, p);
            }
            if (!InLobby || Time.realtimeSinceStartupAsDouble < nextPing) return;
            double now = Time.realtimeSinceStartupAsDouble; nextPing = now + 1;
            var current = Members();
            if (!current.Contains(Host) || (Playing && Host != gameHost)) { Leave(); Status = "Host left the room"; return; }
            var active = new HashSet<ulong>();
            foreach (var id in current)
            {
                if (id == Self || (!IsHost && id != Host)) continue;
                active.Add(id.m_SteamID);
                if (!firstSeen.ContainsKey(id.m_SteamID)) firstSeen[id.m_SteamID] = now;
                double since = Connected.TryGetValue(id.m_SteamID, out double last) ? last : firstSeen[id.m_SteamID];
                if (now - since > settings.peerTimeout && !failed.Contains(id.m_SteamID)) Fail(id, "Connection timed out");
                if (!failed.Contains(id.m_SteamID)) Send(id, new Packet { kind = "ping" });
            }
            var stale = new List<ulong>(); foreach (var id in Connected.Keys) if (!active.Contains(id)) stale.Add(id);
            foreach (var id in stale) { Connected.Remove(id); firstSeen.Remove(id); failed.Remove(id); PeerChanged?.Invoke(); }
            stale.Clear(); foreach (var id in firstSeen.Keys) if (!active.Contains(id)) stale.Add(id);
            foreach (var id in stale) { firstSeen.Remove(id); failed.Remove(id); published.Remove(id); }
            if (IsHost) foreach (var id in current)
            {
                if (id == Self) continue;
                string state = IsConnected(id) ? "ready" : IsFailed(id) ? "failed" : "connecting";
                if (!published.TryGetValue(id.m_SteamID, out string previous) || previous != state)
                { SteamMatchmaking.SetLobbyData(Lobby, "peer_" + id.m_SteamID, state); published[id.m_SteamID] = state; }
            }
        }
        public void Leave()
        {
            if (Available && InLobby) { foreach (var id in Members()) if (id != Self) SteamNetworking.CloseP2PSessionWithUser(id); SteamMatchmaking.LeaveLobby(Lobby); }
            Lobby = CSteamID.Nil; Connected.Clear(); firstSeen.Clear(); failed.Clear(); published.Clear(); Playing = false; PeerChanged?.Invoke();
        }
        void OnDestroy() { Leave(); if (Available) SteamAPI.Shutdown(); }
    }
}
