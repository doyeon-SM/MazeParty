# STOVE Release 빌드 수동 런처 테스트

대상: MazeParty · 훈연구이팀(HYGY) · Windows x64 / IL2CPP / STOVE PC SDK 3.5.3.0.

## 1. 준비

- 입력한 `stove.json`을 포함해 만든 **stove-release** 출력 폴더를 사용합니다. `stove-integration`은 설정이 없어 로그인 테스트용으로 사용할 수 없습니다.
- STOVE Studio에서 해당 프로젝트의 빌드를 올릴 권한이 있는 계정으로 로그인합니다. 런처에서도 같은 Studio 구성원 계정을 사용합니다.
- 설정의 게임ID·Application Key가 업로드할 상품과 같은지 본인이 확인합니다. Application Secret은 게임에 넣지 않습니다.
- Unity UGS 환경이 활성화되어 있고 인터넷에 연결되어 있어야 합니다. 이 절차를 위해 별도 인증 서버를 배포할 필요는 없습니다.

## 2. Studio에 테스트용 빌드 업로드

1. STOVE Studio에서 해당 프로젝트의 **빌드 업로드 관리**로 이동합니다.
2. Studio가 안내하는 업로더를 설치·실행하고 해당 게임ID를 선택합니다.
3. **게임 폴더**로 `MazeParty.exe`가 바로 들어 있는 Release 출력 폴더 전체를 지정합니다. `MazeParty_Data`, `UnityPlayer.dll`, `GameAssembly.dll`, SDK·StreamingAssets 등 함께 생성된 파일을 유지합니다.
4. **실행 파일**을 `MazeParty.exe`로 지정합니다. `UnityCrashHandler64.exe`를 선택하지 않습니다.
5. 빌드 식별 이름은 날짜·버전·커밋을 알아볼 수 있게 정하고 업로드합니다. 이 이름은 업로더의 실제 입력 형식에 맞춥니다.
6. 업로드 완료 후 테스트 빌드 목록에서 방금 올린 버전을 확인합니다. 이번 작업은 설치·실행 테스트이며 상점 자동 출시나 공개 배포 설정을 변경할 필요가 없습니다.

고지 파일(`THIRD_PARTY_NOTICES.md`, `ThirdPartyLicenses`)도 게임 폴더에 포함합니다. 저장소 전체나 원본 `stove.json`을 따로 업로드하지 않습니다. 빌드된 `MazeParty_Data/StreamingAssets/mazeparty-platform.json`은 SDK 실행에 필요한 클라이언트 설정이므로 유지합니다.

## 3. 런처에서 설치·실행

1. STOVE 런처를 실행하고 해당 Studio 구성원 계정으로 로그인합니다.
2. **스토어 → Studio / 스튜디오 테스트 → 테스트 빌드 목록**에서 해당 게임을 찾습니다. 공식 Studio 안내의 화면 명칭은 클라이언트 버전에 따라 다를 수 있습니다.
3. 업로드 버전을 확인하고 **테스트 → 게임 설치**를 진행합니다.
4. 설치가 끝나면 **런처의 게임 실행 버튼**으로 시작합니다.
5. 게임에서 온라인 접속을 진행하고 방을 생성해 방 코드가 표시되는지 확인합니다.

테스트 목록이 보이지 않으면 같은 계정인지, 팀·프로젝트의 Studio 권한이 있는지, 업로드가 완료되었는지를 확인한 뒤 런처를 재시작합니다. 메뉴가 여전히 없으면 현재 Studio 안내 또는 담당자에게 해당 프로젝트의 테스트 빌드 접근 경로를 확인합니다.

일반 STOVE 클라이언트에서 실행하는 Studio 테스트 빌드도 `stoveEnvironment`는 **`LIVE`**입니다. 별도로 발급된 Sandbox 런처·계정을 사용하는 경우에만 `SANDBOX`를 선택합니다. 3.5.3.0에서 이전 `real` 입력은 Sandbox로 처리되어 Live 런처와 오류 308을 일으켰습니다. 수정 코드가 기존 `real`을 `LIVE`로 정규화합니다. AppSecret이나 `PolicyConfig.json` 우회 설정은 필요하지 않습니다.

## 4. 확인할 동작

