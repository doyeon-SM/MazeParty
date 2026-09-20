using System;

namespace MazeParty.Gameplay
{
    public enum ItemChoiceResolution
    {
        NotStarted,
        Pending,
        ItemSelected,
        DoNotUse,
        TimedOut
    }

    /// <summary>
    /// Network-agnostic timing model for the action phase.
    /// The shared 180-second action clock, personal 30-second item choice, and
    /// opening five-second item-damage protection all use the same start timestamp.
    /// </summary>
    public sealed class GameplayPhaseClock
    {
        public const double DefaultActionDurationSeconds = 180d;
        public const double DefaultChoiceDurationSeconds = 30d;
        public const double DefaultOpeningProtectionSeconds = 5d;

        private readonly double _actionDuration;
        private readonly double _choiceDuration;
        private readonly double _openingProtectionDuration;

        public GameplayPhaseClock(
            double actionDuration = DefaultActionDurationSeconds,
            double choiceDuration = DefaultChoiceDurationSeconds,
            double openingProtectionDuration = DefaultOpeningProtectionSeconds)
        {
            if (actionDuration <= 0d)
                throw new ArgumentOutOfRangeException(nameof(actionDuration));
            if (choiceDuration <= 0d || choiceDuration > actionDuration)
                throw new ArgumentOutOfRangeException(nameof(choiceDuration));
            if (openingProtectionDuration < 0d || openingProtectionDuration > actionDuration)
                throw new ArgumentOutOfRangeException(nameof(openingProtectionDuration));

            _actionDuration = actionDuration;
            _choiceDuration = choiceDuration;
            _openingProtectionDuration = openingProtectionDuration;
        }

        public bool IsRunning { get; private set; }
        public double StartedAt { get; private set; }
        public ItemChoiceResolution ChoiceResolution { get; private set; } = ItemChoiceResolution.NotStarted;
        public int SelectedSlotIndex { get; private set; } = -1;
        public bool IsChoicePending => ChoiceResolution == ItemChoiceResolution.Pending;

        public void Start(double synchronizedNow)
        {
            IsRunning = true;
            StartedAt = synchronizedNow;
            ChoiceResolution = ItemChoiceResolution.Pending;
            SelectedSlotIndex = -1;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void Tick(double synchronizedNow)
        {
            if (IsChoicePending && synchronizedNow >= StartedAt + _choiceDuration)
            {
                ChoiceResolution = ItemChoiceResolution.TimedOut;
                SelectedSlotIndex = -1;
            }
        }

        public bool TrySelectItem(int slotIndex, double synchronizedNow)
        {
            if (slotIndex < 0 || !CanResolveChoice(synchronizedNow))
                return false;

            ChoiceResolution = ItemChoiceResolution.ItemSelected;
            SelectedSlotIndex = slotIndex;
            return true;
        }

        public bool TryChooseNoItem(double synchronizedNow)
        {
            if (!CanResolveChoice(synchronizedNow))
                return false;

            ChoiceResolution = ItemChoiceResolution.DoNotUse;
            SelectedSlotIndex = -1;
            return true;
        }

        public bool IsOpeningProtectionActive(double synchronizedNow)
        {
            return IsRunning
                   && synchronizedNow >= StartedAt
                   && synchronizedNow < StartedAt + _openingProtectionDuration;
        }

        public bool IsActionExpired(double synchronizedNow)
        {
            return IsRunning && synchronizedNow >= StartedAt + _actionDuration;
        }

        public double GetActionRemaining(double synchronizedNow)
        {
            return IsRunning ? Math.Max(0d, StartedAt + _actionDuration - synchronizedNow) : 0d;
        }

        public double GetChoiceRemaining(double synchronizedNow)
        {
            return IsChoicePending ? Math.Max(0d, StartedAt + _choiceDuration - synchronizedNow) : 0d;
        }

        public double GetOpeningProtectionRemaining(double synchronizedNow)
        {
            return IsOpeningProtectionActive(synchronizedNow)
                ? Math.Max(0d, StartedAt + _openingProtectionDuration - synchronizedNow)
                : 0d;
        }

        private bool CanResolveChoice(double synchronizedNow)
        {
            if (!IsRunning || !IsChoicePending)
                return false;

            if (synchronizedNow >= StartedAt + _choiceDuration)
            {
                Tick(synchronizedNow);
                return false;
            }

            return true;
        }

        // Online/Steam integration seam:
        // the host/server supplies one authoritative synchronized timestamp to Start().
        // Transport and lobby providers must not add a second local timer or delay the
        // shared action clock while another player's personal item choice is pending.
    }
}
