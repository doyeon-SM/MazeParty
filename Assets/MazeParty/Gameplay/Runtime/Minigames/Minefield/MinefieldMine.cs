using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames.Minefield
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class MinefieldMine : MonoBehaviour
    {
        [SerializeField] private MinefieldMineRegistry registry;
        [SerializeField] private GameObject concealedVisual;
        [SerializeField] private GameObject revealedVisual;
        [SerializeField] private bool armed = true;
        [SerializeField] private bool autoResolveContacts = true;

        private readonly HashSet<MinefieldPlayerActor> _requestedPlayers = new HashSet<MinefieldPlayerActor>();
        private Collider _trigger;
        private float _revealUntil = float.NegativeInfinity;

        public event Action<MinefieldMine, MinefieldPlayerActor> ContactRequested;
        public event Action<MinefieldMine, MinefieldPlayerActor, MinefieldHitResolution>
            ContactResolved;
        public event Action<MinefieldMine, bool> ArmedStateChanged;

        public bool IsArmed => armed;
        public bool IsRevealed => armed && Time.time < _revealUntil;

        private void Awake()
        {
            EnsureTrigger();
            ResolveRegistry();
            ApplyVisualState();
        }

        private void OnEnable()
        {
            ResolveRegistry();
            registry?.Register(this);
            ApplyVisualState();
        }

        private void OnDisable()
        {
            registry?.Unregister(this);
            _requestedPlayers.Clear();
        }

        private void Update()
        {
            if (revealedVisual != null &&
                revealedVisual.activeSelf != IsRevealed)
            {
                ApplyVisualState();
            }
        }

        private void Reset()
        {
            EnsureTrigger();
        }

        private void OnValidate()
        {
            EnsureTrigger();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other == null)
            {
                return;
            }

            TryRequestContact(other.GetComponentInParent<MinefieldPlayerActor>());
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null)
            {
                return;
            }

            var actor = other.GetComponentInParent<MinefieldPlayerActor>();
            if (actor != null)
            {
                _requestedPlayers.Remove(actor);
            }
        }

        public void ConfigureRegistry(MinefieldMineRegistry mineRegistry)
        {
            if (registry == mineRegistry)
            {
                return;
            }

            registry?.Unregister(this);
            registry = mineRegistry;
            if (isActiveAndEnabled)
            {
                registry?.Register(this);
            }
        }

        public void ConfigureVisuals(
            GameObject hiddenVisual,
            GameObject sonarRevealedVisual)
        {
            concealedVisual = hiddenVisual;
            revealedVisual = sonarRevealedVisual;
            ApplyVisualState();
        }

        public void SetAutoResolveContacts(bool enabled)
        {
            autoResolveContacts = enabled;
        }

        public void ArmForRoundAuthoritatively(bool shouldArm = true)
        {
            if (armed != shouldArm)
            {
                armed = shouldArm;
                ArmedStateChanged?.Invoke(this, armed);
            }

            _requestedPlayers.Clear();
            _revealUntil = float.NegativeInfinity;
            ApplyVisualState();
        }

        public bool TryRequestContact(MinefieldPlayerActor actor)
        {
            if (!armed || actor == null || !actor.CanReceiveHazards)
            {
                return false;
            }

            if (!_requestedPlayers.Add(actor))
            {
                return false;
            }

            if (!actor.RequestHazardContact(MinefieldHazardKind.Mine, gameObject))
            {
                _requestedPlayers.Remove(actor);
                return false;
            }

            ContactRequested?.Invoke(this, actor);
            if (autoResolveContacts && armed)
            {
                ResolveContactAuthoritatively(actor);
            }

            return true;
        }

        public MinefieldHitResolution ResolveContactAuthoritatively(
            MinefieldPlayerActor actor)
        {
            if (!armed || actor == null || !actor.CanReceiveHazards)
            {
                var currentState = actor != null
                    ? actor.State
                    : MinefieldPlayerState.Eliminated;
                var hitCount = actor != null ? actor.MineHitCount : 0;
                return new MinefieldHitResolution(
                    false,
                    currentState,
                    currentState,
                    hitCount);
            }

            armed = false;
            _revealUntil = float.NegativeInfinity;
            ArmedStateChanged?.Invoke(this, false);
            ApplyVisualState();

            var resolution = actor.ApplyAuthoritativeMineHit(gameObject);
            ContactResolved?.Invoke(this, actor, resolution);
            return resolution;
        }

        public void RevealFor(float seconds)
        {
            if (!armed)
            {
                return;
            }

            _revealUntil = Mathf.Max(
                _revealUntil,
                Time.time + Mathf.Max(0f, seconds));
            ApplyVisualState();
        }

        private void ResolveRegistry()
        {
            if (registry == null)
            {
                registry = GetComponentInParent<MinefieldMineRegistry>();
            }
        }

        private void EnsureTrigger()
        {
            if (_trigger == null)
            {
                _trigger = GetComponent<Collider>();
            }
            if (_trigger != null)
            {
                _trigger.isTrigger = true;
            }
        }

        private void ApplyVisualState()
        {
            var revealed = IsRevealed;
            if (concealedVisual != null && concealedVisual != gameObject)
            {
                concealedVisual.SetActive(armed && !revealed);
            }
            if (revealedVisual != null && revealedVisual != gameObject)
            {
                revealedVisual.SetActive(armed && revealed);
            }
        }
    }
}
