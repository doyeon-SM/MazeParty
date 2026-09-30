using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public sealed class BoardBoundaryWallVisual : MonoBehaviour
    {
        public const float RequiredFlameTopHeight = 1f;

        [SerializeField] private BoxCollider blockingCollider;
        [SerializeField] private Transform flameWidthRoot;
        [SerializeField] private GameObject passableFlameRoot;
        [SerializeField] private GameObject blockedFlameRoot;
        [SerializeField] private Renderer[] stateRenderers;
        [SerializeField] private ParticleSystem[] stateParticles;
        [SerializeField] private Color passableColor = new Color(.28f, .78f, 1f, .48f);
        [SerializeField] private Color blockedColor = new Color(1f, .12f, .12f, .55f);
        [SerializeField, Min(.01f)] private float authoredFlameWidth = 1f;
        [SerializeField, Min(.1f)] private float flameTopHeight = RequiredFlameTopHeight;
        [SerializeField, Range(.05f, 1f)] private float passableEmissionDensity = .45f;
        [SerializeField, Min(1f)] private float blockedEmissionDensity = 2f;

        private MaterialPropertyBlock _properties;
        private Renderer[] _allRenderers;
        private ParticleEmissionBaseline[] _authoredEmissions;
        private ParticleSystem.Particle[][] _particleBuffers;
        private float _configuredWidth = 1f;
        private float _lastAccessibilityScale = -1f;
        private bool _visible = true;

        public BoxCollider BlockingCollider => blockingCollider;
        public Transform FlameWidthRoot => flameWidthRoot;
        public GameObject PassableFlameRoot => passableFlameRoot;
        public GameObject BlockedFlameRoot => blockedFlameRoot;
        public float FlameTopHeight => flameTopHeight;
        public float PassableEmissionDensity => passableEmissionDensity;
        public float BlockedEmissionDensity => blockedEmissionDensity;
        public bool HasStateDensityContrast =>
            passableEmissionDensity > 0f &&
            blockedEmissionDensity > passableEmissionDensity;
        public int StateParticleCount => stateParticles?.Length ?? 0;
        public bool HasRequiredReferences => blockingCollider != null && stateRenderers != null &&
            stateRenderers.Length > 0 &&
            System.Array.TrueForAll(stateRenderers, renderer => renderer != null) &&
            stateParticles != null && stateParticles.Length > 0 &&
            System.Array.TrueForAll(stateParticles, particle => particle != null) &&
            flameWidthRoot != null && passableFlameRoot != null &&
            blockedFlameRoot != null &&
            passableFlameRoot.transform.IsChildOf(flameWidthRoot) &&
            blockedFlameRoot.transform.IsChildOf(flameWidthRoot);

        private void Awake()
        {
            _configuredWidth = Mathf.Max(.01f, authoredFlameWidth);
            RefreshEmissionDensity(_configuredWidth);
        }

        /// <summary>
        /// Sizes the physical wall without scaling its transform, then stretches only
        /// particle emission shapes across the gate. This keeps the existing owner-only
        /// collider volume unchanged while particle sprites remain undistorted.
        /// </summary>
        public void ConfigureLayout(float width, float colliderHeight, float thickness)
        {
            width = Mathf.Max(.01f, width);
            colliderHeight = Mathf.Max(.01f, colliderHeight);
            thickness = Mathf.Max(.01f, thickness);

            blockingCollider.center = Vector3.up * (colliderHeight * .5f);
            blockingCollider.size = new Vector3(width, colliderHeight, thickness);
            _configuredWidth = width;
            flameWidthRoot.localPosition = Vector3.zero;
            flameWidthRoot.localScale = new Vector3(
                width / Mathf.Max(.01f, authoredFlameWidth),
                1f,
                1f);
            RefreshEmissionDensity(width);
        }

        private void Update()
        {
            var accessibilityScale =
                PresentationAccessibility.FlashIntensityScale;
            if (!Mathf.Approximately(
                    accessibilityScale,
                    _lastAccessibilityScale))
            {
                RefreshEmissionDensity(_configuredWidth);
            }
        }

        private void LateUpdate()
        {
            ConstrainParticleEnvelopeNow();
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            // Includes decorative children so remote players' private walls stay invisible.
            _allRenderers ??= GetComponentsInChildren<Renderer>(true);
            RefreshRendererVisibility();
            RefreshParticlePlayback();
        }

        public void SetPassable(bool passable)
        {
            blockingCollider.enabled = !passable;
            passableFlameRoot.SetActive(passable);
            blockedFlameRoot.SetActive(!passable);
            _properties ??= new MaterialPropertyBlock();
            foreach (var renderer in stateRenderers)
            {
                renderer.GetPropertyBlock(_properties);
                _properties.SetColor("_BaseColor", passable ? passableColor : blockedColor);
                _properties.SetColor("_Color", passable ? passableColor : blockedColor);
                renderer.SetPropertyBlock(_properties);
            }

            ApplyParticleTint(
                passableFlameRoot,
                passableColor);
            ApplyParticleTint(
                blockedFlameRoot,
                blockedColor);
            RefreshRendererVisibility();
            RefreshParticlePlayback();
        }

        private void RefreshRendererVisibility()
        {
            if (_allRenderers == null)
                return;

            foreach (var renderer in _allRenderers)
            {
                if (renderer == null)
                    continue;

                if (!_visible)
                    renderer.enabled = false;
                else if (renderer.gameObject.activeInHierarchy)
                    renderer.enabled = true;
            }
        }

        private void RefreshEmissionDensity(float width)
        {
            if (stateParticles == null)
                return;

            CaptureEmissionBaselines();

            var widthMultiplier = Mathf.Clamp(
                width / Mathf.Max(.01f, authoredFlameWidth),
                1f,
                6f);
            var accessibilityMultiplier =
                PresentationAccessibility.FlashIntensityScale;
            _lastAccessibilityScale = accessibilityMultiplier;
            for (var index = 0; index < stateParticles.Length; index++)
            {
                var particle = stateParticles[index];
                if (particle == null)
                    continue;

                var baseline = _authoredEmissions[index];
                var stateMultiplier = ResolveStateDensity(particle);
                var emissionMultiplier = widthMultiplier *
                                         stateMultiplier *
                                         accessibilityMultiplier;
                var emission = particle.emission;
                emission.rateOverTimeMultiplier =
                    baseline.RateOverTimeMultiplier * emissionMultiplier;
                emission.rateOverDistanceMultiplier =
                    baseline.RateOverDistanceMultiplier * emissionMultiplier;
                for (var burstIndex = 0;
                     burstIndex < baseline.Bursts.Length;
                     burstIndex++)
                {
                    var burst = baseline.Bursts[burstIndex];
                    burst.minCount = ScaleBurstCount(
                        burst.minCount,
                        emissionMultiplier);
                    burst.maxCount = ScaleBurstCount(
                        burst.maxCount,
                        emissionMultiplier);
                    emission.SetBurst(burstIndex, burst);
                }
            }
        }

        /// <summary>
        /// Reapplies presentation-only density and accessibility settings. Public so
        /// settings screens and contract tests can refresh immediately without waiting
        /// for a frame; normal gameplay also refreshes automatically from Update.
        /// </summary>
        public void RefreshPresentationSettings()
        {
            RefreshEmissionDensity(_configuredWidth);
        }

        /// <summary>
        /// Keeps the complete rendered particle billboard below the authored one-metre
        /// curtain top. The clamp is evaluated in this wall's local space, so rotated
        /// board gates and every ParticleSystem simulation space share the same limit.
        /// </summary>
        public void ConstrainParticleEnvelopeNow()
        {
            if (stateParticles == null)
                return;

            if (_particleBuffers == null ||
                _particleBuffers.Length != stateParticles.Length)
            {
                _particleBuffers =
                    new ParticleSystem.Particle[stateParticles.Length][];
            }

            for (var index = 0; index < stateParticles.Length; index++)
            {
                var system = stateParticles[index];
                if (system == null || !system.gameObject.activeInHierarchy)
                    continue;

                var particleCount = system.particleCount;
                if (particleCount <= 0)
                    continue;

                var buffer = _particleBuffers[index];
                if (buffer == null || buffer.Length < particleCount)
                {
                    buffer = new ParticleSystem.Particle[
                        Mathf.NextPowerOfTwo(particleCount)];
                    _particleBuffers[index] = buffer;
                }

                var count = system.GetParticles(buffer);
                var changed = false;
                for (var particleIndex = 0;
                     particleIndex < count;
                     particleIndex++)
                {
                    var particle = buffer[particleIndex];
                    var worldPosition = ToWorldPosition(
                        system,
                        particle.position);
                    var localPosition =
                        transform.InverseTransformPoint(worldPosition);
                    var currentSize = particle.GetCurrentSize3D(system);
                    var worldRadius = currentSize.magnitude * .5f;
                    var localVerticalRadius = Mathf.Abs(
                        transform.InverseTransformVector(
                            transform.up * worldRadius).y);
                    var maximumCenterHeight =
                        flameTopHeight - localVerticalRadius;
                    if (localPosition.y <= maximumCenterHeight)
                        continue;

                    localPosition.y = maximumCenterHeight;
                    particle.position = FromWorldPosition(
                        system,
                        transform.TransformPoint(localPosition));
                    buffer[particleIndex] = particle;
                    changed = true;
                }

                if (changed)
                    system.SetParticles(buffer, count);
            }
        }

        private void CaptureEmissionBaselines()
        {
            if (_authoredEmissions != null &&
                _authoredEmissions.Length == stateParticles.Length)
            {
                return;
            }

            _authoredEmissions =
                new ParticleEmissionBaseline[stateParticles.Length];
            for (var index = 0; index < stateParticles.Length; index++)
            {
                var particle = stateParticles[index];
                if (particle == null)
                    continue;

                var emission = particle.emission;
                var bursts = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(bursts);
                _authoredEmissions[index] = new ParticleEmissionBaseline(
                    emission.rateOverTimeMultiplier,
                    emission.rateOverDistanceMultiplier,
                    bursts);
            }
        }

        private float ResolveStateDensity(ParticleSystem particle)
        {
            if (passableFlameRoot != null &&
                particle.transform.IsChildOf(passableFlameRoot.transform))
            {
                return passableEmissionDensity;
            }

            return blockedEmissionDensity;
        }

        private static short ScaleBurstCount(short count, float scale)
        {
            return (short)Mathf.Clamp(
                Mathf.RoundToInt(count * Mathf.Max(0f, scale)),
                0,
                short.MaxValue);
        }

        private static Vector3 ToWorldPosition(
            ParticleSystem system,
            Vector3 position)
        {
            var main = system.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.World:
                    return position;
                case ParticleSystemSimulationSpace.Custom:
                    return main.customSimulationSpace != null
                        ? main.customSimulationSpace.TransformPoint(position)
                        : system.transform.TransformPoint(position);
                default:
                    return system.transform.TransformPoint(position);
            }
        }

        private static Vector3 FromWorldPosition(
            ParticleSystem system,
            Vector3 position)
        {
            var main = system.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.World:
                    return position;
                case ParticleSystemSimulationSpace.Custom:
                    return main.customSimulationSpace != null
                        ? main.customSimulationSpace.InverseTransformPoint(position)
                        : system.transform.InverseTransformPoint(position);
                default:
                    return system.transform.InverseTransformPoint(position);
            }
        }

        private void ApplyParticleTint(GameObject root, Color color)
        {
            if (root == null || stateParticles == null)
                return;

            foreach (var particle in stateParticles)
            {
                if (particle == null || !particle.transform.IsChildOf(root.transform))
                    continue;

                var main = particle.main;
                main.startColor = color;
            }
        }

        private void RefreshParticlePlayback()
        {
            if (stateParticles == null)
                return;

            foreach (var particle in stateParticles)
            {
                if (particle == null)
                    continue;

                if (_visible && particle.gameObject.activeInHierarchy)
                {
                    if (!particle.isPlaying)
                        particle.Play(true);
                }
                else
                {
                    particle.Stop(
                        true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private readonly struct ParticleEmissionBaseline
        {
            public ParticleEmissionBaseline(
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
