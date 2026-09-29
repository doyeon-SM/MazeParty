# MazeParty 임시 기획 메모

이 문서는 진행 중인 작업의 임시 기억만 보관한다. 확정 기획은 Notion, 구현 이력은 Git,
장기 계약은 코드와 EditMode 테스트를 원본으로 삼는다. 사용자 최신 지시가 항상 우선한다.

## 현재 작업 기준

- 저장소·Unity 프로젝트: `C:/Unity/MazeParty`
- 브랜치: `dev/UI`
- 이번 UI 보완 작업 시작 기준 커밋: `2e87b978284709e7d54746de39a433a9a7d91d92`
  (`26.09.29`, 본문 `언어별 UI 폰트 적용`). 시작 시 로컬 `dev/UI`는
  `origin/dev/UI`보다 1커밋 앞서 있었다.
- 완료 범위: 언어별 UI 폰트 적용과 설정·옷장 UI 보완에 이어, 사용자가 추가한 사운드
  52개를 중앙 SoundCue/BGM 폴백 구조에 매칭하고 보너스 발표 준비음을 구현했다.
- 최종 자동 검증: Unity 6000.6.0f1 컴파일 오류 0, 전체 EditMode **443/443 통과**,
  실패·스킵 0이다. 사운드·수상식 집중 계약 테스트도 26/26 통과했다.
- Ignore 경로 무료 캐릭터 에디터 스크립트의 기존 `CS0414` 경고 2개가 남아 있다.

## 완료 경기 복귀 실패 정책

- 복귀 소유권을 잃거나 저장이 실패한 뒤 `_completedMatchReturnQueued`를 해제해 다음
  복귀 시도가 막히지 않게 한다.
- 세션 단계 저장은 최대 3회 시도한 뒤 fail-closed로 종료한다.
- 미니게임 씬 언로드는 Started·SceneNotLoaded·SceneEventInProgress를 구분해
  대기·진행·재시도하고, 그 밖의 결과는 fail-closed로 처리한다. 준비와 언로드에는
  명시적 deadline을 적용한다.
- fail-closed `LeaveAsync`는 일반 예외 최대 3회, 10초 시간 상한을 사용한다.
  시간 초과는 동시 재시도를 만들지 않는 terminal 상태이며 recovery journal은 보존한다.
- 로비 복귀가 실제로 성공한 경우에만 활성 미니게임 일정을 완료 처리한다.

## 활성 UI 계약

- 영문 디자인을 기준으로 캐주얼하게 구성한다. 보라색을 메인, 초록색과 남색을
  서브색으로 사용한다.
- 일반 UI 에셋은 `Assets/Ignore/RetroCartoonUIPack`과
  `Assets/Ignore/ModernUIPack`만 사용한다. 생성 로고는 `Assets/Ignore/AIImage`에 둔다.
- 둥근 채움 표면은 `128px` 등 다른 해상도를 쓰지 않고 `Rounded Filled 1024px`로 통일한다.
- 글자 역할은 제목 34 Bold, 주요 CTA 32 Bold, 정보 22 Bold, 일반 20 Bold,
  설명 16 Bold를 기준으로 한다.
- 영어 선택 시 `MazeParty`, 한국어 선택 시 `미로파티` 로고를 표시한다.
  일본어·중국어는 전용 로고가 생기기 전까지 영문 로고를 사용한다.
- 플레이 중 Canvas 디자인은 프리팹 원본에서 관리하고 런타임은 직렬화된 바인딩의
  값과 표시 상태만 갱신한다. setup 재실행은 기존 디자인을 덮어쓰지 않는다.
- 보드와 모든 미니게임에서 플레이어 이름을 머리 위에 표시한다.
- 모든 미니게임은 최초 시작 카운트다운 동안 공통 하이라이트로 로컬 위치를 안내한다.
- Minefield와 Balloon Blow는 전용 화면 HUD를 사용하지 않는다.
- Wrong Way는 Modern UI의 로컬 방향 아이콘 하나만 표시하고 정답 입력 시 다음 아이콘으로
  교체한다. 개인 카메라는 로컬 플레이어의 계단 진행을 중앙 추적한다.
- Red Light Green Light는 현재 신호만 표시한다.
- Gift Grab은 운반·스턴·기지 선물 수만 월드에 남기고, Stable Footing은 안전 문양만 유지한다.

## 미정 결정과 바로 다음 작업

- 파일이 없는 중앙 큐 `item.bullet_impact`, `minigame.finish`와
  미니게임 씬 직접 오디오 슬롯 9개는 후속 사운드가 필요하다.
