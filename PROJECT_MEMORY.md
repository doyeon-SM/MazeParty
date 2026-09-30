# MazeParty 임시 기획 메모

이 문서는 현재 구현 기준, 확정된 작업 계약, 미정 결정과 다음 작업만 보관한다.
완료 이력은 Git, 확정 기획 원문은 Notion, 장기 계약은 코드와 EditMode 테스트를
원본으로 삼는다. 사용자의 최신 명시적 지시를 항상 우선한다.

## 현재 작업 기준

- 저장소·Unity 프로젝트: `C:/Unity/MazeParty`
- 작업 브랜치: `dev/UI`
- 최근 자동 검증: Unity 6000.6.0f1 컴파일 오류 0, VFX 집중 EditMode 34/34 및
  전체 EditMode 397/397 통과(실패·스킵 0). `dotnet build`는 오류 0, 기존 경고
  115개이며 2026-09-30 Windows Development(Mono x64) 빌드는
  `Builds/Windows-Development-20260930-180455/MazeParty.exe`로 성공했다.
- 표준 `Builds/Windows-Development`는 이전 Player 프로세스가 `DSTORAGE.dll`을
  잡은 채 Windows 종료 상태에 남아 덮어쓸 수 없다. 빌드 유틸리티는 이 경우
  시간표시가 붙은 형제 폴더로 자동 대체하며 실제 성공 경로를 로그에 남긴다.
- 비차단 기존 경고: Ignore 경로 무료 캐릭터 에디터 스크립트의 `CS0414` 2개
- 로컬 Ignore 캐릭터 팩의 `Resources/Scripts/MaterialImporter.cs`는 에디터 전용인데
  일반 런타임 어셈블리에 포함되어 Player 빌드를 막으므로, 파일 전체를
  `#if UNITY_EDITOR`로 감싼다. `Assets/Ignore/*`는 Git 비추적이어서 새 환경에 팩을
  설치할 때 같은 보정이 필요하다.
- 명령줄 표시 인수(`-screen-width`, `-screen-height`, `-screen-fullscreen`,
  `-window-mode`, `-monitor`, `-popupwindow`, `-parentHWND`, 대소문자·`flag=value`
  포함)가 있으면 시작 설정이 해상도·화면 모드를 덮어쓰지 않는다. 품질과 FPS 설정은
  그대로 적용한다. 저해상도 창 4개 D3D11 실행 중 새 재부팅·bugcheck는 없었다.
- Unity 6000.6 Windows Player는 여러 인스턴스가 동시에 `Application.Quit` 또는
  `Environment.Exit`을 호출하면 native shutdown에서 수 분 머물 수 있다. Development
  E2E는 결과 파일을 동기 저장한 뒤 Windows `TerminateProcess`로 종료하며 성공/실패
  exit code를 보존한다. 종료 스모크는 실패 마커 기록 1ms 뒤 code 2 종료를 확인했다.

### 2026-09-30 4인 Development 빌드 플레이테스트

- 실제 독립 Player 4개(호스트 1, 클라이언트 3)가 Relay·NGO 세션에 접속해 서로 다른
  표정과 모자를 유지했고, `forest-graybox` 콘텐츠 버전 4로 15턴을 진행했다.
- 권총의 실제 선택·사용·hitscan·RPC 경로로 대상 체력이
  `100→80→60→40→20→0`으로 감소했고, 사망·묘비·리스폰 칸 `(13, 0)` 복귀·체력
  100·보호 상태를 확인했다. 같은 칸 전투에서는 실제 펀치 RPC로 체력 `100→95`를
  확인했다.
- 미니게임은 `SequenceMemory`, `WrongWay`, `SnowySpin`, `BalloonBlow`, `TagChase`,
  `Minefield`, `CliffBarrage`, `GiftGrab`, `BouncingBalls`, `StableFooting`,
  `TerritoryPaint`, `Race`, `RedLightGreenLight`, `ArenaCombat`, `BombPassing` 순으로
  중복·누락 없이 한 번씩 로드했다. 네 Player 모두 실제 준비 RPC와 게임별 라우팅 입력
  smoke를 통과했고, 프로덕션 결과·보상·승리 정산 경로를 확인했다.
- 시상식은 아이템 사용·총 골드 획득 수상과 최종 순위를 확인했다. 네 Player 모두 복귀를
  제출했고, 보드·미니게임 씬 언로드 뒤 4인 세션을 유지한 채 Lobby 단계와 준비 해제를
  확인했다.
