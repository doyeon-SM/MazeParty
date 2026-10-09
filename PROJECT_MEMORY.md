# MazeParty 임시 기획 메모

이 문서는 현재 구현 기준, 확정된 기획과 남은 TODO만 기록한다. 완료 이력은 Git과
회의록, 세부 구현 계약은 코드와 유지 대상 EditMode 테스트를 원본으로 삼는다.
사용자의 최신 명시적 지시가 이 문서보다 우선한다.

## 현재 기준

- 저장소·Unity 프로젝트: C:/Unity/MazeParty
- 안정 기준 브랜치: main, 현재 작업 브랜치: dev/minigame
- 현재 게임 버전은 v0.52, PlayerSettings.bundleVersion은 0.52다.
- 외부 패키지 원본은 Assets/Ignore, 프로젝트 전용 추적 프리팹은 Assets/MazeParty에
  둔다. 다른 환경에서도 Ignore 에셋의 경로와 GUID를 동일하게 유지해야 한다.
- Assets/Ignore의 Resources/Scripts/MaterialImporter.cs는 Player 빌드에 포함되지 않도록
  파일 전체를 #if UNITY_EDITOR로 감싼 로컬 패치를 유지한다.
- 사용자가 조정한 씬 배치와 프리팹 디자인을 최종 authored 상태로 취급한다. 런타임은
  직렬화된 바인딩을 통해 값과 표시 상태만 바꾸며 setup 재실행은 기존 디자인을
  덮어쓰거나 런타임 대체 UI를 만들지 않는다.
- 외부 visual에는 Collider와 NetworkObject를 두지 않고 판정과 네트워크 권위는 기존
  gameplay 오브젝트가 소유하는 것을 목표 계약으로 삼는다. 현재 위반은 남은 TODO에 기록한다.
- 보드와 프로덕션 미니게임 15종은
  Assets/Ignore/Fantasy Skybox FREE/Cubemaps/Classic/FS000_Night_01.mat을 사용한다.
  미니게임 전용 Camera를 추가하지 않고 지속 Main Camera·Cinemachine 경로를 사용한다.

## 확정된 구현 계약

### UI·현지화·팝업

- 캐주얼 영문 디자인을 기준으로 보라색을 주색, 초록색·남색을 보조색으로 사용한다.
  둥근 채움 표면은 Rounded Filled 1024px를 사용한다.
- 글자 역할 기준은 제목 34, 주요 CTA 32, 정보 22, 일반 20, 설명 16 Bold다. 글자에는
  UnityEngine.UI.Outline 같은 테두리를 사용하지 않고 크기·색상·배경 대비로 가독성을
  확보한다.
- 영어·한국어는 KCC, 일본어는 Noto JP, 중국어 간체는 Noto SC를 사용한다. 플레이어
  이름에는 상호 fallback을 적용하고 세 동적 폰트 importer는 Hinted Smooth를 사용한다.
- 영어 로고는 MazeParty, 한국어 로고는 미로파티다. 일본어·중국어는 전용 로고가
  생기기 전까지 영어 로고를 사용한다.
- 사용자가 닫을 수 있는 화면 팝업은 로컬 LIFO 스택으로 관리한다. ESC와 각 닫기·취소
  버튼은 최상단 또는 자신이 소유한 항목 하나만 닫는다. 스택이 남아 있으면 로컬 게임
  입력과 포인터 잠금을 막고, 모두 닫힌 뒤의 ESC만 공통 설정 메뉴를 연다.
- 스택 대상은 공통 설정·나가기 확인·알림, 참가 코드, 보드 설정, 옷장, 사용자가 M으로
  연 전체 지도, 아이템 상점, 위치교환 대상창이다. 홀드형 감정표현 휠과 Dropdown은
  자체 Cancel을 우선한다. 서버 상태가 소유하는 턴 선택·결과·재접속·Pause Banner와
  자동 턴 개요 지도는 스택에 넣지 않는다.
- 플레이 중 Canvas UI는 허용된 authored prefab을 원본으로 사용하며 디자인상 삭제한
  텍스트나 바인딩을 setup으로 복원하지 않는다.

### 세션·버전·경기 복귀

- 방 생성 시 Application.version을 MPS 세션 속성과 NGO connection payload에 기록한다.
  참가·재접속은 호스트와 정확히 같은 버전만 승인하며 누락·공백·접미사 차이도 거부한다.
  거절된 pending client의 disconnect는 실제 참가자 이탈로 처리하지 않는다.
- 로비에는 v 접두사를 붙인 현재 빌드 버전을 표시한다. 비호환 변경 배포 시 기획
  버전과 bundleVersion을 함께 올린다.
- 경기 복귀의 세션 단계 저장은 최대 3회, 준비·언로드는 명시적 deadline을 사용한다.
  fail-closed LeaveAsync는 일반 예외 최대 3회와 10초 상한을 사용한다.
