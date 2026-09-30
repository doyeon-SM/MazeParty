# 미니게임 오브젝트 에셋 요구사항

## 조사 결론

- 15종 미니게임의 비 UI 프리팹을 전수 확인했다.
- 현재 환경과 핵심 오브젝트의 `MeshFilter`는 모두 Unity 기본 primitive mesh를 사용한다.
  외부 FBX/모델 프리팹이 적용된 핵심 3D 오브젝트는 없다.
- 공용 플레이어 프레젠테이션, UI, 재질, `CartoonExplosion`·`HitSpark`·`TaggerAura`
  등의 VFX는 이미 적용되어 있으므로 이번 에셋 수집 대상에서 제외한다.
- 외부 패키지와 생성 에셋은 모두 `Assets/Ignore` 아래에 보관하고 별도로 공유한다.
  Git 추적 경로로 옮기지 않는다.

## 적용 계약

- 현재 씬 배치, UI 앵커와 프리팹 바인딩을 원본으로 유지한다.
- 모델은 기존 프리팹의 visual child 또는 `Art Replacement Anchors` 아래에 넣는다.
- 외부 모델에는 `Collider`, `Rigidbody`, `NetworkObject`, `NetworkBehaviour`를 추가하지
  않는다. 기존 authority collider와 판정 root를 그대로 사용한다.
- Unity 단위는 1 unit = 1m, 바닥 접점은 local Y=0, 전방은 local +Z를 기본으로 한다.
- 같은 형상은 하나의 mesh와 재질 변형을 공유한다. 슬롯별 복제 모델을 따로 만들지 않는다.
- setup을 다시 실행해 사용자가 조정한 디자인을 덮어쓰지 않는다. 계약 테스트가 현재
  디자인과 충돌하면 디자인이 아니라 테스트를 수정한다.
- 다음 제작 단계는 아래 footprint에 맞춘 **시각용 바닥만** 생성한다. 기존 판정 collider,
  타일 root, 페인트 UV, 카메라와 게임 상태 바인딩은 변경하지 않는다.

## 공용으로 먼저 확보할 에셋 묶음

### A. 경기장 모듈 키트

- 낮은 경계벽·난간·펜스 직선/코너 모듈
- 시작 게이트, 결승 아치, 깃발과 체크무늬 리본
- 원형·사각 스폰 마커 데칼, 중앙 마커 데칼
- 무대 podium, backdrop frame, trim, 스포트라이트와 장식등

### B. 위험물 키트

- 납작한 지뢰, 압착기/크러셔, 회전 경광등
- 만화풍 시한폭탄과 심지
- 레이저 발사기 하우징, 포탄/캐논볼

### C. 게임 소품 키트

- 계단 블록, 안전 타일과 심벌 인서트
- 풍선과 펌프 스테이션
- 선물 상자와 리본
- 기억 입력 콘솔과 A/S/D 버튼 캡
- 패들/실드, 골 프레임, 공
- 눈덩이, 빙판 가장자리와 눈더미
- 감시 인형/로봇과 2등식 신호탑

### D. 자연 배경 키트

- 바위·절벽면·갈라진 암석·눈 덮인 바위
- 나무·관목·풀·버섯·꽃
- 산업 케이블·경고 표지·경고등

## 게임별 필수 오브젝트

### 1. Minefield

- **필수:** 크러셔 1개, 지뢰 visual 1종(최대 20개 재사용), 경광등 1종×4,
  시작·결승 표식, 측벽 모듈.
- **변형:** 지뢰 상태용 점멸 재질 1개, 경광등 red lens, 크러셔 피스톤/롤러.
- **고정 계약:** 크러셔 root trigger와 `MinefieldCrusher`, 지뢰의 판정 반경 1.05m,
  `Siren Red Lens` Renderer와 `Siren Red Light` Light 이름을 유지한다. 지뢰 visual에는
  collider를 넣지 않는다.
- **바닥 footprint:** 20×44m, 3×12 판정 그리드.

### 2. Wrong Way

- **필수:** 계단 tread 1형×200 인스턴스, 4색 재질, 레일 1형×5 경계,
  결승 아치 1개와 깃발 2개, 시작·종료 platform trim.
- **고정 계약:** `Step 01`~`Step 50` 직접 child 구조를 4레인 모두 유지한다.
  계단 visual에는 collider를 넣지 않고 수학 기반 러너 경로를 유지한다.
- **바닥 footprint:** backdrop 12.05×45.2m. 각 레인은 50계단, 길이 약 36m,
  총 상승 12m, step 폭 1.45m·깊이 0.72m·높이 증가 0.24m.

