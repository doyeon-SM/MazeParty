using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class BoardFlowCameraPresenter
    {
        [SerializeField] private BoardForestGhostView forestGhostPrefab;
        private BoardForestGhostView _forestGhostView;
        private BoardLandingEffectFeedbackView _forestGhostFeedbackVisual;
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
            var avatar = match.GetAvatarForSlot(match.ForestGhost.Slot);
            _forestGhostView.Present(match.ForestGhost, match.SynchronizedNow, cameraDirector, avatar?.AvatarVisual);
            var head = match.ForestGhost.IsIntro ? _forestGhostView.Head : avatar != null ? ResolveHeadAnchor(avatar) : null;
            RefreshForestGhostFeedback(match.ForestGhost, match.SynchronizedNow,
                match.ForestGhost.IsIntro ? _forestGhostView.CaptionAnchor : head);
            if (match.ForestGhost.IsIntro || match.ForestGhost.Slot >= 0)
                cameraDirector.SetBoardResourceEventFocus(_forestGhostView.Focus, head, 0f, match.ForestGhost.IsIntro);
            else
                cameraDirector.SetCombatSpectatorFocus(match.ForestGhost.Position + Vector3.up * 1.45f);
            return true;
        }

        private void RefreshForestGhostFeedback(BoardForestGhostSnapshot snapshot, double now, Transform head)
        {
            if (head == null || landingEffectFeedbackPrefab == null ||
                !BoardLandingEffectFeedbackRules.TryGetForestGhostContent(snapshot, now,
                    out var iconKind, out var label, out var remaining))
            { ClearForestGhostFeedback(); return; }
            if (_forestGhostFeedbackVisual == null)
            {
                _forestGhostFeedbackVisual = Instantiate(landingEffectFeedbackPrefab);
                _forestGhostFeedbackVisual.name = "Forest Ghost Feedback";
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_forestGhostFeedbackVisual.gameObject, gameObject.scene);
            }
            _forestGhostFeedbackVisual.Present(head, cameraDirector.OutputCamera, iconKind, label, remaining);
        }

        private void ClearForestGhostFeedback()
        {
            if (_forestGhostFeedbackVisual != null) Destroy(_forestGhostFeedbackVisual.gameObject);
            _forestGhostFeedbackVisual = null;
        }

        private void ClearForestGhostPresentation()
        {
            if (_forestGhostView != null) _forestGhostView.gameObject.SetActive(false);
            ClearForestGhostFeedback();
        }

        private void OnDestroy()
        { ClearForestGhostFeedback(); DestroyMagicMazePresentation(); if (_forestGhostView != null) Destroy(_forestGhostView.gameObject); }
    }
}
