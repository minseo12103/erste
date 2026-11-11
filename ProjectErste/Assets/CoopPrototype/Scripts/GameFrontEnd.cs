using System;
using Steamworks;
using UnityEngine;

namespace ShapeCoop
{
    /// <summary>Draws the menu and lobby using a snapshot of the Steam room state.</summary>
    public sealed class GameFrontEnd : IDisposable
    {
        public sealed class PlayerSlot
        {
            public string name, state;
            public bool isHost, isLocal;
        }

        public sealed class RoomEntry
        {
            public string name;
            public int count;
            public CSteamID id;
        }

        public sealed class ViewState
        {
            public bool available, inLobby, isHost, canStart, overlayReady;
            public string status, localName, roomName;
            public PlayerSlot[] players = Array.Empty<PlayerSlot>();
            public RoomEntry[] rooms = Array.Empty<RoomEntry>();
        }

        enum Action { None, Create, Browse, Refresh, Join, Invite, Start, Leave, Quit }
        const float Width = 1600, Height = 900;
        static readonly Color Ink = new Color(.055f, .095f, .13f);
        static readonly Color Panel = new Color(.065f, .115f, .15f, .94f);
        static readonly Color Cream = new Color(.96f, .93f, .82f);
        static readonly Color Muted = new Color(.64f, .72f, .73f);
        static readonly Color Yellow = new Color(1f, .81f, .21f);
        static readonly Color Cyan = new Color(.26f, .79f, .88f);
        static readonly Color Line = new Color(.27f, .36f, .39f);
        static readonly GUIContent ShapeTitle = new GUIContent("SHAPE");
        static readonly GUIContent CoopTitle = new GUIContent("CO-OP");
        static readonly GUIContent BrandTitle = new GUIContent("SHAPE CO-OP");
        readonly ViewState live = new ViewState();
        Texture2D mainBackdrop, lobbyBackdrop, characters, leftScrim;
        Font textFont, displayFont;
        bool ownsDisplayFont;
        GUIStyle title, brand, heading, body, small, button, cardName, centered, bigPlus, roomName;
        Vector2 roomScroll;
        bool browsing, controlsOpen, resourcesLoaded;
        double nextRefresh;
        int joinIndex;

        public void Draw(SteamSession session, System.Action startGame)
        {
            if (session == null) return;
            if (Time.realtimeSinceStartupAsDouble >= nextRefresh || live.inLobby != session.InLobby)
            {
                Refresh(session);
                nextRefresh = Time.realtimeSinceStartupAsDouble + .25;
            }
            var action = Render(live);
            switch (action)
            {
                case Action.Create: session.CreateRoom(); break;
                case Action.Browse:
                case Action.Refresh: session.FindRooms(); break;
                case Action.Join:
                    if (joinIndex >= 0 && joinIndex < live.rooms.Length) session.Join(live.rooms[joinIndex].id);
                    break;
                case Action.Invite: session.InviteFriends(); break;
                case Action.Start: if (session.CanStart()) startGame(); break;
                case Action.Leave: session.Leave(); browsing = false; break;
                case Action.Quit: Application.Quit(); break;
            }
            if (action != Action.None) nextRefresh = 0;
        }

        // Used by isolated visual verification without accessing Steam or creating a room.
        public void DrawPreview(ViewState model) { Render(model); }
        public void DrawPreview(ViewState model, float width, float height) { Render(model, width, height); }

        void Refresh(SteamSession session)
        {
            live.available = session.Available;
            live.inLobby = session.InLobby;
            live.isHost = session.IsHost;
            live.canStart = session.CanStart();
            live.overlayReady = session.OverlayReady;
            live.status = session.Status;
            live.localName = session.Available ? SteamFriends.GetPersonaName() : "";
            live.roomName = session.InLobby ? SteamMatchmaking.GetLobbyData(session.Lobby, "name") : "";
            var members = session.Members();
            if (live.players.Length != members.Count) live.players = new PlayerSlot[members.Count];
            for (int i = 0; i < members.Count; i++)
            {
                var slot = live.players[i] ?? (live.players[i] = new PlayerSlot());
                slot.name = SteamFriends.GetFriendPersonaName(members[i]);
                slot.state = session.DisplayState(members[i]);
                slot.isHost = members[i] == session.Host;
                slot.isLocal = members[i] == session.Self;
            }
            if (live.rooms.Length != session.Rooms.Count) live.rooms = new RoomEntry[session.Rooms.Count];
            for (int i = 0; i < session.Rooms.Count; i++)
            {
                var room = live.rooms[i] ?? (live.rooms[i] = new RoomEntry());
                room.id = session.Rooms[i];
                room.name = SteamMatchmaking.GetLobbyData(room.id, "name");
                room.count = SteamMatchmaking.GetNumLobbyMembers(room.id);
            }
        }