- 자동화 시간 안에 전 여정을 검증하기 위해 보드 이동, 첫 실제 전투 타격 뒤 남은 전투
  시간, 미니게임 결과 확정은 Development 전용 훅으로 가속했다. 따라서 15종의 실제
  씬·어댑터·네트워크 입력·정산은 검증했지만, 각 게임을 자연 제한시간까지 플레이한
  조작 세부·자연 종료 검증으로 보지는 않는다.
- 기능 근거 로그: `C:/Unity/MazeParty/Logs/FullMatchE2E-20260930-174245`. 네 Player의
  pass·Lobby 복귀 마커가 모두 존재하고 JSONL에 실패 이벤트가 없다. 이 실행 뒤 발견한
  native shutdown 지연은 최종 빌드의 종료 경로로 보정했으며 근거는
  `C:/Unity/MazeParty/Logs/ExitSmoke-20260930-180530`이다.

## 확정된 구현 계약

### 완료 경기 복귀

- 복귀 소유권 상실 또는 저장 실패 시 대기열을 해제해 재시도를 막지 않는다.
- 세션 단계 저장은 최대 3회, 준비·언로드는 명시적 deadline을 사용한다.
- fail-closed `LeaveAsync`는 일반 예외 최대 3회와 10초 상한을 사용한다.
- 시간 초과는 중복 재시도 없는 terminal 상태이며 recovery journal은 보존한다.
- 실제 로비 복귀가 성공한 뒤에만 활성 미니게임 일정을 완료 처리한다.

### UI·현지화

- 캐주얼 영문 디자인을 기준으로 보라색을 주색, 초록색·남색을 보조색으로 사용한다.
- 둥근 채움 표면은 `Rounded Filled 1024px`로 통일한다.
- 글자 역할 기준은 제목 34, 주요 CTA 32, 정보 22, 일반 20, 설명 16 Bold다.
- 영어 로고는 `MazeParty`, 한국어 로고는 `미로파티`이며 일본어·중국어는
  전용 로고가 생기기 전까지 영어 로고를 사용한다.
- 영어·한국어는 KCC, 일본어는 Noto JP, 중국어 간체는 Noto SC를 사용하고
  플레이어 이름에는 상호 fallback을 적용한다.
- 옷장은 색 → 얼굴 → 모자 순서다. 얼굴은 Face1~15, 모자는 없음·Hat1~30이며
  모두 `Assets/Ignore/Pack_PartyCharacters` 원본을 사용한다. 기존 Face1~3·Hat1~3의
  저장 ID는 유지하고 나머지를 뒤에 추가한다. 선택값은 저장·네트워크 상태·모든 캐릭터
  표시에 반영한다.
- 플레이 중 Canvas는 허용된 프리팹을 원본으로 삼는다. 런타임은 직렬화된 바인딩의
  값·표시 상태만 바꾸고 setup은 기존 프리팹 디자인을 덮어쓰지 않는다.
- 플레이어 이름은 월드 머리 위에 표시하며 최초 카운트다운에는 로컬 위치를 강조한다.

### 대기방 맵 선택

- 대기방 준비 영역에는 현재 선택된 보드 맵 이름을 항상 표시한다.
- 호스트에게만 좌우 맵 선택 버튼을 표시하고 변경 권한을 부여한다. 호스트가 아닌
  플레이어는 같은 영역에서 동기화된 현재 맵 이름만 확인한다.
- 선택 순서는 프로덕션 `BoardMapCatalog.Maps` 순서를 따르며, 선택한 맵의
  `MapId`와 `ContentVersion`을 세션 및 새 경기 시작에 사용한다.
- 현재 프로덕션 카탈로그에는 `Forest Graybox` 한 개만 등록되어 있으므로 선택 버튼은
  비활성화하되, 이후 맵이 추가되면 같은 UI에서 순환 선택한다.
- 저장 경기를 이어갈 때는 저장된 맵 선택을 우선하며 호스트가 다른 맵으로 바꾸지 않는다.

### 숲 맵 바닥과 흙길

- `Forest Graybox`의 `Environment/Generated Ground`에는 잔디 바닥과 보드 연결을
  따라가는 흙길만 자동 생성한다.
- 표면은 `Assets/Ignore/Polytope Studio/Lowpoly_Demos/Environment_Free/Helpers`의
  잔디 및 흙 TerrainLayer를 URP Terrain에 직접 사용하며, 원본 서드파티 에셋은 Git
  추적 경로로 복사하지 않는다.
- 생성 영역 이외의 `Environment` 자식은 맵 갱신 시 보존하여 숲 오브젝트를 수동으로
  배치할 수 있게 한다. 생성 바닥은 시각 전용이며 게임플레이 Collider를 추가하지 않는다.
