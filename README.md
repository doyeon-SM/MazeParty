# MazeParty · 미로파티

**제작: 훈연구이팀(HYGY)**

주사위로 보드를 이동하고, 미니게임과 아이템을 활용해 경쟁하는 최대 4인 온라인 파티 게임입니다. Unity로 개발 중이며 Windows용 STOVE·Steam 출시를 준비하고 있습니다.

## 현재 구현

- 비공개 방 생성과 코드 입장, 최대 4인 멀티플레이
- 보드 이동·이벤트·상점·아이템 및 미니게임 15종
- 캐릭터 꾸미기, 감정표현, 미니맵·전체 지도
- 영어·한국어·일본어·중국어 간체 UI
- 연결 종료 후 재접속과 원래 호스트의 경기 복원 흐름
- Development·STOVE·Steam 빌드 프로필과 플랫폼별 방·계정 캐시·저장 범위 분리

게임 진행은 플레이어 호스트가 담당하며, 방 관리와 네트워크 연결에는 **Unity Gaming Services(MPS/Relay)**를 사용합니다. 별도로 운영할 게임 서버나 인증 서버는 없으며, 온라인 플레이에는 인터넷과 UGS 연결이 필요합니다.

| 구분 | 현재 상태 |
| --- | --- |
| Development | UGS 익명 로그인, 한 PC의 4개 프로세스 테스트 지원 |
| STOVE | PC SDK 3.5.3.0 연동 및 Release 빌드 지원. 런처·소유권 확인 후 UGS 익명 로그인 |
| Steam | 빌드·방 분리 및 Integration Check 지원. Steam SDK 인증과 Release는 아직 미구현 |

STOVE·Steam·Development 이용자는 서로 다른 플랫폼의 방에 참가할 수 없습니다. 현재 STOVE 계정은 해당 PC의 UGS 계정 캐시를 선택하는 데 사용하며, 다른 PC에서 같은 STOVE 계정의 저장 경기를 자동 복원하는 기능은 제공하지 않습니다. 개발 계정과 저장 경기는 출시 계정으로 이전하지 않습니다.

## 개발 환경

| 항목 | 프로젝트 기준 |
| --- | --- |
| Unity | **6000.6.0f1** (`ProjectSettings/ProjectVersion.txt`) |
| 빌드 대상 | Windows x64 |
| 렌더링 | Universal Render Pipeline 17.6.0 |
| 네트워크 | Netcode for GameObjects 2.13.2, Multiplayer Services 2.3.1 |
| 입력 | Input System 1.20.0 |
| STOVE SDK | `StovePCSDK_Full_net46_CSharp_3.5.3.0_x64` |
| 빌드 도구 | Development: Mono / Store: IL2CPP, Windows IL2CPP 모듈·Visual Studio C++ 도구 필요 |

패키지의 정확한 의존성은 [manifest.json](Packages/manifest.json)과 [packages-lock.json](Packages/packages-lock.json)을 따릅니다.

## 프로젝트 열기

1. 접근 권한이 있는 Git 계정으로 저장소를 내려받고 Unity Hub에서 저장소 루트를 프로젝트로 엽니다.
2. 위 Unity 버전과 Windows IL2CPP 모듈을 설치하고 패키지 복원을 기다립니다.
3. Git에서 제외한 외부 에셋을 적법하게 확보하여 `Assets/Ignore`의 기존 경로와 `.meta` GUID를 보존한 상태로 복원합니다. 이 폴더가 빠진 새 체크아웃만으로는 전체 게임이 재현되지 않습니다.
4. 권한이 있는 Unity 프로젝트 및 UGS 환경을 사용합니다. 기본 환경명은 `production`이며, Dashboard에 실제로 존재하는 환경이어야 합니다.
5. `MazeParty > Platform > Select Development`를 선택한 뒤 `Assets/MazeParty/Scenes/Multiplayer/OnlineBootstrap.unity`를 엽니다.

STOVE 작업 시에는 별도로 발급받은 SDK와 상품 설정이 필요합니다. [STOVE 설정 안내](Docs/STOVE_SETUP.md)를 참고하세요.

## 빌드와 테스트

### 일반 개발 빌드 · 한 PC에서 4인 테스트

1. `MazeParty > Platform > Select Development`를 선택하고 컴파일을 기다립니다.
2. `MazeParty > Build > Windows Development (Mono x64)`를 실행합니다.
3. 저장소 루트 PowerShell에서 다음을 실행합니다.

```powershell
& '.\Tools\StartFourPlayerTest.ps1'
```

기본 실행 파일은 `Builds/Windows-Development/MazeParty.exe`입니다. 다른 빌드를 사용하려면 `-Executable '실행 파일의 전체 경로'`를 지정합니다. 각 창은 서로 다른 익명 프로필을 사용합니다. 한 창에서 방을 만들고 나머지 창에서 같은 코드로 입장합니다. STOVE 인증·소유권 검증은 별도로 수행해야 합니다.

### STOVE Release

1. `Assets/Stove&Steam/.local/stove.json`에 Game ID와 Application Key를 입력합니다.
2. `MazeParty > Platform > Select STOVE`를 선택하고 컴파일을 기다립니다.
3. `MazeParty > Build > Selected Store - Release (IL2CPP x64)`를 실행합니다.
4. `Builds/stove-release-날짜시각` 폴더 전체를 테스트용으로 전달합니다.

**Release는 빌드 형식이며 상점 공개를 자동 실행하지 않습니다.** 실제 설치·로그인·소유권·코드 입장 검증은 [STOVE 런처 테스트 절차](Docs/STOVE_LAUNCHER_TEST.md)를 따릅니다.

