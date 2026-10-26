# 미니게임 밤 환경과 가로등

모든 미니게임은 보드의 FS000_Night_01 스카이박스, 달빛과 BoardNightColor 색 보정을 공유합니다.
공통 MinigameNightLighting 프리팹은 각 게임의 표시 루트 아래에 연결됩니다.
온라인에서 표시될 때만 환경을 적용하고 종료 시 보드/로비의 조명과 카메라 설정을 복원합니다.

## 가로등 배치

- 이 폴더의 ForestStreetLamp.prefab을 씬의 Arena Presentation 또는 해당 게임 Environment 아래로 드래그합니다.
- 숲의 Board/World/ForestStreetLamp.prefab을 원본으로 하는 변형 프리팹입니다.
- 바닥에 밑부분을 맞추고 이동/회전해 배치합니다. 원본 크기를 기본으로 사용합니다.
- 노란 Point Light: 밝기 14, 범위 11m, 실시간 조명, 그림자 없음. Inspector에서 장면에 맞게 조절할 수 있습니다.
- 원본처럼 충돌은 꺼져 있습니다. 이동을 막는 장애물로 쓰려면 별도 게임플레이 콜라이더를 배치합니다.
- 게임 표시 루트 아래에 두면 다른 미니게임이나 보드로 돌아갈 때 함께 숨겨집니다.
- 가로등은 자동 배치하지 않습니다. 밝기가 부족한 곳을 확인해 직접 배치하세요.

새 씬/복구 시 메뉴: MazeParty > Minigames > Apply Shared Night Lighting.
각 게임 setup에도 같은 설치 단계가 연결되어 있습니다. 기존 밤 프리팹과 배치한 가로등 디자인은 유지합니다.
