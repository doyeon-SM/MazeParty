using MazeParty.Gameplay;
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
        public Canvas RootCanvas => rootCanvas;
        public bool HasRequiredReferences => rootCanvas != null && actionRoot != null && clickIcon != null &&
            clickIcon.texture != null && actionText != null;
        public void Configure(Canvas canvas, GameObject action, RawImage icon, Text label)
        { rootCanvas = canvas; actionRoot = action; clickIcon = icon; actionText = label; }
        public void SetAction(bool choosing, bool sitting)
        {
            actionRoot.SetActive(choosing);
            actionText.text = GameText.T(sitting ? "Stand" : "Sit");
        }
    }
}
