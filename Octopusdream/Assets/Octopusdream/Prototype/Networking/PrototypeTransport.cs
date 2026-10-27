using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Steamworks;
using UnityEngine;

namespace Octopusdream
{
    [Serializable]
    public sealed class PrototypeLobby
    {
        public ulong Id;
        public string Name;
        public int Members;
        public int Capacity;
    }

    /// <summary>
    /// Host/client transport with authenticated Steam lobby membership. Direct TCP LAN
    /// mode exercises the same gameplay protocol during local multi-process development.
    /// All public events are dispatched on Unity's main thread.
    /// </summary>
    public sealed class PrototypeTransport : MonoBehaviour
    {
        public const uint DevelopmentAppId = 480;
        public const int MaxPlayers = 20;
        public const int MaxPayloadBytes = 256 * 1024;
        private const string GameKey = "octopusdream.prototype";
        private const string Protocol = "1";
        private const int SteamChannel = 27;
        private enum Mode { None, Offline, Steam, Lan }
        private Mode _mode;
        private readonly HashSet<ulong> _peers = new HashSet<ulong>();
        private readonly HashSet<ulong> _failedSteamPeers = new HashSet<ulong>();
        private readonly List<PrototypeLobby> _lobbies = new List<PrototypeLobby>();
        private readonly Dictionary<ulong, TrafficWindow> _traffic = new Dictionary<ulong, TrafficWindow>();
        private readonly IntPtr[] _steamMessages = new IntPtr[32];
        private static PrototypeTransport _steamOwner;
        private bool _steamInitialized;
        private ulong _steamUserId;
        private bool _creating;
        private ulong _joining;
        private bool _joinable = true;
        private double _steamPollAt;
        private CallResult<LobbyCreated_t> _createResult;
        private CallResult<LobbyMatchList_t> _listResult;
        private Callback<LobbyEnter_t> _entered;
        private Callback<LobbyChatUpdate_t> _membership;
        private Callback<GameLobbyJoinRequested_t> _invited;
        private Callback<SteamNetworkingMessagesSessionRequest_t> _sessionRequest;
        private Callback<SteamNetworkingMessagesSessionFailed_t> _sessionFailed;
        private Callback<SteamServersDisconnected_t> _steamDisconnected;
        private Socket _listener;
        private readonly List<LanPeer> _lanPeers = new List<LanPeer>();
        private TcpClient _connectingClient;
        private Task _connectTask;
        private double _connectStarted;
        private ulong _nextLanId = 2;

        public bool SteamAvailable { get; private set; }
        public bool Connected { get; private set; }
        public bool IsHost { get; private set; }
        public ulong LocalId { get; private set; }
        public ulong HostId { get; private set; }
        public ulong LobbyId { get; private set; }
        public string Status { get; private set; } = "연결되지 않음";
        public IReadOnlyList<PrototypeLobby> Lobbies { get { return _lobbies; } }
        public IReadOnlyCollection<ulong> Peers { get { return _peers; } }

        public event Action<ulong, byte[]> Received;
        public event Action<ulong> PeerJoined;
        public event Action<ulong> PeerLeft;
        public event Action SessionReady;
        public event Action SessionEnded;

        private void Awake()
        {
            Application.runInBackground = true;
            if (!Application.isBatchMode && !HasArgument("-noSteam")) InitializeSteam();
        }

