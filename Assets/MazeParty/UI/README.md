# MazeParty UI prefab workflow

## Shared prefab contract

All player-visible Canvas UI is authored as a prefab under `UI/Prefabs`.
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

Scene instances must be created with `PrefabUtility.InstantiatePrefab` so their
prefab provenance remains inspectable. Editor-only windows and non-Canvas world
presentation such as character nameplates or shop signs are outside this Canvas
UI contract.

## Board Canvas workflow

The canonical board HUD asset is
`Assets/MazeParty/UI/Prefabs/BoardCanvas.prefab`.

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
