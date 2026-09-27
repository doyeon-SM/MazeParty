using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Scales one AudioSource by its settings channel (effects or music).
    /// Every AudioSource that plays game sound gets exactly one of these.
    /// <see cref="baseVolume"/> is the designer volume; the effective volume is
    /// baseVolume × channel volume, and the master volume is applied by the
    /// AudioListener. A GameObject with several AudioSources carries one
    /// component per source with <see cref="source"/> assigned.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioChannelSource : MonoBehaviour
    {
        [SerializeField] private AudioSource source;
        [SerializeField] private AudioChannel channel = AudioChannel.Sfx;
        [SerializeField, Range(0f, 1f)] private float baseVolume = 1f;

        public AudioSource Source => source != null ? source : GetComponent<AudioSource>();
        public AudioChannel Channel => channel;
        public float BaseVolume => baseVolume;

        public void Configure(
            AudioSource target,
            AudioChannel configuredChannel,
            float configuredBaseVolume)
        {
            source = target;
            channel = configuredChannel;
            baseVolume = Mathf.Clamp01(configuredBaseVolume);
            if (Application.isPlaying)
            {
                Apply();
            }
        }

        private void Reset()
        {
            source = GetComponent<AudioSource>();
            baseVolume = source != null ? source.volume : 1f;
        }

        private void OnEnable()
        {
            GameAudio.VolumesChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            GameAudio.VolumesChanged -= Apply;
        }

        private void Apply()
        {
            var target = Source;
            if (target != null)
            {
                target.volume = baseVolume * GameAudio.GetChannelVolume(channel);
            }
        }
    }
}
