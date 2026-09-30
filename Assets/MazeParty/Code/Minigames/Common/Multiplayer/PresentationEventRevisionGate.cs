namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Baselines replicated presentation revisions before allowing one-shot
    /// playback. Late joiners and re-enabled additive scenes therefore restore
    /// persistent state without replaying historical effects.
    /// </summary>
    public struct PresentationEventRevisionGate
    {
        private bool _hasBaseline;
        private uint _lastRevision;

        public bool HasBaseline => _hasBaseline;
        public uint LastRevision => _lastRevision;

        public bool Observe(uint revision)
        {
            if (!_hasBaseline)
            {
                _hasBaseline = true;
                _lastRevision = revision;
                return false;
            }

            if (_lastRevision == revision)
            {
                return false;
            }

            _lastRevision = revision;
            return revision != 0U;
        }

        public void Reset()
        {
            _hasBaseline = false;
            _lastRevision = 0U;
        }
    }
}
