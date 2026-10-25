# 외부 에셋 점검 · 2026-10-26

대상: dev/stove, Unity 6000.6.0f1. 실제 사용 후보를 줄이기 위해 활성 씬 22개, Editor 외 Resources, preloaded assets를 합친 312개 루트의 별 장식 제거 후 재귀 의존성 1,665개를 조회했다. 최종 IL2CPP BuildReport가 아니므로 모든 경로가 출시 바이너리에 포함된다는 의미는 아니다. 코드의 동적 로드·SDK·StreamingAssets는 별도로 대조한다. 상세 경로는 [JSON 목록](LEGAL_ASSET_INVENTORY.json)에 기록했다.

## 상품 식별과 조건

메타파일의 AssetOrigin 제품 ID·버전을 사용했다. 아래 17개 상품의 현재 공식 페이지에서 표준 EULA 표시를 확인했다. 취득 당시 계약·권리 증명과 같은 것은 아니다. 사용자는 유료·무료 에셋을 본인 계정으로 취득했고 혼자 제작한다고 이미 확인했다. 추가 인력이 생기면 좌석 조건을 다시 대조한다. 전체 원본의 별도 재배포 권한으로 해석하지 않는다. [공식 EULA](https://unity.com/legal/as-terms).

| 로컬 폴더 | 제품 ID / 로컬 버전 | 제품·출처 | 확인 결과 |
| --- | --- | --- | --- |
| AllIn1VfxToolkit | 206665 / 2.32 | [All In 1 Vfx Toolkit](https://assetstore.unity.com/packages/vfx/all-in-1-vfx-toolkit-206665) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| BTM_Assets | 73764 / 1.3 | [Simple Gems and Items Ultimate Animated Customizable Pack](https://assetstore.unity.com/packages/3d/props/simple-gems-and-items-ultimate-animated-customizable-pack-73764) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| Christmas Gifts | 52190 / 2.0 | [Christmas Gifts](https://assetstore.unity.com/packages/3d/props/interior/christmas-gifts-52190) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| Fantasy Lowpoly Pack (Demo) | 301518 / 1.0 | [Fantasy Lowpoly Pack (Demo)](https://assetstore.unity.com/packages/3d/environments/fantasy/fantasy-lowpoly-pack-demo-301518) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| Fantasy Skybox FREE | 18353 / 1.6.5 | [Fantasy Skybox FREE](https://assetstore.unity.com/packages/2d/textures-materials/sky/fantasy-skybox-free-18353) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| FreeLowpolyScifiObjects | 339074 / 1.0 | [Free Lowpoly Scifi Objects](https://assetstore.unity.com/packages/3d/environments/sci-fi/free-lowpoly-scifi-objects-339074) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| Input Sprites for TextMesh Pro | 310114 / 1.01 | [GamePad and Keyboard Input Sprites for TextMesh Pro](https://assetstore.unity.com/packages/2d/gui/icons/gamepad-and-keyboard-input-sprites-for-textmesh-pro-310114) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| LoafbrrAssets | 307245 / 1.0 | Balloons | 로컬 README CC0 |
| Maze | 302221 / 1.0 | [Maze Modular Puzzle Kit](https://assetstore.unity.com/packages/3d/environments/maze-modular-puzzle-kit-302221) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| Modern UI Pack | 201717 / 5.5.29 | [Modern UI Pack](https://assetstore.unity.com/packages/tools/gui/modern-ui-pack-201717) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| Pack_PartyCharacters | 339036 / 1.3 | [Party Game Character Pack](https://assetstore.unity.com/packages/3d/characters/humanoids/party-game-character-pack-339036?locale=ko-KR) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| Pandazole_Ultimate_Pack | 212621 / 1.0.0 | [Pandazole - Nature Environment Lowpoly Pack](https://assetstore.unity.com/packages/3d/environments/pandazole-nature-environment-low-poly-pack-212621) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| Plugins | 27676 / 1.3.045 | DOTween (HOTween v2) | Demigiant 자체 라이선스 / 기존 고지 보존 |
| Polytope Studio | 187052 / 1.1.1 | [Low Poly Environment - Nature Free - LOWPOLY MEDIEVAL FANTASY SERIES](https://assetstore.unity.com/packages/3d/environments/lowpoly-environment-nature-pack-free-187052) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| SimpleHands | 221297 / 1.0 | [Stylized - Simple Hands](https://assetstore.unity.com/packages/3d/characters/stylized-simple-hands-221297) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| SpaceZeta_StreetLamps2 | 260395 / 1.0 | [Street Lamps 2](https://assetstore.unity.com/packages/3d/props/exterior/street-lamps-2-260395) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| Stylized 3D Animated Chests – FREE Demo | 360542 / 1.0 | [Stylized 3D Animated Chests \u2013 FREE Demo](https://assetstore.unity.com/packages/3d/props/stylized-3d-animated-chests-free-demo-360542) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| ToyBox | 68107 / 1.0 | [Block World](https://assetstore.unity.com/packages/3d/environments/block-world-68107) | 공식 상품 페이지: Standard Unity Asset Store EULA |
| UnitySimpleMiniMap | 226420 / 1.0.0 | Simple Mini Map | MIT / 기존 고지 보존 |
| nappin | 259025 / 2.0.0 | [Weapons Pack - Free](https://assetstore.unity.com/packages/3d/props/weapons/weapons-pack-free-259025) | 공식 상품 페이지: Standard Unity Asset Store EULA |

## 아이콘 출처 확인 및 사용 중단한 별 이미지

- `Assets/Ignore/Icon_NCI/free-icon-door-key-63432.png`: [기획서 9. 에셋 및 폰트](https://app.notion.com/p/3d3c227fda0380f6a34df09eff208ac0)에 같은 파일명 이미지와 “키 아이콘 제작자: Magnific - Flaticon” 고지가 있으며 사용자도 재확인했다. 이를 기준으로 출처·제작자 확인으로 정정한다. 앞선 검색 결과의 Freepik 표기를 최종 제작자명으로 확정하지 않는다. 실제 배포 고지는 사용자가 제공한 Magnific 표기를 보존한다.
- `Assets/Ignore/Icon_NCI/free-icon-dollar-coin-7022685.png`: 같은 기획서 항목에 동일 파일명 이미지와 “동전 아이콘 제작자: Icon mania - Flaticon” 고지가 있으며 사용자도 재확인했다. 출처·제작자 확인으로 정정하고 배포 고지에 반영했다.
- `Assets/Ignore/Retro Cartoon UI Pack/Retro Cartoon UI Resources/SlicedElements/15Complete/LittleStars.png`: 사용자 결정으로 사용 중단. LobbyCanvas의 Party Stars와 MinigameScheduleTower·MinigameResultCanvas·AwardCeremonyCanvas의 Retro Stars 오브젝트를 각 1개 제거했다. 현재 활성 씬·Resources·preloaded assets의 의존성을 다시 조회한 결과 이 이미지와 Retro Cartoon UI Pack의 포함 후보는 0개이며 열린 게임 씬의 이미지 참조도 0개다. 원본 패키지는 보관하되 출시 사용 목록·라이선스 확인 대기 목록에서 제외한다. 다시 사용하기 전에는 출처·조건 확인이 필요하다. UI 바인딩·씬 프리팹 계약 표적 테스트 2/2 통과. 실제 새 Player 빌드/업로드는 하지 않았다.
- 동전·열쇠는 출처 미상 목록에서 제외한다. 사용자가 제공한 출처 링크는 https://www.flaticon.com/kr/free-icons/ 이며 위 두 제작자 고지를 THIRD_PARTY_NOTICES.md에 추가했다. 출처 확인과 고지 작성은 전체 라이선스 조건 준수·실제 공개 완료와 구분한다. AI 이미지 교체 계획은 유지하며 BGM 적용 결과는 아래에 기록한다.
- 최종 SFX 파일별 Sound Effect Lab 출처 대조는 기존 출시 예정 작업으로 남긴다.

## Suno BGM 재다운로드 및 연결 교체 — 2026-10-26

사용자는 결제 후 기존 BGM을 2026-10-26(Asia/Seoul)에 다시 다운로드했다고 확인했다. 첨부 결제내역에서 SUNO INC. 및 16,500원 결제를 확인했다. 구독 등급은 이미지에 표시되지 않으므로 독립 확인 범위를 확대하지 않는다. 개인 이메일·결제수단 정보와 영수증 이미지는 배포 고지에 넣지 않았다.

| 적용 위치 | 새 파일 (Assets/Resources/sound/SunoBGM/) | 삭제한 이전 파일 |
|---|---|---|
| 로비 | Arcade Pulse.mp3 | BGMlobby.mp3 |
| 보드 | Labyrinth's Heart.mp3 | BGMboard.mp3 |
| 미니게임 슬롯 1 | Indigo Pulse.mp3 | BGMminigame1.mp3 |
| 미니게임 슬롯 2 | Indigo Corridors.mp3 | BGMminigame2.mp3 |

- Desktop 다운로드 원본과 프로젝트 파일 4개의 SHA-256이 모두 일치한다. 해시는 LEGAL_ASSET_INVENTORY.json의 suno_bgm에 보관한다.
- SoundCue 3개의 클립 참조를 교체하고 기존 볼륨·루프·랜덤 선택·페이드, Streaming 및 백그라운드 로딩 설정을 유지했다. 미니게임 20종은 공통 Indigo 2곡으로 연결된다.
- 씬·프리팹·ScriptableObject 2,023개 루트에서 이전 파일 참조가 없음을 확인한 뒤 이전 MP3 4개와 .meta를 삭제했다. 작업 복구본은 Assets 밖의 Logs/SunoBgmReplacement-2026-10-26-dbb3caf2f89848e9b7afec988c16243d에만 보관한다.
- 오디오 시스템 계약 및 BGM 흐름 표적 테스트 2/2 통과. 최종 콘솔 오류 0개. 실제 청음·새 Player 빌드·스토어 업로드는 실행하지 않았다.
- 결제·재다운로드·프로젝트 적용은 완료다. 이 기록이 모든 입력 자료의 권리 또는 전체 출시 법률 점검 완료를 뜻하지 않는다. 최신 공식 정책 근거는 https://suno.com/terms 및 https://help.suno.com/en/articles/9601665, https://help.suno.com/en/articles/13614785 이다.

## 고지 동봉

기존 폰트·MIT·CC0·DOTween·Steamworks.NET 고지는 유지한다. 설치 패키지 36개의 최상위 LICENSE/Third Party Notices 텍스트 52개를 대조했고, 빠진 Visual Studio 패키지 ThirdPartyNotices.md를 추가했다. 나머지는 줄바꿈/BOM 차이를 제외한 본문이 일치한다. 개발 도구도 포함된 보수적인 고지 묶음이며 실제 런타임 목록과 동일하지 않다.

`PlatformBuildUtility`의 Windows x64 후처리에서 `THIRD_PARTY_NOTICES.md`와 `ThirdPartyLicenses`의 .txt/.md를 상대 폴더 구조대로 자동 복사한다. 원본 문서가 없거나 고지 묶음이 비어 있으면 빌드를 실패시킨다. SDK DLL·원본 이미지·실행 파일은 고지 폴더에서 복사하지 않는다. 문서 포함은 소재 사용권 확보 자체를 대신하지 않는다.

복사 검사에서 안내 문서 1개와 고지 원문 101개, 총 102개가 원본과 바이트 단위로 일치했다. 프로젝트 루트를 출력으로 잘못 지정한 경우도 거부했다. 이 검사는 실제 IL2CPP 빌드 성공을 뜻하지 않는다.

최종 출시 후보의 BuildReport·업로드 디포 파일 목록과 한 번 더 대조한다. 현 작업은 플랫폼 업로드·공개·구매를 실행하지 않았다.
