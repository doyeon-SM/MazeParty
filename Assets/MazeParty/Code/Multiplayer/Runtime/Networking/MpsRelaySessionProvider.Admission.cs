using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace MazeParty.Multiplayer
{
    public sealed partial class MpsRelaySessionProvider
    {
        private readonly AdmissionReplayGuard _admissionReplay = new AdmissionReplayGuard();
        private int _pendingAdmissions;
        private double _nextJoinTime;

        private void PrepareConnectionPayload(string target)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) throw new InvalidOperationException("NetworkManager is not ready.");
            if (!PlatformContext.IsStoreBuild)
            {
                manager.NetworkConfig.ConnectionData = BuildVersionCompatibility.CreateConnectionPayload();
                return;
            }
            var identity = RequirePlatformIdentity();
            manager.NetworkConfig.ConnectionData = identity.Proof.Create(PlatformContext.Channel,
                PlatformContext.EnvironmentName, BuildVersionCompatibility.CurrentVersion, target, identity.PlayerId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        private IPlatformIdentityProvider RequirePlatformIdentity()
        {
            if (!(_identity is IPlatformIdentityProvider identity) || !identity.IsSignedIn)
                throw new InvalidOperationException("Sign in through the platform before joining a room.");
            return identity;
        }

        private static bool HasRegisteredAdmissionKey(ISession session, VerifiedAdmission proof) =>
            session.Players.Any(player => proof.MatchesMember(player.Id,
                GetPlayerProperty(player, AdmissionProof.PlayerKeyProperty, "")));

        private async Task RefreshAdmissionKeyBeforeReconnectAsync(string sessionId)
        {
            if (!PlatformContext.IsStoreBuild) return;
            var identity = RequirePlatformIdentity();
            // MPS 2.3.1 sessions use Lobby IDs. ReconnectSessionOptions has no
            // PlayerProperties and starts networking before returning a session.
            // Update only OUR authenticated member key before that handshake;
            // a new process intentionally has a new in-memory private key.
            var lobby = await LobbyService.Instance.UpdatePlayerAsync(sessionId, identity.PlayerId,
                new UpdatePlayerOptions { Data = new Dictionary<string, PlayerDataObject>
                {
                    [AdmissionProof.PlayerKeyProperty] = new PlayerDataObject(
                        PlayerDataObject.VisibilityOptions.Member, identity.Proof.PublicKey)
                }});
            if (lobby == null || lobby.Id != sessionId || lobby.Data == null ||
                !lobby.Data.TryGetValue(PlatformSessionAdmission.Property, out var scope) || scope == null ||
                !PlatformSessionAdmission.HasScope(scope.Value, PlatformContext.Channel, PlatformContext.EnvironmentName) ||
                !lobby.Data.TryGetValue(MultiplayerConstants.BuildVersionProperty, out var version) || version == null ||
                !BuildVersionCompatibility.IsCompatibleVersion(version.Value))
                throw new InvalidOperationException("The saved room belongs to a different platform, environment or build.");
        }

        private void ThrottleJoinAttempt()
        {
            var now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (now < _nextJoinTime) throw new InvalidOperationException("Please wait before trying another room code.");
            _nextJoinTime = now + 2d;
        }

        public async Task<bool> ApproveStoreConnectionAsync(ulong clientId, byte[] payload)
        {
            if (!PlatformContext.IsStoreBuild || payload == null || payload.Length > AdmissionProof.MaximumPayloadBytes ||
                _pendingAdmissions >= 8) return false;
            _pendingAdmissions++;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            try
            {
                // CreateSessionAsync starts the host before it returns the session handle.
                while (_session == null && !_ending && DateTime.UtcNow < deadline) await Task.Delay(50);
                var session = _session;
                if (!OwnsSession(session) || !session.IsHost || session.Host != _originalHostId) return false;
                if (!AdmissionProof.TryVerify(payload, PlatformContext.Channel, PlatformContext.EnvironmentName,
                        BuildVersionCompatibility.CurrentVersion, session.Code, session.Id,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out var proof)) return false;
                await session.RefreshAsync();
                if (!OwnsSession(session) || !session.IsHost || !HasCompatibleBuild(session) ||
                    !HasRegisteredAdmissionKey(session, proof)) return false;
                if ((HasRecoveryRoster(session) || GetSessionPhase(session) != MultiplayerConstants.LobbyPhase) &&
                    !HasReservedSeat(session, proof.PlayerId)) return false;
                await _hostMutationGate.WaitAsync();
                try
                {
                    var manager = NetworkManager.Singleton;
                    if (!OwnsSession(session) || !session.IsHost || manager == null || !manager.IsServer ||
                        !HasRegisteredAdmissionKey(session, proof) ||
                        DateTime.UtcNow >= deadline ||
                        (!manager.PendingClients.ContainsKey(clientId) && !manager.ConnectedClients.ContainsKey(clientId))) return false;
                    await EnsureSeatAssignmentsLockedAsync(session);
                    var slot = FindSeat(session, proof.PlayerId);
                    if (slot < 0) return false;
                    var bindings = GetSessionProperty(session, PlatformSessionAdmission.Property, "");
                    if (PlatformSessionAdmission.TryGetClient(bindings, slot, proof.PlayerId, out var previousClient) && previousClient != clientId &&
                        manager.ConnectedClients.ContainsKey(previousClient)) return false;
                    if (!_admissionReplay.TryUse(proof, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) return false;
                    session.AsHost().SetProperty(PlatformSessionAdmission.Property, new SessionProperty(
                        PlatformSessionAdmission.WithConnection(bindings, slot, clientId, proof.PlayerId), VisibilityPropertyOptions.Member));
                    await SaveHostPropertiesWithRetryAsync(session.AsHost(), session);
                    return OwnsSession(session) && session.IsHost && DateTime.UtcNow < deadline &&
                        HasCompatibleBuild(session) && HasRegisteredAdmissionKey(session, proof) && HasReservedSeat(session, proof.PlayerId);
                }
                finally { _hostMutationGate.Release(); }
            }
            catch (Exception)
            {
                // Never expose payloads, service tokens or upstream error bodies to peers/logs.
                return false;
            }
            finally { _pendingAdmissions--; }
        }

        private async Task PublishVerifiedHostConnectionAsync(ulong clientId)
        {
            var session = RequireSession();
            var manager = NetworkManager.Singleton;
            if (!session.IsHost || session.Host != _identity.PlayerId || manager == null ||
                !manager.IsHost || clientId != NetworkManager.ServerClientId) return;
            await _hostMutationGate.WaitAsync();
            try
            {
                if (!OwnsSession(session) || !session.IsHost) return;
                var slot = FindSeat(session, _identity.PlayerId);
                if (slot < 0) return;
                session.AsHost().SetProperty(PlatformSessionAdmission.Property, new SessionProperty(
                    PlatformSessionAdmission.WithConnection(GetSessionProperty(session, PlatformSessionAdmission.Property, ""),
                        slot, clientId, _identity.PlayerId), VisibilityPropertyOptions.Member));
                await SaveHostPropertiesWithRetryAsync(session.AsHost(), session);
            }
            finally { _hostMutationGate.Release(); }
        }

        private bool TryGetVerifiedSlot(ulong clientId, out int slot)
        {
            slot = -1;
            if (_session == null) return false;
            var owners = Enumerable.Range(0, MultiplayerConstants.MaxPlayers).Select(candidate =>
                GetSessionProperty(_session, MultiplayerConstants.SlotProperty(candidate), "")).ToArray();
            return PlatformSessionAdmission.TryGetSlot(GetSessionProperty(_session, PlatformSessionAdmission.Property, ""),
                owners, clientId, out slot);
        }

        private string VerifiedPlayerForClient(ulong clientId) => TryGetVerifiedSlot(clientId, out var slot)
            ? GetSessionProperty(_session, MultiplayerConstants.SlotProperty(slot), "") : "";

        private static int FindSeat(ISession session, string player)
        {
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                if (GetSessionProperty(session, MultiplayerConstants.SlotProperty(slot), "") == player) return slot;
            return -1;
        }
    }

    public sealed class AdmissionReplayGuard
    {
        private readonly Dictionary<string, long> _used = new Dictionary<string, long>();
        public bool TryUse(VerifiedAdmission proof, long now)
        {
            foreach (var key in _used.Where(pair => pair.Value < now).Select(pair => pair.Key).ToArray()) _used.Remove(key);
            var nonceKey = proof.PlayerId + ":" + proof.Nonce;
            if (_used.ContainsKey(nonceKey) || _used.Count >= 1024) return false;
            _used.Add(nonceKey, now + AdmissionProof.MaximumAgeSeconds + 10);
            return true;
        }
        public void Clear() => _used.Clear();
    }
}
