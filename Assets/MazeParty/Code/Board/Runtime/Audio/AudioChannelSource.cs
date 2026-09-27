using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Routes one scene-authored AudioSource to its channel (effects, music or
    /// UI). Every AudioSource that plays game sound gets exactly one of these.
    /// With the sound system's AudioMixer the source outputs to the channel's
    /// mixer group and keeps <see cref="baseVolume"/>; the mixer applies the
    /// settings sliders. Without a mixer the source is scaled by the channel
    /// volume instead. A GameObject with several AudioSources carries one
    /// component per source with <see cref="source"/> assigned. Sounds played
    /// through <c>GameSound</c> do not need this component.
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
            GameAudio.RoutingChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            GameAudio.VolumesChanged -= Apply;
            GameAudio.RoutingChanged -= Apply;
        }

        private void Apply()
        {
            var target = Source;
            if (target == null)
            {
                return;
            }

            var group = GameAudio.GetOutputGroup(channel);
            if (group != null)
            {
                target.outputAudioMixerGroup = group;
                target.volume = baseVolume;
                return;
            }

            target.volume = baseVolume * GameAudio.GetChannelVolume(channel);
        }
    }
}