- 소유권 상실 또는 저장 실패 시 대기열을 해제한다. 시간 초과는 중복 재시도 없는
  terminal 상태로 처리하고 recovery journal은 보존한다. 실제 로비 복귀 성공 뒤에만
  활성 미니게임 일정을 완료 처리한다.
- 게임 종료는 메뉴와 창 닫기 모두 실제 Unity teardown 전에 단일 preparation을 거친다.
  진행 중 세션 작업을 기다린 뒤 MPS Leave/Delete를 먼저 끝내며 전체 정리는 5초를 넘기지
  않는다. 준비 중에는 새 세션 작업과 프레임 기반 상태 진행을 시작하지 않는다.
- Windows Player는 정리 완료 뒤 정상 Application.Quit을 시도한다. Unity 6000.6 네이티브
  teardown이 3초 안에 끝나지 않으면 백그라운드 watchdog이 TerminateProcess를 최후 수단으로
  사용해 사용자가 작업 관리자에서 강제 종료할 필요가 없게 한다.

### 로비·옷장

- 대기방은 Assets/Ignore/Maze 원본을 사용한 철창·감옥 공간이다. 입장 전에는 글자 없는
  16:9 배경과 현재 언어 로고를 표시하고 입장 성공 시 숨기며, 세션 이탈 시 복원한다.
- LobbyCanvas.prefab 비표시와 OnlineBootstrap 활성 override는 사용자가 보존하도록
  지정한 현재 상태다. 해제 요청 전까지 변경하지 않는다.
- 로비에서는 플레이어 장막의 표시와 판정을 끈다. 남쪽 철문 조각은
  LobbyArena/Presentation 아래에 두며 보드 additive 로드 시 다른 로비 프레젠테이션과
  함께 비활성화한다.
- 준비 명단·요약 패널은 사용하지 않는다. 준비 완료는 3D 아바타 머리 위 닉네임을
  초록색으로, 방장은 닉네임 왼쪽의 Modern UI 채운 별 아이콘으로 표시한다. 둘은 로비에서만
  보이고 보드·미니게임 진입 시 기본 상태로 복원한다.
- 준비 안내는 오른쪽 아래에 표시하고 네 명이 모두 준비되면 숨긴다. 준비 버튼은 중앙
  하단, 시작 버튼은 그 아래에 둔다. 저장 경기 복구 시 두 버튼의 폐기/계속 재사용은 유지한다.
- 초대 코드는 중앙 상단에서 기본 마스킹한다. 오른쪽의 보기 아이콘은 누르는 동안만
  원문을 보여 주고, 그 오른쪽의 복사 아이콘은 마스킹 상태로 코드를 복사한다.
- 맵 선택은 옷장 위 보드 설정 버튼으로 여는 별도 팝업에 둔다. 모든 플레이어가 선택
  결과를 보며 호스트만 BoardMapCatalog.Maps 순서로 변경한다. 옷장과 보드 설정은 동시에
  열지 않고 각각 닫기 버튼과 ESC를 같은 경로로 처리한다.
- 새 세션 기본 맵은 forest-graybox다. 저장 경기에서는 저장된 MapId와 ContentVersion을
  우선한다. 표시명은 Forest/숲/森/森林, Maze/미로/迷路/迷宫이다.
- 옷장 순서는 색 → 얼굴 → 모자 → 감정표현 얼굴이다. Face1~15, 없음·Hat1~30을 사용하며
  기존 Face1~3·Hat1~3 저장 ID를 유지한다. 기본 외형은 저장·네트워크·모든 캐릭터 표시에
  반영한다.

### 플레이어 손·감정표현·충돌

- 기본 손은 WhiteHand.prefab 원본의 SimpleFistHand.prefab 변형을 사용하며 왼손은
  visual root X축 미러로 구분한다. 기존 손 앵커·주먹 애니메이션·독립 trigger 히트박스를
  유지하고 모델 자체에는 Collider를 추가하지 않는다. 장착 아이템이 활성화되면 손을 숨긴다.
- T 홀드 원형 휠은 로비·보드에서만 인사·경례·욕설·하트·놀람·항복·부탁·눈가림 8종을
  제공한다. 모션은 2초 동안 서버 권위로 이동을 잠그고 시작·종료를 부드럽게 보간한다.
- 감정표현은 기본 손의 손가락 본과 손 앵커를 사용한다. 인사는 오른손을 두 번 흔들고,
  욕설은 양손 중지만, 하트는 양손 엄지·검지만 편다. 나머지는 열린 손을 지정 위치로 옮긴다.
- 감정표현 얼굴은 8종별로 프로필에 저장하고 서버가 검증·복제한다. 기존 프로필의 미설정
  값은 현재 기본 얼굴로 이관하며 눈가림은 얼굴을 바꾸지 않는다.
