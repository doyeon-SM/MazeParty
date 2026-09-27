using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Key → <see cref="SoundCue"/> registry used by the sound system. A
    /// library can include other libraries (for example one per minigame);
    /// its own cues win over included ones with the same key.
    /// </summary>
    [CreateAssetMenu(menuName = "MazeParty/Audio/Sound Library", fileName = "SoundLibrary")]
    public sealed class SoundLibrary : ScriptableObject
    {
        [SerializeField] private SoundCue[] cues = Array.Empty<SoundCue>();
        [SerializeField] private SoundLibrary[] includes = Array.Empty<SoundLibrary>();

        private Dictionary<string, SoundCue> _lookup;

        public IReadOnlyList<SoundCue> Cues => cues ?? Array.Empty<SoundCue>();
        public IReadOnlyList<SoundLibrary> Includes => includes ?? Array.Empty<SoundLibrary>();

        public bool TryGet(string key, out SoundCue cue)
        {
            cue = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            EnsureLookup();
            return _lookup.TryGetValue(key, out cue) && cue != null;
        }

        /// <summary>Editor tooling: replaces the registered cues.</summary>
        public void SetCues(SoundCue[] value)
        {
            cues = value ?? Array.Empty<SoundCue>();
            _lookup = null;
        }

        private void OnEnable()
        {
            _lookup = null;
        }

        private void OnValidate()
        {
            _lookup = null;
        }

        private void EnsureLookup()
        {
            if (_lookup != null)
            {
                return;
            }

            _lookup = new Dictionary<string, SoundCue>(StringComparer.Ordinal);
            Collect(this, _lookup, new HashSet<SoundLibrary>());
        }

        private static void Collect(
            SoundLibrary library,
            Dictionary<string, SoundCue> lookup,
            HashSet<SoundLibrary> visited)
        {
            if (library == null || !visited.Add(library))
            {
                return;
            }

            foreach (var cue in library.Cues)
            {
                if (cue != null &&
                    !string.IsNullOrEmpty(cue.Key) &&
                    !lookup.ContainsKey(cue.Key))
                {
                    lookup.Add(cue.Key, cue);
                }
            }

            foreach (var included in library.Includes)
            {
                Collect(included, lookup, visited);
            }
        }
    }
}
