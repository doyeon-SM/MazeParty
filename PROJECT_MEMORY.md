# MazeParty 임시 기획 메모

이 문서는 현재 구현 기준, 확정된 작업 계약, 미정 결정과 다음 작업만 보관한다.
완료 이력은 Git, 확정 기획 원문은 Notion, 장기 계약은 코드와 EditMode 테스트를
원본으로 삼는다. 사용자의 최신 명시적 지시를 항상 우선한다.

## 현재 작업 기준

- 저장소·Unity 프로젝트: `C:/Unity/MazeParty`
- 작업 브랜치: `dev/minigame`
- 비차단 기존 경고는 `Assets/Ignore` 무료 캐릭터 에디터 스크립트의 `CS0414` 2개다.
- `Assets/Ignore`의 에디터 전용 `MaterialImporter.cs`는 Player 빌드를 위해 파일 전체를
  `#if UNITY_EDITOR`로 감싼다. Ignore 에셋은 새 환경에도 같은 GUID로 설치해야 한다.

### 에셋·프리팹 운영 원칙

- 사용자가 직접 조정한 씬 배치, UI 앵커와 디자인을 기준으로 삼는다. 런타임은 허용된
  authored prefab의 직렬화된 바인딩을 통해 값과 표시 상태만 바꾸며, setup 재실행은
  기존 디자인을 덮어쓰거나 대체 UI를 절차적으로 만들지 않는다.
- 외부 패키지와 생성 원본은 `Assets/Ignore` 아래에 보관한다. 추적되는 프로젝트 전용
  프리팹은 원본 경로와 GUID 의존을 유지하며 필요한 래퍼만 둔다.
- 외부 visual의 Collider는 제거한다. 게임 판정 Collider와 NetworkObject는 기존 권위
  오브젝트에만 두고, visual prefab이 판정이나 네트워크 권위를 소유하지 않게 한다.
- 미니게임 환경 원본은 `Assets/Ignore/Pandazole_Ultimate_Pack`,
  `Assets/Ignore/FreeLowpolyScifiObjects`, `Assets/Ignore/Fantasy Lowpoly Pack (Demo)`를
  사용한다. URP 파생 재질·메시는 `Assets/Ignore/MazePartyGenerated`에 둔다.
- 프로덕션 미니게임 15종과 보드는
  `Assets/Ignore/Fantasy Skybox FREE/Cubemaps/Classic/FS000_Night_01.mat`을 사용한다.
  미니게임 전용 카메라를 추가하지 않고 지속 Main Camera·Cinemachine 경로를 유지한다.

## 확정된 구현 계약

### 멀티플레이 경기 복귀

- 복귀 소유권 상실 또는 저장 실패 시 대기열을 해제해 재시도를 막지 않는다.
- 세션 단계 저장은 최대 3회, 준비·언로드는 명시적 deadline을 사용한다.
- fail-closed `LeaveAsync`는 일반 예외 최대 3회와 10초 상한을 사용한다.
- 시간 초과는 중복 재시도 없는 terminal 상태이며 recovery journal은 보존한다.
- 실제 로비 복귀가 성공한 뒤에만 활성 미니게임 일정을 완료 처리한다.

### UI·현지화

- 캐주얼 영문 디자인을 기준으로 보라색을 주색, 초록색·남색을 보조색으로 사용하며,
  둥근 채움 표면은 `Rounded Filled 1024px`로 통일한다.
- 글자 역할 기준은 제목 34, 주요 CTA 32, 정보 22, 일반 20, 설명 16 Bold다.
- 영어 로고는 `MazeParty`, 한국어 로고는 `미로파티`다. 일본어·중국어는 전용 로고가
  생기기 전까지 영어 로고를 사용한다.
- 영어·한국어는 KCC, 일본어는 Noto JP, 중국어 간체는 Noto SC를 사용하고 플레이어
  이름에는 상호 fallback을 적용한다.
- 옷장은 색 → 얼굴 → 모자 순서다. Face1~15와 없음·Hat1~30을 사용하며 기존
  Face1~3·Hat1~3 저장 ID를 유지한다. 선택값은 저장·네트워크·모든 캐릭터 표시에 반영한다.
- 플레이어 이름은 월드 머리 위에 표시하고 최초 카운트다운에는 로컬 위치를 강조한다.

### 로비 프레젠테이션과 맵 선택

- 대기방 3D 공간은 `Assets/Ignore/Maze` 원본을 사용하는 하나의 철창·감옥이며,
  `LobbyArena.prefab`과 허용된 `Multiplayer/UI` 프리팹에서 제작한다.
