# STOVE 빌드와 코드 입장 멀티플레이

브랜치: `dev/stove`. SDK: `StovePCSDK_Full_net46_CSharp_3.5.3.0_x64` Full 신규 API.

## 현재 연결 방식

별도 인증 서버나 게임 서버를 배포하지 않는다. 게임 진행은 기존 플레이어 호스트가 담당하고, 방과 연결은 기존 Unity UGS/MPS/Relay를 사용한다. 인터넷과 Unity 서비스는 계속 필요하다.

STOVE 빌드의 순서는 런처 확인 → SDK 초기화 → 게임 소유권 확인 → STOVE 계정 확인 → 플랫폼 전용 로컬 프로필로 UGS 익명 로그인이다. SDK/소유권 확인이 실패하면 UGS 로그인으로 우회하지 않는다. STOVE 사용자 토큰을 별도 서버에 보내거나 호스트에 전달하지 않는다.

UGS는 자체 익명 계정을 인증한다. STOVE ID는 해당 PC의 계정 캐시를 선택하는 데만 사용하며, 서버에서 검증된 STOVE↔UGS 계정 연결이 아니다. 같은 PC의 같은 프로필은 캐시된 UGS 계정을 재사용한다. 개발 계정이나 이전 서버 연동 프로필을 가져오지 않는다. PC 변경·인증 캐시 삭제·재설치 후의 계정/경기 복구는 보장하지 않는다. 같은 STOVE 계정의 다른 PC 복원은 별도 계정 연동 과제다.

Steam은 별도 컴파일 정의와 방 구분을 유지한다. Steam SDK 인증은 아직 구현하지 않아 Steam 프로필의 온라인 로그인과 Release 빌드는 차단되어 있으며 Integration Check만 가능하다.

## SDK와 로컬 파일

- SDK DLL 2개: `Assets/Stove&Steam/STOVE/x86_64/BaseSDK.dll`, `BaseSDK_NET.dll`.
- 클라이언트 설정: `Assets/Stove&Steam/.local/stove.json`, `steam.json`.
- 폴더 내용과 루트 `.meta`까지 Git에서 제외한다. `.local`은 Unity 임포트에서도 제외된다.
- 다른 PC에서는 같은 버전의 DLL을 복원하고 `MazeParty > Platform > Set up profiles and local settings`로 STOVE 전용 임포터 제약을 적용한다.
- 설정 폴더는 `MazeParty > Platform > Open local settings folder`로 연다.
- 다운로드 원본과 `Builds/` 출력은 기존 위치에 유지한다. 실행 파일을 Assets로 복사하지 않는다.

## 로컬 폴더가 없어졌을 때 복구

저장소 루트 PowerShell에서 `& '.\Tools\RestorePlatformLocal.ps1'`을 실행한다. 기본 원본은 바탕화면의 `StovePCSDK_Full_net46_CSharp_3.5.3.0_x64` 폴더이며, 다른 위치라면 `-SdkSourcePath '압축 해제 폴더 전체 경로'`를 지정한다.

이 도구는 3.5.3.0 DLL 2개의 버전·SHA-256을 확인하고 누락된 DLL·원래 GUID의 임포트 설정·빈 JSON만 복원한다. 기존 JSON과 메타데이터는 덮어쓰지 않는다. Unity 임포트가 끝나면 `MazeParty > Platform > Set up profiles and local settings`를 실행한다. 이전 키는 복원하지 않으므로 `stoveGameId`, `stoveAppKey`를 다시 입력한다.

`.gitignore` 외에 해당 저장소의 `.git/info/exclude`에도 폴더와 루트 `.meta` 제외 규칙을 추가한다. 이는 오래된 브랜치로 바뀌어도 실수로 Git에 추가되는 것을 막기 위한 로컬 규칙이며 다른 PC에 전달되지 않는다. Git 제외는 백업이나 삭제 방지 기능이 아니다. `git clean -fdx` 또는 Git 앱의 ignored 파일 삭제 옵션은 로컬 SDK와 설정도 삭제할 수 있으므로 사용하지 않는다. SDK 원본은 저장소 밖에 유지하고, 입력한 키는 별도의 안전한 보관소에서 관리한다.

## stove.json 입력

설정에는 다음 네 필드만 사용한다. 2026-10-18 사용자가 Game ID/Application Key를 다시 입력했고 설정을 포함한 STOVE Release 빌드를 생성했다. 입력값은 출력하지 않고 형식·빌드 포함 여부만 확인했다. 실제 상품키·소유권은 사용자가 런처에서 검증한다.

