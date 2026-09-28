# Runtime language fonts

Runtime UI and world-space labels use the following local Resources assets:

- English / Korean: `Assets/Ignore/Resources/Font/KCCMurukmuruk.otf`
- Japanese: `Assets/Ignore/Resources/Font/NotoSansJP-Regular.ttf`
- Simplified Chinese: `Assets/Ignore/Resources/Font/NotoSansSC-Regular.ttf`

`Noto Sans SC` is the Simplified Chinese family; `TC` is Traditional Chinese.
The downloaded source packages stay in `Assets/Ignore/FontSources` so unused
weights, archives, and documents are not included in the build.

`GameFonts` loads these files by Resources path. `LocalizedFontScope` on each
player-facing prefab applies the correct font whenever the language changes.
If a local font is missing, a warning is logged and `LegacyRuntime.ttf` is used;
CJK glyph coverage is then not guaranteed.