- 대기방 입장 전에는 글자 없는 16:9 임시 배경과 현재 언어 로고를 표시한다. 입장 성공
  즉시 둘을 숨기고 3D 대기방을 표시하며, 세션에서 나왔을 때 현재 언어 로고를 복원한다.
- `LobbyCanvas.prefab` 비표시와 `OnlineBootstrap` 인스턴스 활성 override는 사용자가 보존을
  지시한 현재 상태이며, 관련 EditMode 계약 2건은 알려진 비차단 예외다.
- 플레이어 장막은 대기방에서 표시와 판정을 모두 끄고 보드 단계부터 활성화한다. 남은
  보드 상태와 관계없이 세션 단계가 대기방이면 서버와 클라이언트 모두 즉시 제거한다.
- 준비 영역에는 현재 보드 맵 이름을 항상 표시한다. 호스트만 프로덕션
  `BoardMapCatalog.Maps` 순서로 맵을 순환 선택하고, 다른 플레이어는 동기화된 이름만 본다.
- 새 세션 기본은 `Forest Graybox`이며 `Maze Graybox`도 선택할 수 있다. 저장 경기에서는
  저장된 `MapId`와 `ContentVersion`을 우선한다.

### 플레이어 기본 손

- 좌우 기본 손은 `Assets/Ignore/SimpleHands/Prefabs/WhiteHand.prefab` 원본의
  `SimpleFistHand.prefab` 변형을 사용한다. 왼손은 visual root X축 미러로 구분한다.
- 기존 손 앵커, 주먹 애니메이션, 독립 SphereCollider 히트박스와 직렬화 바인딩을 유지하며
  SimpleHands 모델에는 Collider를 추가하지 않는다.
- 제스처 또는 손에 든 아이템이 활성화되면 기본 주먹을 숨긴다.

### Forest Graybox

- `Environment/Generated Ground`에는 TerrainLayer 기반 잔디와 보드 연결을 따르는 흙길만
  자동 생성한다. 생성 외 `Environment` 자식은 보존하고 시각 지형에는 Collider를 두지 않는다.
- 42칸·49개 일방통행 연결을 사용한다. 외곽은 `0→1→…→22→0`, 내부는
  `4→23→…→28→12`, `8→29→…→31→26`, `26→32→…→34→17`,
  `20→35→…→38→24`, `24→39→40→41→7`이며 `1→3`, `12→14` 지름길을 사용한다.
- 시작 칸은 `03`, 리스폰은 `02`, `13`, 슬롯별 시작은 `03`, `09`, `14`, `19`다.
  콘텐츠 버전은 4이며 수동 꼭짓점·회전은 재생성 청사진에도 동기화한다.
- 칸 연결 기즈모는 실제 `Source→Destination` 방향 화살표로 표시한다.

### Maze Graybox

- mapId는 `maze-graybox`, 표시 이름은 `Maze Graybox`(ko `미로 그레이박스`), 콘텐츠
  버전은 1이다. 정의·루트 프리팹·Terrain은 미로 전용 에셋만 참조한다.
- 사용자가 배치한 8×8 격자 64칸·80개 연결을 유지한다. 좌표는 월드 X 순서를 열,
  Z 순서를 행으로 하는 `(열, 행)` 0~7이며 Footprint는 8×8m 정사각형이다.
- 시작은 `(0,0)`, 리스폰은 `(2,2) (2,5) (5,2) (5,5)`, 슬롯별 시작은
  `(0,0) (7,0) (7,7) (0,7)`이다.
- 미니맵·전체 맵과 Map Authoring은 방 100개를 지원한다. 레거시 7×7 개요 패널은 자유
  배치 맵에서 표시하지 않는다.
- `Maze Ground`는 160×153m이고 전체 잔디 레이어다. 숲 전용 갱신 도구는 미로에 적용하지 않는다.

### 보드 아이템 시각 자산

- 모델 원본은 `Assets/Ignore/nappin/WeaponStylizedPack`에서 Pistol→Revolver,
  Sniper→HuntingRifle, Mine→Dynamite, Grenade→Granade로 매핑한다.
- 기존 아이템 프리팹 GUID를 유지하고 Collider를 제거한 추적 래퍼에 URP 재질로 표시한다.
  `HeldPrefab`이 있는 네 아이템만 장착·사용 중 손을 숨긴다.
- 9종 아이템 아이콘은 `Assets/Ignore/AIImage/Icons` 원본을 퀵슬롯·상점에서 공용한다.