```json
{
  "ugsEnvironment": "production",
  "stoveEnvironment": "LIVE",
  "stoveGameId": "",
  "stoveAppKey": ""
}
```

| 필드 | 입력 기준 |
| --- | --- |
| ugsEnvironment | 이미 사용하는 Unity Dashboard 환경. 기본값은 기존 개발 환경인 `production`. 별도 UGS 환경을 만든 경우에만 해당 이름으로 변경 |
| stoveEnvironment | 일반 STOVE 클라이언트와 Studio 테스트 빌드는 `LIVE`. 별도 Sandbox 계정·런처를 발급받은 경우에만 `SANDBOX`. 기존 `real`은 게임 코드에서 `LIVE`로 변환하며 SDK에 그대로 전달하지 않음 |
| stoveGameId | Studio 상품키 화면의 **게임ID** |
| stoveAppKey | 같은 화면의 **Application Key** |

Studio의 **상품 No.**와 **Application Secret**은 입력하지 않는다. AppKey는 클라이언트 SDK 실행에 필요한 값이므로 출시 출력에 포함된다. Application Secret을 실행 파일에 넣거나 익명 로그인 비밀번호로 사용하지 않는다.

`authBridgeUrl`, `grantKeyModulus`, 서버 서명 키, 서버 API 자격증명은 필요하지 않다. 예전 `Backend/StoveAuthBridge` 소스는 보존했지만 현재 클라이언트가 호출하지 않으며 배포하지 않는다.

## 런처 오류 308과 환경 설정

2026-10-19 실제 PC SDK 3.5.3.0 로그에서 `real` 입력이 `sandbox`로 처리되어, `live` 런처와 환경이 달라 IPC 인증 키 교환에 실패한 것을 확인했다. 308은 `IpcAesKeyNotReceived`다. 이전 문서의 `real` 권장값을 정정한다. 같은 공식 레퍼런스에도 예시가 혼재하므로 이 프로젝트는 실제 로그와 신규 초기화 가이드의 `LIVE`/`SANDBOX`를 기준으로 한다.

기본값·복구 템플릿·로컬 설정은 `LIVE`를 사용한다. 기존 설정의 `real`과 대소문자/주변 공백은 SDK 호출과 빌드 출력 전에 정규화하고, 알 수 없는 환경은 빌드/로그인 전에 거부한다. UGS 로컬 프로필도 같은 정규화 값을 사용해 Live 표기 차이로 계정이 분리되지 않게 한다. Sandbox는 별도 프로필이다.

기존 업로드본의 코드는 새 설정 규칙을 포함하지 않으므로 **수정된 코드로 다시 빌드 → Studio 재업로드 → 런처에서 새 빌드 설치**가 필요하다. JSON만 바꾼 뒤 이전 EXE를 재사용하지 않는다. SDK·소유권 실패 시 접속 차단은 유지한다.

## 플랫폼별 방 분리와 접속 보안

- 동일한 UGS 환경을 쓰더라도 STOVE/Steam/Development의 세션 타입·호스트 방 속성·연결 페이로드·계정 프로필·저장 경로를 구분한다. 다른 플랫폼/환경/버전의 연결은 거부한다. 플랫폼 명칭은 변조 클라이언트에 대한 플랫폼 구매 인증서가 아니다.
- 방은 비공개, 최대 4인, 코드 입장, Relay DTLS를 유지한다. 코드 형식과 클라이언트 시도 간격을 제한한다. 친구 초대는 추가하지 않는다.
- STOVE 클라이언트는 프로세스별 RSA 개인키를 메모리에만 두고, 공개키를 **자신의 UGS 멤버 속성**에 등록한다. 방장은 서명뿐 아니라 UGS에서 받은 해당 플레이어의 등록 공개키와 실제 방 참가 여부를 확인한다. 다른 멤버의 공개키를 복사하는 것만으로 그 개인키 서명을 만들 수는 없다.
- 접속 서명은 방 코드/ID·플랫폼·환경·버전·UGS 계정·시각·nonce를 포함한다. 유효 기간 60초, 미래 시각 허용 오차 10초이며 변조·재전송·중복 활성 연결·예약되지 않은 경기 좌석을 거부한다. 요청 크기와 동시 승인 수도 제한한다.
- 프로세스 재시작 후 재접속은 UGS 인증으로 자기 멤버의 공개키를 먼저 갱신한 다음 MPS/NGO 연결을 시작한다. 개인키·UGS 토큰·STOVE 토큰은 방장에게 보내지 않는다. 승인된 연결과 좌석의 대응은 호스트가 기록한다.
- 이 구조는 **UGS 계정의 멤버 소유와 연결 키**를 확인한다. STOVE 구매 권한을 다른 클라이언트가 독립 검증하거나 변조된 SDK를 서버에서 차단하는 구조는 아니다. 방 코드 유출, 정상 계정의 자리 선점, 악의적인 방장의 게임 상태 조작, 서비스 한도 악용을 모두 막지는 못한다.