- 손 루트 각도는 PlayerAvatarPresentation.prefab의 Player Avatar Presentation Bindings >
  Hand Emote Rotations에서 8종별 월드/1인칭 좌우 Euler와 인사 흔들림 폭을 조절한다.
  최초 schema 이관 뒤 setup은 사용자 값을 덮어쓰거나 불필요하게 프리팹을 재저장하지 않는다.
- 이동 충돌은 플레이어 루트 CharacterController 하나만 담당하며 반경은 0.42m다.
  몸·머리·양손 PlayerHitZone은 trigger 피격 판정 전용이고 장식과 손은 이동을 막지 않는다.
- 보드에서는 전역 일시정지를 제외하고 입력 잠금 중에도 서버가 중력 -24와 접지 속도
  -2를 적용한다. 복구·스왑·리스폰 텔레포트는 수직 속도를 초기화하고 수평 경계 보정은
  낙하 속도를 보존한다.
- 보드 상대 닉네임은 불투명 지형·벽에는 깊이 테스트, 깊이를 기록하지 않는 장막·투명
  장애물에는 비트리거 Collider 시야 판정으로 가린다. 로컬 닉네임과 보드 이탈·미니게임
  상태에는 강제 마스킹을 남기지 않는다.

### 보드 맵·상점

- Forest는 콘텐츠 버전 4, 42칸·49개 일방통행 연결을 사용한다. 시작은 03, 리스폰은
  02, 13, 슬롯별 시작은 03, 09, 14, 19다. authored Terrain·식생·환경 배치는
  사용자 요청 없이 재생성·정규화하지 않는다.
- Forest Terrain은 80×10×73m, 원점 Y -0.12다. 잔디·흙길·48그루 식생을 유지하고
  visual Terrain과 식생에는 Collider를 두지 않는다. 높이맵 갱신은 기존 조형을 평탄화하지
  않으며 런타임 포함용 비활성 참조 placeholder를 유지한다.
- Maze는 콘텐츠 버전 1, 8×8 격자 64칸·80개 연결이다. 시작은 (0,0), 리스폰은
  (2,2) (2,5) (5,2) (5,5), 슬롯별 시작은 네 모서리다. Maze Ground는 160×153m이고
  MazeGroundTerrain.asset만 사용한다.
- Forest·Maze Terrain과 placeholder는 Windows Player 렌더 호환을 위해
  drawInstanced=false를 사용한다. authored 맵 활성 시 레거시 Board Backdrop은 숨긴다.
- Map Authoring과 지도 UI는 방 100개를 지원하고 자유배치 맵에는 레거시 7×7 개요를
  표시하지 않는다. 모든 authored gate와 새 맵 기본 통로 폭은 4m다.
- 열쇠 상점은 blue-house_001, 두 아이템 상점은 house-red_001 visual을 사용한다.
  상점 marker는 회피 오프셋 없이 선택된 BoardTile.WorldCenter에 정확히 놓는다.
- 상점이 처음 나타나거나 위치 revision·좌표가 실제로 바뀌면 Shield VFX를 3초 재생한다.
  아이템 상점은 프로젝트 전용 WaterShield.prefab, 열쇠상점은 AllIn1VfxToolkit의
  Sand Shield를 기준으로 만든 별도 색의 프로젝트 전용 변형을 사용한다. 위치가 같은 구매
  상태 갱신에는 반복하지 않는다.
- 상점 안내 텍스트는 개인 카메라에서 로컬 플레이어를 향하도록 Y축으로만 회전한다.
  상하 각도는 조절하지 않는다.
- 상점의 넓은 조준·상호작용 영역은 Trigger로 유지하고, 실제 집 모델과 같은 메시를 쓰는
  루트 직속 non-trigger Physical Footprint를 별도로 둔다. Visuals 계층은 충돌 없이 유지해
  상호작용 범위가 플레이어나 주사위를 가두지 않으면서 모델 자체는 통과하지 못하게 한다.
- 아이템 상점의 새 5칸 목록은 SpawnWeight 기반 비복원 추출로 구성해 같은 아이템을
  중복하지 않는다. 이전 저장의 복구 목록에 중복이 있으면 기존 고유 항목과 판매 비트 위치는
  유지하고, 빈 자리를 seed 기반의 아직 사용하지 않은 유효 아이템으로 결정론적으로 치환한다.

### 보드 칸·지도 UI

- 월드의 칸 루트·라벨·착지 효과면 Renderer는 숨기고 Collider·Footprint·Topology·효과
  데이터는 유지한다. 칸 종류와 능력은 미니맵·전체 지도 아이콘으로만 표시한다.
