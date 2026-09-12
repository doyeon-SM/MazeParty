# Minigame Solo Tester

에디터에서 현재 미니게임을 네트워크 방이나 추가 플레이어 없이 바로 실행하는 개발자 도구다.

## 실행

1. Unity 메뉴에서 `MazeParty > Developer > Minigame Solo Tester`를 연다.
2. 미니게임과 seed를 고른 뒤 `Play Solo`를 누른다.
3. 빠른 실행은 `MazeParty > Developer` 아래 각 `Play ... Solo` 메뉴를 사용한다.
4. Play Mode를 끝내면 기존 편집 씬과 기존 Play Mode 시작 씬 설정으로 돌아간다.

현재 지원하는 미니게임은 Minefield, WrongWay, Red Light / Green Light다. 실행 시
선택한 production 씬을 Play Mode 시작 씬으로만 사용하고 로컬 카메라, 1인 러너와
HUD를 런타임에 주입한다. 씬 자산이나 Build Settings는 수정하지 않는다.

## Minefield 조작

- `WASD`: 이동
- 멈춘 상태에서 마우스 오른쪽 버튼: 소나
- `R`: 현재 라운드 재시작
- `N`: 다음 seed로 1라운드부터 재시작
- `Esc`: 솔로 테스트 종료

3초 카운트다운, 40초 플레이, 4초 결과 표시를 3라운드 진행한다. 첫 지뢰는
Crippled 상태와 감속을 적용하고, 두 번째 지뢰나 분쇄기는 탈락 처리한다. 도착선을
통과하면 해당 라운드를 클리어한다.

## WrongWay 조작

- 표시된 방향의 `WASD`: 계단 한 칸 전진
- `R`: 현재 라운드 재시작
- `N`: 다음 seed로 1라운드부터 재시작
- `Esc`: 솔로 테스트 종료

60초씩 2라운드를 진행하며 잘못된 키를 누르면 0.5초 동안 입력이 잠긴다. 50번째
계단에 가장 먼저 도착하면 해당 라운드가 끝난다.

## Red Light / Green Light 조작

- `WASD`: Green 및 Turn Warning 중 이동
- Red: 입력을 놓고 정지
- `R`: 현재 라운드 재시작
- `N`: 다음 seed로 1라운드부터 재시작
- `Esc`: 솔로 테스트 종료

3초 카운트다운 뒤 60초씩 3라운드를 진행한다. 첫 Red 위반은 몸통 부상과 걷기
속도 감속, 두 번째 위반은 탈락을 적용한다. 첫 도착자가 나오면 라운드가 끝난다.

## 검증 범위

이 도구는 각 production 미니게임의 deterministic 규칙과 Netcode-neutral
게임플레이 컴포넌트를 재사용한다. 로컬 조작, 위험물, 카메라, 연출, 라운드
타이밍을 빠르게 확인하기 위한 도구이며 NGO RPC, 서버 권한, 4인 점수 정산은 기존
멀티플레이 테스트 흐름에서 확인한다.

새 미니게임을 추가할 때는 `MinigameSoloTestCatalog`에 descriptor를 등록하고
`MinigameSoloTestLauncher.InjectRuntimeHarness`에 해당 로컬 controller 생성
분기를 추가한다.