## 빌드 방법

1. `MazeParty > Platform > Select STOVE`를 선택하고 컴파일이 끝날 때까지 기다린다.
2. 키를 입력하고 `MazeParty > Build > Selected Store - Release (IL2CPP x64)`를 실행한다.
3. `Builds/stove-release-날짜시각/MazeParty.exe`가 생성된다. Windows x64/IL2CPP와 유효한 클라이언트 설정을 검사한다.
4. 키 입력 전 컴파일·패키징만 확인하려면 `Selected Store - Integration Check (IL2CPP x64)`를 사용한다. 이 출력에는 설정을 넣지 않으므로 STOVE 로그인·출시·업로드 용도가 아니다.
5. Steam 검증은 `Select Steam`에서 Integration Check를 사용한다. STOVE DLL을 제외한다.

Unity 6000.6.0f1 Windows IL2CPP 모듈과 Visual Studio C++ 도구가 필요하다. 현재 PC에는 설치되어 있다. 실제 STOVE 실행은 Studio의 빌드 등록·실행 경로·테스터 권한을 설정한 뒤 런처를 통해 검증한다.

## 설정 전 혼자 4인 테스트

1. `MazeParty > Platform > Select Development`를 선택하고 컴파일을 기다린다.
2. `MazeParty > Build > Windows Development (Mono x64)`로 한 번 빌드한다.
3. 기본 결과는 `Builds/Windows-Development/MazeParty.exe`. 이동 시 실행 파일과 Data 폴더 등 **출력 폴더 전체**를 함께 옮긴다.
4. 저장소 루트 PowerShell에서 `.\Tools\StartFourPlayerTest.ps1`을 실행한다. 별도 출력 경로는 `-Executable '전체 exe 경로'`로 지정한다.
5. 한 창에서 방을 만들고 나머지 3개에서 같은 코드로 입장한다.

실행 도구는 `-auth-profile local4-p1`부터 `local4-p4`로 계정을 구분한다. 같은 프로필의 창을 중복 실행하지 않는다. 빌드는 한 종류면 된다. STOVE 설정이나 별도 서버는 필요 없지만 기존 UGS 연결을 위한 인터넷은 필요하다. 이 검증은 일반 게임 흐름용이며 실제 STOVE 인증·소유권 검사를 대신하지 않는다.

## 런처 수동 테스트

Release 빌드 업로드부터 설치·접속·방 생성까지는 [STOVE 런처 테스트 절차](STOVE_LAUNCHER_TEST.md)를 따른다. 게임 제작자는 훈연구이팀(HYGY)이며 [외부 구성 요소 고지](../THIRD_PARTY_NOTICES.md)를 배포 폴더에도 포함한다.

## 남은 실제 검증

- Studio 값 입력 후 런처 실행, 정상/미소유/미로그인 계정, SDK 실패 시 UGS 로그인 차단.
- 서로 다른 STOVE 계정과 PC에서 방 생성·코드 입장·게임 진행·퇴장·클라이언트 재시작·원래 호스트 복귀.
- 같은 PC 재실행의 UGS 계정 유지와 STOVE 계정 전환 시 캐시 분리.
- 다른 플랫폼/환경의 코드 입장 거부, 오래된 공개키·위조 서명·재전송·좌석 사칭 거부.
- Steam SDK 인증, 도전과제, 약관, 저작권, 출시 운영 점검은 별도 남은 작업이다.

## 공식 근거

- [STOVE 신규 Unity PC SDK](https://developers.onstove.com/docs/stove/reference/pc-sdk/pc-sdk-reference-unity-pc)
- [Unity 익명 로그인과 세션 유지](https://docs.unity.com/en-us/authentication/session-management)
- [Unity 계정 인증 방식과 다른 기기 복원](https://docs.unity.com/en-us/authentication/approaches-to-authentication)
- [UGS 멤버 데이터 쓰기 권한과 제한](https://docs.unity.com/en-us/mps-sdk/session-data-and-player-data)
