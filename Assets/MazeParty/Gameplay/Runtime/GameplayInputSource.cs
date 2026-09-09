using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Neutral local input adapter. Gameplay code consumes this interface rather than
    /// polling devices directly, so a later online/Steam ownership adapter can replace it.
    /// </summary>
    public sealed class GameplayInputSource : MonoBehaviour, IGameplayInputSource
    {
        private InputAction _move;
        private InputAction _look;
        private InputAction _walk;
        private InputAction _primary;
        private InputAction _secondary;
        private InputAction _cancel;

        public Vector2 Move => _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
        public Vector2 Look => _look != null ? _look.ReadValue<Vector2>() : Vector2.zero;
        public bool WalkHeld => _walk != null && _walk.IsPressed();
        public bool PrimaryPressed => _primary != null && _primary.WasPressedThisFrame();
        public bool PrimaryHeld => _primary != null && _primary.IsPressed();
        public bool SecondaryPressed => _secondary != null && _secondary.WasPressedThisFrame();
        public bool CancelPressed => _cancel != null && _cancel.WasPressedThisFrame();

        private void Awake()
        {
            CreateActions();
        }

        private void OnEnable()
        {
            CreateActions();
            _move.Enable();
            _look.Enable();
            _walk.Enable();
            _primary.Enable();
            _secondary.Enable();
            _cancel.Enable();
        }

        private void OnDisable()
        {
            _move?.Disable();
            _look?.Disable();
            _walk?.Disable();
            _primary?.Disable();
            _secondary?.Disable();
            _cancel?.Disable();
        }

        private void OnDestroy()
        {
            _move?.Dispose();
            _look?.Dispose();
            _walk?.Dispose();
            _primary?.Dispose();
            _secondary?.Dispose();
            _cancel?.Dispose();
        }

        private void CreateActions()
        {
            if (_move != null)
                return;

            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            _look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            _walk = new InputAction("Quiet Walk", InputActionType.Button, "<Keyboard>/leftCtrl");
            _primary = new InputAction("Primary", InputActionType.Button, "<Mouse>/leftButton");
            _secondary = new InputAction("Secondary", InputActionType.Button, "<Mouse>/rightButton");
            _cancel = new InputAction("Cancel", InputActionType.Button, "<Keyboard>/escape");
        }
    }
}
