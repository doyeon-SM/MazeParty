# MazeParty prefab workflow

## Shared prefab contract

All player-visible Canvas UI is authored as a prefab under
`Assets/MazeParty/Prefabs`: use `Multiplayer/UI`, `Board/UI`,
`Minigames/Common/UI`, or `Minigames/<game>/UI` as appropriate.
Runtime components may instantiate or reference those prefabs and update state,
but must not build fallback UI with `new GameObject`, `AddComponent`, `OnGUI`, or
`GUILayout`.

Each prefab owns its layout, colors, typography, default copy, and visual child
hierarchy. A small serialized `*Bindings` component exposes the references that
runtime code needs, so designers can reorganize the hierarchy without changing
gameplay code.

Editor setup commands follow two rules:

1. Create a usable default prefab only when the asset is missing.
2. Reuse the existing prefab unchanged and validate its binding contract on all
later scene rebuilds.

The same contract applies to every new minigame HUD, including
`StableFootingHud.prefab`, `BalloonBlowHud.prefab`, and
`GiftGrabHud.prefab`: edit a prefab to change
layout or styling, while the scene keeps only serialized gameplay-object
references. Balloon Blow's fixed world labels are likewise authored once in
`BalloonBlowStationLabel.prefab`; runtime code updates only the player name,
progress, highlight, and visibility through `BalloonBlowStationLabel` bindings.
Gift Grab follows the same rule with `GiftGrabBaseLabel.prefab`, reused for both
moving player names and four base owner/gift-count signs. Its HUD root must keep
a unit scale so the screen-space Canvas remains renderable.

Scene instances must be created with `PrefabUtility.InstantiatePrefab` so their
prefab provenance remains inspectable. Editor-only windows are outside the
Canvas UI contract. New player-visible world labels must likewise come from a
serialized prefab binding; runtime code may update their content and state but
must not procedurally author their typography or layout.

## Board Canvas workflow

The canonical board HUD asset is
`Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab`.

Open it from `MazeParty > UI > Open Board Canvas Prefab`, or open the prefab
directly in the Project window. Both the online Board scene and the local board
flow testbed instantiate this same prefab. The testbed keeps simulator-only
components as prefab instance overrides.

Safe design changes include replacing Image sprites and materials; changing
fonts, colors, button transitions, spacing, anchors, and RectTransform values;
and adding decorative children, animation components, particles, or
presentation scripts. Keep the serialized binding component and its required
references valid. Item-selection and shop-offer buttons must also retain their
`BoardItemChoiceButton` components so hover descriptions continue to work.

`MazeParty > Gameplay > Rebuild Board Flow Prototype` reuses the existing
prefab and does not overwrite these design changes. If the prefab is deleted,
the command creates a new default prefab as a bootstrap.

The prefab root must keep `Canvas`, `CanvasScaler`, `GraphicRaycaster`,
`BoardEventSystemBootstrap`, and `BoardFlowView`. Scene rebuild validates this
contract and stops with a descriptive error if a required binding is missing.

## World presentation workflow

Player, lobby, board-die, and minigame environment visuals follow the same
missing-only authoring rule as Canvas UI:

- `Multiplayer/PlayerAvatarPresentation.prefab` owns the shared non-networked
  player model, first-person hands, hit regions, nameplate, and top-view
  highlight. `NetworkPlayer.prefab` keeps its stable NGO root and nests this
  presentation; minigame and award stand-ins reuse it through
  `PlayerAvatarPresentationAssets`.
- `Multiplayer/World/LobbyArena.prefab` owns lobby geometry and authored spawn
  anchors. The online bootstrap scene keeps session/runtime objects outside it.
- `Board/Dice/NetworkWorldDie.prefab` is the complete networked D12 contract:
  physics, NGO components, face markers, result text, and a nested
  `D12WorldDieVisual` source. Board scene instances override only slot and die
  identity.
- Each converted minigame has one `*Environment.prefab` for static presentation.
  The in-scene NetworkState, gameplay/core prefabs, and runtime anchors retain
  scene identity and must not be moved into that environment prefab.

The setup commands seed these assets only when missing, then validate and
instantiate the existing source. Edit an existing prefab directly for art or
layout changes; setup must never recreate it as a way to repair an invalid
binding. Production runtime code updates values, visibility, pose, and tint but
does not assemble replacement presentation geometry.

## Nested modules and repeated families

`Board/UI/BoardCanvas.prefab` composes its reconnect and minigame-ready panels
from connected prefabs under `Board/UI/Modules`. Change those modules at their
source so the board and testbed stay in sync.

Repeated objects that are structurally identical share one prefab source and
use scene-instance transforms for slot differences. Current shared families are
Bouncing Balls balls and Cliff Barrage projectiles/laser rigs. Do not split a
family into numbered prefab copies unless its serialized component contract or
visual hierarchy genuinely diverges.

`RaceHud`, `TagChaseHud`, and `CliffBarrageHud` are recovery-only migration
assets. Production scenes use `MinigameCommonHud`; do not use the legacy HUDs as
new design sources.
