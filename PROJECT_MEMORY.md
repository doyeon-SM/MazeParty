# MazeParty 임시 기획 메모

이 문서는 진행 중인 작업의 임시 기억만 보관한다. 확정 기획은 Notion, 구현 이력은 Git,
장기 계약은 코드와 EditMode 테스트를 원본으로 삼는다. 사용자 최신 지시가 항상 우선한다.

## 현재 작업 기준

- 저장소·Unity 프로젝트: `C:/Unity/MazeParty`
- 브랜치: `dev/UI`
- 작업 시작 기준 커밋: `9c1c0c496364214f7fb20e6e073590460156ef43`
  (`26.09.28`, 본문 `TODO 1~3 검증 기록`). 로컬 `dev/UI`와
  `origin/dev/UI`는 동기화되어 있다.
- 완료 범위: `LobbyCanvas`·`GameMenuCanvas` 루트 스케일과 `OnlineBootstrap`의
  잘못된 RectTransform override를 바로잡고, 완료 경기의 로비 복귀 실패 경로를
  유한 재시도·fail-closed 정책으로 보강했다.
- 최종 자동 검증: Unity 6000.6.0f1 컴파일 오류 0, 전체 EditMode **437/437 통과**,
  실패·스킵 0이다. Mono x64 Development 빌드도 성공했다.
- Ignore 경로 무료 캐릭터 에디터 스크립트의 기존 `CS0414` 경고 2개가 남아 있다.
- Solo 시각 스모크에서 Minefield·Balloon Blow·Wrong Way·Gift Grab·Stable Footing의
  축소 HUD와 월드 이름표를 확인했다. 같은 PC 4프로세스 Relay에서는
  입장→4/4 READY→Board→Wrong Way→Board 복귀와 연결 상태 유지를 확인했다.

## 완료 경기 복귀 실패 정책

- 복귀 소유권을 잃거나 저장이 실패한 뒤 `_completedMatchReturnQueued`를 해제해 다음
  복귀 시도가 막히지 않게 한다.
- 세션 단계 저장은 최대 3회 시도한 뒤 fail-closed로 종료한다.
- 미니게임 씬 언로드는 Started·SceneNotLoaded·SceneEventInProgress를 구분해
  대기·진행·재시도하고, 그 밖의 결과는 fail-closed로 처리한다. 준비와 언로드에는
  명시적 deadline을 적용한다.
- fail-closed `LeaveAsync`는 일반 예외 최대 3회, 10초 시간 상한을 사용한다.
  시간 초과는 동시 재시도를 만들지 않는 terminal 상태이며 recovery journal은 보존한다.
- 로비 복귀가 실제로 성공한 경우에만 활성 미니게임 일정을 완료 처리한다.

## 활성 UI 계약

- 영문 디자인을 기준으로 캐주얼하게 구성한다. 보라색을 메인, 초록색과 남색을
  서브색으로 사용한다.
- 일반 UI 에셋은 `Assets/Ignore/RetroCartoonUIPack`과
  `Assets/Ignore/ModernUIPack`만 사용한다. 생성 로고는 `Assets/Ignore/AIImage`에 둔다.
- 둥근 채움 표면은 `128px` 등 다른 해상도를 쓰지 않고 `Rounded Filled 1024px`로 통일한다.
- 글자 역할은 제목 34 Bold, 주요 CTA 32 Bold, 정보 22 Bold, 일반 20 Bold,
  설명 16 Bold를 기준으로 한다.
- 영어 선택 시 `MazeParty`, 한국어 선택 시 `미로파티` 로고를 표시한다.
  일본어·중국어는 전용 로고가 생기기 전까지 영문 로고를 사용한다.
- 플레이 중 Canvas 디자인은 프리팹 원본에서 관리하고 런타임은 직렬화된 바인딩의
  값과 표시 상태만 갱신한다. setup 재실행은 기존 디자인을 덮어쓰지 않는다.