        private static bool HasArgument(string value)
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private bool InitializeSteam()
        {
            if (_steamInitialized) return SteamAvailable;
            if (_steamOwner != null && _steamOwner != this) return false;
            try
            {
                string error;
                if (SteamAPI.InitEx(out error) != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {
                    SetStatus("Steam 연결 불가. Steam 로그인 또는 오프라인 연습을 이용하세요. " + error);
                    return false;
                }
                _steamInitialized = true;
                _steamOwner = this;
                _steamUserId = SteamUser.GetSteamID().m_SteamID;
                SteamAvailable = SteamUser.BLoggedOn();
                _createResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
                _listResult = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
                _entered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
                _membership = Callback<LobbyChatUpdate_t>.Create(OnMembershipChanged);
                _invited = Callback<GameLobbyJoinRequested_t>.Create(x => JoinSteam(x.m_steamIDLobby.m_SteamID));
                _sessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSteamRequest);
                _sessionFailed = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(OnSteamFailure);
                _steamDisconnected = Callback<SteamServersDisconnected_t>.Create(x =>
                {
                    SteamAvailable = false;
                    if (_mode == Mode.Steam) EndConnection("Steam 서버 연결이 끊겼습니다.");
                });
                SteamNetworkingUtils.InitRelayNetworkAccess();
                SetStatus("Steam 준비 완료 · App " + SteamUtils.GetAppID().m_AppId);
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++)
                    if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong lobby)) _joining = lobby;
                return SteamAvailable;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException || ex is EntryPointNotFoundException)
            {
                SetStatus("Steam 라이브러리를 불러올 수 없습니다. 오프라인/LAN 모드는 사용 가능합니다.");
                Debug.LogWarning("[Octopus Transport] Steam initialization: " + ex.Message);
                return false;
            }
        }

        private void Start()
        {
            // Delay command-line invite handling until the gameplay component has subscribed.
            if (_joining != 0) { ulong target = _joining; _joining = 0; JoinSteam(target); }
        }

        private void Update()
        {
            if (_steamInitialized)
            {
                SteamAPI.RunCallbacks();
                SteamAvailable = SteamUser.BLoggedOn();
            }
            if (_mode == Mode.Steam && Connected)
            {
                if (Time.realtimeSinceStartupAsDouble >= _steamPollAt)
                {
                    _steamPollAt = Time.realtimeSinceStartupAsDouble + 1;
                    RefreshMembers();
                }
                ReceiveSteam();
            }
            if (_mode == Mode.Lan) PumpLan();
        }

        public void HostSteam(int maxPlayers)
        {
            Leave();
            if (!InitializeSteam() || !SteamAvailable) { SetStatus("Steam을 실행하고 로그인한 뒤 다시 시도하세요."); return; }
            _mode = Mode.Steam;
            _creating = true;
            LocalId = _steamUserId;
            _joinable = true;
            SetStatus("Steam 방 만드는 중…");
            _createResult.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, Mathf.Clamp(maxPlayers, 2, MaxPlayers)));
        }

        public void JoinSteam(ulong lobbyId)
        {
            Leave();
            if (lobbyId == 0 || !InitializeSteam() || !SteamAvailable) { SetStatus("Steam 로그인과 방 ID를 확인하세요."); return; }
            _mode = Mode.Steam;
            _joining = lobbyId;
            LocalId = _steamUserId;
            SetStatus("Steam 방 참가 중…");
            SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            if (!_creating || _mode != Mode.Steam) return;
            _creating = false;
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                EndConnection("방 생성 실패: " + result.m_eResult);
                return;
            }
            LobbyId = result.m_ulSteamIDLobby;
            CSteamID lobby = new CSteamID(LobbyId);
            SteamMatchmaking.SetLobbyData(lobby, "game", GameKey);
            SteamMatchmaking.SetLobbyData(lobby, "protocol", Protocol);
            SteamMatchmaking.SetLobbyData(lobby, "name", SteamFriends.GetPersonaName() + "의 문어 방");
            SteamMatchmaking.SetLobbyData(lobby, "host", LocalId.ToString());
            HostId = LocalId;
            IsHost = true;
            Connected = true;
            SetStatus("Steam 호스트 · 방 " + LobbyId);
            SessionReady?.Invoke();
            RefreshMembers();
        }

        private void OnLobbyEntered(LobbyEnter_t result)
        {
            // Canceled asynchronous requests can still finish on Steam's backend.
            // Leave such lobbies instead of keeping invisible memberships alive.
            if (_mode != Mode.Steam)
            {
                SteamMatchmaking.LeaveLobby(new CSteamID(result.m_ulSteamIDLobby));
                return;
            }
            if (_creating || (Connected && result.m_ulSteamIDLobby == LobbyId)) return;
            if (_joining == 0 || result.m_ulSteamIDLobby != _joining)
            {
                SteamMatchmaking.LeaveLobby(new CSteamID(result.m_ulSteamIDLobby));
                return;
            }
            _joining = 0;
            CSteamID lobby = new CSteamID(result.m_ulSteamIDLobby);
            if (result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                EndConnection("방 참가 실패: " + result.m_EChatRoomEnterResponse);
                return;
            }
            if (SteamMatchmaking.GetLobbyData(lobby, "game") != GameKey || SteamMatchmaking.GetLobbyData(lobby, "protocol") != Protocol)
            {
                SteamMatchmaking.LeaveLobby(lobby);
                EndConnection("이 방은 같은 버전의 Octopusdream 방이 아닙니다.");
                return;
            }
            LobbyId = result.m_ulSteamIDLobby;
            HostId = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID;
            if (SteamMatchmaking.GetLobbyData(lobby, "host") != HostId.ToString())
            {
                EndConnection("기존 호스트가 떠난 방입니다. 새 방에 참가하세요.");
                return;
            }
            IsHost = HostId == LocalId;
            Connected = true;
            SetStatus("Steam 방 연결됨 · " + LobbyId);
            SessionReady?.Invoke();
            RefreshMembers();
        }

        public void RefreshLobbies()
        {
            if (!InitializeSteam() || !SteamAvailable) return;
            SteamMatchmaking.AddRequestLobbyListStringFilter("game", GameKey, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter("protocol", Protocol, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
            _listResult.Set(SteamMatchmaking.RequestLobbyList());
            SetStatus("Octopusdream 방 검색 중…");
        }

        private void OnLobbyList(LobbyMatchList_t result, bool ioFailure)
        {
            _lobbies.Clear();
            if (ioFailure) { SetStatus("방 검색에 실패했습니다."); return; }
            for (int i = 0; i < result.m_nLobbiesMatching; i++)
            {
                CSteamID lobby = SteamMatchmaking.GetLobbyByIndex(i);
                if (SteamMatchmaking.GetLobbyData(lobby, "game") != GameKey || SteamMatchmaking.GetLobbyData(lobby, "protocol") != Protocol) continue;
                _lobbies.Add(new PrototypeLobby { Id = lobby.m_SteamID, Name = SteamMatchmaking.GetLobbyData(lobby, "name"), Members = SteamMatchmaking.GetNumLobbyMembers(lobby), Capacity = SteamMatchmaking.GetLobbyMemberLimit(lobby) });
            }
            SetStatus("방 " + _lobbies.Count + "개 찾음");
        }

        private void OnMembershipChanged(LobbyChatUpdate_t update)
        {
            if (_mode != Mode.Steam || update.m_ulSteamIDLobby != LobbyId) return;
            // A genuine lobby leave/rejoin permits a fresh session after a transport failure.
            if ((update.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
                _failedSteamPeers.Remove(update.m_ulSteamIDUserChanged);
            RefreshMembers();
        }

        private void RefreshMembers()
        {
            if (!Connected || LobbyId == 0 || _mode != Mode.Steam) return;
            CSteamID lobby = new CSteamID(LobbyId);
            if (SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID != HostId)
            {
                EndConnection("호스트가 방을 나갔습니다. 새 방을 만들어 주세요.");
                return;
            }
            HashSet<ulong> present = new HashSet<ulong>();
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < count; i++)
            {
                ulong id = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID;
                if (id != LocalId && !_failedSteamPeers.Contains(id)) present.Add(id);
            }
            foreach (ulong id in new List<ulong>(_peers))
            {
                if (present.Contains(id)) continue;
                _peers.Remove(id);
                CloseSteamPeer(id);
                _traffic.Remove(id);
                PeerLeft?.Invoke(id);
            }
            foreach (ulong id in present)
                if (_peers.Add(id)) PeerJoined?.Invoke(id);
        }

        private bool CanReceiveSteam(ulong id)
        {
            return Connected && _mode == Mode.Steam && _peers.Contains(id) && (IsHost || id == HostId);
        }

        private void OnSteamRequest(SteamNetworkingMessagesSessionRequest_t request)
        {
            ulong id = request.m_identityRemote.GetSteamID64();
            RefreshMembers();
            if (CanReceiveSteam(id)) SteamNetworkingMessages.AcceptSessionWithUser(ref request.m_identityRemote);
            else SteamNetworkingMessages.CloseSessionWithUser(ref request.m_identityRemote);
        }

        private void OnSteamFailure(SteamNetworkingMessagesSessionFailed_t failure)
        {
            ulong id = failure.m_info.m_identityRemote.GetSteamID64();
            if (_mode != Mode.Steam || !_peers.Contains(id)) return;
            if (!IsHost && id == HostId) { EndConnection("호스트와 통신이 끊겼습니다."); return; }
            _peers.Remove(id);
            _failedSteamPeers.Add(id);
            CloseSteamPeer(id);
            PeerLeft?.Invoke(id);
            Debug.LogWarning("[Octopus Transport] Steam session failed for " + id + ": " + failure.m_info.m_szEndDebug);
        }

        private void ReceiveSteam()
        {
            int count = SteamNetworkingMessages.ReceiveMessagesOnChannel(SteamChannel, _steamMessages, _steamMessages.Length);
            for (int i = 0; i < count; i++)
            {
                IntPtr pointer = _steamMessages[i];
                try
                {
                    SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(pointer);
                    ulong sender = message.m_identityPeer.GetSteamID64();
                    if (message.m_cbSize <= 0 || message.m_cbSize > MaxPayloadBytes || !CanReceiveSteam(sender) || !AccountTraffic(sender, message.m_cbSize)) continue;
                    byte[] payload = new byte[message.m_cbSize];
                    Marshal.Copy(message.m_pData, payload, 0, payload.Length);
                    Received?.Invoke(sender, payload);
                }
                finally { SteamNetworkingMessage_t.Release(pointer); }
            }
        }

        public void InviteFriends()
        {
            if (SteamAvailable && Connected && LobbyId != 0) SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(LobbyId));
        }

        public string GetPeerName(ulong peer)
        {
            if (_steamInitialized && _mode == Mode.Steam) return SteamFriends.GetFriendPersonaName(new CSteamID(peer));
            return peer == 1 ? "호스트" : "플레이어 " + peer;
        }

        public void SetJoinable(bool value)
        {
            _joinable = value;
            if (IsHost && _mode == Mode.Steam && LobbyId != 0) SteamMatchmaking.SetLobbyJoinable(new CSteamID(LobbyId), value);
        }

        public void StartOffline()
        {
            Leave();
            _mode = Mode.Offline;
            Connected = IsHost = true;
            LocalId = HostId = 1;
            SetStatus("오프라인 연습");
            SessionReady?.Invoke();
        }

        public void HostLan(int port = 27020)
        {
            Leave();
            try
            {
                _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _listener.Bind(new IPEndPoint(IPAddress.Any, port));
                _listener.Listen(MaxPlayers);
                _listener.Blocking = false;
                _mode = Mode.Lan;
                LocalId = HostId = 1;
                _nextLanId = 2;
                _joinable = true;
                Connected = IsHost = true;
                SetStatus("LAN 호스트 · 포트 " + port);
                SessionReady?.Invoke();
            }
            catch (Exception ex) { EndConnection("LAN 호스트 시작 실패: " + ex.Message); }
        }

        public void JoinLan(string address, int port = 27020)
        {
            Leave();
            try
            {
                _mode = Mode.Lan;
                _connectingClient = new TcpClient(AddressFamily.InterNetwork);
                _connectTask = _connectingClient.ConnectAsync(address, port);
                _connectStarted = Time.realtimeSinceStartupAsDouble;
                SetStatus("LAN 연결 중…");
            }
            catch (Exception ex) { EndConnection("LAN 참가 실패: " + ex.Message); }
        }

        private void PumpLan()
        {
            if (_connectTask != null)
            {
                if (_connectTask.IsCompleted)
                {
                    if (_connectTask.IsFaulted || _connectTask.IsCanceled)
                    {
                        string detail = _connectTask.Exception?.GetBaseException().Message ?? "취소됨";
                        EndConnection("LAN 연결 실패: " + detail);
                        return;
                    }
                    Socket socket = _connectingClient.Client;
                    _connectingClient = null;
                    _connectTask = null;
                    LanPeer peer = new LanPeer(socket, 1);
                    _lanPeers.Add(peer);
                    QueueFrame(peer, 1, Encoding.UTF8.GetBytes(GameKey + "/" + Protocol));
                }
                else if (Time.realtimeSinceStartupAsDouble - _connectStarted > 10) { EndConnection("LAN 연결 시간 초과"); return; }
            }
            if (_listener != null)
            {
                for (int i = 0; i < MaxPlayers && _listener.Poll(0, SelectMode.SelectRead); i++)
                {
                    Socket socket = _listener.Accept();
                    if (!_joinable || _lanPeers.Count >= MaxPlayers - 1) { socket.Close(); continue; }
                    _lanPeers.Add(new LanPeer(socket, _nextLanId++));
                }
            }
            foreach (LanPeer peer in new List<LanPeer>(_lanPeers))
            {
                try
                {
                    if (!peer.Ready && Time.realtimeSinceStartupAsDouble - peer.CreatedAt > 10) throw new IOException("Handshake timeout");
                    FlushPeer(peer);
                    if (peer.Socket.Poll(0, SelectMode.SelectRead) && peer.Socket.Available == 0) throw new IOException("Peer closed");
                    int readBudget = MaxPayloadBytes;
                    while (peer.Socket.Available > 0 && readBudget > 0)
                    {
                        int space = peer.Input.Length - peer.InputCount;
                        if (space <= 0) throw new IOException("Frame overflow");
                        int read = peer.Socket.Receive(peer.Input, peer.InputCount, Math.Min(Math.Min(peer.Socket.Available, space), readBudget), SocketFlags.None);
                        if (read <= 0) throw new IOException("Peer closed");
                        peer.InputCount += read;
                        readBudget -= read;
                        ParseFrames(peer);
                        if (!_lanPeers.Contains(peer)) break;
                    }
                    if (_lanPeers.Contains(peer)) FlushPeer(peer);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock) { }
                catch (Exception ex) when (ex is SocketException || ex is IOException || ex is ObjectDisposedException)
                {
                    DropLanPeer(peer, ex.Message);
                }
            }
        }

        private void ParseFrames(LanPeer peer)
        {
            int offset = 0;
            while (peer.InputCount - offset >= 4)
            {
                int length = BitConverter.ToInt32(peer.Input, offset);
                if (length < 1 || length > MaxPayloadBytes + 1) throw new IOException("Invalid frame size");
                if (peer.InputCount - offset < length + 4) break;
                if (!AccountTraffic(peer.Id, length)) throw new IOException("Traffic limit exceeded");
                byte kind = peer.Input[offset + 4];
                byte[] data = new byte[length - 1];
                Buffer.BlockCopy(peer.Input, offset + 5, data, 0, data.Length);
                offset += length + 4;
                HandleLanFrame(peer, kind, data);
                if (!_lanPeers.Contains(peer)) return;
            }
            if (offset > 0)
            {
                Buffer.BlockCopy(peer.Input, offset, peer.Input, 0, peer.InputCount - offset);
                peer.InputCount -= offset;
            }
        }

        private void HandleLanFrame(LanPeer peer, byte kind, byte[] data)
        {
            if (kind == 1 && IsHost && !peer.Ready)
            {
                if (!_joinable || Encoding.UTF8.GetString(data) != GameKey + "/" + Protocol) throw new IOException("Protocol mismatch");
                using (MemoryStream stream = new MemoryStream())
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(peer.Id); writer.Write(HostId); writer.Write(_peers.Count);
                    foreach (ulong id in _peers) writer.Write(id);
                    QueueFrame(peer, 2, stream.ToArray());
                }
                peer.Ready = true;
                foreach (LanPeer other in new List<LanPeer>(_lanPeers)) if (other.Ready && other != peer) QueueFrame(other, 4, BitConverter.GetBytes(peer.Id));
                _peers.Add(peer.Id);
                PeerJoined?.Invoke(peer.Id);
            }
            else if (kind == 2 && !IsHost && !peer.Ready)
            {
                using (BinaryReader reader = new BinaryReader(new MemoryStream(data)))
                {
                    LocalId = reader.ReadUInt64(); HostId = reader.ReadUInt64();
                    int count = reader.ReadInt32();
                    if (LocalId < 2 || HostId != 1 || count < 0 || count > MaxPlayers - 2 || data.Length != 20 + count * 8) throw new IOException("Invalid welcome");
                    _peers.Add(HostId);
                    for (int i = 0; i < count; i++) _peers.Add(reader.ReadUInt64());
                }
                peer.Ready = true;
                Connected = true;
                SetStatus("LAN 연결됨 · 플레이어 " + LocalId);
                SessionReady?.Invoke();
                foreach (ulong id in new List<ulong>(_peers)) PeerJoined?.Invoke(id);
            }
            else if (kind == 3 && peer.Ready && Connected)
            {
                if (data.Length == 0) return;
                Received?.Invoke(peer.Id, data);
            }
            else if ((kind == 4 || kind == 5) && !IsHost && peer.Ready && data.Length == 8)
            {
                ulong id = BitConverter.ToUInt64(data, 0);
                if (kind == 4) { if (id != LocalId && _peers.Add(id)) PeerJoined?.Invoke(id); }
                else if (_peers.Remove(id)) PeerLeft?.Invoke(id);
            }
            else throw new IOException("Unexpected protocol frame");
        }

        public void Send(ulong peer, byte[] payload, bool reliable)
        {
            if (!Connected || peer == LocalId || payload == null || payload.Length == 0 || payload.Length > MaxPayloadBytes || !_peers.Contains(peer)) return;
            if (!IsHost && peer != HostId) return;
            if (_mode == Mode.Steam)
            {
                SteamNetworkingIdentity identity = new SteamNetworkingIdentity();
                identity.SetSteamID64(peer);
                GCHandle pin = GCHandle.Alloc(payload, GCHandleType.Pinned);
                try
                {
                    int flags = reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable;
                    flags |= Constants.k_nSteamNetworkingSend_NoNagle;
                    SteamNetworkingMessages.SendMessageToUser(ref identity, pin.AddrOfPinnedObject(), (uint)payload.Length, flags, SteamChannel);
                }
                finally { pin.Free(); }
            }
            else if (_mode == Mode.Lan)
            {
                LanPeer target = _lanPeers.Find(x => x.Id == peer && x.Ready);
                if (target != null) QueueFrame(target, 3, payload);
            }
        }

        public void Broadcast(byte[] payload, bool reliable)
        {
            if (!IsHost) { Send(HostId, payload, reliable); return; }
            foreach (ulong id in new List<ulong>(_peers)) Send(id, payload, reliable);
        }

        private void QueueFrame(LanPeer peer, byte kind, byte[] data)
        {
            if (peer.QueuedBytes + data.Length + 5 > 2 * 1024 * 1024)
            {
                DropLanPeer(peer, "Outgoing queue exceeded");
                return;
            }
            byte[] frame = new byte[data.Length + 5];
            Buffer.BlockCopy(BitConverter.GetBytes(data.Length + 1), 0, frame, 0, 4);
            frame[4] = kind;
            Buffer.BlockCopy(data, 0, frame, 5, data.Length);
            peer.Output.Enqueue(frame);
            peer.QueuedBytes += frame.Length;
        }

        private static void FlushPeer(LanPeer peer)
        {
            int budget = MaxPayloadBytes;
            while (peer.Output.Count > 0 && budget > 0)
            {
                byte[] frame = peer.Output.Peek();
                int sent;
                try { sent = peer.Socket.Send(frame, peer.OutputOffset, Math.Min(frame.Length - peer.OutputOffset, budget), SocketFlags.None); }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
                {
                    // Continue pumping inbound data even while outbound TCP is backed up.
                    return;
                }
                if (sent <= 0) throw new IOException("Send closed");
                peer.OutputOffset += sent;
                peer.QueuedBytes -= sent;
                budget -= sent;
                if (peer.OutputOffset == frame.Length) { peer.Output.Dequeue(); peer.OutputOffset = 0; }
            }
        }

        private void DropLanPeer(LanPeer peer, string reason)
        {
            if (!_lanPeers.Remove(peer)) return;
            peer.Socket.Close();
            _traffic.Remove(peer.Id);
            if (!IsHost) { EndConnection("호스트와 연결이 끊겼습니다. " + reason); return; }
            if (_peers.Remove(peer.Id))
            {
                foreach (LanPeer other in new List<LanPeer>(_lanPeers)) if (other.Ready) QueueFrame(other, 5, BitConverter.GetBytes(peer.Id));
                PeerLeft?.Invoke(peer.Id);
            }
        }

        private bool AccountTraffic(ulong id, int bytes)
        {
            if (!_traffic.TryGetValue(id, out TrafficWindow window)) { window = new TrafficWindow(); _traffic.Add(id, window); }
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - window.Start >= 1) { window.Start = now; window.Messages = 0; window.Bytes = 0; }
            window.Messages++; window.Bytes += bytes;
            return window.Messages <= 256 && window.Bytes <= 4 * 1024 * 1024;
        }

        public void Leave()
        {
            bool wasConnected = Connected;
            Connected = false;
            _creating = false;
            _joining = 0;
            _createResult?.Cancel();
            if (_steamInitialized)
            {
                if (_mode == Mode.Steam) foreach (ulong id in new List<ulong>(_peers)) CloseSteamPeer(id);
                if (LobbyId != 0) SteamMatchmaking.LeaveLobby(new CSteamID(LobbyId));
            }
            _listener?.Close(); _listener = null;
            _connectingClient?.Close(); _connectingClient = null;
            if (_connectTask != null) _connectTask.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            _connectTask = null;
            foreach (LanPeer peer in _lanPeers) peer.Socket.Close();
            _lanPeers.Clear(); _peers.Clear(); _failedSteamPeers.Clear(); _traffic.Clear();
            LobbyId = LocalId = HostId = 0;
            IsHost = false;
            _mode = Mode.None;
            SetStatus("연결되지 않음");
            if (wasConnected) SessionEnded?.Invoke();
        }

        private void CloseSteamPeer(ulong id)
        {
            SteamNetworkingIdentity identity = new SteamNetworkingIdentity();
            identity.SetSteamID64(id);
            SteamNetworkingMessages.CloseSessionWithUser(ref identity);
        }

        private void EndConnection(string reason) { Leave(); SetStatus(reason); }
        private void SetStatus(string value) { Status = value; Debug.Log("[Octopus Transport] " + value); }

        private void OnDestroy()
        {
            Leave();
            _createResult?.Dispose(); _listResult?.Dispose(); _entered?.Dispose(); _membership?.Dispose();
            _invited?.Dispose(); _sessionRequest?.Dispose(); _sessionFailed?.Dispose(); _steamDisconnected?.Dispose();
            if (_steamInitialized && _steamOwner == this)
            {
                SteamAPI.Shutdown();
                _steamOwner = null;
                _steamInitialized = false;
                SteamAvailable = false;
            }
        }

        private sealed class TrafficWindow { public double Start; public int Messages; public int Bytes; }
        private sealed class LanPeer
        {
            public readonly Socket Socket;
            public readonly ulong Id;
            public readonly byte[] Input = new byte[MaxPayloadBytes + 1024];
            public readonly Queue<byte[]> Output = new Queue<byte[]>();
            public readonly double CreatedAt;
            public int InputCount, OutputOffset, QueuedBytes;
            public bool Ready;
            public LanPeer(Socket socket, ulong id)
            {
                Socket = socket; Id = id; CreatedAt = Time.realtimeSinceStartupAsDouble;
                socket.NoDelay = true;
                socket.Blocking = false;
            }
        }
    }
}
