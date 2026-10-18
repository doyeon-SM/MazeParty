using MazeParty.Gameplay;

namespace MazeParty.Multiplayer
{
    /// <summary>One shared, seeded choice per replicated damage-tile presentation.</summary>
    public sealed class BoardDamageImpactPlayback
    {
        private int _revision = -1;
        private bool _played;
        public void Reset() { _revision = -1; _played = false; }

        public bool TryTake(BoardLandingEffectFeedbackSnapshot snapshot,
            BoardLandingEffectType effect, BoardFlowState flow, bool paused,
            int seed, double now, out int choice)
        {
            choice = -1;
            if (_revision != snapshot.Revision)
            {
                _revision = snapshot.Revision;
                _played = false;
            }
            if (_played || paused || flow != BoardFlowState.LandingEffectResolve ||
                snapshot.FeedbackKind != BoardLandingEffectFeedbackKind.Health ||
                snapshot.SignedAmount >= 0 ||
                (effect != BoardLandingEffectType.Damage20 && effect != BoardLandingEffectType.Damage40) ||
                !BoardLandingEffectFeedbackRules.IsEffectVisible(snapshot, now)) return false;
            _played = true;
            choice = new System.Random(unchecked(seed * 397 ^ snapshot.Revision * 31 ^ snapshot.Slot)).Next(3);
            return true;
        }
    }
}
