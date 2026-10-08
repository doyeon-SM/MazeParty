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
- 보드 상대 플레이어 닉네임은 일반 불투명 지형·벽에는 깊이 테스트로 가리고, 깊이를
  기록하지 않는 파티클 장막·투명 장애물에는 보드 카메라에서 닉네임 앵커까지의 비트리거
  Collider 시야 판정으로 닉네임 Renderer만 숨긴다. 플레이어 Collider와 이 클라이언트에
  보이지 않는 다른 플레이어의 전용 경계벽은 판정에서 제외하며, 로컬 닉네임과 미니게임·
  보드 이탈 상태에는 강제 마스킹을 남기지 않는다. 언어 변경 뒤에도 깊이 테스트 재질과
  현재 언어 폰트 atlas를 함께 유지한다.
- `BoardCanvas.prefab`은 사용자가 조정한 현재 배치를 유지한다. 삭제된 Players·Inventory
  제목과 인벤토리 슬롯 라벨은 런타임 바인딩에서도 제거하며, 각 플레이어 카드의 Key와
  Gold는 `Assets/Ignore/Icon_NCI`의 아이콘 뒤에 숫자 텍스트를 두는 구성으로 표시한다.
- 네 플레이어 카드의 체력은 기존 Bar·Fill·Text 배치를 유지한 표시 전용 `Slider`로
  구성한다. 채움은 좌측에서 우측으로 진행하고 런타임은 값·상태색·텍스트만 갱신하며,
  외곽선·상단 하이라이트·구간 눈금은 `BoardCanvas.prefab`에 직접 제작한다.
- 술래잡기 술래의 1인칭 화면에는 공용 `MinigameCommonHud.prefab`에 authored된 중앙
  `Tagger Aim`을 표시한다. 에임은 입력을 막지 않으며 술래의 실제 Running 단계에서만
  켜고 카운트다운·일시정지·도망자 화면에서는 숨긴다.

### 빌드 버전 및 방 접속

- 현재 기획 버전은 `v0.52`이며 Unity `PlayerSettings.bundleVersion`은 접두사 없는
  `0.52`로 관리한다. 런타임의 단일 버전 출처는 `Application.version`이다.
- `LobbyCanvas.prefab` 왼쪽 아래에는 `v` 접두사를 붙인 현재 빌드 버전을 표시한다.
  버전 라벨은 프리팹에 authored하고 런타임은 표시 문자열만 갱신한다.
- 방 생성 시 현재 버전을 MPS 세션 속성과 NGO connection payload에 함께 기록한다.
  참가·재접속은 호스트와 정확히 같은 버전만 승인하며 누락·공백·접미사 차이도 거부한다.
  NGO 승인 검사를 플레이어 생성 전에 수행하고 기존 MPS 세션 속성 검사는 2차 방어로 유지한다.
- 승인 거절된 pending client의 disconnect는 실제 참가자 이탈로 처리하지 않는다. 따라서
  잘못된 버전의 접속 시도가 진행 중 경기의 재접속 일시정지나 로비 좌석 정리를 일으키지 않는다.
- 서로 호환되지 않는 변경을 배포할 때는 기획 버전과 `bundleVersion`을 함께 올린다.

### 로비 프레젠테이션과 맵 선택

- 대기방 3D 공간은 `Assets/Ignore/Maze` 원본을 사용하는 하나의 철창·감옥이며,
  `LobbyArena.prefab`과 허용된 `Multiplayer/UI` 프리팹에서 제작한다.
- 대기방 입장 전에는 글자 없는 16:9 임시 배경과 현재 언어 로고를 표시한다. 입장 성공
  즉시 둘을 숨기고 3D 대기방을 표시하며, 세션에서 나왔을 때 현재 언어 로고를 복원한다.
- `LobbyCanvas.prefab` 비표시와 `OnlineBootstrap` 인스턴스 활성 override는 사용자가 보존을
  지시한 현재 상태이며, 관련 EditMode 계약 2건은 알려진 비차단 예외다.
