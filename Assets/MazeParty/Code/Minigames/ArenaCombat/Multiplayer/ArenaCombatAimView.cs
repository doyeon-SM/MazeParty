using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized Arena Combat aim binding. The authored image stays hidden
    /// unless the active arena presenter explicitly grants first-person aim.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaCombatAimView : MonoBehaviour
    {
        [SerializeField] private Image aimImage;

        public Image AimImage => aimImage;
        public bool HasRequiredReferences =>
            aimImage != null &&
            (aimImage.transform == transform ||
             aimImage.transform.IsChildOf(transform));
        public bool IsVisible =>
            HasRequiredReferences && aimImage.gameObject.activeSelf;

        public void Configure(Image image)
        {
            aimImage = image;
        }

        private void Awake()
        {
            SetVisible(false);
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (aimImage != null && aimImage.gameObject.activeSelf != visible)
            {
                aimImage.gameObject.SetActive(visible);
            }
        }
    }
}