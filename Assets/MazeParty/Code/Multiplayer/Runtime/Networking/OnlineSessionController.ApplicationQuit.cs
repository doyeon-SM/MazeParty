using System.Threading.Tasks;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
        private const int ApplicationQuitCleanupTimeoutMilliseconds = 5000;
        private const int ApplicationQuitNativeWatchdogMilliseconds = 3000;
        private const int ApplicationQuitUnpreparedWatchdogMilliseconds =
            ApplicationQuitCleanupTimeoutMilliseconds +
            ApplicationQuitNativeWatchdogMilliseconds;

        private readonly ApplicationQuitCoordinator _applicationQuitCoordinator =
            new ApplicationQuitCoordinator();
        private bool _allowApplicationQuit;

        private bool OnApplicationWantsToQuit()
        {
            if (_allowApplicationQuit)
            {
                return true;
            }

            BeginApplicationQuitPreparation();
            return false;
        }

        private void BeginApplicationQuitPreparation()
        {
            _applicationQuitting = true;
            ClearPlayingReconnectTicket();

            if (!_applicationQuitCoordinator.TryBegin(
                    PrepareApplicationQuitAsync,
                    Task.Delay(ApplicationQuitCleanupTimeoutMilliseconds)))
            {
                return;
            }

            _ = FinishApplicationQuitAsync(
                _applicationQuitCoordinator.PreparationTask);
        }

        private async Task PrepareApplicationQuitAsync()
        {
            // Let an in-flight create/join/save settle while the PlayerLoop is
            // still alive, then close whichever session it produced exactly once.
            var activeOperation = _sessionOperations.ActiveOperation;
            if (activeOperation != null && !activeOperation.IsCompleted)
            {
                await activeOperation;
            }

            if (_sessions != null && _sessions.IsInSession)
            {
                await LeaveSessionAsync();
            }
        }

        private async Task FinishApplicationQuitAsync(
            Task<ApplicationQuitPreparationResult> preparation)
        {
            var result = await preparation;
            switch (result)
            {
                case ApplicationQuitPreparationResult.TimedOut:
                    Debug.LogWarning(
                        "Application quit cleanup reached its five-second deadline; " +
                        "continuing with the protected shutdown path.");
                    break;
                case ApplicationQuitPreparationResult.Failed:
                    Debug.LogWarning(
                        "Application quit cleanup failed; continuing with the " +
                        "protected shutdown path. " +
                        _applicationQuitCoordinator.Failure?.Message);
                    break;
            }

            Debug.Log("Application quit preparation finished: " + result + ".");

            // A no-session cleanup can complete synchronously inside the
            // wantsToQuit callback. Resume on the next PlayerLoop turn so the
            // first callback has returned false before requesting quit again.
            await Task.Yield();

            // Unity 6000.6 can remain in native Windows teardown after all
            // managed callbacks have returned. The watchdog only fires if the
            // normal process exit has not completed within this final grace.
            ApplicationExitWatchdog.Arm(
                ApplicationQuitNativeWatchdogMilliseconds,
                0);
            _allowApplicationQuit = true;
            Application.Quit(0);
        }
    }
}