- 플레이어 장막은 대기방에서 표시와 판정을 모두 끄고 보드 단계부터 활성화한다. 남은
  보드 상태와 관계없이 세션 단계가 대기방이면 서버와 클라이언트 모두 즉시 제거한다.
- `OnlineBootstrap`의 남쪽 철문 조각은 최상위 씬 오브젝트로 두지 않고
  `LobbyArena/Presentation` 하위에 둔다. 로비 외벽은 유지하되 보드가 additive 로드될 때
  다른 대기실 프레젠테이션과 함께 표시·충돌이 꺼져 보드에 남지 않게 한다.
- 대기실 플레이어 행은 준비 여부 문구를 붙이지 않고 닉네임으로만 표시한다. 준비한
  플레이어의 닉네임은 초록색으로 바꾸고, 방장 닉네임 왼쪽에는 Modern UI의 채운 별
  아이콘을 표시한다.
- 초대 코드는 화면 중앙 상단에 두며 기본값은 마스킹한다. 코드 오른쪽에는 누르는 동안만
  원문을 보여 주는 보기 아이콘 버튼, 그 오른쪽에는 마스킹을 유지하는 복사 아이콘 버튼을 둔다.
- 준비 버튼은 화면 중앙 하단에, 시작 버튼은 그 바로 아래에 둔다. 저장 경기 복구 선택 시
  같은 두 버튼을 폐기/계속 동작으로 재사용하는 기존 기능은 유지한다.
- 맵 선택은 준비 영역에서 분리한 `Board Settings Popup` 안에 둔다. 옷장 버튼 바로 위의
  `보드 설정` 버튼으로 열고 닫으며, 보드 설정과 옷장은 동시에 열지 않는다. 모든 플레이어가
  동기화된 현재 맵 이름을 볼 수 있고, 호스트만 프로덕션 `BoardMapCatalog.Maps` 순서로
  맵을 순환 선택한다.
- 맵 표시 이름은 영어 `Forest`·`Maze`, 한국어 `숲`·`미로`, 일본어 `森`·`迷路`,
  중국어 간체 `森林`·`迷宫`이다. 새 세션 기본은 `forest-graybox`이며 저장 경기에서는
  저장된 `MapId`와 `ContentVersion`을 우선한다.

### 플레이어 기본 손

- 좌우 기본 손은 `Assets/Ignore/SimpleHands/Prefabs/WhiteHand.prefab` 원본의
  `SimpleFistHand.prefab` 변형을 사용한다. 왼손은 visual root X축 미러로 구분한다.
- 기존 손 앵커, 주먹 애니메이션, 독립 SphereCollider 히트박스와 직렬화 바인딩을 유지하며
  SimpleHands 모델에는 Collider를 추가하지 않는다.
- 제스처 또는 손에 든 아이템이 활성화되면 기본 주먹을 숨긴다.

### 플레이어 충돌·피격 영역

- 이동과 장애물 차단에는 플레이어 루트의 `CharacterController` 하나만 사용한다.
  반경은 몸 시각에 맞춘 `0.42m`이며 기존 높이·중심·앉기 전환은 접지 안정성을 위해
  유지한다.
- 몸·머리·왼손·오른손의 네 `PlayerHitZone` Collider는 모두 trigger로 유지해 피격에만
  사용한다. 손·머리 Collider와 장착 장식은 이동을 막거나 장애물에 걸리지 않는다.

### 보드 플레이어 접지

- 보드 플레이어는 점프 없이 `CharacterController`를 사용한다. 전역 일시정지를 제외하고
  입력이 없거나 단계상 이동 입력이 잠긴 상태에서도 서버가 매 물리 틱 중력 `-24`와 접지
  유지 속도 `-2`를 적용해, 언덕·장식·밀림으로 높아진 Y 위치가 공중에 고정되지 않고 충돌
  지면으로 자연스럽게 내려오게 한다.
