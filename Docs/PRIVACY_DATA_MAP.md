# 개인정보 데이터 경로·로컬 삭제 안내

확인일: 2026-10-26. 현재 코드와 Unity 6000.6.0f1 / Authentication 3.7.4 기준. 이 문서는 내부 운영·공개 초안 근거이며 미확정 처리 국가·법적 근거를 대신하지 않는다.

## 온라인 처리

| 구분 | 현재 처리 | 삭제·보유 범위 |
| --- | --- | --- |
| Steam/STOVE | 플랫폼 실행 계정·이용권 확인, 닉네임·도전과제·통계·초대 | 플랫폼 독립 기록은 각 플랫폼 절차. 게임의 UGS 삭제 버튼으로 삭제하지 않음 |
| Unity Authentication | 별도 익명 Player ID, 로그인 자격; 플랫폼 ID는 로컬 캐시 프로필 구분에 사용 | ID는 계정 삭제까지. 현재 인증된 계정의 DeleteAccountAsync 성공 후 SDK가 SignOut(true)로 현재 프로필의 세션 토큰·Player ID·이름 캐시와 메모리 접근 토큰을 정리 |
| Lobby/MPS | Player ID·IP, 방/참가 상태, 개발자 정의 닉네임·공개키 등 | 공식 안내상 IP·ID의 별도 저장은 없지만 로그 ID 최대 30일. 사용자 정의 정보에 같은 기간을 자동 적용하지 않음 |
| Relay | IP·Player ID 및 중계 게임 통신 | 공식 안내 기본 30일. Authentication과 별도 삭제 절차 |
| 참가자/호스트 PC | 참가자 식별자·닉네임·외형·경기 상태를 전달하고 복구 스냅샷에 일부 보관 | 아래 로컬 저장. 운영자가 다른 이용자 PC의 파일을 일괄 원격 삭제할 수 없음 |
| Gmail | 지원 메일·첨부·답장 | 기존 확정대로 일반 문의 처리 완료 후 최대 30일 내 관련 받은/보낸 메일과 휴지통 영구 삭제. 실제 삭제를 실행한 기록은 아님 |

방의 개발자 정의 필드에 실명·연락처 등 개인정보를 넣지 말라는 Unity 조건과 닉네임 운영의 대조는 남아 있다. 문의·처리 국가 사전 문의는 사용자 결정대로 생략한다. 실제 권리 요청 지원까지 생략하는 것은 아니다.

## Windows PC 저장 위치

기준 폴더: `%USERPROFILE%\AppData\LocalLow\HYGY Game\MazeParty`

저장 경로의 `HYGY Game`은 현재 PlayerSettings.companyName 그대로다. 대외 브랜드 HYGY Games와 다르지만 경로를 바꾸면 기존 저장 접근이 달라지므로 이번에는 변경하지 않았다. 이하 경로는 기준 폴더 아래다.

| 항목 | 위치 | 보관·정리 동작 |
| --- | --- | --- |
| 경기 복구 | `MazeParty\{steam 또는 stove}\{서비스 환경}\release-local-v2\HostMatchState\MatchRecovery\*.a.json`, `*.b.json` | 마지막 저장부터 기본 72시간 유효. 해당 저장을 읽을 때 만료면 A/B 삭제. 앱 미실행·파일 손상·정리 실패·더 이상 참조하지 않는 파일은 즉시 자동 파기를 보장하지 않음. 정상 완료·포기 등에서 대상 파일 정리. 개발용/이전 저장은 `MazeParty\HostMatchState\MatchRecovery`도 가능 |
| 호스트 미니게임 순서 | `MazeParty\HostMatchState\MinigameSchedules\*.json` | 시드·순서·경기 키. 별도 72시간 TTL 없음. 정상 완료·경기 포기/교체 경로에서 해당 파일 삭제 |
| 도전과제 처리 기록 | `Achievements\{로컬 프로필}.json` | 누적 수치·해금 비트·반영 경기 ID 등. 기간 자동 만료 없음. 로컬 삭제 시 미전송 기록/누적 판정 영향. 이미 플랫폼에 반영된 도전과제는 별개 |
| 임시 저장 파일 | 위 저장 폴더의 `.tmp` | 정상 쓰기에서는 정리하나 강제 종료로 잔존 가능. 수동 정리 시 해당 파일도 포함 |
| 로컬 플레이 로그 | `Player.log` 및 존재하는 `Player-prev.log` | 현재 `usePlayerLog=1`. 엔진 진단 업로드 OFF와 별개. 고정 일수 파기 설정은 없음. 게임/SDK 오류·접속 환경 등이 남을 수 있어 출시 실행본에서 실제 항목·교체 동작 확인 필요 |

게임 설정·프로필·인증 캐시 등은 Windows 레지스트리 `HKEY_CURRENT_USER\Software\HYGY Game\MazeParty`의 PlayerPrefs에 저장한다. 같은 Windows 사용자에서 Steam/STOVE 캐시가 이 키에 함께 존재할 수 있다. Unity Editor의 개발용 PlayerPrefs는 별도 위치이므로 개발 환경 초기화에 아래 이용자용 절차를 그대로 사용하지 않는다.

