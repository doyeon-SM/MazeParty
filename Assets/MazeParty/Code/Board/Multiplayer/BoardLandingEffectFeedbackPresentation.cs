using System;
using System.Globalization;
using MazeParty.Gameplay;
using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    public enum BoardLandingEffectFeedbackKind : byte
    {
        None,
        Gold,
        Health,
        Item,
        Event,
        EventIntro
    }

    [Serializable]
    public struct BoardLandingEffectFeedbackSnapshot :
        INetworkSerializable,
        IEquatable<BoardLandingEffectFeedbackSnapshot>
    {
        public bool Active;
        public int Revision;
        public int Slot;
        public byte Kind;
        public int SignedAmount;
        public byte AffectedMask;
        public byte ItemId;
        public byte Resource;
        public double StartedAt;

        public BoardLandingEffectFeedbackKind FeedbackKind =>
            (BoardLandingEffectFeedbackKind)Kind;
        public PrototypeItemId RewardItem => (PrototypeItemId)ItemId;
        public BoardSpecialEventResource ResourceKind => (BoardSpecialEventResource)Resource;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Active);
            serializer.SerializeValue(ref Revision);
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref SignedAmount);
            serializer.SerializeValue(ref AffectedMask);
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref Resource);
            serializer.SerializeValue(ref StartedAt);
        }

        public bool Equals(BoardLandingEffectFeedbackSnapshot other)
        {
            return Active == other.Active &&
                   Revision == other.Revision &&
                   Slot == other.Slot &&
                   Kind == other.Kind &&
                   SignedAmount == other.SignedAmount &&
                   AffectedMask == other.AffectedMask &&
                   ItemId == other.ItemId && Resource == other.Resource &&
                   StartedAt.Equals(other.StartedAt);
        }

        public override bool Equals(object obj)
        {
            return obj is BoardLandingEffectFeedbackSnapshot other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Resource, HashCode.Combine(
                Active,
                Revision,
                Slot,
                Kind,
                SignedAmount,
                AffectedMask,
                ItemId,
                StartedAt));
        }
    }

    public static class BoardLandingEffectFeedbackRules
    {
        public const double DurationSeconds = BoardEffectShotTiming.Duration;
        public const double EventDurationSeconds =
            BoardLandingEffectLayout.SpecialEventResultDurationSeconds;

        public static double GetDurationSeconds(BoardLandingEffectFeedbackKind kind)
        {
            switch (kind)
            {
                case BoardLandingEffectFeedbackKind.Event:
                    return EventDurationSeconds;
                case BoardLandingEffectFeedbackKind.Gold:
                case BoardLandingEffectFeedbackKind.Item:
                    return BoardLandingEffectLayout.ResourceEffectDurationSeconds;
                default:
                    return DurationSeconds;
            }
        }

        public static bool IsVisible(
            BoardLandingEffectFeedbackSnapshot snapshot,
            double synchronizedNow)
        {
            return IsValid(snapshot) &&
                   synchronizedNow < snapshot.StartedAt +
                   GetDurationSeconds(snapshot);
        }

        public static double GetRemainingSeconds(
            BoardLandingEffectFeedbackSnapshot snapshot,
            double synchronizedNow)
        {
            return IsValid(snapshot)
                ? Math.Max(
                    0d,
                     snapshot.StartedAt +
                     GetDurationSeconds(snapshot) - synchronizedNow)
                : 0d;
        }

        public static double GetEventDuration(byte affectedMask) =>
            EventDurationSeconds + (affectedMask != 0 ? BoardEffectShotTiming.Duration : 0d);

        public static double GetDurationSeconds(BoardLandingEffectFeedbackSnapshot snapshot) =>
            snapshot.FeedbackKind == BoardLandingEffectFeedbackKind.Event
                ? GetEventDuration(snapshot.AffectedMask) : GetDurationSeconds(snapshot.FeedbackKind);

        public static double GetShotElapsed(BoardLandingEffectFeedbackSnapshot snapshot, double now) =>
            now - snapshot.StartedAt - (snapshot.FeedbackKind == BoardLandingEffectFeedbackKind.Event
                ? EventDurationSeconds : 0d);

        public static byte GetEventFocusMask(BoardLandingEffectFeedbackSnapshot snapshot, double now) =>
            snapshot.FeedbackKind == BoardLandingEffectFeedbackKind.Event &&
            IsVisible(snapshot, now) && GetShotElapsed(snapshot, now) >= 0d
                ? snapshot.AffectedMask : (byte)0;

        public static bool IsEffectVisible(BoardLandingEffectFeedbackSnapshot snapshot, double now) =>
            IsVisible(snapshot, now) &&
            (snapshot.FeedbackKind != BoardLandingEffectFeedbackKind.Event ||
             GetEventFocusMask(snapshot, now) != 0) &&
            BoardEffectShotTiming.IsEffectVisible(GetShotElapsed(snapshot, now));

        public static double GetEffectRemainingSeconds(
            BoardLandingEffectFeedbackSnapshot snapshot, double now) =>
            IsEffectVisible(snapshot, now)
                ? BoardEffectShotTiming.Lead + BoardEffectShotTiming.Motion -
                  GetShotElapsed(snapshot, now) : 0d;

        public static int GetEventFocusSlot(
            BoardLandingEffectFeedbackSnapshot snapshot, double synchronizedNow)
        {
            var mask = GetEventFocusMask(snapshot, synchronizedNow);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                if ((mask & (1 << slot)) != 0) return slot;
            return -1;
        }

        public static bool TryGetContent(
            BoardLandingEffectFeedbackSnapshot snapshot,
            out BoardMapIconKind iconKind,
            out string label)
        {
            iconKind = BoardMapIconKind.Healing;
            label = string.Empty;
            if (!IsValid(snapshot))
            {
                return false;
            }

            switch (snapshot.FeedbackKind)
            {
                case BoardLandingEffectFeedbackKind.Gold:
                    iconKind = snapshot.SignedAmount >= 0
                        ? BoardMapIconKind.GoldGain
                        : BoardMapIconKind.GoldLoss;
                    label = FormatSigned(snapshot.SignedAmount);
                    return true;
                case BoardLandingEffectFeedbackKind.Health:
                    // The map kind selects the authored text palette only.
                    iconKind = BoardMapIconKind.Healing;
                    label = FormatSigned(snapshot.SignedAmount);
                    return true;
                case BoardLandingEffectFeedbackKind.Item:
                    if (!PrototypeItemCatalog.IsValid(snapshot.RewardItem))
                    {
                        return false;
                    }

                    iconKind = BoardMapIconKind.Item;
                    label = snapshot.SignedAmount > 1
                        ? GameText.F("ITEMS ACQUIRED +{0}", snapshot.SignedAmount)
                        : GameText.T("ITEM ACQUIRED");
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsValid(BoardLandingEffectFeedbackSnapshot snapshot)
        {
            return snapshot.Active &&
                   snapshot.Revision > 0 &&
                   snapshot.Slot >= 0 &&
                   snapshot.Slot < MultiplayerConstants.MaxPlayers &&
                   Enum.IsDefined(
                       typeof(BoardLandingEffectFeedbackKind),
                       snapshot.FeedbackKind) &&
                   snapshot.FeedbackKind != BoardLandingEffectFeedbackKind.None;
        }

        public static bool TryGetForestGhostContent(BoardForestGhostSnapshot snapshot, double now,
            out BoardMapIconKind iconKind, out string label, out float remaining)
        {
            iconKind = BoardMapIconKind.Item;
            label = string.Empty;
            remaining = (float)Math.Max(0d, BoardForestGhostRules.Duration - snapshot.Elapsed(now));
            if (!snapshot.Active || remaining <= 0f) return false;
            if (snapshot.IsIntro)
            {
                label = GameText.T("LET THE MISCHIEF BEGIN!");
                return true;
            }
            if (!snapshot.Applied || snapshot.Slot < 0 || snapshot.Slot >= MultiplayerConstants.MaxPlayers)
                return false;
            switch ((ForestGhostResource)snapshot.Resource)
            {
                case ForestGhostResource.Gold:
                case ForestGhostResource.Key:
                    iconKind = snapshot.ActualAmount < 0 ? BoardMapIconKind.GoldLoss : BoardMapIconKind.GoldGain;
                    break;
                case ForestGhostResource.Health:
                    iconKind = BoardMapIconKind.Healing;
                    break;
                default: return false;
            }
            label = FormatSigned(snapshot.ActualAmount);
            return true;
        }

        private static string FormatSigned(int amount)
        {
            return (amount > 0 ? "+" : string.Empty) +
                   amount.ToString(CultureInfo.InvariantCulture);
        }
    }
}
