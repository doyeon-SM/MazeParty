using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        internal void RefreshTravelAfterWallPlacementOnServer()
        {
            if (IsServer && _traversal.IsInitialized)
                RebaseBoardTravelPreviewAfterRelocationOnServer(_traversal.CurrentTile);
        }

        private GameObject _wallPlacementPreview;
        private float _nextWallPreviewCheck;

        private void RefreshWallPlacementPreview(NetworkMatchState match, PrototypeItemId item)
        {
            var visible = IsOwner && item == PrototypeItemId.Wall && LocalItemCharges > 0 &&
                LocalChoiceResolution == ItemChoiceResolution.ItemSelected && CurrentHealth > 0 &&
                match != null && match.GameplayEnabled && match.CanAcceptActionInput &&
                !IsKnockedDown && !IsSwapping && !IsSettingBombardment && !LocalInputGate.BlocksGameplayInput;
            if (!visible)
            {
                if (_wallPlacementPreview != null) _wallPlacementPreview.SetActive(false);
                _nextWallPreviewCheck = 0f;
                return;
            }
            if (Time.unscaledTime < _nextWallPreviewCheck) return;
            _nextWallPreviewCheck = Time.unscaledTime + .05f;
            var origin = EyePivot != null ? EyePivot.position : transform.position + Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
            var direction = EyePivot != null ? EyePivot.forward : transform.forward;
            if (!match.TryGetWallPlacement(this, origin, direction, out var gate))
            {
                if (_wallPlacementPreview != null) _wallPlacementPreview.SetActive(false);
                return;
            }
            if (_wallPlacementPreview == null)
            {
                _wallPlacementPreview = Instantiate(PrototypeItemCatalog.Get(PrototypeItemId.Wall).PlacementPreviewPrefab);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_wallPlacementPreview, gameObject.scene);
            }
            _wallPlacementPreview.transform.SetPositionAndRotation(BoardWallRules.Position(gate), BoardWallRules.Rotation(gate));
            _wallPlacementPreview.transform.localScale = new Vector3(gate.GateWidth, 1f, 1f);
            _wallPlacementPreview.SetActive(true);
        }

        private void ClearWallPlacementPreview()
        {
            if (_wallPlacementPreview == null) return;
            if (Application.isPlaying) Destroy(_wallPlacementPreview); else DestroyImmediate(_wallPlacementPreview);
            _wallPlacementPreview = null;
        }

        [Rpc(SendTo.Owner)]
        private void RejectWallPlacementRpc() => GameSound.Play(SoundKeys.UiError);
    }
}