- 설정: `MazeParty.Settings.*` — 음량·언어·해상도·조작·접근성 등; 시간 만료 없음.
- 프로필: `MazeParty.PlayerProfile.*` — 닉네임·외형·감정표현. 현재 기본값/실행 인자 기준이며 UGS 계정 삭제로 함께 삭제하지 않음.
- 인증 캐시: `{프로젝트 ID}.{인증 프로필}.unity.services.authentication.*` — Player ID·세션 토큰 등; 현재 계정 삭제 성공 시 해당 캐시 정리. 다른 프로필까지 삭제하지 않음.
- 경기/재접속 포인터: `MazeParty.Host.ActiveMinigameSchedule.*` 등 — 경기/참가자 구성 및 재접속 정보; 해당 정상 완료·나가기 경로 등에서 정리.
- 온라인 중단 표식: `MazeParty.Privacy.OnlineHold.*` — 삭제 요청 전에 저장하여 앱을 재실행해도 자동으로 익명 계정을 새로 생성하지 않도록 함. 사용자가 별도 확인해 온라인을 재개할 때 해제.

로컬 JSON·PlayerPrefs는 암호화 저장으로 설명하지 않는다. 체크섬은 손상 검출이며 암호화가 아니다. 접속 승인용 RSA 개인키는 `PersistKeyInCsp=false`로 실행 중 사용하고 공개키·서명을 통신에 사용한다. 이 기능이 모든 저장 정보의 익명화/암호화를 의미하지 않는다.

## 이용자에게 안내할 PC 데이터 삭제 방법

1. 온라인 계정 삭제도 원하는 경우, 로컬 캐시부터 지우지 말고 **먼저 게임 설정 > 개인정보 및 지원 > Unity 인증계정 삭제**를 진행한다. 진행 중인 방에서 나온 뒤 대상을 확인한다. 캐시만 지우면 기존 익명 계정에 다시 접근하지 못할 수 있으며 서버 계정이 삭제되는 것은 아니다.
2. 관련 서비스의 추가 삭제나 게임 접속 문제가 있으면 `hwangdoyeon423@gmail.com`으로 접수한다. 필요하면 화면을 떠나기 전에 문의 정보를 복사한다. 비밀번호·세션 토큰·레지스트리 내보내기 파일은 보내지 않는다.
3. 게임을 완전히 종료한다. 파일 탐색기 주소창에 위 기준 폴더를 입력한다. 경기 복구만 없애려면 해당 MatchRecovery 폴더, 로컬 도전과제 기록까지 없애려면 Achievements 폴더를 대상으로 한다. **이 PC의 MazeParty 파일을 모두 지우려는 경우에만 기준 폴더 전체를 삭제**한다. 클라우드·플랫폼 기록을 지우는 동작은 아니다.
4. 설정·인증 캐시까지 모두 초기화하려면 Windows 레지스트리 편집기에서 **정확히 `HKEY_CURRENT_USER\Software\HYGY Game\MazeParty` 키만** 삭제한다. 상위 HYGY Game/Software 키나 다른 게임 키는 삭제하지 않는다. 같은 PC의 MazeParty 설정과 여러 플랫폼/프로필 자격·중단 표식이 함께 사라지므로 전체 초기화를 원할 때만 사용한다. 불확실하면 이메일로 안내를 요청한다.
5. 온라인 중단 표식까지 지운 후 다시 게임을 실행하면 새로운 익명 계정이 생성될 수 있다. 온라인 삭제 상태를 유지하려면 게임을 다시 온라인으로 실행하지 않는다. 로컬 초기화가 플랫폼 도전과제·구매 기록을 취소하지 않는다.

운영자가 실제 이용자의 폴더·레지스트리·서버 계정을 이번 점검에서 삭제한 것은 아니다. 위 절차의 실제 사용자 PC 적용과 남아 있는 SDK 자체 로그 위치는 출시 빌드로 확인한다.

## 진단 설정과 검증 상태

Steam/STOVE 프로필의 Engine Diagnostics=Disabled(0)를 live SerializedObject로 확인했다. 프로젝트 기본값과 개발용 Windows 프로필은 별도다. 빌드 전 검사에 Disabled 외 값을 거부하도록 추가했고, 프로필을 새로 설정할 때도 Disabled를 지정한다. 공식 안내상 진단 OFF는 다른 Unity 제품의 데이터 처리까지 끄는 설정이 아니다.

이번 표적 EditMode 검증은 16/16 통과: 계정 삭제 8개, 복구 저널 6개, 개인정보 메뉴 프리팹/씬 계약 2개. 두 실제 프로필의 미저장 SerializedObject에서 Disabled 통과·Enabled/ProjectSettings 거부도 확인했다. 실제 UGS 계정 삭제·두 상점 출시 Player 실행·네트워크 관찰은 아직 하지 않았다. 별도 임시 테스트 계정과 서비스별 삭제 결과를 통해 확인해야 하며 단위 테스트를 실서비스 삭제 성공으로 표시하지 않는다.

## 공식 근거

- [Authentication](https://docs.unity.com/en-us/authentication/privacy-and-consent/overview)
- [Lobby](https://docs.unity.com/en-us/mps-sdk/privacy/privacy-overview-lobby)
- [Relay](https://docs.unity.com/en-us/mps-sdk/privacy/privacy-overview-relay)
- [Editor 진단 설정](https://docs.unity.com/en-us/cloud/developer-data/configure-diagnostics-editor)
- [Windows PlayerPrefs](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PlayerPrefs.html)
- [persistentDataPath](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-persistentDataPath.html)