- BoardFlowTestbed도 같은 접지 규칙을 사용한다. 복구·스왑·리스폰 텔레포트는 수직 속도를
  초기화하되 수평 경계 보정은 진행 중인 낙하 속도를 보존한다.

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
- 칸 사이 논리 통로 폭은 공용 `BoardGate.DefaultWidth=4m`를 사용한다. Forest의 49개와
  Maze의 80개 authored gate 및 새 맵 authoring 기본값을 모두 같은 폭으로 유지한다.

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
- 보드 전체 지도(M키 지도와 자유배치 턴 개요 지도, 미니맵 제외)는 각 로컬
  플레이어의 최종 주사위 결과가 확정된 뒤에만 칸수를 표시한다. 굴림이 확정된
  현재 칸을 `0`으로 두고 주사위 합계까지 가능한 모든 방향성 이동을 미리 계산하며,
  한 굴림 안에서 순환해 같은 칸이나 갈림길을 다시 지나는 경로도 포함한다. authored
  outgoing gate 순서의 첫 경로는 기본 노란색, 두 번째 이후 갈림 경로는 하늘색 숫자를
  칸 구석에 표시한다. 활성 열쇠상점에 방향성 경로로 도달할 수 있으면 authored 첫
  출구보다 열쇠상점까지의 최단경로를 노란색 메인 경로로 우선하며, 상점이 없거나
  도달할 수 없으면 authored 순서로 폴백한다. 일반 칸 이동과 남은 이동 횟수 감소로는
  번호를 다시 계산하거나 당기지 않는다. 실제 갈림길 선택이 확정되면 맞지 않는 후보
  경로 숫자를 제거하고, 선택 후에도 열쇠상점에 도달할 수 있으면 남은 호환 경로 중
  최단 안내 경로를 새 노란색 메인 경로로 승격한다.
  같은 갈림길을 다시 방문하면 선택 순서를 계속 기록한다. 주사위 굴리기 전과 액션 턴
  종료 후에는 숫자를 표시하지 않으며, 이동을 먼저 마친 뒤에도 액션 턴이 끝날 때까지
  해당 굴림의 숫자를 유지한다. 위치교환·사망 리스폰처럼 액션 중 권위적으로 말을
  재배치하면 이미 소비한 칸수는 유지하고 새 칸을 그 숫자의 기준점으로 삼아 남은
  숫자만 다시 계산하며, 재배치 전 갈림길 선택 기록은 초기화한다.
- `BoardCanvas.prefab`의 현재 전체 지도 디자인에서 삭제한 Title·Legend·Current
  Tile·Key Shop Distance 텍스트/아이콘은 선택 바인딩으로 취급하고 setup 재실행으로
  복원하지 않는다. 전체 지도의 열쇠상점은 해당 칸을 노란색으로 채우며 `K` 문자나
  열쇠 타일 아이콘을 겹쳐 표시하지 않는다. 미니맵과 전체 지도에는 현재 칸에서
  열쇠상점까지의 방향성 최단경로를 노란 점선으로 표시하고, 상점 미배치·현재 칸과
  동일·도달 불가 상태에서는 이전 점선을 지운다.
- 플레이어가 죽어 리스폰할 때 이미 표시된 미굴림 `Ready` 월드 주사위는 플레이어와
  함께 새 리스폰 칸으로 재배치한다. 숨김·굴림 중·결과 표시 주사위는 건드리지 않으며,
  사망 처리 중 숨겨진 주사위를 미리 준비하지 않는다.
