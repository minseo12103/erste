using System.Linq;
using UnityEngine;

namespace Octopusdream
{
    public sealed partial class PrototypeGame
    {
        Vector2 rosterScroll;
        PrototypeColorPicker painterColorPicker;
        bool PainterHasTextFocus => editing && painterColorPicker != null && painterColorPicker.HasTextFocus;
        static readonly Color Accent = new Color(.25f, .9f, .77f);

        void ReleasePainterTextFocus() { painterColorPicker?.ReleaseTextFocus(); }
        void ReleasePainterResources() { painterColorPicker?.Dispose(); painterColorPicker = null; }

        void OnGUI()
        {
            if (transport == null) return;
            EnsureStyles();
            guiScale = Mathf.Max(.4f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * guiScale);
            float width = Screen.width / guiScale;
            float height = Screen.height / guiScale;
            GUI.skin.font = uiFont;
            if (!transport.Connected) DrawMainMenu(width, height);
            else if (!CanPlay)
            {
                Panel(new Rect(width / 2f - 230f, height / 2f - 70f, 460f, 140f));
                GUI.Label(new Rect(width / 2f - 200f, height / 2f - 50f, 400f, 40f), "호스트와 연결 중…", headingStyle);
                if (GUI.Button(new Rect(width / 2f - 100f, height / 2f + 10f, 200f, 36f), "취소", buttonStyle)) LeaveFromUI();
            }
            else
            {
                DrawHud(width, height);
                if (Phase == RoundPhase.Lobby && !editing) DrawLobby(width, height);
                if (editing) DrawPainter(width, height);
                if (Phase == RoundPhase.Hiding && Local.State.diver) DrawPreparation(width, height);
                if (Local.State.blinded > 0f && !Local.State.caught)
                {
                    Fill(new Rect(0f, 0f, width, height), new Color(.015f, .01f, .045f, .94f));
                    GUI.Label(new Rect(width / 2f - 250f, height / 2f - 35f, 500f, 55f), "먹물이 시야를 가렸습니다  " + Local.State.blinded.ToString("0.0") + "초", headingStyle);
                }
                if (Phase == RoundPhase.Finished) DrawResult(width, height);
                if (cursorFree && !editing && Phase == RoundPhase.Hunting)
                {
                    Panel(new Rect(width / 2f - 190f, height / 2f - 70f, 380f, 170f));
                    GUI.Label(new Rect(width / 2f - 165f, height / 2f - 55f, 330f, 40f), "라운드는 계속 진행 중입니다", headingStyle);
                    if (GUI.Button(new Rect(width / 2f - 165f, height / 2f, 155f, 40f), "계속하기 · Esc", buttonStyle)) cursorFree = false;
                    if (GUI.Button(new Rect(width / 2f + 10f, height / 2f, 155f, 40f), "방 나가기", buttonStyle)) LeaveFromUI();
                }
                if (help && Phase != RoundPhase.Finished) DrawHelp(width, height);
            }
            GUI.matrix = oldMatrix;
        }

        void LeaveFromUI() { transport.Leave(); GUIUtility.ExitGUI(); }

        void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { font = uiFont, fontSize = 31, fontStyle = FontStyle.Bold, wordWrap = true };
            titleStyle.normal.textColor = Color.white;
            headingStyle = new GUIStyle(titleStyle) { fontSize = 20 };
            textStyle = new GUIStyle(GUI.skin.label) { font = uiFont, fontSize = 16, wordWrap = true };
            textStyle.normal.textColor = new Color(.85f, .94f, .95f);
            smallStyle = new GUIStyle(textStyle) { fontSize = 13 };
            smallStyle.normal.textColor = new Color(.61f, .76f, .79f);
            buttonStyle = new GUIStyle(GUI.skin.button) { font = uiFont, fontSize = 16, padding = new RectOffset(10, 10, 5, 5) };
        }

        void Panel(Rect rect)
        {
            Fill(rect, panelColor);
            Fill(new Rect(rect.x, rect.y, 3f, rect.height), Accent);
        }

