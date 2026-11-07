# Shape Co-op prototype

- Open `Assets/CoopPrototype/Scenes/ShapeCoop.unity` in Unity 6000.3.27f1, start Steam and sign in, then press Play. The scene is also the first scene in Build Settings.
- For Windows builds, Steamworks.NET supplies the native plugin and the build postprocessor writes `steam_appid.txt` beside the executable. Copy the entire build directory to each test PC.
- Host: Create room. Other players: Find rooms, then choose the host's room. Wait for each intended participant to show connected, then the host selects Start game. One, two, or three connected players are supported. A failed connection is shown and excluded from the game after timeout.
- A / D: world-left / world-right, tap Space for a small jump or hold it for up to 1.5x the original jump impulse, Shift: dash in the most recent movement direction, F: freeze / unfreeze. Gravity and collision physics remain active during a dash, and a jump or fall combines with the dash's horizontal velocity. Any player can request a restart; the host executes it. The clock continues across restarts and freezes on the first success.
- Select the `Shape Coop System` prefab or scene instance to adjust `Prototype Settings` in the Inspector. The world, primitive visuals, colliders and immediate-mode UI are created at runtime; `PrototypeVisuals` contains the replaceable presentation code, while `PrototypePlayer` contains movement and collision logic.
- Test with separate Steam accounts and machines. The identifier on the public App 480 lobby keeps searches limited to this prototype; it is not a private or secure room.
- For an overlay test, enable Steam Overlay in Steam settings, add `Build/ShapeCoop/ShapeCoop.exe` to Steam as a non-Steam game and launch it from Steam. Check that the game's menu reports the overlay as ready; this launch method and cross-account invitations still require verification. Both users should run this same prototype build before accepting an invite. App ID 480 belongs to Spacewar, so accepting an invite while the prototype is closed can start Spacewar instead of this executable.
- Create a room, then use `Invite Steam friends`; accepting an invite in a running copy requests room metadata and joins only a room carrying this prototype's current version identifier. An invite cannot join a game that has already started.

## 캐릭터 외형

Unity에서 `Tools > Shape Coop > Character Size Comparison` 또는 맵 에디터의 **캐릭터 크기 비교**를 열면 세 도형을 같은 바닥선에서 볼 수 있습니다. 모자와 충돌체 표시를 켜고 끌 수 있으며, `u`는 Unity의 월드 단위입니다. 비교 창은 임시 미리보기이며 현재 작업 씬을 변경하지 않습니다.

각 도형 아래에서 **크기**, **모자 크기**, **모자 좌우**, **모자 높이**를 조절해 결과를 확인하고 **게임에 적용**을 누르면 다음 게임부터 사용합니다. **적용된 값 불러오기**는 저장된 값으로 돌아가고, **기본값 미리보기**는 저장하지 않고 처음 크기를 보여 줍니다. 크기는 그림과 몸체 충돌체를 함께 바꾸며 모자는 충돌하지 않습니다. 모자 크기와 위치는 몸체 크기를 기준으로 합니다. 플레이 중에는 적용할 수 없습니다.

적용하면 `Assets/Resources/ShapeCoop/CharacterLayout.asset`이 만들어지므로 협업 시 해당 파일과 `.meta`를 함께 공유하세요. Windows 실행 파일에는 적용 후 새로 빌드해야 설정이 들어갑니다. 게임 시작 시 호스트의 설정이 참가자에게 전달됩니다.

제공된 원본 시트는 `Assets/Resources/ShapeCoop/character.png`에 있으며 삼각형·사각형·원과 모자·왼쪽/오른쪽 눈동자를 각각 스프라이트 영역으로 사용합니다. 물리 도형은 `PrototypePlayer`, 외형과 눈동자 애니메이션은 `PrototypeCharacterAppearance`에 분리되어 있습니다. 원본 시트와 동일한 배치의 이미지를 교체하면 그림을 바꿀 수 있으며, 배치가 다른 시트를 쓸 때는 `PrototypeCharacterAppearance.Crop`의 영역과 눈 위치를 함께 조정하세요.

눈동자는 실제 이동 속도의 좌우와 상하 방향에 반응해 각자의 눈 안에서 서로 반대 방향으로 움직이고, 멈추면 부드럽게 중앙으로 돌아옵니다. 정지한 채로 일정 시간이 지나 나타나는 표정은 다시 움직이기 전까지 유지됩니다. 표정 대기 시간과 이동 거리, 복귀 속도는 `PrototypeSettings`의 Character eyes에서 조절합니다.

게임과 구간 테스트에서 키보드 상단 **1**은 손가락질, **2**는 칭찬, **3**은 욕설 이모션입니다. 마우스 포인터로 방향을 정하세요. 짧게 눌러도 동작이 잠시 나타나고, 키를 누르고 있으면 계속 표시하며 방향을 바꿀 수 있습니다. 2와 3은 즉시 손 모양이 나오고 마우스 방향에 직각으로 돌아가며 캐릭터 좌우에서 반전됩니다. 이모션은 충돌하지 않으며, 호스트가 상태를 다른 참가자에게 전달합니다. 재시작하면 표시가 초기화됩니다.

맵 팔레트의 **기본 오브젝트** 폴더에는 제공된 이미지로 만든 사각형·직각삼각형 지형이 각각 기본형과 녹슨형으로 들어 있습니다. **배치용** 태그로도 찾을 수 있습니다. 네 프리팹은 1×1 Unity 단위이며 씬에서 크기와 회전을 바꿀 수 있습니다. 삼각형은 X 크기를 음수로 설정해 반대 방향으로 뒤집을 수 있고 충돌체도 함께 반전됩니다. 원본 이미지는 `Assets/CoopPrototype/MapEditor/Textures/BasicObject.png`입니다.

맵 구간 에디터에서 **격자 맞춤**을 켜면 처음 배치할 때와 배치한 오브젝트를 이동 도구(W) 또는 사각형 도구(T)로 옮길 때 같은 격자 간격을 사용합니다. 격자를 끄면 자유롭게 옮길 수 있으며, 맵 밖의 선택이나 다른 도구에는 Unity의 원래 설정이 복원됩니다.

**무너지는 모래 상자**는 플레이어나 공이 위에 닿으면 포대가 찌그러지고 모래가 새다가 터집니다. `Map Gimmick > Activation Delay`가 끝나는 순간 충돌이 사라지며 기본값은 1.5초입니다. 파편과 먼지는 그 뒤 잠시 표시되고, 재시작하면 발판이 복구됩니다. 이미지 프레임은 `Sand Frames`에서 교체할 수 있으며 호스트의 진행 상태가 참가자에게 전달됩니다.

**공기 환풍구** 프리팹은 본체 그림과 8프레임 바람 이펙트를 사용합니다. 바람 그림의 재생 속도와 프레임은 프리팹의 `Visuals (replaceable) > Air > Wind Vent Effect`에서 조절할 수 있습니다. 이펙트는 시각 전용이며, 공을 들어 올리는 기존 범위와 힘은 `Map Gimmick` 및 트리거 충돌체에서 설정합니다.

`Prototype Settings > Character eyes`에서 이동할 때의 눈동자 이동량(`Eye Look Distance`), 복귀 속도(`Eye Return Speed`), 대기 표정까지의 시간(`Idle Look Min/Max Delay`)과 표정 유지 시간(`Idle Look Duration`)을 조절합니다. 눈은 실제 월드 이동 방향으로 쏠리고 멈추면 중앙으로 돌아오며, 대기 중 가끔 양쪽 눈동자가 다른 방향을 봅니다. 호스트의 이동과 대기 표정 상태가 다른 참가자에게 전달됩니다.