- 보드 칸 종류의 최종 배치 비율을 확정해야 한다.
- Ignore 경로의 UI 팩과 AI 로고를 배포 가능한 추적 경로로 옮길지 결정해야 한다.
- Sequence Memory의 상대 입력 문자열, Territory Paint의 4인 점수표,
  Bouncing Balls의 개별 플레이어 카드를 추가로 축소할지 결정해야 한다.
- 이번에 확인한 5종 외 나머지 10종 미니게임의 조작·조기 종료·시간 종료·동률·결과·씬
  정리 분기와 15종 전체 Relay 순환은 계속 검증해야 한다.
- MVP 실기: pause 중 재접속, 호스트 복구 3개 체크포인트, 공동 순위·수상식,
  15턴 전체 여정, 별도 PC·고지연·IL2CPP 릴리즈 후보를 검증한다.

## Notion 반영 대상

- 기록일: 2026-09-28 (Asia/Seoul)
- 기획서 v0.40: https://app.notion.com/p/3d3c227fda0380f6a34df09eff208ac0
- 누적 회의록: https://app.notion.com/p/3e8c227fda0381c59956eaecf273f797
- TODO 페이지와 같은 날짜 회의록에는 커밋 `9d07c762d625cafb9dcf60190f641bb621388c96`,
  UI 계약 수정, 완료 경기 복귀 실패 정책, 436/436, 5종 Solo 스모크와 실제
  4클라이언트 Board→Wrong Way→Board 결과를 최종 근거로 반영한다.

## 완료: 사운드 큐·BGM 매칭

- `Assets/Resources/sound`의 MP3 52개를 분석해 중앙 SoundCue에 매칭했다. 키는
  `ceremony.award_ready`를 새로 추가해 기존 61개에서 62개가 되었고, 라이브러리 누락 키는
  0개다. 현재 클립이 연결된 큐는 43개, 의도된 폴백 또는 파일 부재로 빈 큐는 19개다.
- `item.use.double_dice`는 1~6 다이스의 `item.use.low_dice`, 7~12 다이스의
  `item.use.high_dice`와 같은 `dicelowandhigh.mp3` 효과음을 공유한다.
- BGM은 `bgm.lobby`, `bgm.board`, 공용 `bgm.minigame`에만 연결한다. 대기실·수상식·15개
  미니게임 전용 키는 기존 폴백을 사용해 같은 곡이 장면 전환마다 재시작되지 않게 한다.
- 긴 BGM 4개는 `Streaming`, preload 해제, background load로 임포트해 약 284MiB 규모의
  동시 PCM 상주를 피한다.
- `award_ready`는 각 보너스 결과 공개 전 `BonusAwardOneReady`·`BonusAwardTwoReady`
  서버 단계에서 정확히 2초만 재생하고, 공개 단계로 넘어갈 때 즉시 중지한다. 준비 뒤 공개는
  기존 4초를 유지하며 열쇠 지급도 각 공개 경계로 이동했다. 기존 복제 enum 값 0~4는 보존한다.
- 준비음 원본은 약 6초이므로 cue의 BGM duck은 끈다. 현재 SoundSystem의 덕킹 종료 시각은
  클립 전체 길이를 따르기 때문에, 2초에 voice를 중지해도 켜 두면 음악만 약 6초간 작아진다.
- 일시정지 진입 시 준비음을 즉시 중지하고, 같은 준비 단계에서 재개하면 남은 서버 시간 동안
  다시 재생한다. 늦은 참가·재접속도 공개 단계 전환 시 handle을 중지해 `award`와 겹치지 않는다.
- 연속 보행 녹음 `footstep1~4` 원본은 보존하고, 파형의 깨끗한 충격 구간을 골라
  `Assets/MazeParty/Sounds/Board/Footsteps`에 0.20~0.325초 모노 PCM16 WAV 11개로 파생했다.
  1~3번 원본은 각 3개, 무음 구간이 적은 4번은 2개만 사용한다. 양끝 페이드와 5ms 무음을
  넣고 1~3번은 -6dBFS, 4번은 -9dBFS peak로 맞췄다.
- `board.footstep`은 긴 MP3 대신 파생 원샷 11개만 참조하며 Shuffle, 동시 인스턴스 6,
  전역 최소 간격 0을 사용한다. 볼륨·피치 편차는 각각 0.10·0.05로 줄였다. 모든 원샷은
  DecompressOnLoad·PCM·preload 설정이며 최대 이동 cadence 0.36초보다 짧다. cue 재생성용
  setup 기본값도 같은 Shuffle·볼륨 0.10·피치 0.05 설정을 사용한다.

## 완료: 캐릭터 커스터마이징 확장