        static void Fill(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void DrawMainMenu(float width, float height)
        {
            Panel(new Rect(32f, 30f, 480f, height - 60f));
            GUILayout.BeginArea(new Rect(55f, 48f, 435f, height - 95f));
            GUILayout.Label("OCTOPUS DREAM", titleStyle);
            GUILayout.Label("색을 바꾸고, 숨어서 살아남으세요.", textStyle);
            GUILayout.Space(12f);
            GUILayout.Label("플레이어 이름", smallStyle);
            nickname = GUILayout.TextField(nickname, 20, GUILayout.Height(28f));
            GUILayout.Space(8f);
            GUILayout.Label("술래 " + hunterCount + "명  /  최대 " + maxPlayers + "명", textStyle);
            hunterCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(hunterCount, 1, maxPlayers - 1));
            if (GUILayout.Button("Steam 방 만들기", buttonStyle, GUILayout.Height(39f)))
            {
                practice = false;
                PlayerPrefs.SetString("Octopusdream.Nickname", CleanName(nickname));
                transport.HostSteam(maxPlayers);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Steam 방 찾기", buttonStyle, GUILayout.Height(33f))) transport.RefreshLobbies();
            if (GUILayout.Button("혼자 연습", buttonStyle, GUILayout.Height(33f)))
            {
                practice = true;
                transport.StartOffline();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            GUILayout.Label("방 ID로 참가", smallStyle);
            GUILayout.BeginHorizontal();
            lobbyInput = GUILayout.TextField(lobbyInput, 22, GUILayout.Height(29f));
            if (GUILayout.Button("참가", buttonStyle, GUILayout.Width(75f), GUILayout.Height(29f)))
            {
                if (ulong.TryParse(lobbyInput, out var lobby)) { practice = false; transport.JoinSteam(lobby); }
                else notice = "숫자로 된 방 ID를 입력하세요.";
            }
            GUILayout.EndHorizontal();
            lobbyScroll = GUILayout.BeginScrollView(lobbyScroll, GUILayout.Height(105f));
            foreach (var lobby in transport.Lobbies)
                if (GUILayout.Button(lobby.Name + "  " + lobby.Members + "/" + lobby.Capacity, buttonStyle, GUILayout.Height(28f)))
                { practice = false; transport.JoinSteam(lobby.Id); }
            GUILayout.EndScrollView();
            GUILayout.Label(transport.Status, smallStyle);
            if (!string.IsNullOrEmpty(notice)) GUILayout.Label(notice, smallStyle);
            advanced = GUILayout.Toggle(advanced, "LAN / 로컬 연결 테스트");
            if (advanced)
            {
                lanAddress = GUILayout.TextField(lanAddress, 80, GUILayout.Height(25f));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("LAN 호스트", buttonStyle, GUILayout.Height(30f))) { practice = false; transport.HostLan(); }
                if (GUILayout.Button("LAN 참가", buttonStyle, GUILayout.Height(30f))) { practice = false; transport.JoinLan(lanAddress); }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
            GUI.Label(new Rect(550f, height - 95f, width - 585f, 55f), "WOC PARK  /  PROTOTYPE\nSteam 테스트 App ID 480", headingStyle);
        }

        void DrawHud(float width, float height)
        {
            Panel(new Rect(18f, 18f, 315f, 145f));
            string role = Local.State.caught ? "포획됨 · 관전" : (Local.State.diver ? "잠수부 / 1인칭" : "문어 / 3인칭");
            GUI.Label(new Rect(34f, 29f, 285f, 32f), role, headingStyle);
            string phase = Phase == RoundPhase.Lobby ? "대기실" : Phase == RoundPhase.Hiding ? "문어 숨기 / 잠수 준비" : Phase == RoundPhase.Hunting ? "수색 중" : "라운드 종료";
            GUI.Label(new Rect(34f, 63f, 285f, 27f), phase + (IsRound ? "   " + FormatTime(remaining) : ""), textStyle);
            float oxygen = Phase == RoundPhase.Hunting ? remaining / Mathf.Max(1f, huntDuration) : 1f;
            Fill(new Rect(35f, 99f, 275f, 8f), new Color(.1f, .2f, .24f));
            Fill(new Rect(35f, 99f, 275f * oxygen, 8f), Accent);
            int alive = players.Values.Count(p => !p.State.diver && !p.State.caught);
            GUI.Label(new Rect(34f, 118f, 285f, 24f), "생존 문어 " + alive + "  ·  참가 " + players.Count + "/" + maxPlayers, smallStyle);

            if (Phase == RoundPhase.Hunting && !Local.State.caught)
            {
                string skill = Local.State.diver ? (Local.State.scanCooldown > 0f ? "탐지 대기 " + Local.State.scanCooldown.ToString("0") + "초" : "E  적외선 탐지 준비 완료") :
                    Local.State.inkUsed ? "먹물 사용 완료 · 이번 라운드 1회" : Local.State.hiddenScore >= 100f ? "E  먹물 사용 가능" : "먹물 충전 " + Local.State.hiddenScore.ToString("0") + "% · 시야 안에서 조준을 피하세요";
                Panel(new Rect(width / 2f - 240f, height - 129f, 480f, 43f));
                GUI.Label(new Rect(width / 2f - 225f, height - 121f, 455f, 30f), skill, textStyle);
                if (Local.State.revealed > 0f)
                {
                    Fill(new Rect(width / 2f - 220f, 24f, 440f, 45f), new Color(.62f, .12f, .08f, .9f));
                    GUI.Label(new Rect(width / 2f - 205f, 33f, 410f, 32f), "적외선에 감지되었습니다! " + Local.State.revealed.ToString("0.0") + "초", headingStyle);
                }
                if (!editing && !cursorFree)
                {
                    Fill(new Rect(width * .5f - 5f, height * .5f - 1f, 10f, 2f), Color.white);
                    Fill(new Rect(width * .5f - 1f, height * .5f - 5f, 2f, 10f), Color.white);
                }
            }
            if (Local.State.caught) GUI.Label(new Rect(width / 2f - 240f, 30f, 480f, 50f), "관전 중  ·  Q로 대상 변경", headingStyle);
        }

        void DrawLobby(float width, float height)
        {
            Panel(new Rect(18f, 177f, 315f, 447f));
            GUILayout.BeginArea(new Rect(34f, 190f, 280f, 420f));
            GUILayout.Label("중앙 작살에서 E → 술래 지원", textStyle);
            GUILayout.Label("지원 초과 시 추첨 · 부족 시 무작위 배정", smallStyle);
            GUILayout.Space(8f);
            rosterScroll = GUILayout.BeginScrollView(rosterScroll, GUILayout.Height(100f));
            foreach (var p in players.Values)
                GUILayout.Label((p.State.volunteer ? "◆ " : "○ ") + p.State.name + (p.State.id == transport.LocalId ? " (나)" : ""), smallStyle);
            GUILayout.EndScrollView();
            GUILayout.Space(8f);
            if (transport.IsHost)
            {
                GUILayout.Label("술래 " + hunterCount + "명 / 숨기 " + hideDuration.ToString("0") + "초", textStyle);
                hunterCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(hunterCount, 1, maxPlayers - 1));
                if (GUILayout.Button(practice ? "연습 시작" : "라운드 시작", buttonStyle, GUILayout.Height(36f))) SendToHost(new PrototypeMessage { kind = "start" });
            }
            else GUILayout.Label("호스트가 라운드를 시작합니다.", textStyle);
            if (transport.LobbyId != 0)
            {
                GUILayout.Label("방 ID  " + transport.LobbyId, smallStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("ID 복사", buttonStyle)) GUIUtility.systemCopyBuffer = transport.LobbyId.ToString();
                if (GUILayout.Button("친구 초대", buttonStyle)) transport.InviteFriends();
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("방 나가기", buttonStyle, GUILayout.Height(29f))) LeaveFromUI();
            GUILayout.EndArea();
            if (practice) GUI.Label(new Rect(355f, 28f, width - 390f, 60f), "혼자 연습: 문어로 시작하거나 작살을 잡고 잠수부를 선택하세요.\n연습 잠수부는 수색 방향을 회전하며 작살을 발사하지 않습니다.", textStyle);
            if (!string.IsNullOrEmpty(notice)) GUI.Label(new Rect(355f, height - 170f, width - 390f, 65f), notice, textStyle);
        }

        void DrawPainter(float width, float height)
        {
            float x = width - 315f;
            Panel(new Rect(x, 18f, 297f, 610f));
            x += 18f;
            GUI.Label(new Rect(x, 30f, 260f, 29f), "문어 색칠하기", headingStyle);
            GUI.Label(new Rect(x, 67f, 260f, 23f), "좌클릭 색칠 · 우클릭 회전 · F 완료", smallStyle);
            if (painterColorPicker == null) painterColorPicker = new PrototypeColorPicker();
            paintColor = painterColorPicker.Draw(new Rect(x, 97f, 260f, 268f), paintColor, smallStyle);
            GUI.Label(new Rect(x, 375f, 260f, 24f), "브러시 크기  " + Mathf.RoundToInt(brushRadius * 100f), textStyle);
            brushRadius = GUI.HorizontalSlider(new Rect(x, 403f, 260f, 18f), brushRadius, .04f, .45f);
            GUI.Label(new Rect(x, 431f, 260f, 29f), "SOFTNESS   " + editSoftness.ToString("0.0") + " / 10", headingStyle);
            editSoftness = GUI.HorizontalSlider(new Rect(x, 465f, 260f, 18f), editSoftness, 0f, 10f);
            GUI.Label(new Rect(x, 494f, 260f, 24f), "0 단단 · 5 말랑 · 10 얇게 퍼짐", smallStyle);
            if (GUI.Button(new Rect(x, 528f, 260f, 28f), "선택 색으로 전체 칠하기", buttonStyle)) SendToHost(new PrototypeMessage { kind = "clear", color = paintColor });
            if (GUI.Button(new Rect(x, 561f, 260f, 28f), "발밑 색 복사 · V", buttonStyle)) SendToHost(new PrototypeMessage { kind = "sample" });
            if (GUI.Button(new Rect(x, 594f, 260f, 28f), "편집 완료 · F", buttonStyle)) ToggleEdit();
        }

        void DrawPreparation(float width, float height)
        {
            Panel(new Rect(width / 2f - 290f, height / 2f - 110f, 580f, 220f));
            GUI.Label(new Rect(width / 2f - 255f, height / 2f - 80f, 510f, 50f), "잠수 장비를 준비하고 있습니다", titleStyle);
            GUI.Label(new Rect(width / 2f - 255f, height / 2f - 10f, 510f, 40f), "문어들이 숨는 중  ·  출발까지 " + Mathf.CeilToInt(remaining) + "초", headingStyle);
            GUI.Label(new Rect(width / 2f - 255f, height / 2f + 45f, 510f, 45f), "수색이 시작되면 산소가 줄어듭니다.\n좌클릭 작살 / E 적외선 탐지", textStyle);
        }

        void DrawResult(float width, float height)
        {
            Panel(new Rect(width / 2f - 330f, height / 2f - 100f, 660f, 220f));
            GUI.Label(new Rect(width / 2f - 300f, height / 2f - 70f, 600f, 80f), result, titleStyle);
            if (transport.IsHost && GUI.Button(new Rect(width / 2f - 235f, height / 2f + 45f, 220f, 42f), "대기실로 돌아가기", buttonStyle)) SendToHost(new PrototypeMessage { kind = "return" });
            if (GUI.Button(new Rect(width / 2f + 15f, height / 2f + 45f, 220f, 42f), "방 나가기", buttonStyle)) LeaveFromUI();
        }

        void DrawHelp(float width, float height)
        {
            Panel(new Rect(18f, height - 73f, width - 36f, 55f));
            string actions = Local.State.diver ? "좌클릭 작살 · E 적외선 탐지" : "F 색칠 편집 · V 발밑 색 복사 · E 먹물";
            GUI.Label(new Rect(35f, height - 64f, width - 65f, 38f), "WASD 이동 · Space 위 · Ctrl 아래   |   " + actions + "   |   Esc 마우스 · F1 도움말", textStyle);
        }

        static string FormatTime(float value)
        {
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, value));
            return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }
    }
}