- 지도 아이콘은 Modern UI Pack을 직접 바인딩한다. 일반·시작은 일반칸, 리스폰은
  Home Filled, 골드 획득/손실은 노랑/빨강 Money Filled, 피해는 빨강 Add, 특별
  이벤트는 빨강 Warning Filled다. 아이템은 기존 흰색, 회복은 기존 초록색 아이콘을 쓴다.
  칸 아이콘은 현재보다 키우고 칸별 사각 배경은 숨기며, 플레이어 표식은 칸 아이콘보다
  위 렌더 순서에 둔다.
- 유효 출구가 둘 이상인 갈림길의 이동 방향 Arrow Up은 플레이어가 도착하기 전부터
  미니맵·전체 지도에 미리 표시한다.
  지뢰는 월드·미니맵·전체 지도 모두 빨강 Help Filled로 표시하며 설치자만 볼 수 있다.
- 플레이어는 Location Mark Filled를 외형색으로 칠해 칸의 안전 중심에 표시한다. 본인은
  현재 위치를 사용한다. 상대의 전체 지도는 시작 탑뷰 위치에서 시작해 1인칭 카메라로
  직접 확인할 때만 최종 관측 위치를 갱신하고, 보드에서 직접 확인 가능한 동안에는
  미니맵에도 현재 위치를 표시한다. 은신 상대는 새로 관측하지 않는다.
- 전체 지도는 기존 600×600에서 780×780으로 1.3배 확대하고 내부 지도 표면은
  560×560에서 728×728로 함께 확대한다. 전체 지도 플레이어 아이콘만 15×15에서
  30×30으로 두 배 키우고 흰색 Outline을 사용하며, 로컬 미니맵 아이콘 크기는 유지한다.
- 로컬 미니맵은 실제 1인칭 출력 카메라의 수평 시야를 부채꼴로 표시한다. 직접 보이는
  범위는 밝게, 시야 밖과 불투명 오브젝트 뒤는 어둡게 표시하되 칸·칸 아이콘·갈림길
  화살표·열쇠상점 안내 같은 정적 지도 정보는 어두운 레이어 아래 계속 확인할 수 있다.
  플레이어 표식은 이 레이어 위에서 별도로 가시성을 제한한다.
- 상대 위치 관측은 1인칭 진입 시 즉시 한 번 실행한 뒤 unscaled time 기준 정확히 1초마다
  실제 카메라 viewport와 월드 시야 차폐를 함께 검사한다. 새 관측은 정확한 월드 좌표가
  아닌 서버 확정 논리 칸 좌표만 로컬 캐시에 저장한다. 미니맵은 이번 샘플에서 직접 보인
  상대의 캐시 칸만, 전체 지도는 마지막으로 확인한 캐시 칸을 표시한다. 지도 비활성·탑뷰·
  전체 지도 전환 중에는 현재 가시성만 즉시 지우고 마지막 관측 기록은 유지한다.
- 주사위 결과 확정 전과 액션 턴 종료 후에는 칸수를 표시하지 않는다. 확정 후 현재 칸을
  기준으로 삼되 숫자 0은 그리지 않고, 1부터 주사위 합계까지 가능한 모든 방향성 경로와
  순환 경로를 미리 계산해 전체 지도에 표시하며 이동 중에는 같은 숫자를 유지한다.
- authored 첫 경로는 노란색, 추가 갈림길은 하늘색 숫자를 칸 구석에 표시한다. 도달 가능한
  열쇠상점이 있으면 최단경로를 노란 메인 경로로 우선한다. 갈림길 선택 후 불가능한 후보는
  지우고 남은 최단 상점 경로를 메인으로 승격한다.
- 액션 중 위치교환·사망 리스폰으로 말이 권위적으로 재배치되면 소비 칸수는 유지하고 새
  칸을 기준으로 남은 숫자만 다시 계산하며 이전 갈림길 선택 기록을 초기화한다.
- 미니맵·전체 지도에는 현재 칸부터 열쇠상점까지 방향성 최단경로를 노란 점선으로 표시한다.
  상점 칸은 노란색으로 채우고 K 문자나 열쇠 아이콘은 겹치지 않는다. 상점 미배치·현재 칸과
  동일·도달 불가 시 이전 점선을 지운다.
- 월드 상점 경로 광점은 KeyShopRouteHemisphere.prefab의 경로·GUID·루트 fileID를
  유지하되 반구 대신 AllIn1VfxToolkit OrbSparkGlow 단일 반복 파티클을 사용한다.
  지름 약 0.15m의 노란 원형 반짝임이며 Light·Collider·NetworkObject는 두지 않는다.
- M으로 연 전체 지도는 닫기 버튼·ESC·M이 같은 사용자 상태를 닫는다. 자동 턴 개요
  지도에는 닫기 버튼과 팝업 스택 항목을 만들지 않는다.
