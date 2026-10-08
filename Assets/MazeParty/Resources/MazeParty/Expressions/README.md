# 캐릭터 외형 및 손 감정표현

- `PlayerExpressions.asset`: 표정 이름/스프라이트, 모자 이름/프리팹/착용 변환,
  감정표현 이름 연결.
- Faces 배열 순서는 저장되는 표정 ID(0부터)다. Hats 배열은 저장 ID 1부터 대응하며
  모자 ID 0은 `없음`이다. 저장 호환성을 위해 기존 항목 순서를 바꾸지 않는다.
- Gestures 배열은 인사/경례/욕설/하트/놀람/항복/부탁/눈가림 순서이며
  원형 UI도 같은 순서의 8칸입니다.
- 얼굴과 모자는 `Assets/Ignore/Pack_PartyCharacters`의 원본을 직접 참조한다.
  얼굴은 `face 1~15.png` 전체를 Face1~Face15로 사용한다.
  모자는 팩의 30개를 모두 사용하며 기존 저장 호환성을 위해 Hat1~Hat3은
  chef hat·orange fedora·party hat 순서를 유지한다. Hat4~Hat30은 alien,
  angle hole, bandage, bonus, clown, cowboy hat, egg, fez, fireman hat,
  goat horns, hair, hat, headphone, heart antenna, horn, king crown,
  mushroom hat, noel hat, party crown, pineapple, pump, soldier hat,
  sombrero, top hat, traffic cone, viking helmet, witch hat 순서다.
  이 팩을 복사하거나 추적 경로로 옮기지 않았으므로 다른 체크아웃에도 같은 팩이 필요하다.
- 손 모델 원본은 플레이어가 평소 사용하는 `SimpleFistHand`입니다. 감정표현은 별도 손 모델을
  생성하지 않고 기존 두 손의 앵커와 손가락 본을 움직입니다.
- UI: LobbyCanvas의 Face Expression·Hat Selection·Emote Face Mapping,
  LobbyCanvas/BoardCanvas의 8칸 Hand Emote Wheel.
  외형/배치는 프리팹에서 편집하고 런타임은 바인딩 값과 활성 상태만 변경합니다.

## 조작

로비에서 얼굴과 모자를 각각 양옆 화살표로 선택합니다. Emote Face Mapping에서는
감정표현별 얼굴도 선택합니다. 다음 실행에도 선택을 유지하고 감정표현 발동 시 서버가
선택 얼굴을 검증해 모든 플레이어에게 표시합니다. 눈가림은 기본 얼굴을 유지합니다.

T를 누른 채 마우스를 끌고 T를 놓으면 발동합니다. 중앙에서 놓기 / ESC / 우클릭은 취소.
선택 중 카메라 시점과 방향은 유지됩니다. 감정표현은 서버 기준 2초 후 원래 손과 얼굴로 돌아옵니다.
맨손으로 로비·보드 행동 중 사용하며 지속 중 이동은 서버에서 제한됩니다.
공격/상호작용 입력 시 취소합니다.
아이템 장착·사망·격투·미니게임·위치변환 시전·전역 정지 중에는 사용할 수 없습니다.
연타로 지속시간을 늘릴 수 없고 은신 시 상대에게 손을 노출하지 않습니다.

`MazeParty/Player/Upgrade Expressions`는 정확히 일치하는 기존 4종 표정 카탈로그 또는
FREE/유료 팩의 3개 얼굴·3개 모자 카탈로그만 유료 팩의 15개 얼굴·30개 모자로 한 번 이전한다.
기존 3칸 휠은 8칸 구조로 한 번 이전하고, 완성된 8칸 휠과 선택 바인딩은 다시 만들지 않습니다.
