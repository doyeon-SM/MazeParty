using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Presents an authoritative footstep: plays <see cref="SoundKeys.BoardFootstep"/>
    /// at the step position, audible only within the footstep radius (quiet
    /// walking is shorter), and reports the presentation for debug gizmos.
    /// Clips and variations live on the footstep SoundCue.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FootstepAudioEmitter : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
        [SerializeField] private bool drawLastAudibleRadius = true;
        [SerializeField, Min(0.05f)] private float debugRadiusDuration = 0.4f;

        private float _debugRadiusExpiresAt;
        private Vector3 _lastWorldPosition;

        public event Action<FootstepPresentation> FootstepPresented;

        public int PresentedCount { get; private set; }
        public float LastAudibleRadius { get; private set; }
        public bool LastWasQuietWalk { get; private set; }
        public bool LastWasAudibleToLocalListener { get; private set; }

        public void PresentFootstep(
            Vector3 authoritativeWorldPosition,
            bool quietWalking)
        {
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

            if (audible)
            {
                GameSound.PlayAt(
                    SoundKeys.BoardFootstep,
                    authoritativeWorldPosition,
                    volume,
                    radius);
            }

            FootstepPresented?.Invoke(new FootstepPresentation(
                authoritativeWorldPosition,
                radius,
                quietWalking,
                audible));
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