- `Assets/Ignore/FREE/Pack_FREE_PartyCharacters`의 원본을 복사하지 않고 직접 참조한다.
- 기존 표정 4종은 팩의 `Resources/Materials/Face Images/face 1~3.png` 3종으로 교체하고,
  데이터 에셋에서 `Face1`·`Face2`·`Face3`으로 관리한다.
- 모자는 `없음`을 기본값(식별자 0)으로 포함하고, 팩의 chef hat·orange fedora·party hat을
  각각 `Hat1`·`Hat2`·`Hat3`으로 관리한다.
- 로비에서는 표정과 동일하게 좌우 버튼으로 모자를 순환 선택하며, 선택값은 프로필 저장과
  네트워크 상태를 거쳐 로비·보드·미니게임·수상식의 캐릭터 표시 모두에 반영한다.
- `Assets/Ignore` 직접 참조는 해당 무료 팩이 없는 다른 체크아웃에서 참조가 끊기는
  의도된 제약이다. 이번 작업에서는 에셋 복사나 추적 경로 이전을 하지 않는다.
- `LobbyCanvas` 프리팹에 표정·모자 선택 바인딩을 두고 레거시 Test Hat 토글과
  아바타의 임시 모자 메시를 제거했다. 모자 프리팹은 런타임에 작성된 `HatAnchor` 아래에만
  생성한다.
- Face1~3과 Hat1~3의 실제 프리뷰, 로비 UI의 좌우 선택·레이아웃을 Play Mode에서 확인했고,
  관련 집중 계약 테스트 19/19와 전체 EditMode 437/437가 실패·스킵 없이 통과했다.

## 완료: 언어별 UI 폰트 적용

- 영어·한국어는 `KCCMurukmuruk.otf`, 일본어는 `NotoSansJP-Regular.ttf`, 중국어 간체는
  `NotoSansSC-Regular.ttf`를 사용한다. Noto의 `SC`는 중국어 간체, `TC`는 번체다.
- 실제 런타임 폰트 3개만 `Assets/Ignore/Resources/Font`에 두고, 다운로드 원본 묶음은
  Resources 밖의 `Assets/Ignore/FontSources`로 이동해 빌드 포함 범위를 제한한다.
- 모든 플레이어 표시용 텍스트 프리팹 36개의 루트에 `LocalizedFontScope`를 두어 활성·비활성
  자식의 `UnityEngine.UI.Text`와 `TextMesh`가 언어 변경 즉시 해당 폰트를 사용한다.
- `TextMesh`는 폰트와 함께 렌더러의 폰트 머티리얼도 교체한다. 임의 언어가 섞일 수 있는
  플레이어 이름을 위해 세 폰트의 importer fallback도 서로 연결한다.
- 각 폰트의 대표 글리프·fallback·런타임 전환·36개 프리팹 바인딩을 계약 테스트로 유지한다.
  Play Mode에서 한국어·일본어·중국어 간체를 각각 전환해 활성 UI Text 104개 모두 폰트
  불일치 0개임을 확인했고, 전체 EditMode 441/441가 실패·스킵 없이 통과했다.
- 폰트 파일은 `Assets/Ignore`에 있으므로 다른 체크아웃에는 자동으로 전달되지 않는다.
  해당 파일이 있는 환경에서 setup 메뉴가 importer fallback과 프리팹 스코프를 재설정한다.

## 완료: 설정 적용 라벨 및 옷장 세로 레이아웃

- 설정창 `Apply Button/Label`은 KCC·Noto 폰트의 실제 줄높이를 수용하도록 세로 여백을
  줄이고 세로 overflow를 허용했다. 영어 `Apply`, 한국어 `적용`, 일본어 `適用`,
  중국어 간체 `应用`이 모두 완전한 글리프로 생성되는 계약 테스트를 유지한다.
- 옷장은 `색 → 색상 버튼 → 얼굴 → 얼굴 선택 → 모자 → 모자 선택` 순서로 배치한다.
  얼굴과 모자 선택기는 각각 한 행을 사용하며 기존 좌우 버튼·프리뷰·이름 바인딩을 유지한다.
- `Color`, `Face`, `Hat` 문자열 키를 추가해 네 언어에서 `색/얼굴/모자` 안내 라벨을
  현지화한다. 새 Lobby/GameMenu 프리팹 생성 경로에도 같은 레이아웃, 번역 바인딩,
  `LocalizedFontScope`가 적용되며 기존 프리팹 디자인은 setup 재실행으로 덮어쓰지 않는다.
- `OnlineBootstrap`의 기존 Lobby 프리팹 override가 참조하는 Footer local fileID를 보존했고,
  프리팹 내부 중복 ID·누락 참조와 씬의 orphan override가 없음을 확인했다.
- 관련 집중 EditMode 21/21과 전체 EditMode 442/442가 실패·스킵 없이 통과했다.
