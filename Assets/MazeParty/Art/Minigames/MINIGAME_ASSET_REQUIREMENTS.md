# 미니게임 에셋 적용·잔여 요구사항

## 최종 결론

- 추가된 `Pandazole_Ultimate_Pack`, `FreeLowpolyScifiObjects`,
  `Fantasy Lowpoly Pack (Demo)`와 기존 Ignore 에셋을 공용화하면 15종 미니게임을
  구현하는 데 **새로 구매·제작해야 하는 필수 3D 모델은 0개**다.
- 외부 원본과 파생 URP 재질·결합 메시는 모두 `Assets/Ignore` 아래에 유지하고 별도로
  공유한다. 게임 프리팹은 같은 GUID의 Ignore 원본을 참조한다.
- 외부 모델의 Collider는 사용하지 않는다. 기존 authority collider, 네트워크 root,
  런타임 Renderer/Light 바인딩과 현재 씬·UI 앵커를 유지한다.
- 이번 단계에서는 에셋 변경 계약만 간소화해 검사한다. 전체 EditMode와 4인 플레이
  테스트는 별도 요청에서 진행한다.

## 확정된 예외 사항

- **Red Light / Green Light:** 감시인형은 사용하지 않는다. 씬과 런타임의 Observer
  참조를 제거하고 신호탑만 상태를 전달한다.
- **Balloon Blow:** 펌프·스테이션은 사용하지 않는다. 풍선은 플레이어 입 위치를
  따라가며 입으로 부는 동안 O자 입 모양으로 표시한다.
- **Sequence Memory:** 진행 NPC용 새 캐릭터는 만들지 않는다.
  `PlayerAvatarPresentation.prefab`을 visual-only로 재사용하고, 이름표·1인칭 표현·
  히트박스는 비활성화한다.
- **Wrong Way / Race:** 서로 다른 결승 아치를 만들지 않고
  `SharedFinishGate.prefab` 하나를 함께 사용한다.

## 현재 적용된 공용 에셋

| 공용 프리팹/표현/메시 | Ignore 원본 | 현재 적용 대상 |
|---|---|---|
| `SharedFinishGate` | Polytope `PT_Modular_Gate_Wood_01` | Wrong Way 결승, Race 결승 |
| `SharedOutdoorFence` | Fantasy Demo `fence` | Red Light / Green Light 4면 경계 |
| `SharedNatureGroundTile` | Pandazole `TileGround_01` | Stable Footing `Tile Surface`의 nested visual에만 사용 |
| `SharedNatureGroundCentered` | `TileGround_01` centered 파생 메시 | Wrong Way 각 Lane의 `Start Platform`, `Step 01~50`, `Finish Platform` 총 52개 surface anchor에 직접 사용 |
| `NatureGroundGrid_*` 결합 메시 | `TileGround_01` grid 파생 메시 | RLGL·Minefield `4x9`, Wrong Way 배경 `3x9`, Balloon Blow·Sequence Memory `4x2`, Gift Grab·Bomb Passing·Arena Combat·Cliff Barrage `4x4`, Tag Chase `5x4`, Race `3x8` 바닥을 각각 1 Renderer로 구성 |
| 신호 패널 | SciFi `switch_007` mesh | Red/Green Signal Renderer와 Light 유지 |
| 신호·경광 표현 | SciFi `object_008` | Minefield 머리 위 경광등 |
| 크러셔 벽 | SciFi `wall_003` | Minefield Crusher visual, 기존 trigger Collider 유지 |
| 폭탄 | SciFi `object_016` | Bomb Passing `Bomb` root |
| 공용 구체 | SciFi `object_017` | Balloon Blow 풍선, Bouncing Balls 공, Snowy Spin 눈덩이, Cliff Barrage 투사체 |
| 지뢰 | SciFi `object_018` | Minefield `DetectedMine` |
| `SharedSciFiBlock`/block mesh | SciFi `block` | Tag Chase 시야 차단물, Bouncing Balls 실드 |
| 공용 링 | SciFi `ring` | Gift Base, Bouncing Balls 골·중앙 링, Bomb Passing 중앙 링, Snowy Spin Ice Edge, Arena Combat Spawn Ring |
| 공용 받침 | SciFi `platform` | Sequence Memory NPC 받침, Bomb Passing 중앙 받침 |
| 선물 본체 | SciFi `box_002` | Gift Grab 선물. 기존 Ribbon/Bow 유지 |
| `SharedTripleSwitch` | SciFi `switch_007` | Sequence Memory 입력 콘솔 4개 |
| 공용 플레이어 형태 | MazeParty `PlayerAvatarPresentation` | Sequence Memory 진행 NPC |
| `SharedFantasyCliff` | Fantasy Demo `cliff-1` | Cliff Barrage 4면에 각 4개, 총 16개 배치 |
| 레이저 장치 | SciFi `laser-spin` | Cliff Barrage LaserRig 하우징·경광봉 디테일 |