- 미니맵과 전체 지도 UI는 보드 1인칭 상태에서만 사용할 수 있다. 턴 개요·착지 정산·
  자원 이벤트·격투 관전을 포함한 모든 탑뷰 상태에서는 열려 있던 전체 지도를 닫고 지도 UI를
  표시하지 않는다.
- 미굴림 Ready 주사위는 플레이어가 굴리기 전에 사망·리스폰하면 함께 재배치한다.
  생성·재배치 위치는 상점과 다른 월드 오브젝트에 겹치지 않는 충돌 안전 위치를 사용하고,
  주사위 몸체는 슬롯 고정색 대신 권위 있는 Appearance.BodyColor를 복제해 사용한다.

### 보드 HUD·착지 효과·아이템

- 플레이 중 Canvas UI의 `UnityEngine.UI.Text`와 `TextMeshProUGUI`는 모두
  Normal/Regular 스타일을 사용한다. 이후 setup으로 생성하는 UI도 같은 스타일로 고정하고,
  중앙 프리팹 계약 테스트로 회귀를 막는다. Gift Grab 기지 표지처럼 Canvas가 아닌 월드
  `TextMesh`는 이 규칙에서 제외한다.
- BoardCanvas.prefab의 사용자 배치를 유지한다. 삭제된 Players·Inventory 제목,
  인벤토리 라벨, BoardStatusText, BoardChoiceTimerText, BoardShieldText, 지도 Title·Legend와
  Current Tile·Key Shop Distance 바인딩은 선택 사항이며 setup으로 복원하지 않는다.
- 플레이어 카드의 Key·Gold는 Assets/Ignore/Icon_NCI 아이콘 뒤 숫자로 표시한다. 체력은
  기존 Bar·Fill·Text를 유지한 표시 전용 Slider이며 런타임은 값·상태색·텍스트만 바꾼다.
- 이름·등수·체력·상태를 담는 각 플레이어 카드는 권위 있는 Appearance.BodyColor를 어둡게
  한 배경을 사용한다. 네 카드를 감싸는 공통 Player State Panel 배경은 완전히 투명하게
  유지한다.
- 인벤토리 3칸에 유효 아이템이 없으면 아이템 선택창을 열지 않고 미사용을 즉시 확정한다.
  아이템이 있으면 기존 선택 패널을 사용하되 개인 상태 텍스트에 선택 상세를 표시하지 않는다.
- 효과 배치 비율은 골드 획득:손실:아이템:회복:피해:특별 이벤트=5:5:2:2:2:1이다.
  회복은 +20/+10, 피해는 -40/-20 균등 분할이고 Respawn은 제외, Start는 포함한다.
  서버 시드로 결정론적으로 배치하며 저장 복구 호환 버전은 4다.
- 특별 이벤트 결과는 BoardEventPopupPanel에서 대상·자원·결과를 모든 플레이어에게 먼저
  3초 동안 보여 준다. 서버 상태 소유 non-dismissible UI라 닫기·ESC·로컬 팝업 스택에
  연결하지 않는다.
- 플레이어가 아이템 피해로 보드 체력을 0으로 만들거나 보드 격투에서 상대를 탈락시키면
  서버가 모든 클라이언트의 BoardKillFeedPanel에 `{공격자}이(가) {피해자}을(를) 처치`를
  전송한다. 두 이름만 권위 있는 Appearance.BodyColor로 표시하되 어두운 색은 텍스트
  가독성만 보정한다. 환경·자해 사망은 제외하며 같은 프레임의 연속 처치는 로컬 큐에서
  한 건씩 순서대로 보여 준다.
- 자원 이동량이 있으면 뺏기는 플레이어 정면 카메라에서 Coin/Key가 머리 위로 올라가고,
  받는 플레이어 정면에서는 위에서 내려온다. 숫자·텍스트는 표시하지 않는다. 이동량이 0이면
  결과 팝업만 3초 표시한다. 모델은 BTM Coin.prefab·Key.prefab의 충돌 없는 로컬 래퍼다.
- 주사위 이동 뒤 탑뷰 착지 정산에서는 서버 확정 결과를 대상 플레이어 머리 위 authored
  월드 Canvas로 정확히 1초 표시한다. 골드·체력은 지도 아이콘과 실제 부호량, 아이템은
  지도 선물 아이콘과 `+ 아이템명`을 사용한다. 특별 이벤트는 텍스트 대신 플레이어 위치에
  공용 LightningStrike를 한 번 재생한다. 복제 순서상 카메라·아바타가 늦게 준비되면 유효
  시간 안에서 표시 성공까지 같은 revision을 재시도한다.
- Pistol·Sniper는 화면 조준선 기준 hitscan이고 탄환 오브젝트 없이 짧은 tracer만 표시한다.
  실제 피해 가능한 상대를 조준할 때만 조준선을 빨간색으로 바꾸며 Sniper 확대는 사용하지 않는다.
