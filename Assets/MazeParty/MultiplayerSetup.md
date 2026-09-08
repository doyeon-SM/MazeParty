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
5. Board에서 WASD로 이동하고 우측 패널에서 1~10 주사위를 굴린다.

게임 플레이 좌석은 NGO 서버가 연결된 플레이어 오브젝트에 0~3번을
직접 배정한다. 클라이언트가 좌석 값을 제출하거나 선점하지 않는다.
로비의 MPS 좌석은 준비/정원 검증용 메타데이터이므로 화면에는 번호로
표시하지 않는다.
주사위 값도 서버에서 생성하며, 정확한 결과는 해당 플레이어 소유자에게만
복제되고 다른 참가자에게는 굴림 완료 여부만 보인다.

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

EditMode 테스트는 좌석 할당, 시작 조건, 빌드 씬, NetworkManager,
UnityTransport, 플레이어 프리팹 및 Board 인씬 NetworkObject 해시를
검증한다. 실제 Relay 왕복과 4개 프로세스 동시 접속은 UGS 계정/환경이
필요하므로 로컬 자동 테스트와 분리한다.