### 3. Red Light / Green Light

- **필수:** 회전 감시 인형/로봇 1개, red/green 2등식 신호탑 1개,
  시작·결승 표식, 외곽 펜스.
- **변형:** 감시자 정면/후면이 명확한 silhouette, 두 emissive 신호 재질.
- **고정 계약:** `Observer Head` 회전 pivot과 이름, `Green Signal`·`Red Signal`
  Renderer와 named Light를 유지한다. visual prefab에는 collider를 넣지 않는다.
- **바닥 footprint:** 20×44m.

### 4. Stable Footing

- **필수:** 타일 frame/surface 1형×48, Cross/Circle/Square 심벌 인서트 3종,
  Safe Symbol Display 1개, 하부 지지대 모듈.
- **고정 계약:** `Tile Surface`와 세 심벌 child 이름, 48개 tile anchor를 유지한다.
  tile root의 기존 authority collider는 유지하며 제거된 타일과 함께 전체 anchor가 꺼져야 한다.
- **바닥 footprint:** active grid 14.4×19.2m, 받침 영역 16.4×21.2m,
  6×8 타일, 간격 2.4m, 타일 면 2.16×2.16m.

### 5. Balloon Blow

- **필수:** 풍선 1형×4색, knot 1형, 펌프/스테이션 1형×4색,
  무대 backdrop·front trim·간판.
- **변형:** 풍선 wobble, 펌프 레버와 호스. 조명·깃발·관객 소품은 후순위다.
- **고정 계약:** `Balloon Body`·`Balloon Knot` child 이름을 유지한다. root가
  0.22~1.7 균일 scale되고 pop 시 두 child가 비활성화된다. authority floor collider는
  기존 씬에 둔다.
- **바닥 footprint:** 15.5×9m.

### 6. Gift Grab

- **필수:** 선물 1형(씬에서 최대 19개 재사용), 포장 재질 3~5종,
  base pad 1형×4색, 낮은 외곽 림/경계 모듈.
- **고정 계약:** 선물 visual은 중앙 pivot, 지름 약 0.8~0.9m 이하, collider 없음.
  `Gift Visual` anchor를 유지한다. base는 지름 3.5m의 색 표시이며 collider가 없다.
- **바닥 footprint:** visual 18×18m, 실제 플레이 영역 16×16m.

### 7. Territory Paint

- **필수:** 연속 난간 4면, 시작 마커 데칼 4개, arena trim.
- **바닥 단계 필수:** 0~1 UV를 갖는 단일 paint surface mesh 1개. 96×96 런타임
  paint texture가 root Renderer에 표시되어야 한다.
- **고정 계약:** paint surface와 장식에는 collider를 넣지 않고 기존 authority collider를
  사용한다.
- **바닥 footprint:** paint surface와 arena 모두 18×18m.

### 8. Tag Chase

- **필수:** 시야 차단물 visual 2~4형을 4개 위치에 배치, 외곽벽 4면,
  tagger marker 1개와 runner marker 3개.
- **고정 계약:** 장애물 root collider와 2.4×3.3×2.4m AABB, 위치·축 회전을 유지한다.
  외부 모델 collider는 제거하고 기존 root 아래 visual child로 넣는다.
- **바닥 footprint:** 24×20m. 장애물은 중앙 기준 (±3.9, ±3.9)에 위치한다.

### 9. Race

- **필수:** 결승 아치/리본 1개, 시작 gate 1개, 측면 rail 2열과 후면 rail 1열,
  출발 marker 4개, lane divider 3개.
- **고정 계약:** `RaceTrack` root와 BoxCollider를 유지한다. 장식 collider는 제거하고
  기존 authority wall과 겹치게 배치한다.
- **바닥 footprint:** 10.8×40m, 4레인, 레인 폭 2.4m.

### 10. Sequence Memory

- **필수:** 진행 NPC 1개, NPC podium 1개, 입력 station 1형×4색,
  A/S/D 버튼 캡 3종, 무대 backdrop과 trim.
- **변형:** 버튼 점등을 실제로 사용할 경우 station마다 3개, 총 12개 Renderer binding이
  추가로 필요하다.
- **고정 계약:** NPC floor pivot과 audio/VFX anchor를 유지하고 collider를 넣지 않는다.
  station 지름은 약 1.35m이며 collider가 없다.
- **바닥 footprint:** 15.5×10m.

### 11. Bouncing Balls