- Grenade 선택 중에는 플레이어 발높이에 맞춘 로컬 최대 사거리 원만 표시하고 궤적선은
  표시하지 않는다. 네 장착 아이템 visual은 WeaponStylizedPack, 9종 아이콘은
  Assets/Ignore/AIImage/Icons를 사용한다. 비행 표시는 안정 ID별 50ms 지연 보간과 최대
  100ms 외삽을 사용하고, 제거 snapshot도 표시 시각이 도달할 때까지 tombstone으로 유지해
  폭발 직전 프레임이 끊기지 않게 한다.

### 공용 VFX

- 원본은 Assets/Ignore/AllIn1VfxToolkit v2.32다. 추적되는 전용 VFX는 authored prefab으로
  만들고 외부 helper, Collider, NetworkObject, Distort/GrabPass 의존을 제거한다.
- 서버가 의미 이벤트를 확정한 뒤 각 클라이언트가 로컬 VFX를 재생한다. 복수 이벤트를
  유실하지 않고 재접속·재진입 시 과거 revision을 재생하지 않는다.
- Race·Wrong Way·Minefield·Red Light Green Light 도착은 초록·노랑 두 burst의 풀링
  one-shot ArrivalFireworks를 사용한다.
- 보호막, 미니게임 라운드별 로컬 위치 강조, 보드의 일반 하이라이트와 아이템 상점
  등장·재배치는 프로젝트 전용 WaterShield.prefab을 공용한다. 열쇠상점 하이라이트는
  Sand Shield 기반의 별도 색 변형을 사용한다. 플레이어 보호·보드 탑뷰·미니게임 위치 강조
  요청은 각각 독립적으로 유지하고 최종 표시만 합산한다.
- 플레이어 장막은 authority Collider와 visual을 분리한다. 자기 장막만 표시하며 파랑은
  통과 가능, 빨강은 통과 불가다. 숨길 때 Renderer와 ParticleSystem을 함께 끈다.

### 미니게임

- 보드의 미니게임 준비 화면은 왼쪽 위에 네 플레이어의 준비 상태를 세로로, 왼쪽 아래에
  게임별 조작을 최대 4행으로 표시한다. 조작 아이콘은
  `Assets/Ignore/Input Sprites for TextMesh Pro/all input icons same size.asset`를 각 TMP 행에
  직접 바인딩하며 현재 실제 지원 범위인 키보드·마우스 입력만 안내한다.
- 준비 화면 오른쪽 위의 넓은 미리보기는 authored `RawImage`와 APIOnly `VideoPlayer`를
  사용한다. `ScheduledMinigameId`와 같은 16칸 `VideoClip` 배열은 현재 비워 두며 이후
  Inspector에서 게임별 영상을 직접 할당한다. 클립이 없거나 게임이 공개되기 전이면 안전하게
  정지하고 placeholder를 표시한다. 영상 아래 설명은 당분간 `---`로 통일한다. Notion과
  런타임은 자동 동기화하지 않으며, 설명 확정·반영 요청 시 최종 문구를 코드/프리팹에 옮긴다.
- 게임은 항상 4인 구조이며 첫 시작과 각 라운드 시작마다 서버 기준 공통 HUD에서
  3 → 2 → 1을 표시한다. 같은 시간 동안 로컬 플레이어를 WaterShield.prefab으로
  강조한다. Snowy Spin·Bouncing Balls 전용 표현도 같은 VFX를 균일 스케일로 사용한다.
- 공격 입력은 기존 한손 공격 모션, 밀기는 양손 전진 모션을 재생한다. 유효 시도는 대상이
  없어도 쿨다운당 한 번 애니메이션을 재생하고 실제 피격 모션·VFX는 대상이 있을 때만 낸다.
- 화면 HUD에는 현재 판단에 필요한 입력·신호·점수만 둔다. Solo의
  DEVELOPER SOLO TEST 패널은 Editor 전용이며 Player 빌드에는 포함하지 않는다.
- Stable Footing은 8×6이다. 공개 전 3초 동안 세 심벌을 0.5초마다 서로 다른 슬롯으로
  섞고 마지막 배치에서 정답 하나만 초록색으로 공개한다. 오답 발판은 일시 하강하고
  복원 뒤 사이클당 4개를 영구 제거한다.
- Territory Paint는 호스트 슬롯 0을 화면 기준 왼쪽 위에 두고 왼쪽 아래 → 오른쪽 아래 →
  오른쪽 위 순으로 반시계 배치한다. 바닥 칠 색은 각 슬롯의 권위 있는 Appearance.BodyColor를
  사용하고 Unity Plane의 반전된 X/Z UV를 보정해 상하좌우 입력과 칠 위치를 일치시킨다.
- Balloon Blow는 기침 cooldown 동안 기존 쓰러짐 자세를 사용하고 풍선 입력을 허용하지
  않는다. 풍선이 터질 때 도착 ArrivalFireworks VFX를 재사용하고 `balloon` 큐를 재생한다.
