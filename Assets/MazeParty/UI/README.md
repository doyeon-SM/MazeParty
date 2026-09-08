# Board UI prefab workflow

The canonical board HUD asset is
`Assets/MazeParty/UI/Prefabs/BoardCanvas.prefab`.

Open it from `MazeParty > UI > Open Board Canvas Prefab`, or open the prefab
directly in the Project window. Both the online Board scene and the local board
flow testbed instantiate this same prefab. The testbed keeps its simulator-only
components as prefab instance overrides.

## Safe design changes

- Replace Image sprites and materials.
- Change fonts, colors, button transitions, spacing, anchors, and RectTransform
  values.
- Add decorative children, animation components, particles, and presentation
  scripts that do not replace the runtime binding components.
- Keep visible copy in English until the Korean font asset is added.

`MazeParty > Gameplay > Rebuild Board Flow Prototype` reuses the existing
prefab and does not overwrite these design changes. If the prefab is deleted,
the command creates a new default prefab as a bootstrap.

## Protected runtime contract

Do not rename or remove the UI objects bound by `BoardFlowView`, including the
turn/phase/timer text, four player cards and their state fields, three inventory
slots, item selection buttons, item shop offers, ready/result panels, reticle,
and reconnect overlay. Keep the expected Text, Image, and Button components on
those objects. Item selection and shop offer buttons must also keep their
BoardItemChoiceButton component so hover descriptions continue to work.

The prefab root must keep Canvas, CanvasScaler, GraphicRaycaster,
BoardEventSystemBootstrap, and BoardFlowView. Scene rebuild validates this
contract and stops with a descriptive error if a required anchor is missing.