- **필수:** 공 1형×3 인스턴스, 이동 패들/실드 1형×4색, goal frame 1형×4색,
  외곽 wall/corner 모듈과 center marker.
- **고정 계약:** 공·실드는 수학 기반 상태가 Transform을 직접 이동하므로 visual에
  collider를 넣지 않는다. `shieldTransforms`·`shieldRenderers` 4개와
  `ballTransforms`·`ballRenderers` 3개 바인딩을 유지한다.
- **바닥 footprint:** 정사각 field 16.4×16.4m. goal 폭 6.2m, shield 폭 2.2m.

### 12. Bomb Passing

- **필수:** 만화풍 시한폭탄 1개, fuse와 warning lens, 중앙 pedestal/ring,
  spawn marker 4개, 낮은 외곽 wall 4면.
- **고정 계약:** `Bomb` root, `Bomb Fuse`, `Bomb Warning Light`와 기존 Light binding을
  유지한다. 폭발 VFX는 이미 별도 프리팹으로 적용되어 있으므로 모델에 넣지 않는다.
- **바닥 footprint:** 외곽 18.2×18.2m, 안쪽 play floor 16.2×16.2m.

### 13. Snowy Spin

- **필수:** 눈덩이 1형×4색, roll cap/spot, 원형 ice rim, center marker,
  눈더미·빙판 파편·눈 덮인 바위.
- **고정 계약:** 4개 player ball visual에는 collider가 없어야 하며 Transform을 런타임
  상태가 직접 이동한다.
- **바닥 footprint:** 지름 16m 원형 ice arena, player spawn 반지름 4.5m.

### 14. Arena Combat

- **필수:** arena boundary/ring 모듈 4면, corner/post 또는 wall trim,
  spawn marker 4개, 경기장 조명·관중 장식.
- **고정 계약:** 기존 `Arena Floor`와 네 `* Boundary` collider를 유지한다.
  fighter visual은 새 모델을 만들지 않고 authoritative network avatar를 재사용한다.
- **바닥 footprint:** 18×18m.

### 15. Cliff Barrage

- **필수:** 포탄 visual 1형×최대 5 pool slot, laser emitter housing 1형×최대 2,
  절벽 edge/face 모듈, aim/spawn marker decal.
- **선택:** launcher/cannon 1~2개, 갈라진 바위·낙석·crater 장식.
- **고정 계약:** projectile와 laser root는 pool이 이동·활성화하므로 visual에 collider를
  넣지 않는다. 기존 warning/firing beam과 VFX를 유지한다.
- **바닥 footprint:** 16×16m cliff platform, 아래 낙하 연출 영역은 별도 visual이다.

## 현재 `Assets/Ignore` 재사용 후보

- `Maze/Prefabs`: `Wall_1M/2M/3M`, `Pilar`, `Wall_Light`, `Wall_Lamp`, `Switch`,
  `Floor_1M/2M/3M`. Minefield·Tag Chase·무대/경기장 trim에 적합하다.
- `Polytope Studio/Lowpoly_Village`: Fence 01~03, Gate 01, Wooden Bridge.
  Wrong Way·Race·Red Light/Green Light의 rail과 gate 후보로 사용한다.
- `Polytope Studio/Lowpoly_Environments`: 바위·나무·관목·풀·꽃·버섯.
  야외 arena와 Cliff Barrage 배경 장식에 사용한다.
- `Pack_PartyCharacters`: Sequence Memory 진행 NPC 후보. Observer는 머리 회전 pivot이
  명확해야 하므로 별도 인형/로봇 모델이 더 적합하다.
- `nappin/WeaponStylizedPack`: Cannon은 Cliff Barrage launcher 후보다. Granade/Dynamite는
  임시 hazard 후보일 뿐, Bomb Passing 폭탄과 Minefield 지뢰는 전용 silhouette를 권장한다.

## 새로 확보해야 하는 핵심 에셋

현재 Ignore 폴더에 적합한 전용 모델이 없는 우선 항목은 다음과 같다.

1. 지뢰·크러셔·경광등 세트
2. 감시 인형/로봇과 2등식 신호탑
3. 50계단용 modular tread와 안전 타일/glyph 세트
4. 풍선·펌프 스테이션
5. 선물 상자 변형과 player base
6. 기억 입력 콘솔과 A/S/D 버튼
7. Bouncing Balls용 공·패들·goal frame
8. Bomb Passing 전용 시한폭탄
9. 눈덩이·ice rim·눈 장식
10. laser emitter와 포탄, modular cliff edge

