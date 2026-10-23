# Google Sheets 현지화

[MazeParty 현지화](https://docs.google.com/spreadsheets/d/1uiKBeUpS4oD18SEsxplsJf9O30Kb7hT3Js_x3ikT8Cc/edit)의 `Strings` 탭에서 번역을 관리한다. Google Drive의 `ChatGPT` 폴더에 만든 비공개 파일이다.

2026-10-18 생성 시점에 게임의 831개 문구와 모든 번역을 그대로 옮겼다. 연결 정보는 [Tools/LocalizationSheet.json](../Tools/LocalizationSheet.json)에 저장한다. 사용자가 선택한 방식은 **요청 시 Google Sheets → 프로젝트 반영**이다.

2026-10-24 로컬 CSV에 번체 `zh-Hant`를 추가했다. 원격 시트는 이번 작업에서 수정하지 않았다. 다음 동기화 전에 E열과 로컬의 최신 키를 시트에 반영해야 한다. 예전 4열 내보내기는 번체 유실 방지를 위해 거부한다.

## 편집 규칙

| 열 | 내용 |
| --- | --- |
| A: source | 영어 원문 겸 코드 조회 키. 기존 값의 대소문자·공백·구두점을 바꾸지 않는다. |
| B: ko | 한국어 번역 |
| C: ja | 일본어 번역 |
| D: zh-Hans | 중국어 간체 번역 |
| E: zh-Hant | 중국어 번체 번역 |

- 첫 행과 A열을 고정했다. 필터로 문구를 찾고 B~E열을 수정한다. 모든 셀은 일반 텍스트다.
- `{0}`, `{1}`, `{0:0.0}` 같은 치환 표시의 종류와 개수를 보존한다. 문장에 맞게 순서를 바꿀 수 있다.
- `\n`은 게임에서 줄바꿈이 된다. 원문과 같은 개수를 유지한다. 셀 안의 실제 줄바꿈도 가져올 때 `\n`으로 저장된다.
- 따옴표, 쉼표, 한국어·일본어·중국어 문자는 그대로 입력한다. 수식을 사용하지 않는다.
- 키 행이나 언어 열을 삭제하지 않고 번역을 비워 두지 않는다. `CLOSE`와 `Close`는 서로 다른 키다.
- 새 문구는 코드·로컬 CSV·시트에 같은 source로 추가한다. 가져오기 도구는 현재 로컬 CSV와 시트의 키 집합이 다르면 중단한다.
- 주사위의 이동/사용/굴리는 중 문구도 이 표에 있다. 마우스 아이콘은 공통 에셋이며 번역 대상이 아니다.

## 요청해서 반영하기

시트를 편집한 뒤 Codex에 **“구글시트 현지화 내용을 프로젝트에 반영해줘”**라고 요청한다.

Codex는 다음 순서로 처리한다.

1. 설정 파일의 ID로 연결된 Google Drive/Sheets에서 문서와 탭을 확인한다. 탭의 현재 행 수를 조회하고 `Strings!A1:E<행 수>` 전체 값을 읽는다. 832행으로 고정하지 않는다.
2. `get_spreadsheet_range`의 `UNFORMATTED_VALUE` 응답에서 `values` 배열 또는 `{ "majorDimension": "ROWS", "values": [...] }`를 UTF-8 JSON으로 임시 저장한다. 행/셀 값을 요약하거나 다시 번역하지 않는다. 수식이 있으면 원문 텍스트로 확정한 뒤 가져온다.
3. 아래 도구를 먼저 검증 모드로 실행하고 변경 문구를 확인한다. 검증이 통과하면 `--apply`로 반영한다.
4. 실제 CSV diff를 확인하고 Unity의 `StringTableContractTests` 및 `LocalizationRulesTests` 표적 EditMode를 실행한다. 변경이 없으면 CSV를 다시 저장하거나 테스트를 반복하지 않는다.
5. 반영 건수와 검증 결과를 보고하고 `PROJECT_MEMORY.md`를 갱신한다.

게임 실행이나 Unity 시작 때 Google에 접속하지 않는다. 빌드는 기존 Resources CSV를 사용하므로, 번역을 반영한 뒤 새 빌드에 포함해야 배포 게임에 적용된다. 프로젝트의 변경을 Google Sheets에 자동으로 덮어쓰지 않는다.

## 직접 가져오기

Google Sheets의 `파일 → 다운로드 → 쉼표로 구분된 값(.csv)`로 **Strings 탭**을 내려받아도 같은 도구를 사용할 수 있다. Python 3.9 이상이 필요하며 추가 패키지는 없다. 저장소 루트에서 실행한다.

```powershell
python -B Tools/SyncLocalizationFromSheet.py --input "C:/경로/Strings.csv"
python -B Tools/SyncLocalizationFromSheet.py --input "C:/경로/Strings.csv" --apply
```

연결 도구에서 읽은 JSON도 `--input "C:/경로/sheet-values.json"`으로 처리한다. 이 스크립트 자체에는 Google 인증·다운로드 기능이 없다. 비공개 시트 접근은 연결된 Google 계정 또는 사용자의 브라우저가 담당한다.

검증 실패 시 게임 CSV를 바꾸지 않는다. 키 누락·추가·중복, 빈 번역, 치환 표시/줄바꿈 불일치, 잘못된 열 구성, 숫자 등 텍스트가 아닌 JSON 셀을 거부한다. 키는 게임과 같은 방식으로 이스케이프를 해석한 후 대소문자를 구분해 검사한다.

적용할 변경이 있으면 기존 CSV를 `Logs/LocalizationSync/StringTable-<UTC 시각>.csv`에 바이트 그대로 백업한 후 같은 디렉터리의 임시 파일을 원자적으로 교체한다. `.meta`와 GUID는 건드리지 않는다. 검증 도중 로컬 CSV가 바뀌면 중단한다. 기존 행 순서·UTF-8 BOM 여부·줄 끝 형식을 보존하며, 의미상 변경이 없으면 원본 파일의 바이트를 그대로 유지한다.

복원할 때는 출력된 백업 CSV를 원래 `Assets/MazeParty/Resources/MazeParty/Localization/StringTable.csv`에 복사하고 `.meta`는 유지한다. 백업에는 반영 직전의 미커밋 번역도 포함된다.

도구의 저장·손상 방지 테스트:

```powershell
python -B -m unittest discover -s Tools/tests -p test_localization_sheet_sync.py -v
```