        void Initialize()
        {
            if (!resourcesLoaded)
            {
                mainBackdrop = Resources.Load<Texture2D>("ShapeCoop/UI/MainMenuBackdrop");
                lobbyBackdrop = Resources.Load<Texture2D>("ShapeCoop/UI/LobbyBackdrop");
                characters = Resources.Load<Texture2D>("ShapeCoop/character");
                leftScrim = new Texture2D(128, 1, TextureFormat.RGBA32, false)
                {
                    name = "Menu readability gradient", wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
                };
                for (int i = 0; i < 128; i++)
                    leftScrim.SetPixel(i, 0, new Color(.025f, .055f, .08f, .56f * (1f - Mathf.SmoothStep(0, 1, i / 127f))));
                leftScrim.Apply(false, true);
                resourcesLoaded = true;
            }
            if (body != null) return;
            textFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 22);
            displayFont = Resources.Load<Font>("ShapeCoop/UI/BarlowCondensed-ExtraBold");
            ownsDisplayFont = displayFont == null;
            if (ownsDisplayFont) displayFont = Font.CreateDynamicFontFromOSFont(new[] { "Arial Black", "Arial", "Malgun Gothic" }, 100);
            displayFont.RequestCharactersInTexture("SHAPE CO-", 160, FontStyle.Bold);
            displayFont.RequestCharactersInTexture("SHAPE CO-", 26, FontStyle.Bold);
            body = TextStyle(22, Cream);
            small = TextStyle(17, Muted);
            heading = TextStyle(48, Cream, FontStyle.Bold);
            brand = TextStyle(26, Cream, FontStyle.Bold);
            brand.font = displayFont;
            title = TextStyle(160, Cream, FontStyle.Bold);
            title.font = displayFont;
            button = TextStyle(25, Ink, FontStyle.Bold);
            button.alignment = TextAnchor.MiddleCenter;
            cardName = TextStyle(27, Cream, FontStyle.Bold);
            cardName.alignment = TextAnchor.MiddleCenter;
            centered = TextStyle(20, Muted);
            centered.alignment = TextAnchor.MiddleCenter;
            bigPlus = TextStyle(80, Line);
            bigPlus.alignment = TextAnchor.MiddleCenter;
            roomName = TextStyle(22, Cream, FontStyle.Bold);
        }