- 보드와 모든 미니게임에서 플레이어 이름을 머리 위에 표시한다.
- 모든 미니게임은 최초 시작 카운트다운 동안 공통 하이라이트로 로컬 위치를 안내한다.
- Minefield와 Balloon Blow는 전용 화면 HUD를 사용하지 않는다.
- Wrong Way는 Modern UI의 로컬 방향 아이콘 하나만 표시하고 정답 입력 시 다음 아이콘으로
  교체한다. 개인 카메라는 로컬 플레이어의 계단 진행을 중앙 추적한다.
- Red Light Green Light는 현재 신호만 표시한다.
- Gift Grab은 운반·스턴·기지 선물 수만 월드에 남기고, Stable Footing은 안전 문양만 유지한다.

## 미정 결정과 바로 다음 작업

- 보드 칸 종류의 최종 배치 비율을 확정해야 한다.
- Ignore 경로의 UI 팩과 AI 로고를 배포 가능한 추적 경로로 옮길지 결정해야 한다.
- Sequence Memory의 상대 입력 문자열, Territory Paint의 4인 점수표,
  Bouncing Balls의 개별 플레이어 카드를 추가로 축소할지 결정해야 한다.
- 이번에 확인한 5종 외 나머지 10종 미니게임의 조작·조기 종료·시간 종료·동률·결과·씬
  정리 분기와 15종 전체 Relay 순환은 계속 검증해야 한다.
- MVP 실기: pause 중 재접속, 호스트 복구 3개 체크포인트, 공동 순위·수상식,
  15턴 전체 여정, 별도 PC·고지연·IL2CPP 릴리즈 후보를 검증한다.

## Notion 반영 대상

- 기록일: 2026-09-28 (Asia/Seoul)
- 기획서 v0.40: https://app.notion.com/p/3d3c227fda0380f6a34df09eff208ac0
- 누적 회의록: https://app.notion.com/p/3e8c227fda0381c59956eaecf273f797
- TODO 페이지와 같은 날짜 회의록에는 커밋 `9d07c762d625cafb9dcf60190f641bb621388c96`,
  UI 계약 수정, 완료 경기 복귀 실패 정책, 436/436, 5종 Solo 스모크와 실제
  4클라이언트 Board→Wrong Way→Board 결과를 최종 근거로 반영한다.

## 완료: 캐릭터 커스터마이징 확장

- `Assets/Ignore/FREE/Pack_FREE_PartyCharacters`의 원본을 복사하지 않고 직접 참조한다.
- 기존 표정 4종은 팩의 `Resources/Materials/Face Images/face 1~3.png` 3종으로 교체하고,
  데이터 에셋에서 `Face1`·`Face2`·`Face3`으로 관리한다.
- 모자는 `없음`을 기본값(식별자 0)으로 포함하고, 팩의 chef hat·orange fedora·party hat을
  각각 `Hat1`·`Hat2`·`Hat3`으로 관리한다.
- 로비에서는 표정과 동일하게 좌우 버튼으로 모자를 순환 선택하며, 선택값은 프로필 저장과
  네트워크 상태를 거쳐 로비·보드·미니게임·수상식의 캐릭터 표시 모두에 반영한다.
- `Assets/Ignore` 직접 참조는 해당 무료 팩이 없는 다른 체크아웃에서 참조가 끊기는
  의도된 제약이다. 이번 작업에서는 에셋 복사나 추적 경로 이전을 하지 않는다.
- `LobbyCanvas` 프리팹에 표정·모자 선택 바인딩을 두고 레거시 Test Hat 토글과
  아바타의 임시 모자 메시를 제거했다. 모자 프리팹은 런타임에 작성된 `HatAnchor` 아래에만
  생성한다.
- Face1~3과 Hat1~3의 실제 프리뷰, 로비 UI의 좌우 선택·레이아웃을 Play Mode에서 확인했고,
  관련 집중 계약 테스트 19/19와 전체 EditMode 437/437가 실패·스킵 없이 통과했다.
