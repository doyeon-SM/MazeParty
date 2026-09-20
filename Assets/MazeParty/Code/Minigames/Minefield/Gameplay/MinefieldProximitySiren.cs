using System;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames.Minefield
{
    /// <summary>
    /// Head-mounted warning lamp. The nearest armed mine controls both its red lens
    /// color and optional point-light intensity; no armed mine means fully off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinefieldProximitySiren : MonoBehaviour
    {
        [SerializeField] private MinefieldPlayerActor playerActor;
        [SerializeField] private MinefieldMineRegistry registry;
        [SerializeField] private Transform sensorOrigin;
        [SerializeField] private Renderer lampRenderer;
        [SerializeField] private Light lampLight;
        [SerializeField, Min(0.01f)] private float maximumWarningDistance = 5f;
        [SerializeField, Min(0.01f)] private float responseExponent = 1f;
        [SerializeField, Min(0f)] private float peakLightIntensity = 3f;
        [SerializeField, Min(0f)] private float peakEmissionIntensity = 3f;
        [SerializeField] private Color offColor = Color.black;
        [SerializeField] private Color warningColor = Color.red;

        private MaterialPropertyBlock _propertyBlock;
        private float _normalizedIntensity = -1f;

        public event Action<MinefieldProximitySiren, float> IntensityChanged;

        public float NormalizedIntensity => Mathf.Max(0f, _normalizedIntensity);
        public float NearestMineDistance { get; private set; } =
            float.PositiveInfinity;

        private void Awake()
        {
            ResolveReferences();
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        private void OnDisable()
        {
            ApplyNormalizedIntensity(0f);
        }

        public void Configure(
            MinefieldPlayerActor actor,
            MinefieldMineRegistry mineRegistry,
            Transform origin,
            Renderer lensRenderer,
            Light warningLight)
        {
            playerActor = actor;
            registry = mineRegistry;
            sensorOrigin = origin;
            lampRenderer = lensRenderer;
            lampLight = warningLight;
            Refresh();
        }

        public void ConfigureWarning(
            float warningDistance,
            float intensityExponent,
            float maximumLightIntensity,
            float maximumEmissionIntensity)
        {
            maximumWarningDistance = Mathf.Max(0.01f, warningDistance);
            responseExponent = Mathf.Max(0.01f, intensityExponent);
            peakLightIntensity = Mathf.Max(0f, maximumLightIntensity);
            peakEmissionIntensity = Mathf.Max(0f, maximumEmissionIntensity);
            Refresh();
        }
        public void Refresh()
        {
            ResolveReferences();
            if (playerActor != null && !playerActor.CanMove)
            {
                NearestMineDistance = float.PositiveInfinity;
                ApplyNormalizedIntensity(0f);
                return;
            }

            var origin = sensorOrigin != null
                ? sensorOrigin.position
                : transform.position;
            if (registry == null ||
                !registry.TryGetNearestArmedMine(
                    origin,
                    out _,
                    out var nearestDistance))
            {
                NearestMineDistance = float.PositiveInfinity;
                ApplyNormalizedIntensity(0f);
                return;
            }

            NearestMineDistance = nearestDistance;
            var linearIntensity = CalculateNormalizedIntensity(
                nearestDistance,
                maximumWarningDistance);
            ApplyNormalizedIntensity(
                Mathf.Pow(linearIntensity, Mathf.Max(0.01f, responseExponent)));
        }

        public void ApplyNormalizedIntensity(float normalizedIntensity)
        {
            var value = Mathf.Clamp01(normalizedIntensity);
            if (Mathf.Approximately(_normalizedIntensity, value))
            {
                return;
            }

            _normalizedIntensity = value;
            var lensColor = Color.Lerp(offColor, warningColor, value);
            if (lampRenderer != null)
            {
                if (_propertyBlock == null)
                {
                    _propertyBlock = new MaterialPropertyBlock();
                }

                lampRenderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor("_BaseColor", lensColor);
                _propertyBlock.SetColor("_Color", lensColor);
                _propertyBlock.SetColor(
                    "_EmissionColor",
                    warningColor * (value * peakEmissionIntensity));
                lampRenderer.SetPropertyBlock(_propertyBlock);
            }

            if (lampLight != null)
            {
                lampLight.color = warningColor;
                lampLight.intensity = value * peakLightIntensity;
                lampLight.enabled = value > 0.001f;
            }

            IntensityChanged?.Invoke(this, value);
        }

        public static float CalculateNormalizedIntensity(
            float planarDistance,
            float warningDistance)
        {
            if (float.IsInfinity(planarDistance) || warningDistance <= 0f)
            {
                return 0f;
            }

            return 1f - Mathf.Clamp01(
                Mathf.Max(0f, planarDistance) / warningDistance);
        }

        private void ResolveReferences()
        {
            if (playerActor == null)
            {
                playerActor = GetComponentInParent<MinefieldPlayerActor>();
            }
            if (sensorOrigin == null)
            {
                sensorOrigin = transform;
            }
            if (lampRenderer == null)
            {
                lampRenderer = GetComponent<Renderer>();
            }
            if (lampLight == null)
            {
                lampLight = GetComponent<Light>();
            }
            if (registry == null)
            {
                registry = GetComponentInParent<MinefieldMineRegistry>();
            }
        }
    }
}
