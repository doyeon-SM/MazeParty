using MazeParty.Gameplay.Minigames;
using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Operations every registered minigame runtime must support. Optional
    /// presentation, reconnect and input features live in capability interfaces.
    /// </summary>
    internal interface IMinigameRuntimeAdapter
    {
        ScheduledMinigameId Id { get; }
        NetworkBehaviour Instance { get; }
        bool IsSpawned { get; }
        bool UsesRoundSeriesResults { get; }
        MinigameResultRow GetResultRow(int slot);
        bool TryGetClock(double runningSeconds, out double remaining, out double total, out int round);

        bool CanAcceptInputForSlot(int slot);
        bool TryGetRoundCountdown(out double remainingSeconds);
        void BeginMatchOnServer(ulong matchSeed);
        void PauseOnServer(double now);
        void ResumeOnServer(double now);
        void EndMatchOnServer();
    }

    internal abstract class MinigameRuntimeAdapter<TState> :
        IMinigameRuntimeAdapter
        where TState : NetworkBehaviour
    {
        public abstract ScheduledMinigameId Id { get; }

        protected abstract TState CurrentState { get; }

        public NetworkBehaviour Instance => CurrentState;

        public bool IsSpawned
        {
            get
            {
                var state = CurrentState;
                return state != null && state.IsSpawned;
            }
        }

        public virtual bool UsesRoundSeriesResults => false;
        public abstract MinigameResultRow GetResultRow(int slot);
        public abstract bool TryGetClock(double runningSeconds,
            out double remaining, out double total, out int round);

        public abstract bool CanAcceptInputForSlot(int slot);

        public abstract bool TryGetRoundCountdown(
            out double remainingSeconds);

        public abstract void BeginMatchOnServer(ulong matchSeed);

        public abstract void PauseOnServer(double now);

        public abstract void ResumeOnServer(double now);

        public abstract void EndMatchOnServer();
    }
}
