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

**도르래 연동 발판**은 제공된 그림의 철판, 도르래와 밧줄을 사용합니다. 한쪽에 플레이어나 공이 올라가면 그쪽이 내려가고 반대쪽이 올라가며, 밧줄의 표시 길이도 발판을 따라 바뀝니다. 이동 범위와 속도는 프리팹의 `Map Gimmick > Travel / Pulley Speed`에서 조절합니다. 그림은 충돌체와 분리되어 있고 재시작하면 두 발판의 높이가 복구됩니다.

**기름 웅덩이**는 바닥 위에 올려놓는 구역이며 바닥 자체는 아닙니다. `Box Collider 2D > Size X`로 범위를 조절하면 기름 그림의 양 끝은 유지되고 가운데가 반복되며, 짧은 구역은 작은 웅덩이 그림으로 바뀝니다. `Offset`으로 범위를 옮겨도 그림이 따라갑니다. 기존 플레이어·공의 마찰 효과와 기름 위 대쉬 처리는 그대로 적용됩니다. 제공된 원본 시트와 네 스프라이트는 `MapEditor/Textures/Gimmicks/Oil/OilSurface_Tiles.png`에 정리되어 있습니다.

**무거운 시멘트 포대**는 목표 공이 닿으면 기존처럼 공의 선속도와 각속도를 0으로 만들고, 닿은 위치에 제공된 충돌 먼지를 잠시 보여 줍니다. 포대는 그대로 남고 다시 충돌해도 연출이 재생됩니다. 애니메이션 속도와 스프라이트는 `Visuals (replaceable) > Cement Impact Visual`에서 조절하며, 호스트의 충돌 상태가 다른 참가자에게 전달됩니다. 재시작 시 먼지와 포대 그림이 초기화됩니다.

**용접 스파크 구역**은 주기적으로 켜지는 장치와 전기줄, 첫 충돌 지점의 스파크를 표시합니다. 플레이어나 지형이 막으면 그 뒤로 판정이 이어지지 않으며 맞은 플레이어는 기존처럼 잠시 행동할 수 없습니다. `Map Gimmick`의 `Active Seconds`, `Inactive Seconds`, `Effect Seconds`, `Beam Range`로 주기와 범위를 조절하고, `Visuals (replaceable) > Welding Spark Visual`의 `Frames Per Second`로 그림의 재생 속도를 바꿉니다. 맵 편집 중에는 범위를 확인할 수 있도록 켜진 모습으로 보이며 게임을 시작하면 실제 주기에 맞게 꺼진 상태부터 시작합니다.

**수압 장벽**은 제공된 배관과 움직이는 물줄기를 표시합니다. 삼각형이 물을 막으면 물보라와 갈라지는 물이 표시되며 삼각형 아래의 기존 보호 구간에서는 공에 아래 방향의 힘이 가해지지 않습니다. 플레이어에게 스턴은 적용되지 않습니다. `Map Gimmick > Force`로 공을 미는 힘을, `Visuals (replaceable) > Water Barrier Visual > Frames Per Second`로 물의 애니메이션 속도를 조절합니다. 재시작하면 애니메이션이 처음부터 다시 재생됩니다.

**공기 환풍구**는 공과 플레이어, 맵에 배치된 동적 `Rigidbody2D`에 바람 방향으로 같은 크기의 힘을 가합니다. 무거운 물체는 더 작게 가속하며 시소처럼 축에 연결된 물체는 바람이 닿은 위치에 따라 회전합니다. F로 고정된 플레이어와 운동학적·정적 물체는 움직이지 않습니다. `Map Gimmick > Force`로 힘을 조절하고, 맵의 일반 동적 물체 위치와 회전은 호스트가 참가자에게 전달합니다. 재시작 시 해당 물체도 배치된 위치로 돌아갑니다.

기능 오브젝트의 보이는 장치 본체에는 플레이어와 공이 부딪힙니다. 환풍구·배관·자석·용접 장치·도르래의 본체와 크레인의 지지대·회전 팔은 별도의 충돌체를 사용합니다. 크레인 회전 중에는 탑승자가 지지대에 부딪힐 수도 있습니다. 바람·가스·물·전기 같은 효과 영역과 기름 웅덩이는 별도 감지 영역으로 유지되며, 도르래 밧줄은 시각 표현입니다. 크레인 반경이나 발판 크기를 조절하면 회전 팔과 발판 지지대의 충돌체도 함께 조절됩니다.

**부실공사 벽돌 벽**은 목표 공이 설정된 속도 이상으로 부딪히면 즉시 열리면서 균열, 파편과 먼지 연출이 재생됩니다. 바닥에 남은 잔해는 장식이며 충돌하지 않습니다. 재시작하면 벽이 복구됩니다. `Map Gimmick > Impact Speed`로 파괴에 필요한 최소 상대 속도를, `Visuals (replaceable) > Brick Wall Visual > Frame Seconds`로 파괴 연출의 프레임 시간을 조절합니다. 파괴 상태와 연출 진행은 호스트에서 참가자에게 전달됩니다.

