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

현재 기본 `stoveEnvironment`는 `real`입니다. 3.5 SDK의 일반 런처 경로로 검증하며, 구형 SDK 2.x 문서의 AppSecret·개발자 모드 설정을 이 빌드에 그대로 적용하지 않습니다. 현행 PC 클라이언트 문서는 로컬 개발자 모드를 Sandbox 전용으로 안내하므로, 별도 Sandbox 환경을 발급받지 않았다면 환경 문자열을 임의로 바꾸거나 `PolicyConfig.json`으로 우회하지 않습니다.

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

## 공식 참고

- [STOVE Studio FAQ: 업로드·테스트 빌드·소유권 테스트](https://studio-docs.onstove.com/pc/References/faq.html)
- [STOVE Studio BASIC 업로드 안내](https://studio-docs.onstove.com/pc/StudioGuide/basicrelease.html)
- [현행 STOVE PC 클라이언트 및 환경 안내](https://developers.onstove.com/docs/stove/setup/pc-client)
- [PC SDK 3.5 이상 Unity API](https://developers.onstove.com/docs/stove/reference/pc-sdk/pc-sdk-reference-unity-pc)

확인일: 2026-10-18. Studio 문서는 새 개발자센터로 연결될 수 있습니다. 업로드·테스트 권한과 화면 명칭은 로그인한 Studio의 실제 안내를 우선합니다.
