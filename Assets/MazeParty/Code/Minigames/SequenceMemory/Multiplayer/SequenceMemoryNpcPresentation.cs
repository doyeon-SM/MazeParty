using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>Shared avatar and pitch gestures for the sequence demonstrator.</summary>
    [DisallowMultipleComponent]
    public sealed class SequenceMemoryNpcPresentation : MonoBehaviour
    {
        [SerializeField] private PlayerAvatarVisual visual;
        [SerializeField] private Color bodyColor = new Color(0.95f, 0.25f, 0.25f);
        [SerializeField] private byte hatId = 1;
        [SerializeField] private byte expressionId;
        [Header("Hand positions in avatar space (mirrored left/right)")]
        [SerializeField] private Vector3 lowHandPosition = new Vector3(0.7f, -0.7f, 0.25f);
        [SerializeField] private Vector3 middleHandPosition = new Vector3(0.7f, -0.12f, 0.35f);
        [SerializeField] private Vector3 highHandPosition = new Vector3(0.7f, 0.8f, 0.15f);
        [SerializeField, Min(0.1f)] private float gestureSeconds = 0.55f;
        private Transform _camera;
        private Vector3 _target;
        private float _elapsed;
        private bool _presenting;
        private bool _paused;

        public PlayerAvatarVisual Visual => visual;
        public bool HasRequiredReferences => visual != null && visual.Bindings != null &&
            visual.Bindings.HasRequiredReferences;

        public void Configure(PlayerAvatarVisual avatar) { visual = avatar; }
        public void SetCamera(Transform cameraTransform) { _camera = cameraTransform; }
        public void SetPaused(bool paused) { _paused = paused; }

        private void Awake()
        {
            if (visual == null) return;
            visual.EnsureBuilt();
            visual.SetBodyColor(bodyColor);
            visual.ApplyAppearance(0, 0, hatId, expressionId);
            visual.SetOwnerFirstPerson(false);
            visual.SetTopViewHighlight(false);
            visual.SetNameplateVisible(false);
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }

        public void PresentTone(SequenceMemoryInput input)
        {
            if (!SequenceMemoryRules.IsValidInput(input)) return;
            _target = input == SequenceMemoryInput.A ? lowHandPosition :
                input == SequenceMemoryInput.S ? middleHandPosition : highHandPosition;
            _elapsed = 0f;
            _presenting = true;
        }

        public void ClearTone()
        {
            _presenting = false;
            _elapsed = 0f;
        }

        private void OnDisable() { ClearTone(); _paused = false; }

        private void LateUpdate()
        {
            if (visual == null || !visual.IsBuilt) return;
            var direction = (_camera != null ? _camera.position :
                SequenceMemoryNetworkView.SharedCameraPosition) - visual.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                visual.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            if (!_presenting) return;
            if (!_paused) _elapsed += Time.unscaledDeltaTime;
            var progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.1f, gestureSeconds));
            var amount = progress < 0.18f ? Mathf.SmoothStep(0f, 1f, progress / 0.18f) :
                progress < 0.72f ? 1f : Mathf.SmoothStep(1f, 0f, (progress - 0.72f) / 0.28f);
            var bindings = visual.Bindings;
            bindings.LeftHandAnchor.localPosition = Vector3.Lerp(
                bindings.LeftHandAnchor.localPosition, _target, amount);
            var right = new Vector3(-_target.x, _target.y, _target.z);
            bindings.RightHandAnchor.localPosition = Vector3.Lerp(
                bindings.RightHandAnchor.localPosition, right, amount);
            if (progress >= 1f) ClearTone();
        }
    }
}
