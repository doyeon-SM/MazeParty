using System;
using MazeParty.Gameplay;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private readonly NetworkVariable<FixedString64Bytes> _boardMapId =
            new NetworkVariable<FixedString64Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _boardMapContentVersion =
            new NetworkVariable<int>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _boardMapSelectionCommitted =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private BoardMapRuntimeLoader _boardMapLoader;
        private string _boardMapLoadFailure = string.Empty;

        public string BoardMapId => _boardMapId.Value.ToString();
        public int BoardMapContentVersion => _boardMapContentVersion.Value;
        public string BoardMapLoadFailure => _boardMapLoadFailure;
        public bool IsBoardMapReady =>
            _boardMapSelectionCommitted.Value &&
            string.IsNullOrEmpty(_boardMapLoadFailure) &&
            (_boardMapLoader != null
                ? _boardMapLoader.IsReady
                : CurrentBoardMapSelection.IsLegacy && _boardTopology != null);
        public BoardTopology ActiveBoardTopology =>
            IsBoardMapReady ? _boardTopology : null;
        public BoardMapRoot ActiveBoardMapRoot =>
            IsBoardMapReady && _boardMapLoader != null
                ? _boardMapLoader.RuntimeMapRoot
                : null;
        public BoardMapSelection CurrentBoardMapSelection =>
            BoardMapSelection.TryCreate(
                BoardMapId,
                BoardMapContentVersion,
                out var selection)
                ? selection
                : default;

        private void InitializeBoardMapRuntime()
        {
            _boardMapId.OnValueChanged += OnBoardMapIdChanged;
            _boardMapContentVersion.OnValueChanged +=
                OnBoardMapContentVersionChanged;
            _boardMapSelectionCommitted.OnValueChanged +=
                OnBoardMapSelectionCommittedChanged;

            _boardMapLoader = GetComponent<BoardMapRuntimeLoader>();
            if (_boardMapLoader == null)
            {
                _boardMapLoader = gameObject.AddComponent<BoardMapRuntimeLoader>();
            }

            if (IsServer && !PrepareBoardMapSelectionOnServer(out var error))
            {
                _boardMapLoadFailure = error;
                Debug.LogError(error, this);
                OnlineSessionController.Instance?
                    .EndActiveMatchForNetworkFailure(error);
            }

            if (_boardMapSelectionCommitted.Value)
            {
                TryApplyReplicatedBoardMapSelection();
            }
        }

        private void ShutdownBoardMapRuntime()
        {
            _boardMapId.OnValueChanged -= OnBoardMapIdChanged;
            _boardMapContentVersion.OnValueChanged -=
                OnBoardMapContentVersionChanged;
            _boardMapSelectionCommitted.OnValueChanged -=
                OnBoardMapSelectionCommittedChanged;
            _boardMapLoader = null;
            _boardMapLoadFailure = string.Empty;
        }

        private bool PrepareBoardMapSelectionOnServer(out string error)
        {
            error = string.Empty;
            if (!IsServer)
            {
                error = "Only the server may select the board map.";
                return false;
            }

            BoardMapSelection selection;
            var controller = OnlineSessionController.Instance;
            if (controller != null && controller.ShouldResumeSavedMatch)
            {
                var session = new HostMinigameScheduleSession();
                var status = session.TryPeekRecoverySnapshot(out var snapshot);
                if (status != MatchRecoveryLoadStatus.Loaded || snapshot == null ||
                    !BoardMapSelection.TryCreate(
                        snapshot.boardMapId,
                        snapshot.boardMapContentVersion,
                        out var savedSelection))
                {
                    error = GameText.T(
                        "The saved match does not contain a valid board map selection.");
                    return false;
                }

                if (!TryResolveExactBoardMap(savedSelection, out selection, out error))
                {
                    return false;
                }
            }
            else if (controller != null)
            {
                if (!controller.TryGetSelectedBoardMap(
                        out var lobbySelection))
                {
                    error = GameText.T(
                        "The selected board map could not be loaded safely.");
                    return false;
                }

                var catalog = _boardMapLoader != null
                    ? _boardMapLoader.Catalog
                    : null;
                if (lobbySelection.IsLegacy &&
                    catalog != null && catalog.Maps.Count > 0)
                {
                    error = GameText.T(
                        "The selected board map could not be loaded safely.");
                    return false;
                }

                if (!TryResolveExactBoardMap(
                        lobbySelection,
                        out selection,
                        out error))
                {
                    return false;
                }
            }
            else if (!TryResolveFreshBoardMap(out selection, out error))
            {
                return false;
            }

            _boardMapId.Value = new FixedString64Bytes(selection.MapId);
            _boardMapContentVersion.Value = selection.ContentVersion;
            _boardMapSelectionCommitted.Value = true;
            return TryApplyReplicatedBoardMapSelection();
        }

        private bool TryResolveFreshBoardMap(
            out BoardMapSelection selection,
            out string error)
        {
            if (_boardMapLoader != null)
            {
                return _boardMapLoader.TryResolveFreshSelection(
                    out selection,
                    out error);
            }

            selection = BoardMapSelection.Legacy;
            error = string.Empty;
            return true;
        }

        private bool TryResolveExactBoardMap(
            BoardMapSelection requested,
            out BoardMapSelection selection,
            out string error)
        {
            if (_boardMapLoader != null)
            {
                return _boardMapLoader.TryResolveExactSelection(
                    requested,
                    out selection,
                    out error);
            }

            selection = requested;
            if (requested.IsLegacy)
            {
                error = string.Empty;
                return true;
            }

            error = $"Board map '{requested}' requires the Board runtime loader.";
            return false;
        }

        private bool TryApplyReplicatedBoardMapSelection()
        {
            if (!_boardMapSelectionCommitted.Value ||
                !BoardMapSelection.TryCreate(
                    BoardMapId,
                    BoardMapContentVersion,
                    out var selection))
            {
                _boardMapLoadFailure =
                    "The server replicated an invalid board map selection.";
                return false;
            }

            if (_boardMapLoader != null)
            {
                if (!_boardMapLoader.TryActivate(selection, out var error))
                {
                    _boardMapLoadFailure = error;
                    Debug.LogError(error, this);
                    return false;
                }

                _boardTopology = _boardMapLoader.RuntimeTopology;
            }
            else
            {
                if (!selection.IsLegacy)
                {
                    _boardMapLoadFailure =
                        $"Board map '{selection}' requires the Board runtime loader.";
                    return false;
                }

                _boardTopology = FindAnyObjectByType<BoardTopology>();
            }

            if (_boardTopology == null)
            {
                _boardMapLoadFailure =
                    "The selected board map has no active topology.";
                return false;
            }

            _boardMapLoadFailure = string.Empty;
            _boardEffectLayout = null;
            _cachedBoardEffectRevision = -1;
            if (IsServer)
            {
                ResolveWorldDiceCoordinator();
                if (_diceCoordinator != null)
                {
                    _diceCoordinator.ConfigureSceneDice(
                        FindObjectsByType<NetworkWorldDie>(
                            FindObjectsInactive.Include),
                        _boardTopology);
                }

                ForEachAvatar(avatar =>
                {
                    if (avatar != null && avatar.IsBoardReady)
                    {
                        avatar.InitializeBoardStateOnServer();
                    }
                });
            }

            return true;
        }

        private bool TryEnsureBoardMapReadyOnServer(out string error)
        {
            if (IsBoardMapReady)
            {
                error = string.Empty;
                return true;
            }

            if (IsServer && !_boardMapSelectionCommitted.Value &&
                PrepareBoardMapSelectionOnServer(out error))
            {
                return true;
            }

            error = string.IsNullOrWhiteSpace(_boardMapLoadFailure)
                ? GameText.T("The selected board map could not be loaded safely.")
                : _boardMapLoadFailure;
            return false;
        }

        private void OnBoardMapIdChanged(
            FixedString64Bytes _,
            FixedString64Bytes __)
        {
            if (_boardMapSelectionCommitted.Value)
            {
                TryApplyReplicatedBoardMapSelection();
            }
        }

        private void OnBoardMapContentVersionChanged(int _, int __)
        {
            if (_boardMapSelectionCommitted.Value)
            {
                TryApplyReplicatedBoardMapSelection();
            }
        }

        private void OnBoardMapSelectionCommittedChanged(bool _, bool current)
        {
            if (current)
            {
                TryApplyReplicatedBoardMapSelection();
            }
        }
    }
}