공용 래퍼는 `Assets/MazeParty/Prefabs/Minigames/Common/Environment`에 두고,
Standard 재질을 URP로 변환한 파생 재질은
`Assets/Ignore/MazePartyGenerated/Materials`에 둔다. centered·grid 파생 메시는
`Assets/Ignore/MazePartyGenerated/Meshes`에 둔다. 공용 환경 래퍼에 포함된 Ignore 원본
Collider는 모두 제거한다.

## 확보된 에셋의 통합 사용안

다음 항목은 별도 모델을 더 구하지 않고 같은 원본을 재질·크기 변형으로 재사용한다.

| 공용 분류 | 통합 적용 대상 |
|---|---|
| Pandazole ground tile | Stable Footing만 nested 래퍼, Wrong Way 52 surface/lane은 centered 메시 직접 참조, 나머지 광범위 바닥은 grid 결합 메시 1 Renderer |
| Fantasy/Pandazole 자연물 | RLGL·Race·Wrong Way·Gift Grab 외곽, Snowy Spin·Cliff Barrage 배경 |
| SciFi `object_017` 구체 | 풍선, Bouncing Balls 공, Snowy Spin 눈덩이, Cliff 투사체 |
| SciFi signal/beacon | RLGL 신호, Minefield 경광등, 위험 경고등 |
| SciFi platform/ring | NPC 받침, Gift Base, Bomb 중앙대, Arena Spawn, Goal frame |
| SciFi fence/wall | Minefield·Gift·Territory·Tag·Bouncing·Bomb·Arena 외곽 |
| SciFi cover/block | Tag Chase 시야 차단물, Bouncing Balls 실드 |
| SciFi boxes | Gift Grab 선물 본체. 기존 ribbon/bow child 유지 |
| Fantasy cliff + SciFi laser | Cliff Barrage 절벽면과 LaserRig |
| Polytope gate | Wrong Way와 Race의 공용 시작·결승 게이트 |

## 게임별 적용 현황과 후속 맵 장식

1. **Minefield** — `object_018` 지뢰, `object_008` 경광등, `wall_003` 크러셔와
   `NatureGroundGrid_4x9` 바닥을 적용했다. 기존 Crusher trigger와
   `Siren Red Lens/Light`를 유지한다. 외곽 SciFi wall은 후속 맵 장식 범위다.
2. **Wrong Way** — 각 Lane의 `Start Platform`, `Step 01~50`, `Finish Platform` 총
   52개 surface anchor는 유지하고 `SharedNatureGroundCentered` 메시를 기존 MeshFilter에
   직접 적용했다. 배경 바닥은 `NatureGroundGrid_3x9`, 결승은 공용 Polytope gate를
   사용하며 레일 fence는 후속 장식이다.
3. **Red Light / Green Light** — `NatureGroundGrid_4x9` 바닥, Fantasy fence,
   SciFi 2등식 신호를 사용한다. Observer는 만들지 않는다.
4. **Stable Footing** — `SharedNatureGroundTile` nested visual과
   Cross/Circle/Square 인서트 3개를 공유한다. 공용 바닥 중 유일하게 nested tile 래퍼를
   유지하며 48개 authority tile root도 그대로 둔다.
5. **Balloon Blow** — `object_017` 1개를 4색 풍선에 공유하고 기존 knot와
   `NatureGroundGrid_4x2` 무대 바닥을 유지한다. 펌프·호스·스테이션은 만들지 않는다.
