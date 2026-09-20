using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Gameplay.Minigames.Minefield
{
    public readonly struct MinefieldInputFrame
    {
        public MinefieldInputFrame(uint sequence, Vector2 move, bool sonarPressed)
        {
            Sequence = sequence;
            Move = Vector2.ClampMagnitude(move, 1f);
            SonarPressed = sonarPressed;
        }

        public uint Sequence { get; }
        public Vector2 Move { get; }
        public bool SonarPressed { get; }
    }

    /// <summary>
    /// WASD and right-mouse input for the locally owned Minefield avatar.
    /// Input starts disabled so remote prefab instances cannot poll local devices.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinefieldInputAdapter : MonoBehaviour
    {
        [SerializeField] private bool inputEnabled;

        private InputAction _move;
        private InputAction _sonar;
        private uint _sequence;

        public bool InputEnabled => inputEnabled;
        public Vector2 Move => inputEnabled && _move != null
            ? _move.ReadValue<Vector2>()
            : Vector2.zero;
        public bool SonarPressed => inputEnabled && _sonar != null &&
                                    _sonar.WasPressedThisFrame();

        private void Awake()
        {
            CreateActions();
        }

        private void OnEnable()
        {
            CreateActions();
            ApplyEnabledState();
        }

        private void OnDisable()
        {
            DisableActions();
        }

        private void OnDestroy()
        {
            _move?.Dispose();
            _sonar?.Dispose();
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
            ApplyEnabledState();
        }

        public MinefieldInputFrame ReadFrame()
        {
            _sequence++;
            return new MinefieldInputFrame(_sequence, Move, SonarPressed);
        }

        private void CreateActions()
        {
            if (_move != null)
            {
                return;
            }

            _move = new InputAction("Minefield Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _sonar = new InputAction(
                "Minefield Sonar",
                InputActionType.Button,
                "<Mouse>/rightButton");
        }

        private void ApplyEnabledState()
        {
            if (!isActiveAndEnabled || !inputEnabled)
            {
                DisableActions();
                return;
            }

            _move?.Enable();
            _sonar?.Enable();
        }

        private void DisableActions()
        {
            _move?.Disable();
            _sonar?.Disable();
        }
    }
}
