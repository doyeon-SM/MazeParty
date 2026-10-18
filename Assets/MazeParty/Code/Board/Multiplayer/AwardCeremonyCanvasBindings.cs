using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Authored bonus overlay and compact return controls; no ranking panel.</summary>
    [DisallowMultipleComponent]
    public sealed class AwardCeremonyCanvasBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private GraphicRaycaster rootRaycaster;
        [SerializeField] private GameObject bonusAwardOverlay;
        [SerializeField] private Animator bonusAwardAnimator;
        [SerializeField] private Text awardStepText;
        [SerializeField] private Text awardCategoryText;
        [SerializeField] private Text awardWinnerText;
        [SerializeField] private Text awardRewardText;
        [SerializeField] private GameObject returnControlsRoot;
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
        public Text AwardWinnerText => awardWinnerText;
        public Text AwardRewardText => awardRewardText;
        public GameObject ReturnControlsRoot => returnControlsRoot;
        public Text InputLockText => inputLockText;
        public Button LeaveRoomButton => leaveRoomButton;
        public Text LeaveRoomButtonText => leaveRoomButtonText;
        public Text ReturnStatusText => returnStatusText;

        public bool HasRequiredReferences => rootCanvas != null && rootRaycaster != null &&
            bonusAwardOverlay != null && bonusAwardAnimator != null && awardStepText != null &&
            awardCategoryText != null && awardWinnerText != null && awardRewardText != null &&
            returnControlsRoot != null && inputLockText != null && leaveRoomButton != null &&
            leaveRoomButtonText != null && returnStatusText != null &&
            bonusAwardOverlay.transform.IsChildOf(transform) && returnControlsRoot.transform.IsChildOf(transform);

        public void Configure(Canvas canvas, GraphicRaycaster raycaster, GameObject overlay,
            Animator animator, Text step, Text category, Text winners, Text reward,
            GameObject controls, Text inputLock, Button leaveButton, Text buttonText, Text status)
        {
            rootCanvas = canvas; rootRaycaster = raycaster;
            bonusAwardOverlay = overlay; bonusAwardAnimator = animator;
            awardStepText = step; awardCategoryText = category;
            awardWinnerText = winners; awardRewardText = reward;
            returnControlsRoot = controls; inputLockText = inputLock;
            leaveRoomButton = leaveButton; leaveRoomButtonText = buttonText; returnStatusText = status;
        }
    }
}
