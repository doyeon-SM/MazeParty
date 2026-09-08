# MazeParty 온라인 멀티플레이 부트스트랩

## 현재 기준

- PC 온라인 고정 4인, 실행 프로세스당 로컬 플레이어 1명
- 호스트 1명 + 원격 클라이언트 3명
- 비공개 방 생성 후 초대 코드로 참가
- Unity Multiplayer Services Session + Relay(DTLS) + Netcode for GameObjects
- 정확히 4명이 모두 준비해야 호스트가 게임 시작 가능
- MVP에서는 호스트가 나가면 세션과 경기를 종료하며 호스트 이전은 하지 않음
- 로비의 비호스트 강제 종료는 재접속 유예 없이 호스트가 연결 해제를 감지해 좌석을 조기 해제
- 게임 중 비호스트 강제 종료는 서비스 재접속 유예를 유지하며, 정상 Leave는 전원 세션 종료
- 로비 UI는 Canvas/uGUI 기반이며 한글 폰트 추가 전까지 화면 문구는 영어 사용

## 실행 확인

1. Build Settings의 0번 씬인 `OnlineBootstrap`을 연다.
2. 호스트에서 비공개 방을 만들고 초대 코드를 복사한다.
3. Multiplayer Play Mode 또는 별도 빌드 3개에서 같은 코드를 입력한다.
   같은 PC에서 빌드를 여러 개 실행할 때는 각 프로세스에 서로 다른
   인증 프로필을 준다. 예: `MazeParty.exe -auth-profile player2`,
   `MazeParty.exe -auth-profile player3`, `MazeParty.exe -auth-profile player4`.
4. 네 명 모두 준비한 뒤 호스트가 `Start 4-Player Game`을 누른다.
5. Board에서 턴 탑뷰와 하강 카메라 전환이 끝나면 각 플레이어의 3분
   행동 시간이 동시에 시작된다.
6. 30초 안에 아이템 또는 `USE NO ITEM`을 고른다. 선택하지 않으면
   자동으로 사용 안 함이 적용된다. 행동 시작 후 최초 5초는 방어막 시간이다.
7. 선택이 끝나면 주사위를 굴리기 전에도 WASD로 현재 칸 안을 이동할 수 있다.
   자신의 앞에 놓인 월드 주사위는 좌클릭 타격의 서버 물리 임펄스로 밀 수 있다.
   주사위를 조준한 상태에서 우클릭하면 서버 판정 1~10 굴림을 시작한다.
   다른 플레이어의 주사위는 밀거나 굴릴 수 없다.
8. 굴림 결과는 주사위 오브젝트 위에 표시되어 모든 플레이어가 월드에서 직접
   확인한다. 결과 확인 시간 2초가 지나면 월드 주사위는 사라진다. 열린 방향의
   파란 경계만 통과해 다음 칸으로 이동하며, 검은 경계는
   물리적으로 이동을 막는다. 이동 횟수가 0이 된 뒤에도 마지막 칸 안에서는
   WASD 이동과 아이템 사용을 계속할 수 있다.
9. 전원이 목적 칸에 도착하거나 3분이 끝나면 상승 카메라 전환과 정리 시간을
   거친다. 현재 미니게임은 개발용 준비/스킵 단계로 연결되어 다음 턴을 확인한다.

게임 플레이 좌석은 NGO 서버가 연결된 플레이어 오브젝트에 0~3번을
직접 배정한다. 클라이언트가 좌석 값을 제출하거나 선점하지 않는다.
로비의 MPS 좌석은 준비/정원 검증용 메타데이터이므로 화면에는 번호로
표시하지 않는다.
주사위 값과 월드 물리는 서버에서 판정한다. 정확한 결과는 소유자 HUD와 월드
주사위 오브젝트에 2초 동안 표시되며, 다른 참가자의 HUD에는 숫자를 노출하지 않는다.
따라서 다른 참가자는 실제 보드 위 주사위를 육안으로 보고 결과를 확인한다.
각 플레이어는 재사용되는 경계 오브젝트 4개를 가지므로 4인 기준 총 16개만
생성하며, 턴 동안 파괴하거나 새로 만들지 않는다. 열쇠 상점은 정적 타일이 아니라
2턴 탑뷰 시작 시 플레이어가 없는 일반 칸 하나를 서버가 선택해 등장시킨다.

