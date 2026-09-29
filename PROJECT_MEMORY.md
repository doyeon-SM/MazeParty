# MazeParty 임시 기획 메모

이 문서는 현재 구현 기준, 확정된 작업 계약, 미정 결정과 다음 작업만 보관한다.
완료 이력은 Git, 확정 기획 원문은 Notion, 장기 계약은 코드와 EditMode 테스트를
원본으로 삼는다. 사용자의 최신 명시적 지시를 항상 우선한다.

## 현재 작업 기준

- 저장소·Unity 프로젝트: `C:/Unity/MazeParty`
- 작업 브랜치: `dev/UI`
- 최근 자동 검증: Unity 6000.6.0f1 컴파일 오류 0, 전체 EditMode 373/373 통과,
  실패·스킵 0
- 비차단 기존 경고: Ignore 경로 무료 캐릭터 에디터 스크립트의 `CS0414` 2개

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
- `Forest Graybox`는 수동 편집을 반영한 42칸·47개 일방통행 연결을 사용한다.
  외곽은 `0→1→…→22→0`의 XZ 기준 반시계 방향이며, 내부 경로는
  `4→23→…→28→12`, `8→29→…→31→26`, `26→32→…→34→17`,
  `20→35→…→38→24`, `24→39→40→41→7` 순서로 외곽에 합류한다.
- 시작 칸은 `03` 한 칸, 리스폰 칸은 사용자가 의도적으로 배치한 `02`, `13` 두 칸이다.
  수동 꼭짓점과 회전은 재생성 청사진에도 동기화하며 맵 콘텐츠 버전은 3을 사용한다.
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

- 이번에 확인한 5종 외 나머지 10종 미니게임의 조작·조기 종료·시간 종료·동률·결과·씬
  정리 분기와 15종 전체 Relay 순환
- pause 중 재접속, 호스트 복구 3개 체크포인트, 공동 순위·수상식
- 별도 PC, 고지연, IL2CPP 릴리즈 후보
- 15턴 전체 여정은 사용자가 별도로 진행