**산업용 대형 자석**은 작동 주기에 따라 장치가 빛나고 자기장이 움직입니다. 작동 중에는 기존처럼 범위 안의 플레이어와 공을 자석 쪽으로 끌어당기며, 꺼지면 자기장도 사라집니다. `Map Gimmick`의 `Force`, `Active Seconds`, `Inactive Seconds`로 힘과 주기를 조절하고, `Box Collider 2D`의 `Size`와 `Offset`으로 범위를 조절하면 자기장 그림도 따라갑니다. `Visuals (replaceable) > Magnet Field Visual > Frames Per Second`로 연출 속도를 바꿀 수 있습니다. 편집 화면에서는 범위를 확인할 수 있도록 자기장이 보이며 게임 시작과 재시작 시 꺼진 상태부터 시작합니다.

**액체 질소 가이저**는 배관에서 분출하는 가스와 함께 작동합니다. 가스에 닿아 강제로 얼어붙은 캐릭터 뒤에는 충돌하지 않는 얼음 그림이 표시되고, 자동 해제되거나 재시작하면 사라집니다. 세 도형의 얼음은 캐릭터 크기와 관계없이 같은 크기이며 그림은 `Assets/Resources/ShapeCoop/Effects/CharacterIceBlock.png`에 있습니다. F로 직접 고정한 상태에는 얼음이 표시되지 않습니다. `Map Gimmick`의 `Active Seconds`, `Inactive Seconds`, `Effect Seconds`로 분출 시간과 대기 시간, 동결 시간을 조절합니다. `Box Collider 2D`의 `Size`와 `Offset`을 바꾸면 가스 그림도 범위에 맞춰집니다. 호스트의 분출·동결 상태가 참가자에게 전달되며, 편집 화면에서는 가스 범위가 보이고 게임 시작 시 꺼진 상태부터 시작합니다.

**움직이는 타워크레인 빔**은 고정된 지지대와 중앙을 중심으로 회전하는 여섯 개의 팔·발판으로 구성됩니다. 팔은 함께 돌지만 각 발판은 항상 수평을 유지하며, 플레이어와 공이 각각의 발판에 닿습니다. 발판 위에 가만히 선 플레이어는 발판을 따라 이동하며 좌우 이동과 점프도 할 수 있습니다. 회전 팔과 지지대는 시각 표현입니다.

맵 구간에서 크레인을 선택하면 **크레인 발판 설정**에 회전 속도, 회전 반경, 발판 크기(너비·두께)가 나타납니다. Unity Inspector의 `Rotating Crane Platforms`에서도 같은 값을 바꿀 수 있습니다. 속도의 음수는 시계 방향이며 기본값은 -45도/초, 반경 2.15, 발판 크기 1.12 × 0.19입니다. 크기는 오브젝트의 로컬 단위이며 그림과 충돌체에 함께 적용됩니다. 다른 크레인과 독립적으로 저장되고, 호스트의 설정과 회전 상태가 참가자에게 전달됩니다. 재시작하면 설정은 유지되고 모든 발판이 초기 위치로 돌아갑니다.

플레이어가 스파크로 기절하면 눈동자가 사라지고 몸체가 푸르게 변하며, 캐릭터의 실루엣을 따라 전기 테두리가 표시됩니다. 기존 용접 리소스의 충돌 스파크는 처음 맞았을 때 머리 위에 한 번 나타납니다. 재시작하거나 기절이 끝나면 테두리가 사라지고 원래 색과 눈동자로 돌아갑니다. 외형만 변경하며 호스트가 전송하는 기존 기절 상태를 사용합니다. 테두리의 맥박 속도와 몸체 색조는 `Assets/Resources/ShapeCoop/CharacterStunArt.asset`에서 조절합니다.

맵 팔레트의 **기능 오브젝트 > 철골 시소 발판**은 제공된 그림의 발판과 받침대를 나누어 사용합니다. 중앙의 고정 축에 연결된 발판에 플레이어나 공이 닿으면 실제 질량과 축으로부터의 거리에 따른 회전력이 작용합니다. 플레이어와 공의 기본 질량은 모두 1이며 `Shape Coop System > Prototype Settings`에서 바꿀 수 있습니다. 발판의 질량과 회전 감쇠는 프리팹의 `Seesaw beam (physics) > Rigidbody 2D`, 최대 기울기는 같은 오브젝트의 `Hinge Joint 2D > Limits`에서 조절합니다. 호스트가 물리를 계산하고 참가자에게 발판 각도를 전달하며, 재시작 시 각도와 속도를 초기화합니다.

`Prototype Settings > Character eyes`에서 이동할 때의 눈동자 이동량(`Eye Look Distance`), 복귀 속도(`Eye Return Speed`), 대기 표정까지의 시간(`Idle Look Min/Max Delay`)과 표정 유지 시간(`Idle Look Duration`)을 조절합니다. 눈은 실제 월드 이동 방향으로 쏠리고 멈추면 중앙으로 돌아오며, 대기 중 가끔 양쪽 눈동자가 다른 방향을 봅니다. 호스트의 이동과 대기 표정 상태가 다른 참가자에게 전달됩니다.
