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
- `BoardCanvas.prefab`은 사용자가 조정한 현재 배치를 유지한다. 삭제된 Players·Inventory
  제목과 인벤토리 슬롯 라벨은 런타임 바인딩에서도 제거하며, 각 플레이어 카드의 Key와
  Gold는 `Assets/Ignore/Icon_NCI`의 아이콘 뒤에 숫자 텍스트를 두는 구성으로 표시한다.

### 로비 프레젠테이션과 맵 선택

- 대기방 3D 공간은 `Assets/Ignore/Maze` 원본을 사용하는 하나의 철창·감옥이며,
  `LobbyArena.prefab`과 허용된 `Multiplayer/UI` 프리팹에서 제작한다.
- 대기방 입장 전에는 글자 없는 16:9 임시 배경과 현재 언어 로고를 표시한다. 입장 성공
  즉시 둘을 숨기고 3D 대기방을 표시하며, 세션에서 나왔을 때 현재 언어 로고를 복원한다.
- `LobbyCanvas.prefab` 비표시와 `OnlineBootstrap` 인스턴스 활성 override는 사용자가 보존을
  지시한 현재 상태이며, 관련 EditMode 계약 2건은 알려진 비차단 예외다.
- 플레이어 장막은 대기방에서 표시와 판정을 모두 끄고 보드 단계부터 활성화한다. 남은
  보드 상태와 관계없이 세션 단계가 대기방이면 서버와 클라이언트 모두 즉시 제거한다.
- 준비 영역에는 현재 보드 맵의 현지화된 이름을 항상 표시한다. 호스트만 프로덕션
  `BoardMapCatalog.Maps` 순서로 맵을 순환 선택하고, 다른 플레이어는 동기화된 이름만 본다.
- 맵 표시 이름은 영어 `Forest`·`Maze`, 한국어 `숲`·`미로`, 일본어 `森`·`迷路`,
  중국어 간체 `森林`·`迷宫`이다. 새 세션 기본은 `forest-graybox`이며 저장 경기에서는
  저장된 `MapId`와 `ContentVersion`을 우선한다.

### 플레이어 기본 손

- 좌우 기본 손은 `Assets/Ignore/SimpleHands/Prefabs/WhiteHand.prefab` 원본의
  `SimpleFistHand.prefab` 변형을 사용한다. 왼손은 visual root X축 미러로 구분한다.
- 기존 손 앵커, 주먹 애니메이션, 독립 SphereCollider 히트박스와 직렬화 바인딩을 유지하며
  SimpleHands 모델에는 Collider를 추가하지 않는다.
- 제스처 또는 손에 든 아이템이 활성화되면 기본 주먹을 숨긴다.

### Forest Graybox

- `Environment/Generated Ground`에는 TerrainLayer 기반 잔디·보드 연결을 따르는 흙길과
  Terrain 식생을 자동 생성한다. 생성 외 `Environment` 자식은 보존하고 시각 지형과 식생에는
  Collider를 두지 않는다.
- Forest Terrain은 `80×10×73m`, 원점 Y `-0.12`를 사용한다. 기존 조형의 실제 높이를
  보존한 채 수직 범위를 확장하므로 현재 표면 최고점 약 `0.88m`는 유지되고 이후에는
  약 `9.88m`까지 조형할 수 있다. Ground 갱신을 다시 실행해도 높이맵을 평탄화하지 않는다.
- Paint Trees는 Pandazole `Tree_24_Spring`의 충돌 없는 URP 래퍼를 사용해 48그루를
  결정론적으로 배치한다. Paint Details는 `Grass_25/24/20/19` 충돌 없는 래퍼를
  `256/16` 해상도로 혼합하며, 원본 FBX와 아틀라스의 Read/Write를 활성화한다.
  Terrain은 `drawTreesAndFoliage=true`를 사용한다.
- Resources에서 런타임 로드되는 Terrain이 Player 빌드에도 포함되도록 Board 씬에는
  비활성 GameObject와 활성 Terrain 컴포넌트로 된 참조 placeholder를 유지한다.
- Unity 6000.6.0f1 URP Standalone Player에서는 인스턴스 Terrain 경로가 표면을 누락하므로
  Forest·Maze Terrain과 placeholder는 `drawInstanced=false`를 사용한다. authored 맵이
  활성화되면 레거시 `Board Backdrop (No Gameplay Collision)`은 숨기고 해제 시 복원한다.
