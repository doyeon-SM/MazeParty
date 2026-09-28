# 캐릭터 외형 및 손 감정표현

- `PlayerExpressions.asset`: 표정 이름/스프라이트, 모자 이름/프리팹/착용 변환,
  손 이름/프리팹 연결.
- Faces 배열 순서는 저장되는 표정 ID(0부터)다. Hats 배열은 저장 ID 1부터 대응하며
  모자 ID 0은 `없음`이다. 저장 호환성을 위해 기존 항목 순서를 바꾸지 않는다.
- Gestures 배열은 엄지/V/하트 순서이며 현재 원형 UI는 3칸입니다.
- 얼굴과 모자는 `Assets/Ignore/FREE/Pack_FREE_PartyCharacters`의 원본을 직접 참조한다.
  현재 얼굴은 `face 1~3.png`, 모자는 chef hat·orange fedora·party hat이다.
  이 팩을 복사하거나 추적 경로로 옮기지 않았으므로 다른 체크아웃에도 같은 팩이 필요하다.
- 손 모델 원본: `Assets/MazeParty/Prefabs/Multiplayer/Expressions`.
  두 손을 포함한 프리팹이며 Collider 없이 교체하세요. 플레이어 색상을 적용합니다.
- UI: LobbyCanvas의 Face Expression·Hat Selection, LobbyCanvas/BoardCanvas의 Hand Emote Wheel.
  외형/배치는 프리팹에서 편집하고 런타임은 바인딩 값과 활성 상태만 변경합니다.

## 조작

로비에서 얼굴과 모자를 각각 양옆 화살표로 선택합니다. 다음 실행 및 온라인 재접속에도
선택을 유지하며 몸 색상과 함께 모든 플레이어 표시 경로에 반영합니다.

T를 누른 채 마우스를 끌고 T를 놓으면 발동합니다. 중앙에서 놓기 / ESC / 우클릭은 취소.
선택 중 카메라 시점과 방향은 유지됩니다. 감정표현 손은 서버 기준 1초 후 원래 손으로 돌아옵니다.
맨손으로 로비·보드 행동 중 사용합니다. 이동은 가능하며 공격/상호작용 시 취소합니다.
아이템 장착·사망·격투·미니게임·위치변환 시전·전역 정지 중에는 사용할 수 없습니다.
연타로 지속시간을 늘릴 수 없고 은신 시 상대에게 손을 노출하지 않습니다.

`MazeParty/Player/Upgrade Expressions`는 정확히 일치하는 기존 4종 표정 카탈로그만 새 팩으로
한 번 이전하고, 누락된 선택 바인딩만 추가하며 이후 카탈로그 설정과 기존 디자인을 덮어쓰지 않습니다.
