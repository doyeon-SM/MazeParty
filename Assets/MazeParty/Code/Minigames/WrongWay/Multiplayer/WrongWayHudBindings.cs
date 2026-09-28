using MazeParty.Gameplay.Minigames.WrongWay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Stable references owned by WrongWayHud.prefab. Runtime presentation
    /// code only binds replicated state to these prefab-authored controls.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WrongWayHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private Image directionIcon;
        [SerializeField] private Sprite upIcon;
        [SerializeField] private Sprite downIcon;
        [SerializeField] private Sprite leftIcon;
        [SerializeField] private Sprite rightIcon;

        public Canvas Canvas => canvas;
        public Image DirectionIcon => directionIcon;
        public Sprite UpIcon => upIcon;
        public Sprite DownIcon => downIcon;
        public Sprite LeftIcon => leftIcon;
        public Sprite RightIcon => rightIcon;

        public bool HasRequiredReferences =>
            canvas != null &&
            directionIcon != null &&
            upIcon != null &&
            downIcon != null &&
            leftIcon != null &&
            rightIcon != null;

        public void Configure(
            Canvas targetCanvas,
            Image targetDirectionIcon,
            Sprite targetUpIcon,
            Sprite targetDownIcon,
            Sprite targetLeftIcon,
            Sprite targetRightIcon)
        {
            canvas = targetCanvas;
            directionIcon = targetDirectionIcon;
            upIcon = targetUpIcon;
            downIcon = targetDownIcon;
            leftIcon = targetLeftIcon;
            rightIcon = targetRightIcon;
        }

        public Sprite GetDirectionIcon(WrongWayDirection direction)
        {
            switch (direction)
            {
                case WrongWayDirection.Up:
                    return upIcon;
                case WrongWayDirection.Down:
                    return downIcon;
                case WrongWayDirection.Left:
                    return leftIcon;
                case WrongWayDirection.Right:
                    return rightIcon;
                default:
                    return null;
            }
        }
    }
}
