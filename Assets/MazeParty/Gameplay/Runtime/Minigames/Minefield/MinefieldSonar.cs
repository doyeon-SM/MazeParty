using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames.Minefield
{
    public enum MinefieldSonarRejection : byte
    {
        None,
        Disabled,
        MissingMotor,
        PlayerTerminal,
        Moving,
        StationaryDelay,
        Cooldown
    }

    public readonly struct MinefieldSonarResult
    {
        public MinefieldSonarResult(
            int pulseId,
            Vector3 origin,
            int detectedMineCount,
            float nearestMineDistance)
        {
            PulseId = pulseId;
            Origin = origin;
            DetectedMineCount = Mathf.Max(0, detectedMineCount);
            NearestMineDistance = detectedMineCount > 0
                ? Mathf.Max(0f, nearestMineDistance)
                : -1f;
        }

        public int PulseId { get; }
        public Vector3 Origin { get; }
        public int DetectedMineCount { get; }
        public float NearestMineDistance { get; }
        public bool HasDetection => DetectedMineCount > 0;
    }

    /// <summary>
    /// Stationary-only sonar request and local presentation resolver. Online play
    /// can disable autoResolveLocally, forward PulseRequested to the server, then
    /// call ApplyAuthoritativeResult after validating the request.
    /// </summary>
    [DefaultExecutionOrder(20)]
    [DisallowMultipleComponent]
    public sealed class MinefieldSonar : MonoBehaviour
    {
        [SerializeField] private MinefieldPlayerActor playerActor;
        [SerializeField] private MinefieldPlayerMotor motor;
        [SerializeField] private MinefieldMineRegistry registry;
        [SerializeField, Min(0f)] private float pulseRadius = 6f;
        [SerializeField, Min(0f)] private float revealSeconds = 1.25f;
        [SerializeField, Min(0f)] private float maximumStationarySpeed = 0.05f;
        [SerializeField, Min(0f)] private float moveIntentDeadZone = 0.05f;
        [SerializeField, Min(0f)] private float requiredStationarySeconds;
        [SerializeField, Min(0f)] private float cooldownSeconds;
        [SerializeField] private bool sonarEnabled = true;
        [SerializeField] private bool autoResolveLocally = true;

        private readonly List<MinefieldMine> _detectedMines =
            new List<MinefieldMine>();
        private float _stationarySeconds;
        private float _nextPulseTime;
        private int _nextPulseId;
        private int _lastResolvedPulseId;

        public event Action<MinefieldSonar, int> PulseRequested;
        public event Action<MinefieldSonar, MinefieldSonarResult> PulseResolved;
        public event Action<MinefieldSonar, MinefieldSonarRejection> PulseRejected;

        public bool SonarEnabled => sonarEnabled;
        public float PulseRadius => pulseRadius;
        public float StationarySeconds => _stationarySeconds;
        public MinefieldSonarResult LastResult { get; private set; }

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            ResolveReferences();
            if (motor != null &&
                IsStationaryForPulse(
                    motor.PlanarSpeed,
                    motor.LastMoveIntent,
                    maximumStationarySpeed,
                    moveIntentDeadZone))
            {
                _stationarySeconds += Time.unscaledDeltaTime;
            }
            else
            {
                _stationarySeconds = 0f;
            }
        }

        private void OnDisable()
        {
            _stationarySeconds = 0f;
        }

        public void Configure(
            MinefieldPlayerActor actor,
            MinefieldPlayerMotor playerMotor,
            MinefieldMineRegistry mineRegistry)
        {
            playerActor = actor;
            motor = playerMotor;
            registry = mineRegistry;
        }

        public void ConfigureDetection(
            float radius,
            float revealedDuration,
            float stationarySpeed,
            float stationaryDelay,
            float cooldown)
        {
            pulseRadius = Mathf.Max(0f, radius);
            revealSeconds = Mathf.Max(0f, revealedDuration);
            maximumStationarySpeed = Mathf.Max(0f, stationarySpeed);
            requiredStationarySeconds = Mathf.Max(0f, stationaryDelay);
            cooldownSeconds = Mathf.Max(0f, cooldown);
        }
        public void SetSonarEnabled(bool enabled)
        {
            sonarEnabled = enabled;
            if (!enabled)
            {
                _stationarySeconds = 0f;
            }
        }

        public void SetAutoResolveLocally(bool enabled)
        {
            autoResolveLocally = enabled;
        }

        public bool TryRequestPulse()
        {
            return TryRequestPulse(
                motor != null ? motor.LastMoveIntent : Vector2.zero);
        }

        public bool TryRequestPulse(Vector2 currentMoveIntent)
        {
            var rejection = GetRejection(currentMoveIntent, Time.unscaledTime);
            if (rejection != MinefieldSonarRejection.None)
            {
                PulseRejected?.Invoke(this, rejection);
                return false;
            }

            var pulseId = ++_nextPulseId;
            _nextPulseTime = Time.unscaledTime + cooldownSeconds;
            PulseRequested?.Invoke(this, pulseId);

            if (autoResolveLocally && _lastResolvedPulseId != pulseId)
            {
                ResolvePulseAuthoritatively(pulseId);
            }

            return true;
        }

        public MinefieldSonarResult ResolvePulseAuthoritatively(int pulseId)
        {
            ResolveReferences();
            _detectedMines.Clear();
            if (registry != null)
            {
                registry.GetArmedMinesWithinRadius(
                    transform.position,
                    pulseRadius,
                    _detectedMines);
            }

            var nearestDistance = float.PositiveInfinity;
            for (var index = 0; index < _detectedMines.Count; index++)
            {
                var mine = _detectedMines[index];
                if (mine == null)
                {
                    continue;
                }

                var delta = mine.transform.position - transform.position;
                delta.y = 0f;
                nearestDistance = Mathf.Min(nearestDistance, delta.magnitude);
                mine.RevealFor(revealSeconds);
            }

            var result = new MinefieldSonarResult(
                pulseId,
                transform.position,
                _detectedMines.Count,
                nearestDistance);
            ApplyAuthoritativeResult(result);
            return result;
        }

        public void ApplyAuthoritativeResult(MinefieldSonarResult result)
        {
            if (result.PulseId <= _lastResolvedPulseId)
            {
                return;
            }

            _lastResolvedPulseId = result.PulseId;
            LastResult = result;
            PulseResolved?.Invoke(this, result);
        }

        public MinefieldSonarRejection GetRejection(
            Vector2 currentMoveIntent,
            float now)
        {
            if (!sonarEnabled)
            {
                return MinefieldSonarRejection.Disabled;
            }
            if (motor == null)
            {
                return MinefieldSonarRejection.MissingMotor;
            }
            if (playerActor != null && !playerActor.CanMove)
            {
                return MinefieldSonarRejection.PlayerTerminal;
            }
            if (!IsStationaryForPulse(
                    motor.PlanarSpeed,
                    currentMoveIntent,
                    maximumStationarySpeed,
                    moveIntentDeadZone))
            {
                return MinefieldSonarRejection.Moving;
            }
            if (_stationarySeconds < requiredStationarySeconds)
            {
                return MinefieldSonarRejection.StationaryDelay;
            }
            if (now < _nextPulseTime)
            {
                return MinefieldSonarRejection.Cooldown;
            }

            return MinefieldSonarRejection.None;
        }

        public static bool IsStationaryForPulse(
            float planarSpeed,
            Vector2 moveIntent,
            float maximumSpeed,
            float intentDeadZone)
        {
            return Mathf.Max(0f, planarSpeed) <= Mathf.Max(0f, maximumSpeed) &&
                   moveIntent.sqrMagnitude <=
                   Mathf.Max(0f, intentDeadZone) *
                   Mathf.Max(0f, intentDeadZone);
        }

        private void ResolveReferences()
        {
            if (playerActor == null)
            {
                playerActor = GetComponent<MinefieldPlayerActor>();
            }
            if (motor == null)
            {
                motor = GetComponent<MinefieldPlayerMotor>();
            }
            if (registry == null)
            {
                registry = GetComponentInParent<MinefieldMineRegistry>();
            }
        }
    }
}