- 42칸·49개 일방통행 연결을 사용한다. 외곽은 `0→1→…→22→0`, 내부는
  `4→23→…→28→12`, `8→29→…→31→26`, `26→32→…→34→17`,
  `20→35→…→38→24`, `24→39→40→41→7`이며 `1→3`, `12→14` 지름길을 사용한다.
- 시작 칸은 `03`, 리스폰은 `02`, `13`, 슬롯별 시작은 `03`, `09`, `14`, `19`다.
  콘텐츠 버전은 4이며 수동 꼭짓점·회전은 재생성 청사진에도 동기화한다.
- 칸 연결 기즈모는 실제 `Source→Destination` 방향 화살표로 표시한다.
- `ForestGrayboxMapRoot.prefab`의 현재 Terrain 조형·식생과 `Environment` 오브젝트 배치는
  사용자가 완료하고 저장한 최종 authored 디자인이다. 사용자가 다시 요청하기 전에는
  setup·authoring 도구로 이를 재생성·정규화하거나 덮어쓰지 않는다.

### 보드 상점

- 열쇠 상점은 `Fantasy Lowpoly Pack (Demo)`의 `blue-house_001`, 아이템 상점 1·2는
  `house-red_001`을 기존 상점 래퍼의 visual로 사용한다. 두 아이템 상점 래퍼와 각 인덱스는
  유지하며, 외부 집의 MeshCollider는 제거하고 기존 구매용 interaction target만 사용한다.
- 열쇠·아이템 상점 marker 루트는 항상 선택된 `BoardTile.WorldCenter`에 정확히 배치한다.
  주변 장식 오브젝트와의 Physics 겹침 검사나 회피 오프셋은 사용하지 않으며, 이미 다른
  오브젝트가 중앙을 차지해도 겹친 상태로 그대로 소환한다.

### Maze Graybox

- mapId는 `maze-graybox`, 표시 이름의 영문 원문은 `Maze`, 콘텐츠 버전은 1이다.
  정의·루트 프리팹·Terrain은 미로 전용 에셋만 참조한다.
- 사용자가 배치한 8×8 격자 64칸·80개 연결을 유지한다. 좌표는 월드 X 순서를 열,
  Z 순서를 행으로 하는 `(열, 행)` 0~7이며 Footprint는 8×8m 정사각형이다.
- 시작은 `(0,0)`, 리스폰은 `(2,2) (2,5) (5,2) (5,5)`, 슬롯별 시작은
  `(0,0) (7,0) (7,7) (0,7)`이다.
- 미니맵·전체 맵과 Map Authoring은 방 100개를 지원한다. 레거시 7×7 개요 패널은 자유
  배치 맵에서 표시하지 않는다.
- `Maze Ground`는 160×153m이고 전체 잔디 레이어다. 실제 지형은
  `MazeGroundTerrain.asset` 하나만 사용하며 숲 전용 갱신 도구는 미로에 적용하지 않는다.

### 보드 착지 효과

- 칸 효과 6범주의 전역 배치 비율은 골드 획득 : 골드 손실 : 아이템 : 회복 : 피해 :
  특별 이벤트 = `5:5:2:2:2:1`이다. 회복은 `+20/+10`, 피해는 `-40/-20`으로 균등
  분할하며 홀수일 때 강한 효과에 1칸을 더 배정한다.
- Respawn 칸은 효과 배정에서 제외하고 Start 칸은 포함한다. 서버 시드로 위치를 결정하며
  같은 시드는 같은 배치를 만든다. Forest 40칸은 `12/12/5/5/4/2`, Maze 60칸은
  `18/18/7/7/7/3`으로 배정한다.
- 착지 효과 비율 변경에 따라 보드 저장 복구 호환 버전은 4이며, 이전 비율의 안정
  체크포인트는 다른 칸 배치로 복구하지 않고 콘텐츠 지문에서 차단한다.

### 보드 칸 시각

- 일반·시작·리스폰·효과 칸의 루트, 라벨, 착지 효과면 Renderer는 플레이 화면에서
  항상 숨긴다. Collider·Footprint·Topology·착지 효과 데이터는 그대로 유지한다.
- 칸 종류와 능력은 미니맵·전체 지도 UI의 아이콘과 설명으로만 표시한다. 월드에서는
  Terrain의 길, 장막, 플레이어, 주사위·아이템과 일회성 VFX만 보여준다.
