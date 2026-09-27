using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Defines the small shared waiting room used before the board scene loads.
    /// Movement remains server authoritative; this component only supplies stable
    /// spawn positions and horizontal room bounds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyArena : MonoBehaviour
    {
        [SerializeField] private LobbyArenaBindings bindings;
        private bool _missingBindingsReported;

        public static LobbyArena Instance { get; private set; }

        public LobbyArenaBindings Bindings => bindings;
        public bool HasRequiredReferences =>
            bindings != null && bindings.HasRequiredReferences;
        public Vector3 RoomCenter => HasRequiredReferences
            ? bindings.RoomCenter
            : transform.position;
        public Vector2 InnerSize => HasRequiredReferences
            ? bindings.InnerSize
            : Vector2.zero;
        public int SpawnCount => HasRequiredReferences
            ? bindings.SpawnCount
            : 0;

        public static LobbyArena EnsureRuntimeCreated(Camera lobbyCamera)
        {
            var arena = Instance;
            if (arena == null)
            {
                arena = FindAnyObjectByType<LobbyArena>(FindObjectsInactive.Include);
                if (arena == null)
                {
                    Debug.LogError(
                        "The authored LobbyArena scene instance is missing. " +
                        "Run MazeParty/Multiplayer/Install Player And Lobby " +
                        "Prefabs; runtime room geometry will not be generated.");
                    return null;
                }
            }

            if (!arena.HasRequiredReferences)
            {
                arena.ReportMissingBindings();
                if (Instance == arena)
                {
                    Instance = null;
                }
                return null;
            }

            // The parameter remains for source compatibility. Camera composition
            // belongs to the authored lobby scene and is never changed here.
            _ = lobbyCamera;
            Instance = arena;
            return arena;
        }

        public void ConfigureBindings(LobbyArenaBindings value)
        {
            bindings = value;
            _missingBindingsReported = false;
        }

        public Vector3 GetSpawnPosition(int slot)
        {
            if (HasRequiredReferences)
            {
                return bindings.GetSpawnPosition(slot);
            }
            ReportMissingBindings();
            return transform.position;
        }

        public Vector3 ClampHorizontalPosition(Vector3 position, float radius)
        {
            if (HasRequiredReferences)
            {
                return bindings.ClampHorizontalPosition(position, radius);
            }
            ReportMissingBindings();
            return position;
        }

        private void ReportMissingBindings()
        {
            if (_missingBindingsReported)
            {
                return;
            }

            _missingBindingsReported = true;
            Debug.LogError(
                "LobbyArena bindings are missing or incomplete. Run " +
                "MazeParty/Multiplayer/Install Player And Lobby Prefabs; " +
                "runtime room geometry will not be generated.",
                this);
        }

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("Only one LobbyArena may be active at a time.", this);
                enabled = false;
                return;
            }

            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
