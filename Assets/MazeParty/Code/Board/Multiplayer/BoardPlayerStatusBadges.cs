using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Four distinct status badges authored on each player card in
    /// BoardCanvas.prefab. All values come from replicated board state.
    /// </summary>
    public sealed class BoardPlayerStatusBadges : MonoBehaviour
    {
        [SerializeField] private Text[] baseActionIcons;
        [SerializeField] private Text[] forcedMoveIcons;
        [SerializeField] private Text[] combatIcons;
        [SerializeField] private Text[] shopRevealIcons;
        [SerializeField] private Text[] deathIcons;

        public bool HasRequiredReferences =>
            HasSlots(baseActionIcons) && HasSlots(forcedMoveIcons) &&
            HasSlots(combatIcons) && HasSlots(shopRevealIcons) &&
            HasSlots(deathIcons);

        public void Configure(Text[] baseIcons, Text[] forcedIcons,
            Text[] fightIcons, Text[] shopIcons, Text[] downIcons)
        {
            baseActionIcons = baseIcons;
            forcedMoveIcons = forcedIcons;
            combatIcons = fightIcons;
            shopRevealIcons = shopIcons;
            deathIcons = downIcons;
        }

        private void Update()
        {
            if (!HasRequiredReferences) return;

            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var avatar = match != null && match.IsSpawned
                    ? match.GetAvatarForSlot(slot) : null;
                var death = avatar != null && avatar.CurrentHealth <= 0;
                var shop = !death && match != null &&
                           match.IsKeyShopRevealActive;
                var combat = !death && !shop && match != null &&
                             match.IsCombatActive &&
                             match.IsCombatParticipant(slot);
                var forced = !death && !shop && !combat && match != null &&
                             match.FlowState == BoardFlowState.AscendingResolve &&
                             match.LastActionEndReason == BoardActionEndReason.TimeExpired &&
                             match.HasRolled(slot) && !match.HasArrived(slot);

                SetActive(deathIcons[slot], death);
                SetActive(shopRevealIcons[slot], shop);
                SetActive(combatIcons[slot], combat);
                SetActive(forcedMoveIcons[slot], forced);
                SetActive(baseActionIcons[slot], !death && !shop &&
                    !combat && !forced);
            }
        }

        private static bool HasSlots(Text[] icons)
        {
            if (icons == null || icons.Length != MultiplayerConstants.MaxPlayers)
                return false;
            for (var index = 0; index < icons.Length; index++)
            {
                if (icons[index] == null) return false;
            }
            return true;
        }

        private static void SetActive(Text icon, bool active)
        {
            if (icon.gameObject.activeSelf != active)
                icon.gameObject.SetActive(active);
        }
    }
}
