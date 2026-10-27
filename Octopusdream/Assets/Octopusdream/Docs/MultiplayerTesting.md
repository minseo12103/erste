# Octopusdream — 멀티플레이 테스트 안내

## 먼저 혼자 실행하기

1. Unity에서 `Assets/WOC/Scenes/WOC_Ct_Building_Park.unity`를 연다.
2. Play → Game 탭 → **혼자 연습**을 선택한다.
3. `F`로 편집모드를 열고 좌클릭 드래그로 색칠한다. 우클릭으로 뒷면까지 카메라를 돌리고, SOFTNESS 슬라이더를 조절한다.
4. 편집을 끝내고 이동한다. `V`로 발밑 색을 가져왔을 때 문어 전체가 바뀌는지 확인한다.
5. 대기실에서 **연습 시작**을 눌러 숨기·수색·산소 시간을 확인한다. 잠수부로 연습하려면 중앙 작살 근처에서 먼저 `E`를 눌러 지원한다.

`Esc`는 게임 시간을 멈추지 않는다. 수색 중 마우스를 해제해도 다른 참가자의 움직임과 산소 시간은 계속 진행된다.

## Windows 개발 빌드 준비

1. Unity 메뉴 **Octopusdream → Build Windows Prototype**을 실행한다.
2. 빌드가 성공하면 `Builds/Windows/Octopusdream.exe`를 사용한다.
3. 테스트 참가자에게 **`Builds/Windows` 폴더 전체**를 전달한다. 실행 파일만 보내면 데이터·Unity 런타임·Steam 라이브러리가 빠질 수 있다.
4. 모든 참가자는 같은 빌드를 사용한다. 실행 파일 옆의 `steam_appid.txt`에는 개발용 ID `480`이 들어 있어야 한다.

