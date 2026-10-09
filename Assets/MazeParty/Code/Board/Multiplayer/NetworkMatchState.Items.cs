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
            public uint PresentationId;
            public int OwnerSlot;
            public Vector3 Position, Velocity;
            public float Life, FlightDuration;
        }
        private readonly List<PlantedMine> _boardMines = new List<PlantedMine>();
        private readonly List<Grenade> _boardGrenades = new List<Grenade>();
        private readonly Dictionary<uint, GameObject> _grenadeViews =
            new Dictionary<uint, GameObject>();
        private readonly GrenadePresentationTrackSet _grenadePresentationTracks =
            new GrenadePresentationTrackSet(.05d, .1d);
        private readonly List<uint> _addedGrenadeViewIds = new List<uint>();
        private readonly List<uint> _removedGrenadeViewIds = new List<uint>();
        private float _nextItemSync;
        private readonly NetworkPlayerAvatar[] _mineRecipients = new NetworkPlayerAvatar[4];
        private int _lastGrenadeViewCount;
        private uint _nextGrenadePresentationId;

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
            var minePosition = default(Vector3);
            if (id == PrototypeItemId.Mine &&
                (!BoardItemPhysics.Cast(origin, direction, item.Range, avatar.gameObject, out ground) ||
                 ground.collider.GetComponentInParent<NetworkPlayerAvatar>() != null ||
                 !BoardItemLifecycleRules.TryGetMinePlacementPosition(
                     _boardTopology,
                     ground.point,
                     ground.normal,
                     out minePosition))) return false;
            if (!avatar.SpendItemChargeOnServer(item)) return false;
            avatar.PresentItemUseOnServer(id);
            if (id == PrototypeItemId.Cloak) avatar.ActivateCloakOnServer();
            else if (id == PrototypeItemId.Pistol || id == PrototypeItemId.Sniper)
            {
                var endpoint = origin + direction * item.Range;
                var didHit = BoardItemPhysics.Cast(
                    origin,
                    direction,
                    item.Range,
                    avatar.gameObject,
                    out var hit);
                if (didHit)
                {
                    endpoint = hit.point;
                    var target = hit.collider.GetComponentInParent<NetworkPlayerAvatar>();
                    if (target != null) target.ApplyDamage(new DamageRequest(item.Damage, DamageKind.Item, avatar.gameObject));
                }
                PresentBoardShotRpc(origin, endpoint, (byte)id, didHit);
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
                _boardGrenades.Add(new Grenade
                {
                    PresentationId = NextGrenadePresentationId(),
                    OwnerSlot = avatar.AssignedSlot,
                    Position = origin,
                    Velocity = (target - origin) / flight -
                               Physics.gravity * (.5f * flight),
                    FlightDuration = flight
                });
#if UNITY_EDITOR || DEBUG
                DevelopmentBeginGrenadeObservation(
                    origin,
                    target,
                    item.Range,
                    flight);
#endif
            }
            else if (id == PrototypeItemId.Mine)
            {
                _boardMines.Add(new PlantedMine { OwnerSlot = avatar.AssignedSlot,
                    Position = minePosition, ArmRemaining = item.ArmingDelay });
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
                ClearBoardGrenadesOnServer();
                return;
            }

            var flowState = FlowState;
            if (!BoardItemLifecycleRules.CanSimulateProjectile(flowState))
            {
                ClearBoardGrenadesOnServer();
            }
            else if (!IsGlobalSimulationPaused)
            {
                var dt = Time.unscaledDeltaTime;
                var clearAtSettlementBoundary = false;
                if (flowState == BoardFlowState.AscendingResolve && _flow != null)
                {
                    var settlementRemaining = (float)Math.Max(
                        0d,
                        _flow.GetStateRemaining(ServerNow));
                    clearAtSettlementBoundary = dt >= settlementRemaining;
                    dt = Mathf.Min(dt, settlementRemaining);
                }

                TickBoardGrenades(dt);
                if (clearAtSettlementBoundary)
                {
                    // AscendingResolve is the post-action settlement window.
                    // No airborne board item may enter combat or a minigame.
                    ClearBoardGrenadesOnServer();
                }
            }

            if (BoardItemLifecycleRules.CanSimulateMine(flowState) &&
                !IsGlobalSimulationPaused)
            {
                var frameDelta = Time.unscaledDeltaTime;
                TickBoardMines(
                    BoardItemLifecycleRules.GetMineSimulationDelta(
                        _flow,
                        ServerNow,
                        frameDelta,
                        _arrivalGracePendingSlot >= 0
                            ? _arrivalGraceEndsAt.Value
                            : 0d));
            }
            SyncBoardGrenadeViews();
        }

        private void TickBoardGrenades(float deltaTime)
        {
            if (deltaTime <= 0f || _boardGrenades.Count == 0)
            {
                return;
            }

            var bomb = PrototypeItemCatalog.Get(PrototypeItemId.Grenade);
            for (int i = _boardGrenades.Count - 1; i >= 0; i--)
            {
                var grenade = _boardGrenades[i];
                // Small swept steps keep the parabola stable under a long host frame.
                float remaining = deltaTime;
                bool exploded = false;
                while (remaining > 0f && !exploded)
                {
                    // ThrowFlightSeconds is the authored landing time. It is
                    // also the hard range boundary: a missing/late floor hit
                    // must not let the grenade continue past its 16m target
                    // until the longer generic projectile failsafe expires.
                    var simulationDuration = Mathf.Min(
                        bomb.ProjectileLifetime,
                        grenade.FlightDuration);
                    var lifetimeRemaining = Mathf.Max(
                        0f,
                        simulationDuration - grenade.Life);
                    if (lifetimeRemaining <= 0f)
                    {
                        ExplodeBoardItem(grenade.Position, grenade.OwnerSlot, bomb);
                        _boardGrenades.RemoveAt(i);
                        break;
                    }

                    float step = Mathf.Min(
                        Mathf.Min(remaining, .02f),
                        lifetimeRemaining);
                    remaining -= step;
                    var delta = grenade.Velocity * step +
                                Physics.gravity * (.5f * step * step);
                    var owner = GetAvatarForSlot(grenade.OwnerSlot);
                    bool hit = BoardItemPhysics.Cast(
                        grenade.Position,
                        delta.normalized,
                        delta.magnitude,
                        owner != null ? owner.gameObject : null,
                        out var collision,
                        bomb.ProjectileRadius);
                    grenade.Position = hit
                        ? collision.point + collision.normal * .08f
                        : grenade.Position + delta;
                    grenade.Velocity += Physics.gravity * step;
                    grenade.Life += step;
#if UNITY_EDITOR || DEBUG
                    DevelopmentRecordGrenadePosition(
                        grenade.Position,
                        grenade.Life);
#endif
                    if (hit || BoardItemLifecycleRules.HasReachedProjectileLifetime(
                            grenade.Life,
                            simulationDuration))
                    {
                        ExplodeBoardItem(grenade.Position, grenade.OwnerSlot, bomb);
                        _boardGrenades.RemoveAt(i);
                        exploded = true;
                    }
                }
            }
        }

        private void TickBoardMines(float deltaTime)
        {
            if (deltaTime <= 0f || _boardMines.Count == 0)
            {
                return;
            }

            var mineDefinition = PrototypeItemCatalog.Get(PrototypeItemId.Mine);
            bool minesChanged = false;
            for (int i = _boardMines.Count - 1; i >= 0; i--)
            {
                var mine = _boardMines[i];
                mine.ArmRemaining = Mathf.Max(0f, mine.ArmRemaining - deltaTime);
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

        private void SyncBoardGrenadeViews()
        {
            _nextItemSync -= Time.unscaledDeltaTime;
            if (_nextItemSync <= 0f)
            {
                _nextItemSync = .05f;
                var ids = new uint[_boardGrenades.Count];
                var positions = new Vector3[_boardGrenades.Count];
                var velocities = new Vector3[_boardGrenades.Count];
                for (int i = 0; i < positions.Length; i++)
                {
                    var grenade = _boardGrenades[i];
                    ids[i] = grenade.PresentationId;
                    positions[i] = grenade.Position;
                    velocities[i] = IsGlobalSimulationPaused
                        ? Vector3.zero
                        : grenade.Velocity;
                }
                if (positions.Length > 0 || _lastGrenadeViewCount > 0)
                {
                    SyncBoardGrenadesRpc(ids, positions, velocities, ServerNow);
                }
                _lastGrenadeViewCount = positions.Length;
            }
        }

        private uint NextGrenadePresentationId()
        {
            unchecked
            {
                _nextGrenadePresentationId++;
                if (_nextGrenadePresentationId == 0u)
                {
                    _nextGrenadePresentationId = 1u;
                }
            }

            return _nextGrenadePresentationId;
        }

        private void ClearBoardGrenadesOnServer()
        {
            if (_boardGrenades.Count == 0 && _lastGrenadeViewCount == 0)
            {
                return;
            }

            _boardGrenades.Clear();
            _lastGrenadeViewCount = 0;
            SyncBoardGrenadesRpc(
                Array.Empty<uint>(),
                Array.Empty<Vector3>(),
                Array.Empty<Vector3>(),
                ServerNow);
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
        private void SyncBoardGrenadesRpc(
            uint[] ids,
            Vector3[] positions,
            Vector3[] velocities,
            double snapshotTime)
        {
            if (!_grenadePresentationTracks.ApplySnapshot(
                    ids,
                    positions,
                    velocities,
                    snapshotTime,
                    _addedGrenadeViewIds,
                    _removedGrenadeViewIds))
            {
                return;
            }

            var prefab = PrototypeItemCatalog.Get(PrototypeItemId.Grenade).WorldPrefab;
            foreach (var id in _addedGrenadeViewIds)
            {
                var view = Instantiate(prefab);
                _grenadeViews.Add(id, view);
                if (_grenadePresentationTracks.TrySample(
                        id,
                        ServerNow,
                        out var position))
                {
                    view.transform.position = position;
                }
            }
        }

        private void TickBoardGrenadePresentation()
        {
            var now = ServerNow;
            if (_grenadePresentationTracks.RemoveExpired(
                    now,
                    _removedGrenadeViewIds))
            {
                foreach (var id in _removedGrenadeViewIds)
                {
                    if (!_grenadeViews.TryGetValue(id, out var expiredView))
                    {
                        continue;
                    }

                    if (expiredView != null)
                    {
                        Destroy(expiredView);
                    }
                    _grenadeViews.Remove(id);
                }
            }

            if (_grenadeViews.Count == 0)
            {
                return;
            }

            foreach (var pair in _grenadeViews)
            {
                var view = pair.Value;
                if (view != null &&
                    _grenadePresentationTracks.TrySample(
                        pair.Key,
                        now,
                        out var position))
                {
                    view.transform.position = position;
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PresentBoardExplosionRpc(Vector3 position, byte itemId)
        {
            GameSound.PlayAt(SoundKeys.ItemExplosion, position);
            var item = PrototypeItemCatalog.Get((PrototypeItemId)itemId);
            if (item.ExplosionPrefab == null) return;
            OneShotVfxPool.Play(
                item.ExplosionPrefab,
                position,
                Quaternion.identity,
                ResolveBoardExplosionVisualScale(item.BlastRadius));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PresentBoardShotRpc(
            Vector3 origin,
            Vector3 end,
            byte itemId,
            bool didHit)
        {
            var item = PrototypeItemCatalog.Get((PrototypeItemId)itemId);
            if (didHit)
            {
                GameSound.PlayAt(SoundKeys.ItemBulletImpact, end);
                if (item.ImpactPrefab != null)
                {
                    var incoming = origin - end;
                    var rotation = incoming.sqrMagnitude > 0.0001f
                        ? Quaternion.LookRotation(incoming.normalized, Vector3.up)
                        : Quaternion.identity;
                    OneShotVfxPool.Play(
                        item.ImpactPrefab,
                        end,
                        rotation,
                        item.Id == PrototypeItemId.Sniper ? 0.48f : 0.34f);
                }
            }

            var prefab = Resources.Load<GameObject>("MazeParty/ItemViews/Shot");
            if (prefab == null) return;
            var view = Instantiate(prefab, (origin + end) * .5f, Quaternion.LookRotation(end - origin));
            view.transform.localScale = new Vector3(.025f, .025f, Vector3.Distance(origin, end));
#if UNITY_EDITOR || DEBUG
            DevelopmentRecordPresentedShot(
                origin,
                end,
                item.Id,
                didHit,
                view.GetComponentInChildren<Rigidbody>(true) != null ||
                view.GetComponentInChildren<NetworkObject>(true) != null);
#endif
            Destroy(view, .08f);
        }

        private static float ResolveBoardExplosionVisualScale(float blastRadius)
        {
            return Mathf.Max(0.6f, blastRadius * 0.45f);
        }

        private static void PrewarmBoardItemVfx()
        {
            var grenade = PrototypeItemCatalog.Get(PrototypeItemId.Grenade);
            var pistol = PrototypeItemCatalog.Get(PrototypeItemId.Pistol);
            OneShotVfxPool.Prewarm(grenade.ExplosionPrefab, 4);
            OneShotVfxPool.Prewarm(pistol.ImpactPrefab, 8);
        }

        private void ClearBoardItemWorld()
        {
            _boardMines.Clear(); _boardGrenades.Clear();
            foreach (var pair in _grenadeViews)
            {
                if (pair.Value != null) Destroy(pair.Value);
            }
            _grenadeViews.Clear();
            _grenadePresentationTracks.Clear();
            _addedGrenadeViewIds.Clear();
            _removedGrenadeViewIds.Clear();
            Array.Clear(_mineRecipients, 0, _mineRecipients.Length);
            _lastGrenadeViewCount = 0;
            _nextItemSync = 0f;
        }
    }
}
