# 보드 월드 프리팹 편집 안내

## 편집 위치

| 대상 | 프리팹 |
| --- | --- |
| 일반 칸 2종 | `TileNormalA`, `TileNormalB` |
| 시작·리스폰 칸 | `TileStart`, `TileRespawn` |
| 열쇠 상점 | `KeyShop` |
| 아이템 상점 2종 | `ItemShop1`, `ItemShop2` |
| 플레이어별 이동 제한 벽 | `PlayerBoundaryWall` |
| 보드 배경 바닥 | `BoardBackdrop` |
| 묘비·열쇠 상점 안내 점선 | `BoardTombstone`, `KeyShopRouteHemisphere` (기존) |

이 폴더의 프리팹을 더블클릭해 Prefab Mode에서 편집한다. 재질은
`Assets/MazeParty/Board/Materials`에 있다. 주사위 외형은 인접한
`../Dice/D12WorldDieVisual.prefab`, Canvas UI는 `../UI`에서 편집한다.
공용 플레이어 외형·1인칭 손·장착 아이템 모델은 이번 범위에서 제외했다.

## 에셋 교체 방법

- **상점**: `Visuals/Body`의 MeshFilter·Renderer를 교체하거나 `Visuals` 아래에
  모델 프리팹을 넣고 기존 Body를 제거한다. `Interaction Target`의 Collider와
  `KeyShopWorldTarget` / `ItemShopWorldTarget`은 구매 판정이므로 유지한다.
  열쇠 상점 받침대 외형은 `Visuals/Base`, 받침대 판정은 `Base Interaction Target`이다.
  판정 Collider를 바꿀 때는 `BoardShopVisual.interactionColliders` 연결도 갱신한다.
  `World Label`은 폰트·크기·위치, `Top View Highlight`는 탑뷰 강조 외형이다.
  아이템 상점 문구는 루트 `BoardShopVisual`의 Available/Sold Out Text에서 편집한다.
  `{0}`은 상점 번호다. 열쇠 상점 기본 문구는 World Label의 Text에서 편집한다.
- **칸**: 루트 MeshFilter·Renderer의 에셋/재질을 변경할 수 있다. 자식으로 모델을
  추가한다면 기존 바닥 Renderer만 숨기고, 필요하면 `BoardTile.landingEffectRenderer`를
  새 Renderer로 연결한다. 루트 BoardTile·BoxCollider와 논리 칸 크기 8m는 유지한다.
  현재 루트 스케일은 7.72 × 0.2 × 7.72이며 자식 모델 배율도 영향을 받는다.
  좌표·칸 종류·Room Label의 좌표 문구는 씬/setup이 지정한다.
  착지 효과가 활성화된 동안 연결 Renderer의 색은 게임 상태 색으로 덮어쓴다.
- **벽**: 기본은 단위 Cube이며 실제 위치와 크기는 현재 칸/방향에 맞춰 런타임에서
  배치한다. 루트 BoxCollider를 유지하고 모델 자식에는 추가 Collider를 넣지 않는다.
  상태 색이 필요한 Renderer는 `BoardBoundaryWallVisual.stateRenderers`에 연결한다.
  통과 가능/불가 색상도 이 컴포넌트에서 변경한다. 장식 Renderer까지 모두 해당
  플레이어의 표시 정책을 따른다. 벽 높이/두께는 NetworkPlayer의
  `PlayerBoardBoundaryWalls`에서 설정한다.
- **배경**: Mesh·Material을 교체할 수 있으며 게임 충돌용 Collider는 추가하지 않는다.
- **묘비·경로·주사위**: 기존 바인딩과 Collider 계약을 유지하며 외형을 편집한다.

## 연결과 재설정

`Assets/MazeParty/Resources/MazeParty/Board/BoardWorldPrefabs.asset`이 런타임
상점/벽 프리팹을 참조한다. 씬의 상점 표시기와 NetworkPlayer에도 이 카탈로그가
직렬화되어 있다. 동적으로 생성되는 테스트 플레이어는 같은 Resources 카탈로그를 읽는다.
외형이 빠졌을 때 런타임에서 임시 Primitive를 만들지 않는다.

`MazeParty > Board > Install World Prefabs`는 Board·BoardFlowTestbed 씬과
NetworkPlayer의 연결을 설치한다. 기존 프리팹과 재질 디자인은 덮어쓰지 않는다.
보드 전체 setup도 같은 칸·상점·배경·벽·주사위 프리팹을 재사용한다.
씬에서 개별 외형 override를 만들면 프리팹 변경을 가릴 수 있으므로 공통 디자인은
프리팹 원본에서 수정한다. 통로 Gate·카메라·게임 규칙 컴포넌트는 논리 연결로 유지한다.
