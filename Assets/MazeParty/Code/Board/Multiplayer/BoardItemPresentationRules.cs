using MazeParty.Gameplay;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Pure presentation policy shared by the board item runtime and its
    /// long-lived boundary tests.
    /// </summary>
    public static class BoardItemPresentationRules
    {
        public static bool IsFirearm(PrototypeItemId item)
        {
            return item == PrototypeItemId.Pistol ||
                   item == PrototypeItemId.Sniper;
        }

        public static bool ShouldHighlightFirearmTarget(
            bool hitPlayer,
            int targetHealth,
            bool isCloaked,
            bool openingProtected,
            double personalProtectionRemaining)
        {
            return hitPlayer &&
                   targetHealth > 0 &&
                   !isCloaked &&
                   !openingProtected &&
                   personalProtectionRemaining <= 0d;
        }

        public static bool ShouldShowGrenadeRange(
            bool isOwner,
            bool canAcceptActionInput,
            int currentHealth,
            ItemChoiceResolution choice,
            PrototypeItemId equippedItem,
            int charges,
            bool localUsePending)
        {
            return isOwner &&
                   canAcceptActionInput &&
                   currentHealth > 0 &&
                   choice == ItemChoiceResolution.ItemSelected &&
                   equippedItem == PrototypeItemId.Grenade &&
                   charges > 0 &&
                   !localUsePending;
        }
    }

    /// <summary>
    /// Keeps the local grenade preview hidden from input until the server
    /// rejects that request or the authoritative selection becomes unavailable.
    /// </summary>
    public sealed class GrenadeRangeUseState
    {
        private uint _nextRequestId;
        private uint _pendingRequestId;

        public bool IsPending { get; private set; }

        public bool TryBegin(out uint requestId)
        {
            if (IsPending)
            {
                requestId = 0u;
                return false;
            }

            unchecked
            {
                _nextRequestId++;
                if (_nextRequestId == 0u)
                {
                    _nextRequestId = 1u;
                }
            }

            IsPending = true;
            _pendingRequestId = _nextRequestId;
            requestId = _pendingRequestId;
            return true;
        }

        public void Resolve(uint requestId, bool accepted)
        {
            if (!IsPending || requestId == 0u ||
                requestId != _pendingRequestId)
            {
                return;
            }

            if (!accepted)
            {
                Clear();
            }
        }

        public void ClearWhenUnavailable(bool selectionAvailable)
        {
            if (!selectionAvailable)
            {
                Clear();
            }
        }

        public void Clear()
        {
            IsPending = false;
            _pendingRequestId = 0u;
        }
    }
}
