# Player name font

Add a licensed Korean-capable dynamic font at this exact Resources address:

- `Assets/Resources/MazeParty/Fonts/PlayerNameFont.ttf`, or
- `Assets/Resources/MazeParty/Fonts/PlayerNameFont.otf`

The lobby input and world-space player name automatically use it. Until the file
is supplied, Unity's `LegacyRuntime.ttf` fallback is used and Korean glyphs may be
missing even though Korean names are saved and synchronized correctly.
