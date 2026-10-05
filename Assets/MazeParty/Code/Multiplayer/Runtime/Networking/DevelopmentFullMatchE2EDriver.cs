#if UNITY_EDITOR || DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.Race;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using MazeParty.Gameplay.Minigames.WrongWay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Development-build-only four-process acceptance driver. It uses the real
    /// Relay/session, NGO ownership, board flow, minigame scenes and lobby-return
    /// paths. Board travel is accelerated. Final minigame settlement is
    /// accelerated by default, while an opt-in mode lets all runtimes complete
    /// and award their results naturally.
    /// </summary>
    internal sealed class DevelopmentFullMatchE2EDriver : MonoBehaviour
    {
        private const string EnableArgument = "-e2e-full-match";
        private const string CaptureMinigamesArgument = "-e2e-capture-minigames";
        private const string NaturalMinigamesArgument = "-e2e-natural-minigames";
        private const string CeremonyTieArgument = "-e2e-ceremony-tie";
        private const string RecoveryCheckpointArgument =
            "-e2e-recovery-checkpoint";
        private const string RecoveryStageArgument = "-e2e-recovery-stage";
        private const string RecoveryInitialStage = "initial";
        private const string RecoveryResumeStage = "resume";
        private const string HostRole = "host";
        private const string ClientRole = "client";
        private const string ForestMapId = "forest-graybox";
        private const int ForestContentVersion = 4;
        private const string MazeMapId = "maze-graybox";
        private const int MazeContentVersion = 1;
        private const int PollMilliseconds = 100;

        private enum RecoveryTarget
        {
            None,
            TurnOverview,
            MinigameIntroReady,
            MatchComplete
        }

        private readonly HashSet<ScheduledMinigameId> _completedGames =
            new HashSet<ScheduledMinigameId>();
        private readonly List<ScheduledMinigameId> _gameOrder =
            new List<ScheduledMinigameId>();
        private readonly List<string> _minigameScreenshotPaths =
            new List<string>();
        private readonly List<string> _damageSteps = new List<string>();
        private readonly object _fileGate = new object();

        private static bool s_installed;

#if UNITY_STANDALONE_WIN
        [System.Runtime.InteropServices.DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool TerminateProcess(
            IntPtr processHandle,
            uint exitCode);
#endif

        private string _role;
        private string _mapId;
        private string _recoveryStage;
        private string _runDirectory;
        private string _logPath;
        private int _mapContentVersion;
        private int _playerIndex;
        private int _assignedSlot = -1;
        private double _startedAt;
        private bool _hostItemProvisioned;
        private bool _hostCeremonyValidated;
        private bool _captureMinigames;
        private bool _naturalMinigames;
        private bool _ceremonyTie;
        private int _verifiedMinigameResultCount;
        private RecoveryTarget _recoveryTarget;
        private MatchRecoverySnapshot _resumeRecoverySnapshot;
        private bool _returnRequested;
        private bool _observedMatch;
        private bool _quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void PrepareRecoveryResume()
        {
            var arguments = Environment.GetCommandLineArgs();
            if (HasCommandLineSwitch(arguments, EnableArgument) &&
                !string.IsNullOrWhiteSpace(
                    GetArgument(arguments, RecoveryCheckpointArgument)) &&
                string.Equals(
                    GetArgument(arguments, RecoveryStageArgument),
                    RecoveryResumeStage,
                    StringComparison.OrdinalIgnoreCase))
            {
                OnlineSessionController.DevelopmentClearPlayingReconnectTicket();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_installed || Application.isEditor || !Debug.isDebugBuild ||
                !HasCommandLineSwitch(Environment.GetCommandLineArgs(), EnableArgument))
            {
                return;
            }

            s_installed = true;
            var driverObject = new GameObject("[Development Full Match E2E]");
            DontDestroyOnLoad(driverObject);
            driverObject.AddComponent<DevelopmentFullMatchE2EDriver>();
        }

        private async void Start()
        {
            _startedAt = Time.realtimeSinceStartupAsDouble;
            try
            {
                ParseArguments();
                Directory.CreateDirectory(_runDirectory);
                var logFileName = IsRecoveryMode
                    ? "player-" +
                      _playerIndex.ToString(CultureInfo.InvariantCulture) +
                      "-" + _recoveryStage + ".jsonl"
                    : "player-" +
                      _playerIndex.ToString(CultureInfo.InvariantCulture) +
                      ".jsonl";
                _logPath = Path.Combine(
                    _runDirectory,
                    logFileName);
                if (File.Exists(_logPath))
                {
                    File.Delete(_logPath);
                }

                PrepareMinigameCaptureDirectory();
                Log(
                    "driver_started",
                    "acceleratedResultSettlement=" + (!_naturalMinigames) +
                    ";captureMinigames=" +
                    _captureMinigames +
                    ";naturalMinigames=" + _naturalMinigames +
                    ";ceremonyTie=" + _ceremonyTie +
                    ";map=" + _mapId +
                    ";mapVersion=" + _mapContentVersion +
                    ";recoveryTarget=" + _recoveryTarget +
                    ";recoveryStage=" + (_recoveryStage ?? string.Empty));
                await RunAsync();
                Log("driver_passed");
                WriteAtomic(
                    Path.Combine(_runDirectory, "passed-" + _playerIndex + ".marker"),
                    "PASS");
                QuitProcess(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                TryLogFailure(exception);
                QuitProcess(2);
            }
        }

        private static void QuitProcess(int exitCode)
        {
            // Unity 6000.6 can remain inside native shutdown for minutes after
            // Application.Quit or Environment.Exit when Windows players close
            // together. The E2E result files are synchronously persisted before
            // this point, so terminate the development test process directly.
            Environment.ExitCode = exitCode;
#if UNITY_STANDALONE_WIN
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                if (!TerminateProcess(
                        process.Handle,
                        unchecked((uint)exitCode)))
                {
                    process.Kill();
                }
            }
#else
            Environment.Exit(exitCode);
#endif
        }

        private void OnApplicationQuit()
        {
            _quitting = true;
        }

        private async Task RunAsync()
        {
            var controller = await WaitForControllerAsync();
            var appearance = BuildAppearance(_playerIndex);
            var displayName = "E2E Player " + (_playerIndex + 1);

            if (IsHost)
            {
                await controller.DevelopmentCreateSessionAsync(displayName, appearance);
                await WaitUntilAsync(
                    () => controller.IsInSession &&
                          !string.IsNullOrWhiteSpace(controller.CurrentSession.Code),
                    120d,
                    "host session creation");
                if (IsRecoveryResume)
                {
                    await WaitUntilAsync(
                        () => controller.IsMatchRecoveryChoiceVisible,
                        60d,
                        "saved-match recovery choice");
                    var recoveryStatus =
                        controller.DevelopmentPeekRecoverySnapshot(
                            out _resumeRecoverySnapshot);
                    if (recoveryStatus != MatchRecoveryLoadStatus.Loaded ||
                        _resumeRecoverySnapshot == null ||
                        _resumeRecoverySnapshot.checkpoint !=
                            ToRecoveryCheckpoint(_recoveryTarget))
                    {
                        throw new InvalidOperationException(
                            "The expected recovery checkpoint was unavailable.");
                    }

                    await controller.DevelopmentSelectSavedMatchAsync();
                }
                else
                {
                    await controller.DevelopmentSelectBoardMapAsync(_mapId);
                }
                ValidateMapSelection(controller.CurrentSession.BoardMapSelection);
                WriteAtomic(JoinCodePath, controller.CurrentSession.Code.Trim());
                Log(
                    "session_created",
                    "map=" + _mapId + ";version=" + _mapContentVersion);
            }
            else
            {
                await Task.Delay(_playerIndex * 900);
                await WaitUntilAsync(
                    () => File.Exists(JoinCodePath) &&
                          !string.IsNullOrWhiteSpace(ReadAllTextShared(JoinCodePath)),
                    150d,
                    "join-code handoff");
                var code = ReadAllTextShared(JoinCodePath).Trim();
                await controller.DevelopmentJoinSessionAsync(code, displayName, appearance);
                await WaitUntilAsync(
                    () => controller.IsInSession,
                    120d,
                    "client session join");
                ValidateMapSelection(controller.CurrentSession.BoardMapSelection);
                Log(
                    "session_joined",
                    "map=" + _mapId + ";version=" + _mapContentVersion);
            }

            await WaitUntilAsync(
                () => controller.LocalSlot >= 0 && controller.GetLocalAvatar() != null,
                60d,
                "local network avatar assignment");
            _assignedSlot = controller.LocalSlot;

            await WaitUntilAsync(
                () =>
                {
                    var local = controller.GetLocalAvatar();
                    return local != null &&
                           local.Appearance.HatId == appearance.HatId &&
                           local.Appearance.ExpressionId == appearance.ExpressionId &&
                           SameBodyColor(local.Appearance, appearance);
                },
                30d,
                "local appearance replication");
            Log(
                "appearance_verified",
                "slot=" + _assignedSlot +
                ";hat=" + appearance.HatId +
                ";expression=" + appearance.ExpressionId);

            // Avoid four simultaneous MPS player-property saves. Real clients
            // naturally reach Ready at different times; deterministic players
            // otherwise create an artificial backend burst.
            await Task.Delay(_playerIndex * 1000);
            await controller.DevelopmentSetReadyAsync(true);
            await WaitUntilAsync(
                () => controller.CurrentSession.LocalReady,
                30d,
                "local ready state");

            if (IsHost)
            {
                await WaitUntilAsync(
                    () => controller.CurrentSession.CanStart &&
                          controller.DevelopmentHasFourAssignedPlayers() &&
                          HasFourDistinctAppearances(),
                    150d,
                    "four ready players with distinct appearances");
                await Task.Delay(1000);
                await WaitUntilAsync(
                    () => controller.CurrentSession.CanStart &&
                          controller.DevelopmentHasFourAssignedPlayers() &&
                          HasFourDistinctAppearances(),
                    30d,
                    "stable four-player ready lobby");
                ValidateLobbyRoster(controller.CurrentSession);
                TryDelete(JoinCodePath);
                Log("lobby_verified", "players=4;ready=4;distinctAppearances=true");
                await controller.DevelopmentStartGameAsync();
                Log("match_start_requested");
            }

            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    return match != null && match.IsSpawned &&
                           match.IsBoardMapReady && match.GameplayEnabled;
                },
                180d,
                _mapId + " Board scene synchronization");

            var networkMatch = NetworkMatchState.Instance;
            ValidateMapSelection(networkMatch.CurrentBoardMapSelection);
            if (!SceneManager.GetSceneByName(MultiplayerConstants.BoardScene).isLoaded)
            {
                throw new InvalidOperationException("Board scene did not report loaded.");
            }

            _observedMatch = true;
            Log(
                "board_ready",
                "map=" + networkMatch.BoardMapId +
                ";version=" + networkMatch.BoardMapContentVersion);

            if (IsRecoveryMode)
            {
                await RunRecoveryAsync(controller);
                return;
            }

            var participantTask = RunLocalParticipantAsync(controller);
            if (IsHost)
            {
                await RunHostMatchAsync();
            }

            await participantTask;

            if (IsHost)
            {
                await FinishHostRunAsync(controller);
            }
            else
            {
                await WaitUntilAsync(
                    () => File.Exists(QuitSignalPath),
                    180d,
                    "host completion signal");
                Log("host_completion_observed");
            }
        }

        private async Task<OnlineSessionController> WaitForControllerAsync()
        {
            await WaitUntilAsync(
                () => OnlineSessionController.Instance != null,
                60d,
                "online session controller");
            return OnlineSessionController.Instance;
        }

        private async Task RunLocalParticipantAsync(OnlineSessionController controller)
        {
            var lastChoiceTurn = -1;
            var lastChoiceAt = -100d;
            var lastReadyRevision = -1;
            var lastReadyAt = -100d;

            while (!_quitting)
            {
                var match = NetworkMatchState.Instance;
                var avatar = controller != null ? controller.GetLocalAvatar() : null;
                var now = Time.realtimeSinceStartupAsDouble;

                if (match != null && match.IsSpawned && avatar != null)
                {
                    _observedMatch = true;
                    if (match.FlowState == BoardFlowState.Action &&
                        !avatar.HasResolvedItemChoice &&
                        (lastChoiceTurn != match.Turn || now - lastChoiceAt >= 2d))
                    {
                        if (!IsHost || match.Turn != 1 || _hostItemProvisioned)
                        {
                            if (IsHost && match.Turn == 1)
                            {
                                avatar.ChooseItem(0);
                            }
                            else
                            {
                                avatar.ChooseNoItem();
                            }

                            lastChoiceTurn = match.Turn;
                            lastChoiceAt = now;
                            Log(
                                "item_choice_submitted",
                                "turn=" + match.Turn +
                                ";choice=" + (IsHost && match.Turn == 1 ? "pistol" : "none"));
                        }
                    }

                    if (match.FlowState == BoardFlowState.MinigameIntroReady &&
                        !match.IsMinigameReady(avatar.AssignedSlot) &&
                        (lastReadyRevision != match.MinigameRevealRevision ||
                         now - lastReadyAt >= 2d))
                    {
                        avatar.SetMinigameReady();
                        lastReadyRevision = match.MinigameRevealRevision;
                        lastReadyAt = now;
                        Log(
                            "minigame_ready_submitted",
                            "turn=" + match.Turn +
                            ";game=" + match.CurrentMinigame);
                    }

                    if (match.CanSubmitCeremonyReturn && !_returnRequested &&
                        (!IsHost || _hostCeremonyValidated))
                    {
                        _returnRequested = true;
                        controller.RequestCompletedMatchReturn();
                        Log("ceremony_return_submitted");
                    }
                }

                if (_observedMatch &&
                    controller != null &&
                    controller.IsInSession &&
                    controller.CurrentSession.Phase == MultiplayerConstants.LobbyPhase &&
                    NetworkMatchState.Instance == null &&
                    !SceneManager.GetSceneByName(MultiplayerConstants.BoardScene).isLoaded)
                {
                    await WaitUntilAsync(
                        () => !controller.CurrentSession.LocalReady,
                        30d,
                        "local ready reset after completed match");
                    Log("lobby_returned", "players=" + controller.CurrentSession.Players.Count);
                    WriteAtomic(
                        Path.Combine(
                            _runDirectory,
                            "returned-" + _playerIndex + ".marker"),
                        "RETURNED");
                    return;
                }

                await Task.Delay(PollMilliseconds);
            }

            throw new OperationCanceledException("Application quit before participant completed.");
        }

        private async Task RunRecoveryAsync(OnlineSessionController controller)
        {
            if (IsRecoveryResume)
            {
                WriteAtomic(
                    RecoveryResumeBoardMarkerPath(_playerIndex),
                    "READY");
                Log(
                    "recovery_resume_board_ready",
                    "target=" + _recoveryTarget);

                if (!IsHost)
                {
                    await WaitUntilAsync(
                        () => File.Exists(QuitSignalPath),
                        240d,
                        "host recovery verification");
                    return;
                }

                // TurnOverview advances after five seconds, and MatchComplete
                // starts granting ceremony keys after two seconds. Verify those
                // restored snapshots before a slow remote board load can mutate
                // them, then keep four-client synchronization separate.
                if (_recoveryTarget == RecoveryTarget.TurnOverview ||
                    _recoveryTarget == RecoveryTarget.MatchComplete)
                {
                    ValidateRestoredRecoveryCheckpoint(
                        controller,
                        RequireMatch());
                }

                await WaitUntilAsync(
                    AllRecoveryResumeBoardMarkersPresent,
                    180d,
                    "all four recovery board markers");
                if (_recoveryTarget == RecoveryTarget.MinigameIntroReady)
                {
                    ValidateRestoredRecoveryCheckpoint(
                        controller,
                        RequireMatch());
                }

                if (_recoveryTarget == RecoveryTarget.MatchComplete)
                {
                    await WaitUntilAsync(
                        () =>
                        {
                            var match = NetworkMatchState.Instance;
                            return match != null &&
                                   match.FlowState ==
                                       BoardFlowState.MatchComplete &&
                                   match.IsFinalRankingLocked;
                        },
                        120d,
                        "recovered award ceremony final ranking");
                    ValidateCeremony(RequireMatch());
                }

                await CaptureRecoveryScreenshotAsync(
                    Path.Combine(
                        _runDirectory,
                        "recovery-" + RecoveryTargetSlug +
                        "-resumed-host.png"));
                controller.DevelopmentDiscardSavedMatch();
                WriteAtomic(
                    Path.Combine(_runDirectory, "summary.json"),
                    "{\"recoveryCheckpoint\":\"" +
                    EscapeJson(_recoveryTarget.ToString()) +
                    "\",\"recoveryResumed\":true," +
                    "\"players\":4,\"map\":\"" +
                    EscapeJson(_mapId) +
                    "\",\"mapContentVersion\":" +
                    _mapContentVersion + "}");
                WriteAtomic(QuitSignalPath, "COMPLETE");
                Log(
                    "recovery_checkpoint_verified",
                    "target=" + _recoveryTarget);
                return;
            }

            switch (_recoveryTarget)
            {
                case RecoveryTarget.TurnOverview:
                    if (IsHost)
                    {
                        await WaitUntilAsync(
                            () =>
                            {
                                var match = NetworkMatchState.Instance;
                                return match != null &&
                                       match.FlowState ==
                                           BoardFlowState.TurnOverview;
                            },
                            60d,
                            "initial turn-overview checkpoint");
                        await HoldAtRecoveryCheckpointAsync(controller);
                    }
                    else
                    {
                        await WaitForExternalRecoveryKillAsync();
                    }
                    break;
                case RecoveryTarget.MinigameIntroReady:
                    await RunToMinigameIntroRecoveryCheckpointAsync(controller);
                    break;
                case RecoveryTarget.MatchComplete:
                    var participantTask =
                        RunRecoveryParticipantAutomationAsync(controller);
                    if (IsHost)
                    {
                        await RunHostMatchAsync();
                    }
                    await participantTask;
                    await WaitForExternalRecoveryKillAsync();
                    break;
                default:
                    throw new InvalidOperationException(
                        "A recovery target is required.");
            }
        }

        private async Task RunToMinigameIntroRecoveryCheckpointAsync(
            OnlineSessionController controller)
        {
            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    return match != null &&
                           match.FlowState == BoardFlowState.Action;
                },
                90d,
                "opening recovery action phase");

            var localAvatar = controller.GetLocalAvatar();
            if (localAvatar == null)
            {
                throw new InvalidOperationException(
                    "The local recovery avatar was unavailable.");
            }
            localAvatar.ChooseNoItem();

            if (!IsHost)
            {
                await WaitForExternalRecoveryKillAsync();
                return;
            }

            await WaitUntilAsync(
                () => AllChoicesResolved(RequireMatch()),
                45d,
                "recovery item choices");
            var match = RequireMatch();
            for (var slot = 0;
                 slot < MultiplayerConstants.MaxPlayers;
                 slot++)
            {
                if (!match.ApplyWorldDieResultOnServer(slot, slot + 1))
                {
                    throw new InvalidOperationException(
                        "Could not apply recovery die result for slot " +
                        slot + ".");
                }
            }
            if (!match.DevelopmentSettleAndReportAllPlayers(false))
            {
                throw new InvalidOperationException(
                    "Could not settle the recovery opening turn.");
            }

            await WaitForMinigameRevealAndDrainCombatsAsync(1);
            await HoldAtRecoveryCheckpointAsync(controller);
        }

        private async Task RunRecoveryParticipantAutomationAsync(
            OnlineSessionController controller)
        {
            var lastChoiceTurn = -1;
            var lastReadyRevision = -1;
            while (!_quitting &&
                   !File.Exists(RecoveryCheckpointReadyPath))
            {
                var match = NetworkMatchState.Instance;
                var avatar = controller.GetLocalAvatar();
                if (match != null && avatar != null)
                {
                    if (match.FlowState == BoardFlowState.Action &&
                        !avatar.HasResolvedItemChoice &&
                        lastChoiceTurn != match.Turn &&
                        (!IsHost || match.Turn != 1 ||
                         _hostItemProvisioned))
                    {
                        if (IsHost && match.Turn == 1)
                        {
                            avatar.ChooseItem(0);
                        }
                        else
                        {
                            avatar.ChooseNoItem();
                        }
                        lastChoiceTurn = match.Turn;
                    }

                    if (match.FlowState ==
                            BoardFlowState.MinigameIntroReady &&
                        !match.IsMinigameReady(avatar.AssignedSlot) &&
                        lastReadyRevision != match.MinigameRevealRevision)
                    {
                        avatar.SetMinigameReady();
                        lastReadyRevision = match.MinigameRevealRevision;
                    }
                }

                await Task.Delay(PollMilliseconds);
            }
        }

        private async Task HoldAtRecoveryCheckpointAsync(
            OnlineSessionController controller)
        {
            var expected = ToRecoveryCheckpoint(_recoveryTarget);
            MatchRecoverySnapshot snapshot = null;
            await WaitUntilAsync(
                () =>
                {
                    var status = controller.DevelopmentPeekRecoverySnapshot(
                        out snapshot);
                    return status == MatchRecoveryLoadStatus.Loaded &&
                           snapshot != null &&
                           snapshot.checkpoint == expected;
                },
                30d,
                expected + " recovery journal");

            if (!string.Equals(
                    snapshot.boardMapId,
                    _mapId,
                    StringComparison.Ordinal) ||
                snapshot.boardMapContentVersion != _mapContentVersion)
            {
                throw new InvalidOperationException(
                    "The recovery journal stored the wrong board map.");
            }

            WriteAtomic(
                Path.Combine(
                    _runDirectory,
                    "recovery-" + RecoveryTargetSlug +
                    "-initial-snapshot.json"),
                JsonUtility.ToJson(snapshot, true));
            await CaptureRecoveryScreenshotAsync(
                Path.Combine(
                    _runDirectory,
                    "recovery-" + RecoveryTargetSlug +
                    "-initial-host.png"));
            WriteAtomic(RecoveryCheckpointReadyPath, expected.ToString());
            Log(
                "recovery_checkpoint_ready",
                "target=" + expected +
                ";turn=" + snapshot.turn +
                ";remaining=" + snapshot.remainingMinigameSlots +
                ";map=" + snapshot.boardMapId);
            await WaitForExternalRecoveryKillAsync();
        }

        private async Task WaitForExternalRecoveryKillAsync()
        {
            await WaitUntilAsync(
                () => File.Exists(RecoveryExternalKillSignalPath),
                3600d,
                "external hard process termination");
        }

        private void ValidateRestoredRecoveryCheckpoint(
            OnlineSessionController controller,
            NetworkMatchState match)
        {
            var snapshot = _resumeRecoverySnapshot;
            if (snapshot == null ||
                snapshot.checkpoint != ToRecoveryCheckpoint(_recoveryTarget) ||
                match.Turn != snapshot.turn ||
                match.RemainingMinigameSlots !=
                    snapshot.remainingMinigameSlots ||
                (int)match.CurrentMinigame != snapshot.currentMinigame ||
                !string.Equals(
                    match.BoardMapId,
                    snapshot.boardMapId,
                    StringComparison.Ordinal) ||
                match.BoardMapContentVersion !=
                    snapshot.boardMapContentVersion ||
                match.FlowState != ToRecoveryFlowState(_recoveryTarget))
            {
                throw new InvalidOperationException(
                    "The resumed authoritative checkpoint did not match its journal.");
            }

            if (snapshot.players == null ||
                snapshot.players.Length != MultiplayerConstants.MaxPlayers)
            {
                throw new InvalidOperationException(
                    "The resumed recovery roster was incomplete.");
            }

            var savedByPlayerKey =
                new Dictionary<string, MatchRecoveryPlayerSnapshot>(
                    StringComparer.Ordinal);
            for (var slot = 0; slot < snapshot.players.Length; slot++)
            {
                var saved = snapshot.players[slot];
                if (saved == null || string.IsNullOrWhiteSpace(saved.playerKey) ||
                    !savedByPlayerKey.TryAdd(saved.playerKey, saved))
                {
                    throw new InvalidOperationException(
                        "The recovery journal contained an invalid player roster.");
                }
            }

            var avatars = RequireFourAvatars(match);
            for (var slot = 0; slot < avatars.Length; slot++)
            {
                if (!controller.TryGetMatchPlayerIdForSlot(
                        slot,
                        out var playerId) ||
                    !savedByPlayerKey.TryGetValue(
                        MatchRecoveryFingerprint.CreatePlayerKey(playerId),
                        out var saved))
                {
                    throw new InvalidOperationException(
                        "Recovered player identity mismatch for slot " + slot + ".");
                }

                var avatar = avatars[slot];
                if (avatar.CurrentHealth != saved.currentHealth ||
                    avatar.KeyCount != saved.keyCount ||
                    avatar.Gold != saved.gold ||
                    avatar.MinigameWins != saved.minigameWins ||
                    avatar.HasLogicalBoardTile != saved.hasLogicalTile ||
                    (saved.hasLogicalTile &&
                     avatar.LogicalBoardTileCoordinate != saved.logicalTile))
                {
                    throw new InvalidOperationException(
                        "Recovered player state mismatch for slot " + slot + ".");
                }
            }
        }

        private async Task CaptureRecoveryScreenshotAsync(string path)
        {
            if (File.Exists(path))
            {
                throw new InvalidOperationException(
                    "Refusing to overwrite recovery evidence: " + path);
            }
            ScreenCapture.CaptureScreenshot(path);
            await WaitUntilAsync(
                () => IsNonEmptyFile(path),
                15d,
                "recovery screenshot " + Path.GetFileName(path));
        }

        private async Task RunHostMatchAsync()
        {
            for (var expectedTurn = 1;
                 expectedTurn <= MinigameScheduleRules.DefaultTurnCount;
                 expectedTurn++)
            {
                await WaitUntilAsync(
                    () =>
                    {
                        var match = NetworkMatchState.Instance;
                        return match != null &&
                               match.GameplayEnabled &&
                               match.Turn == expectedTurn &&
                               match.FlowState == BoardFlowState.Action;
                    },
                    120d,
                    "turn " + expectedTurn + " action phase");

                var match = RequireMatch();
                var avatars = RequireFourAvatars(match);
                Log(
                    "turn_action_started",
                    "turn=" + expectedTurn +
                    ";choiceRemaining=" + match.ChoiceRemaining.ToString("0.00", CultureInfo.InvariantCulture));

                if (expectedTurn == 1)
                {
                    if (!avatars[0].TryAddItemOnServer(PrototypeItemId.Pistol))
                    {
                        throw new InvalidOperationException("Could not provision the turn-one pistol.");
                    }

                    _hostItemProvisioned = true;
                    Log("pistol_provisioned", "slot=0");
                }

                await WaitUntilAsync(
                    () => AllChoicesResolved(RequireMatch()),
                    45d,
                    "turn " + expectedTurn + " item choices");

                if (expectedTurn == 1)
                {
                    await RunItemDamageAndDeathAsync(RequireMatch());
                }

                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                {
                    var dieFace = (expectedTurn + slot - 1) %
                                  WorldDieAuthorityModel.MaximumFace + 1;
                    if (!match.ApplyWorldDieResultOnServer(slot, dieFace))
                    {
                        throw new InvalidOperationException(
                            "Could not apply the world-die result for slot " + slot +
                            " on turn " + expectedTurn + ".");
                    }
                }

                match = RequireMatch();
                if (!match.DevelopmentSettleAndReportAllPlayers(expectedTurn == 2))
                {
                    throw new InvalidOperationException(
                        "Could not settle all board moves for turn " + expectedTurn + ".");
                }

                Log(
                    "board_moves_settled",
                    "turn=" + expectedTurn +
                    ";combatSetup=" + (expectedTurn == 2));

                if (expectedTurn == 2)
                {
                    await RunCombatAsync();
                }

                await WaitForMinigameRevealAndDrainCombatsAsync(expectedTurn);

                match = RequireMatch();
                var game = match.CurrentMinigame;
                if (!_completedGames.Add(game))
                {
                    throw new InvalidOperationException(
                        "Minigame schedule repeated " + game + ".");
                }

                _gameOrder.Add(game);
                Log(
                    "minigame_revealed",
                    "turn=" + expectedTurn +
                    ";game=" + game +
                    ";seed=" + match.CurrentMinigameSeed);

                await WaitUntilAsync(
                    () =>
                    {
                        var current = NetworkMatchState.Instance;
                        if (current == null ||
                            current.Turn != expectedTurn ||
                            current.FlowState != BoardFlowState.MinigamePlaying ||
                            current.CurrentMinigame != game)
                        {
                            return false;
                        }

                        return MinigameRuntimeRegistry.TryGet(game, out var runtime) &&
                               runtime != null &&
                               runtime.IsSpawned;
                    },
                    150d,
                    "turn " + expectedTurn + " " + game + " runtime start");

                match = RequireMatch();
                Log(
                    "minigame_started",
                    "turn=" + expectedTurn +
                    ";game=" + game +
                    ";seed=" + match.CurrentMinigameSeed);

                var goldBefore = new int[MultiplayerConstants.MaxPlayers];
                var winsBefore = new int[MultiplayerConstants.MaxPlayers];
                avatars = RequireFourAvatars(match);
                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                {
                    goldBefore[slot] = avatars[slot].Gold;
                    winsBefore[slot] = avatars[slot].MinigameWins;
                }

                await ExerciseMinigameInputAsync(match, game);
                if (_captureMinigames)
                {
                    await CaptureMinigameScreenshotAsync(expectedTurn, game);
                }

                if (_naturalMinigames)
                {
                    await WaitForNaturalMinigameResultAsync(
                        expectedTurn,
                        game,
                        goldBefore,
                        winsBefore);
                }
                else
                {
                    var placements = BuildPlacements(expectedTurn);
                    if (!match.DevelopmentCompleteCurrentMinigame(placements))
                    {
                        throw new InvalidOperationException(
                            "Controlled result settlement was rejected for " +
                            game + ".");
                    }

                    await WaitUntilAsync(
                        () =>
                        {
                            var current = NetworkMatchState.Instance;
                            return current != null &&
                                   current.FlowState ==
                                       BoardFlowState.MinigameResult;
                        },
                        10d,
                        game + " result phase");

                    ValidateRewards(
                        RequireMatch(),
                        placements,
                        goldBefore,
                        winsBefore);

                    _verifiedMinigameResultCount++;
                    Log(
                        "minigame_result_verified",
                        "turn=" + expectedTurn +
                        ";game=" + game +
                        ";ranks=" + FormatRanks(placements));
                }

                if (expectedTurn == MinigameScheduleRules.DefaultTurnCount &&
                    _ceremonyTie)
                {
                    var tieAvatars = RequireFourAvatars(RequireMatch());
                    for (var slot = 0; slot < tieAvatars.Length; slot++)
                    {
                        if (!tieAvatars[slot]
                                .DevelopmentSetAllCeremonyStatsEqualOnServer(
                                    0,
                                    100,
                                    0))
                        {
                            throw new InvalidOperationException(
                                "Could not prepare the four-player ceremony tie.");
                        }
                    }

                    Log(
                        "ceremony_tie_prepared",
                        "keys=0;gold=100;minigameWins=0;awardStats=equal");
                }
            }

            ValidateCompleteSchedule();
            if (_captureMinigames)
            {
                ValidateMinigameCaptures();
            }

            Log(
                "schedule_complete",
                "order=" + JoinGameOrder() +
                ";resultMode=" +
                (_naturalMinigames ? "natural" : "accelerated") +
                ";verifiedResults=" + _verifiedMinigameResultCount);

            if (IsRecoveryInitial &&
                _recoveryTarget == RecoveryTarget.MatchComplete)
            {
                await WaitUntilAsync(
                    () =>
                    {
                        var match = NetworkMatchState.Instance;
                        return match != null &&
                               match.FlowState ==
                                   BoardFlowState.MatchComplete;
                    },
                    120d,
                    "match-complete recovery checkpoint");
                await HoldAtRecoveryCheckpointAsync(
                    OnlineSessionController.Instance);
                return;
            }

            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    return match != null &&
                           match.FlowState == BoardFlowState.MatchComplete &&
                           match.CanSubmitCeremonyReturn &&
                           match.IsFinalRankingLocked;
                },
                120d,
                "award ceremony final ranking");

            ValidateCeremony(RequireMatch());
            _hostCeremonyValidated = true;
            Log("ceremony_verified", DescribeCeremony(RequireMatch()));
        }

        private async Task WaitForMinigameRevealAndDrainCombatsAsync(
            int expectedTurn)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 120d;
            var lastExpiredSequence = -1;
            while (!_quitting && Time.realtimeSinceStartupAsDouble < deadline)
            {
                var match = NetworkMatchState.Instance;
                if (match == null || !match.IsSpawned)
                {
                    throw new InvalidOperationException(
                        "Match state disappeared while waiting for the minigame reveal.");
                }

                if (match.Turn == expectedTurn &&
                    match.FlowState == BoardFlowState.MinigameIntroReady &&
                    match.CurrentMinigame != ScheduledMinigameId.Skip)
                {
                    return;
                }

                if (match.Turn == expectedTurn &&
                    match.FlowState == BoardFlowState.CombatResolve &&
                    match.IsCombatActive &&
                    match.CombatSequenceIndex != lastExpiredSequence)
                {
                    if (!match.DevelopmentExpireCurrentCombat())
                    {
                        throw new InvalidOperationException(
                            "Could not accelerate additional combat " +
                            match.CombatSequenceIndex + " on turn " + expectedTurn + ".");
                    }

                    lastExpiredSequence = match.CombatSequenceIndex;
                    Log(
                        "additional_combat_accelerated",
                        "turn=" + expectedTurn +
                        ";sequence=" + lastExpiredSequence +
                        ";participants=" + match.CombatParticipantMask);
                }

                await Task.Delay(PollMilliseconds);
            }

            var current = NetworkMatchState.Instance;
            throw new TimeoutException(
                "Timed out waiting for turn " + expectedTurn +
                " minigame reveal; observed turn=" +
                (current != null ? current.Turn.ToString() : "none") +
                ", flow=" +
                (current != null ? current.FlowState.ToString() : "none") +
                ", combatActive=" +
                (current != null && current.IsCombatActive) + ".");
        }

        private async Task RunItemDamageAndDeathAsync(NetworkMatchState match)
        {
            var avatars = RequireFourAvatars(match);
            var attacker = avatars[0];
            var target = avatars[1];

            await WaitUntilAsync(
                () => attacker.LocalSelectedItemSlot == 0 &&
                      attacker.LocalItemCharges == 7,
                15d,
                "host pistol selection");
            await WaitUntilAsync(
                () => RequireMatch().ShieldRemaining <= 0.01d &&
                      target.PersonalItemProtectionRemaining <= 0.01d,
                15d,
                "opening item protection expiry");

            var encounterTile = target.CurrentBoardTileOnServer ??
                                attacker.CurrentBoardTileOnServer;
            if (encounterTile == null)
            {
                throw new InvalidOperationException(
                    "No board tile was available for the item encounter.");
            }

            var encounterSeparation =
                encounterTile.transform.right.normalized * 0.65f;

            var targetStartingGold = target.Gold;
            var tombstonesBefore = match.Tombstones.Count;
            var attackerStatsBefore = attacker.CreateMatchAwardStatsOnServer();
            var targetStatsBefore = target.CreateMatchAwardStatsOnServer();

            for (var shot = 1; shot <= 5; shot++)
            {
                if (!attacker.DevelopmentRelocateToTile(
                        encounterTile,
                        -encounterSeparation) ||
                    !target.DevelopmentRelocateToTile(
                        encounterTile,
                        encounterSeparation))
                {
                    throw new InvalidOperationException(
                        "Could not arrange pistol shot " + shot + ".");
                }

                Physics.SyncTransforms();
                await Task.Delay(75);
                if (!attacker.DevelopmentPrepareItemShot(
                        target.transform.position,
                        out var origin,
                        out var direction))
                {
                    throw new InvalidOperationException(
                        "Could not prepare pistol shot " + shot + ".");
                }

                await Task.Delay(175);
                if (!attacker.DevelopmentRequestUseSelectedItem())
                {
                    throw new InvalidOperationException(
                        "Could not submit pistol shot " + shot + ".");
                }

                var expectedHealth = PlayerStatRules.DefaultMaxHealth - shot * 20;
                await WaitUntilAsync(
                    () => target.CurrentHealth == expectedHealth,
                    4d,
                    "pistol shot " + shot + " health result");

                var expectedCharges = 7 - shot;
                if (attacker.LocalItemCharges != expectedCharges)
                {
                    throw new InvalidOperationException(
                        "Pistol charge mismatch after shot " + shot +
                        ": expected " + expectedCharges +
                        ", observed " + attacker.LocalItemCharges + ".");
                }

                _damageSteps.Add(expectedHealth.ToString(CultureInfo.InvariantCulture));
                Log(
                    "item_shot_verified",
                    "shot=" + shot +
                    ";hp=" + target.CurrentHealth +
                    ";charges=" + attacker.LocalItemCharges +
                    ";origin=" + origin.ToString("F2") +
                    ";direction=" + direction.ToString("F2"));
                await Task.Delay(350);
            }

            await WaitUntilAsync(
                () => target.IsBoardDeathInProgressOnServer &&
                      target.CurrentHealth == 0,
                3d,
                "board death start");

            var expectedDroppedGold = BoardDeathRules.DroppedGold(targetStartingGold);
            if (target.Gold != targetStartingGold - expectedDroppedGold ||
                match.Tombstones.Count != tombstonesBefore + 1)
            {
                throw new InvalidOperationException(
                    "Board death gold/tombstone state was incorrect.");
            }

            Log(
                "death_started",
                "goldBefore=" + targetStartingGold +
                ";goldAfter=" + target.Gold +
                ";tombstones=" + match.Tombstones.Count);

            await WaitUntilAsync(
                () => !target.IsBoardDeathInProgressOnServer &&
                      target.CurrentHealth == target.MaxHealth &&
                      target.CurrentBoardTileOnServer != null &&
                      target.CurrentBoardTileOnServer.TileType == BoardTileType.Respawn &&
                      target.PersonalItemProtectionRemaining > 0d,
                8d,
                "board respawn");

            var attackerStatsAfter = attacker.CreateMatchAwardStatsOnServer();
            var targetStatsAfter = target.CreateMatchAwardStatsOnServer();
            if (attackerStatsAfter.ItemUses - attackerStatsBefore.ItemUses != 5 ||
                attackerStatsAfter.PlayerDamageDealt -
                    attackerStatsBefore.PlayerDamageDealt != 100 ||
                targetStatsAfter.DamageTaken - targetStatsBefore.DamageTaken != 100)
            {
                throw new InvalidOperationException(
                    "Damage award counters did not record five item hits and 100 damage.");
            }

            Log(
                "respawn_verified",
                "hp=" + target.CurrentHealth +
                ";tile=" + target.CurrentBoardTileOnServer.Coordinate +
                ";protection=" +
                target.PersonalItemProtectionRemaining.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private async Task RunCombatAsync()
        {
            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    return match != null &&
                           match.FlowState == BoardFlowState.CombatResolve &&
                           match.IsCombatActive &&
                           CountBits(match.CombatParticipantMask) >= 2;
                },
                60d,
                "guaranteed board combat");

            var matchState = RequireMatch();
            var avatars = RequireFourAvatars(matchState);
            var attacker = avatars[0];
            var target = avatars[1];
            if (attacker.CombatHealth != BoardCombatRules.TemporaryHealth ||
                target.CombatHealth != BoardCombatRules.TemporaryHealth)
            {
                throw new InvalidOperationException(
                    "Combat participants did not begin at full temporary health.");
            }

            if (!attacker.DevelopmentPrepareCombatPunch(
                    target.transform.position,
                    out var origin,
                    out var direction))
            {
                throw new InvalidOperationException("Could not prepare the combat punch.");
            }

            await Task.Delay(175);
            if (!attacker.DevelopmentRequestCombatPunch())
            {
                throw new InvalidOperationException("Could not submit the combat punch.");
            }

            await WaitUntilAsync(
                () => target.CombatHealth ==
                      BoardCombatRules.TemporaryHealth - BoardCombatRules.PunchDamage,
                4d,
                "combat punch damage");

            Log(
                "combat_hit_verified",
                "hpBefore=" + BoardCombatRules.TemporaryHealth +
                ";hpAfter=" + target.CombatHealth +
                ";origin=" + origin.ToString("F2") +
                ";direction=" + direction.ToString("F2"));

            while (NetworkMatchState.Instance != null &&
                   NetworkMatchState.Instance.FlowState == BoardFlowState.CombatResolve)
            {
                var current = NetworkMatchState.Instance;
                if (current.IsCombatActive && !current.DevelopmentExpireCurrentCombat())
                {
                    throw new InvalidOperationException(
                        "Could not accelerate a verified combat result.");
                }

                await Task.Delay(PollMilliseconds);
            }

            Log("combat_resolved", "durationAcceleratedAfterVerifiedHit=true");
        }

        private async Task ExerciseMinigameInputAsync(
            NetworkMatchState match,
            ScheduledMinigameId game)
        {
            var inputDeadline = Time.realtimeSinceStartupAsDouble + 10d;
            while (match.FlowState == BoardFlowState.MinigamePlaying &&
                   !AnySlotAcceptsInput(match) &&
                   Time.realtimeSinceStartupAsDouble < inputDeadline)
            {
                await Task.Delay(PollMilliseconds);
            }

            var inputAccepted = AnySlotAcceptsInput(match);
            if (!inputAccepted)
            {
                throw new InvalidOperationException(
                    game + " never opened an authoritative input window.");
            }

            for (var pulse = 0; pulse < 4; pulse++)
            {
                if (match.FlowState != BoardFlowState.MinigamePlaying)
                {
                    throw new InvalidOperationException(
                        game + " ended before its input smoke completed.");
                }

                match.TryGetCurrentMinigameRoundAndInputEpoch(
                    out var round,
                    out var epoch);
                var avatars = RequireFourAvatars(match);
                for (var slot = 0; slot < avatars.Length; slot++)
                {
                    RouteMinigameInput(
                        match,
                        game,
                        avatars[slot],
                        slot,
                        pulse,
                        round,
                        epoch);
                }

                await Task.Delay(250);
            }

            var finalAvatars = RequireFourAvatars(match);
            for (var slot = 0; slot < finalAvatars.Length; slot++)
            {
                match.RouteMovementInputOnCurrentMinigameOnServer(
                    finalAvatars[slot],
                    Vector2.zero);
                match.RouteInflateHeldOnCurrentMinigameOnServer(
                    finalAvatars[slot],
                    false,
                    0,
                    0U);
                match.RouteBouncingShieldAxisOnCurrentMinigameOnServer(
                    finalAvatars[slot],
                    0f,
                    0,
                    0U);
            }

            Log(
                "minigame_input_smoke",
                "game=" + game + ";inputWindowObserved=" + inputAccepted);
        }

        private async Task CaptureMinigameScreenshotAsync(
            int turn,
            ScheduledMinigameId game)
        {
            if (!IsHost)
            {
                throw new InvalidOperationException(
                    "Only the host may capture minigame screenshots.");
            }

            // Give the Player one more rendered frame after the input smoke so
            // movement and game-specific presentation are visible in the image.
            await Task.Delay(500);

            var match = RequireMatch();
            if (match.FlowState != BoardFlowState.MinigamePlaying ||
                match.CurrentMinigame != game ||
                !MinigameRuntimeRegistry.TryGet(game, out var runtime) ||
                runtime == null ||
                !runtime.IsSpawned)
            {
                throw new InvalidOperationException(
                    game + " was not visibly in progress at screenshot time.");
            }

            var fileName =
                turn.ToString("00", CultureInfo.InvariantCulture) +
                "-" + game + ".png";
            var path = Path.Combine(MinigameScreenshotDirectory, fileName);
            if (File.Exists(path))
            {
                throw new InvalidOperationException(
                    "Refusing to overwrite an existing minigame screenshot: " +
                    path);
            }

            Log(
                "minigame_screenshot_requested",
                "turn=" + turn +
                ";game=" + game +
                ";file=minigame-screenshots/" + fileName);
            ScreenCapture.CaptureScreenshot(path);

            try
            {
                await WaitUntilAsync(
                    () => IsNonEmptyFile(path),
                    15d,
                    game + " screenshot file");
            }
            catch (Exception)
            {
                Log(
                    "minigame_screenshot_missing",
                    "turn=" + turn +
                    ";game=" + game +
                    ";file=minigame-screenshots/" + fileName);
                throw;
            }

            var fileLength = new FileInfo(path).Length;
            _minigameScreenshotPaths.Add(path);
            Log(
                "minigame_screenshot_saved",
                "turn=" + turn +
                ";game=" + game +
                ";file=minigame-screenshots/" + fileName +
                ";bytes=" + fileLength);
        }

        private async Task WaitForNaturalMinigameResultAsync(
            int turn,
            ScheduledMinigameId game,
            IReadOnlyList<int> goldBefore,
            IReadOnlyList<int> winsBefore)
        {
            var timeoutSeconds = GetNaturalMinigameTimeoutSeconds(game);
            Log(
                "minigame_natural_result_wait_started",
                "turn=" + turn +
                ";game=" + game +
                ";timeoutSeconds=" +
                timeoutSeconds.ToString("0.0", CultureInfo.InvariantCulture));

            await WaitUntilAsync(
                () =>
                {
                    var current = NetworkMatchState.Instance;
                    return current != null &&
                           current.Turn == turn &&
                           current.CurrentMinigame == game &&
                           current.FlowState == BoardFlowState.MinigameResult;
                },
                timeoutSeconds,
                game + " natural result phase");

            if (!MinigameRuntimeRegistry.TryGet(game, out var runtime) ||
                runtime == null)
            {
                throw new InvalidOperationException(
                    "The completed runtime adapter was unavailable for " +
                    game + ".");
            }

            var runtimeSpawnedAtResult = runtime.IsSpawned;
            if (runtimeSpawnedAtResult)
            {
                for (var slot = 0;
                     slot < MultiplayerConstants.MaxPlayers;
                     slot++)
                {
                    if (runtime.CanAcceptInputForSlot(slot))
                    {
                        throw new InvalidOperationException(
                            game + " still accepted input for slot " + slot +
                            " after naturally reporting its result.");
                    }
                }
            }

            var rewardDetails = ValidateNaturalRewardDeltas(
                RequireMatch(),
                goldBefore,
                winsBefore);
            _verifiedMinigameResultCount++;
            Log(
                "minigame_natural_result_verified",
                "turn=" + turn +
                ";game=" + game +
                ";runtimeSpawnedAtResult=" + runtimeSpawnedAtResult +
                ";runtimeAcceptsInput=false;" + rewardDetails);
        }

        private static double GetNaturalMinigameTimeoutSeconds(
            ScheduledMinigameId game)
        {
            if (!MinigameCatalog.TryGetDefinition(game, out var definition))
            {
                throw new InvalidOperationException(
                    "No minigame definition is registered for " + game + ".");
            }

            var roundCount = Math.Max(1, definition.RoundCount);
            var phaseSeconds = Math.Max(1d, definition.PhaseDurationSeconds);
            var timedGameplaySeconds = phaseSeconds * roundCount;
            if (game == ScheduledMinigameId.BombPassing)
            {
                // The catalog stores one bomb's maximum fuse. A natural match
                // can require one explosion for each eliminated player.
                timedGameplaySeconds = Math.Max(
                    timedGameplaySeconds,
                    phaseSeconds * (MultiplayerConstants.MaxPlayers - 1));
            }

            var transitionAllowanceSeconds = (roundCount * 15d) + 45d;
            return Math.Min(
                480d,
                Math.Max(
                    120d,
                    timedGameplaySeconds + transitionAllowanceSeconds));
        }

        private static string ValidateNaturalRewardDeltas(
            NetworkMatchState match,
            IReadOnlyList<int> goldBefore,
            IReadOnlyList<int> winsBefore)
        {
            var avatars = RequireFourAvatars(match);
            var firstPlaceReward =
                MinigameRewardRules.GetFinalPlacementGold(1);
            var firstPlaceWinnerCount = 0;
            byte firstPlaceMask = 0;
            var goldDeltas = new StringBuilder();
            var winDeltas = new StringBuilder();

            for (var slot = 0;
                 slot < MultiplayerConstants.MaxPlayers;
                 slot++)
            {
                var goldDelta = avatars[slot].Gold - goldBefore[slot];
                var winDelta = avatars[slot].MinigameWins - winsBefore[slot];
                if (goldDelta < 0 || winDelta < 0)
                {
                    throw new InvalidOperationException(
                        "Natural reward delta was negative for slot " + slot +
                        ": gold/wins " + goldDelta + "/" + winDelta + ".");
                }

                if (!IsPlacementRewardValue(goldDelta))
                {
                    throw new InvalidOperationException(
                        "Natural gold delta was not a placement reward for slot " +
                        slot + ": " + goldDelta + ".");
                }

                if (winDelta > 1)
                {
                    throw new InvalidOperationException(
                        "Natural minigame-win delta exceeded one for slot " +
                        slot + ": " + winDelta + ".");
                }

                var receivedFirstPlaceReward =
                    goldDelta == firstPlaceReward;
                if ((winDelta == 1) != receivedFirstPlaceReward)
                {
                    throw new InvalidOperationException(
                        "Natural first-place reward/win delta disagreed for " +
                        "slot " + slot + ": gold/wins " + goldDelta + "/" +
                        winDelta + ".");
                }

                if (receivedFirstPlaceReward)
                {
                    firstPlaceWinnerCount++;
                    firstPlaceMask = (byte)(firstPlaceMask | (1 << slot));
                }

                if (slot > 0)
                {
                    goldDeltas.Append(',');
                    winDeltas.Append(',');
                }

                goldDeltas.Append(goldDelta);
                winDeltas.Append(winDelta);
            }

            if (firstPlaceWinnerCount < 1)
            {
                throw new InvalidOperationException(
                    "Natural result awarded no first-place winner.");
            }

            return
                "goldDeltas=" + goldDeltas +
                ";winDeltas=" + winDeltas +
                ";firstPlaceWinners=" + firstPlaceWinnerCount +
                ";firstPlaceMask=" + firstPlaceMask;
        }

        private static bool IsPlacementRewardValue(int value)
        {
            for (var rank = 1;
                 rank <= MinigameRewardRules.PlacementCount;
                 rank++)
            {
                if (value == MinigameRewardRules.GetFinalPlacementGold(rank))
                {
                    return true;
                }
            }

            return false;
        }

        private void PrepareMinigameCaptureDirectory()
        {
            if (!_captureMinigames || !IsHost)
            {
                return;
            }

            Directory.CreateDirectory(MinigameScreenshotDirectory);
            var existingScreenshots = Directory.GetFiles(
                MinigameScreenshotDirectory,
                "*.png",
                SearchOption.TopDirectoryOnly);
            if (existingScreenshots.Length > 0)
            {
                throw new InvalidOperationException(
                    "The minigame screenshot directory must be empty before " +
                    "an opt-in capture run: " + MinigameScreenshotDirectory);
            }
        }

        private void ValidateMinigameCaptures()
        {
            var expectedCount = MinigameScheduleRules.DefaultTurnCount;
            var savedScreenshots = Directory.GetFiles(
                MinigameScreenshotDirectory,
                "*.png",
                SearchOption.TopDirectoryOnly);
            if (_minigameScreenshotPaths.Count != expectedCount ||
                savedScreenshots.Length != expectedCount)
            {
                throw new InvalidOperationException(
                    "Expected exactly " + expectedCount +
                    " minigame screenshots, captured " +
                    _minigameScreenshotPaths.Count +
                    " and found " + savedScreenshots.Length + " on disk.");
            }

            for (var index = 0;
                 index < _minigameScreenshotPaths.Count;
                 index++)
            {
                if (!IsNonEmptyFile(_minigameScreenshotPaths[index]))
                {
                    throw new InvalidOperationException(
                        "Minigame screenshot was missing or empty: " +
                        _minigameScreenshotPaths[index]);
                }
            }

            Log(
                "minigame_screenshots_verified",
                "count=" + savedScreenshots.Length +
                ";directory=minigame-screenshots");
        }

        private static bool IsNonEmptyFile(string path)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length > 0L;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void RouteMinigameInput(
            NetworkMatchState match,
            ScheduledMinigameId game,
            NetworkPlayerAvatar avatar,
            int slot,
            int pulse,
            byte round,
            uint epoch)
        {
            var horizontal = (slot & 1) == 0 ? 0.65f : -0.65f;
            var movement = new Vector2(horizontal, 0.55f);
            switch (game)
            {
                case ScheduledMinigameId.Minefield:
                    match.RouteMovementInputOnCurrentMinigameOnServer(
                        avatar,
                        movement,
                        round,
                        epoch);
                    if (pulse == 0)
                    {
                        match.RouteSonarInputOnCurrentMinigameOnServer(avatar, movement);
                    }
                    break;
                case ScheduledMinigameId.WrongWay:
                    if (pulse == 0)
                    {
                        match.RouteWrongWayDirectionOnCurrentMinigameOnServer(
                            avatar,
                            (WrongWayDirection)(slot % 4));
                    }
                    break;
                case ScheduledMinigameId.RedLightGreenLight:
                case ScheduledMinigameId.StableFooting:
                case ScheduledMinigameId.TerritoryPaint:
                case ScheduledMinigameId.TagChase:
                case ScheduledMinigameId.SnowySpin:
                case ScheduledMinigameId.CliffBarrage:
                    match.RouteMovementInputOnCurrentMinigameOnServer(
                        avatar,
                        movement,
                        round,
                        epoch);
                    break;
                case ScheduledMinigameId.BalloonBlow:
                    match.RouteInflateHeldOnCurrentMinigameOnServer(
                        avatar,
                        pulse < 3,
                        round,
                        epoch);
                    break;
                case ScheduledMinigameId.GiftGrab:
                case ScheduledMinigameId.BombPassing:
                case ScheduledMinigameId.ArenaCombat:
                    match.RouteMovementInputOnCurrentMinigameOnServer(
                        avatar,
                        movement,
                        round,
                        epoch);
                    if (pulse == 0)
                    {
                        match.RoutePrimaryActionOnCurrentMinigameOnServer(
                            avatar,
                            round,
                            epoch);
                        match.RoutePushInputOnCurrentMinigameOnServer(avatar);
                    }
                    break;
                case ScheduledMinigameId.Race:
                    match.RouteRaceStepOnCurrentMinigameOnServer(
                        avatar,
                        (pulse & 1) == 0
                            ? RaceStepInput.Left
                            : RaceStepInput.Right,
                        round,
                        epoch);
                    break;
                case ScheduledMinigameId.SequenceMemory:
                    if (slot == 0 && pulse == 0)
                    {
                        match.RouteSequenceMemoryInputOnCurrentMinigameOnServer(
                            avatar,
                            SequenceMemoryInput.A,
                            round,
                            epoch);
                    }
                    break;
                case ScheduledMinigameId.BouncingBalls:
                    match.RouteBouncingShieldAxisOnCurrentMinigameOnServer(
                        avatar,
                        horizontal,
                        round,
                        epoch);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(game), game, null);
            }
        }

        private async Task FinishHostRunAsync(OnlineSessionController controller)
        {
            await WaitUntilAsync(
                () => AllReturnMarkersPresent(),
                180d,
                "all four lobby-return markers");
            await WaitUntilAsync(
                () => controller.IsInSession &&
                      controller.CurrentSession.Phase == MultiplayerConstants.LobbyPhase &&
                      controller.CurrentSession.Players.Count == MultiplayerConstants.MaxPlayers &&
                      AllLobbyPlayersNotReady(controller.CurrentSession) &&
                      NetworkMatchState.Instance == null &&
                      !SceneManager.GetSceneByName(MultiplayerConstants.BoardScene).isLoaded &&
                      NoMinigameRuntimeSpawned(),
                60d,
                "final four-player lobby state");

            var summary = BuildSummary(controller.CurrentSession);
            WriteAtomic(Path.Combine(_runDirectory, "summary.json"), summary);
            WriteAtomic(QuitSignalPath, "COMPLETE");
            Log("all_players_lobby_verified", "players=4;allReady=false");
        }

        private void ValidateCompleteSchedule()
        {
            if (_completedGames.Count != MinigameRuntimeRegistry.RegisteredIds.Count ||
                _completedGames.Count != MinigameScheduleRules.DefaultTurnCount ||
                _verifiedMinigameResultCount !=
                    MinigameScheduleRules.DefaultTurnCount)
            {
                throw new InvalidOperationException(
                    "Expected all fifteen unique minigames and verified results, " +
                    "observed " + _completedGames.Count + "/" +
                    _verifiedMinigameResultCount + ".");
            }

            for (var index = 0;
                 index < MinigameRuntimeRegistry.RegisteredIds.Count;
                 index++)
            {
                if (!_completedGames.Contains(MinigameRuntimeRegistry.RegisteredIds[index]))
                {
                    throw new InvalidOperationException(
                        "Missing minigame " +
                        MinigameRuntimeRegistry.RegisteredIds[index] + ".");
                }
            }
        }

        private static void ValidateRewards(
            NetworkMatchState match,
            IReadOnlyList<PlayerPlacement> placements,
            IReadOnlyList<int> goldBefore,
            IReadOnlyList<int> winsBefore)
        {
            var avatars = RequireFourAvatars(match);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var rank = FindRank(placements, slot);
                var expectedGold =
                    goldBefore[slot] + MinigameRewardRules.GetFinalPlacementGold(rank);
                var expectedWins = winsBefore[slot] + (rank == 1 ? 1 : 0);
                if (avatars[slot].Gold != expectedGold ||
                    avatars[slot].MinigameWins != expectedWins)
                {
                    throw new InvalidOperationException(
                        "Reward mismatch for slot " + slot +
                        ": expected gold/wins " + expectedGold + "/" + expectedWins +
                        ", observed " + avatars[slot].Gold + "/" +
                        avatars[slot].MinigameWins + ".");
                }
            }
        }

        private void ValidateCeremony(NetworkMatchState match)
        {
            var firstCategory = match.GetCeremonyAwardCategory(0);
            var secondCategory = match.GetCeremonyAwardCategory(1);
            var firstMask = match.GetCeremonyAwardWinnerMask(0);
            var secondMask = match.GetCeremonyAwardWinnerMask(1);
            if (firstCategory == secondCategory ||
                firstMask == 0 || secondMask == 0 ||
                (firstMask & 0xF0) != 0 || (secondMask & 0xF0) != 0 ||
                match.GetCeremonyAwardWinningValue(0) < 0 ||
                match.GetCeremonyAwardWinningValue(1) < 0)
            {
                throw new InvalidOperationException("Ceremony award data was invalid.");
            }

            var avatars = RequireFourAvatars(match);
            var stats = new PlayerRankingStats[MultiplayerConstants.MaxPlayers];
            for (var slot = 0; slot < stats.Length; slot++)
            {
                stats[slot] = new PlayerRankingStats(
                    avatars[slot].KeyCount,
                    avatars[slot].Gold,
                    avatars[slot].MinigameWins);
            }

            var expectedRanks = PlayerRankingRules.Calculate(stats);
            for (var slot = 0; slot < expectedRanks.Length; slot++)
            {
                if (match.GetFinalCeremonyRank(slot) != expectedRanks[slot])
                {
                    throw new InvalidOperationException(
                        "Final ceremony rank mismatch for slot " + slot + ".");
                }

                if (_ceremonyTie && expectedRanks[slot] != 1)
                {
                    throw new InvalidOperationException(
                        "The deterministic ceremony tie did not rank every player first.");
                }
            }

            if (_ceremonyTie && (firstMask != 0x0F || secondMask != 0x0F))
            {
                throw new InvalidOperationException(
                    "The deterministic ceremony tie did not award all four players.");
            }
        }

        private static PlayerPlacement[] BuildPlacements(int turn)
        {
            var placements = new PlayerPlacement[MultiplayerConstants.MaxPlayers];
            var offset = (turn - 1) % MultiplayerConstants.MaxPlayers;
            for (var slot = 0; slot < placements.Length; slot++)
            {
                placements[slot] = new PlayerPlacement(
                    slot,
                    (slot + offset) % MultiplayerConstants.MaxPlayers + 1);
            }

            return placements;
        }

        private static int FindRank(
            IReadOnlyList<PlayerPlacement> placements,
            int slot)
        {
            for (var index = 0; index < placements.Count; index++)
            {
                if (placements[index].PlayerSlot == slot)
                {
                    return placements[index].Rank;
                }
            }

            throw new InvalidOperationException("No placement for slot " + slot + ".");
        }

        private static bool AllChoicesResolved(NetworkMatchState match)
        {
            var avatars = TryGetFourAvatars(match);
            if (avatars == null)
            {
                return false;
            }

            for (var slot = 0; slot < avatars.Length; slot++)
            {
                if (!avatars[slot].HasResolvedItemChoice)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AnySlotAcceptsInput(NetworkMatchState match)
        {
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if (match.CanCurrentMinigameAcceptInputForSlot(slot))
                {
                    return true;
                }
            }

            return false;
        }

        private static NetworkPlayerAvatar[] RequireFourAvatars(NetworkMatchState match)
        {
            var avatars = TryGetFourAvatars(match);
            if (avatars == null)
            {
                throw new InvalidOperationException(
                    "Four authoritative player avatars were not available.");
            }

            return avatars;
        }

        private static NetworkPlayerAvatar[] TryGetFourAvatars(NetworkMatchState match)
        {
            if (match == null)
            {
                return null;
            }

            var avatars = new NetworkPlayerAvatar[MultiplayerConstants.MaxPlayers];
            for (var slot = 0; slot < avatars.Length; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar == null || !avatar.IsSpawned)
                {
                    return null;
                }

                avatars[slot] = avatar;
            }

            return avatars;
        }

        private static NetworkMatchState RequireMatch()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsSpawned || !match.IsServer)
            {
                throw new InvalidOperationException(
                    "The authoritative match state is unavailable.");
            }

            return match;
        }

        private bool HasFourDistinctAppearances()
        {
            var found = new NetworkPlayerAvatar[MultiplayerConstants.MaxPlayers];
            var all = FindObjectsByType<NetworkPlayerAvatar>();
            for (var index = 0; index < all.Length; index++)
            {
                var avatar = all[index];
                if (avatar == null || !avatar.IsSpawned ||
                    avatar.AssignedSlot < 0 ||
                    avatar.AssignedSlot >= found.Length)
                {
                    continue;
                }

                found[avatar.AssignedSlot] = avatar;
            }

            var hats = new HashSet<byte>();
            var expressions = new HashSet<byte>();
            var colors = new HashSet<int>();
            for (var slot = 0; slot < found.Length; slot++)
            {
                if (found[slot] == null)
                {
                    return false;
                }

                var appearance = found[slot].Appearance;
                hats.Add(appearance.HatId);
                expressions.Add(appearance.ExpressionId);
                colors.Add(
                    appearance.BodyRed << 16 |
                    appearance.BodyGreen << 8 |
                    appearance.BodyBlue);
            }

            return hats.Count == found.Length &&
                   expressions.Count == found.Length &&
                   colors.Count == found.Length;
        }

        private static void ValidateLobbyRoster(SessionSnapshot snapshot)
        {
            if (snapshot == null ||
                snapshot.Players.Count != MultiplayerConstants.MaxPlayers ||
                !snapshot.CanStart)
            {
                throw new InvalidOperationException(
                    "Lobby did not contain four ready players.");
            }

            byte slotMask = 0;
            for (var index = 0; index < snapshot.Players.Count; index++)
            {
                var player = snapshot.Players[index];
                if (player.Slot < 0 ||
                    player.Slot >= MultiplayerConstants.MaxPlayers ||
                    !player.IsReady)
                {
                    throw new InvalidOperationException(
                        "Lobby roster contained an invalid slot or ready state.");
                }

                slotMask = (byte)(slotMask | (1 << player.Slot));
            }

            if (slotMask != 0x0F)
            {
                throw new InvalidOperationException(
                    "Lobby roster did not own slots zero through three.");
            }
        }

        private void ValidateMapSelection(BoardMapSelection selection)
        {
            if (!string.Equals(
                    selection.MapId,
                    _mapId,
                    StringComparison.Ordinal) ||
                selection.ContentVersion != _mapContentVersion)
            {
                throw new InvalidOperationException(
                    "Expected board map " + _mapId +
                    " v" + _mapContentVersion +
                    ", observed " + selection.MapId +
                    " v" + selection.ContentVersion + ".");
            }
        }

        private static bool AllLobbyPlayersNotReady(SessionSnapshot snapshot)
        {
            if (snapshot == null ||
                snapshot.Players.Count != MultiplayerConstants.MaxPlayers)
            {
                return false;
            }

            for (var index = 0; index < snapshot.Players.Count; index++)
            {
                if (snapshot.Players[index].IsReady)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool NoMinigameRuntimeSpawned()
        {
            for (var index = 0;
                 index < MinigameRuntimeRegistry.RegisteredIds.Count;
                 index++)
            {
                if (MinigameRuntimeRegistry.TryGet(
                        MinigameRuntimeRegistry.RegisteredIds[index],
                        out var runtime) &&
                    runtime != null &&
                    runtime.IsSpawned)
                {
                    return false;
                }
            }

            return true;
        }

        private bool AllReturnMarkersPresent()
        {
            for (var index = 0;
                 index < MultiplayerConstants.MaxPlayers;
                 index++)
            {
                if (!File.Exists(
                        Path.Combine(_runDirectory, "returned-" + index + ".marker")))
                {
                    return false;
                }
            }

            return true;
        }

        private async Task WaitUntilAsync(
            Func<bool> predicate,
            double timeoutSeconds,
            string description)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
            while (!_quitting && Time.realtimeSinceStartupAsDouble < deadline)
            {
                if (predicate())
                {
                    return;
                }

                await Task.Delay(PollMilliseconds);
            }

            throw new TimeoutException(
                "Timed out after " +
                timeoutSeconds.ToString("0.0", CultureInfo.InvariantCulture) +
                " seconds waiting for " + description + ".");
        }

        private void ParseArguments()
        {
            var arguments = Environment.GetCommandLineArgs();
            var captureMinigamesRequested = HasCommandLineSwitch(
                arguments,
                CaptureMinigamesArgument);
            var naturalMinigamesRequested = HasCommandLineSwitch(
                arguments,
                NaturalMinigamesArgument);
            var ceremonyTieRequested = HasCommandLineSwitch(
                arguments,
                CeremonyTieArgument);
            _role = GetArgument(arguments, "-e2e-role");
            if (!string.Equals(_role, HostRole, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_role, ClientRole, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "-e2e-role must be host or client.");
            }

            if (!int.TryParse(
                    GetArgument(arguments, "-e2e-player"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out _playerIndex) ||
                _playerIndex < 0 ||
                _playerIndex >= MultiplayerConstants.MaxPlayers)
            {
                throw new ArgumentException(
                    "-e2e-player must be between 0 and 3.");
            }

            _runDirectory = GetArgument(arguments, "-e2e-run-dir");
            if (string.IsNullOrWhiteSpace(_runDirectory))
            {
                throw new ArgumentException("-e2e-run-dir is required.");
            }

            _runDirectory = Path.GetFullPath(_runDirectory);
            if (IsHost && _playerIndex != 0)
            {
                throw new ArgumentException("The host must use -e2e-player 0.");
            }
            if (!IsHost && _playerIndex == 0)
            {
                throw new ArgumentException("A client must use -e2e-player 1, 2, or 3.");
            }

            _captureMinigames = IsHost && captureMinigamesRequested;
            _naturalMinigames = IsHost && naturalMinigamesRequested;
            _ceremonyTie = IsHost && ceremonyTieRequested;

            _mapId = GetArgument(arguments, "-e2e-map").Trim();
            if (string.IsNullOrEmpty(_mapId) ||
                string.Equals(
                    _mapId,
                    ForestMapId,
                    StringComparison.OrdinalIgnoreCase))
            {
                _mapId = ForestMapId;
                _mapContentVersion = ForestContentVersion;
            }
            else if (string.Equals(
                         _mapId,
                         MazeMapId,
                         StringComparison.OrdinalIgnoreCase))
            {
                _mapId = MazeMapId;
                _mapContentVersion = MazeContentVersion;
            }
            else
            {
                throw new ArgumentException(
                    "-e2e-map must be forest-graybox or maze-graybox.");
            }

            var recoveryCheckpoint = GetArgument(
                arguments,
                RecoveryCheckpointArgument).Trim();
            if (string.IsNullOrEmpty(recoveryCheckpoint))
            {
                _recoveryTarget = RecoveryTarget.None;
                _recoveryStage = string.Empty;
            }
            else
            {
                switch (recoveryCheckpoint.ToLowerInvariant())
                {
                    case "turn-overview":
                        _recoveryTarget = RecoveryTarget.TurnOverview;
                        break;
                    case "minigame-intro-ready":
                        _recoveryTarget = RecoveryTarget.MinigameIntroReady;
                        break;
                    case "match-complete":
                        _recoveryTarget = RecoveryTarget.MatchComplete;
                        break;
                    default:
                        throw new ArgumentException(
                            "-e2e-recovery-checkpoint must be " +
                            "turn-overview, minigame-intro-ready, or " +
                            "match-complete.");
                }

                _recoveryStage = GetArgument(
                    arguments,
                    RecoveryStageArgument).Trim().ToLowerInvariant();
                if (_recoveryStage != RecoveryInitialStage &&
                    _recoveryStage != RecoveryResumeStage)
                {
                    throw new ArgumentException(
                        "-e2e-recovery-stage must be initial or resume.");
                }

                if (_captureMinigames || _naturalMinigames || _ceremonyTie)
                {
                    throw new ArgumentException(
                        "Recovery staging cannot be combined with capture, " +
                        "natural-minigame, or ceremony-tie modes.");
                }
            }
        }

        private static PlayerAppearanceState BuildAppearance(int slot)
        {
            var safeSlot = Mathf.Clamp(
                slot,
                0,
                MultiplayerConstants.MaxPlayers - 1);
            return PlayerAppearanceState.FromColor(
                LobbyColorPalette.GetColor(safeSlot),
                0,
                0,
                (byte)(safeSlot + 1),
                0,
                (byte)(safeSlot + 1));
        }

        private static bool SameBodyColor(
            PlayerAppearanceState left,
            PlayerAppearanceState right)
        {
            return left.BodyRed == right.BodyRed &&
                   left.BodyGreen == right.BodyGreen &&
                   left.BodyBlue == right.BodyBlue;
        }

        private static MatchRecoveryCheckpoint ToRecoveryCheckpoint(
            RecoveryTarget target)
        {
            switch (target)
            {
                case RecoveryTarget.TurnOverview:
                    return MatchRecoveryCheckpoint.TurnOverview;
                case RecoveryTarget.MinigameIntroReady:
                    return MatchRecoveryCheckpoint.MinigameIntroReady;
                case RecoveryTarget.MatchComplete:
                    return MatchRecoveryCheckpoint.MatchComplete;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target));
            }
        }

        private static BoardFlowState ToRecoveryFlowState(
            RecoveryTarget target)
        {
            switch (target)
            {
                case RecoveryTarget.TurnOverview:
                    return BoardFlowState.TurnOverview;
                case RecoveryTarget.MinigameIntroReady:
                    return BoardFlowState.MinigameIntroReady;
                case RecoveryTarget.MatchComplete:
                    return BoardFlowState.MatchComplete;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target));
            }
        }

        private bool IsHost =>
            string.Equals(_role, HostRole, StringComparison.OrdinalIgnoreCase);

        private bool IsRecoveryMode =>
            _recoveryTarget != RecoveryTarget.None;

        private bool IsRecoveryInitial =>
            IsRecoveryMode && string.Equals(
                _recoveryStage,
                RecoveryInitialStage,
                StringComparison.Ordinal);

        private bool IsRecoveryResume =>
            IsRecoveryMode && string.Equals(
                _recoveryStage,
                RecoveryResumeStage,
                StringComparison.Ordinal);

        private string RecoveryTargetSlug
        {
            get
            {
                switch (_recoveryTarget)
                {
                    case RecoveryTarget.TurnOverview:
                        return "turn-overview";
                    case RecoveryTarget.MinigameIntroReady:
                        return "minigame-intro-ready";
                    case RecoveryTarget.MatchComplete:
                        return "match-complete";
                    default:
                        return "none";
                }
            }
        }

        private string JoinCodePath =>
            Path.Combine(_runDirectory, "join-code.txt");

        private string QuitSignalPath =>
            Path.Combine(_runDirectory, "quit.signal");

        private string MinigameScreenshotDirectory =>
            Path.Combine(_runDirectory, "minigame-screenshots");

        private string RecoveryCheckpointReadyPath =>
            Path.Combine(_runDirectory, "recovery-checkpoint-ready.marker");

        private string RecoveryExternalKillSignalPath =>
            Path.Combine(_runDirectory, "recovery-external-kill.signal");

        private string RecoveryResumeBoardMarkerPath(int playerIndex)
        {
            return Path.Combine(
                _runDirectory,
                "recovery-resume-board-" + playerIndex + ".marker");
        }

        private bool AllRecoveryResumeBoardMarkersPresent()
        {
            for (var slot = 0;
                 slot < MultiplayerConstants.MaxPlayers;
                 slot++)
            {
                if (!File.Exists(RecoveryResumeBoardMarkerPath(slot)))
                {
                    return false;
                }
            }
            return true;
        }

        private void Log(string eventName, string details = "")
        {
            var elapsed = Math.Max(
                0d,
                Time.realtimeSinceStartupAsDouble - _startedAt);
            var line =
                "{\"utc\":\"" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                "\",\"elapsed\":" + elapsed.ToString("0.000", CultureInfo.InvariantCulture) +
                ",\"player\":" + _playerIndex +
                ",\"slot\":" + _assignedSlot +
                ",\"role\":\"" + EscapeJson(_role ?? string.Empty) +
                "\",\"event\":\"" + EscapeJson(eventName) +
                "\",\"details\":\"" + EscapeJson(details ?? string.Empty) + "\"}" +
                Environment.NewLine;
            lock (_fileGate)
            {
                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }

            Debug.Log(
                "[FullMatchE2E P" + _playerIndex + "] " +
                eventName +
                (string.IsNullOrWhiteSpace(details) ? string.Empty : " | " + details));
        }

        private void TryLogFailure(Exception exception)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_runDirectory))
                {
                    Directory.CreateDirectory(_runDirectory);
                }

                if (string.IsNullOrWhiteSpace(_logPath) &&
                    !string.IsNullOrWhiteSpace(_runDirectory))
                {
                    _logPath = Path.Combine(
                        _runDirectory,
                        "player-" + _playerIndex + ".jsonl");
                }

                if (!string.IsNullOrWhiteSpace(_logPath))
                {
                    Log(
                        "fatal",
                        exception.GetType().Name + ": " + exception.Message);
                }

                if (!string.IsNullOrWhiteSpace(_runDirectory))
                {
                    WriteAtomic(
                        Path.Combine(
                            _runDirectory,
                            "failed-" + _playerIndex + ".marker"),
                        exception.ToString());
                }
            }
            catch (Exception loggingException)
            {
                Debug.LogException(loggingException);
            }
        }

        private string BuildSummary(SessionSnapshot snapshot)
        {
            var matchDescription =
                "\"map\":\"" + EscapeJson(_mapId) + "\"," +
                "\"mapContentVersion\":" + _mapContentVersion + "," +
                "\"players\":" + snapshot.Players.Count + "," +
                "\"minigameCount\":" + _completedGames.Count + "," +
                "\"minigameOrder\":\"" + EscapeJson(JoinGameOrder()) + "\"," +
                "\"damageHealthSequence\":\"" +
                    EscapeJson(string.Join(",", _damageSteps)) + "\"," +
                "\"itemDeathRespawn\":true," +
                "\"combatDamage\":true," +
                "\"ceremonyValidated\":true," +
                "\"ceremonyAllTie\":" +
                    (_ceremonyTie ? "true" : "false") + "," +
                "\"allPlayersReturnedToLobby\":true," +
                "\"acceleratedBoardTravel\":true," +
                "\"acceleratedCombatAfterVerifiedHit\":true," +
                "\"naturalMinigameCompletion\":" +
                    (_naturalMinigames ? "true" : "false") + "," +
                "\"acceleratedResultSettlement\":" +
                    (_naturalMinigames ? "false" : "true") + "," +
                "\"verifiedMinigameResultCount\":" +
                    _verifiedMinigameResultCount +
                (_captureMinigames
                    ? ",\"minigameScreenshotCount\":" +
                      _minigameScreenshotPaths.Count
                    : string.Empty);
            return "{" + matchDescription + "}";
        }

        private string JoinGameOrder()
        {
            var values = new string[_gameOrder.Count];
            for (var index = 0; index < _gameOrder.Count; index++)
            {
                values[index] = _gameOrder[index].ToString();
            }

            return string.Join(",", values);
        }

        private static string FormatRanks(IReadOnlyList<PlayerPlacement> placements)
        {
            var builder = new StringBuilder();
            for (var index = 0; index < placements.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append(placements[index].PlayerSlot);
                builder.Append(':');
                builder.Append(placements[index].Rank);
            }

            return builder.ToString();
        }

        private static string DescribeCeremony(NetworkMatchState match)
        {
            return
                "award0=" + match.GetCeremonyAwardCategory(0) +
                ";mask0=" + match.GetCeremonyAwardWinnerMask(0) +
                ";value0=" + match.GetCeremonyAwardWinningValue(0) +
                ";award1=" + match.GetCeremonyAwardCategory(1) +
                ";mask1=" + match.GetCeremonyAwardWinnerMask(1) +
                ";value1=" + match.GetCeremonyAwardWinningValue(1) +
                ";ranks=" +
                match.GetFinalCeremonyRank(0) + "," +
                match.GetFinalCeremonyRank(1) + "," +
                match.GetFinalCeremonyRank(2) + "," +
                match.GetFinalCeremonyRank(3);
        }

        private static int CountBits(byte value)
        {
            var count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }

            return count;
        }

        private static bool HasCommandLineSwitch(
            IReadOnlyList<string> arguments,
            string name)
        {
            for (var index = 0; index < arguments.Count; index++)
            {
                if (string.Equals(
                        arguments[index],
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetArgument(
            IReadOnlyList<string> arguments,
            string name)
        {
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index] ?? string.Empty;
                if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
                {
                    return index + 1 < arguments.Count
                        ? arguments[index + 1]
                        : string.Empty;
                }

                var prefix = name + "=";
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return argument.Substring(prefix.Length);
                }
            }

            return string.Empty;
        }

        private static string ReadAllTextShared(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException)
            {
                return string.Empty;
            }
        }

        private static void WriteAtomic(string path, string contents)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, contents ?? string.Empty, Encoding.UTF8);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // The joiners may still have the handoff file open briefly.
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}
#endif
