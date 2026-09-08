using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    public sealed class NetworkPlayerAvatar : NetworkBehaviour
    {
        private static readonly Color[] PlayerColors =
        {
            new Color(0.95f, 0.25f, 0.25f),
            new Color(0.25f, 0.55f, 1f),
            new Color(0.25f, 0.85f, 0.4f),
            new Color(1f, 0.75f, 0.2f)
        };

        [SerializeField] private float moveSpeed = 5f;

        private readonly NetworkVariable<int> _slot = new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // Exact dice values are server-written and replicated only to this avatar's owner.
        private readonly NetworkVariable<int> _privateRoll = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        private CharacterController _characterController;
        private Vector2 _serverInput;
        private Vector2 _lastSentInput;
        private float _nextInputRefresh;

        public int AssignedSlot => _slot.Value;
        public int LocalVisibleRoll => IsOwner ? _privateRoll.Value : 0;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            _slot.OnValueChanged += OnSlotChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplySlotVisual(_slot.Value);

            if (IsServer)
            {
                AssignFirstAvailableSlotOnServer();
            }
        }

        public override void OnNetworkDespawn()
        {
            _slot.OnValueChanged -= OnSlotChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }

            var input = Vector2.zero;
            var keyboard = Keyboard.current;
            if (keyboard != null && IsBoardLoaded() && NetworkMatchState.IsGameplayReady)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentInput || Time.unscaledTime >= _nextInputRefresh)
            {
                _lastSentInput = input;
                _nextInputRefresh = Time.unscaledTime + 0.1f;
                SubmitMovementRpc(input);
            }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned ||
                !IsServer ||
                _slot.Value < 0 ||
                !IsBoardLoaded() ||
                !NetworkMatchState.IsGameplayReady)
            {
                return;
            }

            var movement = new Vector3(_serverInput.x, 0f, _serverInput.y);
            _characterController.Move(movement * (moveSpeed * Time.fixedDeltaTime));

            var position = transform.position;
            position.x = Mathf.Clamp(position.x, -9f, 9f);
            position.y = 1f;
            position.z = Mathf.Clamp(position.z, -9f, 9f);
            transform.position = position;

            if (movement.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(movement, Vector3.up);
            }
        }

        private void AssignFirstAvailableSlotOnServer()
        {
            if (!IsServer || _slot.Value >= 0)
            {
                return;
            }

            var avatars = FindObjectsByType<NetworkPlayerAvatar>();
            for (var candidate = 0; candidate < MultiplayerConstants.MaxPlayers; candidate++)
            {
                var occupied = false;
                foreach (var avatar in avatars)
                {
                    if (avatar != this && avatar.IsSpawned && avatar._slot.Value == candidate)
                    {
                        occupied = true;
                        break;
                    }
                }

                if (occupied)
                {
                    continue;
                }

                // The NGO server owns gameplay-seat assignment; no client-supplied slot
                // or identity is trusted. MPS seat properties remain lobby metadata.
                // TODO(STEAM-SESSION): bind an authenticated Steam lobby member to this
                // server assignment when a Steam transport/session backend is introduced.
                _slot.Value = candidate;
                MoveToSpawnPoint(candidate);
                return;
            }

            Debug.LogError("No slot is available for the connected player (4-player limit).");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitMovementRpc(Vector2 input, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            if (!NetworkMatchState.IsGameplayReady)
            {
                _serverInput = Vector2.zero;
                return;
            }

            if (float.IsNaN(input.x) ||
                float.IsInfinity(input.x) ||
                float.IsNaN(input.y) ||
                float.IsInfinity(input.y))
            {
                _serverInput = Vector2.zero;
                return;
            }

            _serverInput = Vector2.ClampMagnitude(input, 1f);
        }

        private void OnSlotChanged(int _, int current)
        {
            ApplySlotVisual(current);
            if (IsServer && IsBoardLoaded())
            {
                MoveToSpawnPoint(current);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == MultiplayerConstants.BoardScene && IsServer && _slot.Value >= 0)
            {
                MoveToSpawnPoint(_slot.Value);
            }
        }

        private void MoveToSpawnPoint(int slot)
        {
            if (slot < 0 || slot >= MultiplayerConstants.MaxPlayers)
            {
                return;
            }

            var spawnPoints = new[]
            {
                new Vector3(-6f, 1f, -6f),
                new Vector3(6f, 1f, -6f),
                new Vector3(-6f, 1f, 6f),
                new Vector3(6f, 1f, 6f)
            };

            _characterController.enabled = false;
            transform.SetPositionAndRotation(spawnPoints[slot], Quaternion.identity);
            _characterController.enabled = true;
        }

        private void ApplySlotVisual(int slot)
        {
            gameObject.name = slot >= 0 ? "NetworkPlayer_" + (slot + 1) : "NetworkPlayer_Unassigned";

            var meshRenderer = GetComponentInChildren<MeshRenderer>();
            if (meshRenderer == null || slot < 0 || slot >= PlayerColors.Length)
            {
                return;
            }

            var properties = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", PlayerColors[slot]);
            properties.SetColor("_Color", PlayerColors[slot]);
            meshRenderer.SetPropertyBlock(properties);
        }

        private static bool IsBoardLoaded()
        {
            var board = SceneManager.GetSceneByName(MultiplayerConstants.BoardScene);
            return board.IsValid() && board.isLoaded;
        }

        public void SetRollOnServer(int roll)
        {
            if (!IsServer)
            {
                return;
            }

            _privateRoll.Value = Mathf.Clamp(roll, 0, 10);
        }
    }
}