6. **Gift Grab** — SciFi `box_002`와 기존 ribbon/bow를 선물로 사용하고 `ring`을
   4색 base로 사용하며 `NatureGroundGrid_4x4` 바닥을 적용했다.
7. **Territory Paint** — 런타임 페인트용 연속 UV surface를 그대로 유지한다.
   SciFi 외곽 wall은 후속 맵 장식 범위다.
8. **Tag Chase** — SciFi block을 기존 4개 authority BoxCollider 아래 visual로 넣고
   `NatureGroundGrid_5x4` 바닥을 적용했다. SciFi 외곽 wall은 후속 장식이다.
9. **Race** — Wrong Way와 같은 `SharedFinishGate`와 `NatureGroundGrid_3x8` 트랙 바닥을
   사용한다. `RaceTrack` collider는 유지하며 fence·자연물은 후속 장식이다.
10. **Sequence Memory** — 공용 플레이어 형태의 NPC, `SharedTripleSwitch` 4개,
    SciFi platform podium, `NatureGroundGrid_4x2` 무대 바닥을 사용한다.
11. **Bouncing Balls** — `object_017` 공, SciFi block 실드, ring 골·중앙 링을
    적용했다. 수직 플레이 필드는 전용 표현을 유지한다.
12. **Bomb Passing** — `object_016` 폭탄과 기존 fuse/warning Light를 유지하고
    platform/ring과 `NatureGroundGrid_4x4` 내부 바닥을 적용했다. SciFi 경계는 후속
    장식이다.
13. **Snowy Spin** — `object_017` 눈덩이와 ring Ice Edge를 적용했다. 얼음 표면은
    전용 재질을 유지하며 겨울 자연물은 후속 장식이다.
14. **Arena Combat** — `NatureGroundGrid_4x4` 바닥과 ring 스폰 장식을 적용하고 실제
    fighter는 네트워크 플레이어를 재사용한다. SciFi 경계는 후속 장식이다.
15. **Cliff Barrage** — `NatureGroundGrid_4x4` 플랫폼, Fantasy `cliff-1`, SciFi
    `laser-spin`, `object_017` 투사체를 적용했다. pool root와 beam/VFX 바인딩은
    유지한다.

## 최종적으로 남은 에셋

기능 구현에 필요한 신규 에셋은 없다. 다음 세 가지는 기존 에셋 조합보다 완성도를 더
높이고 싶을 때만 제작하거나 교체한다.

1. 롤러·피스톤 애니메이션이 포함된 **전용 기계식 Crusher**
2. 주름과 매듭이 모델링된 **전용 풍선**
3. 리본과 포장 변형이 모델링된 **전용 선물 상자**

위 세 항목이 없어도 현재 확보된 에셋으로 게임 규칙과 식별성을 표현할 수 있다.

## 바닥·충돌 계약

- 바닥 visual은 기존 판정 바닥보다 위로 돌출되거나 이동 범위를 바꾸지 않는다.
- 광범위 바닥은 `Assets/Ignore/MazePartyGenerated/Meshes`의 `NatureGroundGrid_4x9`,
  `3x9`, `4x2`, `4x4`, `5x4`, `3x8` 결합 메시를 사용해 맵별 1 MeshFilter·1 Renderer로
  구성한다.
- Wrong Way는 Lane마다 기존 52개 surface anchor를 유지하고 각 anchor의 MeshFilter가
  `SharedNatureGroundCentered`를 직접 참조한다. nested tile wrapper를 추가하지 않는다.
- Stable Footing만 `Tile Surface` 아래 `SharedNatureGroundTile` nested visual을 유지한다.
- Pandazole/Fantasy 원본의 MeshCollider는 래퍼에서 제거한다.
- Stable Footing의 낙하 타일, Territory Paint의 연속 UV, Tag Chase 장애물 AABB처럼
  게임 로직이 직접 사용하는 root·mesh 계약은 유지하고 visual만 교체한다.
- 미니게임 setup 재실행은 현재 프리팹 디자인을 덮어쓰지 않는다.
- 이번 맵 단계는 바닥까지만 제작한다. 레일·경계벽·자연 장식은 필요한 공용 원본을
  확정했지만 현재 수작업 디자인 범위를 넓히지 않고 후속 맵 장식으로 남긴다.
