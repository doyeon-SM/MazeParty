using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private MatchStatistics _statistics;
        private readonly NetworkVariable<uint> _statisticsRevision = new NetworkVariable<uint>();
        private string _finalStatisticsJson;
        private readonly HashSet<ulong> _statisticsSending = new HashSet<ulong>();
        private readonly Dictionary<ulong, double> _statisticsLastRequest = new Dictionary<ulong, double>();
        private readonly StringBuilder _statisticsIncoming = new StringBuilder();
        private int _statisticsExpectedChunk, _statisticsExpectedCount;
        public MatchStatistics CeremonyStatistics { get; private set; }

        private void InitializeStatisticsReplication()
        {
            ClearReceivedStatistics();
            _statisticsRevision.OnValueChanged += StatisticsRevisionChanged;
        }
        private void ReleaseStatisticsReplication()
        {
            _statisticsRevision.OnValueChanged -= StatisticsRevisionChanged;
            ClearReceivedStatistics();
            _statisticsSending.Clear(); _statisticsLastRequest.Clear();
            _statistics = null; _finalStatisticsJson = null;
        }
        private void StatisticsRevisionChanged(uint previous, uint current) => ClearReceivedStatistics();
        private void ClearReceivedStatistics()
        {
            CeremonyStatistics = null;
            _statisticsIncoming.Clear(); _statisticsExpectedChunk = _statisticsExpectedCount = 0;
        }

        private void BeginStatisticsOnServer()
        {
            _statistics = MatchStatistics.Begin(GameSettings.Turns);
            _statisticsRevision.Value++;
            _finalStatisticsJson = null;
            CeremonyStatistics = null;
            _statisticsLastRequest.Clear();
            for (var slot = 0; slot < 4; slot++) RecordStatisticsBalanceOnServer(GetAvatarForSlot(slot), 0);
        }

        public void RecordStatisticsBalanceOnServer(NetworkPlayerAvatar avatar, int turn = -1)
        {
            if (!CanRecordStatistics(avatar)) return;
            _statistics.RecordBalance(avatar.AssignedSlot, turn < 0 ? Turn : turn, avatar.KeyCount, avatar.Gold,
                avatar.CreateMatchAwardStatsOnServer().TotalGoldEarned);
        }

        public void RecordStatisticsPlacementOnServer(NetworkPlayerAvatar avatar, int rank)
        {
            if (CanRecordStatistics(avatar)) _statistics.RecordPlacement(avatar.AssignedSlot, Turn, rank);
        }

        public void RecordStatisticsMoveOnServer(NetworkPlayerAvatar avatar)
        {
            if (CanRecordStatistics(avatar) && _statistics.players[avatar.AssignedSlot].moves < int.MaxValue)
                _statistics.players[avatar.AssignedSlot].moves++;
        }

        public void RecordStatisticsDamageOnServer(NetworkPlayerAvatar victim, NetworkPlayerAvatar source, int amount, int item)
        {
            if (!CanRecordStatistics(victim)) return;
            var sourceSlot = source != null && GetAvatarForSlot(source.AssignedSlot) == source ? source.AssignedSlot : -1;
            _statistics.RecordDamage(sourceSlot, victim.AssignedSlot, Turn, item, amount);
        }

        private bool CanRecordStatistics(NetworkPlayerAvatar avatar) => IsServer && GameplayEnabled &&
            _activeMatchVoidGate.AllowsResultMutation && _statistics != null && !_statistics.frozen && avatar != null &&
            avatar.AssignedSlot >= 0 && avatar.AssignedSlot < 4 && GetAvatarForSlot(avatar.AssignedSlot) == avatar;

        private void FinalizeStatisticsOnServer()
        {
            if (!IsServer || _statistics == null || _statistics.frozen) return;
            for (var slot = 0; slot < 4; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                if (avatar == null) return;
                var awards = avatar.CreateMatchAwardStatsOnServer();
                var p = _statistics.players[slot];
                p.name = avatar.DisplayName; p.color = avatar.Appearance.BodyColor;
                p.eye = avatar.Appearance.EyeId; p.mouth = avatar.Appearance.MouthId;
                p.hat = avatar.Appearance.HatId; p.expression = avatar.Appearance.ExpressionId;
                p.rank = GetFinalCeremonyRank(slot); p.keys = avatar.KeyCount; p.gold = avatar.Gold;
                p.peakGold = awards.PeakGoldHeld; p.earnedGold = awards.TotalGoldEarned;
                p.wins = awards.MinigameWins; p.lastPlaces = awards.MinigameLastPlaces;
                p.itemUses = awards.ItemUses; p.received = awards.DamageTaken; p.dealt = awards.PlayerDamageDealt;
                for (var award = 0; award < 2; award++)
                {
                    _statistics.bonusCategories[award] = (int)GetCeremonyAwardCategory(award);
                    if ((GetCeremonyAwardWinnerMask(award) & (1 << slot)) != 0) p.bonusMask |= 1 << award;
                }
            }
            _statistics.frozen = true;
            _finalStatisticsJson = JsonUtility.ToJson(_statistics);
            CeremonyStatistics = _statistics;
        }

        public void RequestCeremonyStatistics()
        {
            if (IsSpawned && IsFinalRankingLocked && CeremonyStatistics == null) RequestCeremonyStatisticsRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestCeremonyStatisticsRpc(RpcParams rpcParams = default)
        {
            var client = rpcParams.Receive.SenderClientId;
            if (!IsServer || !IsFinalRankingLocked || _finalStatisticsJson == null || _statisticsSending.Contains(client)) return;
            var authorized = false;
            for (var slot = 0; slot < 4; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                if (avatar != null && avatar.OwnerClientId == client && !IsBackInWaitingRoomDuringCeremony(slot)) authorized = true;
            }
            if (!authorized || (_statisticsLastRequest.TryGetValue(client, out var last) && Time.unscaledTimeAsDouble - last < 3d)) return;
            _statisticsLastRequest[client] = Time.unscaledTimeAsDouble;
            StartCoroutine(SendStatistics(client, _statisticsRevision.Value, _finalStatisticsJson));
        }

        private IEnumerator SendStatistics(ulong client, uint revision, string json)
        {
            const int chunkSize = 6000;
            _statisticsSending.Add(client);
            try
            {
                var count = (json.Length + chunkSize - 1) / chunkSize;
                for (var index = 0; index < count; index++)
                {
                    if (!IsSpawned || revision != _statisticsRevision.Value || !IsAwardCeremonyActive || !NetworkManager.ConnectedClients.ContainsKey(client)) yield break;
                    ReceiveCeremonyStatisticsRpc(revision, index, count, json.Substring(index * chunkSize, Math.Min(chunkSize, json.Length - index * chunkSize)),
                        RpcTarget.Single(client, RpcTargetUse.Temp));
                    yield return null;
                }
            }
            finally { _statisticsSending.Remove(client); }
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server, Delivery = RpcDelivery.Reliable)]
        private void ReceiveCeremonyStatisticsRpc(uint revision, int index, int count, string chunk, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != Unity.Netcode.NetworkManager.ServerClientId || revision != _statisticsRevision.Value || !IsFinalRankingLocked ||
                count < 1 || count > 1400 || index < 0 || index >= count || chunk == null || chunk.Length > 6000) return;
            if (index == 0) { _statisticsIncoming.Clear(); _statisticsExpectedChunk = 0; _statisticsExpectedCount = count; }
            if (index != _statisticsExpectedChunk || count != _statisticsExpectedCount ||
                _statisticsIncoming.Length + chunk.Length > MatchStatistics.MaxJsonLength) return;
            _statisticsIncoming.Append(chunk); _statisticsExpectedChunk++;
            if (_statisticsExpectedChunk == count)
            {
                if (MatchStatistics.TryDecode(_statisticsIncoming.ToString(), out var result) && result.frozen)
                    CeremonyStatistics = result;
                _statisticsIncoming.Clear();
            }
        }

        private void RestoreStatistics(MatchRecoverySnapshot snapshot, MatchRecoveryPlayerSnapshot[] currentPlayers)
        {
            _statisticsRevision.Value++;
            _finalStatisticsJson = null; CeremonyStatistics = null;
            if (MatchStatistics.TryDecode(snapshot.statistics, out var saved))
            {
                var mapping = new int[4];
                for (var old = 0; old < 4; old++)
                {
                    mapping[old] = -1;
                    for (var current = 0; current < 4; current++)
                        if (snapshot.players[old].playerKey == currentPlayers[current].playerKey) mapping[old] = current;
                }
                saved.RemapSlots(mapping);
                _statistics = saved;
            }
            else
            {
                _statistics = MatchStatistics.Begin(snapshot.scheduleTurnCount);
                _statistics.historyIncomplete = true;
                for (var slot = 0; slot < 4; slot++)
                {
                    var p = currentPlayers[slot];
                    _statistics.RecordBalance(slot, snapshot.turn, p.keyCount, p.gold, p.totalGoldEarned);
                    _statistics.placements[slot * 4] = p.minigameWins;
                    _statistics.placements[slot * 4 + 3] = p.minigameLastPlaces;
                    _statistics.lastPlacementTurn[slot] = snapshot.settledMinigameTurn < 0 ? 0 : snapshot.settledMinigameTurn;
                }
            }
        }
    }
}