- 미니맵·전체 지도에서 플레이어는 자신의 색 원형 표식으로 현재 논리 칸의 안전 중심에
  표시한다. 착지 효과 아이콘도 같은 중심에 표시하며, 둘이 겹치면 효과 아이콘을 플레이어
  원보다 위 레이어에 그린다.
- 칸 경계 Gizmo는 Editor 작업용이므로 유지하며 Player 빌드에는 표시하지 않는다.

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

- 사용자가 2026-10-07 저장한 미니게임 씬·환경 프리팹의 배치, 카메라, 조명과 UI 디자인을
  최종 authored 디자인으로 취급한다. 아래에서 명시한 세 변경 외에는 setup/rebuild 도구로
  재생성·정규화하거나 미관을 수정하지 않는다.
- Red Light Green Light 씬의 신호탑은 삭제된 최종 상태를 유지한다. 상단 HUD에는 글자 대신
  빨강·초록 3등만 표시한다. 매 주기는 `초록 3 → 빨강 1/초록 2 → 빨강 2/초록 1 → 빨강 3`
  순서이며, 빨강 3개 다음에는 세 등이 동시에 초록으로 돌아간다. 완전한 빨강 3개에서만
  이동을 막고 위반을 판정한다. 빨강으로 바뀌기 전 세 단계의 유지시간은 서버 시드로 각각
  `0.1~3.0초` 안에서 독립 결정한다. 완전한 빨강이 되면 이동 상태는 즉시 금지되며,
  서버의 자발적 이동 위반 판정에는 기존 네트워크 보정용 `0.15초` 유예를 유지한다.
- Stable Footing의 `SafeSymbolDisplay`는 authored 왼쪽·중앙·오른쪽 슬롯과 세 스프라이트를
  그대로 사용한다. 매 사이클 세 심벌을 모두 표시하되 정답을 항상 중앙·초록색으로 옮기고,
  오답 둘은 양옆·원래 흰색으로 표시한다.
- Wrong Way는 기존 Lane/Step anchor, 플레이 좌표, 씬 환경 디자인을 유지한다. 네 Lane의
  `Step 01~50`에 ToyBox `BasicBlock` 12종을 충돌 없는 nested visual로 결정론적으로 섞고,
  Standard 셰이더 원본은 Ignore 원본을 수정하지 않은 URP/Lit 파생 재질로 표시한다.
- 2026-10-07 Board UI·Sequence Memory 오디오 표적 EditMode 계약은 8/8 통과했다.
  전체 EditMode는 450개 중 442개 통과했다. 남은 8개는 현재 최종 디자인과 옛 계약이
  충돌하는 Forest TerrainCollider, BombPassing 환경 Collider, MinigameResultCanvas,
  공용 에셋의 옛 Bomb mesh 기대, TagChase 씬, OnlineBootstrap의 LobbyCanvas 활성 override,
  MinigameCommonHud 루트 scale, Wrong Way 화살표 원본 경로 계약이며 이번 작업 범위에서는
  해당 디자인을 되돌리지 않았다.

- 게임은 항상 4인 구조이며 화면 HUD에는 현재 판단에 필요한 입력·신호·점수만 둔다.
- Tag Chase·Race·Bomb Passing·Snowy Spin·Cliff Barrage는 공통 HUD만, Arena Combat은
  공통 HUD와 피격 플래시만 사용한다. Minefield·Balloon Blow는 전용 화면 HUD가 없다.
- Wrong Way는 로컬 방향 아이콘, Red Light Green Light는 현재 신호만 표시한다.
- Gift Grab은 운반·스턴·기지 수량을 월드에, Stable Footing은 안전 문양만 표시한다.
- Sequence Memory는 NPC 순서와 로컬 입력·상태 한 줄을 표시한다. Territory Paint와
  Bouncing Balls는 이름 중복 없이 P1~P4 점수만 작게 표시한다.
- Solo 캡처의 좌측 `DEVELOPER SOLO TEST` 패널은 Editor 전용 테스트 HUD다.
  `UNITY_EDITOR` 전용 assembly와 Editor 런처에서만 생성되므로 Development·Release
  Player 빌드에는 포함되거나 표시되지 않는다.

### 미니게임·격투 관전 카메라

- Gift Grab·Bomb Passing·Snowy Spin·Cliff Barrage·Race·Stable Footing·
  Territory Paint의 고정 공용 orthographic 카메라는 정탑뷰에서 수직 기준 35°를
  낮춘 시점(Euler X 55°)을 사용한다. 높이와 orthographic size는 유지하고 경기장
  중심을 계속 바라보도록 뒤쪽 위치를 보정한다.