- 월드 주사위 몸체 색은 슬롯 고정색이 아니라 해당 플레이어의 권위 있는
  `Appearance.BodyColor` RGB를 서버에서 복제해 모든 클라이언트와 중도 접속자에게
  동일하게 표시한다. 프리팹의 슬롯색은 에디터·스폰 전 폴백으로만 유지한다.
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
- 도착 지점이 있는 Race·Wrong Way·Minefield·Red Light Green Light는 AllIn1VfxToolkit의
  `Explosion Galaxy`를 정리한 프로젝트 전용 `ArrivalFireworks`를 사용한다. 폭죽은 초록색과
  노란색 두 burst로 구성한 풀링 one-shot이며, 첫 관측·재접속·재진입 때 이미 끝난 도착을
  다시 재생하지 않는다.
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
- 2026-10-08 전체 EditMode는 457개 중 450개가 통과했다. 이번 Tag Chase 충돌·카메라,
  공통 HUD 에임, 플레이어 충돌 캡슐, 보드 통로 폭, 빌드 버전 접속 계약은 모두 통과했다. 남은 7개는
  현재 authored 상태와 옛 계약이 충돌하는 Forest TerrainCollider, Bomb Passing 환경
  Collider, MinigameResultCanvas, 공용 에셋의 옛 Bomb mesh 기대, OnlineBootstrap의
  LobbyCanvas 활성 override, BoardCanvas 루트 scale, Wrong Way 화살표 원본 경로다.

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
- 여러 참가자가 함께 보는 공용 카메라는 플레이 중 위치·회전·줌을 바꾸지 않는다.
  Tag Chase 도망자 카메라와 Arena Combat 관전 카메라는 각 경기장 중앙의 시작 포즈에
  고정하고, Bomb Passing은 폭발 VFX·조명은 유지하되 카메라 shake를 사용하지 않는다.
  기존 공용 카메라들도 authored 포즈와 lens에서 런타임 reframe을 하지 않는다. Race·Minefield·
  Wrong Way처럼 로컬 플레이어를 따라가는 개인 카메라는 이 고정 규칙에서 제외한다.

### 미니게임 시각 피드백

- 15종 미니게임의 첫 시작은 서버 기준 공통 HUD에서 `3 → 2 → 1`을 표시한다. 여러 라운드
  게임은 첫 라운드에만 이 카운트다운을 사용하고 이후 라운드는 바로 시작한다.
- 공격 입력은 기존 한손 공격 모션을 유지하고, 밀기 입력은 양손이 동시에 앞으로 나가는
  공통 모션을 사용한다. 대상이 없는 유효한 공격·밀기 시도도 쿨다운당 한 번 애니메이션을
  재생하며, 실제 피격 모션과 VFX는 대상이 있을 때만 재생한다.
- Cliff Barrage 투사체는 20Hz 권위 스냅샷 사이를 제한된 속도 예측과 지수 보간으로 표시하고,
  일시정지 중에는 예측 시간을 고정한다. 투사체·레이저 생성 빈도는 경기 경과 10초마다
  20%씩 증가하며 5단계·최대 2배로 제한한다.
- Race 완주 입력 목표는 A/D 교대 200회다.
- 15종의 플레이어 스폰 표시는 숨긴다. Board 단계에서는 Lobby 프레젠테이션을
  비활성화하며 Bomb Passing은 중앙 블록만 숨기고 소환 링은 유지한다.
- Tag Chase 서버·Solo 이동 충돌은 기존 네 개의 보이지 않는 임시 사각형 대신 authored
  환경의 내부 벽 16개와 기둥 14개의 XZ bounds를 사용한다. 외곽 경계는 기존 arena clamp를
  유지하며, 환경 프리팹과 결정론적 collision layout의 30개 bounds 일치를 EditMode에서
  검증한다.
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

- 2026-10-08 보드 상대 닉네임의 깊이 마스킹·언어 전환 재질 유지·파티클 장막 Collider
  시야 마스킹 계약을 포함한 표적 EditMode 13/13이 통과했고 Unity 콘솔 컴파일 오류는
  0건이다. 전체 EditMode는 483개 중 476개가 통과했으며 남은 7개는 기존 authored 계약
  불일치와 동일해 이번 변경으로 새 실패는 없다. `MazeParty.Gameplay.csproj`와
  `MazeParty.Multiplayer.csproj --no-restore` 빌드도 오류 0건으로 성공했다.
