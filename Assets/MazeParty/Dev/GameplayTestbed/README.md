# Gameplay Testbed

GameplayTestbed.unity is an offline development scene and is intentionally excluded from Build Settings.

## Open and rebuild

- Open Assets/MazeParty/Dev/GameplayTestbed/GameplayTestbed.unity.
- Rebuild it from MazeParty > Gameplay > Rebuild Gameplay Testbed.
- The generator preserves the currently open scene by building additively and then restoring it.

## Controls

- WASD: movement in every camera mode
- LMB: active item / primary action
- RMB: interaction / secondary action
- F1: first-person camera
- F2: board top view
- F3: minigame camera
- Escape: release or recapture the pointer in first-person mode
- R: reset the testbed

F1/F2/F3/R are editor-test shortcuts only. Item choice cannot be changed with wheel or number keys.

## Verification scenarios

1. Start in board top view and use F1/F2/F3 to verify Cinemachine blending.
2. Click START ACTION / END TOP VIEW. Confirm ACTION starts at 03:00, CHOOSE at 30.0s, and HP SHIELD at 5.0s from the same instant.
3. While the item choice panel is open and HP SHIELD is active, click REMOTE HIT + PUSH. HP must stay at 100 while the player is displaced; the choice panel must remain open.
4. Wait until HP SHIELD reads OFF, without choosing an item, then click REMOTE HIT + PUSH again. HP must fall to 80 and push must still apply while the choice panel stays open.
5. Choose Pulse Blaster. Confirm the bottom-center slot uses the gold active background and bottom-right AMMO reads 7. Fire with LMB and confirm ammo decreases.
6. Restart and make no choice for 30 seconds. Confirm automatic TIMEOUT / NO ITEM; the shared ACTION clock must already be near 02:30.
7. With all three slots full, click TRY ADD REWARD. Confirm acquisition is rejected without a discard prompt.
8. Select an item and click END ACTION / CLEAR ITEM. Confirm the item is removed and the slot highlight ends.

## Architecture note

Runtime interfaces, phase timing, health/damage, push, inventory, input, and camera service live in MazeParty.Gameplay. The testbed controller is a local adapter. A future online/Steam adapter should supply the authoritative shared start timestamp, validate item uses and hits on the host/server, and synchronize each player's personal choice independently.
