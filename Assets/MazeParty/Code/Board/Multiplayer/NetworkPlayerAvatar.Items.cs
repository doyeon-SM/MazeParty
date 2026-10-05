using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        [SerializeField] private BoardGrenadeRangeIndicator grenadeRangeIndicator;
        private readonly NetworkVariable<byte> _equippedItem = new NetworkVariable<byte>();
        private readonly NetworkVariable<int> _itemCharges = new NetworkVariable<int>(0, NetworkVariableReadPermission.Owner);
        private readonly NetworkVariable<bool> _doubleDice = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Owner);
        private readonly NetworkVariable<int> _firstDieResult = new NetworkVariable<int>(0, NetworkVariableReadPermission.Owner);
        private readonly NetworkVariable<int> _secondDieResult = new NetworkVariable<int>(0, NetworkVariableReadPermission.Owner);
        private double _itemCooldownRemaining;
        private readonly GrenadeRangeUseState _grenadeRangeUseState =
            new GrenadeRangeUseState();
        private readonly List<GameObject> _mineViews = new List<GameObject>();
        private Vector3[] _localMines = Array.Empty<Vector3>();
        public BoardGrenadeRangeIndicator GrenadeRangeIndicator => grenadeRangeIndicator;
        public IReadOnlyList<Vector3> LocalMinePositions => IsOwner ? _localMines : Array.Empty<Vector3>();
        public int LocalItemCharges => IsOwner ? _itemCharges.Value : 0;
        public bool UsesDoubleDice => (IsOwner || IsServer) && _doubleDice.Value;
        public string LocalDiceSummary => HasRolled
            ? GameText.F("D12 {0} + {1} = {2}",
                _firstDieResult.Value > 0 ? _firstDieResult.Value.ToString() : "?",
                _secondDieResult.Value > 0 ? _secondDieResult.Value.ToString() : "?",
                _privateRoll.Value)
            : GameText.F("D12 {0} + {1} / RMB EACH DIE",
                _firstDieResult.Value > 0 ? _firstDieResult.Value.ToString() : "?",
                _secondDieResult.Value > 0 ? _secondDieResult.Value.ToString() : "?");

        private void InitializeSelectedItem()
        {
            var id = GetSelectedItemOnServer();
            _equippedItem.Value = (byte)id;
            _itemCharges.Value = PrototypeItemCatalog.Get(id).Charges;
            _doubleDice.Value = id == PrototypeItemId.DoubleDice;
            _rangeDieItem.Value = id == PrototypeItemId.LowDice || id == PrototypeItemId.HighDice ? (byte)id : (byte)0;
            _itemCooldownRemaining = 0;
        }

        public bool CanUseItemChargeOnServer => IsServer && _itemCharges.Value > 0 && _itemCooldownRemaining <= 0d;

        public bool SpendItemChargeOnServer(BoardItemDefinition item)
        {
            if (!CanUseItemChargeOnServer || GetSelectedItemOnServer() != item.Id) return false;
            RecordItemUseOnServer();
            _itemCharges.Value--;
            _itemCooldownRemaining = item.FireInterval;
            if (_itemCharges.Value == 0) ConsumeSelectedItemOnServer();
            return true;
        }

        public int RecordDieResultOnServer(int index, int face)
        {
            if (!IsServer || HasRolled || !BoardDiceProgress.TrySettle(UsesDoubleDice,
                _firstDieResult.Value, _secondDieResult.Value, index, face, out var first, out var second, out var total)) return 0;
            _firstDieResult.Value = first;
            _secondDieResult.Value = second;
            if (total > 0 && (UsesDoubleDice || HasRangeDie))
            {
                RecordItemUseOnServer();
                ConsumeSelectedItemOnServer();
            }
            return total;
        }

        public bool HasDieResultOnServer(int index) => IsServer &&
            (index == 0 ? _firstDieResult.Value : _secondDieResult.Value) > 0;

        public int CompletePartialDiceOnTimeout()
        {
            if (!IsServer || HasRolled) return 0;
            int missing = BoardDiceProgress.MissingDieOnTimeout(UsesDoubleDice, _firstDieResult.Value, _secondDieResult.Value);
            return missing < 0 ? 0 : RecordDieResultOnServer(missing, UnityEngine.Random.Range(1, 13));
        }

        private void TickBoardItems()
        {
            TickUtilityItems();
            var match = NetworkMatchState.Instance;
            if (IsServer && match != null && match.IsActionPhase && !match.IsGlobalSimulationPaused)
                _itemCooldownRemaining = Math.Max(0d, _itemCooldownRemaining - Time.unscaledDeltaTime);
            var equippedItem = (PrototypeItemId)_equippedItem.Value;
            if (_avatarVisual != null) _avatarVisual.SetEquippedItem(equippedItem);
            if (!IsOwner)
            {
                grenadeRangeIndicator?.Hide();
                return;
            }

            RefreshGrenadeRangeIndicator(match, equippedItem);
            bool visible = match != null && match.GameplayEnabled &&
                match.FlowState <= BoardFlowState.LandingEffectResolve;
            foreach (var view in _mineViews) if (view != null) view.SetActive(visible);
        }

        public bool HasLocalDamageableFirearmTarget()
        {
            var match = NetworkMatchState.Instance;
            var itemId = LocalEquippedItem;
            if (!IsOwner || !IsSpawned || match == null || !match.CanAcceptActionInput ||
                CurrentHealth <= 0 || LocalChoiceResolution != ItemChoiceResolution.ItemSelected ||
                LocalItemCharges <= 0 || IsSwapping || !BoardItemPresentationRules.IsFirearm(itemId))
            {
                return false;
            }

            var origin = eyePivot != null
                ? eyePivot.position
                : transform.position + Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
            var direction = eyePivot != null ? eyePivot.forward : transform.forward;
            if (!BoardItemPhysics.Cast(
                    origin,
                    direction,
                    PrototypeItemCatalog.Get(itemId).Range,
                    gameObject,
                    out var hit))
            {
                return false;
            }

            var target = hit.collider.GetComponentInParent<NetworkPlayerAvatar>();
            return target != null && target.IsSpawned &&
                   BoardItemPresentationRules.ShouldHighlightFirearmTarget(
                       true,
                       target.CurrentHealth,
                       target.IsCloaked,
                       match.IsOpeningProtectionActive,
                       target.PersonalItemProtectionRemaining);
        }

        private void RefreshGrenadeRangeIndicator(
            NetworkMatchState match,
            PrototypeItemId equippedItem)
        {
            var grenadeSelectionAvailable =
                equippedItem == PrototypeItemId.Grenade &&
                LocalItemCharges > 0 &&
                match != null &&
                match.IsActionPhase &&
                LocalChoiceResolution == ItemChoiceResolution.ItemSelected;
            _grenadeRangeUseState.ClearWhenUnavailable(
                grenadeSelectionAvailable);

            var showRange = BoardItemPresentationRules.ShouldShowGrenadeRange(
                IsOwner,
                match != null && match.CanAcceptActionInput,
                CurrentHealth,
                LocalChoiceResolution,
                equippedItem,
                LocalItemCharges,
                _grenadeRangeUseState.IsPending);
            grenadeRangeIndicator?.SetPresentation(
                PrototypeItemCatalog.Get(PrototypeItemId.Grenade).Range,
                showRange);
        }

        private bool TryBeginLocalItemUseRequest(
            PrototypeItemId itemId,
            out uint requestId)
        {
            requestId = 0u;
            if (!IsOwner)
            {
                return false;
            }

            if (itemId != PrototypeItemId.Grenade)
            {
                return true;
            }

            if (!_grenadeRangeUseState.TryBegin(out requestId))
            {
                return false;
            }

            grenadeRangeIndicator?.Hide();
            return true;
        }

        private void ResolveLocalItemUseRequest(uint requestId, bool accepted)
        {
            if (IsOwner)
            {
                _grenadeRangeUseState.Resolve(requestId, accepted);
            }
        }

        [Rpc(SendTo.Owner)]
        private void ConfirmItemUseRpc(uint requestId, bool accepted)
        {
            ResolveLocalItemUseRequest(requestId, accepted);
        }

        public void SendMinePositionsOnServer(Vector3[] positions)
        {
            if (IsServer && IsSpawned) ReceiveMinePositionsRpc(positions);
        }

        [Rpc(SendTo.Owner)]
        private void ReceiveMinePositionsRpc(Vector3[] positions)
        {
            _localMines = positions ?? Array.Empty<Vector3>();
            var prefab = PrototypeItemCatalog.Get(PrototypeItemId.Mine).WorldPrefab;
            while (_mineViews.Count < _localMines.Length)
                _mineViews.Add(Instantiate(prefab));
            for (int i = _mineViews.Count - 1; i >= _localMines.Length; i--)
            {
                Destroy(_mineViews[i]);
                _mineViews.RemoveAt(i);
            }
            for (int i = 0; i < _mineViews.Count; i++) _mineViews[i].transform.position = _localMines[i];
        }

        private void DisposeBoardItems()
        {
            _grenadeRangeUseState.Clear();
            grenadeRangeIndicator?.Hide();
            foreach (var view in _mineViews) if (view != null) Destroy(view);
            _mineViews.Clear();
            _localMines = Array.Empty<Vector3>();
        }
    }
}
