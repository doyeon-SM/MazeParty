# Minigame Solo Tester

에디터에서 현재 미니게임을 네트워크 방이나 추가 플레이어 없이 바로 실행하는 개발자 도구다.

## 실행

1. Unity 메뉴에서 `MazeParty > Developer > Minigame Solo Tester`를 연다.
2. 미니게임과 seed를 고른 뒤 `Play Solo`를 누른다.
3. 빠른 실행은 `MazeParty > Developer > Play Minefield Solo`를 사용한다.
4. Play Mode를 끝내면 기존 편집 씬과 기존 Play Mode 시작 씬 설정으로 돌아간다.

현재 지원하는 미니게임은 Minefield다. 실행 시 production
`Assets/MazeParty/Scenes/Minefield.unity`를 Play Mode 시작 씬으로만 사용하고,
로컬 카메라, 1인 러너, 지뢰, 분쇄기, HUD를 런타임에 주입한다. 씬 자산이나 Build
Settings는 수정하지 않는다.

## Minefield 조작

- `WASD`: 이동
- 멈춘 상태에서 마우스 오른쪽 버튼: 소나
- `R`: 현재 라운드 재시작
- `N`: 다음 seed로 1라운드부터 재시작
- `Esc`: 솔로 테스트 종료

3초 카운트다운, 40초 플레이, 4초 결과 표시를 3라운드 진행한다. 첫 지뢰는
Crippled 상태와 감속을 적용하고, 두 번째 지뢰나 분쇄기는 탈락 처리한다. 도착선을
통과하면 해당 라운드를 클리어한다.

## 검증 범위

이 도구는 production Minefield의 deterministic 지뢰 배치와 Netcode-neutral
게임플레이 컴포넌트(`MinefieldPlayerMotor`, `MinefieldMine`,
`MinefieldSonar`, `MinefieldCrusher`)를 재사용한다. 로컬 조작, 위험물,
카메라, 연출, 라운드 타이밍을 빠르게 확인하기 위한 도구이며 NGO RPC, 서버 권한,
4인 점수 정산은 기존 멀티플레이 테스트 흐름에서 확인한다.

새 미니게임을 추가할 때는 `MinigameSoloTestCatalog`에 descriptor를 등록하고
`MinigameSoloTestLauncher.InjectRuntimeHarness`에 해당 로컬 controller 생성
분기를 추가한다.