`Selected Store - Integration Check (IL2CPP x64)`에는 플랫폼 설정이 포함되지 않습니다. 컴파일·패키징 검증용으로만 사용합니다. Steam 프로필에서는 현재 이 검증 빌드만 지원합니다.

검증은 수정한 기능과 관련된 EditMode 테스트를 대상으로 합니다. 전체 테스트 실행과 제작 규칙은 [AGENTS.md](AGENTS.md)를 따릅니다.

## 로컬 SDK와 설정 관리

| 경로 | 용도 |
| --- | --- |
| `Assets/Stove&Steam/STOVE/x86_64` | STOVE DLL 2개와 플랫폼 전용 임포트 설정 |
| `Assets/Stove&Steam/.local/stove.json` | UGS 환경, SDK 환경, STOVE Game ID·Application Key |
| `Assets/Stove&Steam/.local/steam.json` | 현재는 UGS 환경만 저장 |
| `Assets/Ignore` | 별도로 복원하는 외부 에셋·폰트 원본 등 |
| `Builds`, `Logs`, `Library`, `UserSettings` | 로컬 출력·로그·캐시 |

SDK와 로컬 설정은 Git에 올리지 않습니다. **Application Secret·개인키·인증 토큰은 클라이언트 설정, README, 로그에 넣지 않습니다.** Application Key는 SDK 실행을 위해 출시 빌드에 포함되는 클라이언트 설정이며 Application Secret과 다릅니다.

로컬 SDK 폴더가 없어졌다면 다음 도구를 실행합니다. 기본 원본은 바탕화면에 압축을 푼 동일 버전 SDK이며, 다른 경로는 `-SdkSourcePath`로 지정합니다.

```powershell
& '.\Tools\RestorePlatformLocal.ps1'
```

이 도구는 누락된 파일만 복원하고 기존 JSON을 덮어쓰지 않습니다. `.git/info/exclude`에도 제외 규칙을 적용해 이전 브랜치에서 실수로 추가되는 것을 방지합니다. **Git 제외는 백업이나 삭제 방지 기능이 아닙니다.** SDK 원본과 키의 안전한 보관은 별도로 필요합니다.

## 저장소 구성과 브랜치

| 경로 | 내용 |
| --- | --- |
| `Assets/MazeParty` | 게임 코드·씬·프리팹·프로젝트 리소스 |
| `Assets/Resources/MazeParty` | 공통 런타임 리소스 및 관련 문서 |
| `Assets/Settings/Build Profiles` | Windows·STOVE·Steam 빌드 프로필 |
| `Tools` | 로컬 테스트·복구 도구 |
| `Docs` | SDK 설정과 수동 테스트 안내 |
| `ThirdPartyLicenses` | 확인한 외부 구성 요소의 라이선스·고지 원문 |
| `PROJECT_MEMORY.md` | 최종 기획, 현재 구현과 남은 작업 |
| `Backend/StoveAuthBridge` | 보존 중인 이전 구현. 현재 사용·배포하지 않음 |

`main`은 검증된 공통 기준, `dev/stove`·`dev/steam`은 각 플랫폼 작업, `dev/<기능명>`은 개별 기능 작업에 사용합니다. 검증된 변경을 `main`에 병합한 뒤 플랫폼 브랜치가 가져갑니다. 실제 빌드와 방 구분은 컴파일 정의·빌드 프로필·접속 검증 코드가 담당합니다.

## 제작 및 저작권

**MazeParty(미로파티) 제작: 훈연구이팀(HYGY)**

**Copyright © 2026 훈연구이팀(HYGY). All rights reserved.**

위 표기는 훈연구이팀(HYGY)이 직접 제작한 게임 코드·콘텐츠에 적용합니다. 별도로 허락하지 않은 자체 제작물의 복제·배포·상업적 이용을 허용하는 오픈소스 라이선스를 이 README로 부여하지 않습니다.

Unity·STOVE SDK·폰트·외부 에셋·오픈소스 구성 요소의 권리는 각 권리자에게 있으며 각 항목의 별도 라이선스가 적용됩니다. 이 프로젝트의 저작권 표기는 그 권리를 대체하거나 제한하지 않습니다.

| 주요 외부 구성 요소 | 권리자·출처 | 확인한 조건 |
| --- | --- | --- |
| KCC무럭무럭체 | 한국저작권위원회 · 공유마당 | SIL Open Font License 1.1 |
| Noto Sans JP / SC | 포함 파일의 고지: Adobe, Reserved Font Name `Source` | SIL Open Font License 1.1 |
| Liberation Sans | 포함 파일의 고지: Google Corporation / Red Hat, Inc. | SIL Open Font License 1.1 |
| Unity Simple Mini Map | Bilal ARIKAN | MIT |
| DOTween | Daniele Giardini · Demigiant | 제작사 DOTween License |
| Balloons | Loafbrr | 제공 README의 CC0 표시 |
| Unity 패키지·STOVE SDK·기타 에셋 | 각 제공자 | 해당 패키지·SDK 계약 및 개별 에셋 이용 조건 |

출처·고지 원문과 확인이 필요한 에셋은 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)에 정리했습니다. 출처 표기만으로 사용권이 생기는 것은 아니며, 이 문서는 모든 구매·배포 권한 검토가 완료되었음을 뜻하지 않습니다. 배포 빌드에도 적용되는 고지와 라이선스 원문을 함께 포함합니다.
