using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private sealed class PlantedMine
        {
            public int OwnerSlot;
            public Vector3 Position;
            public float ArmRemaining;
        }
        private sealed class Grenade
        {
            public int OwnerSlot;
            public Vector3 Position, Velocity;
            public float Life;
        }
        private readonly List<PlantedMine> _boardMines = new List<PlantedMine>();
        private readonly List<Grenade> _boardGrenades = new List<Grenade>();
        private readonly List<GameObject> _grenadeViews = new List<GameObject>();
        private float _nextItemSync;
        private readonly NetworkPlayerAvatar[] _mineRecipients = new NetworkPlayerAvatar[4];
        private int _lastGrenadeViewCount;

        public bool TryUseSelectedItemOnServer(NetworkPlayerAvatar avatar, Vector3 claimedOrigin, Vector3 claimedDirection)
        {
            if (!CanProcessActionRequest(avatar) || !avatar.CanUseItemChargeOnServer ||
                !IsFinite(claimedOrigin) || !IsFinite(claimedDirection) || claimedDirection.sqrMagnitude < .0001f)
                return false;
            var id = avatar.GetSelectedItemOnServer();
            if (!PrototypeItemCatalog.IsValid(id) || id == PrototypeItemId.DoubleDice || id == PrototypeItemId.LowDice || id == PrototypeItemId.HighDice ||
                id == PrototypeItemId.PositionSwapper) return false;
            var item = PrototypeItemCatalog.Get(id);
            var origin = avatar.EyePivot != null ? avatar.EyePivot.position : avatar.transform.position + Vector3.up * .75f;
            var direction = avatar.EyePivot != null ? avatar.EyePivot.forward : avatar.transform.forward;
            if (Vector3.Distance(origin, claimedOrigin) > 1.5f || Vector3.Dot(direction, claimedDirection.normalized) < .94f)
                return false;

            RaycastHit ground = default;
            if (id == PrototypeItemId.Mine &&
                (!BoardItemPhysics.Cast(origin, direction, item.Range, avatar.gameObject, out ground) ||
                 ground.normal.y < .5f || ground.collider.GetComponentInParent<NetworkPlayerAvatar>() != null)) return false;
            if (!avatar.SpendItemChargeOnServer(item)) return false;
            avatar.PresentItemUseOnServer(id);
            if (id == PrototypeItemId.Cloak) avatar.ActivateCloakOnServer();
            else if (id == PrototypeItemId.Pistol || id == PrototypeItemId.Sniper)
            {
                var endpoint = origin + direction * item.Range;
                if (BoardItemPhysics.Cast(origin, direction, item.Range, avatar.gameObject, out var hit))
                {
                    endpoint = hit.point;
                    var target = hit.collider.GetComponentInParent<NetworkPlayerAvatar>();
                    if (target != null) target.ApplyDamage(new DamageRequest(item.Damage, DamageKind.Item, avatar.gameObject));
                }
                PresentBoardShotRpc(origin, endpoint);
            }
            else if (id == PrototypeItemId.Grenade)
            {
                var planar = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
                if (planar.sqrMagnitude < .01f) planar = avatar.transform.forward;
                float range = item.Range;
                if (BoardItemPhysics.Cast(origin, direction, item.Range, avatar.gameObject, out var aimHit))
                    range = Mathf.Min(range, Vector3.ProjectOnPlane(aimHit.point - origin, Vector3.up).magnitude);
                var target = origin + planar * range;
                // Aim at board floor: the sweep, rather than a client collision, decides the impact.
                target.y = avatar.transform.position.y - 1f;
                var flight = Mathf.Max(.1f, item.ThrowFlightSeconds);
                _boardGrenades.Add(new Grenade { OwnerSlot = avatar.AssignedSlot, Position = origin,
                    Velocity = (target - origin) / flight - Physics.gravity * (.5f * flight) });
            }
            else if (id == PrototypeItemId.Mine)
            {
                _boardMines.Add(new PlantedMine { OwnerSlot = avatar.AssignedSlot,
                    Position = ground.point + Vector3.up * .06f, ArmRemaining = item.ArmingDelay });
                SyncBoardMines();
            }
            return true;
        }

        private void TickBoardItemWorld()
        {
            if (!GameplayEnabled) return;
            for (int slot = 0; slot < 4; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                if (avatar != null && avatar.IsBoardReady && _mineRecipients[slot] != avatar)
                { SyncBoardMines(); break; }
            }
            if (FlowState == BoardFlowState.MatchComplete)
            {
                if (_boardMines.Count > 0) { _boardMines.Clear(); SyncBoardMines(); }
                if (_boardGrenades.Count > 0) { _boardGrenades.Clear(); SyncBoardGrenadesRpc(Array.Empty<Vector3>()); }
                return;
            }
            if (!IsActionPhase)
            {
                // An airborne consumable cannot deal damage in the following minigame.
                if (_boardGrenades.Count > 0) { _boardGrenades.Clear(); SyncBoardGrenadesRpc(Array.Empty<Vector3>()); }
            }
            if (IsActionPhase && !IsGlobalSimulationPaused)
            {
                var dt = Time.unscaledDeltaTime;
                var bomb = PrototypeItemCatalog.Get(PrototypeItemId.Grenade);
                for (int i = _boardGrenades.Count - 1; i >= 0; i--)
                {
                    var grenade = _boardGrenades[i];
                    // Small swept steps keep the parabola stable under a long host frame.
                    float remaining = dt;
                    bool exploded = false;
                    while (remaining > 0f && !exploded)
                    {
                        float step = Mathf.Min(remaining, .02f);
                        remaining -= step;
                        var delta = grenade.Velocity * step + Physics.gravity * (.5f * step * step);
                        var owner = GetAvatarForSlot(grenade.OwnerSlot);
                        bool hit = BoardItemPhysics.Cast(grenade.Position, delta.normalized, delta.magnitude,
                            owner != null ? owner.gameObject : null, out var collision, bomb.ProjectileRadius);
                        grenade.Position = hit ? collision.point + collision.normal * .08f : grenade.Position + delta;
                        grenade.Velocity += Physics.gravity * step;
                        grenade.Life += step;
                        if (hit || grenade.Life >= bomb.ProjectileLifetime)
                        {
                            ExplodeBoardItem(grenade.Position, grenade.OwnerSlot, bomb);
                            _boardGrenades.RemoveAt(i);
                            exploded = true;
                        }
                    }
                }
                var mineDefinition = PrototypeItemCatalog.Get(PrototypeItemId.Mine);
                bool minesChanged = false;
                for (int i = _boardMines.Count - 1; i >= 0; i--)
                {
                    var mine = _boardMines[i];
                    mine.ArmRemaining = Mathf.Max(0f, mine.ArmRemaining - dt);
                    if (mine.ArmRemaining > 0f) continue;
                    for (int slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                    {
                        var target = GetAvatarForSlot(slot);
                        if (slot == mine.OwnerSlot || target == null || target.CurrentHealth <= 0 ||
                            Vector3.Distance(target.transform.position, mine.Position) > mineDefinition.TriggerRadius ||
                            !BoardItemPhysics.HasBlastLineOfSight(mine.Position + Vector3.up * .15f, target.transform.position)) continue;
                        _boardMines.RemoveAt(i);
                        ExplodeBoardItem(mine.Position, mine.OwnerSlot, mineDefinition);
                        minesChanged = true;
                        break;
                    }
                }
                if (minesChanged) SyncBoardMines();
            }
            _nextItemSync -= Time.unscaledDeltaTime;
            if (_nextItemSync <= 0f)
            {
                _nextItemSync = .05f;
                var positions = new Vector3[_boardGrenades.Count];
                for (int i = 0; i < positions.Length; i++) positions[i] = _boardGrenades[i].Position;
                if (positions.Length > 0 || _lastGrenadeViewCount > 0) SyncBoardGrenadesRpc(positions);
                _lastGrenadeViewCount = positions.Length;
            }
        }

        private void ExplodeBoardItem(Vector3 position, int ownerSlot, BoardItemDefinition item)
        {
            var owner = GetAvatarForSlot(ownerSlot);
            for (int slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var target = GetAvatarForSlot(slot);
                if (target == null || target.CurrentHealth <= 0 ||
                    Vector3.Distance(position, target.transform.position) > item.BlastRadius ||
                    !BoardItemPhysics.HasBlastLineOfSight(position + Vector3.up * .15f, target.transform.position)) continue;
                target.ApplyDamage(new DamageRequest(item.Damage, DamageKind.Item, owner != null ? owner.gameObject : null));
            }
            PresentBoardExplosionRpc(position, (byte)item.Id);
        }

        public void SyncBoardMines()
        {
            if (!IsServer) return;
            for (int slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                if (avatar == null) continue;
                _mineRecipients[slot] = avatar;
                var positions = new List<Vector3>();
                foreach (var mine in _boardMines) if (mine.OwnerSlot == slot) positions.Add(mine.Position);
                avatar.SendMinePositionsOnServer(positions.ToArray());
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncBoardGrenadesRpc(Vector3[] positions)
        {
            var prefab = PrototypeItemCatalog.Get(PrototypeItemId.Grenade).WorldPrefab;
            while (_grenadeViews.Count < positions.Length) _grenadeViews.Add(Instantiate(prefab));
            for (int i = _grenadeViews.Count - 1; i >= positions.Length; i--)
            { Destroy(_grenadeViews[i]); _grenadeViews.RemoveAt(i); }
            for (int i = 0; i < positions.Length; i++) _grenadeViews[i].transform.position = positions[i];
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PresentBoardExplosionRpc(Vector3 position, byte itemId)
        {
            var item = PrototypeItemCatalog.Get((PrototypeItemId)itemId);
            if (item.ExplosionPrefab == null) return;
            var view = Instantiate(item.ExplosionPrefab, position, Quaternion.identity);
            view.transform.localScale = Vector3.one * item.BlastRadius * 2f;
            Destroy(view, .3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PresentBoardShotRpc(Vector3 origin, Vector3 end)
        {
            var prefab = Resources.Load<GameObject>("MazeParty/ItemViews/Shot");
            if (prefab == null) return;
            var view = Instantiate(prefab, (origin + end) * .5f, Quaternion.LookRotation(end - origin));
            view.transform.localScale = new Vector3(.025f, .025f, Vector3.Distance(origin, end));
            Destroy(view, .08f);
        }

        private void ClearBoardItemWorld()
        {
            _boardMines.Clear(); _boardGrenades.Clear();
            foreach (var view in _grenadeViews) if (view != null) Destroy(view);
            _grenadeViews.Clear();
        }
    }
}
