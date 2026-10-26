# MazeParty Steam 연결

기준: 2026-10-24, `dev/steam`, App ID **5495650**, Steamworks.NET **2025.164.1** (Steamworks SDK 1.64). 사용자 요청에 따라 **게임 코드만 연결**했으며 Steamworks 웹 등록·게시·빌드 업로드는 수행하지 않았다.

## 설정과 실행

- Unity Package Manager의 Git 의존성은 버전 태그로 고정한다. `Packages/manifest.json`·`packages-lock.json`을 함께 유지한다.
- `Assets/Stove&Steam/.local/steam.json`에는 `ugsEnvironment`와 공개 `steamAppId`만 사용한다. 현재 값은 `production`, `5495650`이다. Steam Web API 키·퍼블리셔 키·암호·티켓을 요구하거나 빌드에 넣지 않는다.
- `MazeParty > Platform > Select Steam`을 선택하고 컴파일을 기다린다. 게임 실행 시 Steam 계정 로그인, 해당 App ID, 게임 소유권을 확인한 뒤 기존 UGS 익명 인증과 MPS/Relay를 사용한다. 실패하면 익명 개발 계정으로 우회하지 않는다.
- 이 연결은 **로컬 Steam 확인 + 계정별 UGS 캐시**다. Unity Authentication의 `SignInWithSteamAsync` 또는 서버의 Steam 티켓 검증을 구현한 것이 아니다. UGS Steam 외부 로그인 연결은 별도 대시보드·서버 비밀정보 설정이 필요한 후속 범위다.
- UGS 프로필·도전과제 파일은 Steam ID, App ID, UGS 환경으로 분리한다. 다른 PC의 UGS 저장 경기 자동 복원·누적 도전과제 기록 합산은 지원하지 않는다. STOVE/개발 기록도 이전하지 않는다.
- 로컬 Editor 테스트용 `steam_appid.txt`는 루트에 `5495650`으로 작성되어 Git에서 제외한다. 이 파일을 배포하지 않는다. Steam 클라이언트의 로그인 계정에 앱 이용 권한이 있어야 한다. Steam에서 실행한 플레이어 빌드에서 오버레이를 검증한다.

## 닉네임

- Steam 인증 시 `SteamFriends.GetPersonaName()`으로 현재 프로필 이름을 읽고 로비 멤버 정보로 전송한다. 로컬에 저장된 수동 닉네임은 Steam 이름을 덮어쓰지 않으며 기존 닉네임 입력 필드는 읽기 전용이다.
- 로비·보드·미니게임·시상식·통계는 서버가 세션 멤버에서 확인한 공통 이름을 사용한다. 재접속 시 로비 이름을 먼저 갱신하고 저장된 옛 닉네임보다 현재 세션 닉네임을 우선한다.
- 기존 게임 표시 길이 제한(UTF-16 16자, UTF-8 60바이트)과 줄바꿈 제거를 적용한다. 이모지의 서로게이트 쌍은 중간에서 자르지 않으며 꺾쇠는 전각 문자로 표시해 리치 텍스트 태그로 해석되지 않게 한다. 게임 진행 중 Steam 프로필에서 바꾼 이름은 다음 인증/방 입장 시 반영한다.

## 친구 초대

- `LobbyCanvas.prefab`의 직렬화된 `steamInviteButton`을 사용한다. Steam 대기실에 **Steam 친구 초대** 버튼이 나타나며 빈자리·정상 대기 상태·오버레이 사용 가능 상태에서 활성화된다.
- 버튼은 `ActivateGameOverlayInviteDialogConnectString`으로 Steam의 친구 선택 화면을 연다. 게임이 친구에게 자동 메시지를 보내거나 임의의 친구를 선택하지 않는다.
- `connect` Rich Presence는 참가 가능한 방에 있을 때만 설정한다. 만원·경기 시작·복구 전용 방·작업 중·퇴장·종료 시 지운다.
- 실행 중에는 `GameRichPresenceJoinRequested_t`, 종료 상태에서는 실행 인자, 추가 Steam URL 실행은 `NewUrlLaunchParameters_t`와 `GetLaunchCommandLine`으로 수락을 처리한다.
- 연결 형식은 `+mp_join <UGS 환경>/<게임 버전>/<방 코드>`다. 허용된 문자·길이·동일 버전·동일 환경을 검사한다. 명령 실행·URL 이동은 하지 않는다.
- 로그인/초기화 중 받은 초대 하나는 최대 90초 대기한다. 이미 방에 있으면 안내하고 거부하며 자동으로 기존 경기를 끝내지 않는다. 실제 참가는 기존 방 코드 경로를 사용하여 정원·좌석·플랫폼·게임 버전 검증을 그대로 거친다.
- UI 생성은 Editor의 `MazeParty > Setup > Upgrade Steam Invite UI`에서 수행한다. 기존 바인딩이 있으면 디자인을 수정하지 않는다. 런타임에는 UI를 조립하지 않는다.

