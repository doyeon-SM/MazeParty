using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Authored one-shot presentation that is returned to
    /// <see cref="OneShotVfxPool"/> instead of being destroyed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PooledOneShotVfx : MonoBehaviour
    {
        private const float CompletionCheckDelay = 0.05f;

        [SerializeField] private ParticleSystem[] particleSystems =
            Array.Empty<ParticleSystem>();
        [SerializeField] private Light[] flashLights = Array.Empty<Light>();
        [Tooltip("Hard safety timeout. Effects normally return after every " +
                 "authored particle system has finished.")]
        [SerializeField, Min(0.05f)] private float effectLifetime = 1f;
        [SerializeField, Min(0f)] private float flashLightSeconds = 0.15f;
        [SerializeField, Range(0f, 1f)] private float reducedEmissionScale =
            PresentationAccessibility.ReducedFlashIntensityScale;

        private ParticleBaseline[] _particleBaselines =
            Array.Empty<ParticleBaseline>();
        private float[] _baseLightIntensities = Array.Empty<float>();
        private Action<PooledOneShotVfx> _returnToPool;
        private float _timeoutAt;
        private float _earliestReturnAt;
        private float _disableLightsAt;
        private bool _baselinesCaptured;
        private bool _isPlaying;

        public ParticleSystem[] ParticleSystems => particleSystems;
        public Light[] FlashLights => flashLights;
        public float EffectLifetime => effectLifetime;

        /// <summary>
        /// Editor/setup binding hook. Runtime callers should use
        /// <see cref="OneShotVfxPool.Play"/>.
        /// </summary>
        public void Configure(
            ParticleSystem[] systems,
            Light[] lights,
            float lifetime,
            float lightSeconds)
        {
            particleSystems = systems ?? Array.Empty<ParticleSystem>();
            flashLights = lights ?? Array.Empty<Light>();
            effectLifetime = Mathf.Max(0.05f, lifetime);
            flashLightSeconds = Mathf.Max(0f, lightSeconds);
            _baselinesCaptured = false;
        }

        internal void PrepareForPool(Action<PooledOneShotVfx> returnToPool)
        {
            _returnToPool = returnToPool;
            CaptureBaselines();
            StopAndRestore();
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        internal void PlayFromPool()
        {
            CaptureBaselines();
            ApplyAccessibility();
            _isPlaying = true;
            var now = Time.unscaledTime;
            _timeoutAt = now + effectLifetime;
            _disableLightsAt = now + flashLightSeconds;
            _earliestReturnAt = Mathf.Max(
                now + CompletionCheckDelay,
                _disableLightsAt);

            for (var index = 0; index < particleSystems.Length; index++)
            {
                var system = particleSystems[index];
                if (system == null)
                {
                    continue;
                }

                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Play(true);
            }

            SetLightsEnabled(flashLightSeconds > 0f);
        }

        private void Update()
        {
            if (!_isPlaying)
            {
                return;
            }

            var now = Time.unscaledTime;
            if (now >= _disableLightsAt)
            {
                SetLightsEnabled(false);
            }
            if (now >= _timeoutAt ||
                (now >= _earliestReturnAt && !HasLiveParticles()))
            {
                ReturnToPool();
            }
        }

        private void OnDisable()
        {
            if (!_isPlaying)
            {
                return;
            }

            ReturnToPool();
        }

        private bool HasLiveParticles()
        {
            for (var index = 0; index < particleSystems.Length; index++)
            {
                var system = particleSystems[index];
                if (system != null && system.IsAlive(true))
                {
                    return true;
                }
            }

            return false;
        }

        private void CaptureBaselines()
        {
            if (_baselinesCaptured)
            {
                return;
            }

            particleSystems ??= Array.Empty<ParticleSystem>();
            flashLights ??= Array.Empty<Light>();
            _particleBaselines = new ParticleBaseline[particleSystems.Length];
            for (var index = 0; index < particleSystems.Length; index++)
            {
                var system = particleSystems[index];
                if (system == null)
                {
                    continue;
                }

                var emission = system.emission;
                var bursts = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(bursts);
                _particleBaselines[index] = new ParticleBaseline(
                    emission.rateOverTimeMultiplier,
                    emission.rateOverDistanceMultiplier,
                    bursts);
            }

            _baseLightIntensities = new float[flashLights.Length];
            for (var index = 0; index < flashLights.Length; index++)
            {
                _baseLightIntensities[index] = flashLights[index] != null
                    ? Mathf.Max(0f, flashLights[index].intensity)
                    : 0f;
            }
            _baselinesCaptured = true;
        }

        private void ApplyAccessibility()
        {
            var emissionScale = ResolveEmissionScale(
                PresentationAccessibility.ReduceFlashes,
                reducedEmissionScale);
            for (var index = 0; index < particleSystems.Length; index++)
            {
                var system = particleSystems[index];
                if (system == null)
                {
                    continue;
                }

                var baseline = _particleBaselines[index];
                var emission = system.emission;
                emission.rateOverTimeMultiplier =
                    baseline.RateOverTimeMultiplier * emissionScale;
                emission.rateOverDistanceMultiplier =
                    baseline.RateOverDistanceMultiplier * emissionScale;
                for (var burstIndex = 0;
                     burstIndex < baseline.Bursts.Length;
                     burstIndex++)
                {
                    var burst = baseline.Bursts[burstIndex];
                    burst.minCount = ScaleBurstCount(
                        burst.minCount,
                        emissionScale);
                    burst.maxCount = ScaleBurstCount(
                        burst.maxCount,
                        emissionScale);
                    emission.SetBurst(burstIndex, burst);
                }
            }

            var lightScale = PresentationAccessibility.FlashIntensityScale;
            for (var index = 0; index < flashLights.Length; index++)
            {
                if (flashLights[index] != null)
                {
                    flashLights[index].intensity =
                        _baseLightIntensities[index] * lightScale;
                }
            }
        }

        private void StopAndRestore()
        {
            CaptureBaselines();
            _isPlaying = false;
            for (var index = 0; index < particleSystems.Length; index++)
            {
                var system = particleSystems[index];
                if (system == null)
                {
                    continue;
                }

                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var baseline = _particleBaselines[index];
                var emission = system.emission;
                emission.rateOverTimeMultiplier =
                    baseline.RateOverTimeMultiplier;
                emission.rateOverDistanceMultiplier =
                    baseline.RateOverDistanceMultiplier;
                emission.SetBursts(baseline.Bursts);
            }

            for (var index = 0; index < flashLights.Length; index++)
            {
                var light = flashLights[index];
                if (light == null)
                {
                    continue;
                }

                light.intensity = _baseLightIntensities[index];
                light.enabled = false;
            }
        }

        private void SetLightsEnabled(bool enabled)
        {
            for (var index = 0; index < flashLights.Length; index++)
            {
                if (flashLights[index] != null)
                {
                    flashLights[index].enabled = enabled;
                }
            }
        }

        private void ReturnToPool()
        {
            if (!_isPlaying)
            {
                return;
            }

            _isPlaying = false;
            StopAndRestore();
            _returnToPool?.Invoke(this);
        }

        internal void StopAndReturnToPool()
        {
            if (!_isPlaying)
            {
                return;
            }

            ReturnToPool();
        }

        internal static float ResolveEmissionScale(
            bool reduceFlashes,
            float reducedScale)
        {
            return reduceFlashes ? Mathf.Clamp01(reducedScale) : 1f;
        }

        private static short ScaleBurstCount(short count, float scale)
        {
            if (count <= 0)
            {
                return 0;
            }

            return (short)Mathf.Clamp(
                Mathf.RoundToInt(count * scale),
                1,
                short.MaxValue);
        }

        private readonly struct ParticleBaseline
        {
            public ParticleBaseline(
                float rateOverTimeMultiplier,
                float rateOverDistanceMultiplier,
                ParticleSystem.Burst[] bursts)
            {
                RateOverTimeMultiplier = rateOverTimeMultiplier;
                RateOverDistanceMultiplier = rateOverDistanceMultiplier;
                Bursts = bursts ?? Array.Empty<ParticleSystem.Burst>();
            }

            public float RateOverTimeMultiplier { get; }
            public float RateOverDistanceMultiplier { get; }
            public ParticleSystem.Burst[] Bursts { get; }
        }
    }
}
