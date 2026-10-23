# MazeParty 외부 구성 요소 저작권 및 라이선스 고지

확인일: **2026-10-18** · 제작: **훈연구이팀(HYGY)**

이 문서는 현재 로컬 프로젝트에서 확인한 출처·라이선스 정보를 정리합니다. 폴더가 존재한다고 모두 출시 바이너리에 포함되는 것은 아닙니다. 아래 고지 원문은 각 권리자의 권리를 유지하며, HYGY의 자체 제작물에 대한 저작권 표시와 별개로 적용됩니다. 구매 영수증·라이선스 좌석·특약까지 확인한 전체 권리 감사 보고서는 아닙니다.

## 런타임 폰트

### KCC무럭무럭체

- 권리자: 한국저작권위원회(Korea Copyright Commission).
- 포함 OTF의 저작권 고지: `© 2019 Korea Copyright Commission. All rights reserved.`
- 사용 파일: `Assets/Ignore/Resources/Font/KCCMurukmuruk.otf`.
- 출처: [한국저작권위원회 공유마당](https://gongu.copyright.or.kr/gongu/wrt/wrt/view.do?wrtSn=13262151&menuNo=200026).
- 제공 출처 기록의 라이선스: **SIL Open Font License 1.1(OFL)**.
- [저작권·OFL 원문](ThirdPartyLicenses/KCC-Murukmuruk-OFL.txt), [프로젝트에 보관된 출처 기록](ThirdPartyLicenses/KCC-Murukmuruk-source.txt).

저작권 문구는 실제 OTF 메타데이터에서 확인했습니다. OFL 본문은 제공된 Noto 패키지의 동일한 SIL OFL 1.1 본문을 사용하고 KCC 권리자 고지를 구분해 수록했습니다.

### Noto Sans JP / Noto Sans SC

- 사용 파일: `NotoSansJP-Regular.ttf`, `NotoSansSC-Regular.ttf` (`Assets/Ignore/Resources/Font`).
- 실제 파일 및 다운로드 패키지 고지: `Copyright 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source'`.
- 라이선스: **SIL Open Font License 1.1**.
- [JP 패키지의 OFL 원문](ThirdPartyLicenses/Noto-Sans-JP-OFL.txt), [SC 패키지의 OFL 원문](ThirdPartyLicenses/Noto-Sans-SC-OFL.txt).
- Noto Sans TC 원본은 별도 보관 중이며 현재 언어 설정의 중국어 간체 폰트는 SC입니다.

OFL 적용 폰트의 저작권과 라이선스 고지를 보존합니다. 폰트 자체 판매, 수정본의 예약 이름 사용, 재배포 조건은 [공식 OFL 1.1](https://openfontlicense.org/open-font-license-official-text/)을 따릅니다.

### Liberation Sans / TextMesh Pro 부속 폰트

- `Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt`에 포함된 고지는 Google Corporation(2010), Red Hat, Inc.(2012)를 명시합니다.
- [Liberation Sans 고지·OFL 원문](ThirdPartyLicenses/LiberationSans-OFL.txt)을 보존했습니다.
- TextMesh Pro Examples & Extras의 추가 폰트 고지는 [TMP-Examples](ThirdPartyLicenses/TMP-Examples) 폴더에 보존했습니다. 이 예제 폰트 목록이 현재 게임의 실제 사용 목록을 뜻하지는 않습니다.

## 코드·도구·외부 그래픽

| 구성 요소 | 출처·권리자 | 조건 및 근거 |
| --- | --- | --- |
| Unity Simple Mini Map | [Bilal ARIKAN](https://github.com/bilal-arikan/UnitySimpleMiniMap), Copyright (c) 2022 | MIT. [원문](ThirdPartyLicenses/UnitySimpleMiniMap-MIT.txt) 보존 |
| DOTween | [Daniele Giardini · Demigiant](https://dotween.demigiant.com/license.php) | 기본 DOTween은 제작사 자체 라이선스. MIT로 간주하지 않음. 로컬 저작권 고지(2014–2026)와 제작사 링크를 담은 [README 원문](ThirdPartyLicenses/DOTween-readme.txt) 보존. Pro 사용 시 별도 조건 확인 |
| Balloons | [Loafbrr](https://loafbrr.itch.io/) | 로컬 README에 CC0 표시. [제공 README 원문](ThirdPartyLicenses/Loafbrr-Balloons-readme.txt) 보존 |
| Fantasy Skybox FREE | [Render Knight](https://render-knight.com/) · [제품 페이지](https://assetstore.unity.com/packages/2d/textures-materials/sky/fantasy-skybox-free-18353) | 로컬 README로 출처 확인. 구매·취득 당시 제품 라이선스 확인 필요 |
| All In 1 VFX Toolkit | [제품 페이지](https://assetstore.unity.com/packages/vfx/all-in-1-vfx-toolkit-206665) | 로컬 README로 제품 출처 확인. 라이선스 범위·좌석·구매 증빙 확인 필요 |
| Unity Engine / Unity 패키지 | Unity Technologies 및 각 구성 요소의 권리자 | Unity 약관, 각 패키지의 LICENSE·Third Party Notices 적용. 현재 설치 패키지의 원문을 [Unity-Packages](ThirdPartyLicenses/Unity-Packages)에 보존 |
| STOVE PC SDK 3.5.3.0 | Smilegate/STOVE 및 SDK에 명시된 각 권리자 | 발급·다운로드 시 적용된 SDK 약관과 배포 조건 적용. SDK 원본은 Git 재배포 대상에 포함하지 않음 |

Unity 패키지 고지는 설치된 패키지 버전의 스냅샷입니다. 개발 도구와 간접 의존성도 포함할 수 있으며, 업데이트 후 다시 확인해야 합니다. 각 폴더의 `PACKAGE.txt`에 원래 패키지 이름·버전·원본 파일 위치를 기록합니다.

## 개별 이용 조건 확인이 남은 로컬 에셋

다음은 폴더명 기준 조사 목록입니다. 폴더명만으로 권리자, Unity Asset Store 표준 약관 적용 여부, 상업 이용권 확보를 확정하지 않습니다.

- `BTM_Assets`, `Collection Dice set for role-playing games`, `Fantasy Lowpoly Pack (Demo)`
- `FreeLowpolyScifiObjects`, `Icon_NCI`, `Input Sprites for TextMesh Pro`
- `Low Poly Pistol Weapon Pack 2`, `Maze`, `Modern UI Pack`, `nappin`
- `Pack_PartyCharacters`, `Pandazole_Ultimate_Pack`, `Polytope Studio`
- `Retro Cartoon UI Pack`, `SimpleHands`, `ToyBox`

`AIImage`, `CodexVfxMaterials`, `MazePartyGenerated`, `VfxPreviewCaptures`처럼 프로젝트 생성물·캡처로 보이는 폴더도 이름만으로 출처나 권리를 판정하지 않습니다. 원본 출처와 제작·생성 기록, 사용 조건을 별도로 확인합니다. `Resources`, `FontSources`, `Plugins` 하위 항목은 위 고지 및 실제 사용 목록과 함께 관리합니다.

출시 전에는 실제 포함되는 모델·텍스처·UI·효과음·음악별로 **제품 URL, 제작자, 취득일·증빙, 라이선스 종류, 필수 크레딧, 배포 허용 범위**를 확인합니다. [Unity Asset Store EULA](https://unity.com/legal/as-terms)와 [공식 FAQ](https://assetstore.unity.com/browse/eula-faq)는 기본 참고 자료이며, 비표준·제한 에셋의 별도 조건을 대신하지 않습니다. 외부 에셋의 원본 파일을 저장소 공개나 별도 파일 묶음으로 재배포할 권한이 자동으로 주어지는 것은 아닙니다.

## 빌드에 고지 포함하기

배포할 게임 폴더의 최상위에 이 `THIRD_PARTY_NOTICES.md`와 `ThirdPartyLicenses` 폴더를 함께 둡니다. 파일 내부의 상대 링크를 유지하도록 같은 이름과 구조를 사용합니다. SDK DLL·폰트 바이너리·유료 에셋 원본을 이 고지 폴더에 추가하지 않습니다.

이번 STOVE Release 출력에는 위 고지 사본을 함께 배치합니다. 현재 빌드 메뉴의 자동 복사 기능은 아니므로 이후 빌드에도 같은 고지를 수동으로 포함해야 합니다. 배포 대상이나 의존성이 바뀌면 고지 목록도 갱신합니다.

## Steamworks.NET

Steamworks.NET 2025.164.1 — Riley Labrecque, MIT. Source: https://github.com/rlabrecque/Steamworks.NET . License: ThirdPartyLicenses/Steamworks.NET.txt. Native Steamworks SDK redistribution remains subject to Valve's Steamworks SDK terms.