Steam 외부에서 개발용 실행 파일을 직접 실행할 때 `steam_appid.txt`로 App ID를 지정하는 방식은 [Valve의 Steamworks 예제 안내](https://partner.steamgames.com/doc/sdk/api/example)에 설명되어 있다. 최종 Steam 출시에서는 Octopusdream의 고유 App ID와 출시 설정을 사용해야 한다.

## 실제 Steam 연결: PC 두 대

준비물은 같은 개발 빌드, PC 두 대, 서로 다른 Steam 계정 두 개다. 두 PC 모두 Steam을 실행하고 로그인한다. 같은 계정으로 두 위치에서 로그인하면 기존 로비 접속이 해제될 수 있으므로 참가자마다 별도 계정을 사용한다. [Steam 로비 인증 문서](https://partner.steamgames.com/doc/features/multiplayer/matchmaking#authentication)

1. **두 PC 모두 먼저 Octopusdream 개발 빌드를 실행한다.** App ID 480에서는 Steam에 Spacewar로 표시될 수 있다. [Steamworks.NET FAQ](https://steamworks.github.io/faq/)
2. 호스트 PC에서 이름과 술래 인원을 정한 뒤 **Steam 방 만들기**를 누른다.
3. 참가자는 **Steam 방 찾기**에서 호스트 방을 선택하거나, 호스트가 복사한 숫자 **방 ID**를 입력해 참가한다. 호스트의 **친구 초대**를 통해 온 Steam 초대를 수락해도 된다.
4. 두 화면의 대기실 명단에 서로가 나타나는지 확인한다. 같은 위치에서 상대 캐릭터가 보이고 움직이는지도 확인한다.
5. 참가자 한 명이 문어 편집모드에서 서로 다른 면에 색칠한다. 상대 화면에서도 동일한 색칠이 보이는지 확인한다.
6. 필요하면 중앙 작살 근처에서 `E`로 잠수부에 지원한다. 호스트가 술래 수를 확인하고 **라운드 시작**을 누른다.
7. 문어는 숨고 잠수부는 준비 화면에서 대기한다. 30초 뒤 잠수부가 수색할 수 있어야 한다.
8. 작살·적외선·먹물·포획·관전·승리 화면을 확인한 뒤 호스트가 대기실로 돌아간다.

Steam 초대는 게임이 실행 중인지에 따라 게임 실행 또는 로비 참가 콜백으로 처리된다. 480을 쓰는 개발 빌드에서는 앱 ID가 Spacewar를 가리키므로, 게임을 닫은 상태에서 초대를 수락하면 프로토타입 대신 Spacewar가 실행될 수 있다. 따라서 위 순서처럼 **양쪽 프로토타입을 먼저 실행한 뒤 초대**한다. [Steam 초대 동작 문서](https://partner.steamgames.com/doc/features/multiplayer/matchmaking#friends-invites-and-lobbies)

Unity Editor 안에서는 Steam 오버레이가 정상적으로 나타나지 않을 수 있다. 친구 초대와 오버레이 확인은 Windows 개발 빌드를 기준으로 한다. [Steamworks.NET 오버레이 안내](https://steamworks.github.io/faq/)

## 로비 구분과 연결 조건

- Steam 개발 App ID는 `480`이다.
- 프로젝트 로비 식별값은 `octopusdream.prototype`, 통신 프로토콜 버전은 `1`이다. 방 검색과 입장 과정에서 둘 다 확인한다.
- 최대 인원은 호스트를 포함해 20명이다. 실제 성능은 참가자의 컴퓨터와 회선, 게임 데이터량에 따라 따로 확인해야 한다.
- 라운드가 시작되면 신규 입장이 닫힌다. 다음 참가자는 대기실로 돌아온 뒤 입장한다.
- 호스트가 나가면 참가자의 연결도 끝난다. 현재 호스트 이전 기능은 없다.

로비 데이터와 검색 필터는 [Steam Matchmaking & Lobbies 공식 문서](https://partner.steamgames.com/doc/features/multiplayer/matchmaking)에 설명된 기능을 사용한다.

## LAN / 같은 PC의 두 실행 창

Steam 계정 두 개가 준비되지 않았을 때에는 게임 로직과 네트워크 메시지 흐름을 LAN 모드로 확인할 수 있다. LAN 테스트 성공은 Steam 접속 검증을 대신하지 않는다.

1. 개발 빌드를 두 개 실행한다. Steam 초기화를 건너뛰려면 실행 인수로 `-noSteam`을 사용한다. 예: `Octopusdream.exe -noSteam`.
2. 각 창에서 **LAN / 로컬 연결 테스트**를 펼친다.
3. 첫 번째 창에서 **LAN 호스트**를 누른다.
4. 같은 PC의 두 번째 창에서는 주소를 `127.0.0.1`로 두고 **LAN 참가**를 누른다.
5. 다른 PC끼리 연결할 때는 참가자 쪽에 호스트의 같은 네트워크 내 IPv4 주소를 입력한다.
6. 두 캐릭터의 이동, 색칠, 역할 배정, 라운드 시작과 종료를 확인한다.

LAN 연결은 TCP 포트 `27020`을 사용한다. 연결되지 않으면 호스트 실행 여부, 주소, 두 PC의 네트워크와 해당 포트 접근 상태를 확인한다. 같은 PC에서 LAN 호스트는 하나만 실행한다.

## 기능별 확인 항목

| 확인 대상 | 기대 결과 |
|---|---|
| 문어 이동 | 3인칭, WASD 이동·Space 상승·Ctrl 하강 |
| 잠수부 이동 | 1인칭, 숨기 시간에는 이동과 수색 불가 |
| 편집모드 | F로 정지 상태에서 색칠, 색상·채도 평면/밝기 막대/RGB 숫자 입력, 우클릭 카메라 회전, F로 복귀 |
| 그림 동기화 | 다른 참가자 화면에서 같은 면과 위치에 같은 그림 표시 |
| 새 참가자 | 대기실의 기존 그림과 캐릭터 상태 수신 |
| V 주변색 복사 | 바로 아래 표면의 텍스처 색과 재질 색을 문어 전체에 적용 |
| SOFTNESS | 바닥에 접촉하며 0은 단단한 구체, 5는 둥글게 눌리고 가장자리가 처짐, 10은 얇게 퍼져 단차를 따름. Space로 표면을 떠나면 복원 |
| 변형된 몸체의 판정 | 색칠과 작살이 현재 보이는 문어 표면에 적용됨. 이동용 충돌체는 고정 크기 |
| 술래 배정 | 지원 우선 무작위 선정, 부족하면 다른 참가자에서 충원 |
| 적외선 | 장애물에 가려지지 않은 앞쪽 문어가 3.5초 강조되고 해당 문어에게 경고 |
| 먹물 충전 | 술래 시야 안이지만 정면 조준·적외선 탐지를 피할 때 100점까지 충전 |
| 먹물 사용 | E로 1회 사용, 사용 순간 5m 안 잠수부 시야를 5초 방해 |
| 작살·포획 | 한 번 명중하면 문어가 포획되고 Q로 관전 대상 변경 |
| 시간 승리 | 수색 180초가 끝날 때 문어가 남아 있으면 문어 팀 승리 |
| 전원 포획 | 모든 문어가 포획되면 잠수부 팀 승리 |
| 호스트 종료 | 참가자도 방 연결 종료, 자동 호스트 이전 없음 |

## 검증 결과와 오류 확인

저장된 실행 보고서의 결과는 다음과 같다. 색상창·접촉 변형 변경 후 2026-10-27에 기능·접촉·LAN·화면 검사를 다시 실행했다.

| 검증 범위 | 확인 결과 | 근거 파일 |
|---|---|---|
| Unity Play Mode 동작 | 15개 검사 모두 통과, `runtimeErrors` 빈 배열 | `Logs/Prototype/verification.json` |
| Windows x64 개발 빌드 | `Succeeded`, 오류 0개·경고 0개 | `Logs/Prototype/build.json` |
| 표면 접촉과 변형 | 4개 검사 통과: 0·5·10 바닥 접촉, 계단 처짐·관통 방지, 변형된 면의 색칠·빈 공간 명중 방지, 공중 복원 | `Logs/Prototype/surface-verification.json` |
| SOFTNESS 5·10 비교 화면 | 임시 계단에서 둥근 몸체와 얇은 몸체의 접촉·처짐 및 새 색상창 표시 확인 | `Logs/Prototype/softness-5.png`, `Logs/Prototype/softness-10.png` |
| GPU 표면색 복사 | Direct3D 11에서 읽기 불가능한 텍스처의 네 지점 샘플이 기준값과 일치. UV 변환·반복·재질 색상·렌더 상태 복원 확인 | `Logs/Prototype/graphics.json` |
| 실제 독립 실행 호스트 프로세스의 LAN 연결 | `passed: true`, 연결 명단·역할·색칠·SOFTNESS 9·이동 입력 확인 | `Logs/Prototype/lan-host.json` |
| 실제 독립 실행 참가자 프로세스의 LAN 연결 | `passed: true`, 호스트의 수색 단계·역할·외형·이동 상태 수신 확인 | `Logs/Prototype/lan-client.json` |
| Steam 계정 두 개의 실제 접속 | 미검증. LAN 보고서의 `steamVerified`도 `false` | 별도 Steam 테스트 필요 |
| 실제 20인 부하와 장시간 플레이 | 미검증 | 별도 인원·부하 테스트 필요 |
| 표시된 Windows 창의 D3D12 화면 | 공원 맵·색칠된 구체·한글 편집 UI 표시 확인, 조작 항목 잘림 없음. 실행 보고서 통과·로그의 런타임 예외 없음 | `Logs/Prototype/preview-visible.png`, `Logs/Prototype/visual-visible.json`, `Logs/Prototype/visual-visible.log` |

화면 확인은 실제 표시된 창의 캡처 이미지를 기준으로 했다. 앞선 숨김 창에서 검게 저장된 캡처는 화면 검증의 근거에서 제외했다.

Play Mode 검사에는 준비된 맵의 충돌체와 20개 스폰 위치, 술래 인원 배정, 숨기 중 이동·공격 제한, 수색 전환, 시야각과 장애물을 고려한 탐지, 미발견 점수, 먹물의 충전·범위·1회 제한, 색칠 경계와 PNG 전달, 잘못된 그림 데이터 거부, SOFTNESS 0과 10의 차이, 표면 색 복사, 전원 포획·산소 소진 승리, 대기실 복귀 초기화가 포함된다. 일부 규칙은 Play Mode에서만 만든 독립 물리 배치로 검사하므로, 전체 맵의 모든 지형 조합을 사람이 플레이해 본 것과는 범위가 다르다.

LAN 검사는 실제 Windows 게임 프로세스 두 개가 서로 연결하여 수행했다. 호스트와 참가자가 내보낸 그림의 SHA-256 해시는 둘 다 `65E111DD6A2B8471152FFBFF49291C0606375DBAD607348097F711E18E66659D`였으며, SOFTNESS 9 전달과 순번을 붙인 원격 이동 입력 처리도 확인했다. 이 결과는 Steam 친구 초대·로비·중계 서버를 거치는 연결의 검증 결과가 아니다.

- Unity 메뉴 **Octopusdream → Run Prototype Verification**으로 구성된 자동 검증을 실행할 수 있다. 실행 결과는 `Logs/Prototype/verification.json`에 기록된다.
- Windows 빌드 결과는 `Logs/Prototype/build.json`에서 확인한다.
- Editor 오류는 **Window → General → Console**에서 확인한다.
- 빌드에서 문제가 생기면 두 PC의 사용 빌드, 접속 방식, 첫 오류와 발생 순서를 함께 기록한다. 한쪽 화면만 보고 다른 참가자의 상태까지 정상이라고 판단하지 않는다.

이 문서는 개발 중인 프로토타입을 재현 가능하게 확인하기 위한 안내다. 아직 실행하지 않은 확인 항목은 통과한 것으로 취급하지 않는다.

