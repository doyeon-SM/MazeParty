using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkMatchState : NetworkBehaviour
    {
        public static NetworkMatchState Instance { get; private set; }

        private readonly NetworkVariable<bool> _gameplayEnabled =
            new NetworkVariable<bool>(false);
        private readonly NetworkVariable<byte> _rolledMask = new NetworkVariable<byte>(0);
        private readonly NetworkVariable<int> _turn = new NetworkVariable<int>(1);

        public bool GameplayEnabled => _gameplayEnabled.Value;
        public static bool IsGameplayReady =>
            Instance != null && Instance.IsSpawned && Instance.GameplayEnabled;

        public override void OnNetworkSpawn()
        {
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnGUI()
        {
            if (!IsSpawned)
            {
                return;
            }

            GUILayout.BeginArea(
                new Rect(Screen.width - 330f, 20f, 310f, 280f),
                GUI.skin.box);
            GUILayout.Label("<b>Server-Authoritative Dice Test</b>", RichLabelStyle());
            if (!_gameplayEnabled.Value)
            {
                GUILayout.Label("Waiting for all 4 players to load the Board...");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("Turn " + _turn.Value);

            var localAvatar = GetLocalAvatar();
            var localSlot = localAvatar != null ? localAvatar.AssignedSlot : -1;
            var localRoll = localAvatar != null ? localAvatar.LocalVisibleRoll : 0;

            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                string state;
                if (!HasRolled(slot))
                {
                    state = "Waiting";
                }
                else if (slot == localSlot && localRoll > 0)
                {
                    state = localRoll + " · Your result";
                }
                else
                {
                    state = "Rolled · Hidden";
                }

                GUILayout.Label((slot + 1) + "P : " + state);
            }

            var canRoll = localSlot >= 0 &&
                          localSlot < MultiplayerConstants.MaxPlayers &&
                          !HasRolled(localSlot);

            var previousEnabled = GUI.enabled;
            GUI.enabled = canRoll;
            if (GUILayout.Button("Roll Dice", GUILayout.Height(34f)))
            {
                RequestRollRpc();
            }

            GUI.enabled = previousEnabled;
            if (IsHost && AllPlayersRolled() &&
                GUILayout.Button("Next Turn", GUILayout.Height(30f)))
            {
                RequestNextTurnRpc();
            }

            GUILayout.Label("The exact value is visible only to you and the server.");
            GUILayout.EndArea();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestRollRpc(RpcParams rpcParams = default)
        {
            if (!_gameplayEnabled.Value)
            {
                return;
            }

            var playerObject = NetworkManager.SpawnManager.GetPlayerNetworkObject(
                rpcParams.Receive.SenderClientId);
            if (playerObject == null)
            {
                return;
            }

            var avatar = playerObject.GetComponent<NetworkPlayerAvatar>();
            var slot = avatar != null ? avatar.AssignedSlot : -1;
            if (slot < 0 ||
                slot >= MultiplayerConstants.MaxPlayers ||
                HasRolled(slot))
            {
                return;
            }

            avatar.SetRollOnServer(Random.Range(1, 11));
            _rolledMask.Value = (byte)(_rolledMask.Value | (1 << slot));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestNextTurnRpc(RpcParams rpcParams = default)
        {
            if (!_gameplayEnabled.Value ||
                rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId ||
                !AllPlayersRolled())
            {
                return;
            }

            _turn.Value++;
            _rolledMask.Value = 0;

            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                var playerObject = NetworkManager.SpawnManager.GetPlayerNetworkObject(clientId);
                var avatar = playerObject != null
                    ? playerObject.GetComponent<NetworkPlayerAvatar>()
                    : null;
                avatar?.SetRollOnServer(0);
            }
        }

        public void EnableGameplayOnServer()
        {
            if (IsServer)
            {
                _gameplayEnabled.Value = true;
            }
        }

        private bool HasRolled(int slot)
        {
            return slot >= 0 &&
                   slot < MultiplayerConstants.MaxPlayers &&
                   (_rolledMask.Value & (1 << slot)) != 0;
        }

        private bool AllPlayersRolled()
        {
            var allPlayersMask = (1 << MultiplayerConstants.MaxPlayers) - 1;
            return (_rolledMask.Value & allPlayersMask) == allPlayersMask;
        }

        private NetworkPlayerAvatar GetLocalAvatar()
        {
            var playerObject = NetworkManager != null && NetworkManager.SpawnManager != null
                ? NetworkManager.SpawnManager.GetLocalPlayerObject()
                : null;
            return playerObject != null
                ? playerObject.GetComponent<NetworkPlayerAvatar>()
                : null;
        }

        private static GUIStyle RichLabelStyle()
        {
            return new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 16
            };
        }
    }
}