### 보드 아이템 사용 UX·판정

- Pistol과 Sniper는 별도 조준 모드 없이 화면 조준선 기준 hitscan으로 피해를 적용한다.
  실제 탄환 오브젝트는 발사하지 않고 짧은 tracer 선은 유지하며, Sniper 우클릭 2배 확대는 제거한다.
- 총기 조준선은 차폐·사거리·생존·리스폰 보호를 모두 반영해 실제 피해 가능한 상대를
  조준할 때만 빨간색이다. 그 외에는 흰색이며 Cloak 상태를 색으로 드러내지 않는다.
- Mine 설치 위치는 설치한 플레이어에게만 보드와 지도에 표시한다.
- Grenade가 사용 선택된 동안 로컬 플레이어에게 최대 투척 사거리의 원형 범위를 표시하고
  사용 즉시 숨긴다. 수류탄은 포물선으로 이동하지만 궤적선·포물선 미리보기는 표시하지 않는다.

### VFX

- 원본은 `Assets/Ignore/AllIn1VfxToolkit` v2.32이며, 추적되는 프로젝트 전용 VFX는
  허용된 authored prefab에서 제작한다. 원본 GUID 의존은 유지하고 외부 helper script,
  Collider, NetworkObject와 Distort/GrabPass 의존은 제거한다.
- 서버가 의미 이벤트를 확정한 뒤 각 클라이언트가 로컬 VFX를 재생한다. 복수 이벤트는
  잃지 않는 전달 방식을 사용하고 재접속·재진입 때 과거 revision을 재생하지 않는다.
- 플레이어 장막은 authority Collider와 visual Transform을 분리한다. 자기 장막만 표시하며
  파랑은 통과 가능, 빨강은 통과 불가다. 숨길 때 Renderer와 ParticleSystem을 모두 끈다.

### 미니게임 HUD

- 게임은 항상 4인 구조이며 화면 HUD에는 현재 판단에 필요한 입력·신호·점수만 둔다.
- Tag Chase·Race·Bomb Passing·Snowy Spin·Cliff Barrage는 공통 HUD만, Arena Combat은
  공통 HUD와 피격 플래시만 사용한다. Minefield·Balloon Blow는 전용 화면 HUD가 없다.
- Wrong Way는 로컬 방향 아이콘, Red Light Green Light는 현재 신호만 표시한다.
- Gift Grab은 운반·스턴·기지 수량을 월드에, Stable Footing은 안전 문양만 표시한다.
- Sequence Memory는 NPC 순서와 로컬 입력·상태 한 줄을 표시한다. Territory Paint와
  Bouncing Balls는 이름 중복 없이 P1~P4 점수만 작게 표시한다.

### 오디오

- 직접 BGM은 로비·보드·공용 미니게임만 사용하고 나머지는 fallback을 사용한다.
- 보너스 준비음은 공개 전 2초만 재생하며 공개·일시정지에서 중지하고 duck하지 않는다.
- 보드 발소리는 짧은 원샷 11개를 shuffle 재생한다.

## 미정 결정

- 파일이 없는 중앙 큐 `item.bullet_impact`, `minigame.finish`와 미니게임 씬 직접 오디오
  슬롯 9개의 후속 사운드
- 보드 칸 종류의 최종 배치 비율
- 참조되지 않는 `Art/Board/Maze/TerrainData_*.asset` 10개 삭제 여부
- `ForestGrayboxMapRoot.prefab`의 연결 이름 재생성 형식을 의도된 변경으로 유지할지 여부

## 남은 검증·TODO

- VFX 자연 재생시간, 투명 정렬, Bloom·Soft Particle, `Reduce Flashes` 차이 시각 QA
- 15종 미니게임을 자연 제한시간까지 플레이하는 조작·조기 종료·시간 종료·동률 분기 검증
- pause 중 재접속, 호스트 복구 3개 체크포인트, 공동 순위 수상식
- 별도 PC, 고지연, IL2CPP 릴리즈 후보 검증
- 대기방에서 `Maze Graybox` 선택 후 4인 경기 시작·저장 경기 복구 실제 플레이 확인
- 미로 레이아웃 변경 뒤 흙길을 다시 칠할 미로용 바닥 갱신 수단 마련
- `SequenceMemory/Npc.prefab` 루트 `LocalizedFontScope` 추가 여부 확인
- 로비 `Warm Cell Light` 4개의 shadow atlas 초과 경고 처리 방안 결정
- 플레이 종료 후 `[Wire] FATAL The header part of a frame could not be read.` 재현 여부 확인
