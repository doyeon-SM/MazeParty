#if UNITY_EDITOR || DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Opt-in four-process acceptance driver for board item presentation and
    /// authority. It is installed only by a Development Player launched with
    /// -e2e-item-qa; ordinary players and release builds are unaffected.
    /// </summary>
    internal sealed class DevelopmentItemQaE2EDriver : MonoBehaviour
    {
        private const string EnableArgument = "-e2e-item-qa";
        private const string HostRole = "host";
        private const string ClientRole = "client";
        private const string ForestMapId = "forest-graybox";
        private const int ForestContentVersion = 4;
        private const int PollMilliseconds = 50;
        private const float ColorTolerance = 0.015f;

        private static readonly PrototypeItemId[] ItemBySlot =
        {
            PrototypeItemId.Pistol,
            PrototypeItemId.Sniper,
            PrototypeItemId.Grenade,
            PrototypeItemId.Mine
        };

        private readonly object _fileGate = new object();
        private readonly List<string> _screenshots = new List<string>();
        private readonly List<Task> _pendingScreenshotCaptures =
            new List<Task>();
        private readonly Dictionary<string, ScreenshotEvidence>
            _screenshotEvidence =
                new Dictionary<string, ScreenshotEvidence>(
                    StringComparer.OrdinalIgnoreCase);
        private static bool s_installed;

        private readonly struct ScreenshotEvidence
        {
            public ScreenshotEvidence(
                int width,
                int height,
                int nonBlackPixels,
                int luminanceRange)
            {
                Width = width;
                Height = height;
                NonBlackPixels = nonBlackPixels;
                LuminanceRange = luminanceRange;
            }

            public int Width { get; }
            public int Height { get; }
            public int NonBlackPixels { get; }
            public int LuminanceRange { get; }
        }

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
        private string _runDirectory;
        private string _artifactDirectory;
        private string _logPath;
        private string _shotCaptureLabel;
        private int _playerIndex;
        private int _assignedSlot = -1;
        private double _startedAt;
        private bool _quitting;
        private NetworkMatchState _observedMatch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_installed || Application.isEditor || !Debug.isDebugBuild ||
                !HasCommandLineSwitch(
                    Environment.GetCommandLineArgs(),
                    EnableArgument))
            {
                return;
            }

            s_installed = true;
            var driverObject = new GameObject("[Development Item QA E2E]");
            DontDestroyOnLoad(driverObject);
            driverObject.AddComponent<DevelopmentItemQaE2EDriver>();
        }

        private async void Start()
        {
            _startedAt = Time.realtimeSinceStartupAsDouble;
            try
            {
                ParseArguments();
                Application.runInBackground = true;
                Directory.CreateDirectory(_runDirectory);
                Directory.CreateDirectory(_artifactDirectory);
                _logPath = Path.Combine(
                    _runDirectory,
                    "item-player-" + _playerIndex.ToString(
                        CultureInfo.InvariantCulture) + ".jsonl");
                TryDelete(_logPath);

                Log(
                    "driver_started",
                    "map=" + ForestMapId +
                    ";realRelay=true;controlledTransportSimulation=false");
                await RunAsync();
                await WaitForPendingScreenshotCapturesAsync();
                WriteScreenshotManifest();
                WriteAtomic(PassedPath(_playerIndex), "PASS");
                Log("driver_passed");

                if (IsHost)
                {
                    await WaitUntilAsync(
                        AllPassMarkersPresent,
                        30d,
                        "all item QA pass markers");
                    WriteAtomic(
                        Path.Combine(_runDirectory, "summary.json"),
                        BuildSummary());
                    WriteAtomic(QuitSignalPath, "COMPLETE");
                }
                else
                {
                    await WaitUntilAsync(
                        () => File.Exists(QuitSignalPath),
                        30d,
                        "host item QA completion signal");
                }

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

        private void OnDestroy()
        {
            DetachMatchObservation();
        }

        private async Task RunAsync()
        {
            var controller = await WaitForControllerAsync();
            var appearance = BuildAppearance(_playerIndex);
            var displayName = "Item QA Player " + (_playerIndex + 1);

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
                    "host item QA session creation");

                if (!IsForest(controller.CurrentSession.BoardMapSelection))
                {
                    if (!controller.DevelopmentRequestForestBoardMap())
                    {
                        throw new InvalidOperationException(
                            "Could not request the Forest board map.");
                    }

                    await WaitUntilAsync(
                        () => IsForest(
                            controller.CurrentSession.BoardMapSelection),
                        30d,
                        "Forest board-map selection");
                }

                WriteAtomic(
                    JoinCodePath,
                    controller.CurrentSession.Code.Trim());
                Log("session_created", "map=" + ForestMapId);
            }
            else
            {
                await Task.Delay(_playerIndex * 900);
                await WaitUntilAsync(
                    () => File.Exists(JoinCodePath) &&
                          !string.IsNullOrWhiteSpace(
                              ReadAllTextShared(JoinCodePath)),
                    150d,
                    "item QA join-code handoff");
                await controller.DevelopmentJoinSessionAsync(
                    ReadAllTextShared(JoinCodePath).Trim(),
                    displayName,
                    appearance);
                await WaitUntilAsync(
                    () => controller.IsInSession,
                    120d,
                    "item QA client join");
                ValidateForest(
                    controller.CurrentSession.BoardMapSelection);
                Log("session_joined", "map=" + ForestMapId);
            }

            await WaitUntilAsync(
                () => controller.LocalSlot >= 0 &&
                      controller.GetLocalAvatar() != null,
                60d,
                "local item QA avatar assignment");
            _assignedSlot = controller.LocalSlot;
            // Client joins may complete in a different order. The scenario is
            // assigned by the authoritative seat, not by launcher process.
            if (IsHost && _assignedSlot != 0)
            {
                throw new InvalidOperationException(
                    "The item QA host must own slot 0, but it owns slot " +
                    _assignedSlot + ".");
            }

            await controller.DevelopmentSetReadyAsync(true);
            await WaitUntilAsync(
                () => controller.CurrentSession.LocalReady,
                30d,
                "local item QA ready state");

            if (IsHost)
            {
                await WaitUntilAsync(
                    () => controller.CurrentSession.CanStart &&
                          controller.DevelopmentHasFourAssignedPlayers(),
                    150d,
                    "four ready item QA players");
                TryDelete(JoinCodePath);
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
                "Forest board synchronization for item QA");

            var matchState = RequireMatch();
            ValidateForest(matchState.CurrentBoardMapSelection);
            if (!SceneManager.GetSceneByName(
                    MultiplayerConstants.BoardScene).isLoaded)
            {
                throw new InvalidOperationException(
                    "Board scene did not report loaded for item QA.");
            }

            AttachMatchObservation(matchState);
            Log(
                "board_ready",
                "slot=" + _assignedSlot +
                ";map=" + matchState.BoardMapId +
                ";version=" + matchState.BoardMapContentVersion);

            var participantTask = RunParticipantScriptAsync(controller);
            if (IsHost)
            {
                await RunHostScriptAsync();
            }

            await participantTask;
        }

        private async Task<OnlineSessionController> WaitForControllerAsync()
        {
            await WaitUntilAsync(
                () => OnlineSessionController.Instance != null,
                60d,
                "online session controller");
            return OnlineSessionController.Instance;
        }

        private async Task RunParticipantScriptAsync(
            OnlineSessionController controller)
        {
            await WaitForSignalAsync("items-provisioned", 120d);
            var local = RequireLocalAvatar(controller);
            var expectedItem = ItemBySlot[_assignedSlot];
            await WaitUntilAsync(
                () => FindLocalItemSlot(local, expectedItem) >= 0,
                15d,
                expectedItem + " inventory replication");
            local.ChooseItem(FindLocalItemSlot(local, expectedItem));
            await WaitUntilAsync(
                () => local.LocalChoiceResolution ==
                          ItemChoiceResolution.ItemSelected &&
                      local.LocalEquippedItem == expectedItem,
                15d,
                expectedItem + " selection replication");
            Mark("item-selected");
            Log(
                "item_selected",
                "slot=" + _assignedSlot + ";item=" + expectedItem);

            await ObserveFirearmAimAsync(
                "pistol-valid",
                0,
                1,
                true,
                false);
            await ObserveShotAsync(
                "pistol-valid",
                0,
                1,
                PrototypeItemId.Pistol,
                false);
            await ObserveFirearmAimAsync(
                "pistol-far",
                0,
                1,
                false,
                false);
            await ObserveFirearmAimAsync(
                "pistol-occluded",
                0,
                1,
                false,
                false);
            await ObserveFirearmAimAsync(
                "pistol-cloaked",
                0,
                1,
                false,
                false);
            await RunPistolKillAsync(controller);
            await ObserveFirearmAimAsync(
                "pistol-dead",
                0,
                1,
                false,
                false);
            await ObserveFirearmAimAsync(
                "pistol-respawn-protected",
                0,
                1,
                false,
                false);

            await ObserveFirearmAimAsync(
                "sniper-valid",
                1,
                0,
                true,
                true);
            await ObserveShotAsync(
                "sniper-valid",
                1,
                0,
                PrototypeItemId.Sniper,
                true);

            await ObserveGrenadeOwnershipAsync(controller);
            await RunRejectedGrenadeAsync(controller);
            await RunAcceptedGrenadeAsync(controller);
            await RunMinePlacementAsync(controller);
            await ObserveMineCleanupAsync();

            await WaitForSignalAsync("qa-complete", 60d);
            Mark("participant-complete");
            Log("participant_complete");
        }

        private async Task RunHostScriptAsync()
        {
            await WaitUntilAsync(
                () =>
                {
                    var match = NetworkMatchState.Instance;
                    return match != null && match.GameplayEnabled &&
                           match.Turn == 1 &&
                           match.FlowState == BoardFlowState.Action;
                },
                120d,
                "turn-one item QA action phase");

            var matchState = RequireMatch();
            var avatars = RequireFourAvatars(matchState);
            for (var slot = 0; slot < avatars.Length; slot++)
            {
                if (!avatars[slot].TryAddItemOnServer(ItemBySlot[slot]))
                {
                    throw new InvalidOperationException(
                        "Could not provision " + ItemBySlot[slot] +
                        " for slot " + slot + ".");
                }
            }

            Signal("items-provisioned");
            await WaitForAllMarkersAsync("item-selected", 30d);
            await WaitUntilAsync(
                () => AllChoicesResolved(RequireMatch()),
                30d,
                "all item QA selections");
            await WaitUntilAsync(
                () => RequireMatch().ShieldRemaining <= 0.01d,
                20d,
                "opening item shield expiry");

            await HostRunPistolAsync(avatars);
            await HostRunSniperAsync(avatars);
            await HostRunGrenadeAsync(avatars);
            await HostRunMineAsync(avatars);

            Signal("qa-complete");
            await WaitForAllMarkersAsync("participant-complete", 30d);
        }

        private async Task HostRunPistolAsync(
            IReadOnlyList<NetworkPlayerAvatar> avatars)
        {
            var match = RequireMatch();
            if (!match.DevelopmentArrangeNearPair(0, 1))
            {
                throw new InvalidOperationException(
                    "Could not arrange the valid Pistol pair.");
            }

            await AllowReplicationAsync();
            Signal("pistol-valid-aim");
            await WaitForSlotMarkerAsync("pistol-valid-aim", 0, 10d);
            Signal("pistol-valid-observe");
            await WaitForAllMarkersAsync("pistol-valid-ready", 10d);
            var healthBefore = avatars[1].CurrentHealth;
            Signal("pistol-valid-fire");
            await WaitUntilAsync(
                () => avatars[1].CurrentHealth == healthBefore - 20,
                5d,
                "valid Pistol hitscan damage");
            await WaitForAllMarkersAsync("pistol-valid-shot", 10d);
            if (match.DevelopmentAuthoritativeGrenadeCount != 0)
            {
                throw new InvalidOperationException(
                    "Pistol created an authoritative projectile.");
            }

            if (!match.DevelopmentArrangeFarPair(
                    0,
                    1,
                    PrototypeItemCatalog.Get(PrototypeItemId.Pistol).Range +
                    1f))
            {
                throw new InvalidOperationException(
                    "Could not arrange an out-of-range Pistol target.");
            }

            await AllowReplicationAsync();
            Signal("pistol-far-aim");
            await WaitForSlotMarkerAsync("pistol-far-aim", 0, 10d);

            if (!match.DevelopmentArrangeOccludedPair(
                    0,
                    1,
                    PrototypeItemCatalog.Get(PrototypeItemId.Pistol).Range))
            {
                throw new InvalidOperationException(
                    "Forest has no discoverable ordinary-wall Pistol occlusion pair.");
            }

            await AllowReplicationAsync();
            Signal("pistol-occluded-aim");
            await WaitForSlotMarkerAsync("pistol-occluded-aim", 0, 10d);

            if (!match.DevelopmentArrangeNearPair(0, 1) ||
                !avatars[1].DevelopmentSetCloakedOnServer(true))
            {
                throw new InvalidOperationException(
                    "Could not arrange the cloaked Pistol target.");
            }

            await AllowReplicationAsync();
            Signal("pistol-cloaked-aim");
            await WaitForSlotMarkerAsync("pistol-cloaked-aim", 0, 10d);
            if (!avatars[1].DevelopmentSetCloakedOnServer(false))
            {
                throw new InvalidOperationException(
                    "Could not clear the item QA cloak state.");
            }

            if (!match.DevelopmentArrangeNearPair(0, 1))
            {
                throw new InvalidOperationException(
                    "Could not arrange the Pistol death sequence.");
            }

            await AllowReplicationAsync();
            Signal("pistol-kill");
            await WaitUntilAsync(
                () => avatars[1].CurrentHealth == 0 &&
                      avatars[1].IsBoardDeathInProgressOnServer,
                8d,
                "Pistol death state");
            Signal("pistol-dead-aim");
            await WaitForSlotMarkerAsync("pistol-dead-aim", 0, 10d);

            await WaitUntilAsync(
                () => !avatars[1].IsBoardDeathInProgressOnServer &&
                      avatars[1].CurrentHealth == avatars[1].MaxHealth &&
                      avatars[1].PersonalItemProtectionRemaining > 0d,
                8d,
                "Pistol target respawn protection");
            if (!match.DevelopmentArrangeNearPair(0, 1))
            {
                throw new InvalidOperationException(
                    "Could not arrange the protected Pistol target.");
            }

            await AllowReplicationAsync();
            Signal("pistol-respawn-protected-aim");
            await WaitForSlotMarkerAsync(
                "pistol-respawn-protected-aim",
                0,
                10d);
            if (!avatars[1].DevelopmentClearItemProtectionOnServer())
            {
                throw new InvalidOperationException(
                    "Could not clear item protection before Sniper QA.");
            }
        }

        private async Task HostRunSniperAsync(
            IReadOnlyList<NetworkPlayerAvatar> avatars)
        {
            var match = RequireMatch();
            if (!match.DevelopmentArrangeNearPair(1, 0))
            {
                throw new InvalidOperationException(
                    "Could not arrange the valid Sniper pair.");
            }

            await AllowReplicationAsync();
            Signal("sniper-valid-aim");
            await WaitForSlotMarkerAsync("sniper-valid-aim", 1, 10d);
            Signal("sniper-valid-observe");
            await WaitForAllMarkersAsync("sniper-valid-ready", 10d);
            var healthBefore = avatars[0].CurrentHealth;
            Signal("sniper-valid-fire");
            await WaitUntilAsync(
                () => avatars[0].CurrentHealth == healthBefore - 50,
                5d,
                "valid Sniper hitscan damage");
            await WaitForAllMarkersAsync("sniper-valid-shot", 10d);
            if (match.DevelopmentAuthoritativeGrenadeCount != 0)
            {
                throw new InvalidOperationException(
                    "Sniper created an authoritative projectile.");
            }
        }

        private async Task HostRunGrenadeAsync(
            IReadOnlyList<NetworkPlayerAvatar> avatars)
        {
            var match = RequireMatch();
            if (!match.DevelopmentArrangeGroundUse(2))
            {
                throw new InvalidOperationException(
                    "Could not arrange the Grenade owner.");
            }

            await AllowReplicationAsync();
            Signal("grenade-owner-visibility");
            await WaitForAllMarkersAsync("grenade-owner-visibility", 15d);

            Signal("grenade-rejected");
            await WaitForSlotMarkerAsync("grenade-rejected", 2, 15d);
            if (avatars[2].GetSelectedItemOnServer() !=
                    PrototypeItemId.Grenade ||
                match.DevelopmentAuthoritativeGrenadeCount != 0)
            {
                throw new InvalidOperationException(
                    "Rejected Grenade request changed authoritative state.");
            }

            Signal("grenade-accepted");
            await WaitUntilAsync(
                () => match.DevelopmentAuthoritativeGrenadeCount > 0,
                15d,
                "accepted Grenade projectile");
            await WaitUntilAsync(
                () => match.DevelopmentAuthoritativeGrenadeCount == 0,
                6d,
                "Grenade settlement cleanup");

            var samples = new List<Vector3>();
            if (!match.DevelopmentCopyGrenadeTrajectory(
                    samples,
                    out var rangeLimit,
                    out var plannedDistance,
                    out var flightDuration,
                    out var observedDuration))
            {
                throw new InvalidOperationException(
                    "No authoritative Grenade trajectory was recorded.");
            }

            var arcEvidence = ValidateGrenadeArc(
                samples,
                rangeLimit,
                plannedDistance,
                flightDuration,
                observedDuration);
            Log("grenade_authoritative_arc_verified", arcEvidence);
            await WaitForAllMarkersAsync("grenade-accepted", 15d);
        }

        private async Task HostRunMineAsync(
            IReadOnlyList<NetworkPlayerAvatar> avatars)
        {
            var match = RequireMatch();
            if (!match.DevelopmentArrangeMineGroundUse(3))
            {
                throw new InvalidOperationException(
                    "Could not arrange the Mine owner.");
            }

            await AllowReplicationAsync();
            Signal("mine-place");
            await WaitUntilAsync(
                () => match.DevelopmentAuthoritativeMineCount == 1,
                5d,
                "authoritative Mine placement");
            await WaitForAllMarkersAsync("mine-place", 15d);

            await Task.Delay(1250);
            var targetHealth = avatars[1].CurrentHealth;
            if (!match.DevelopmentRelocateSlotToFirstMine(1))
            {
                throw new InvalidOperationException(
                    "Could not move a target onto the armed Mine.");
            }

            await WaitUntilAsync(
                () => match.DevelopmentAuthoritativeMineCount == 0 &&
                      avatars[1].CurrentHealth == targetHealth - 50,
                5d,
                "Mine trigger damage and cleanup");
            Signal("mine-cleanup");
            await WaitForAllMarkersAsync("mine-cleanup", 15d);
        }

        private async Task ObserveFirearmAimAsync(
            string label,
            int shooterSlot,
            int targetSlot,
            bool expectedDamageable,
            bool exerciseRightMouse)
        {
            await WaitForSignalAsync(label + "-aim", 120d);
            if (_assignedSlot != shooterSlot)
            {
                return;
            }

            var match = RequireMatch();
            var shooter = match.GetAvatarForSlot(shooterSlot);
            var target = match.GetAvatarForSlot(targetSlot);
            if (shooter == null || target == null ||
                !shooter.DevelopmentPrepareItemShot(
                    target.transform.position,
                    out _,
                    out _))
            {
                throw new InvalidOperationException(
                    "Could not prepare " + label + " aim.");
            }

            await Task.Delay(250);
            var damageable = shooter.HasLocalDamageableFirearmTarget();
            if (damageable != expectedDamageable)
            {
                throw new InvalidOperationException(
                    label + " target highlight mismatch. Expected " +
                    expectedDamageable + ", observed " + damageable + ".");
            }

            ValidateReticle(expectedDamageable, label);
            var fovBefore = RequireMainCamera().fieldOfView;
            if (exerciseRightMouse)
            {
                var fovWhilePressed =
                    await PressSyntheticRightMouseAsync();
                var fovAfter = RequireMainCamera().fieldOfView;
                if (Mathf.Abs(fovWhilePressed - fovBefore) > 0.01f ||
                    Mathf.Abs(fovAfter - fovBefore) > 0.01f)
                {
                    throw new InvalidOperationException(
                        "Sniper RMB changed camera FOV from " +
                        fovBefore.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture) +
                        " to " +
                        fovWhilePressed.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture) +
                        " while held, then " +
                        fovAfter.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture) +
                        " after release.");
                }
            }

            await CaptureAndWaitAsync(label + "-p" + _playerIndex + ".png");
            Log(
                "firearm_aim_verified",
                "scenario=" + label +
                ";damageable=" + damageable +
                ";fov=" + fovBefore.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture));
            MarkSlot(label + "-aim");
        }

        private async Task ObserveShotAsync(
            string label,
            int shooterSlot,
            int targetSlot,
            PrototypeItemId expectedItem,
            bool fovWasExercised)
        {
            await WaitForSignalAsync(label + "-observe", 120d);
            var match = RequireMatch();
            match.DevelopmentResetPresentedShotObservation();
            _shotCaptureLabel = label;
            Mark(label + "-ready");
            await WaitForSignalAsync(label + "-fire", 20d);

            if (_assignedSlot == shooterSlot)
            {
                var shooter = match.GetAvatarForSlot(shooterSlot);
                var target = match.GetAvatarForSlot(targetSlot);
                if (shooter == null || target == null ||
                    !shooter.DevelopmentPrepareItemShot(
                        target.transform.position,
                        out _,
                        out _))
                {
                    throw new InvalidOperationException(
                        "Could not prepare " + label + " shot.");
                }

                await Task.Delay(175);
                if (!shooter.DevelopmentRequestUseSelectedItem())
                {
                    throw new InvalidOperationException(
                        "Could not submit " + label + " shot.");
                }
            }

            await WaitUntilAsync(
                () => match.DevelopmentPresentedShotCount == 1,
                5d,
                label + " tracer presentation");
            _shotCaptureLabel = null;
            if (match.DevelopmentLastShotItem != expectedItem ||
                match.DevelopmentLocalGrenadeViewCount != 0 ||
                match.DevelopmentLastShotWasProjectileLike)
            {
                throw new InvalidOperationException(
                    label + " did not remain a hitscan-only shot.");
            }

            var tracerPath = ScreenshotPath(
                label + "-tracer-p" + _playerIndex + ".png");
            await WaitForScreenshotAsync(tracerPath, 10d);
            Mark(label + "-shot");
            Log(
                "hitscan_verified",
                "scenario=" + label +
                ";item=" + expectedItem +
                ";didHit=" + match.DevelopmentLastShotHit +
                ";projectileLike=" +
                match.DevelopmentLastShotWasProjectileLike +
                ";fovExercised=" + fovWasExercised);
        }

        private async Task RunPistolKillAsync(
            OnlineSessionController controller)
        {
            await WaitForSignalAsync("pistol-kill", 120d);
            if (_assignedSlot != 0)
            {
                return;
            }

            var shooter = RequireLocalAvatar(controller);
            var target = RequireMatch().GetAvatarForSlot(1);
            for (var shot = 0; shot < 4; shot++)
            {
                if (target == null ||
                    !shooter.DevelopmentPrepareItemShot(
                        target.transform.position,
                        out _,
                        out _))
                {
                    throw new InvalidOperationException(
                        "Could not prepare Pistol death shot " +
                        (shot + 1) + ".");
                }

                await Task.Delay(175);
                if (!shooter.DevelopmentRequestUseSelectedItem())
                {
                    throw new InvalidOperationException(
                        "Could not submit Pistol death shot " +
                        (shot + 1) + ".");
                }

                await Task.Delay(350);
            }

            Mark("pistol-kill");
        }

        private async Task ObserveGrenadeOwnershipAsync(
            OnlineSessionController controller)
        {
            await WaitForSignalAsync("grenade-owner-visibility", 120d);
            var match = RequireMatch();
            var grenadeOwner = match.GetAvatarForSlot(2);
            var local = RequireLocalAvatar(controller);
            await Task.Delay(250);

            var ownerProcess = _assignedSlot == 2;
            var visible = grenadeOwner != null &&
                          grenadeOwner.GrenadeRangeIndicator != null &&
                          grenadeOwner.GrenadeRangeIndicator.IsVisible;
            if (visible != ownerProcess)
            {
                throw new InvalidOperationException(
                    "Grenade range owner visibility mismatch on slot " +
                    _assignedSlot + ".");
            }

            if (ownerProcess)
            {
                var radius = local.GrenadeRangeIndicator != null
                    ? local.GrenadeRangeIndicator.DevelopmentPresentedRadius
                    : 0f;
                if (Mathf.Abs(radius - 16f) > 0.01f ||
                    local.DevelopmentActiveItemLineCount != 1)
                {
                    throw new InvalidOperationException(
                        "Grenade range must be one 16m owner-only ring.");
                }
            }
            else if (grenadeOwner != null &&
                     grenadeOwner.DevelopmentActiveItemLineCount != 0)
            {
                throw new InvalidOperationException(
                    "A non-owner observed a Grenade preview line.");
            }

            await CaptureAndWaitAsync(
                "grenade-owner-visibility-p" + _playerIndex + ".png");
            Mark("grenade-owner-visibility");
        }

        private async Task RunRejectedGrenadeAsync(
            OnlineSessionController controller)
        {
            await WaitForSignalAsync("grenade-rejected", 120d);
            if (_assignedSlot != 2)
            {
                return;
            }

            var local = RequireLocalAvatar(controller);
            PrepareGrenadeAim(local);
            if (!local.DevelopmentBeginTrackedItemUse() ||
                !local.DevelopmentGrenadeUsePending ||
                local.GrenadeRangeIndicator.IsVisible ||
                local.DevelopmentActiveItemLineCount != 0)
            {
                throw new InvalidOperationException(
                    "Grenade rejection did not enter the hidden pending state.");
            }

            await CaptureAndWaitAsync(
                "grenade-rejected-pending-p" + _playerIndex + ".png");
            await Task.Delay(350);
            var submittedAt = Time.realtimeSinceStartupAsDouble;
            if (!local.DevelopmentSubmitTrackedItemUse(true))
            {
                throw new InvalidOperationException(
                    "Could not submit the intentionally invalid Grenade request.");
            }

            await WaitUntilAsync(
                () => !local.DevelopmentGrenadeUsePending &&
                      local.GrenadeRangeIndicator.IsVisible &&
                      local.LocalItemCharges == 1,
                5d,
                "Grenade rejection presentation recovery");
            var roundTripMilliseconds =
                (Time.realtimeSinceStartupAsDouble - submittedAt) * 1000d;
            await CaptureAndWaitAsync(
                "grenade-rejected-restored-p" + _playerIndex + ".png");
            Log(
                "grenade_rejection_recovered",
                "relayRoundTripMs=" + roundTripMilliseconds.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture) +
                ";preSubmitHoldMs=350");
            MarkSlot("grenade-rejected");
        }

        private async Task RunAcceptedGrenadeAsync(
            OnlineSessionController controller)
        {
            await WaitForSignalAsync("grenade-accepted", 120d);
            var match = RequireMatch();
            if (_assignedSlot == 2)
            {
                var local = RequireLocalAvatar(controller);
                PrepareGrenadeAim(local);
                if (!local.DevelopmentBeginTrackedItemUse() ||
                    local.GrenadeRangeIndicator.IsVisible ||
                    local.DevelopmentActiveItemLineCount != 0)
                {
                    throw new InvalidOperationException(
                        "Accepted Grenade did not hide the preview immediately.");
                }

                await CaptureAndWaitAsync(
                    "grenade-accepted-hidden-p" + _playerIndex + ".png");
                if (!local.DevelopmentSubmitTrackedItemUse(false))
                {
                    throw new InvalidOperationException(
                        "Could not submit the accepted Grenade request.");
                }

                await WaitUntilAsync(
                    () => local.LocalSelectedItemSlot < 0 &&
                          !local.GrenadeRangeIndicator.IsVisible,
                    5d,
                    "accepted Grenade consumption");
            }

            await WaitUntilAsync(
                () => match.DevelopmentLocalGrenadeViewCount > 0,
                5d,
                "replicated Grenade world view");
            await CaptureAndWaitAsync(
                "grenade-arc-p" + _playerIndex + ".png");
            if (match.GetAvatarForSlot(2).DevelopmentActiveItemLineCount != 0)
            {
                throw new InvalidOperationException(
                    "A trajectory-preview line appeared after Grenade use.");
            }

            Mark("grenade-accepted");
        }

        private async Task RunMinePlacementAsync(
            OnlineSessionController controller)
        {
            await WaitForSignalAsync("mine-place", 120d);
            var match = RequireMatch();
            if (_assignedSlot == 3)
            {
                var local = RequireLocalAvatar(controller);
                PrepareMineAim(local);
                await Task.Delay(350);
                if (!local.DevelopmentRequestUseSelectedItem())
                {
                    throw new InvalidOperationException(
                        "Could not submit Mine placement through the owner RPC.");
                }
            }

            var expectedOwnerCount = _assignedSlot == 3 ? 1 : 0;
            await WaitUntilAsync(
                () =>
                {
                    var mineOwner = match.GetAvatarForSlot(3);
                    return mineOwner != null &&
                           mineOwner.LocalMinePositions.Count ==
                               expectedOwnerCount &&
                           mineOwner.DevelopmentLocalMineViewCount ==
                               expectedOwnerCount;
                },
                5d,
                "owner-filtered Mine world replication");

            await ValidateMineMapMarkersAsync(expectedOwnerCount);
            await CaptureAndWaitAsync(
                "mine-owner-map-p" + _playerIndex + ".png");
            Mark("mine-place");
        }

        private async Task ObserveMineCleanupAsync()
        {
            await WaitForSignalAsync("mine-cleanup", 120d);
            var match = RequireMatch();
            var mineOwner = match.GetAvatarForSlot(3);
            await WaitUntilAsync(
                () => mineOwner != null &&
                      mineOwner.LocalMinePositions.Count == 0 &&
                      mineOwner.DevelopmentLocalMineViewCount == 0,
                5d,
                "Mine owner presentation cleanup");
            await ValidateMineMapMarkersAsync(0);
            if (_assignedSlot == 3)
            {
                await CaptureAndWaitAsync(
                    "mine-trigger-cleanup-p" + _playerIndex + ".png");
            }

            Mark("mine-cleanup");
        }

        private async Task ValidateMineMapMarkersAsync(int expectedCount)
        {
            var mapView = FindAnyObjectByType<BoardMapView>();
            if (mapView == null)
            {
                throw new InvalidOperationException(
                    "BoardMapView was unavailable during Mine QA.");
            }

            if (mapView.FullMapOpen)
            {
                mapView.UpdateFullMapState(true, true);
                await Task.Delay(150);
            }

            if (!mapView.FullMapOpen)
            {
                mapView.UpdateFullMapState(true, true);
            }

            await Task.Delay(250);
            var graphics = FindObjectsByType<BoardMapMineGraphic>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (graphics.Length < 2)
            {
                throw new InvalidOperationException(
                    "Expected authored minimap and full-map Mine graphics.");
            }

            for (var index = 0; index < graphics.Length; index++)
            {
                if (graphics[index].MarkerCount != expectedCount)
                {
                    throw new InvalidOperationException(
                        "Mine map marker count mismatch on process " +
                        _playerIndex + ": expected " + expectedCount +
                        ", observed " + graphics[index].MarkerCount + ".");
                }
            }
        }

        private void PrepareGrenadeAim(NetworkPlayerAvatar local)
        {
            var target = local.transform.position +
                         local.transform.forward *
                         PrototypeItemCatalog.Get(
                             PrototypeItemId.Grenade).Range;
            if (!local.DevelopmentPrepareItemShot(target, out _, out _))
            {
                throw new InvalidOperationException(
                    "Could not prepare the Grenade QA aim.");
            }
        }

        private void PrepareMineAim(NetworkPlayerAvatar local)
        {
            var topology = FindAnyObjectByType<BoardTopology>();
            if (topology == null || !local.HasLogicalBoardTile ||
                !topology.TryGetTile(
                    local.LogicalBoardTileCoordinate,
                    out var tile) ||
                !local.DevelopmentPrepareItemShot(
                    tile.WorldCenter,
                    out _,
                    out _))
            {
                throw new InvalidOperationException(
                    "Could not prepare the Mine ground aim.");
            }
        }

        private void ValidateReticle(bool damageable, string label)
        {
            var view = BoardFlowView.Instance;
            var bindings = view != null ? view.UiBindings : null;
            var reticle = bindings != null ? bindings.Reticle : null;
            var text = bindings != null ? bindings.ReticleText : null;
            if (reticle == null || text == null ||
                !reticle.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    label + " reticle was not visible.");
            }

            var expected = damageable
                ? bindings.ReticleDamageableTargetColor
                : bindings.ReticleDefaultColor;
            if (!Approximately(text.color, expected))
            {
                throw new InvalidOperationException(
                    label + " reticle color mismatch. Expected " +
                    expected + ", observed " + text.color + ".");
            }
        }

        private async Task<float> PressSyntheticRightMouseAsync()
        {
            var previousMouse = Mouse.current;
            Mouse synthetic = null;
            var fovWhilePressed = RequireMainCamera().fieldOfView;
            try
            {
                synthetic = InputSystem.AddDevice<Mouse>(
                    "Item QA Synthetic Mouse");
                synthetic.MakeCurrent();
                InputSystem.QueueStateEvent(
                    synthetic,
                    new MouseState().WithButton(MouseButton.Right));
                await Task.Yield();
                await Task.Yield();
                fovWhilePressed = RequireMainCamera().fieldOfView;
                InputSystem.QueueStateEvent(synthetic, new MouseState());
                await Task.Yield();
                await Task.Yield();
            }
            finally
            {
                if (synthetic != null && synthetic.added)
                {
                    InputSystem.RemoveDevice(synthetic);
                }

                if (previousMouse != null && previousMouse.added)
                {
                    previousMouse.MakeCurrent();
                }
            }

            return fovWhilePressed;
        }

        private static string ValidateGrenadeArc(
            IReadOnlyList<Vector3> samples,
            float rangeLimit,
            float plannedDistance,
            float flightDuration,
            float observedDuration)
        {
            if (samples == null || samples.Count < 3)
            {
                throw new InvalidOperationException(
                    "Grenade arc produced fewer than three authoritative samples.");
            }

            var start = samples[0];
            var maximumY = start.y;
            var maximumIndex = 0;
            var maximumPlanarDistance = 0f;
            for (var index = 1; index < samples.Count; index++)
            {
                if (samples[index].y > maximumY)
                {
                    maximumY = samples[index].y;
                    maximumIndex = index;
                }

                maximumPlanarDistance = Mathf.Max(
                    maximumPlanarDistance,
                    Vector3.ProjectOnPlane(
                        samples[index] - start,
                        Vector3.up).magnitude);
            }

            var definitionRange = PrototypeItemCatalog.Get(
                PrototypeItemId.Grenade).Range;
            var final = samples[samples.Count - 1];
            var rangeTolerance = 0.25f;
            var arcIsInvalid =
                Mathf.Abs(definitionRange - 16f) > 0.001f ||
                Mathf.Abs(rangeLimit - definitionRange) > 0.001f ||
                plannedDistance <= 0.1f ||
                plannedDistance > rangeLimit + 0.001f ||
                Mathf.Abs(flightDuration - 1f) > 0.001f ||
                observedDuration <= 0f ||
                observedDuration > flightDuration + 0.001f ||
                maximumY <= start.y + 0.05f ||
                maximumIndex <= 0 ||
                maximumIndex >= samples.Count - 1 ||
                final.y >= maximumY - 0.05f ||
                maximumPlanarDistance > plannedDistance + rangeTolerance ||
                maximumPlanarDistance > rangeLimit + rangeTolerance;
            var evidence =
                "samples=" + samples.Count +
                ";rangeLimit=" + FormatFloat(rangeLimit) +
                ";plannedDistance=" + FormatFloat(plannedDistance) +
                ";maxPlanarDistance=" + FormatFloat(maximumPlanarDistance) +
                ";flightDuration=" + FormatFloat(flightDuration) +
                ";observedDuration=" + FormatFloat(observedDuration) +
                ";start=" + FormatVector(start) +
                ";apexIndex=" + maximumIndex +
                ";apex=" + FormatVector(samples[maximumIndex]) +
                ";final=" + FormatVector(final);
            if (arcIsInvalid)
            {
                throw new InvalidOperationException(
                    "Grenade server samples were not a bounded " +
                    "rising-and-falling 16m arc: " + evidence);
            }

            return evidence;
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string FormatVector(Vector3 value)
        {
            return "(" + FormatFloat(value.x) + "," +
                   FormatFloat(value.y) + "," +
                   FormatFloat(value.z) + ")";
        }

        private void AttachMatchObservation(NetworkMatchState match)
        {
            DetachMatchObservation();
            _observedMatch = match;
            _observedMatch.DevelopmentShotPresented += OnShotPresented;
        }

        private void DetachMatchObservation()
        {
            if (_observedMatch != null)
            {
                _observedMatch.DevelopmentShotPresented -= OnShotPresented;
                _observedMatch = null;
            }
        }

        private void OnShotPresented(
            PrototypeItemId item,
            Vector3 origin,
            Vector3 end,
            bool didHit)
        {
            if (string.IsNullOrWhiteSpace(_shotCaptureLabel))
            {
                return;
            }

            var path = ScreenshotPath(
                _shotCaptureLabel + "-tracer-p" + _playerIndex + ".png");
            _ = CaptureRenderedScreenshotAsync(path);
            Log(
                "tracer_presented",
                "scenario=" + _shotCaptureLabel +
                ";item=" + item +
                ";didHit=" + didHit +
                ";distance=" + Vector3.Distance(origin, end).ToString(
                    "0.00",
                    CultureInfo.InvariantCulture));
        }

        private async Task CaptureAndWaitAsync(string fileName)
        {
            var path = ScreenshotPath(fileName);
            await CaptureRenderedScreenshotAsync(path);
        }

        private async Task WaitForScreenshotAsync(
            string path,
            double timeoutSeconds)
        {
            await WaitUntilAsync(
                () =>
                {
                    try
                    {
                        return _screenshotEvidence.ContainsKey(path) &&
                               File.Exists(path) &&
                               new FileInfo(path).Length > 0L;
                    }
                    catch (IOException)
                    {
                        return false;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return false;
                    }
                },
                timeoutSeconds,
                "rendered screenshot " + Path.GetFileName(path));
        }

        private Task CaptureRenderedScreenshotAsync(string path)
        {
            TryDelete(path);
            var completion = new TaskCompletionSource<bool>();
            var task = completion.Task;
            _pendingScreenshotCaptures.Add(task);
            StartCoroutine(
                CaptureRenderedScreenshotAtEndOfFrame(path, completion));
            return task;
        }

        private IEnumerator CaptureRenderedScreenshotAtEndOfFrame(
            string path,
            TaskCompletionSource<bool> completion)
        {
            // Capturing the composed game view after rendering preserves
            // Screen Space Overlay UI as well as the short-lived tracer.
            yield return new WaitForEndOfFrame();

            Texture2D texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture == null)
                {
                    throw new InvalidOperationException(
                        "Unity returned no rendered screenshot texture for " +
                        Path.GetFileName(path) + ".");
                }

                var pixels = texture.GetPixels32();
                var evidence = InspectRenderedScreenshot(
                    texture.width,
                    texture.height,
                    pixels,
                    path);
                var png = texture.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Unity encoded an empty screenshot for " +
                        Path.GetFileName(path) + ".");
                }

                File.WriteAllBytes(path, png);
                _screenshotEvidence[path] = evidence;
                RegisterScreenshot(path);
                Log(
                    "rendered_screenshot_captured",
                    "file=" + Path.GetFileName(path) +
                    ";size=" + evidence.Width + "x" + evidence.Height +
                    ";nonBlackPixels=" + evidence.NonBlackPixels +
                    ";luminanceRange=" + evidence.LuminanceRange);
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
        }

        private static ScreenshotEvidence InspectRenderedScreenshot(
            int width,
            int height,
            IReadOnlyList<Color32> pixels,
            string path)
        {
            if (width <= 0 || height <= 0 || pixels == null ||
                pixels.Count != width * height)
            {
                throw new InvalidOperationException(
                    "Screenshot dimensions/pixels were invalid for " +
                    Path.GetFileName(path) + ".");
            }

            var nonBlackPixels = 0;
            var minimumLuminance = 255;
            var maximumLuminance = 0;
            for (var index = 0; index < pixels.Count; index++)
            {
                var pixel = pixels[index];
                var brightest = Math.Max(
                    pixel.r,
                    Math.Max(pixel.g, pixel.b));
                if (brightest > 8)
                {
                    nonBlackPixels++;
                }

                var luminance =
                    (54 * pixel.r + 183 * pixel.g + 19 * pixel.b) >> 8;
                minimumLuminance = Math.Min(
                    minimumLuminance,
                    luminance);
                maximumLuminance = Math.Max(
                    maximumLuminance,
                    luminance);
            }

            var luminanceRange = maximumLuminance - minimumLuminance;
            var requiredNonBlackPixels = Math.Max(256, pixels.Count / 1000);
            if (nonBlackPixels < requiredNonBlackPixels ||
                luminanceRange < 8)
            {
                throw new InvalidOperationException(
                    "Screenshot did not contain a rendered game view: " +
                    Path.GetFileName(path) +
                    "; nonBlackPixels=" + nonBlackPixels +
                    "; required=" + requiredNonBlackPixels +
                    "; luminanceRange=" + luminanceRange + ".");
            }

            return new ScreenshotEvidence(
                width,
                height,
                nonBlackPixels,
                luminanceRange);
        }

        private async Task WaitForPendingScreenshotCapturesAsync()
        {
            if (_pendingScreenshotCaptures.Count == 0)
            {
                throw new InvalidOperationException(
                    "Item QA completed without capturing screenshots.");
            }

            await Task.WhenAll(_pendingScreenshotCaptures.ToArray());
        }

        private void RegisterScreenshot(string path)
        {
            if (!_screenshots.Contains(path))
            {
                _screenshots.Add(path);
            }
        }

        private void WriteScreenshotManifest()
        {
            var builder = new StringBuilder();
            builder.Append("{\"player\":");
            builder.Append(_playerIndex);
            builder.Append(",\"slot\":");
            builder.Append(_assignedSlot);
            builder.Append(",\"screenshots\":[");
            for (var index = 0; index < _screenshots.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var path = _screenshots[index];
                if (!_screenshotEvidence.TryGetValue(
                        path,
                        out var evidence))
                {
                    throw new InvalidOperationException(
                        "Screenshot evidence is missing for " +
                        Path.GetFileName(path) + ".");
                }

                builder.Append("{\"file\":\"");
                builder.Append(EscapeJson(Path.GetFileName(path)));
                builder.Append("\",\"bytes\":");
                builder.Append(
                    File.Exists(path) ? new FileInfo(path).Length : 0L);
                builder.Append(",\"width\":");
                builder.Append(evidence.Width);
                builder.Append(",\"height\":");
                builder.Append(evidence.Height);
                builder.Append(",\"nonBlackPixels\":");
                builder.Append(evidence.NonBlackPixels);
                builder.Append(",\"luminanceRange\":");
                builder.Append(evidence.LuminanceRange);
                builder.Append('}');
            }

            builder.Append("]}");
            WriteAtomic(
                Path.Combine(
                    _artifactDirectory,
                    "manifest-p" + _playerIndex + ".json"),
                builder.ToString());
        }

        private string BuildSummary()
        {
            return
                "{" +
                "\"map\":\"" + ForestMapId + "\"," +
                "\"mapContentVersion\":" + ForestContentVersion + "," +
                "\"players\":4," +
                "\"pistolHitscanAndReticle\":true," +
                "\"sniperHitscanNoZoom\":true," +
                "\"tracerAllProcesses\":true," +
                "\"firearmProjectileCount\":0," +
                "\"grenadeOwnerOnlyRange\":true," +
                "\"grenadeRangeMeters\":16," +
                "\"grenadeRejectedRequestRecovered\":true," +
                "\"grenadeAcceptedArc\":true," +
                "\"grenadeFlightBoundarySeconds\":1," +
                "\"grenadeServerTrajectoryVerified\":true," +
                "\"grenadeTrajectoryPreview\":false," +
                "\"mineOwnerOnlyWorldAndMaps\":true," +
                "\"mineTriggerCleanup\":true," +
                "\"renderedScreenshotPixelsVerified\":true," +
                "\"realRelay\":true," +
                "\"controlledTransportSimulation\":false" +
                "}";
        }

        private void ParseArguments()
        {
            var arguments = Environment.GetCommandLineArgs();
            _role = GetArgument(arguments, "-e2e-role");
            if (!string.Equals(
                    _role,
                    HostRole,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    _role,
                    ClientRole,
                    StringComparison.OrdinalIgnoreCase))
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
            var artifactArgument = GetArgument(
                arguments,
                "-e2e-artifact-dir");
            _artifactDirectory = string.IsNullOrWhiteSpace(artifactArgument)
                ? Path.Combine(_runDirectory, "screenshots")
                : Path.GetFullPath(artifactArgument);

            if (IsHost && _playerIndex != 0)
            {
                throw new ArgumentException(
                    "The item QA host must use -e2e-player 0.");
            }

            if (!IsHost && _playerIndex == 0)
            {
                throw new ArgumentException(
                    "An item QA client must use player 1, 2, or 3.");
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

        private static int FindLocalItemSlot(
            NetworkPlayerAvatar avatar,
            PrototypeItemId item)
        {
            for (var slot = 0; slot < GameplayInventory.Capacity; slot++)
            {
                if (avatar.GetLocalItemId(slot) == item)
                {
                    return slot;
                }
            }

            return -1;
        }

        private static bool AllChoicesResolved(NetworkMatchState match)
        {
            for (var slot = 0;
                 slot < MultiplayerConstants.MaxPlayers;
                 slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar == null || !avatar.HasResolvedItemChoice)
                {
                    return false;
                }
            }

            return true;
        }

        private static NetworkPlayerAvatar[] RequireFourAvatars(
            NetworkMatchState match)
        {
            var avatars = new NetworkPlayerAvatar[
                MultiplayerConstants.MaxPlayers];
            for (var slot = 0; slot < avatars.Length; slot++)
            {
                avatars[slot] = match.GetAvatarForSlot(slot);
                if (avatars[slot] == null || !avatars[slot].IsSpawned)
                {
                    throw new InvalidOperationException(
                        "Item QA avatar slot " + slot +
                        " was unavailable.");
                }
            }

            return avatars;
        }

        private static NetworkMatchState RequireMatch()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsSpawned)
            {
                throw new InvalidOperationException(
                    "Item QA requires a spawned NetworkMatchState.");
            }

            return match;
        }

        private static NetworkPlayerAvatar RequireLocalAvatar(
            OnlineSessionController controller)
        {
            var avatar = controller != null
                ? controller.GetLocalAvatar()
                : null;
            if (avatar == null || !avatar.IsSpawned || !avatar.IsOwner)
            {
                throw new InvalidOperationException(
                    "Item QA local owner avatar was unavailable.");
            }

            return avatar;
        }

        private static Camera RequireMainCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                throw new InvalidOperationException(
                    "Item QA main camera was unavailable.");
            }

            return camera;
        }

        private static bool Approximately(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) <= ColorTolerance &&
                   Mathf.Abs(left.g - right.g) <= ColorTolerance &&
                   Mathf.Abs(left.b - right.b) <= ColorTolerance &&
                   Mathf.Abs(left.a - right.a) <= ColorTolerance;
        }

        private static bool IsForest(BoardMapSelection selection)
        {
            return string.Equals(
                       selection.MapId,
                       ForestMapId,
                       StringComparison.Ordinal) &&
                   selection.ContentVersion == ForestContentVersion;
        }

        private static void ValidateForest(BoardMapSelection selection)
        {
            if (!IsForest(selection))
            {
                throw new InvalidOperationException(
                    "Expected Forest " + ForestMapId + " v" +
                    ForestContentVersion + ", observed " +
                    selection.MapId + " v" +
                    selection.ContentVersion + ".");
            }
        }

        private static void QuitProcess(int exitCode)
        {
            Environment.ExitCode = exitCode;
#if UNITY_STANDALONE_WIN
            using (var process =
                   System.Diagnostics.Process.GetCurrentProcess())
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

        private async Task AllowReplicationAsync()
        {
            Physics.SyncTransforms();
            await Task.Delay(450);
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
                timeoutSeconds.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture) +
                " seconds waiting for " + description + ".");
        }

        private async Task WaitForSignalAsync(
            string name,
            double timeoutSeconds)
        {
            await WaitUntilAsync(
                () => File.Exists(SignalPath(name)),
                timeoutSeconds,
                "signal " + name);
        }

        private async Task WaitForSlotMarkerAsync(
            string name,
            int slot,
            double timeoutSeconds)
        {
            await WaitUntilAsync(
                () => File.Exists(SlotMarkerPath(name, slot)),
                timeoutSeconds,
                "marker " + name + " for slot " + slot);
        }

        private async Task WaitForAllMarkersAsync(
            string name,
            double timeoutSeconds)
        {
            await WaitUntilAsync(
                () => AllMarkersPresent(name),
                timeoutSeconds,
                "all markers for " + name);
        }

        private bool AllMarkersPresent(string name)
        {
            for (var index = 0;
                 index < MultiplayerConstants.MaxPlayers;
                 index++)
            {
                if (!File.Exists(MarkerPath(name, index)))
                {
                    return false;
                }
            }

            return true;
        }

        private bool AllPassMarkersPresent()
        {
            for (var index = 0;
                 index < MultiplayerConstants.MaxPlayers;
                 index++)
            {
                if (!File.Exists(PassedPath(index)))
                {
                    return false;
                }
            }

            return true;
        }

        private void Signal(string name)
        {
            WriteAtomic(SignalPath(name), "GO");
            Log("signal", name);
        }

        private void Mark(string name)
        {
            WriteAtomic(MarkerPath(name, _playerIndex), "OK");
        }

        private void MarkSlot(string name)
        {
            WriteAtomic(SlotMarkerPath(name, _assignedSlot), "OK");
        }

        private string SignalPath(string name)
        {
            return Path.Combine(_runDirectory, "signal-" + name + ".marker");
        }

        private string MarkerPath(string name, int playerIndex)
        {
            return Path.Combine(
                _runDirectory,
                name + "-" + playerIndex + ".marker");
        }

        private string SlotMarkerPath(string name, int slot)
        {
            return Path.Combine(
                _runDirectory,
                name + "-slot-" + slot + ".marker");
        }

        private string PassedPath(int playerIndex)
        {
            return Path.Combine(
                _runDirectory,
                "item-passed-" + playerIndex + ".marker");
        }

        private string ScreenshotPath(string fileName)
        {
            return Path.Combine(_artifactDirectory, fileName);
        }

        private bool IsHost => string.Equals(
            _role,
            HostRole,
            StringComparison.OrdinalIgnoreCase);

        private string JoinCodePath =>
            Path.Combine(_runDirectory, "item-join-code.txt");

        private string QuitSignalPath =>
            Path.Combine(_runDirectory, "item-quit.signal");

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
                ",\"role\":\"" + EscapeJson(_role ?? string.Empty) +
                "\",\"event\":\"" + EscapeJson(eventName) +
                "\",\"details\":\"" + EscapeJson(details ?? string.Empty) +
                "\"}" + Environment.NewLine;
            lock (_fileGate)
            {
                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }

            Debug.Log(
                "[ItemQaE2E P" + _playerIndex + "] " +
                eventName +
                (string.IsNullOrWhiteSpace(details)
                    ? string.Empty
                    : " | " + details));
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
                        "item-player-" + _playerIndex + ".jsonl");
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
                            "item-failed-" + _playerIndex + ".marker"),
                        exception.ToString());
                }
            }
            catch (Exception loggingException)
            {
                Debug.LogException(loggingException);
            }
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
            for (var index = 0; index < arguments.Count - 1; index++)
            {
                if (string.Equals(
                        arguments[index],
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
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
            File.WriteAllText(
                temporary,
                contents ?? string.Empty,
                Encoding.UTF8);
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
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Another process can briefly hold a handoff or screenshot.
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
