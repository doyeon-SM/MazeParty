using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames.Minefield
{
    /// <summary>
    /// Motion and contact bridge for the temporary cube crusher. The server can
    /// own TickAuthoritatively and replicate normalized progress, while an offline
    /// scene can leave simulationAuthority and autoResolveContacts enabled.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class MinefieldCrusher : MonoBehaviour
    {
        [SerializeField] private Transform startAnchor;
        [SerializeField] private Transform endAnchor;
        [SerializeField] private Vector3 fallbackEndOffset =
            new Vector3(0f, 0f, 24f);
        [SerializeField, Min(0f)] private float sweepSpeed = 2.5f;
        [SerializeField] private bool simulationAuthority = true;
        [SerializeField] private bool autoResolveContacts = true;

        private readonly HashSet<MinefieldPlayerActor> _requestedActors = new HashSet<MinefieldPlayerActor>();
        private Collider _trigger;
        private Vector3 _startPosition;
        private Vector3 _endPosition;
        private bool _pathCaptured;
        private bool _sweeping;
        private float _progress;

        public event Action<MinefieldCrusher, float> ProgressChanged;
        public event Action<MinefieldCrusher> SweepCompleted;
        public event Action<MinefieldCrusher, MinefieldPlayerActor>
            EliminationRequested;
        public event Action<MinefieldCrusher, MinefieldPlayerActor>
            EliminationResolved;

        public bool IsSweeping => _sweeping;
        public float Progress => _progress;
        public Vector3 StartPosition => _startPosition;
        public Vector3 EndPosition => _endPosition;

        private void Awake()
        {
            EnsureTrigger();
            CaptureConfiguredPath();
        }

        private void Update()
        {
            if (simulationAuthority && _sweeping)
            {
                TickAuthoritatively(Time.deltaTime);
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
            if (!_sweeping || other == null)
            {
                return;
            }

            TryRequestElimination(
                other.GetComponentInParent<MinefieldPlayerActor>());
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
                _requestedActors.Remove(actor);
            }
        }

        public void ConfigurePath(Vector3 start, Vector3 end)
        {
            startAnchor = null;
            endAnchor = null;
            _startPosition = start;
            _endPosition = end;
            _pathCaptured = true;
            ApplyAuthoritativeProgress(0f);
        }

        public void ConfigurePath(Transform start, Transform end)
        {
            startAnchor = start;
            endAnchor = end;
            _pathCaptured = false;
            CaptureConfiguredPath();
            ApplyAuthoritativeProgress(0f);
        }

        public void SetSimulationAuthority(bool hasAuthority)
        {
            simulationAuthority = hasAuthority;
        }

        public void SetAutoResolveContacts(bool enabled)
        {
            autoResolveContacts = enabled;
        }

        public void ResetForRoundAuthoritatively()
        {
            CaptureConfiguredPath();
            _sweeping = false;
            _requestedActors.Clear();
            ApplyAuthoritativeProgress(0f);
        }

        public void BeginSweepAuthoritatively()
        {
            CaptureConfiguredPath();
            _sweeping = true;
            if (_progress >= 1f)
            {
                ApplyAuthoritativeProgress(0f);
            }
        }

        public void StopSweepAuthoritatively()
        {
            _sweeping = false;
        }

        public void TickAuthoritatively(float deltaTime)
        {
            if (!_sweeping)
            {
                return;
            }

            CaptureConfiguredPath();
            var distance = Vector3.Distance(_startPosition, _endPosition);
            if (distance <= 0.0001f)
            {
                ApplyAuthoritativeProgress(1f);
                CompleteSweep();
                return;
            }

            var nextProgress = _progress +
                               sweepSpeed * Mathf.Max(0f, deltaTime) / distance;
            ApplyAuthoritativeProgress(nextProgress);
            if (_progress >= 1f)
            {
                CompleteSweep();
            }
        }

        public void ApplyAuthoritativeProgress(float normalizedProgress)
        {
            CaptureConfiguredPath();
            var nextProgress = Mathf.Clamp01(normalizedProgress);
            _progress = nextProgress;
            transform.position = Vector3.Lerp(
                _startPosition,
                _endPosition,
                _progress);
            ProgressChanged?.Invoke(this, _progress);
        }

        public bool TryRequestElimination(MinefieldPlayerActor actor)
        {
            if (!_sweeping || actor == null || !actor.CanReceiveHazards)
            {
                return false;
            }

            if (!_requestedActors.Add(actor))
            {
                return false;
            }

            if (!actor.RequestHazardContact(
                    MinefieldHazardKind.Crusher,
                    gameObject))
            {
                _requestedActors.Remove(actor);
                return false;
            }

            EliminationRequested?.Invoke(this, actor);
            if (autoResolveContacts && actor.CanReceiveHazards)
            {
                ResolveEliminationAuthoritatively(actor);
            }

            return true;
        }

        public bool ResolveEliminationAuthoritatively(MinefieldPlayerActor actor)
        {
            if (actor == null ||
                !actor.ApplyAuthoritativeElimination(
                    MinefieldEliminationCause.Crusher,
                    gameObject))
            {
                return false;
            }

            EliminationResolved?.Invoke(this, actor);
            return true;
        }

        private void CompleteSweep()
        {
            if (!_sweeping)
            {
                return;
            }

            _sweeping = false;
            SweepCompleted?.Invoke(this);
        }

        private void CaptureConfiguredPath()
        {
            if (_pathCaptured &&
                startAnchor == null &&
                endAnchor == null)
            {
                return;
            }

            _startPosition = startAnchor != null
                ? startAnchor.position
                : transform.position;
            _endPosition = endAnchor != null
                ? endAnchor.position
                : _startPosition + fallbackEndOffset;
            _pathCaptured = true;
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
        public void SetSweepSpeed(float speed)
        {
            sweepSpeed = Mathf.Max(0f, speed);
        }
    }
}
