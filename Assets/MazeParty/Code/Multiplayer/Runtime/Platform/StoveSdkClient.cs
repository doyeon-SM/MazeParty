#if MAZEPARTY_STOVE
using System;
using System.Threading.Tasks;
using UnityEngine;
using static Stove.PCSDK.V3.Base;
using static Stove.PCSDK.V3.Ownership;

namespace MazeParty.Multiplayer
{
    internal sealed class StoveSdkClient : IDisposable
    {
        private Task _initialization;
        private bool _callbacks;
        private bool _initialized;
        private bool _disposed;
        private int _generation;
        private OnRestartAppIfNecessaryCallback _restartCallback;
        private OnOwnershipListCallback _ownershipCallback;
        public ulong UserId { get; private set; }
        public string DisplayName { get; private set; } = "Player";

        public Task InitializeAsync(PlatformLaunchSettings settings) =>
            _initialization ?? (_initialization = InitializeCoreAsync(settings));

        private async Task InitializeCoreAsync(PlatformLaunchSettings settings)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StoveSdkClient));
            settings.Validate("stove");
            var generation = ++_generation;
            var completion = new TaskCompletionSource<bool>();
            _restartCallback = (result, outcome) =>
            {
                if (_disposed || generation != _generation) return;
                if (!result.Result.IsSuccessful)
                {
                    completion.TrySetException(Failure("launcher verification", result.Result.ResultCode));
                    return;
                }
                if (outcome.IsRestartRequired)
                {
                    completion.TrySetException(new InvalidOperationException("Restart MazeParty through the STOVE launcher."));
                    Application.Quit();
                    return;
                }
                var initialized = Stove_Initialize();
                if (!initialized.IsSuccessful)
                {
                    completion.TrySetException(Failure("initialization", initialized.ResultCode));
                    if (initialized.ResultCode == 304) Application.Quit();
                    return;
                }
                _initialized = true;
                completion.TrySetResult(true);
            };
            _callbacks = true;
            Debug.Log("STOVE launcher verification: SDK environment " + settings.StoveSdkEnvironment + ".");
            Stove_RestartAppIfNecessary(new IStoveRestartAppIfNecessaryParam
            {
                Environment = settings.StoveSdkEnvironment,
                GameId = settings.stoveGameId,
                AppKey = settings.stoveAppKey,
                WaitTimeMilliSec = 10000,
                LaunchStoveLauncher = true,
                PlatformName = "Stove"
            }, _restartCallback);
            await WithTimeout(completion.Task);
            RefreshUser();
            await CheckOwnershipAsync(settings.stoveGameId);
        }

        public ulong RefreshUser()
        {
            if (!_initialized || _disposed) throw new InvalidOperationException("STOVE authentication is not initialized.");
            var user = default(IStoveUser);
            var result = Stove_GetUser(ref user);
            if (!result.IsSuccessful || user.UserId == 0) throw Failure("user lookup", result.ResultCode);
            UserId = user.UserId;
            DisplayName = PlayerProfilePreferences.SanitizeDisplayName(user.NickName);
            return UserId;
        }

        private async Task CheckOwnershipAsync(string gameId)
        {
            var generation = _generation;
            var completion = new TaskCompletionSource<bool>();
            _ownershipCallback = (result, ownerships) =>
            {
                if (_disposed || generation != _generation) return;
                if (result.Result.IsSuccessful)
                {
                    for (var index = 0; index < ownerships.Count; index++)
                    {
                        var ownership = ownerships[index];
                        if (ownership.GameId == gameId &&
                            ownership.OwnershipCode == EStoveOwnershipCode.k_EStoveOwnershipCode_Acquire)
                        { completion.TrySetResult(true); return; }
                    }
                }
                completion.TrySetException(new InvalidOperationException("This STOVE account has no verified ownership of this game."));
            };
            Stove_FetchOwnerships(_ownershipCallback);
            await WithTimeout(completion.Task);
        }

        private async Task WithTimeout(Task task)
        {
            if (await Task.WhenAny(task, Task.Delay(20000)) != task)
            {
                ++_generation;
                throw new TimeoutException("STOVE authentication timed out. Restart through the STOVE launcher.");
            }
            await task;
        }

        public void Tick()
        {
            if (_callbacks && !_disposed) Stove_RunCallback();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ++_generation;
            _callbacks = false;
            if (_initialized)
            {
                _initialized = false;
                var result = Stove_Uninitialize();
                if (!result.IsSuccessful) Debug.LogWarning("STOVE shutdown returned code " + result.ResultCode + ".");
            }
            _restartCallback = null;
            _ownershipCallback = null;
        }

        private static Exception Failure(string operation, uint code) =>
            new InvalidOperationException("STOVE " + operation + " failed (code " + code + "). " +
                (code == (uint)EStoveResultCode.k_EStoveResultCode_IpcAesKeyNotReceived
                    ? "The launcher authentication key was not received. Check that the SDK and launcher environments match (LIVE for the standard STOVE client), then restart through the launcher."
                    : "Restart through the launcher."));
    }
}
#endif
