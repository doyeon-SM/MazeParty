using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public sealed class FootstepAudioEmitter : MonoBehaviour
    {
        [SerializeField] private AudioClip[] footstepClips = Array.Empty<AudioClip>();
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
        [SerializeField] private bool drawLastAudibleRadius = true;
        [SerializeField, Min(0.05f)] private float debugRadiusDuration = 0.4f;

        private AudioSource _source;
        private int _clipCursor;
        private float _debugRadiusExpiresAt;
        private Vector3 _lastWorldPosition;

        public event Action<FootstepPresentation> FootstepPresented;

        public int PresentedCount { get; private set; }
        public float LastAudibleRadius { get; private set; }
        public bool LastWasQuietWalk { get; private set; }
        public bool LastWasAudibleToLocalListener { get; private set; }

        private void Awake()
        {
            EnsureAudioSource();
        }

        public void PresentFootstep(
            Vector3 authoritativeWorldPosition,
            bool quietWalking)
        {
            EnsureAudioSource();
            var radius = FootstepRules.AudibleRadius(quietWalking);
            var audible = IsAudibleToLocalListener(
                authoritativeWorldPosition,
                radius);

            PresentedCount++;
            LastAudibleRadius = radius;
            LastWasQuietWalk = quietWalking;
            LastWasAudibleToLocalListener = audible;
            _lastWorldPosition = authoritativeWorldPosition;
            _debugRadiusExpiresAt = Time.unscaledTime + debugRadiusDuration;

            if (audible && TryGetNextClip(out var clip))
            {
                _source.maxDistance = radius;
                _source.PlayOneShot(clip, volume);
            }

            FootstepPresented?.Invoke(new FootstepPresentation(
                authoritativeWorldPosition,
                radius,
                quietWalking,
                audible));
        }

        private void EnsureAudioSource()
        {
            if (_source == null)
            {
                _source = GetComponent<AudioSource>();
            }
            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
            }

            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 0.5f;
        }

        private bool TryGetNextClip(out AudioClip clip)
        {
            if (footstepClips == null || footstepClips.Length == 0)
            {
                clip = null;
                return false;
            }

            for (var attempt = 0; attempt < footstepClips.Length; attempt++)
            {
                var index = _clipCursor++ % footstepClips.Length;
                if (footstepClips[index] != null)
                {
                    clip = footstepClips[index];
                    return true;
                }
            }

            clip = null;
            return false;
        }

        private static bool IsAudibleToLocalListener(
            Vector3 worldPosition,
            float radius)
        {
            var listener = FindAnyObjectByType<AudioListener>();
            return listener != null &&
                   Vector3.Distance(listener.transform.position, worldPosition) <= radius;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawLastAudibleRadius || !Application.isPlaying ||
                Time.unscaledTime > _debugRadiusExpiresAt ||
                LastAudibleRadius <= 0f)
            {
                return;
            }

            Gizmos.color = LastWasQuietWalk
                ? new Color(0.2f, 0.8f, 1f, 0.85f)
                : new Color(1f, 0.65f, 0.15f, 0.85f);
            Gizmos.DrawWireSphere(_lastWorldPosition, LastAudibleRadius);
        }
    }

    public readonly struct FootstepPresentation
    {
        public FootstepPresentation(
            Vector3 worldPosition,
            float audibleRadius,
            bool quietWalking,
            bool audibleToLocalListener)
        {
            WorldPosition = worldPosition;
            AudibleRadius = audibleRadius;
            QuietWalking = quietWalking;
            AudibleToLocalListener = audibleToLocalListener;
        }

        public Vector3 WorldPosition { get; }
        public float AudibleRadius { get; }
        public bool QuietWalking { get; }
        public bool AudibleToLocalListener { get; }
    }
}