- Red Light Green Light는 상단의 빨강·초록 3등만 사용한다. 단계는 초록3 → 빨강1 →
  빨강2 → 빨강3이며 완전한 빨강에서만 이동 금지·위반 판정을 한다. 전환 전 단계는 서버
  시드 0.1~2.0초, 네트워크 보정 유예는 0.15초다. 빨간불 위반으로 체력이 감소할 때마다
  플레이어 위치에 LightningStrike를 재생하고 `lightning` 큐를 낸다. 한 복제 갱신에 여러
  위반이 합쳐져도 증가량만큼 피드백을 보존한다.
- Cliff Barrage 투사체는 20Hz 권위 스냅샷 사이를 제한 속도 예측과 지수 보간한다.
  투사체·레이저 빈도는 10초마다 20%씩, 5단계·최대 2배까지 증가한다.
- Race의 A/D 교대 완주 목표는 200회다.
- Minefield 탐지 반경은 3m, 탐지 시간은 0.75초다. 탐지 중 이동을 잠그되 지뢰·결승선·
  압사 판정은 유지하고 지뢰 접촉 시 서버 확정 CartoonExplosion을 재생한다.
- Tag Chase 충돌은 authored 내부 벽 16개·기둥 14개의 XZ bounds와 기존 외곽 clamp를
  사용한다. 술래 1인칭에는 Running 단계에만 공용 HUD의 중앙 Tagger Aim을 표시한다.
- Sequence Memory의 A/S/D는 도/미/솔이며 Bell 7 원음 G5를 0.6674199/0.8408964/1.0
  피치로 재생한다. 큐 선행 무음 0.21초를 건너뛰고 동시 재생은 8개로 제한한다.
- 15종의 플레이어 스폰 표시는 숨긴다. Bomb Passing은 중앙 블록만 숨기고 소환 링은
  유지한다. Snowy Spin·Bouncing Balls 중앙 원은 숨긴다.
- Minefield·Wrong Way·Race는 로컬 플레이어 중심 개인 카메라를 사용한다. 공용 정탑
  카메라 계열 7종과 Arena·보드 격투 관전은 SharedCameraFraming의 정탑 기준 35°를
  사용하며 플레이 중 reframe·zoom·shake를 하지 않는다.
- 미니게임 표시 탑은 플레이를 마친 항목을 제거하지 않고 열린 상태와 게임명을 유지해
  이전에 어떤 미니게임을 플레이했는지 계속 확인할 수 있게 한다.
- 미니게임 결과 발표의 플레이어 닉네임은 권위 있는 Appearance.BodyColor로 표시하되
  어두운 색은 텍스트 가독성만 보정한다.
- Wrong Way는 사용자가 조절한 계단 높이를 보존하고 그 높이에 맞춰 플레이어 시작·배치
  위치를 조절한다.
- Arena Combat 비활성화 시 자신이 소유한 로컬 1인칭 presentation을 한 번만 해제해
  보드 복귀 뒤 얼굴 Sprite나 월드 모델이 카메라를 가리지 않게 한다.

### 오디오

- 직접 BGM은 로비·보드·공용 미니게임만 사용하고 나머지는 fallback을 사용한다.
- 보너스 준비음은 공개 전 2초만 재생하고 공개·일시정지에서 중지하며 duck하지 않는다.
- 보드 발소리는 짧은 원샷 11개를 shuffle 재생한다.
- Sequence Memory 음계는 전역 풀 사운드 큐 하나에서 독립 피치 요청으로 재생한다.
- `balloon`과 `lightning`은 사용자 제공 MP3를 실제 SoundCue와 SoundLibrary에 연결한다.
  여러 플레이어의 동시 팝·위반은 4개 voice 한도 안에서 같은 프레임에도 누락하지 않는다.

## 현재 검증 기준

- 장기 유지 EditMode는 핵심 상태 전이, 정확한 시간 경계, 서버 권한·거부 조건,
  순위·보상, 시드 결정론, 저장 복원·손상 방지, 실제 씬·프리팹 직렬화 계약에 한정한다.
- 구현 완료용 시각 세부값, 단순 상수·getter·DTO, Testbed/Solo 내부 구현, 중복 씬·입력
  사례 테스트는 제거하거나 상위 계약과 표 기반 테스트로 통합한다.