## 도전과제

- 기존 STOVE와 같은 34개 판정·29개 공통 스탯·아이콘을 사용한다. [STEAM_ACHIEVEMENTS.csv](STEAM_ACHIEVEMENTS.csv)에 API 이름·연결 스탯·목표·기존 아이콘 경로를 정리했다. CSV는 등록 참고표이며 Steamworks 공식 업로드 양식은 아니다.
- Steamworks에 **INT 스탯 29개**를 등록한다. 이름은 `MP_ACH_01`, `MP_ACH_03` 등 CSV의 고유 `stat_api_name`을 따른다. 초기값 0, 음수 불허, 단조 증가 기준으로 설정한다. `MP_ACH_01/03/07/12`는 누적 횟수, `MP_ACH_25`는 한 경기 최고 미니게임 승수, 나머지 24개는 0/1이다.
- 도전과제 **34개**의 API 이름은 `MP_ACH_01_1`, `MP_ACH_01_5`처럼 CSV의 `api_name`을 정확히 사용한다. 클라이언트가 `SetAchievement`로 해제하므로 클라이언트 설정을 허용해야 한다. 표시 이름·설명·아이콘은 기존 확정안을 등록하고 변경을 게시해야 실제 SDK 조회가 성공한다.
- Release 빌드에서 4인으로 시작한 10/15/20턴 정식 경기의 최종 시상식 확정 시만 기록한다. Editor/Development/Testbed/Solo·강제 결과는 제외한다. 판정·무효화·체크포인트·중복 결과 방지는 기존 공통 코드를 사용한다.
- Steam SDK 1.64에서는 현재 사용자 스탯을 클라이언트가 실행 전에 동기화한다. 삭제된 `RequestCurrentStats` API를 호출하지 않으며 `GetStat`·`GetAchievement`로 전체 스키마를 확인한 뒤 사용한다.
- 각 스탯은 `max(Steam 캐시 값, 해당 PC의 누적 값)`으로 기록하여 서버 값을 내리지 않는다. 이것은 여러 PC의 카운터 합산이 아니다. 모든 미니게임 경험·선물 강탈의 미완료 조각도 현재 PC의 저널에만 남는다.
- 해제 후 `StoreStats`의 성공 콜백에서만 전송 당시 로컬 값을 확인 처리한다. 전송 중 새 결과는 다음 요청으로 남는다. 실패·스키마 미등록은 60초 간격으로 재시도한다. 응답이 30초 동안 없으면 콜백의 요청 식별자가 없으므로 현재 세션에서 후속 전송을 중지하고 다음 실행 때 저널을 다시 보낸다.
- 기존 Steam 해제 기록을 초기화하거나 취소하는 API는 사용하지 않는다. 저널 손상 시 기존 파일을 보존하고 해당 실행의 전송을 중지한다.

## 빌드와 남은 실기 확인

1. Steamworks에 해당 계정의 앱 이용 권한과 Windows 실행 파일 경로를 준비한다. 도전과제·스탯 34/29개 등록·게시가 필요하다. 이 작업에서는 웹을 수정하지 않았다.
2. Steam 프로필 선택 후 `MazeParty > Build > Selected Store - Release (IL2CPP x64)`로 빌드한다. Release 설정에는 Steam App ID·UGS 환경만 포함하며 STOVE DLL은 제외한다. 반대로 STOVE/Development 빌드는 Steam 네이티브 DLL을 제외한다. `steam_appid.txt`가 출력 폴더에 있으면 빌드 검증을 실패시킨다.
3. `Integration Check`는 설정 파일 없는 개발 검증 빌드다. 온라인·실적 해제 검증이나 업로드에 사용하지 않는다.
4. Steam을 통해 실행한 서로 다른 두 계정/PC에서 오버레이 열기 → 초대 → 실행 중 수락/종료 상태 수락 → 같은 대기실 입장을 확인한다. 만원·진행 중·다른 버전·기존 방 참가 상태에서도 거부되는지 확인한다.
5. 도전과제 실기는 Release 4인 정상 경기로 진행한다. 한 경기 정상 종료, 완주 1→5, 구매 1→10, 미니게임 한 경기 최고 1→5, 중도 종료 무효, 재접속 후 중복 방지, 다음 실행 재전송을 확인한다.

코드 컴파일·EditMode 테스트는 실제 Steam 로그인, 오버레이 또는 계정 해제 성공을 대체하지 않는다. 테스트 결과는 `PROJECT_MEMORY.md`에 별도로 기록한다.

공식 근거: [Steamworks.NET 설치](https://steamworks.github.io/installation/), [고정 버전](https://github.com/rlabrecque/Steamworks.NET/releases/tag/2025.164.1), [Steam 도전과제](https://partner.steamgames.com/doc/features/achievements), [ISteamFriends](https://partner.steamgames.com/doc/api/ISteamFriends), [ISteamUserStats](https://partner.steamgames.com/doc/api/ISteamUserStats).
