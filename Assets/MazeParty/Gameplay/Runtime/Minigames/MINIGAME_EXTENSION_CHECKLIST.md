# 미니게임 추가 체크리스트

새 미니게임을 추가할 때 가능한 변경 지점만 한 번에 정리한다.
목표는 `NetworkMatchState` 입력 라우팅, 카탈로그, 스케줄, Solo 테스트, 씬 계약이
동시에 정합되도록 하는 것이다.

## 1) 카탈로그/기본 식별자

- `ScheduledMinigameId` enum에 새 ID를 추가한다.
- `MinigameCatalog.RegisteredMinigameDefinitions`에 새 `MinigameDefinition`을 등록한다.
  - `displayName`, `roundCount`, `phaseDurationSeconds`, `sceneName`, `towerColor`
  - `RoundCount`는 런타임 규칙의 라운드 수와 일치해야 한다.
- `MinigameScheduleRules.RegisteredGameCount`와 기존 큐 시드 규칙이 자동으로 반영되는지
  확인한다. (특수한 경우가 아니면 별도 수정 불필요)

## 2) 미니게임 런타임 등록(필수)

- `Runtime/Networking/Minigames`에 `MinigameRuntimeAdapter<TState>`를 상속한
  미니게임별 어댑터 클래스를 추가한다.
  - `Id`, 현재 `NetworkBehaviour` 상태 조회
  - 시작/중지/복귀/종료 수명주기
  - 로컬 입력과 라우팅: 이동/푸시/메인액션/예외 입력(소나/방향 등)
  - 입력 게이트: `CanAcceptInputForSlot`
  - 필요한 게임만 라운드/입력 에폭 조회를 재정의한다.
- 새 어댑터 인스턴스를 `MinigameRuntimeRegistry`에 등록한다.
- `CurrentMinigame` 중심 쿼리 메서드(`IsMinefieldPhase` 등)를 운영상 필요하면
  추가하고 기존 UI 분기 없이 동작하도록 유지한다.
- `MinigameSoloTestCatalog` 및 런처 브랜치도 동일한 ID-씬 매핑으로 추가한다.

## 3) 씬/오브젝트 계약

- Production 씬을 만들고 `Assets/MazeParty/Scenes`에 배치한다.
- `Board`/로비 씬 이동 경로에서 additive load/unload 대상이 되는지 확인한다.
- 해당 미니게임 UI는 프리팹 기반 바인딩 계약으로 운영하고 `new GameObject` 조립은
  하지 않는다.
- NetworkObject/NetworkPrefab 정책 및 카메라/입력 전환 계약을 기존 방식으로 유지한다.

## 4) 데이터/결과 계약

- 미니게임 결과 점수(순위·보상)와 마무리 UI에 필요한 값이 공용 규칙(3/2/1/0 점수,
  골드 지급, 승수 반영)과 호환되는지 확인한다.
- 타이밍(카운트다운/플레이/결과 표시)은 기존 공통 상수를 우선 사용한다.
- 저장/재접속과 시드 판정(라운드 에폭 포함)이 기존 규약(권한 서버·결정론) 안에 있는지
  확인한다.

## 5) 테스트/검증

### 자동 테스트

- `MinigameRuntimeRegistration_AlignsWithCatalog` 통과:
  - `MinigameRuntimeRegistry.RegisteredIds`가 `MinigameCatalog`와 1:1인지.
- 기존 `MinigameScheduleTests`, `MultiplayerBootstrapTests` 시나리오가 깨지지 않는지 확인.
- 입력 게이트와 라운드/에폭을 사용하는 게임은 해당 기능의 서버 권한·재접속 계약
  테스트에서 미로드 상태의 안전한 폴백도 함께 검증한다.

### 수동 빠른 검증

- `Minigame Solo Tester`에서 새 미니게임 단독 실행 후 조작/타이머/결과 텍스트가 정상인지.
- `OnlineBootstrap`에서 4인 접속 기준 게임 시작 → 새 큐 슬롯 공개 → 미니게임 로드/언로드
  루프가 정상인지.
- 재접속(호스트/클라이언트) 시 새 미니게임 런타임이 `TryGetCurrentMinigameRoundAndInputEpoch`,
  `CanCurrentMinigameAcceptInputForSlot` 경로에서 `NetworkMatchState` 폴백으로 멈추지
  않고 복구되는지.