- `Forest Graybox`는 수동 편집을 반영한 42칸·49개 일방통행 연결을 사용한다.
  외곽은 `0→1→…→22→0`의 XZ 기준 반시계 방향이며, 내부 경로는
  `4→23→…→28→12`, `8→29→…→31→26`, `26→32→…→34→17`,
  `20→35→…→38→24`, `24→39→40→41→7` 순서로 외곽에 합류한다. 리스폰
  칸을 건너갈 수 있는 `1→3`, `12→14` 지름길도 사용한다.
- 시작 칸은 `03` 한 칸, 리스폰 칸은 사용자가 의도적으로 배치한 `02`, `13` 두 칸이다.
  플레이어 시작 위치는 슬롯 순서대로 `03`, `09`, `14`, `19`를 사용한다. 수동
  꼭짓점과 회전은 재생성 청사진에도 동기화하며 맵 콘텐츠 버전은 4를 사용한다.
- 칸 연결의 에디터 기즈모는 마젠타 경계선·광선 조합 대신 실제 `Source→Destination`
  방향을 가리키는 화살표로 표시한다.
- 보드 씬은 `Assets/Ignore/Fantasy Skybox FREE/Cubemaps/Classic/FS000_Night_01.mat`
  스카이박스를 사용하며 Main Camera는 Skybox clear flags, 환경광은 Skybox 모드로 둔다.
  다른 개발·빌드 환경에도 같은 Ignore 에셋과 GUID가 필요하다.

### 아이템 시각 자산

- 아이템 모델은 `Assets/Ignore/nappin/WeaponStylizedPack`의 Pistol→Revolver,
  Sniper→HuntingRifle, Mine→Dynamite, Grenade→Granade 매핑을 사용한다.
- 기존 아이템 프리팹 GUID는 유지하고, 충돌체를 제거한 추적 래퍼에 URP 재질로
  표현한다.
- 장착·사용 중 `HeldPrefab`이 있는 Pistol·Sniper·Mine·Grenade만 기존
  `PlayerAvatarVisual`이 손을 숨기고 모델을 표시한다.
- DoubleDice는 두 D12가 실제로 생성되므로 별도 전용 모델을 요구하지 않는다.
  LowDice·HighDice는 기존 D12 모델을 활용하며, PositionSwapper·Cloak도 추가
  3D 모델링을 요구하지 않는다.
- 9종 아이템 아이콘은 Git 추적 경로로 복사하지 않고
  `Assets/Ignore/AIImage/Icons` 원본을 퀵슬롯·상점에서 공용한다.

### VFX 제작 기준

- VFX는 `Assets/Ignore/AllIn1VfxToolkit` v2.32를 사용하고 원본 에셋은 수정하거나
  Git에 포함하지 않는다. 추적되는 `Assets/MazeParty/Prefabs/.../VFX` 프로젝트 전용
  프리팹은 원본 계층을 완전히 Unpack하고 정리하되, 툴킷 재질·메시·텍스처·셰이더
  GUID 의존은 유지한다. 다른 개발·빌드 환경에는 동일 GUID의 툴킷 설치가 필요하다.
- 전체 연출은 유혈 표현이 없는 캐주얼·카툰풍으로 통일한다. 폭발은 `Toon Explosion`
  계열, 작은 피격은 반짝임과 짧은 흔들림을 중심으로 하며 총구 화염과 탄도 VFX는
  제작 범위에서 제외한다.
- 플레이어 장막은 불꽃 윗선이 캐릭터 허리 높이인 1m에서 끝나게 한다. 파랑은 통과
  가능, 빨강은 통과 불가이며 각 플레이어에게 자기 장막만 표시한다. 색상 외에도 통과
  가능 장막은 옅고 부드럽게, 통과 불가 장막은 조밀하고 강하게 구분한다.
- 장막 Collider와 VFX Transform을 분리하고, 숨김 상태에서는 Renderer뿐 아니라
  ParticleSystem 방출·재생도 중지한다. 시각 효과가 게임 판정 Collider나 NetworkObject를
  소유하지 않도록 한다.
- 장막 불꽃은 런타임 billboard 외곽을 1m 이하로 제한한다. 통과 가능 방출 밀도는 0.45,
  통과 불가는 2.0이며 `Reduce Flashes`는 지속 방출과 순간 burst를 함께 줄인다.
- 공용 `CartoonExplosion`, `HitSpark`, `TaggerAura`는 툴킷 helper script, Collider,
  NetworkObject, Distort/GrabPass 의존을 제거한 표시 전용 프리팹이다. 원샷 풀은 프리팹별
  최대 24개, 유휴 보관 8개로 제한하고 씬 전환 정리, 외부 비활성 반환, 입자 자연 종료와
  안전 timeout을 모두 처리한다.