        GUIStyle TextStyle(int size, Color color, FontStyle weight = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                font = textFont, fontSize = size, fontStyle = weight,
                padding = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip,
                richText = false
            };
            style.normal.textColor = color;
            return style;
        }

        Action Render(ViewState state, float viewportWidth = 0, float viewportHeight = 0)
        {
            Initialize();
            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            float screenWidth = viewportWidth > 0 ? viewportWidth : Screen.width;
            float screenHeight = viewportHeight > 0 ? viewportHeight : Screen.height;
            float scale = Mathf.Min(screenWidth / Width, screenHeight / Height);
            // Full-bleed art extends behind the centered safe area at all aspect ratios.
            Fill(new Rect(0, 0, screenWidth, screenHeight), Ink);
            var backdrop = state.inLobby ? lobbyBackdrop : mainBackdrop;
            if (backdrop != null) GUI.DrawTexture(new Rect(0, 0, screenWidth, screenHeight), backdrop, ScaleMode.ScaleAndCrop);
            float x = (screenWidth - Width * scale) * .5f;
            float y = (screenHeight - Height * scale) * .5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(x, y), Quaternion.identity, Vector3.one * scale);
            try
            {
                GUI.color = Color.white;
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape && (controlsOpen || browsing))
                {
                    if (controlsOpen) controlsOpen = false;
                    else browsing = false;
                    Event.current.Use();
                }
                GUI.enabled = previousEnabled && !controlsOpen;
                Action action;
                if (state.inLobby) { browsing = false; action = DrawLobby(state); }
                else if (browsing) action = DrawRooms(state);
                else action = DrawMain(state);
                GUI.enabled = previousEnabled;
                if (controlsOpen) DrawControls();
                if (!string.IsNullOrEmpty(GUI.tooltip)) DrawTooltip(GUI.tooltip);
                return action;
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
                GUI.enabled = previousEnabled;
            }
        }

        Action DrawMain(ViewState state)
        {
            Action result = Action.None;
            // A subtle left scrim keeps lettering legible across the illustrated background.
            GUI.DrawTexture(new Rect(0, 0, 1000, Height), leftScrim, ScaleMode.StretchToFill);
            Fill(new Rect(96, 110, 62, 6), Yellow);
            GUI.Label(new Rect(90, 119, 610, 178), ShapeTitle, title);
            var color = title.normal.textColor;
            title.normal.textColor = Yellow;
            GUI.Label(new Rect(90, 257, 610, 178), CoopTitle, title);
            title.normal.textColor = color;
            GUI.Label(new Rect(100, 438, 510, 35), "1–3인 협동 물리 퍼즐", body);
            bool interactive = !controlsOpen;
            if (Button(new Rect(96, 504, 374, 64), "방 만들기", true, state.available && interactive)) result = Action.Create;
            if (Button(new Rect(96, 582, 374, 64), "방 찾기", false, state.available && interactive))
            { browsing = true; result = Action.Browse; }
            if (Button(new Rect(96, 668, 180, 46), "조작 방법", false, interactive)) controlsOpen = true;
            if (Button(new Rect(290, 668, 180, 46), "종료", false, interactive)) result = Action.Quit;
            ConnectionStatus(state, new Rect(96, 760, 570, 70));
            if (state.available)
            {
                Fill(new Rect(1072, 812, 448, 42), new Color(.025f, .055f, .08f, .86f));
                GUI.Label(new Rect(1090, 822, 414, 28), state.overlayReady ? "SHIFT + TAB    Steam 친구" : "Steam 오버레이를 켜 주세요", small);
            }
            return result;
        }

        Action DrawRooms(ViewState state)
        {
            Action result = Action.None;
            Fill(new Rect(0, 0, Width, Height), new Color(.02f, .045f, .065f, .65f));
            GUI.Label(new Rect(96, 62, 500, 38), BrandTitle, brand);
            GUI.Label(new Rect(96, 116, 950, 66), "함께할 작업실 찾기", heading);
            GUI.Label(new Rect(98, 192, 950, 34), "친구의 방에 참여하거나 새로운 방을 만드세요.", body);
            if (Button(new Rect(1200, 132, 142, 48), "새로고침", false, state.available)) result = Action.Refresh;
            if (Button(new Rect(1358, 132, 146, 48), "돌아가기", false)) browsing = false;
            PanelBox(new Rect(96, 260, 1408, 444));
            GUI.Label(new Rect(124, 279, 900, 30), "공개 작업실", body);
            GUI.Label(new Rect(1260, 283, 215, 24), state.rooms.Length + "개의 방", small);
            Fill(new Rect(124, 327, 1352, 1), Line);
            roomScroll = GUI.BeginScrollView(new Rect(124, 346, 1354, 330), roomScroll,
                new Rect(0, 0, 1326, Mathf.Max(320, state.rooms.Length * 86)));
            if (state.rooms.Length == 0)
            {
                GUI.Label(new Rect(0, 82, 1326, 50), "참여할 수 있는 방이 없습니다.", cardName);
                GUI.Label(new Rect(0, 136, 1326, 40), "새로고침하거나 직접 방을 만들어 친구를 초대하세요.", centered);
            }
            for (int i = 0; i < state.rooms.Length; i++)
            {
                var room = state.rooms[i];
                float y = i * 86;
                Fill(new Rect(0, y, 1326, 72), new Color(.11f, .17f, .20f));
                GUI.Label(new Rect(23, y + 23, 920, 32), room.name, roomName);
                GUI.Label(new Rect(977, y + 26, 120, 26), room.count + " / 3", small);
                if (Button(new Rect(1130, y + 12, 174, 48), room.count >= 3 ? "정원 초과" : "참여하기", true, state.available && room.count < 3))
                { joinIndex = i; result = Action.Join; }
            }
            GUI.EndScrollView();
            ConnectionStatus(state, new Rect(96, 760, 900, 70));
            if (Button(new Rect(1174, 760, 330, 64), "새 방 만들기", true, state.available)) result = Action.Create;
            return result;
        }

        Action DrawLobby(ViewState state)
        {
            Action result = Action.None;
            Fill(new Rect(0, 0, Width, Height), new Color(.02f, .04f, .06f, .34f));
            GUI.Label(new Rect(96, 54, 500, 40), BrandTitle, brand);
            GUI.Label(new Rect(96, 111, 900, 70), "작업 대기실", heading);
            GUI.Label(new Rect(98, 185, 1050, 34), string.IsNullOrWhiteSpace(state.roomName) ? "친구와 함께 출발하세요." : state.roomName, body);
            GUI.Label(new Rect(1300, 140, 200, 68), state.players.Length + " / 3", heading);
            for (int i = 0; i < 3; i++)
            {
                var rect = new Rect(96 + i * 480, 260, 448, 416);
                var slot = i < state.players.Length ? state.players[i] : null;
                if (DrawPlayerCard(rect, slot, i, state.overlayReady)) result = Action.Invite;
            }
            GUI.Label(new Rect(96, 694, 1408, 30), "캐릭터는 게임 시작 시 무작위로 배정됩니다.", small);
            GUI.Label(new Rect(620, 783, 460, 28), new GUIContent("Steam 연결 정보", state.status ?? ""), small);
            if (Button(new Rect(96, 766, 196, 60), "방 나가기", false)) result = Action.Leave;
            if (Button(new Rect(312, 766, 254, 60), "Steam 친구 초대", false, state.overlayReady)) result = Action.Invite;
            if (!state.overlayReady)
                GUI.Label(new Rect(312, 834, 730, 26), "친구 초대는 Steam 오버레이에서 사용할 수 있습니다.", small);
            if (state.isHost)
            {
                if (Button(new Rect(1124, 754, 380, 76), "게임 시작", true, state.canStart)) result = Action.Start;
                if (!state.canStart) GUI.Label(new Rect(1124, 837, 380, 28), "팀원 연결을 기다리는 중", centered);
            }
            else
            {
                PanelBox(new Rect(1124, 754, 380, 76));
                GUI.Label(new Rect(1124, 754, 380, 76), "방장의 시작을 기다리는 중", centered);
            }
            return result;
        }

        bool DrawPlayerCard(Rect rect, PlayerSlot slot, int index, bool canInvite)
        {
            PanelBox(rect);
            var accent = index == 0 ? Cyan : index == 1 ? new Color(.54f, .78f, .30f) : new Color(1f, .56f, .17f);
            Fill(new Rect(rect.x, rect.y, rect.width, 5), slot == null ? Line : accent);
            if (slot == null)
            {
                GUI.Label(new Rect(rect.x, rect.y + 82, rect.width, 120), "+", bigPlus);
                GUI.Label(new Rect(rect.x, rect.y + 226, rect.width, 40), "빈 자리", cardName);
                GUI.Label(new Rect(rect.x, rect.y + 270, rect.width, 34), "함께할 친구를 초대하세요", centered);
                return Button(new Rect(rect.x + 90, rect.y + 339, rect.width - 180, 49), "친구 초대", false, canInvite);
            }
            GUI.Label(new Rect(rect.x + 24, rect.y + 23, 300, 28), slot.isHost ? "방장" : "팀원", small);
            if (slot.isLocal) GUI.Label(new Rect(rect.xMax - 74, rect.y + 23, 50, 28), "나", small);
            DrawCharacter(new Vector2(rect.center.x, rect.y + 195), index);
            GUI.Label(new Rect(rect.x + 20, rect.y + 312, rect.width - 40, 42), slot.name ?? "", cardName);
            bool connected = slot.state == "connected";
            var stateColor = connected ? Cyan : slot.state == "connection failed" ? new Color(1f, .43f, .28f) : Yellow;
            Fill(new Rect(rect.center.x - 55, rect.y + 375, 7, 7), stateColor);
            var old = centered.normal.textColor;
            centered.normal.textColor = stateColor;
            GUI.Label(new Rect(rect.x + 10, rect.y + 359, rect.width - 20, 40),
                connected ? "연결됨" : slot.state == "connection failed" ? "연결 실패" : "연결 중", centered);
            centered.normal.textColor = old;
            return false;
        }

        void DrawCharacter(Vector2 center, int index)
        {
            if (characters == null) return;
            const float size = 172;
            var rect = new Rect(center.x - size * .5f, center.y - size * .5f + 22, size, size);
            Rect source = index == 0 ? new Rect(1031, 80, 478, 406) :
                index == 1 ? new Rect(578, 101, 381, 383) : new Rect(74, 85, 401, 417);
            Crop(rect, source);
            float eyeX = index == 0 ? .145f : index == 1 ? .22f : .205f;
            float eyeY = index == 0 ? -.145f : index == 1 ? .07f : .065f;
            float eyeWidth = index == 0 ? .135f : .16f;
            float pupilY = rect.center.y - eyeY * size;
            Crop(new Rect(center.x - eyeX * size - eyeWidth * size * .5f, pupilY - .08f * size, eyeWidth * size, .16f * size), new Rect(733, 716, 71, 70));
            Crop(new Rect(center.x + eyeX * size - eyeWidth * size * .5f, pupilY - .08f * size, eyeWidth * size, .16f * size), new Rect(1233, 716, 72, 71));
            float helmetY = rect.center.y - size * 1.075f + (index == 0 ? 12f : 0f);
            Crop(new Rect(center.x - size * .56f, helmetY, size * 1.12f, size * .68f), new Rect(44, 632, 457, 279));
        }

        void Crop(Rect destination, Rect source)
        {
            var uv = new Rect(source.x / 1536f, (1024f - source.y - source.height) / 1024f, source.width / 1536f, source.height / 1024f);
            GUI.DrawTextureWithTexCoords(destination, characters, uv, true);
        }

        void ConnectionStatus(ViewState state, Rect rect)
        {
            Fill(new Rect(rect.x, rect.y + 7, 7, 7), state.available ? Cyan : Yellow);
            GUI.Label(new Rect(rect.x + 20, rect.y, rect.width - 20, 28),
                new GUIContent(state.available ? "Steam 연결됨" : "Steam 연결이 필요합니다", state.status ?? ""), small);
            string detail = state.available ? state.localName : "Steam에 로그인한 뒤 게임을 다시 실행해 주세요.";
            GUI.Label(new Rect(rect.x + 20, rect.y + 32, rect.width - 20, 29), detail ?? "", small);
            if (state.available && !string.IsNullOrEmpty(state.status) &&
                !state.status.StartsWith("Steam connected:", StringComparison.Ordinal))
                GUI.Label(new Rect(rect.x + 20, rect.y + 61, rect.width - 20, 26), state.status, small);
        }

        void DrawControls()
        {
            Fill(new Rect(0, 0, Width, Height), new Color(0, 0, 0, .74f));
            PanelBox(new Rect(410, 166, 780, 568));
            GUI.Label(new Rect(456, 207, 690, 62), "조작 방법", heading);
            ControlRow(310, "A  /  D", "이동");
            ControlRow(365, "SPACE", "점프");
            ControlRow(420, "SHIFT", "대시");
            ControlRow(475, "F", "몸 얼리기");
            ControlRow(530, "1  /  2  /  3", "감정 표현 · 마우스로 방향 지정");
            if (Button(new Rect(456, 632, 688, 60), "확인", true)) controlsOpen = false;
        }

        void ControlRow(float y, string key, string action)
        {
            Fill(new Rect(456, y, 220, 40), new Color(.17f, .23f, .26f));
            GUI.Label(new Rect(456, y, 220, 40), key, centered);
            GUI.Label(new Rect(706, y + 5, 435, 34), action, body);
        }

        void DrawTooltip(string text)
        {
            var rect = new Rect(Mathf.Clamp(Event.current.mousePosition.x + 20, 20, Width - 580),
                Mathf.Clamp(Event.current.mousePosition.y + 24, 20, Height - 180), 560, 160);
            PanelBox(rect);
            bool previousWrap = small.wordWrap;
            small.wordWrap = true;
            GUI.Label(new Rect(rect.x + 18, rect.y + 16, rect.width - 36, rect.height - 32), text, small);
            small.wordWrap = previousWrap;
        }

        bool Button(Rect rect, string text, bool primary, bool enabled = true)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            bool hovered = enabled && rect.Contains(Event.current.mousePosition);
            Color fill = primary ? Yellow : Panel;
            if (hovered) fill = primary ? new Color(1f, .90f, .43f) : new Color(.16f, .25f, .29f);
            if (!enabled) fill = new Color(.15f, .20f, .22f, .88f);
            Fill(new Rect(rect.x + 3, rect.y + 5, rect.width, rect.height), new Color(0, 0, 0, .28f));
            Fill(rect, fill);
            Border(rect, primary && enabled ? Yellow : Line);
            button.fontSize = rect.height < 54 ? 20 : 25;
            button.normal.textColor = enabled ? primary ? Ink : Cream : Muted;
            button.hover.textColor = button.normal.textColor;
            button.active.textColor = button.normal.textColor;
            button.focused.textColor = button.normal.textColor;
            bool pressed = GUI.Button(rect, text, button);
            GUI.enabled = previous;
            return pressed;
        }

        static void PanelBox(Rect rect) { Fill(rect, Panel); Border(rect, Line); }
        static void Border(Rect rect, Color color)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, 1), color);
            Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            Fill(new Rect(rect.x, rect.y, 1, rect.height), color);
            Fill(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }
        static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public void Dispose()
        {
            if (textFont != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(textFont);
                else UnityEngine.Object.DestroyImmediate(textFont);
            }
            if (ownsDisplayFont && displayFont != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(displayFont);
                else UnityEngine.Object.DestroyImmediate(displayFont);
            }
            if (leftScrim != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(leftScrim);
                else UnityEngine.Object.DestroyImmediate(leftScrim);
            }
        }
    }
}