| 순서 | 확인 내용 | 기대 결과 |
| --- | --- | --- |
| 1 | Studio 테스트 빌드를 런처에서 실행 | SDK 초기화·소유권 확인 후 온라인 접속 가능 |
| 2 | 방 생성 | 방 코드가 생성되고 최대 4인 제한 유지 |
| 3 | 다른 PC·다른 권한 있는 STOVE 계정으로 같은 코드 입력 | 같은 STOVE 버전끼리 참가 가능 |
| 4 | 게임 시작·보드·미니게임·퇴장 | 호스트/클라이언트 상태가 함께 진행 |
| 5 | 클라이언트 종료 후 같은 PC·계정으로 재실행 | 기존 재접속·좌석 복원 흐름 확인 |
| 6 | 원래 호스트 종료·재실행 | 원래 호스트 복귀 정책에 따라 복원 |
| 7 | STOVE 방 코드를 Development 빌드에 입력 | 플랫폼이 달라 참가 거부 |
| 8 | 로그인·소유권을 충족하지 못한 조건 | 온라인 로그인이 거부되고 UGS로 우회하지 않음 |

먼저 2개 PC·2개 계정으로 확인한 뒤 4인으로 확대합니다. 테스트 계정의 이용 권한은 Studio에서 정상적으로 부여합니다. 한 PC의 동일 STOVE 계정 창 4개는 서로 다른 실제 사용자 검증을 대체하지 않습니다. 일반 게임 흐름을 혼자 4인으로 점검할 때는 [Development 테스트 도구](../Tools/StartFourPlayerTest.ps1)를 사용합니다.

EXE를 직접 실행했을 때는 SDK가 런처를 열거나 다시 시작하도록 처리할 수 있습니다. EXE가 열렸는지만으로 로그인·소유권 성공을 판정하지 않습니다.

## 5. 오류 기록

- 실패한 단계, 런처에서 선택한 빌드 버전, 화면의 오류 문구를 기록합니다.
- 현재 프로젝트의 Unity `companyName`은 `DefaultCompany`, `productName`은 `MazeParty`이므로 기본 Player 로그는 `%USERPROFILE%\AppData\LocalLow\DefaultCompany\MazeParty\Player.log`입니다. 향후 회사명을 바꾸면 이 경로도 바뀝니다.
- 키 값, 토큰, 원본 설정 파일 전체는 공유하지 않습니다. 필요한 오류 구간만 민감정보를 가려 전달합니다.
- `SDK 초기화/IPC`: 런처 상태·로그인·환경·상품 일치를 확인합니다. `소유권`: Studio 계정·상품 접근 권한을 확인합니다. `UGS`: Unity 환경·인터넷·서비스 상태를 확인합니다. `방 참가`: 플랫폼·게임 버전·코드를 확인합니다.

### 308이 계속 발생할 때

- 308은 런처의 IPC 인증 키를 받지 못했다는 뜻입니다. 이번 사례는 SDK `sandbox`와 런처 `live`의 불일치가 로그로 확인됐습니다.
- 수정된 코드로 STOVE / Windows x64 / IL2CPP / Development Build 해제 상태에서 다시 빌드합니다. 출력 `MazeParty_Data/StreamingAssets/mazeparty-platform.json`의 `stoveEnvironment`가 `LIVE`인지 값 전체를 공유하지 않고 확인합니다.
- 새 빌드를 업로드하고 런처에서 그 버전을 설치합니다. 이전 설치본 재시작만으로는 수정이 적용되지 않습니다.
- 새 Player.log의 `STOVE launcher verification: SDK environment LIVE.`를 확인합니다. 계속 실패하면 SDK 로그에서 환경 불일치 여부를 확인합니다. SDK 로그에는 키·토큰이 포함될 수 있으므로 원본 전체를 공유하지 않습니다.
- 성공 기준은 런처 실행뿐 아니라 소유권 확인 후 방 생성·코드 표시까지입니다.

## 6. 도전과제 실계정 테스트 · 2026-10-24 Release

### 사용할 빌드

