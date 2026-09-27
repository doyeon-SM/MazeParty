using MazeParty.Gameplay;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Local board sounds from replicated state: turn start, minigame finish
    /// and the local player's gold, key and item changes. Driven by
    /// <see cref="BoardFlowView"/>; the first observation only primes the
    /// state so joining or reconnecting plays nothing.
    /// </summary>
    internal sealed class BoardSoundFeedback
    {
        private bool _flowPrimed;
        private BoardFlowState _flowState;
        private NetworkPlayerAvatar _walletAvatar;
        private int _gold;
        private int _keys;
        private int _items;

        public void Reset()
        {
            _flowPrimed = false;
            _walletAvatar = null;
        }

        public void Observe(NetworkMatchState match, NetworkPlayerAvatar localAvatar)
        {
            ObserveFlow(match);
            ObserveWallet(
                localAvatar,
                match.FlowState != BoardFlowState.MatchComplete);
        }

        private void ObserveFlow(NetworkMatchState match)
        {
            var current = match.FlowState;
            if (_flowPrimed)
            {
                var key = BoardFeedbackSoundRules.ClassifyFlowTransition(
                    _flowState,
                    current);
                if (key != null)
                {
                    GameSound.Play(key);
                }
            }

            _flowState = current;
            _flowPrimed = true;
        }

        private void ObserveWallet(NetworkPlayerAvatar avatar, bool enabled)
        {
            if (avatar == null || !avatar.IsSpawned)
            {
                _walletAvatar = null;
                return;
            }

            var gold = avatar.Gold;
            var keys = avatar.KeyCount;
            var items = avatar.OccupiedItemCount;
            if (_walletAvatar == avatar && enabled)
            {
                var key = BoardFeedbackSoundRules.ClassifyWalletChange(
                    _gold,
                    gold,
                    _keys,
                    keys,
                    _items,
                    items);
                if (key != null)
                {
                    GameSound.Play(key);
                }
            }

            _walletAvatar = avatar;
            _gold = gold;
            _keys = keys;
            _items = items;
        }
    }
}
