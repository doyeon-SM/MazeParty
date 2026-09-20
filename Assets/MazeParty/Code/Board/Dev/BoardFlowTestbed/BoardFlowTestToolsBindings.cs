using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Gameplay.BoardFlowTestbed
{
    /// <summary>
    /// Serialized controls owned by BoardFlowTestTools.prefab. The local
    /// simulator binds behavior without depending on hierarchy names.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardFlowTestToolsBindings : MonoBehaviour
    {
        [SerializeField] private Button stageFightButton;
        [SerializeField] private Button finishActionButton;
        [SerializeField] private Button speedButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button damagePlayerButton;
        [SerializeField] private Button addGoldButton;
        [SerializeField] private Button buyKeyButton;
        [SerializeField] private Text speedButtonLabel;

        public Button StageFightButton => stageFightButton;
        public Button FinishActionButton => finishActionButton;
        public Button SpeedButton => speedButton;
        public Button PauseButton => pauseButton;
        public Button DamagePlayerButton => damagePlayerButton;
        public Button AddGoldButton => addGoldButton;
        public Button BuyKeyButton => buyKeyButton;
        public Text SpeedButtonLabel => speedButtonLabel;

        public bool HasRequiredReferences =>
            stageFightButton != null &&
            finishActionButton != null &&
            speedButton != null &&
            pauseButton != null &&
            damagePlayerButton != null &&
            addGoldButton != null &&
            buyKeyButton != null &&
            speedButtonLabel != null;

        public void Configure(
            Button stageFight,
            Button finishAction,
            Button speed,
            Button pause,
            Button damagePlayer,
            Button addGold,
            Button buyKey,
            Text speedLabel)
        {
            stageFightButton = stageFight;
            finishActionButton = finishAction;
            speedButton = speed;
            pauseButton = pause;
            damagePlayerButton = damagePlayer;
            addGoldButton = addGold;
            buyKeyButton = buyKey;
            speedButtonLabel = speedLabel;
        }
    }
}
