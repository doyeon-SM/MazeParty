#if UNITY_EDITOR || DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Development Player acceptance driver for a hard-disconnected client that
    /// reconnects while its own player pause is being held by the global
    /// reconnect pause. The external launcher owns the hard kill/relaunch step.
    /// </summary>
    internal sealed class DevelopmentReconnectE2EDriver : MonoBehaviour
    {
        private const string EnableArgument = "-e2e-reconnect";
        private const string InitialStage = "initial";
        private const string ResumeStage = "resume";
        private const string HostRole = "host";
        private const string ClientRole = "client";
        private const string ForestMapId = "forest-graybox";
        private const int ForestContentVersion = 4;
        private const int ReconnectingPlayer = 1;
        private const int PollMilliseconds = 100;

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

        private readonly object _fileGate = new object();
        private string _role;
        private string _stage;
        private string _runDirectory;
        private string _logPath;
        private int _playerIndex;
        private int _assignedSlot = -1;
        private int _reconnectingSlot = -1;
        private double _startedAt;
        private bool _quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_installed || Application.isEditor || !Debug.isDebugBuild ||
                !HasSwitch(Environment.GetCommandLineArgs(), EnableArgument))
            {
                return;
            }

            s_installed = true;
            var driverObject = new GameObject("[Development Reconnect E2E]");
            DontDestroyOnLoad(driverObject);
            driverObject.AddComponent<DevelopmentReconnectE2EDriver>();
        }

        private async void Start()
        {
            _startedAt = Time.realtimeSinceStartupAsDouble;
            try
            {
                ParseArguments();
                Directory.CreateDirectory(_runDirectory);
                _logPath = Path.Combine(
                    _runDirectory,
                    "player-" + _playerIndex + "-" + _stage + ".jsonl");
                if (File.Exists(_logPath))
                {
                    File.Delete(_logPath);
                }

                Log("driver_started");
                if (string.Equals(_stage, ResumeStage, StringComparison.Ordinal))
                {
                    await RunResumedClientAsync();
                }
                else
                {
                    await RunInitialProcessAsync();
                }

                Log("driver_passed");
                WriteAtomic(PassedMarkerPath, "PASS");
                QuitProcess(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                TryLogFailure(exception);
                QuitProcess(2);
            }
        }

        private void OnApplicationQuit()
        {
            _quitting = true;
        }

        private async Task RunInitialProcessAsync()
        {
            var controller = await WaitForControllerAsync();
            var appearance = BuildAppearance(_playerIndex);
            var displayName = "Reconnect Player " + (_playerIndex + 1);

            if (IsHost)
            {
                await controller.DevelopmentCreateSessionAsync(
                    displayName,
                    appearance);
                await WaitUntilAsync(
                    () => controller.IsInSession &&
                          !string.IsNullOrWhiteSpace(
                              controller.CurrentSession.Code),
                    120d,
                    "host session creation");
                await controller.DevelopmentSelectBoardMapAsync(ForestMapId);
                ValidateForest(controller.CurrentSession.BoardMapSelection);
                WriteAtomic(JoinCodePath, controller.CurrentSession.Code.Trim());
                Log("session_created");
            }
            else
            {
                await Task.Delay(_playerIndex * 900);
                await WaitUntilAsync(
                    () => File.Exists(JoinCodePath) &&
                          !string.IsNullOrWhiteSpace(
                              ReadAllTextShared(JoinCodePath)),
                    150d,
                    "join-code handoff");
                await controller.DevelopmentJoinSessionAsync(
                    ReadAllTextShared(JoinCodePath).Trim(),
                    displayName,
                    appearance);
                await WaitUntilAsync(
                    () => controller.IsInSession,
                    120d,
                    "client session join");
                ValidateForest(controller.CurrentSession.BoardMapSelection);
                Log("session_joined");
            }

            await WaitUntilAsync(
                () => controller.LocalSlot >= 0 &&
                      controller.GetLocalAvatar() != null,
                60d,
                "local avatar assignment");
            _assignedSlot = controller.LocalSlot;
            if (_playerIndex == 0 && _assignedSlot != 0)
            {
                throw new InvalidOperationException(
                    "Expected the host in slot 0, observed slot " +
                    _assignedSlot + ".");
            }
            if (_playerIndex == ReconnectingPlayer)
            {
                _reconnectingSlot = _assignedSlot;
                if (_reconnectingSlot <= 0 ||
                    _reconnectingSlot >= MultiplayerConstants.MaxPlayers)
                {
                    throw new InvalidOperationException(
                        "The reconnecting client received invalid slot " +
                        _reconnectingSlot + ".");
                }

                WriteAtomic(
                    ReconnectingSlotPath,
                    _reconnectingSlot.ToString(CultureInfo.InvariantCulture));
                Log("reconnecting_slot_recorded", "slot=" + _reconnectingSlot);
            }
            else if (IsHost)
            {
                await WaitUntilAsync(
                    TryLoadReconnectingSlot,
                    60d,
                    "reconnecting client slot handoff");
            }

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
                          controller.DevelopmentHasFourAssignedPlayers(),
                    150d,
                    "four ready players");
                await Task.Delay(1000);
                await WaitUntilAsync(
                    () => controller.CurrentSession.CanStart &&
                          controller.DevelopmentHasFourAssignedPlayers(),
                    30d,
                    "stable four-player ready lobby");
                TryDelete(JoinCodePath);
                await controller.DevelopmentStartGameAsync();
                Log("match_start_requested");
            }

            await WaitForBoardAsync();
            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    return match != null &&
                           match.FlowState == BoardFlowState.Action;
                },
                90d,
                "opening action phase");

            if (IsHost)
            {
                var reconnectingAvatar = RequireMatch()
                    .GetAvatarForSlot(_reconnectingSlot);
                if (reconnectingAvatar == null ||
                    !reconnectingAvatar.TryAddItemOnServer(
                        PrototypeItemId.Pistol))
                {
                    throw new InvalidOperationException(
                        "Could not provision the reconnecting player's item.");
                }

                WriteAtomic(ItemProvisionedPath, "PISTOL");
                Log(
                    "item_provisioned",
                    "slot=" + _reconnectingSlot + ";item=Pistol");
            }

            var localAvatar = controller.GetLocalAvatar();
            if (_playerIndex == ReconnectingPlayer)
            {
                await WaitUntilAsync(
                    () => File.Exists(ItemProvisionedPath) &&
                          localAvatar.LocalOccupiedItemMask != 0,
                    30d,
                    "reconnecting player item replication");
                localAvatar.ChooseItem(0);
            }
            else
            {
                localAvatar.ChooseNoItem();
            }

            Log(
                "item_choice_submitted",
                _playerIndex == ReconnectingPlayer ? "pistol" : "none");

            if (IsHost)
            {
                await RunHostDisconnectObservationAsync();
                return;
            }

            if (_playerIndex == ReconnectingPlayer)
            {
                await RunDisconnectingClientAsync(controller);
                return;
            }

            await WaitUntilAsync(
                () => File.Exists(QuitSignalPath),
                180d,
                "reconnect scenario completion");
        }

        private async Task RunHostDisconnectObservationAsync()
        {
            await WaitUntilAsync(
                () => AllChoicesResolved(RequireMatch()),
                45d,
                "all opening item choices");
            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    return match != null && match.IsPlayerPaused &&
                           match.PlayerPauseSlot == _reconnectingSlot;
                },
                45d,
                "player pause before disconnect");

            var matchBefore = RequireMatch();
            var avatarBefore = matchBefore.GetAvatarForSlot(_reconnectingSlot);
            if (avatarBefore == null)
            {
                throw new InvalidOperationException(
                    "The reconnecting avatar was unavailable before disconnect.");
            }

            var snapshotBefore = avatarBefore.CreateReconnectSnapshotOnServer();
            var actionRemainingBefore = matchBefore.ActionRemaining;
            var playerPauseRemainingBefore = matchBefore.PlayerPauseRemaining;
            if (!snapshotBefore.IsValid ||
                snapshotBefore.SelectedItemSlot != 0 ||
                snapshotBefore.EquippedItem != (byte)PrototypeItemId.Pistol)
            {
                throw new InvalidOperationException(
                    "The pre-disconnect owner state was not ready for validation.");
            }

            WriteAtomic(KillRequestPath, "KILL PLAYER 1");
            Log(
                "hard_disconnect_requested",
                "slot=" + _reconnectingSlot + ";actionRemaining=" +
                actionRemainingBefore.ToString("0.000", CultureInfo.InvariantCulture) +
                ";pauseRemaining=" +
                playerPauseRemainingBefore.ToString("0.000", CultureInfo.InvariantCulture));

            await WaitUntilAsync(
                () => File.Exists(ClientKilledPath),
                45d,
                "launcher hard-kill acknowledgement");
            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    return match != null && match.IsReconnectPaused &&
                           match.IsPlayerPauseActive &&
                           !match.IsPlayerPaused;
                },
                45d,
                "global reconnect pause");

            var heldAction = RequireMatch().ActionRemaining;
            var heldPlayerPause = RequireMatch().PlayerPauseRemaining;
            await Task.Delay(1000);
            var heldMatch = RequireMatch();
            AssertNearlyEqual(
                heldAction,
                heldMatch.ActionRemaining,
                0.15d,
                "action timer during reconnect pause");
            AssertNearlyEqual(
                heldPlayerPause,
                heldMatch.PlayerPauseRemaining,
                0.15d,
                "player-pause timer during reconnect pause");

            var reconnectScreenshot = Path.Combine(
                _runDirectory,
                "board-reconnect-pause-host.png");
            await CaptureScreenshotAsync(reconnectScreenshot);
            WriteAtomic(ReconnectPauseObservedPath, "OBSERVED");
            Log("reconnect_pause_verified");

            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    var avatar = match != null
                        ? match.GetAvatarForSlot(_reconnectingSlot)
                        : null;
                    return match != null && !match.IsReconnectPaused &&
                           match.IsPlayerPaused &&
                           match.PlayerPauseSlot == _reconnectingSlot &&
                           avatar != null && avatar.IsSpawned;
                },
                90d,
                "same-seat client reconnection");

            var restoredMatch = RequireMatch();
            await WaitForRestoredSnapshotAsync(snapshotBefore, 5d);
            AssertNearlyEqual(
                heldPlayerPause,
                restoredMatch.PlayerPauseRemaining,
                0.75d,
                "resumed player-pause duration");
            WriteAtomic(ReconnectRestoredPath, "RESTORED");
            Log(
                "reconnect_state_verified",
                "slot=" + _reconnectingSlot);

            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    return match != null &&
                           !match.IsReconnectPaused &&
                           !match.IsPlayerPauseActive;
                },
                45d,
                "reconnected player pause release");

            WriteAtomic(
                SummaryPath,
                "{\"map\":\"forest-graybox\"," +
                "\"mapContentVersion\":4," +
                "\"reconnectingSlot\":" + _reconnectingSlot + "," +
                "\"hardDisconnect\":true," +
                "\"reconnectPauseHeldTimers\":true," +
                "\"sameSeatStateRestored\":true," +
                "\"playerPauseResumedAndReleased\":true}");
            WriteAtomic(QuitSignalPath, "COMPLETE");
            Log("scenario_complete");
        }

        private async Task RunDisconnectingClientAsync(
            OnlineSessionController controller)
        {
            await WaitUntilAsync(
                () =>
                {
                    var local = controller.GetLocalAvatar();
                    return local != null &&
                           local.LocalChoiceResolution ==
                               ItemChoiceResolution.ItemSelected &&
                           local.LocalSelectedItemSlot == 0;
                },
                30d,
                "selected item before pause");
            if (!controller.RequestPlayerPause())
            {
                throw new InvalidOperationException(
                    "The reconnecting client could not request a player pause.");
            }

            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    return match != null && match.IsPlayerPaused &&
                           match.PlayerPauseSlot == _reconnectingSlot;
                },
                30d,
                "local player pause");
            var localAvatar = controller.GetLocalAvatar();
            WriteAtomic(
                ClientStateBeforePath,
                "slot=" + localAvatar.AssignedSlot +
                ";tile=" + localAvatar.LogicalBoardTileCoordinate +
                ";selected=" + localAvatar.LocalSelectedItemSlot +
                ";charges=" + localAvatar.LocalItemCharges +
                ";health=" + localAvatar.CurrentHealth +
                ";gold=" + localAvatar.Gold);
            Log("player_pause_verified", "awaiting hard kill");

            // The launcher must terminate this process without allowing the
            // application-quitting callback to clear its reconnect ticket.
            await WaitUntilAsync(
                () => File.Exists(QuitSignalPath),
                600d,
                "external hard disconnect");
        }

        private async Task RunResumedClientAsync()
        {
            if (IsHost || _playerIndex != ReconnectingPlayer)
            {
                throw new InvalidOperationException(
                    "Only client player 1 may run the reconnect resume stage.");
            }

            await WaitUntilAsync(
                TryLoadReconnectingSlot,
                60d,
                "recorded reconnecting slot");
            var controller = await WaitForControllerAsync();
            await WaitUntilAsync(
                () => controller.IsInSession &&
                      controller.CurrentSession.Phase ==
                          MultiplayerConstants.PlayingPhase,
                120d,
                "automatic session reconnection");
            await WaitForBoardAsync();
            await WaitUntilAsync(
                () =>
                {
                    var local = controller.GetLocalAvatar();
                    var match = NetworkMatchState.Instance;
                    return local != null &&
                           local.AssignedSlot == _reconnectingSlot &&
                           local.LocalSelectedItemSlot == 0 &&
                           local.LocalEquippedItem == PrototypeItemId.Pistol &&
                           local.LocalItemCharges ==
                               PrototypeItemCatalog.Get(
                                   PrototypeItemId.Pistol).Charges &&
                           match != null && match.IsPlayerPaused &&
                           match.PlayerPauseSlot == _reconnectingSlot;
                },
                90d,
                "owner state and held player pause restoration");
            _assignedSlot = controller.LocalSlot;

            var restoredScreenshot = Path.Combine(
                _runDirectory,
                "board-player-pause-restored-client1.png");
            await CaptureScreenshotAsync(restoredScreenshot);
            await WaitUntilAsync(
                () => File.Exists(ReconnectRestoredPath),
                45d,
                "host restored-state validation before pause release");
            if (!controller.RequestPlayerPauseRelease())
            {
                throw new InvalidOperationException(
                    "The reconnected client could not release its player pause.");
            }

            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    return match != null && !match.IsPlayerPauseActive &&
                           !match.IsReconnectPaused;
                },
                45d,
                "player pause release after reconnect");
            WriteAtomic(ResumeSucceededPath, "RESUMED");
            Log("resume_client_verified");
            await WaitUntilAsync(
                () => File.Exists(QuitSignalPath),
                90d,
                "host scenario completion");
        }

        private async Task WaitForBoardAsync()
        {
            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    return match != null && match.IsSpawned &&
                           match.IsBoardMapReady && match.GameplayEnabled;
                },
                180d,
                "Forest board synchronization");
            ValidateForest(RequireMatch().CurrentBoardMapSelection);
            if (!SceneManager.GetSceneByName(
                    MultiplayerConstants.BoardScene).isLoaded)
            {
                throw new InvalidOperationException(
                    "The Board scene did not report loaded.");
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

        private async Task CaptureScreenshotAsync(string path)
        {
            if (File.Exists(path))
            {
                throw new InvalidOperationException(
                    "Refusing to overwrite reconnect evidence: " + path);
            }

            ScreenCapture.CaptureScreenshot(path);
            await WaitUntilAsync(
                () => IsNonEmptyFile(path),
                15d,
                "reconnect screenshot " + Path.GetFileName(path));
        }

        private async Task WaitForRestoredSnapshotAsync(
            ReconnectSnapshot before,
            double timeoutSeconds)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
            var lastMismatch = "the restored avatar was unavailable";
            while (!_quitting &&
                   Time.realtimeSinceStartupAsDouble < deadline)
            {
                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(_reconnectingSlot)
                    : null;
                if (avatar != null && avatar.IsSpawned)
                {
                    var after = avatar.CreateReconnectSnapshotOnServer();
                    if (TryValidateRestoredSnapshot(
                            before,
                            after,
                            out lastMismatch))
                    {
                        return;
                    }
                }

                await Task.Delay(PollMilliseconds);
            }

            throw new TimeoutException(
                "Timed out after " +
                timeoutSeconds.ToString("0.0", CultureInfo.InvariantCulture) +
                " seconds waiting for complete reconnect snapshot restoration. " +
                "Last mismatch: " + lastMismatch + ".");
        }

        private static bool TryValidateRestoredSnapshot(
            ReconnectSnapshot before,
            ReconnectSnapshot after,
            out string mismatch)
        {
            var differences = new List<string>();
            AddDifference(differences, "IsValid", true, after.IsValid);
            AddDifference(
                differences,
                "SelectedItemSlot",
                before.SelectedItemSlot,
                after.SelectedItemSlot);
            AddDifference(
                differences,
                "EquippedItem",
                before.EquippedItem,
                after.EquippedItem);
            AddDifference(
                differences,
                "ItemCharges",
                before.ItemCharges,
                after.ItemCharges);
            AddDifference(
                differences,
                "OccupiedItemMask",
                before.OccupiedItemMask,
                after.OccupiedItemMask);
            AddDifference(
                differences,
                "ItemSlot0",
                before.ItemSlot0,
                after.ItemSlot0);
            AddDifference(
                differences,
                "RemainingMoves",
                before.RemainingMoves,
                after.RemainingMoves);
            AddDifference(
                differences,
                "CurrentHealth",
                before.CurrentHealth,
                after.CurrentHealth);
            AddDifference(
                differences,
                "KeyCount",
                before.KeyCount,
                after.KeyCount);
            AddDifference(differences, "Gold", before.Gold, after.Gold);
            AddDifference(
                differences,
                "HasLogicalCurrentTile",
                before.HasLogicalCurrentTile,
                after.HasLogicalCurrentTile);
            AddDifference(
                differences,
                "LogicalCurrentTileCoordinate",
                before.LogicalCurrentTileCoordinate,
                after.LogicalCurrentTileCoordinate);

            var positionDistance = Vector3.Distance(
                before.Position,
                after.Position);
            if (positionDistance > 0.1f)
            {
                differences.Add(
                    "Position expected=" + before.Position.ToString("F3") +
                    ", actual=" + after.Position.ToString("F3") +
                    ", distance=" +
                    positionDistance.ToString("0.000", CultureInfo.InvariantCulture));
            }

            mismatch = string.Join("; ", differences);
            return differences.Count == 0;
        }

        private static void AddDifference<T>(
            ICollection<string> differences,
            string field,
            T expected,
            T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                differences.Add(
                    field + " expected=" + expected + ", actual=" + actual);
            }
        }

        private static bool AllChoicesResolved(NetworkMatchState match)
        {
            for (var slot = 0;
                 slot < MultiplayerConstants.MaxPlayers;
                 slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar == null || !avatar.IsSpawned ||
                    !avatar.HasResolvedItemChoice)
                {
                    return false;
                }
            }

            return true;
        }

        private static NetworkMatchState RequireMatch()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsSpawned)
            {
                throw new InvalidOperationException(
                    "The network match state is unavailable.");
            }

            return match;
        }

        private static void ValidateForest(BoardMapSelection selection)
        {
            if (!string.Equals(
                    selection.MapId,
                    ForestMapId,
                    StringComparison.Ordinal) ||
                selection.ContentVersion != ForestContentVersion)
            {
                throw new InvalidOperationException(
                    "Expected Forest " + ForestMapId + " v" +
                    ForestContentVersion + ", observed " +
                    selection.MapId + " v" + selection.ContentVersion + ".");
            }
        }

        private static void AssertNearlyEqual(
            double expected,
            double actual,
            double tolerance,
            string description)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    description + " changed from " +
                    expected.ToString("0.000", CultureInfo.InvariantCulture) +
                    " to " +
                    actual.ToString("0.000", CultureInfo.InvariantCulture) + ".");
            }
        }

        private bool TryLoadReconnectingSlot()
        {
            if (_reconnectingSlot >= 0)
            {
                return true;
            }
            if (!File.Exists(ReconnectingSlotPath) ||
                !int.TryParse(
                    ReadAllTextShared(ReconnectingSlotPath).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var slot) ||
                slot <= 0 || slot >= MultiplayerConstants.MaxPlayers)
            {
                return false;
            }

            _reconnectingSlot = slot;
            return true;
        }

        private async Task WaitUntilAsync(
            Func<bool> predicate,
            double timeoutSeconds,
            string description)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
            while (!_quitting &&
                   Time.realtimeSinceStartupAsDouble < deadline)
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
            _role = GetArgument(arguments, "-e2e-role").ToLowerInvariant();
            _stage = GetArgument(
                arguments,
                "-e2e-reconnect-stage").ToLowerInvariant();
            if (_role != HostRole && _role != ClientRole)
            {
                throw new ArgumentException(
                    "-e2e-role must be host or client.");
            }
            if (_stage != InitialStage && _stage != ResumeStage)
            {
                throw new ArgumentException(
                    "-e2e-reconnect-stage must be initial or resume.");
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

            _runDirectory = Path.GetFullPath(
                GetArgument(arguments, "-e2e-run-dir"));
            if (IsHost != (_playerIndex == 0) ||
                (_stage == ResumeStage &&
                 (IsHost || _playerIndex != ReconnectingPlayer)))
            {
                throw new ArgumentException(
                    "The reconnect role, player, and stage combination is invalid.");
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

        private bool IsHost =>
            string.Equals(_role, HostRole, StringComparison.Ordinal);

        private string JoinCodePath =>
            Path.Combine(_runDirectory, "join-code.txt");
        private string ItemProvisionedPath =>
            Path.Combine(_runDirectory, "item-provisioned.marker");
        private string ReconnectingSlotPath =>
            Path.Combine(_runDirectory, "client-1-slot.txt");
        private string KillRequestPath =>
            Path.Combine(_runDirectory, "kill-client-1.request");
        private string ClientKilledPath =>
            Path.Combine(_runDirectory, "client-1-killed.marker");
        private string ReconnectPauseObservedPath =>
            Path.Combine(_runDirectory, "reconnect-pause-observed.marker");
        private string ReconnectRestoredPath =>
            Path.Combine(_runDirectory, "reconnect-restored.marker");
        private string ResumeSucceededPath =>
            Path.Combine(_runDirectory, "resume-client-succeeded.marker");
        private string ClientStateBeforePath =>
            Path.Combine(_runDirectory, "client-1-state-before.txt");
        private string QuitSignalPath =>
            Path.Combine(_runDirectory, "quit.signal");
        private string SummaryPath =>
            Path.Combine(_runDirectory, "summary.json");
        private string PassedMarkerPath =>
            Path.Combine(_runDirectory, "passed-" + _playerIndex + ".marker");

        private void Log(string eventName, string details = "")
        {
            var elapsed = Math.Max(
                0d,
                Time.realtimeSinceStartupAsDouble - _startedAt);
            var line =
                "{\"utc\":\"" +
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                "\",\"elapsed\":" +
                elapsed.ToString("0.000", CultureInfo.InvariantCulture) +
                ",\"player\":" + _playerIndex +
                ",\"slot\":" + _assignedSlot +
                ",\"stage\":\"" + EscapeJson(_stage) +
                "\",\"role\":\"" + EscapeJson(_role) +
                "\",\"event\":\"" + EscapeJson(eventName) +
                "\",\"details\":\"" + EscapeJson(details) + "\"}" +
                Environment.NewLine;
            lock (_fileGate)
            {
                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }

            Debug.Log(
                "[ReconnectE2E P" + _playerIndex + " " + _stage + "] " +
                eventName +
                (string.IsNullOrWhiteSpace(details)
                    ? string.Empty
                    : " | " + details));
        }

        private void TryLogFailure(Exception exception)
        {
            try
            {
                Directory.CreateDirectory(_runDirectory);
                if (string.IsNullOrWhiteSpace(_logPath))
                {
                    _logPath = Path.Combine(
                        _runDirectory,
                        "player-" + _playerIndex + "-" + _stage + ".jsonl");
                }
                Log("fatal", exception.GetType().Name + ": " + exception.Message);
                WriteAtomic(
                    Path.Combine(
                        _runDirectory,
                        "failed-" + _playerIndex + "-" + _stage + ".marker"),
                    exception.ToString());
            }
            catch (Exception loggingException)
            {
                Debug.LogException(loggingException);
            }
        }

        private static void QuitProcess(int exitCode)
        {
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

        private static bool HasSwitch(
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
                if (string.Equals(
                        arguments[index],
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return index + 1 < arguments.Count
                        ? arguments[index + 1]
                        : string.Empty;
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

        private static bool IsNonEmptyFile(string path)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void WriteAtomic(string path, string contents)
        {
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
                // A joining process may still have the handoff file open.
            }
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}
#endif
