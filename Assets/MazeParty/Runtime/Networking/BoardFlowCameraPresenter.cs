using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Maps replicated flow state to the local-only Cinemachine presentation.
    /// Camera completion never blocks or advances the authoritative server timeline.
    /// </summary>
    public sealed class BoardFlowCameraPresenter : MonoBehaviour
    {
        [SerializeField] private GameplayCameraDirector cameraDirector;

        private NetworkPlayerAvatar _localAvatar;
        private int _observedRevision = -1;
        private bool _wasReconnectPaused;
        private bool _hasObservedState;

        public void Configure(GameplayCameraDirector director)
        {
            cameraDirector = director;
        }

        private void OnDisable()
        {
            // Player objects persist with the bootstrap scene. Never carry the
            // first-person hidden-body presentation out of the Board scene.
            ApplyLocalBodyVisibility(GameplayMode.BoardTopDown);
        }

        private void Update()
        {
            if (cameraDirector == null)
            {
                cameraDirector = FindAnyObjectByType<GameplayCameraDirector>();
            }

            ResolveLocalAvatar();
            var match = NetworkMatchState.Instance;
            if (cameraDirector == null || match == null || !match.IsSpawned || !match.GameplayEnabled)
            {
                return;
            }

            if (_localAvatar != null)
            {
                cameraDirector.SetLocalPlayerEye(_localAvatar.EyePivot);
            }

            if (match.IsReconnectPaused)
            {
                _wasReconnectPaused = true;
                return;
            }

            var targetMode = match.IsKeyShopRevealActive
                ? GameplayMode.BoardTopDown
                : ModeFor(match.FlowState);
            if (!_hasObservedState || _wasReconnectPaused)
            {
                cameraDirector.SnapTo(targetMode, _localAvatar != null ? _localAvatar.EyePivot : null);
                _observedRevision = match.StateRevision;
                _hasObservedState = true;
                _wasReconnectPaused = false;
                ApplyLocalBodyVisibility(targetMode);
                return;
            }

            if (_observedRevision == match.StateRevision)
            {
                return;
            }

            _observedRevision = match.StateRevision;
            if (cameraDirector.ActiveMode != targetMode)
            {
                cameraDirector.SwitchTo(targetMode);
            }
            ApplyLocalBodyVisibility(targetMode);
        }

        private void ResolveLocalAvatar()
        {
            if (_localAvatar != null && _localAvatar.IsSpawned)
            {
                return;
            }

            var manager = NetworkManager.Singleton;
            var playerObject = manager != null && manager.SpawnManager != null
                ? manager.SpawnManager.GetLocalPlayerObject()
                : null;
            _localAvatar = playerObject != null
                ? playerObject.GetComponent<NetworkPlayerAvatar>()
                : null;
        }

        private void ApplyLocalBodyVisibility(GameplayMode targetMode)
        {
            if (_localAvatar == null)
            {
                return;
            }

            var renderers = _localAvatar.GetComponentsInChildren<Renderer>(true);
            var visible = targetMode != GameplayMode.FirstPerson;
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = visible;
            }
        }

        private static GameplayMode ModeFor(BoardFlowState state)
        {
            return state == BoardFlowState.Descending || state == BoardFlowState.Action
                ? GameplayMode.FirstPerson
                : GameplayMode.BoardTopDown;
        }
    }
}
