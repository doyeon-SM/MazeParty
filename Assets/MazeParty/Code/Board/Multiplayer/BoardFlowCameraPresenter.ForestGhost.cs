using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class BoardFlowCameraPresenter
    {
        [SerializeField] private BoardForestGhostView forestGhostPrefab;
        private BoardForestGhostView _forestGhostView;
        public void ConfigureForestGhost(BoardForestGhostView prefab) => forestGhostPrefab = prefab;

        private bool RefreshForestGhostPresentation(NetworkMatchState match)
        {
            if (!match.IsForestGhostActive || forestGhostPrefab == null)
            { ClearForestGhostPresentation(); return false; }
            if (_forestGhostView == null)
            {
                _forestGhostView = Instantiate(forestGhostPrefab);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_forestGhostView.gameObject, gameObject.scene);
            }
            _forestGhostView.gameObject.SetActive(true);
            _forestGhostView.Present(match.ForestGhost, match.SynchronizedNow, cameraDirector,
                match.GetAvatarForSlot(match.ForestGhost.Slot)?.AvatarVisual);
            if (match.ForestGhost.Slot >= 0)
                cameraDirector.SetBoardResourceEventFocus(_forestGhostView.Focus, null, 0f);
            else
                cameraDirector.SetCombatSpectatorFocus(match.ForestGhost.Position + Vector3.up * 1.45f);
            return true;
        }

        private void ClearForestGhostPresentation()
        { if (_forestGhostView != null) _forestGhostView.gameObject.SetActive(false); }

        private void OnDestroy()
        { DestroyMagicMazePresentation(); if (_forestGhostView != null) Destroy(_forestGhostView.gameObject); }
    }
}
