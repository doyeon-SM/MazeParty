using System;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkSequenceMemoryPhase : byte
    {
        Inactive,
        Countdown,
        PresentingProblem,
        AcceptingInput,
        RevealingAnswer,
        Complete
    }

    /// <summary>
    /// Server-authoritative network shell for the single-match A/S/D memory
    /// game. The pure match model owns mistakes, elimination and ranking;
    /// this component owns authoritative clocks and replicated presentation.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkSequenceMemoryState : NetworkBehaviour
    {
        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable((byte)NetworkSequenceMemoryPhase.Inactive);
        private readonly NetworkVariable<byte> _roundNumber =
            CreateByteVariable(0);
        private readonly NetworkVariable<byte> _problemLength =
            CreateByteVariable(0);
        private readonly NetworkVariable<double> _phaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<uint> _inputEpoch =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _mistakeCounts =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _lifeStates =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _turnStatuses =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<FixedString32Bytes> _visibleProblem =
            CreateStringVariable();
        private readonly NetworkVariable<FixedString32Bytes> _playerInput0 =
            CreateStringVariable();
        private readonly NetworkVariable<FixedString32Bytes> _playerInput1 =
            CreateStringVariable();
        private readonly NetworkVariable<FixedString32Bytes> _playerInput2 =
            CreateStringVariable();
        private readonly NetworkVariable<FixedString32Bytes> _playerInput3 =
            CreateStringVariable();

        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[SequenceMemoryRules.PlayerCount];

        private SequenceMemoryMatchState _serverMatch;
        private double _phaseStartedAt;
        private double _phaseElapsedAtPause;
        private double _nextProblemSymbolAt;
        private double _pausedNextProblemSymbolRemaining;
        private int _presentedSymbolCount;
        private bool _completionReported;

        public static NetworkSequenceMemoryState Instance {
            get;
            private set;
        }

        public event Action<SequenceMemoryInput, bool> ToneRequested;

        public NetworkSequenceMemoryPhase Phase =>
            (NetworkSequenceMemoryPhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public int ProblemLength => _problemLength.Value;
        public uint InputEpoch => _inputEpoch.Value;
        public bool IsPaused => _paused.Value;
        public bool IsMatchActive => _matchActive.Value;
        public string VisibleProblem => _visibleProblem.Value.ToString();
        public double Remaining => GetRemaining(
            _phaseEndsAt.Value,
            _pausedPhaseRemaining.Value,
            Phase == NetworkSequenceMemoryPhase.Inactive);

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "More than one NetworkSequenceMemoryState is spawned.");
            }

            Instance = this;
            if (IsServer && !_matchActive.Value)
            {
                ResetReplicatedStateOnServer();
            }
        }

        public override void OnNetworkDespawn()
        {
            ClearLocalRuntime();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || !_matchActive.Value ||
                _paused.Value)
            {
                return;
            }

            FreezeBoardAvatarsOnServer();
            var now = ServerNow;
            switch (Phase)
            {
                case NetworkSequenceMemoryPhase.Countdown:
                    if (HasReachedDeadline(now))
                    {
                        BeginProblemPresentationOnServer(now);
                    }
                    break;
                case NetworkSequenceMemoryPhase.PresentingProblem:
                    UpdateProblemPresentationOnServer(now);
                    if (_presentedSymbolCount >= _problemLength.Value &&
                        HasReachedDeadline(now))
                    {
                        BeginInputWindowOnServer(now);
                    }
                    break;
                case NetworkSequenceMemoryPhase.AcceptingInput:
                    if (HasReachedDeadline(now))
                    {
                        _serverMatch?.TryEndInputForTimeout(
                            SequenceMemoryRules.InputWindowSeconds);
                        SyncPlayerStateOnServer();
                        BeginAnswerRevealOnServer(now);
                    }
                    break;
                case NetworkSequenceMemoryPhase.RevealingAnswer:
                    if (HasReachedDeadline(now))
                    {
                        CompleteRevealOnServer(now);
                    }
                    break;
                case NetworkSequenceMemoryPhase.Complete:
                    if (HasReachedDeadline(now))
                    {
                        CompleteMatchOnServer();
                    }
                    break;
            }
        }

        public void BeginMatchOnServer(ulong matchSeed)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            ClearLocalRuntime();
            ResetReplicatedStateOnServer();
            _serverMatch = new SequenceMemoryMatchState(matchSeed);
            _serverMatch.BeginMatch();
            _matchActive.Value = true;
            _paused.Value = false;
            _roundNumber.Value = 1;
            _problemLength.Value = (byte)_serverMatch.CurrentProblem.Length;
            CacheAndFreezeBoardAvatarsOnServer();

            var now = ServerNow;
            _phaseStartedAt = now;
            _phase.Value = (byte)NetworkSequenceMemoryPhase.Countdown;
            _phaseEndsAt.Value = now + SequenceMemoryRules.CountdownSeconds;
            SyncPlayerStateOnServer();
            Debug.Log(
                "[SequenceMemory] Match started. Seed " + matchSeed +
                "; ten A/S/D problems, no per-problem score.");
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            return SequenceMemoryRules.IsValidPlayerSlot(slot) &&
                   _matchActive.Value && !_paused.Value &&
                   Phase == NetworkSequenceMemoryPhase.AcceptingInput &&
                   GetPlayerTurnStatus(slot) ==
                       SequenceMemoryPlayerTurnStatus.Entering &&
                   Remaining > 0d;
        }

        public bool RequestInputOnServer(
            NetworkPlayerAvatar avatar,
            SequenceMemoryInput input,
            byte roundNumber,
            uint inputEpoch)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot) ||
                !SequenceMemoryRules.IsValidInput(input) ||
                !ValidateInputEnvelope(slot, roundNumber, inputEpoch))
            {
                return false;
            }

            _avatars[slot] = avatar;
            avatar.StopServerInputOnServer();
            var now = ServerNow;
            var elapsed = Math.Max(0d, now - _phaseStartedAt);
            var resolution = _serverMatch.SubmitInput(
                slot,
                input,
                roundNumber,
                _serverMatch.InputEpoch,
                elapsed);
            SyncPlayerStateOnServer();

            if (resolution.ShouldPlayInputTone)
            {
                PlayToneRpc((byte)input, false);
            }

            if (resolution.InputPhaseClosed ||
                _serverMatch.Phase ==
                    SequenceMemoryMatchPhase.RevealingAnswer)
            {
                BeginAnswerRevealOnServer(now);
            }

            return resolution.WasAccepted;
        }

        public string GetPlayerInput(int slot)
        {
            switch (slot)
            {
                case 0:
                    return _playerInput0.Value.ToString();
                case 1:
                    return _playerInput1.Value.ToString();
                case 2:
                    return _playerInput2.Value.ToString();
                case 3:
                    return _playerInput3.Value.ToString();
                default:
                    return string.Empty;
            }
        }

        public int GetMistakeCount(int slot)
        {
            return SequenceMemoryRules.IsValidPlayerSlot(slot)
                ? (int)((_mistakeCounts.Value >> (slot * 8)) & 0xffU)
                : 0;
        }

        public SequenceMemoryPlayerLifeState GetPlayerLifeState(int slot)
        {
            return SequenceMemoryRules.IsValidPlayerSlot(slot)
                ? (SequenceMemoryPlayerLifeState)(
                    (_lifeStates.Value >> (slot * 8)) & 0xffU)
                : SequenceMemoryPlayerLifeState.Intact;
        }

        public SequenceMemoryPlayerTurnStatus GetPlayerTurnStatus(int slot)
        {
            return SequenceMemoryRules.IsValidPlayerSlot(slot)
                ? (SequenceMemoryPlayerTurnStatus)(
                    (_turnStatuses.Value >> (slot * 8)) & 0xffU)
                : SequenceMemoryPlayerTurnStatus.ObservingProblem;
        }

        public bool ShouldHidePlayerTorso(int slot)
        {
            return GetPlayerLifeState(slot) !=
                SequenceMemoryPlayerLifeState.Intact;
        }

        public bool IsPlayerEliminated(int slot)
        {
            return GetPlayerLifeState(slot) ==
                SequenceMemoryPlayerLifeState.Eliminated;
        }

        public int GetFinalRank(int slot)
        {
            return SequenceMemoryRules.IsValidPlayerSlot(slot)
                ? (int)((_finalRanks.Value >> (slot * 8)) & 0xffU)
                : 0;
        }

        public void PauseOnServer(double now)
        {
            if (!IsSpawned || !IsServer || !_matchActive.Value ||
                _paused.Value)
            {
                return;
            }

            _phaseElapsedAtPause = Math.Max(0d, now - _phaseStartedAt);
            _pausedNextProblemSymbolRemaining =
                Phase == NetworkSequenceMemoryPhase.PresentingProblem
                    ? Math.Max(0d, _nextProblemSymbolAt - now)
                    : 0d;
            _pausedPhaseRemaining.Value =
                Math.Max(0d, _phaseEndsAt.Value - now);
            _phaseEndsAt.Value = 0d;
            _paused.Value = true;
            AdvanceInputEpochOnServer();
        }

        public void ResumeOnServer(double now)
        {
            if (!IsSpawned || !IsServer || !_matchActive.Value ||
                !_paused.Value)
            {
                return;
            }

            _phaseStartedAt = now - _phaseElapsedAtPause;
            if (Phase == NetworkSequenceMemoryPhase.PresentingProblem)
            {
                _nextProblemSymbolAt =
                    now + _pausedNextProblemSymbolRemaining;
            }
            _phaseEndsAt.Value =
                now + Math.Max(0d, _pausedPhaseRemaining.Value);
            _pausedPhaseRemaining.Value = 0d;
            _paused.Value = false;
            AdvanceInputEpochOnServer();
        }

        public void RestoreAvatarForReconnectOnServer(
            NetworkPlayerAvatar avatar)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot))
            {
                return;
            }

            _avatars[slot] = avatar;
            avatar.StopServerInputOnServer();
        }

        public void EndMatchOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            FreezeBoardAvatarsOnServer();
            ResetReplicatedStateOnServer();
            ClearLocalRuntime();
        }

        [Rpc(
            SendTo.ClientsAndHost,
            Delivery = RpcDelivery.Reliable)]
        private void PlayToneRpc(byte inputValue, bool fromNpc)
        {
            var input = (SequenceMemoryInput)inputValue;
            if (SequenceMemoryRules.IsValidInput(input))
            {
                ToneRequested?.Invoke(input, fromNpc);
            }
        }

        private void BeginProblemPresentationOnServer(double now)
        {
            if (_serverMatch == null ||
                _serverMatch.Phase !=
                    SequenceMemoryMatchPhase.PresentingProblem)
            {
                return;
            }

            _roundNumber.Value =
                (byte)_serverMatch.CurrentRoundNumber;
            _problemLength.Value =
                (byte)_serverMatch.CurrentProblem.Length;
            _presentedSymbolCount = 0;
            _nextProblemSymbolAt = now +
                SequenceMemoryRules.ProblemSymbolIntervalSeconds;
            _visibleProblem.Value = default;
            _phaseStartedAt = now;
            _phase.Value =
                (byte)NetworkSequenceMemoryPhase.PresentingProblem;
            _phaseEndsAt.Value = now +
                SequenceMemoryRules.GetProblemPresentationSeconds(
                    _serverMatch.CurrentRoundNumber);
            SyncPlayerStateOnServer();
            Debug.Log(
                "[SequenceMemory] Problem " + _roundNumber.Value +
                " presenting (" + _problemLength.Value + " inputs).");
        }

        private void UpdateProblemPresentationOnServer(double now)
        {
            if (_serverMatch?.CurrentProblem == null)
            {
                return;
            }

            if (_presentedSymbolCount >=
                    _serverMatch.CurrentProblem.Length ||
                now < _nextProblemSymbolAt)
            {
                return;
            }

            RevealNextProblemSymbolOnServer();
            var remainingSymbols =
                _serverMatch.CurrentProblem.Length -
                _presentedSymbolCount;
            if (remainingSymbols == 0)
            {
                _phaseEndsAt.Value = now +
                    SequenceMemoryRules.ProblemFinalHoldSeconds;
                return;
            }

            _nextProblemSymbolAt = now +
                SequenceMemoryRules.ProblemSymbolIntervalSeconds;
            _phaseEndsAt.Value = now +
                (remainingSymbols *
                    SequenceMemoryRules.ProblemSymbolIntervalSeconds) +
                SequenceMemoryRules.ProblemFinalHoldSeconds;
        }

        private void RevealNextProblemSymbolOnServer()
        {
            var problem = _serverMatch?.CurrentProblem;
            if (problem == null ||
                _presentedSymbolCount >= problem.Length)
            {
                return;
            }

            var input = problem[_presentedSymbolCount];
            _presentedSymbolCount++;
            _visibleProblem.Value = new FixedString32Bytes(
                problem.ToString().Substring(0, _presentedSymbolCount));
            PlayToneRpc((byte)input, true);
        }

        private void BeginInputWindowOnServer(double now)
        {
            if (_serverMatch == null ||
                _serverMatch.Phase !=
                    SequenceMemoryMatchPhase.PresentingProblem)
            {
                return;
            }

            _serverMatch.OpenInputWindow();
            _visibleProblem.Value = default;
            _phaseStartedAt = now;
            _phase.Value =
                (byte)NetworkSequenceMemoryPhase.AcceptingInput;
            _phaseEndsAt.Value = now +
                SequenceMemoryRules.InputWindowSeconds;
            AdvanceInputEpochOnServer();
            SyncPlayerStateOnServer();
        }

        private void BeginAnswerRevealOnServer(double now)
        {
            if (_serverMatch?.CurrentProblem == null ||
                Phase == NetworkSequenceMemoryPhase.RevealingAnswer ||
                Phase == NetworkSequenceMemoryPhase.Complete)
            {
                return;
            }

            _visibleProblem.Value = new FixedString32Bytes(
                _serverMatch.CurrentProblem.ToString());
            _phaseStartedAt = now;
            _phase.Value =
                (byte)NetworkSequenceMemoryPhase.RevealingAnswer;
            _phaseEndsAt.Value = now +
                SequenceMemoryRules.AnswerRevealSeconds;
            AdvanceInputEpochOnServer();
            SyncPlayerStateOnServer();
        }

        private void CompleteRevealOnServer(double now)
        {
            if (_serverMatch == null)
            {
                return;
            }

            if (_serverMatch.IsComplete)
            {
                BeginFinalResultOnServer(now);
                return;
            }

            if (_serverMatch.Phase !=
                SequenceMemoryMatchPhase.RevealingAnswer)
            {
                return;
            }

            _serverMatch.CompleteRevealAndAdvance();
            if (_serverMatch.IsComplete)
            {
                BeginFinalResultOnServer(now);
                return;
            }

            BeginProblemPresentationOnServer(now);
        }

        private void BeginFinalResultOnServer(double now)
        {
            if (_serverMatch?.Result == null)
            {
                return;
            }

            PackFinalRanksOnServer();
            _phaseStartedAt = now;
            _phase.Value = (byte)NetworkSequenceMemoryPhase.Complete;
            _phaseEndsAt.Value = now + SequenceMemoryRules.ResultSeconds;
            AdvanceInputEpochOnServer();
            SyncPlayerStateOnServer();
            Debug.Log(
                "[SequenceMemory] Final standings ready; displaying for " +
                SequenceMemoryRules.ResultSeconds.ToString("0") +
                " seconds.");
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported || _serverMatch?.Result == null)
            {
                return;
            }

            var standings = _serverMatch.Result.Standings;
            PackFinalRanksOnServer();

            var match = NetworkMatchState.Instance;
            if (match == null ||
                !match.TryCompleteSequenceMemoryOnServer(standings))
            {
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phase.Value = (byte)NetworkSequenceMemoryPhase.Complete;
            _phaseEndsAt.Value = 0d;
            FreezeBoardAvatarsOnServer();
            Debug.Log(
                "[SequenceMemory] Match complete after problem " +
                _roundNumber.Value + ".");
        }

        private void PackFinalRanksOnServer()
        {
            if (_serverMatch?.Result == null)
            {
                return;
            }

            uint packedRanks = 0U;
            var standings = _serverMatch.Result.Standings;
            for (var index = 0; index < standings.Count; index++)
            {
                var standing = standings[index];
                packedRanks |=
                    (uint)standing.Rank << (standing.PlayerSlot * 8);
            }
            _finalRanks.Value = packedRanks;
        }

        private bool ValidateInputEnvelope(
            int slot,
            byte roundNumber,
            uint inputEpoch)
        {
            return _serverMatch != null &&
                   _matchActive.Value && !_paused.Value &&
                   Phase == NetworkSequenceMemoryPhase.AcceptingInput &&
                   roundNumber == _roundNumber.Value &&
                   inputEpoch != 0U &&
                   inputEpoch == _inputEpoch.Value &&
                   Remaining > 0d &&
                   _serverMatch.CanAcceptInputForSlot(
                       slot,
                       roundNumber,
                       _serverMatch.InputEpoch);
        }

        private bool TryResolveAuthoritativeSlot(
            NetworkPlayerAvatar avatar,
            out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            return IsSpawned && IsServer && avatar != null &&
                   avatar.IsSpawned &&
                   SequenceMemoryRules.IsValidPlayerSlot(slot);
        }

        private void CacheAndFreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                _avatars[slot] =
                    match != null ? match.GetAvatarForSlot(slot) : null;
                _avatars[slot]?.StopServerInputOnServer();
            }
        }

        private void FreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                var avatar = _avatars[slot];
                if (avatar == null && match != null)
                {
                    avatar = match.GetAvatarForSlot(slot);
                    _avatars[slot] = avatar;
                }
                avatar?.StopServerInputOnServer();
            }
        }

        private void SyncPlayerStateOnServer()
        {
            if (_serverMatch == null)
            {
                _mistakeCounts.Value = 0U;
                _lifeStates.Value = 0U;
                _turnStatuses.Value = 0U;
                SetPlayerInputOnServer(0, string.Empty);
                SetPlayerInputOnServer(1, string.Empty);
                SetPlayerInputOnServer(2, string.Empty);
                SetPlayerInputOnServer(3, string.Empty);
                return;
            }

            uint mistakeCounts = 0U;
            uint lifeStates = 0U;
            uint turnStatuses = 0U;
            for (var slot = 0;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                var player = _serverMatch.GetPlayer(slot);
                mistakeCounts |=
                    (uint)Mathf.Clamp(player.MistakeCount, 0, 255) <<
                    (slot * 8);
                lifeStates |= (uint)player.LifeState << (slot * 8);
                turnStatuses |= (uint)player.TurnStatus << (slot * 8);
                SetPlayerInputOnServer(
                    slot,
                    BuildInputString(player.CurrentInput));
            }

            _mistakeCounts.Value = mistakeCounts;
            _lifeStates.Value = lifeStates;
            _turnStatuses.Value = turnStatuses;
        }

        private void SetPlayerInputOnServer(int slot, string value)
        {
            var fixedValue = new FixedString32Bytes(value ?? string.Empty);
            switch (slot)
            {
                case 0:
                    _playerInput0.Value = fixedValue;
                    break;
                case 1:
                    _playerInput1.Value = fixedValue;
                    break;
                case 2:
                    _playerInput2.Value = fixedValue;
                    break;
                case 3:
                    _playerInput3.Value = fixedValue;
                    break;
            }
        }

        private void ResetReplicatedStateOnServer()
        {
            _matchActive.Value = false;
            _paused.Value = false;
            _phase.Value =
                (byte)NetworkSequenceMemoryPhase.Inactive;
            _roundNumber.Value = 0;
            _problemLength.Value = 0;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _inputEpoch.Value = 0U;
            _mistakeCounts.Value = 0U;
            _lifeStates.Value = 0U;
            _turnStatuses.Value = 0U;
            _finalRanks.Value = 0U;
            _visibleProblem.Value = default;
            _playerInput0.Value = default;
            _playerInput1.Value = default;
            _playerInput2.Value = default;
            _playerInput3.Value = default;
        }

        private void AdvanceInputEpochOnServer()
        {
            unchecked
            {
                _inputEpoch.Value = _inputEpoch.Value == uint.MaxValue
                    ? 1U
                    : _inputEpoch.Value + 1U;
            }
        }

        private void ClearLocalRuntime()
        {
            _serverMatch = null;
            _phaseStartedAt = 0d;
            _phaseElapsedAtPause = 0d;
            _nextProblemSymbolAt = 0d;
            _pausedNextProblemSymbolRemaining = 0d;
            _presentedSymbolCount = 0;
            _completionReported = false;
            Array.Clear(_avatars, 0, _avatars.Length);
        }

        private bool HasReachedDeadline(double now)
        {
            return _phaseEndsAt.Value > 0d && now >= _phaseEndsAt.Value;
        }

        private double GetRemaining(
            double deadline,
            double pausedRemaining,
            bool inactive)
        {
            if (inactive)
            {
                return 0d;
            }

            return _paused.Value
                ? Math.Max(0d, pausedRemaining)
                : Math.Max(0d, deadline - ServerNow);
        }

        private static string BuildInputString(
            System.Collections.Generic.IReadOnlyList<SequenceMemoryInput>
                inputs)
        {
            if (inputs == null || inputs.Count == 0)
            {
                return string.Empty;
            }

            var characters = new char[inputs.Count];
            for (var index = 0; index < inputs.Count; index++)
            {
                switch (inputs[index])
                {
                    case SequenceMemoryInput.A:
                        characters[index] = 'A';
                        break;
                    case SequenceMemoryInput.S:
                        characters[index] = 'S';
                        break;
                    default:
                        characters[index] = 'D';
                        break;
                }
            }
            return new string(characters);
        }

        private static NetworkVariable<bool> CreateBoolVariable()
        {
            return new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<byte> CreateByteVariable(byte value)
        {
            return new NetworkVariable<byte>(
                value,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<double> CreateDoubleVariable()
        {
            return new NetworkVariable<double>(
                0d,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<uint> CreateUIntVariable()
        {
            return new NetworkVariable<uint>(
                0U,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<FixedString32Bytes>
            CreateStringVariable()
        {
            return new NetworkVariable<FixedString32Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }
    }
}
