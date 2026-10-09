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
        Event
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
        public byte ItemId;
        public double StartedAt;

        public BoardLandingEffectFeedbackKind FeedbackKind =>
            (BoardLandingEffectFeedbackKind)Kind;
        public PrototypeItemId RewardItem => (PrototypeItemId)ItemId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Active);
            serializer.SerializeValue(ref Revision);
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref SignedAmount);
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref StartedAt);
        }

        public bool Equals(BoardLandingEffectFeedbackSnapshot other)
        {
            return Active == other.Active &&
                   Revision == other.Revision &&
                   Slot == other.Slot &&
                   Kind == other.Kind &&
                   SignedAmount == other.SignedAmount &&
                   ItemId == other.ItemId &&
                   StartedAt.Equals(other.StartedAt);
        }

        public override bool Equals(object obj)
        {
            return obj is BoardLandingEffectFeedbackSnapshot other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                Active,
                Revision,
                Slot,
                Kind,
                SignedAmount,
                ItemId,
                StartedAt);
        }
    }

    public static class BoardLandingEffectFeedbackRules
    {
        public const double DurationSeconds = 1d;

        public static bool IsVisible(
            BoardLandingEffectFeedbackSnapshot snapshot,
            double synchronizedNow)
        {
            return IsValid(snapshot) &&
                   synchronizedNow < snapshot.StartedAt + DurationSeconds;
        }

        public static double GetRemainingSeconds(
            BoardLandingEffectFeedbackSnapshot snapshot,
            double synchronizedNow)
        {
            return IsValid(snapshot)
                ? Math.Max(
                    0d,
                    snapshot.StartedAt + DurationSeconds - synchronizedNow)
                : 0d;
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
                    // Both healing and damage use the map's health icon; the
                    // signed value and authored color communicate direction.
                    iconKind = BoardMapIconKind.Healing;
                    label = FormatSigned(snapshot.SignedAmount);
                    return true;
                case BoardLandingEffectFeedbackKind.Item:
                    if (!PrototypeItemCatalog.IsValid(snapshot.RewardItem))
                    {
                        return false;
                    }

                    iconKind = BoardMapIconKind.Item;
                    label = "+ " + GameText.T(
                        PrototypeItemCatalog.Get(snapshot.RewardItem).DisplayName);
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

        private static string FormatSigned(int amount)
        {
            return (amount > 0 ? "+" : string.Empty) +
                   amount.ToString(CultureInfo.InvariantCulture);
        }
    }
}