- 2026-10-09 종료 선행 정리·시간 상한·Windows watchdog과 파괴된 아바타 참조 방지를
  반영했다. 종료 상태 전이 표적 EditMode 10/10이 통과했고 전체 EditMode는 509개 중
  506개가 통과했다. Windows Development Mono x64 빌드가 성공했으며 실제 창 닫기
  스모크에서 정리 결과 Completed, 종료 코드 0, 3.026초 내 프로세스 종료를 확인했다.
  같은 빌드에서 watchdog만 끈 A/B 실행은 12초 뒤에도 프로세스가 남아 테스트가 종료했으므로
  sleeping thread가 원인이 아니라 Unity 6000.6 네이티브 teardown 잔류임을 재확인했다.
  Player 로그에는 기존 PlayerAvatarVisual NullReference와 MPS StopAsync-after-dispose가
  재발하지 않았다. 남은 실제 계약 실패는
  Forest visual Terrain의 TerrainCollider, Bomb Passing 환경 Collider 소유권, 전용
  authored MinigameResultCanvas 출처 3건이며 Unity 콘솔 오류는 0건이다.
- 2026-10-09 보드 HUD·킬 피드·지도·주사위·수류탄·Shield 상점 강조와 미니게임 탑·결과·
  Wrong Way 피드백을 반영했다. 관련 표적 EditMode는 79개 중 78개, 전체 EditMode는 515개 중
  512개가 통과했다. 실패는 위의 기존 3건뿐이며 새 회귀는 없다. Windows Development Mono
  x64 빌드는 `Builds/Windows-Development-20261009-205102/MazeParty.exe`로 성공했고 빌드 후
  Unity 콘솔의 프로젝트 오류는 0건이다.
- 2026-10-09 미니게임 준비 화면의 세로 준비 상태·TMP 입력 안내·선택형 영상 미리보기·설명
  placeholder와 전 플레이 Canvas 텍스트 Normal/Regular 정책을 반영했다. 요청에 따라 전체
  EditMode는 실행하지 않았고, `BoardUiPrefabTests`와 UI 글꼴 정책을 합친 표적 테스트
  17/17만 통과했다. 스크립트 컴파일과 최종 Unity 콘솔 오류는 0건이다.
- 2026-10-10 전체 지도 확대·플레이어 아이콘 2배/흰색 Outline과 로컬 미니맵 카메라
  부채꼴·장애물 가림·1초 논리 칸 관측 캐시를 반영했다. 요청에 따라 전체 EditMode는
  실행하지 않았고, `BoardPlayerMapKnowledgeTests`, `BoardMinimapProjectionTests`,
  `BoardMapUiPrefabTests` 표적 테스트 14/14만 통과했다. 스크립트 컴파일과 Unity 콘솔
  오류는 0건이다.
- 2026-10-10 Territory Paint 스폰·색·UV, 이벤트 3초, 탑뷰 지도 차단, Balloon Blow
  팝/기침, Red Light Green Light 시간·번개, 수류탄 보간, 상점 중복 방지와 1초 착지
  피드백을 반영했다. 요청에 따라 전체 EditMode는 실행하지 않았고, 정확 시간 경계·결정론·
  저장 복구·씬/프리팹 계약을 포함한 표적 EditMode 31/31만 통과했다. 스크립트 컴파일과
  최종 Unity 콘솔 오류는 0건이다.

## 남은 TODO

- Windows Development 빌드는 현재 상점 Physical Footprint 메시를 자동 조리하지만 향후 Unity
  버전에서는 pre-baked collision을 요구한다는 경고를 낸다. Unity 업그레이드 전 전역
  `bakeCollisionMeshes` 사용 또는 전용 충돌 메시 asset 생성 방식을 확정한다.
- 공통 카운트다운 숫자는 값이 갱신되지만 196×196 Rect보다 현지화 폰트 preferred height가
  커 Vertical Overflow = Truncate에서 보이지 않는 것으로 진단됐다. 사용자 요청에 따라
  아직 폰트 크기와 프리팹 시각값은 변경하지 않았다.
- 보드 설정은 현재 맵만 지원한다. 추후 턴 수·열쇠 가격·미니게임 골드 지급량을 추가하고
  SessionSnapshot, MPS 속성, 저장 복원과 서버 검증을 함께 확장한다.
- Forest Generated Ground에서 visual-only TerrainCollider를 제거하고, Bomb Passing
  환경 Collider를 씬 또는 gameplay core 권위로 옮기며, 최종 결과 UI를 전용 authored
  MinigameResultCanvas 하나로 통일한다.
- 15종 캡처 피드백에 따라 게임별 에셋·배치·카메라를 조정한다. 우선 후보는 Bouncing Balls
  하단 골대·방어바 프레이밍, Race 하단 여백, Arena·보드 격투 실제 관전 화면이다.
- 조기 종료·시간 종료·공동 순위 조합, 수동 조작감, VFX 투명 정렬·Bloom·Soft Particle·
  Reduce Flashes, 제어된 지연·패킷 손실, 별도 PC 장시간 soak, Windows IL2CPP Release를
  추가 검증한다.
- 미로 레이아웃 변경 후 흙길을 다시 칠하는 미로 전용 바닥 갱신 도구를 추가한다.
