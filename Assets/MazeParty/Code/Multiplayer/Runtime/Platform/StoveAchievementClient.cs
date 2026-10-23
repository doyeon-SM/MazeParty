#if MAZEPARTY_STOVE
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static Stove.PCSDK.V3.GameSupport;

namespace MazeParty.Multiplayer
{
    internal sealed class StoveAchievementClient : IDisposable
    {
        private readonly StoveSdkClient _sdk;
        private readonly ulong _user;
        private readonly AchievementJournal _journal;
        private readonly AchievementUploadQueue _queue = new AchievementUploadQueue();
        private readonly HashSet<OnSetStatCallback> _callbacks = new HashSet<OnSetStatCallback>();
        private bool _disabled, _disposed, _reportedFailure;

        public StoveAchievementClient(StoveSdkClient sdk, ulong user, string scope)
        {
            _sdk = sdk; _user = user;
            _journal = new AchievementJournal(Path.Combine(Application.persistentDataPath,
                "Achievements", scope + ".json"), scope);
        }
        public void Commit(AchievementReceipt receipt, string playerKey)
        {
            if (_disposed || _disabled || _sdk.RefreshUser() != _user) return;
            _journal.Commit(receipt, playerKey);
        }
        public void Tick()
        {
            if (_disposed || _disabled || _callbacks.Count >= 8 || _journal.Pending == 0) return;
            if (!_queue.TryBegin(_journal, Time.realtimeSinceStartupAsDouble, out var number, out var value, out var ticket)) return;
            try
            {
                if (_sdk.RefreshUser() != _user) { _disabled = true; return; }
                OnSetStatCallback callback = null;
                callback = (result, outcome) =>
                {
                    _callbacks.Remove(callback);
                    if (_disposed || _disabled) return;
                    try
                    {
                        if (_sdk.RefreshUser() != _user) { _disabled = true; return; }
                        var confirmed = _queue.Complete(ticket, result.Result.IsSuccessful, outcome.CurrentValue);
                        if (confirmed != 0) _journal.Confirm(confirmed, value);
                        else ReportFailure();
                    }
                    catch (Exception) { ReportFailure(); }
                };
                // Retain even timed-out delegates until callback or SDK shutdown.
                _callbacks.Add(callback);
                Stove_SetStat(new IStoveSetStatParam { StatsId = PartyAchievements.StatId(number), StatValue = value }, callback);
            }
            catch (Exception) { _queue.Complete(ticket, false, 0); ReportFailure(); }
        }
        private void ReportFailure()
        {
            if (_reportedFailure) return;
            _reportedFailure = true;
            Debug.LogWarning("STOVE achievement sync is pending. Check Studio stat registration and network access; saved achievements will retry.");
        }
        public void Dispose() { _disposed = true; }
    }
}
#endif
