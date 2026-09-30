using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardBoundaryActivationRulesTests
    {
        [TestCase(
            MultiplayerConstants.LobbyPhase,
            true,
            true,
            false,
            false,
            true,
            false,
            TestName = "Lobby_HidesStaleActionBoundary")]
        [TestCase(
            MultiplayerConstants.LobbyPhase,
            true,
            false,
            true,
            true,
            true,
            false,
            TestName = "Lobby_HidesStaleCombatBoundary")]
        [TestCase(
            MultiplayerConstants.PlayingPhase,
            false,
            true,
            false,
            false,
            true,
            false,
            TestName = "GameplayDisabled_HidesBoundary")]
        [TestCase(
            MultiplayerConstants.PlayingPhase,
            true,
            true,
            false,
            false,
            true,
            true,
            TestName = "PlayingAction_ShowsBoundary")]
        [TestCase(
            MultiplayerConstants.PlayingPhase,
            true,
            false,
            true,
            true,
            true,
            true,
            TestName = "PlayingCombatActive_ShowsBoundary")]
        [TestCase(
            MultiplayerConstants.PlayingPhase,
            true,
            false,
            true,
            false,
            true,
            false,
            TestName = "PlayingCombatInactive_HidesBoundary")]
        [TestCase(
            MultiplayerConstants.PlayingPhase,
            true,
            false,
            false,
            false,
            true,
            false,
            TestName = "PlayingOverview_HidesBoundary")]
        [TestCase(
            MultiplayerConstants.PlayingPhase,
            true,
            true,
            false,
            false,
            false,
            false,
            TestName = "MissingLogicalTile_HidesBoundary")]
        public void ShouldActivate_RequiresPlayingBoardPhase(
            string sessionPhase,
            bool gameplayEnabled,
            bool isActionPhase,
            bool isCombatPhase,
            bool combatBoundaryActive,
            bool hasLogicalTile,
            bool expected)
        {
            Assert.That(
                BoardBoundaryActivationRules.ShouldActivate(
                    sessionPhase,
                    gameplayEnabled,
                    isActionPhase,
                    isCombatPhase,
                    combatBoundaryActive,
                    hasLogicalTile),
                Is.EqualTo(expected));
        }

        [Test]
        public void IsLobbyPhase_UsesExactSessionPhase()
        {
            Assert.That(
                BoardBoundaryActivationRules.IsLobbyPhase(
                    MultiplayerConstants.LobbyPhase),
                Is.True);
            Assert.That(
                BoardBoundaryActivationRules.IsLobbyPhase(
                    MultiplayerConstants.PlayingPhase),
                Is.False);
            Assert.That(
                BoardBoundaryActivationRules.IsLobbyPhase(null),
                Is.False);
        }
    }
}
