using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class BoardFlowCameraPresenter
    {
        [SerializeField] private BoardCameraCloudView magicMazeCloudPrefab;
        private BoardCameraCloudView _magicMazeCloud;
        private bool _magicMazeCameraReady;
        public BoardCameraCloudView MagicMazeCloudPrefab => magicMazeCloudPrefab;

        private bool RefreshMagicMazePresentation(NetworkMatchState match)
        {
            if (!match.IsMagicMazeShuffle)
            { ClearMagicMazePresentation(); return false; }
            if (_magicMazeCloud == null && magicMazeCloudPrefab != null)
            {
                _magicMazeCloud = Instantiate(magicMazeCloudPrefab);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_magicMazeCloud.gameObject, gameObject.scene);
            }
            var elapsed = match.MagicMazeElapsed;
            if (elapsed >= BoardCameraCloudTiming.CoveredAt && !_magicMazeCameraReady)
            {
                ClearEventPresentation(); ClearResourceTransferVisual(); ClearLandingEffectFeedbackVisual();
                cameraDirector.SnapTo(GameplayMode.BoardTopDown);
                _magicMazeCameraReady = true;
            }
            if (_magicMazeCloud != null)
            {
                if (elapsed < BoardCameraCloudTiming.Duration) _magicMazeCloud.Present(elapsed);
                else if (elapsed >= MagicMazeRules.ExitCloudAt) _magicMazeCloud.Present(elapsed - MagicMazeRules.ExitCloudAt);
                else _magicMazeCloud.Hide();
            }
            ApplyLocalBodyVisibility(cameraDirector.ActiveMode);
            RefreshOpponentNameplateOcclusion(match, cameraDirector.ActiveMode);
            return true;
        }
        private void ClearMagicMazePresentation()
        {
            _magicMazeCameraReady = false;
            if (_magicMazeCloud != null) _magicMazeCloud.Hide();
        }
        private void DestroyMagicMazePresentation()
        {
            if (_magicMazeCloud != null) Destroy(_magicMazeCloud.gameObject);
        }
    }
}
