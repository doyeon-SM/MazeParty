using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        private enum BoardWorldInteractionKind : byte
        {
            None,
            Tombstone,
            KeyShop,
            ItemShop
        }

        private readonly struct BoardWorldInteractionTarget
        {
            private BoardWorldInteractionTarget(
                BoardWorldInteractionKind kind,
                int targetIndex,
                int gold)
            {
                Kind = kind;
                TargetIndex = targetIndex;
                Gold = gold;
            }

            public BoardWorldInteractionKind Kind { get; }
            public int TargetIndex { get; }
            public int Gold { get; }

            public static BoardWorldInteractionTarget Tombstone(
                int tombstoneId,
                int gold)
            {
                return new BoardWorldInteractionTarget(
                    BoardWorldInteractionKind.Tombstone,
                    tombstoneId,
                    Mathf.Max(0, gold));
            }

            public static BoardWorldInteractionTarget KeyShop()
            {
                return new BoardWorldInteractionTarget(
                    BoardWorldInteractionKind.KeyShop,
                    -1,
                    0);
            }

            public static BoardWorldInteractionTarget ItemShop(int shopIndex)
            {
                return new BoardWorldInteractionTarget(
                    BoardWorldInteractionKind.ItemShop,
                    shopIndex,
                    0);
            }

            public string ResolvePrompt()
            {
                switch (Kind)
                {
                    case BoardWorldInteractionKind.Tombstone:
                        return GameText.F("+{0} GOLD\nRMB", Gold);
                    case BoardWorldInteractionKind.KeyShop:
                        return GameText.T("KEY SHOP\nRMB BUY  20 GOLD");
                    case BoardWorldInteractionKind.ItemShop:
                        return GameText.F(
                            "ITEM SHOP {0}\nRMB OPEN",
                            TargetIndex + 1);
                    default:
                        return string.Empty;
                }
            }
        }

        /// <summary>
        /// Resolves the localized prompt for the world target under the local
        /// crosshair. The same target resolution and action gates are used by
        /// right-click interaction routing.
        /// </summary>
        public bool TryGetLocalBoardInteractionPrompt(out string prompt)
        {
            prompt = string.Empty;
            if (!TryGetLocalBoardInteraction(out var interaction))
            {
                return false;
            }

            prompt = interaction.ResolvePrompt();
            return !string.IsNullOrEmpty(prompt);
        }

        private bool TryGetLocalBoardInteraction(
            out BoardWorldInteractionTarget interaction)
        {
            interaction = default;
            var match = NetworkMatchState.Instance;
            if (!IsOwner || !IsSpawned || match == null ||
                !match.CanAcceptActionInput ||
                !HasResolvedItemChoice ||
                CurrentHealth <= 0 ||
                BoardFlowView.IsItemShopOpen ||
                BoardUtilityItemView.IsTargetPickerOpen ||
                IsSwapping ||
                HandEmoteWheelView.BlocksPointerInput ||
                IsPointerOverUi() ||
                LocalMouse == null)
            {
                return false;
            }

            return TryGetAimedBoardInteraction(out interaction);
        }

        private bool TryGetAimedBoardInteraction(
            out BoardWorldInteractionTarget interaction)
        {
            interaction = default;
            if (!TryGetAimedBoardInteractionHit(out var hit) ||
                hit.collider == null)
            {
                return false;
            }

            var tombstone =
                hit.collider.GetComponentInParent<BoardTombstoneMarker>();
            if (tombstone != null)
            {
                interaction = BoardWorldInteractionTarget.Tombstone(
                    tombstone.Id,
                    tombstone.Gold);
                return true;
            }

            if (hit.collider.GetComponentInParent<KeyShopWorldTarget>() != null)
            {
                interaction = BoardWorldInteractionTarget.KeyShop();
                return true;
            }

            var itemShop =
                hit.collider.GetComponentInParent<ItemShopWorldTarget>();
            if (itemShop == null ||
                itemShop.ShopIndex < 0 ||
                itemShop.ShopIndex >= ItemShopRules.ShopCount)
            {
                return false;
            }

            interaction =
                BoardWorldInteractionTarget.ItemShop(itemShop.ShopIndex);
            return true;
        }
    }
}