- 이미 사선·정면·개인 추적 시점인 나머지 미니게임 카메라는 각 게임의 기존 구도를
  유지한다.
- Arena Combat 탈락자 관전과 보드 착지 전투의 비참가자·탈락자 관전도 같은
  `SharedCameraFraming`의 정탑 기준 35°를 사용한다. Arena는 높이 12m·FOV 58,
  보드 격투는 높이 10m·FOV 55이며 뒤쪽 거리는 35°에서 자동 계산한다. 살아 있는
  격투 참가자는 기존 1인칭 시점을 유지한다.

### 미니게임 시각 피드백

- 15종의 플레이어 스폰 표시는 숨긴다. Board 단계에서는 Lobby 프레젠테이션을
  비활성화하며 Bomb Passing은 중앙 블록만 숨기고 소환 링은 유지한다.
- Sequence Memory의 A/S/D는 도/미/솔에 대응한다. `Assets/Resources/sound/- Bell 7.mp3`를
  공용 사운드 큐로 등록하고 원음 G5를 각각 0.6674199·0.8408964·1.0배 피치로 재생한다.
  Snowy Spin 중앙 원, Bouncing Balls 중앙 원은 시각적으로 숨긴다.
- Minefield·Wrong Way·Race는 로컬 플레이어 중심 개인 카메라를 사용하고 Minefield와
  Red Light Green Light의 시점을 낮춘다. Bouncing Balls는 슬롯별 화면 축과 측면 방어바
  방향을 보정하되 본인 점수는 별도로 강조하지 않는다.
- Balloon Blow는 보드와 겹치지 않는 위치에서 플레이어가 카메라를 향한다. Stable Footing은
  가로 8×세로 6이며 전광판은 뒤쪽 벽처럼 세운다. Gift Grab 기지 표시는 숫자만 사용한다.

### 오디오

- 직접 BGM은 로비·보드·공용 미니게임만 사용하고 나머지는 fallback을 사용한다.
- 보너스 준비음은 공개 전 2초만 재생하며 공개·일시정지에서 중지하고 duck하지 않는다.
- 보드 발소리는 짧은 원샷 11개를 shuffle 재생한다.
- Sequence Memory 음계는 `minigame.sequence_memory.tone` 큐 하나를 전역 풀 음성으로
  재생한다. 원본의 선행 무음 0.21초를 큐에서 건너뛰고 동시 재생은 8개로 제한한다.
  각 음은 독립 피치 요청이라 겹치는 잔향도 서로의 피치를 덮어쓰지 않는다.

## 현재 검증 상태

- 2026-10-06 기준 Unity 컴파일 오류는 0건이며 Windows Development Mono x64 빌드가
  성공했다. 전체 EditMode 445개 중 443개가 통과했다. 남은 2개는 사용자가 보존한
  `LobbyCanvas.prefab` 비표시와 `OnlineBootstrap` 활성 override를 검사하는 알려진 예외다.
  Terrain·Board 표적 계약 11개, Skybox·Sequence Memory 표적 테스트 20개와 카메라 관련
  씬 계약 15개도 통과했다. 지도 중앙 정렬·원형 플레이어 표식·효과 아이콘 상위 표시를
  포함한 지도 UI 계약 11개도 모두 통과했다.
- Sequence Memory의 `Npc.prefab` 루트에 `LocalizedFontScope`를 적용해 이전 현지화 폰트
  계약 실패를 해소했다.
- 최종 아이템 4프로세스 실기 QA가 Forest v4·실제 UGS/Relay에서 4/4 PASS했다. Pistol·Sniper
  hitscan, 조준선 조건, 무탄환 오브젝트, 전 프로세스 tracer, Sniper 무확대, Grenade
  소유자 전용 16m 원·승인/거절 복구·1초 포물선, Mine 소유자 전용 보드/지도 표시와
  발동 정리를 확인했다. 같은 빌드에서 월드 칸 블럭은 숨고 지도 능력 아이콘, Terrain
  흙길, 장막·주사위는 유지됐다. 증빙은 `Logs/ItemMultiplayerQA-20261006-013807` 및
  `Builds/TestArtifacts/ItemMultiplayerQA/2026-10-06/20261006-013807`의 31장이다.
