# 보드 아이템 밸런스

이 폴더의 9개 `BoardItemDefinition` SO를 Inspector에서 편집합니다.

- Price: 골드 가격 / Spawn Weight: 상점·랜덤 보상 상대 가중치 (0이면 제외)
- Charges: 턴 시작 장착 시 탄약·수량 / Damage: 고정 피해
- Range: 미터 단위, 한 칸은 8m / Fire Interval: 클릭 사이 최소 초
- Aim Magnification: 조준 배율 / Blast Radius: 폭발 반경
- Trigger Radius / Arming Delay: 지뢰 감지 반경 / 설치 후 활성화 초
- Throw Flight Seconds / Projectile Radius / Projectile Lifetime: 수류탄 궤적·충돌·최대 수명
- Dice Minimum / Dice Maximum: 범위 주사위의 최소/최대 눈 (1~12, 양끝 포함)
- Cast Duration: 위치변환 시전 시간, 기본 2초
- Held Prefab / World Prefab / Explosion Prefab: 교체 가능한 시각 프리팹

데이터는 호스트 기준으로 판정합니다. 모든 참가자는 같은 에셋 버전으로 빌드하세요.
일반 벽은 총알·폭발을 차단하고 칸 이동 제한 벽은 무시합니다.
권총·스나이퍼는 클릭당 한 발이며, 잔탄과 선택한 미사용 아이템은 행동 종료 시 소멸합니다.
설치된 지뢰는 게임 종료·폭발 전까지 유지하고 보드 행동 단계에서만 감지·활성화 시간이 흐릅니다.
원본 SO는 런타임에서 변경하지 않습니다. 가격/가중치를 바꾸면 재시작한 플레이 세션부터 검증하세요.
표시 이름과 설명은 Display Name / Description에서 함께 갱신하세요.
모델 원본: Assets/MazeParty/Prefabs/Board/Items. 프리팹에는 판정용 Collider를 추가하지 마세요.
`MazeParty/Board/Upgrade Board Items`는 누락 에셋만 생성하며 기존 수치·모델 디자인을 덮어쓰지 않습니다.

## 추가 유틸리티 아이템

| SO | 초기 가격 | 동작 |
|---|---:|---|
| LowDice | 5G | 기존 D12 결과를 1~6 균등 확률로 변경 |
| HighDice | 8G | 기존 D12 결과를 7~12 균등 확률로 변경 |
| PositionSwapper | 12G | 좌클릭 → 대상 목록 → 2초 시전 후 현재 위치 교환 |
| Cloak | 10G | 좌클릭 → 행동 종료/사망까지 상대 시야·미니맵에서 숨김 |

위치변환은 완료 시점의 실제 위치·현재 칸을 교환하고 각자의 시선·남은 이동 수를 유지합니다.
시전 중 실제 체력 피해, 대상 사망 또는 행동 종료 시 취소되며 아이템은 소모됩니다.
전역 정지 중에는 시전 시간도 정지합니다. 은신 대상도 위치 공개 없이 선택할 수 있습니다.
은신은 공격·비치명적 피격으로 해제되지 않으며 행동 종료 전환에서 해제되어 격투에 남지 않습니다.
대상 선택·상태·안내 UI 디자인은 BoardCanvas 프리팹의 BoardUtilityItemView 바인딩을 편집합니다.