- 업로드 폴더: `C:/Unity/MazeParty/Builds/stove-release-20261024-achievements`
- 실행 파일: `MazeParty.exe`. 업로드 폴더 용량 약 328 MB(디버그 백업 제외).
- 버전 0.0.56 / STOVE / LIVE / UGS production / Windows x64 / IL2CPP / Development Build 해제.
- 소스: `dev/stove`의 `a133836a2deaede16bf937b190ed693193f39e30` (격투 카운트 다운 추가). 도전과제 연결 커밋 `78102620279ff0e0d1dfbb05e9e1a4c0134fbb34` 포함.
- 빌드 성공: 2026-10-24 02:51 KST. 오류 0건, API 폐기 예정·메시·셰이더 등 경고 43건. 빌드 보고서와 파일 검증: `Logs/StoveRelease20261024QA`.
- 배포용 고지·라이선스는 포함했다. Unity의 `MazeParty_BackUpThisFolder_ButDontShipItWithYourGame`은 인접한 `stove-release-20261024-achievements-symbols` 폴더로 분리해 보존했으므로 업로드하지 않는다.

**LIVE 환경은 STOVE에 업로드한 빌드를 런처에서 설치·실행해야 한다.** 로컬 경로를 연결하는 런처 개발자 모드는 Sandbox 전용이다. 위 2~3절의 업로드·설치 순서를 따른다. [공식 PC 클라이언트 안내](https://developers.onstove.com/docs/stove/setup/pc-client)

### 등록 상태 확인

1. 같은 상품에 [공통 스탯 29개](STOVE_STATS.csv)가 등록·저장되어 있는지 확인한다. 연산은 모두 MAX다.
2. [도전과제 34개](STOVE_ACHIEVEMENTS.csv)의 연결 Stat ID와 목표값(이상)을 확인한다. 01~06번 도전과제 ID는 Notion 사용자 입력값을 반영했고, 07~34번은 `스탯 ID_목표값` 규칙으로 정리한 값이므로 **Studio에 실제 표시되는 자동 할당 ID와 대조**한다.
3. 첫 번째 시상식은 Stat ID `MP_ACH_01` / 목표 1 이상 / 도전과제 ID `MP_ACH_01_1`이다. 파티의 단골손님은 같은 Stat ID / 목표 5 이상 / `MP_ACH_01_5`다.
4. 게임의 SDK 요청에는 **Stat ID와 실제 값**이 전달된다. Studio의 도전과제 연결 스탯·목표값이 맞아야 해제된다.
5. 아직 해제하지 않은 테스트 계정을 사용하거나 계정별 기존 달성 상태를 기록한다. 실제 계정에 기록되는 테스트이며 이미 해제한 도전과제는 다시 해제되지 않는다.

### 첫 번째 시상식으로 빠르게 확인

1. 서로 다른 STOVE 계정 4개와 PC 4대에서 위 테스트 빌드에 대한 이용 권한을 준비한다. 모두 같은 업로드 버전을 설치하고 각자의 STOVE 런처에서 실행한다.
2. 한 명이 방을 만들고 나머지 세 명이 코드로 참가한다. **4인으로 시작**, **숲**, **10턴**을 선택한다. 15턴·20턴도 인정되지만 첫 검증은 10턴으로 진행한다.
3. 정상 플레이로 마지막 미니게임과 보너스 정산을 마친 뒤 **시상식의 최종 순위 확정**까지 기다린다. 첫 번째 시상식에는 우승이 필요하지 않다.
4. 시상식 또는 로비에서 게임과 인터넷 연결을 유지한다. 집계는 경기 종료 시 확정되며, 대기 중인 스탯은 **전체 요청 기준 60초 간격으로 하나씩** 전송된다. 신규 계정의 첫 완주 스탯은 보통 첫 요청이지만, 다른 대기 기록이나 실패 재시도가 있으면 늦어질 수 있다.
5. STOVE 런처의 해당 게임 도전과제 목록을 새로 확인한다. 신규 계정 기준 `MP_ACH_01_1`(첫 번째 시상식), `MP_ACH_05_1`(숲 탐험가)이 해제되어야 한다. 종합 1위 계정은 `MP_ACH_03_1`도 확인한다. 알림 팝업 유무만으로 판정하지 말고 목록의 달성 상태를 확인한다.
6. 여러 조건을 달성했다면 수분 이상 기다린다. 29개 스탯이 모두 대기 중인 경우 첫 요청부터 마지막 요청까지 약 28분에 응답 시간이 더해진다. 실패·네트워크 중단 시 더 걸리며, 미전송 기록은 저장되어 같은 PC·계정의 다음 정상 로그인에서 이어간다.

### 누적·최고 기록 확인

| 확인할 도전과제 | 전송 Stat ID | 정상 완주 뒤 확인할 값과 목표 |
| --- | --- | --- |
| 첫 번째 시상식 → 파티의 단골손님 | `MP_ACH_01` | 누적 정상 완주 1 → 5회 |
| 오늘의 주인공 → 다섯 번의 왕관 | `MP_ACH_03` | 누적 종합 우승 1 → 5회 |
| 첫 열쇠 → 열쇠 상점 단골 | `MP_ACH_07` | 골드를 지불한 열쇠 구매 누적 1 → 10회 |
| 아이템 구매도 전략 → 게임은 템빨 | `MP_ACH_12` | 골드를 지불한 아이템 구매 누적 1 → 10회 |
| 첫 미니게임 우승 → 오늘은 내 무대 | `MP_ACH_25` | 정상 완주한 한 경기의 최고 미니게임 우승 수 1 → 5회 |

- 첫 경기 중 열쇠·아이템을 한 번씩 유료 구매하고 정상 완주하면 해당 첫 구매 도전과제도 확인할 수 있다. 구매 즉시가 아닌 시상식 확정 후 전송된다.
- 같은 PC·같은 계정에서 기록을 이어간다. 현재 구현은 다른 PC의 미달성 누적 기록을 조회·합산하지 않는다.
- 최고 기록 검증: 한 경기 3승 후 다음 경기 2승은 최고 3승이며 5승 도전과제가 열리면 안 된다. 한 경기에서 5승 이상하고 정상 종료했을 때 열린다. 개별 라운드 승리가 아닌 미니게임 최종 순위의 우승 횟수다.
- 10·15·20턴을 각각 정상 완주해 누적 횟수가 한 번씩 늘어나는지도 확인한다. 중도 포기·강제 결과·Editor·Development·Testbed·Solo는 인정하지 않는다.
- 로컬 기록을 삭제·편집해 초기화하지 않는다. 이미 서버에 반영된 해제 상태는 로컬 파일 삭제로 되돌아가지 않는다.

### 해제되지 않을 때

- 먼저 현재 설치본이 위 빌드인지, 네 계정의 상품 권한과 Studio의 Stat ID·MAX·목표값·연결이 맞는지 확인한다.
- 기록: `%USERPROFILE%/AppData/LocalLow/DefaultCompany/MazeParty/Player.log`. 오류 단계·시상식 완료 시각·기대 도전과제를 같이 기록한다.
- `STOVE achievement sync is pending.`은 아직 전송 성공이 확인되지 않았다는 뜻이다. Studio 스탯 등록과 인터넷 연결을 확인하고 게임을 켠 채 재시도를 기다린다.
- `Achievement journal unavailable.`은 기존 기록을 보존한 채 이번 실행의 동기화를 중단한 상태다. `Could not save achievement result.`가 뜨면 시상식을 유지하고 저장 오류를 확인한다.
- 로컬 `Achievements/<계정범위>.json`에는 누적·최고 기록과 `confirmedValues`가 저장된다. SDK가 값을 수락했다는 기록과 STOVE 도전과제 목록의 실제 해제 상태는 따로 확인해야 한다.
- 빌드와 로컬 검증은 완료했지만, 이 문서 작성 시점에는 Studio 업로드·실계정 도전과제 해제를 실행하지 않았다.

## 공식 참고

- [STOVE Studio FAQ: 업로드·테스트 빌드·소유권 테스트](https://studio-docs.onstove.com/pc/References/faq.html)
- [STOVE Studio BASIC 업로드 안내](https://studio-docs.onstove.com/pc/StudioGuide/basicrelease.html)
- [현행 STOVE PC 클라이언트 및 환경 안내](https://developers.onstove.com/docs/stove/setup/pc-client)
- [PC SDK 3.5 이상 Unity API](https://developers.onstove.com/docs/stove/reference/pc-sdk/pc-sdk-reference-unity-pc)

확인일: 2026-10-24. Studio 문서는 새 개발자센터로 연결될 수 있습니다. 업로드·테스트 권한과 화면 명칭은 로그인한 Studio의 실제 안내를 우선합니다.
