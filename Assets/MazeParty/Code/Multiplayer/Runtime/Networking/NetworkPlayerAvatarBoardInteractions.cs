using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        private enum BoardWorldInteractionKind : byte
        {
            None,
            KeyShop,
            ItemShop,
            Chest
        }

        private readonly struct BoardWorldInteractionTarget
        {
            private BoardWorldInteractionTarget(
                BoardWorldInteractionKind kind,
                int targetIndex)
            {
                Kind = kind;
                TargetIndex = targetIndex;
            }

            public BoardWorldInteractionKind Kind { get; }
            public int TargetIndex { get; }

            public static BoardWorldInteractionTarget KeyShop()
            {
                return new BoardWorldInteractionTarget(
                    BoardWorldInteractionKind.KeyShop,
                    -1);
            }

            public static BoardWorldInteractionTarget ItemShop(int shopIndex)
            {
                return new BoardWorldInteractionTarget(
                    BoardWorldInteractionKind.ItemShop,
                    shopIndex);
            }

            public static BoardWorldInteractionTarget Chest(int index) =>
                new BoardWorldInteractionTarget(BoardWorldInteractionKind.Chest, index);

            public string ResolvePrompt()
            {
                switch (Kind)
                {
                    case BoardWorldInteractionKind.KeyShop:
                        return GameText.T("KEY SHOP (RMB TO BUY A KEY -20 GOLD)");
                    case BoardWorldInteractionKind.ItemShop:
                        return GameText.T("ITEM SHOP (RMB TO OPEN SHOP)");
                    case BoardWorldInteractionKind.Chest:
                        return GameText.T("CHEST (RMB TO EQUIP ITEM)");
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
                IsSettingBombardment || BoardFlowView.IsItemShopOpen ||
                BoardUtilityItemView.IsTargetPickerOpen ||
                HandEmoteWheelView.BlocksPointerInput ||
                IsPointerOverUi() ||
                LocalMouse == null)
            {
                return false;
            }

            return TryGetAimedBoardInteraction(out interaction) &&
                (!IsSwapping || interaction.Kind == BoardWorldInteractionKind.Chest);
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

            if (hit.collider.GetComponentInParent<KeyShopWorldTarget>() != null)
            {
                interaction = BoardWorldInteractionTarget.KeyShop();
                return true;
            }

            var chest = hit.collider.GetComponentInParent<BoardChestVisual>();
            if (chest != null)
            {
                interaction = BoardWorldInteractionTarget.Chest(chest.ChestIndex);
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
