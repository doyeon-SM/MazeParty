using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.OddOneOut;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class OddOneOutHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private GameObject actionRoot;
        [SerializeField] private RawImage clickIcon;
        [SerializeField] private Text actionText;
        [SerializeField] private GameObject selectionTimerRoot;
        [SerializeField] private Slider selectionTimeBar;
        [SerializeField] private Text selectionTimeText;
        public Canvas RootCanvas => rootCanvas;
        public bool HasRequiredReferences => rootCanvas != null && actionRoot != null && clickIcon != null &&
            clickIcon.texture != null && actionText != null && selectionTimerRoot != null &&
            selectionTimeBar != null && selectionTimeBar.fillRect != null && selectionTimeText != null &&
            selectionTimerRoot.transform.IsChildOf(transform) &&
            selectionTimeBar.transform.IsChildOf(selectionTimerRoot.transform) &&
            selectionTimeText.transform.IsChildOf(selectionTimerRoot.transform);
        public void Configure(Canvas canvas, GameObject action, RawImage icon, Text label)
        { rootCanvas = canvas; actionRoot = action; clickIcon = icon; actionText = label; }
        public void ConfigureSelectionTimer(GameObject timerRoot, Slider timeBar, Text timeText)
        { selectionTimerRoot = timerRoot; selectionTimeBar = timeBar; selectionTimeText = timeText; }
        public void SetSelectionTime(OddOneOutPhase phase, double remaining)
        {
            selectionTimerRoot.SetActive(phase == OddOneOutPhase.Choosing);
            var seconds = Mathf.Clamp((float)remaining, 0f, (float)OddOneOutMatchState.SelectionSeconds);
            selectionTimeBar.SetValueWithoutNotify(seconds / (float)OddOneOutMatchState.SelectionSeconds);
            selectionTimeText.text = Mathf.CeilToInt(seconds).ToString();
        }
        public void SetAction(bool choosing, bool sitting)
        {
            actionRoot.SetActive(choosing);
            actionText.text = GameText.T(sitting ? "Stand" : "Sit");
        }
    }
}