실제 인터넷 연결 전 Unity Dashboard의 연결된 프로젝트와 `production`
환경에서 Authentication(Anonymous) 및 Multiplayer/Relay 사용 가능 상태를 확인한다.

## 생명주기 주의

MPS의 `CreateSessionAsync(...WithRelayNetwork())`와
`JoinSessionByCodeAsync`가 NGO의 Host/Client 시작을 자동 처리한다.
세션 이용 중 게임 코드에서 `StartHost`, `StartClient`, `Shutdown`을
직접 호출하지 않는다. 정상 종료는 호스트 `DeleteAsync`, 클라이언트
`LeaveAsync`를 사용한다. 종료와 호스트 속성 저장은 직렬화하며,
일시적인 서비스 오류에는 제한된 지수 백오프 재시도를 적용한다.
로비에서 비호스트 NGO 연결이 끊기면 호스트가 최신 MPS 플레이어 정보를
다시 조회한 뒤 해당 멤버를 제거한다. 정상 Leave와 강제 종료가 경합하지
않도록 정상 이탈 표식을 확인하고, 표식이 남은 채 프로세스가 종료된 경우에는
짧은 추가 유예 후 한 번 더 정리한다. 게임 중 NGO 연결 종료만으로는 즉시
멤버를 제거하지 않아 서비스의 재접속 유예를 보존한다. 호스트가 바뀐 경우
새 호스트만 세션을 삭제하며 나머지 클라이언트는 삭제 이벤트를 기다린다.
Board 씬 로드 타임아웃은 고정 4인 경기를 계속하지 않고 종료 경로로 정리한다.
게임 중 비호스트 연결이 끊기면 행동 입력과 흐름 타이머를 포함한 경기를 최대
60초 동안 전역 정지한다. 동일 세션 좌석의 플레이어가 돌아오면 서버에 보관된
위치·이동·아이템·턴 상태를 복원하고 현재 카메라 상태로 즉시 스냅해 재개한다.
60초 안에 복귀하지 않거나 호스트가 종료하면 고정 4인 경기를 종료한다.
Unity Dashboard의 Lobby/MPS `Disconnect Removal Time`은 UTP 연결 해제 감지
(현재 최대 약 15초) 뒤에도 게임의 60초 복귀 유예가 온전히 남도록 75~90초로
설정한다. 이 값이 60초 이하이면 백엔드가 먼저 멤버를 제거해 자동 재접속이
실패할 수 있다. 비호스트 빌드는 게임 진입 시 인증 프로필별로 현재 Session ID를
로컬 재접속 티켓에 저장하며, 비정상 프로세스 종료 뒤 같은 `-auth-profile`로
다시 실행하면 해당 ID의 기존 멤버십을 확인한 후에만 자동 재접속한다. 로비,
정상 Leave, 정상 앱 종료 및 경기 종료 경로에서는 티켓을 제거한다.

## Steam 확장 경계

- 인증 교체 지점: `IPlayerIdentityProvider`
- 로비/세션 교체 지점: `IOnlineSessionProvider`
- UI와 게임은 Unity Services 타입 대신 `SessionSnapshot`만 사용
- Steamworks Web API 티켓 인증은 `PlayerIdentity.cs`의
  `TODO(STEAM-AUTH)` 참고
- Steam Lobby/NetworkingSockets 전환은
  `IOnlineSessionProvider.cs`의 `TODO(STEAM-SESSION)` 참고
- 전송 계층 교체 시 `NetworkManager`의 `NetworkTransport`만
  Steam용 NGO 어댑터로 교체할 수 있도록 상위 로직을 분리함

## 테스트 범위

EditMode 테스트는 좌석 할당, 시작 조건, 보드 흐름 상태 머신, 32칸 토폴로지,
빌드 씬, NetworkManager, UnityTransport, 플레이어 프리팹 및 Board 인씬
NetworkObject 해시를 검증한다. 에디터 전용 로컬 흐름 검증은 빌드 목록에서
제외된 `Assets/MazeParty/Dev/BoardFlowTestbed/BoardFlowTestbed.unity`를 사용한다.
실제 Relay 왕복과 4개 프로세스 동시 접속은 UGS 계정/환경이 필요하므로 로컬
자동 테스트와 분리한다.