- 2026-10-08 공통 3/2/1 카운트다운·공격/밀기 애니메이션·도착 폭죽·Cliff Barrage
  보간/10초 빈도 상승·Race 200회 변경의 표적 EditMode 57/57과 후속 엣지 재검증
  30/30이 통과했다. 전체 EditMode는 482개 중 475개가 통과했고 남은 7개는 기존 authored
  계약 불일치와 동일하다. Unity 콘솔 오류는 0건이며 `MazeParty.Gameplay.csproj`와
  `MazeParty.Multiplayer.csproj --no-restore` 빌드는 모두 오류 0건으로 성공했다.
- 2026-10-08 열쇠상점 최단경로 기반 노란 칸수·전체 지도 점선·주사위 리스폰 동행·
  플레이어 외형색 복제의 표적 EditMode 35/35와 주사위 프리팹 계약 1/1이 통과했고
  Unity 콘솔 컴파일 오류는 0건이다. 전체 EditMode는 471개 중 464개가 통과했으며
  남은 7개는 기존 authored 계약 불일치와 동일하다. `MazeParty.Multiplayer.csproj
  --no-restore` 빌드도 오류 0건으로 성공했다.
- 2026-10-08 대기실 준비 닉네임 색·방장 별·상단 초대 코드 아이콘·하단 준비/시작
  액션·보드 설정 팝업의 표적 EditMode 9/9가 통과했고 Unity 콘솔 컴파일 오류는 0건이다.
  전체 EditMode는 465개 중 458개가 통과했으며 남은 7개는 기존 authored 계약 불일치와
  동일하다. 새 헤더 배치를 덮던 `OnlineBootstrap` RectTransform override는 제거했고,
  사용자가 보존한 LobbyCanvas 활성 override는 유지했다. `MazeParty.Multiplayer.csproj
  --no-restore` 빌드도 오류 0건으로 성공했다.
- 2026-10-08 주사위 결과 기반 칸수 표시 수명주기·순환/반복 갈림길·강제 재배치
  재기준화·재접속 복원과 전체 지도 UI 계약을 포함한 표적 EditMode 20/20가 통과했고,
  Unity 콘솔 컴파일 오류는 0건이다. 전체 EditMode는 464개 중 457개가 통과했으며
  남은 7개는 기존 authored 계약 불일치와 동일하다.
  `MazeParty.Multiplayer.csproj --no-restore` 빌드도 오류 0건으로 성공했다.
- 2026-10-08 보드 전체 지도 이동 횟수·갈림길 필터·열쇠상점 단색 표시·삭제 UI
  바인딩 계약을 포함한 표적 EditMode 24/24가 통과했고 Unity 콘솔 컴파일 오류는
  0건이다. 전체 EditMode는 459개 중 452개가 통과했으며 남은 7개는 기존에 기록된
  authored 계약 불일치와 동일하다. `MazeParty.Multiplayer.csproj --no-restore`
  빌드도 오류 0건으로 성공했다.
- 2026-10-08 Unity 컴파일 오류는 0건이다. 빌드 버전 payload·거절 상태·로비 프리팹·
  `OnlineBootstrap` 연결 승인 표적 계약은 4/4 통과했다. 전체 EditMode는 457개 중
  450개 통과·7개 기존 authored 계약 불일치이며 이번 변경으로 새로 남은 실패는 없다.
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
- 보드 설정 팝업은 현재 맵만 설정한다. 추후 턴 수, 열쇠 가격, 미니게임 골드 지급량 등
  세션 커스텀 규칙으로 확장하며, 이때 SessionSnapshot/MPS 속성/저장 복원/서버 검증도
  함께 확장한다.
- 사용자가 보존하도록 지정한 `LobbyCanvas.prefab` 비표시와 `OnlineBootstrap` 활성
  override 계약 예외 2건은 해당 숨김 상태를 해제하라는 명시적 요청이 있을 때 정리한다.
- 미로 레이아웃 변경 뒤 흙길을 다시 칠할 미로 전용 바닥 갱신 도구는 기술 부채로 남는다.
