# MazeParty 임시 기획 메모

이 문서는 현재 구현 기준, 확정된 작업 계약, 미정 결정과 다음 작업만 보관한다.
완료 이력은 Git, 확정 기획 원문은 Notion, 장기 계약은 코드와 EditMode 테스트를
원본으로 삼는다. 사용자의 최신 명시적 지시를 항상 우선한다.

## 현재 작업 기준

- 저장소·Unity 프로젝트: `C:/Unity/MazeParty`
- 작업 브랜치: `dev/UI`
- 최근 자동 검증: Unity 6000.6.0f1 컴파일 오류 0, 전체 EditMode 369/369 통과,
  실패·스킵 0
- 비차단 기존 경고: Ignore 경로 무료 캐릭터 에디터 스크립트의 `CS0414` 2개

## 확정된 구현 계약

### 완료 경기 복귀

- 복귀 소유권 상실 또는 저장 실패 시 대기열을 해제해 재시도를 막지 않는다.
- 세션 단계 저장은 최대 3회, 준비·언로드는 명시적 deadline을 사용한다.
- fail-closed `LeaveAsync`는 일반 예외 최대 3회와 10초 상한을 사용한다.
- 시간 초과는 중복 재시도 없는 terminal 상태이며 recovery journal은 보존한다.
- 실제 로비 복귀가 성공한 뒤에만 활성 미니게임 일정을 완료 처리한다.

### UI·현지화

- 캐주얼 영문 디자인을 기준으로 보라색을 주색, 초록색·남색을 보조색으로 사용한다.
- 둥근 채움 표면은 `Rounded Filled 1024px`로 통일한다.
- 글자 역할 기준은 제목 34, 주요 CTA 32, 정보 22, 일반 20, 설명 16 Bold다.
- 영어 로고는 `MazeParty`, 한국어 로고는 `미로파티`이며 일본어·중국어는
  전용 로고가 생기기 전까지 영어 로고를 사용한다.
- 영어·한국어는 KCC, 일본어는 Noto JP, 중국어 간체는 Noto SC를 사용하고
  플레이어 이름에는 상호 fallback을 적용한다.
- 옷장은 색 → 얼굴 → 모자 순서다. 얼굴은 Face1~3, 모자는 없음·Hat1~3이며
  선택값은 저장·네트워크 상태·모든 캐릭터 표시에 반영한다.
- 플레이 중 Canvas는 허용된 프리팹을 원본으로 삼는다. 런타임은 직렬화된 바인딩의
  값·표시 상태만 바꾸고 setup은 기존 프리팹 디자인을 덮어쓰지 않는다.
- 플레이어 이름은 월드 머리 위에 표시하며 최초 카운트다운에는 로컬 위치를 강조한다.

### 미니게임 HUD

- 게임은 항상 4인 구조를 유지한다.
- 화면 HUD에는 현재 판단에 필요한 입력·신호·점수만 남긴다.
- 15종 HUD 전수 감사를 완료했다. Tag Chase·Race·Bomb Passing·Snowy Spin·
  Cliff Barrage는 공통 HUD만, Arena Combat은 공통 HUD와 피격 플래시만 유지한다.
- Minefield와 Balloon Blow는 전용 화면 HUD를 쓰지 않는다.
- Wrong Way는 로컬 방향 아이콘 하나, Red Light Green Light는 현재 신호만 표시한다.
- Gift Grab은 운반·스턴·기지 수량을 월드에 표시하고 Stable Footing은 안전 문양만 남긴다.
- Sequence Memory는 NPC 순서와 로컬 플레이어 입력·상태 한 줄만 화면에 표시한다.
- Territory Paint와 Bouncing Balls는 이름 중복 없이 P1~P4 점수만 작게 표시한다.
- 실제 씬에서 쓰지 않는 Minefield·Balloon Blow·Gift Grab 전용 HUD와 Balloon Blow
  스테이션 라벨, Tag Chase·Race·Cliff Barrage·Stable Footing 복구 HUD 및 재생성용
  일회성 setup을 제거했다. 퇴역 자산의 복구 기준은 별도 복사본이 아닌 Git 이력이다.

### 오디오

- 직접 BGM은 로비·보드·공용 미니게임만 사용하고 나머지는 fallback을 사용한다.
- 보너스 준비음은 공개 전 2초만 재생하며 공개·일시정지에서 중지하고 duck하지 않는다.
- 보드 발소리는 짧은 원샷 11개를 shuffle 재생한다.

## 미정 결정

- 파일이 없는 중앙 큐 `item.bullet_impact`, `minigame.finish`와
  미니게임 씬 직접 오디오 슬롯 9개의 후속 사운드
- 보드 칸 종류의 최종 배치 비율
- Ignore 경로 UI 팩·AI 로고·폰트·캐릭터를 배포 가능한 추적 경로로 옮길지 여부

## 남은 검증·TODO

- 이번에 확인한 5종 외 나머지 10종 미니게임의 조작·조기 종료·시간 종료·동률·결과·씬
  정리 분기와 15종 전체 Relay 순환
- pause 중 재접속, 호스트 복구 3개 체크포인트, 공동 순위·수상식
- 별도 PC, 고지연, IL2CPP 릴리즈 후보
- 15턴 전체 여정은 사용자가 별도로 진행