- 수류탄·지뢰와 Bomb Passing 폭발은 서버가 확정한 의미 이벤트 뒤 각 클라이언트에서
  로컬 VFX로 재생한다. 일회성 효과는 풀링하고, 재접속·늦은 표시에서 과거 이벤트를
  재생하지 않도록 최초 revision을 기준값으로 사용한다.
- 보드 아이템은 Grenade·Mine만 폭발, Pistol·Sniper만 가벼운 피격 반짝임을 재생한다.
  빗나간 공격에는 피격 SFX/VFX가 없고 신규 총구 화염·탄도는 추가하지 않는다.
- 미니게임 15종 모두 핵심 가독성 VFX를 설치했다. Bouncing Balls·Cliff Barrage·
  Gift Grab·Stable Footing·Minefield는 같은 프레임의 복수 이벤트를 잃지 않는 reliable
  RPC를 사용하고, Sequence Memory는 권위 `actorSlot`, Race는 25걸음마다 및 결승선,
  Arena 계열은 재진입 시 오래된 revision을 재생하지 않는 기준값을 사용한다.
- 기존 `Reduce Flashes` 설정은 폭발·불꽃·신호의 발광, 순간 방출과 Light 강도에도
  적용한다. 정확한 색·입자 수 같은 시각 세부값은 장기 EditMode 계약으로 고정하지 않고
  4인 빌드 플레이테스트와 화면 검수로 확인한다.

### 미니게임 HUD

- 게임은 항상 4인 구조를 유지한다.
- 화면 HUD에는 현재 판단에 필요한 입력·신호·점수만 남긴다.
- 15종 HUD 전수 감사를 완료했다. Tag Chase·Race·Bomb Passing·Snowy Spin·
  Cliff Barrage는 공통 HUD만, Arena Combat은 공통 HUD와 피격 플래시만 유지한다.
- Minefield와 Balloon Blow는 전용 화면 HUD를 쓰지 않는다.
- Wrong Way는 로컬 방향 아이콘 하나, Red Light Green Light는 현재 신호만 표시한다.
- Gift Grab은 운반·스턴·기지 수량을 월드에 표시하고 Stable Footing은 안전 문양만 남긴다.
- Sequence Memory는 NPC 순서와 로컬 플레이어 입력·상태 한 줄만 화면에 표시한다.
- Territory Paint와 Bouncing Balls는 이름 중복 없이 P1~P4 점수만 작게 표시한다.
- 실제 씬에서 쓰지 않는 Minefield·Balloon Blow·Gift Grab 전용 HUD와 Balloon Blow
  스테이션 라벨, Tag Chase·Race·Cliff Barrage·Stable Footing 복구 HUD 및 재생성용
  일회성 setup을 제거했다. 퇴역 자산의 복구 기준은 별도 복사본이 아닌 Git 이력이다.

### 오디오

- 직접 BGM은 로비·보드·공용 미니게임만 사용하고 나머지는 fallback을 사용한다.
- 보너스 준비음은 공개 전 2초만 재생하며 공개·일시정지에서 중지하고 duck하지 않는다.
- 보드 발소리는 짧은 원샷 11개를 shuffle 재생한다.

## 미정 결정

- 파일이 없는 중앙 큐 `item.bullet_impact`, `minigame.finish`와
  미니게임 씬 직접 오디오 슬롯 9개의 후속 사운드
- 보드 칸 종류의 최종 배치 비율
- Ignore 경로 UI 팩·AI 이미지(로고·아이콘)·폰트·캐릭터를 배포 가능한 추적
  경로로 옮길지 여부
- Ignore 경로의 WeaponStylizedPack을 추적 배포할지 여부. 현재 래퍼가
  이 팩의 메시·텍스처 GUID를 참조하므로 다른 개발·빌드 환경에도 동일 GUID 팩이 필요하다.

## 남은 검증·TODO

- 실제 화면에서 VFX 자연 재생시간, 투명 정렬, Bloom·Soft Particle, `Reduce Flashes`
  차이를 보는 시각 QA. 자동 E2E는 이벤트·프리팹·빌드 포함 여부를 검증하지만 픽셀 품질은
  판정하지 않는다.
- 15종 미니게임 각각을 자연 제한시간까지 플레이하는 조작 세부·조기 종료·시간 종료·
  동률 분기 검증
- pause 중 재접속, 호스트 복구 3개 체크포인트, 공동 순위 수상식
- 별도 PC, 고지연, IL2CPP 릴리즈 후보
