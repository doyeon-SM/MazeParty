using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class SequenceMemorySoloTestController : MonoBehaviour
    {
        public const int LocalPlayerSlot = 0;

        private static readonly Color32[] PlayerColors =
        {
            new Color32(45, 122, 242, 255),
            new Color32(235, 57, 48, 255),
            new Color32(46, 199, 82, 255),
            new Color32(177, 68, 232, 255)
        };

        private readonly PlayerAvatarVisual[] _visuals =
            new PlayerAvatarVisual[SequenceMemoryRules.PlayerCount];
        private readonly RedLightGreenLightPlayerPresentation[]
            _playerPresentations =
                new RedLightGreenLightPlayerPresentation[
                    SequenceMemoryRules.PlayerCount];
        private readonly double[] _nextAiInputAt =
            new double[SequenceMemoryRules.PlayerCount];
        private readonly double[] _aiInputIntervals =
            new double[SequenceMemoryRules.PlayerCount];
        private readonly int[] _aiFailureIndices =
            new int[SequenceMemoryRules.PlayerCount];

        private SequenceMemoryMatchState _match;
        private MinigameSoloHudView _developerHud;
        private NetworkSequenceMemoryState _productionState;
        private SequenceMemoryNetworkView _productionView;
        private SequenceMemoryHudBindings _productionHud;
        private GameObject _arenaPresentation;
        private GameObject _productionPlayerRoot;
        private Transform _runtimeRoot;
        private Camera _runtimeCamera;
        private NetworkSequenceMemoryPhase _phase;
        private double _phaseElapsed;
        private double _phaseDuration;
        private int _presentedSymbolCount;
        private int _seed;
        private bool _initialized;

        public bool IsInitialized => _initialized;
        public int Seed => _seed;
        public SequenceMemoryMatchState Match => _match;
        public NetworkSequenceMemoryPhase Phase => _phase;
        public Camera RuntimeCamera => _runtimeCamera;

        public void ConfigureHud(MinigameSoloHudView hud)
        {
            _developerHud = hud;
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "Sequence Memory solo is already initialized.");
            }
            if (_developerHud == null ||
                !_developerHud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Sequence Memory solo requires the developer HUD " +
                    "prefab instance.");
            }

            _developerHud.BindActions(
                RestartMatch,
                StartNextSeed,
                StopSoloTest);
            ResolveAndDisableProduction();
            CreateRuntimeCamera();
            ConfigurePlayers();
            _initialized = true;
            BeginMatch(seed);
            Debug.Log(
                "[Minigame Solo Test] Sequence Memory started: local " +
                "P1 A/S/D input with three deterministic practice players.");
        }

        private void Update()
        {
            if (!_initialized || _match == null)
            {
                return;
            }
            if (HandleKeyboardShortcuts())
            {
                return;
            }

            var localInput = ReadLocalInput();
            Tick(Math.Max(0d, Time.unscaledDeltaTime), localInput);
            RefreshPresentation();
        }

        private void OnDestroy()
        {
            if (_runtimeRoot != null)
            {
                Destroy(_runtimeRoot.gameObject);
            }
        }

        private void ResolveAndDisableProduction()
        {
            _productionState = FindAnyObjectByType<
                NetworkSequenceMemoryState>(FindObjectsInactive.Include);
            _productionView = FindAnyObjectByType<
                SequenceMemoryNetworkView>(FindObjectsInactive.Include);
            if (_productionState == null || _productionView == null)
            {
                throw new InvalidOperationException(
                    "Sequence Memory production state and view were not " +
                    "found in the scene.");
            }

            _productionState.enabled = false;
            _productionView.enabled = false;
            _arenaPresentation = FindDescendant(
                _productionState.transform,
                "Arena Presentation")?.gameObject;
            _productionPlayerRoot = FindDescendant(
                _productionState.transform,
                "Runtime Players")?.gameObject;
            _productionHud = _productionState.GetComponentInChildren<
                SequenceMemoryHudBindings>(true);
            if (_arenaPresentation == null ||
                _productionPlayerRoot == null ||
                _productionHud == null ||
                !_productionHud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Sequence Memory scene presentation contract is " +
                    "incomplete.");
            }

            _arenaPresentation.SetActive(true);
            _productionPlayerRoot.SetActive(true);
            _productionHud.RootCanvas.gameObject.SetActive(true);
        }

        private void ConfigurePlayers()
        {
            var presentations =
                _productionPlayerRoot.GetComponentsInChildren<
                    RedLightGreenLightPlayerPresentation>(true);
            Array.Sort(
                presentations,
                (left, right) => string.CompareOrdinal(
                    left.name,
                    right.name));
            if (presentations.Length != SequenceMemoryRules.PlayerCount)
            {
                throw new InvalidOperationException(
                    "Sequence Memory production view must create four " +
                    "player presentations before the solo harness begins.");
            }

            for (var slot = 0;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                var presentation = presentations[slot];
                var visual = presentation.GetComponent<PlayerAvatarVisual>();
                if (visual == null)
                {
                    throw new InvalidOperationException(
                        "Sequence Memory player " + (slot + 1) +
                        " is missing PlayerAvatarVisual.");
                }

                _playerPresentations[slot] = presentation;
                _visuals[slot] = visual;
                visual.SetBodyColor(PlayerColors[slot]);
                visual.SetDisplayName(
                    slot == LocalPlayerSlot
                        ? "SOLO DEV"
                        : "PRACTICE " + (slot + 1));
                visual.SetOwnerFirstPerson(false);
                visual.SetTopViewHighlight(slot == LocalPlayerSlot);
                presentation.ApplyState(0, false);
            }
        }

        private void CreateRuntimeCamera()
        {
            _runtimeRoot = new GameObject(
                "[Developer] Sequence Memory Runtime").transform;
            var cameraObject = new GameObject(
                "Sequence Memory Solo Camera");
            cameraObject.transform.SetParent(_runtimeRoot, false);
            _runtimeCamera = cameraObject.AddComponent<Camera>();
            _runtimeCamera.orthographic = true;
            _runtimeCamera.orthographicSize =
                SequenceMemoryNetworkView.SharedCameraOrthographicSize;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 100f;
            _runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.018f, 0.024f, 0.036f);
            cameraObject.transform.SetPositionAndRotation(
                SequenceMemoryNetworkView.SharedCameraPosition,
                SequenceMemoryNetworkView.SharedCameraRotation);
            cameraObject.AddComponent<AudioListener>();
        }

        private void BeginMatch(int seed)
        {
            _seed = seed;
            _match = new SequenceMemoryMatchState(
                unchecked((ulong)(uint)seed));
            _match.BeginMatch();
            _phase = NetworkSequenceMemoryPhase.Countdown;
            _phaseElapsed = 0d;
            _phaseDuration = SequenceMemoryRules.CountdownSeconds;
            _presentedSymbolCount = 0;
            Array.Clear(_nextAiInputAt, 0, _nextAiInputAt.Length);
            Array.Clear(_aiInputIntervals, 0, _aiInputIntervals.Length);
            for (var slot = 0;
                 slot < _aiFailureIndices.Length;
                 slot++)
            {
                _aiFailureIndices[slot] = -1;
            }
            RefreshPresentation();
        }

        private void Tick(
            double deltaSeconds,
            SequenceMemoryInput? localInput)
        {
            _phaseElapsed += deltaSeconds;
            switch (_phase)
            {
                case NetworkSequenceMemoryPhase.Countdown:
                    if (_phaseElapsed >= _phaseDuration)
                    {
                        BeginProblemPresentation();
                    }
                    break;
                case NetworkSequenceMemoryPhase.PresentingProblem:
                    UpdateProblemPresentation();
                    if (_phaseElapsed >= _phaseDuration)
                    {
                        BeginInputWindow();
                    }
                    break;
                case NetworkSequenceMemoryPhase.AcceptingInput:
                    UpdateInputWindow(localInput);
                    break;
                case NetworkSequenceMemoryPhase.RevealingAnswer:
                    if (_phaseElapsed >= _phaseDuration)
                    {
                        CompleteAnswerReveal();
                    }
                    break;
                case NetworkSequenceMemoryPhase.Complete:
                    _phaseElapsed = Math.Min(
                        _phaseElapsed,
                        _phaseDuration);
                    break;
            }
        }

        private void BeginProblemPresentation()
        {
            _phase = NetworkSequenceMemoryPhase.PresentingProblem;
            _phaseElapsed = 0d;
            _phaseDuration =
                SequenceMemoryRules.GetProblemPresentationSeconds(
                    _match.CurrentRoundNumber);
            _presentedSymbolCount = 0;
        }

        private void UpdateProblemPresentation()
        {
            _presentedSymbolCount = Math.Min(
                _match.CurrentProblem.Length,
                (int)Math.Floor(
                    _phaseElapsed /
                    SequenceMemoryRules.ProblemSymbolIntervalSeconds));
        }

        private void BeginInputWindow()
        {
            _match.OpenInputWindow();
            _phase = NetworkSequenceMemoryPhase.AcceptingInput;
            _phaseElapsed = 0d;
            _phaseDuration = SequenceMemoryRules.InputWindowSeconds;
            ConfigureAiForCurrentRound();
        }

        private void UpdateInputWindow(
            SequenceMemoryInput? localInput)
        {
            if (_phaseElapsed >= SequenceMemoryRules.InputWindowSeconds)
            {
                _match.TryEndInputForTimeout(
                    SequenceMemoryRules.InputWindowSeconds);
                BeginAnswerReveal();
                return;
            }

            if (localInput.HasValue &&
                _match.CanAcceptInputForSlot(
                    LocalPlayerSlot,
                    (byte)_match.CurrentRoundNumber,
                    _match.InputEpoch))
            {
                _match.SubmitInput(
                    LocalPlayerSlot,
                    localInput.Value,
                    (byte)_match.CurrentRoundNumber,
                    _match.InputEpoch,
                    _phaseElapsed);
            }

            if (_match.Phase == SequenceMemoryMatchPhase.AcceptingInput)
            {
                UpdateAiInputs();
            }
            if (_match.Phase == SequenceMemoryMatchPhase.RevealingAnswer)
            {
                BeginAnswerReveal();
            }
        }

        private void ConfigureAiForCurrentRound()
        {
            var problemLength = _match.CurrentProblem.Length;
            for (var slot = 1;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                var timing = GetDeterministicValue(
                    slot,
                    _match.CurrentRoundNumber,
                    17);
                _aiInputIntervals[slot] =
                    0.3d + slot * 0.035d + (timing % 7U) * 0.012d;
                _nextAiInputAt[slot] =
                    0.22d + slot * 0.08d +
                    ((timing >> 8) % 8U) * 0.012d;
                if (!ShouldAiFail(slot, _match.CurrentRoundNumber))
                {
                    _aiFailureIndices[slot] = -1;
                    continue;
                }

                var firstPossibleFailure = Math.Min(2, problemLength - 1);
                var span = Math.Max(
                    1,
                    problemLength - firstPossibleFailure);
                _aiFailureIndices[slot] = firstPossibleFailure +
                    (int)(GetDeterministicValue(
                        slot,
                        _match.CurrentRoundNumber,
                        43) % (uint)span);
            }
        }

        private void UpdateAiInputs()
        {
            for (var slot = 1;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                var safety = SequenceMemoryRules.GetProblemLength(
                    _match.CurrentRoundNumber) + 1;
                while (safety-- > 0 &&
                       _match.Phase ==
                           SequenceMemoryMatchPhase.AcceptingInput &&
                       _phaseElapsed >= _nextAiInputAt[slot] &&
                       _match.CanAcceptInputForSlot(
                           slot,
                           (byte)_match.CurrentRoundNumber,
                           _match.InputEpoch))
                {
                    var player = _match.GetPlayer(slot);
                    var inputIndex = player.CurrentInput.Count;
                    var expected = _match.CurrentProblem[inputIndex];
                    var input = inputIndex == _aiFailureIndices[slot]
                        ? GetWrongInput(expected, slot)
                        : expected;
                    _match.SubmitInput(
                        slot,
                        input,
                        (byte)_match.CurrentRoundNumber,
                        _match.InputEpoch,
                        _phaseElapsed);
                    _nextAiInputAt[slot] += _aiInputIntervals[slot];
                }
            }
        }

        private void BeginAnswerReveal()
        {
            _phase = NetworkSequenceMemoryPhase.RevealingAnswer;
            _phaseElapsed = 0d;
            _phaseDuration = SequenceMemoryRules.AnswerRevealSeconds;
            _presentedSymbolCount = _match.CurrentProblem.Length;
        }

        private void CompleteAnswerReveal()
        {
            _match.CompleteRevealAndAdvance();
            if (_match.IsComplete)
            {
                _phase = NetworkSequenceMemoryPhase.Complete;
                _phaseElapsed = 0d;
                _phaseDuration = SequenceMemoryRules.ResultSeconds;
                return;
            }

            BeginProblemPresentation();
        }

        private void RefreshPresentation()
        {
            RefreshPlayers();
            RefreshProductionHud();
            RefreshDeveloperHud();
        }

        private void RefreshPlayers()
        {
            for (var slot = 0;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                var player = _match.GetPlayer(slot);
                _playerPresentations[slot]?.ApplyState(
                    player.MistakeCount,
                    player.IsEliminated);
                _visuals[slot]?.SetTopViewHighlight(
                    slot == LocalPlayerSlot);
            }
        }

        private void RefreshProductionHud()
        {
            if (_productionHud == null ||
                !_productionHud.HasRequiredReferences)
            {
                return;
            }

            _productionHud.NpcSequenceText.text = GetNpcSequenceLabel();

            for (var slot = 0;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                var player = _match.GetPlayer(slot);
                _productionHud.PlayerNameTexts[slot].text =
                    slot == LocalPlayerSlot
                        ? "SOLO DEV"
                        : "PRACTICE " + (slot + 1);
                var input = BuildInputString(player.CurrentInput);
                _productionHud.PlayerInputTexts[slot].text =
                    string.IsNullOrEmpty(input) ? "—" : input;
                _productionHud.PlayerStatusTexts[slot].text =
                    GetPlayerStatusLabel(slot);
                _productionHud.PlayerRows[slot].color =
                    slot == LocalPlayerSlot
                        ? new Color(1f, 0.88f, 0.25f, 1f)
                        : _productionHud.GetDefaultPlayerRowColor(slot);
                _productionHud.PlayerStatusTexts[slot].color =
                    GetPlayerStatusColor(slot);
            }
        }

        private void RefreshDeveloperHud()
        {
            var localPlayer = _match.GetPlayer(LocalPlayerSlot);
            var localInput = BuildInputString(localPlayer.CurrentInput);
            var primary = _phase == NetworkSequenceMemoryPhase.Complete
                ? BuildFinalLabel()
                : "PROBLEM " + _match.CurrentRoundNumber + "/" +
                  SequenceMemoryRules.RoundCount + " · " +
                  _phase.ToString().ToUpperInvariant() + " · " +
                  Remaining.ToString("0.0") + "s";
            var secondary = "P1 INPUT " +
                (string.IsNullOrEmpty(localInput) ? "—" : localInput) +
                " · MISSES " + localPlayer.MistakeCount + "/" +
                SequenceMemoryRules.MistakesToEliminate;
            var feature = "NPC " + GetNpcSequenceLabel() +
                " · FINAL PLACEMENT REWARD ONLY";
            var status = GetPlayerStatusLabel(LocalPlayerSlot);
            var style = localPlayer.IsEliminated
                ? MinigameSoloFeedbackStyle.Error
                : localPlayer.MistakeCount > 0
                    ? MinigameSoloFeedbackStyle.Warning
                    : localPlayer.TurnStatus ==
                      SequenceMemoryPlayerTurnStatus.Correct
                        ? MinigameSoloFeedbackStyle.Success
                        : MinigameSoloFeedbackStyle.Neutral;
            _developerHud.SetContent(
                "SEQUENCE MEMORY · SOLO",
                primary,
                secondary,
                feature,
                "A / S / D INPUT · R RESTART · N NEXT SEED · ESC STOP",
                status,
                style);
        }

        private string GetNpcSequenceLabel()
        {
            if (_phase == NetworkSequenceMemoryPhase.AcceptingInput)
            {
                return "— HIDDEN —";
            }
            if (_phase == NetworkSequenceMemoryPhase.Countdown)
            {
                return "—";
            }

            var problem = _match.CurrentProblem?.ToString();
            if (string.IsNullOrEmpty(problem))
            {
                return "—";
            }
            if (_phase == NetworkSequenceMemoryPhase.PresentingProblem)
            {
                return _presentedSymbolCount == 0
                    ? "—"
                    : problem.Substring(
                        0,
                        Math.Min(_presentedSymbolCount, problem.Length));
            }
            return problem;
        }

        private string GetPlayerStatusLabel(int slot)
        {
            var rank = GetFinalRank(slot);
            if (rank > 0)
            {
                return MinigameDisplayFormatter.ToOrdinal(rank) +
                       " · GOLD +" +
                       MinigameRewardRules.GetFinalPlacementGold(rank);
            }

            var player = _match.GetPlayer(slot);
            if (player.IsEliminated)
            {
                return "OUT · 2 MISSES";
            }
            if (player.MistakeCount > 0 &&
                player.TurnStatus ==
                    SequenceMemoryPlayerTurnStatus.Correct)
            {
                return "CORRECT · TORSO LOST";
            }

            switch (player.TurnStatus)
            {
                case SequenceMemoryPlayerTurnStatus.Entering:
                    return "INPUTTING";
                case SequenceMemoryPlayerTurnStatus.Correct:
                    return "CORRECT · WAITING";
                case SequenceMemoryPlayerTurnStatus.Failed:
                    return player.MistakeCount > 0
                        ? "WRONG · TORSO LOST"
                        : "WRONG · LOCKED";
                case SequenceMemoryPlayerTurnStatus.LockedForMatchEnd:
                    return "SURVIVED";
                case SequenceMemoryPlayerTurnStatus.Eliminated:
                    return "OUT";
                default:
                    return player.MistakeCount > 0
                        ? "WATCHING · TORSO LOST"
                        : "WATCHING";
            }
        }

        private Color GetPlayerStatusColor(int slot)
        {
            var player = _match.GetPlayer(slot);
            if (player.IsEliminated)
            {
                return new Color(1f, 0.36f, 0.28f, 1f);
            }
            if (GetFinalRank(slot) > 0 ||
                player.TurnStatus ==
                    SequenceMemoryPlayerTurnStatus.Correct)
            {
                return new Color(0.35f, 1f, 0.55f, 1f);
            }
            if (player.MistakeCount > 0)
            {
                return new Color(1f, 0.72f, 0.15f, 1f);
            }
            return _productionHud.GetDefaultStatusColor(slot);
        }

        private string BuildFinalLabel()
        {
            var rank = GetFinalRank(LocalPlayerSlot);
            return rank <= 0
                ? "COMPLETE"
                : "COMPLETE · " +
                  MinigameDisplayFormatter.ToOrdinal(rank) +
                  " · GOLD +" +
                  MinigameRewardRules.GetFinalPlacementGold(rank);
        }

        private int GetFinalRank(int slot)
        {
            var standings = _match.Result?.Standings;
            if (standings == null)
            {
                return 0;
            }
            for (var index = 0; index < standings.Count; index++)
            {
                if (standings[index].PlayerSlot == slot)
                {
                    return standings[index].Rank;
                }
            }
            return 0;
        }

        private double Remaining => Math.Max(
            0d,
            _phaseDuration - _phaseElapsed);

        private void RestartMatch()
        {
            BeginMatch(_seed);
        }

        private void StartNextSeed()
        {
            BeginMatch(unchecked(_seed + 1));
        }

        private static SequenceMemoryInput? ReadLocalInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return null;
            }

            var aPressed = keyboard.aKey.wasPressedThisFrame;
            var sPressed = keyboard.sKey.wasPressedThisFrame;
            var dPressed = keyboard.dKey.wasPressedThisFrame;
            var pressedCount =
                (aPressed ? 1 : 0) +
                (sPressed ? 1 : 0) +
                (dPressed ? 1 : 0);
            if (pressedCount != 1)
            {
                return null;
            }

            return aPressed
                ? SequenceMemoryInput.A
                : sPressed
                    ? SequenceMemoryInput.S
                    : SequenceMemoryInput.D;
        }

        private bool HandleKeyboardShortcuts()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                StopSoloTest();
                return true;
            }
            if (keyboard.rKey.wasPressedThisFrame)
            {
                RestartMatch();
                return true;
            }
            if (keyboard.nKey.wasPressedThisFrame)
            {
                StartNextSeed();
                return true;
            }
            return false;
        }

        private uint GetDeterministicValue(
            int slot,
            int roundNumber,
            int salt)
        {
            unchecked
            {
                var value = (uint)_seed;
                value ^= (uint)slot * 0x9E3779B9U;
                value ^= (uint)roundNumber * 0x85EBCA6BU;
                value ^= (uint)salt * 0xC2B2AE35U;
                value ^= value >> 16;
                value *= 0x7FEB352DU;
                value ^= value >> 15;
                value *= 0x846CA68BU;
                value ^= value >> 16;
                return value;
            }
        }

        private static bool ShouldAiFail(int slot, int roundNumber)
        {
            return (slot == 1 &&
                    (roundNumber == 1 || roundNumber == 4)) ||
                   (slot == 2 && roundNumber == 2) ||
                   (slot == 3 && roundNumber == 3);
        }

        private static SequenceMemoryInput GetWrongInput(
            SequenceMemoryInput expected,
            int slot)
        {
            return (SequenceMemoryInput)(
                ((int)expected + 1 + slot % 2) % 3);
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

        private static Transform FindDescendant(
            Transform root,
            string objectName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == objectName)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(
                    root.GetChild(index),
                    objectName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void StopSoloTest()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