- pause 중 실제 client를 종료·재실행한 재접속 QA가 4/4 PASS했다. 일시정지 중 타이머
  정지, 동적 슬롯 재연결, 동일 좌석의 권위 상태·위치 복원, player pause 복원·해제를
  확인했다. 복원 직후 로비 초기 위치가 덮어쓰던 실제 결함은 `_restoredFromSnapshot`
  가드와 `NetworkTransform.Teleport`로 수정했다. 증빙은
  `Logs/ReconnectE2E-20261005-205843`이다.
- Maze v1을 선택한 4인 경기를 실제 프로세스 종료·재실행으로 복구했다. Turn Overview,
  Minigame Intro Ready, Match Complete 세 체크포인트가 각각 4/4 PASS했고 증빙은
  `Builds/TestArtifacts/RecoveryQA/2026-10-05`에 있다.
- Forest v4 Development Player 4프로세스에서 15종을 자연 결과까지 연속 실행했다.
  15/15 입력 창·자연 종료·정산, 공동 1위 수상식(`ranks=1,1,1,1`), 4인 로비 복귀가
  PASS했다. 결과 정산 주입은 사용하지 않았으며 게임별 1280×720 캡처는
  `Builds/TestArtifacts/BuildMinigameQA/2026-10-05/FullMatch-20261005-212630`에 있다.
- additive 씬에서 `OnlineBootstrap`의 기본 Skybox가 유지되던 실제 빌드 문제를 발견해
  Bootstrap·setup·계약 테스트를 `FS000_Night_01`로 통일했다. 재빌드한 최종 15장에는
  동일한 야간 Skybox가 적용됐다.
- Forest Terrain은 Editor와 런타임 상태·빌드 포함 자산이 모두 정상이었지만
  `drawInstanced=true`일 때만 Windows Player에서 사라졌다. 비인스턴스 경로로 전환한
  `Builds/TestArtifacts/ItemMultiplayerQA/2026-10-06/20261006-010604/pistol-far-p0.png`에서
  잔디·흙길과 야간 Skybox가 함께 표시되는 것을 확인했다. 이 실행은 이후 별도 아이템
  마커 대기시간 초과로 중단되어 Terrain 시각 증빙으로만 사용한다.
- 최종 Forest v4 Development Player에서 사용자 피드백 반영 후 15종을 다시 촬영했다.
  결과·스크린샷 15/15, 수상식과 4인 로비 복귀가 통과했으며 보드 이동과 결과 정산은
  물론 검증된 타격 이후 전투도 가속했다. 증빙은
  `Builds/TestArtifacts/BuildMinigameQA/2026-10-06/FullMatch-20261006-011032`에 있다.
- 공용 미니게임 7종, Arena Combat 관전, 보드 격투 관전은 하나의 정탑 기준 35° 계산을
  사용한다. Unity 런타임 계산은 양쪽 관전 모두 `35.000°`였고 최종 빌드가 성공했다.
- `DEVELOPER SOLO TEST` 패널은 Editor 전용이며 Player 캡처에는 없다. 우하단
  `Development Build`는 캡처 도구 오버레이가 아니라 Development Player 자체 표시다.

## 남은 검증·TODO

- 15종 캡처 피드백 뒤 게임별 에셋 교체, 디자인 변경, 배치·카메라 위치를 조정한다.
  우선 검토 후보는 Bouncing Balls의 하단 골대·방어바 프레이밍과 Race의 하단 여백이다.
- Arena Combat 최종 캡처는 살아 있는 참가자의 1인칭 화면이다. 35° 관전 계산은
  검증했지만 최종 디자인 판단용 실제 관전 화면과 보드 격투 관전 화면은 별도 캡처한다.
- 자연 진행 QA는 15종 정상 종료를 확인했지만, 모든 게임의 조기 종료·시간 종료·공동
  순위 조합과 수동 조작감, VFX 투명 정렬·Bloom·Soft Particle·`Reduce Flashes`는
  화면 피드백 단계에서 추가 확인한다.
- 실제 Relay는 사용했지만 제어된 지연·패킷 손실 주입은 하지 않았다. 별도 PC 네트워크,
  장시간 soak, Windows IL2CPP Release 후보를 검증한다.
- `FS000_Night_01`은 Git 비추적 `Assets/Ignore` 에셋이므로 다른 빌드 환경에도 같은
  GUID의 원본이 필요하다.
- 알려진 Lobby UI 계약 예외 2건은 현재 디자인을 변경할 때 함께 정리한다.
- 미로 레이아웃 변경 뒤 흙길을 다시 칠할 미로 전용 바닥 갱신 도구는 기술 부채로 남는다.
