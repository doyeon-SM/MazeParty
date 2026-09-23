using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized presentation contract owned by AwardCeremonyCanvas.prefab.
    /// Runtime code only changes copy, visibility and interaction state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AwardCeremonyCanvasBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private GraphicRaycaster rootRaycaster;
        [SerializeField] private GameObject bonusAwardOverlay;
        [SerializeField] private Animator bonusAwardAnimator;
        [SerializeField] private Text awardStepText;
        [SerializeField] private Text awardCategoryText;
        [SerializeField] private Text awardValueText;
        [SerializeField] private Text awardWinnerText;
        [SerializeField] private Text awardRewardText;
        [SerializeField] private GameObject finalRankingPanel;
        [SerializeField] private Text finalTitleText;
        [SerializeField] private Text[] finalRankTexts;
        [SerializeField] private Text inputLockText;
        [SerializeField] private Button leaveRoomButton;
        [SerializeField] private Text leaveRoomButtonText;
        [SerializeField] private Text returnStatusText;

        public Canvas RootCanvas => rootCanvas;
        public GraphicRaycaster RootRaycaster => rootRaycaster;
        public GameObject BonusAwardOverlay => bonusAwardOverlay;
        public Animator BonusAwardAnimator => bonusAwardAnimator;
        public Text AwardStepText => awardStepText;
        public Text AwardCategoryText => awardCategoryText;
        public Text AwardValueText => awardValueText;
        public Text AwardWinnerText => awardWinnerText;
        public Text AwardRewardText => awardRewardText;
        public GameObject FinalRankingPanel => finalRankingPanel;
        public Text FinalTitleText => finalTitleText;
        public Text[] FinalRankTexts => finalRankTexts;
        public Text InputLockText => inputLockText;
        public Button LeaveRoomButton => leaveRoomButton;
        public Text LeaveRoomButtonText => leaveRoomButtonText;
        public Text ReturnStatusText => returnStatusText;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            rootRaycaster != null &&
            bonusAwardOverlay != null &&
            bonusAwardAnimator != null &&
            awardStepText != null &&
            awardCategoryText != null &&
            awardValueText != null &&
            awardWinnerText != null &&
            awardRewardText != null &&
            finalRankingPanel != null &&
            finalTitleText != null &&
            finalRankTexts != null &&
            finalRankTexts.Length == MultiplayerConstants.MaxPlayers &&
            AllTextsAreAssigned(finalRankTexts) &&
            inputLockText != null &&
            leaveRoomButton != null &&
            leaveRoomButtonText != null &&
            returnStatusText != null &&
            bonusAwardOverlay.transform.IsChildOf(transform) &&
            finalRankingPanel.transform.IsChildOf(transform);

        public void Configure(
            Canvas canvas,
            GraphicRaycaster raycaster,
            GameObject awardOverlay,
            Animator awardAnimator,
            Text stepText,
            Text categoryText,
            Text valueText,
            Text winnerText,
            Text rewardText,
            GameObject rankingPanel,
            Text titleText,
            Text[] rankTexts,
            Text lockText,
            Button leaveButton,
            Text leaveButtonText,
            Text statusText)
        {
            rootCanvas = canvas;
            rootRaycaster = raycaster;
            bonusAwardOverlay = awardOverlay;
            bonusAwardAnimator = awardAnimator;
            awardStepText = stepText;
            awardCategoryText = categoryText;
            awardValueText = valueText;
            awardWinnerText = winnerText;
            awardRewardText = rewardText;
            finalRankingPanel = rankingPanel;
            finalTitleText = titleText;
            finalRankTexts = rankTexts;
            inputLockText = lockText;
            leaveRoomButton = leaveButton;
            leaveRoomButtonText = leaveButtonText;
            returnStatusText = statusText;
        }

        private static bool AllTextsAreAssigned(Text[] texts)
        {
            for (var index = 0; index < texts.Length; index++)
            {
                if (texts[index] == null)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
