using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Gameplay.Testbed
{
    [RequireComponent(typeof(Collider))]
    public sealed class TestbedTarget : MonoBehaviour, IDamageable, IPushReceiver, IInteractable
    {
        [SerializeField] private string interactionPrompt = "Inspect Target";
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField] private bool rollDieOnInteract;

        private Rigidbody _body;
        private Renderer _renderer;
        private Color _baseColor = Color.white;
        private bool _toggled;
        private Vector3 _initialPosition;
        private Quaternion _initialRotation;

        public string InteractionPrompt => interactionPrompt;
        public string LastInteractionMessage { get; private set; } = string.Empty;
        public int CurrentHealth { get; private set; }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _renderer = GetComponentInChildren<Renderer>();
            if (_renderer != null)
                _baseColor = _renderer.material.color;

            _initialPosition = transform.position;
            _initialRotation = transform.rotation;
            CurrentHealth = maxHealth;
        }

        public void Configure(string prompt, bool rollsDie)
        {
            interactionPrompt = prompt;
            rollDieOnInteract = rollsDie;
        }

        public DamageResult ApplyDamage(DamageRequest request)
        {
            if (request.Amount <= 0 || CurrentHealth <= 0)
                return DamageResult.Ignored;

            CurrentHealth = Mathf.Max(0, CurrentHealth - request.Amount);
            UpdateDamageColor();
            LastInteractionMessage = name + " HP " + CurrentHealth + "/" + maxHealth;
            return DamageResult.Applied;
        }

        public void ApplyPush(Vector3 impulse)
        {
            if (_body != null && !_body.isKinematic)
                _body.AddForce(impulse, ForceMode.VelocityChange);
            else
                transform.position += impulse * 0.08f;
        }

        public void Interact(GameObject instigator)
        {
            if (rollDieOnInteract)
            {
                var result = Random.Range(1, 11);
                transform.rotation = Random.rotation;
                LastInteractionMessage = "Board die result: " + result;
                return;
            }

            _toggled = !_toggled;
            if (_renderer != null)
                _renderer.material.color = _toggled ? Color.cyan : _baseColor;
            LastInteractionMessage = interactionPrompt + (_toggled ? " ON" : " OFF");
        }

        public void ResetState()
        {
            CurrentHealth = maxHealth;
            _toggled = false;
            LastInteractionMessage = string.Empty;
            if (_renderer != null)
                _renderer.material.color = _baseColor;

            transform.SetPositionAndRotation(_initialPosition, _initialRotation);
            if (_body != null)
            {
                _body.position = _initialPosition;
                _body.rotation = _initialRotation;
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
        }

        private void UpdateDamageColor()
        {
            if (_renderer == null)
                return;

            var healthRatio = maxHealth > 0 ? (float)CurrentHealth / maxHealth : 0f;
            _renderer.material.color = Color.Lerp(Color.black, _baseColor, healthRatio);
        }
    }
}
