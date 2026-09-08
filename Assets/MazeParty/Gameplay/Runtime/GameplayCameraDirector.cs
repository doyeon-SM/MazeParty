using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public sealed class GameplayCameraDirector : MonoBehaviour, IGameplayCameraService
    {
        private const int LivePriority = 100;
        private const int StandbyPriority = 0;

        [SerializeField] private Camera outputCamera;
        [SerializeField] private CinemachineCamera firstPersonCamera;
        [SerializeField] private CinemachineCamera boardTopDownCamera;
        [SerializeField] private CinemachineCamera minigameCamera;
        [SerializeField] private GameplayMode activeMode = GameplayMode.BoardTopDown;

        private bool _uiPointerVisible;

        public GameplayMode ActiveMode => activeMode;
        public Camera OutputCamera => outputCamera;

        public void Configure(
            Camera cameraOutput,
            CinemachineCamera firstPerson,
            CinemachineCamera boardTopDown,
            CinemachineCamera minigame)
        {
            outputCamera = cameraOutput;
            firstPersonCamera = firstPerson;
            boardTopDownCamera = boardTopDown;
            minigameCamera = minigame;
            ApplyPriorities();
        }

        public void SwitchTo(GameplayMode mode)
        {
            activeMode = mode;
            ApplyPriorities();
            ApplyCursorPolicy();
        }

        public void SetUiPointerVisible(bool visible)
        {
            _uiPointerVisible = visible;
            ApplyCursorPolicy();
        }

        private void OnEnable()
        {
            ApplyPriorities();
            ApplyCursorPolicy();
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ApplyPriorities()
        {
            if (firstPersonCamera != null)
                firstPersonCamera.Priority = activeMode == GameplayMode.FirstPerson ? LivePriority : StandbyPriority;
            if (boardTopDownCamera != null)
                boardTopDownCamera.Priority = activeMode == GameplayMode.BoardTopDown ? LivePriority : StandbyPriority;
            if (minigameCamera != null)
                minigameCamera.Priority = activeMode == GameplayMode.Minigame ? LivePriority : StandbyPriority;
        }

        private void ApplyCursorPolicy()
        {
            var pointerVisible = _uiPointerVisible || activeMode != GameplayMode.FirstPerson;
            Cursor.lockState = pointerVisible ? CursorLockMode.Confined : CursorLockMode.Locked;
            Cursor.visible = pointerVisible;
        }
    }
}
