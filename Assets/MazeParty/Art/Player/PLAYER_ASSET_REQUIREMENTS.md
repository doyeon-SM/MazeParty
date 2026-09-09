# Player asset requirements

The current prototype is generated from Unity primitives and already supports the
complete gameplay/customization seams. Art can replace these fixed anchors later.

## Coordinate and scale

- Unity units: 1 unit = 1 metre.
- Player root origin: centre of the standing CharacterController.
- Forward/face direction: local +Z.
- Standing controller: height 2.0, radius 0.5, centre Y 0.0.
- Crouching controller: height 1.2, radius 0.5, centre Y -0.4.
- Keep pivots centred at each attachment anchor and apply transforms before export.

## Modular deliverables

- Body: one rounded/clay standing mesh. Crouch is currently a runtime height squash.
- Head: one rounded head mesh.
- Hands: separate left and right meshes.
- Eyes: paired resource per `EyeId`.
- Mouth: one resource per `MouthId`.
- Hats: one resource per `HatId`; `0=None`, `1=Test Hat` is implemented.
- Outfits: `0=None`. Material-only outfits may reuse the body; silhouette-changing
  coats should provide a standing mesh and a crouch-safe mesh or accept Y squashing.
- Held/used items: one local +Z-facing model for each `PrototypeItemId`. The current
  placeholders cover `PulseBlaster`, `PushMine`, and `MedKit`. Use a centred pivot,
  baked transforms, no collider/Rigidbody, and keep the item within roughly a
  0.7-metre cube so the same model fits the world and first-person item anchors.

Recommended source format is FBX for meshes and PNG/TGA for optional masks. Use one
shared URP Lit material family with metallic 0 and low-to-medium smoothness for the
soft clay look. Thick black outlines are intentionally excluded; black is reserved
for eyes and mouth.

## Optional authored animation upgrade

The prototype currently animates rigid parts procedurally (idle, move/crouch,
alternating left/right punch, and localized body/head/hand hit wobble). If a skinned
rig replaces it later, provide looping Idle, Move and CrouchMove, plus non-looping
PunchLeft, PunchRight, HitBody, HitHead, and HitHand clips, all in-place with no root
motion. Hit clips must animate only the visual rig and must never move the player
root, camera pivot, controller, or hitboxes.
