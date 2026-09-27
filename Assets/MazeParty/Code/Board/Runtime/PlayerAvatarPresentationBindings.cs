using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Authored hierarchy used by <see cref="PlayerAvatarVisual"/>. Pose anchors may
    /// move at runtime, while their visual children remain designer-owned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAvatarPresentationBindings : MonoBehaviour
    {
        [Header("World presentation")]
        [SerializeField] private Transform worldModel;
        [SerializeField] private Transform bodyAnchor;
        [SerializeField] private Transform headAnchor;
        [SerializeField] private Transform leftHandAnchor;
        [SerializeField] private Transform rightHandAnchor;
        [SerializeField] private Transform leftEyeAnchor;
        [SerializeField] private Transform rightEyeAnchor;
        [SerializeField] private Transform mouthAnchor;
        [SerializeField] private Transform hatAnchor;
        [SerializeField] private Transform outfitAnchor;
        [SerializeField] private Transform worldItemRoot;
        [SerializeField] private Transform worldGestureRoot;
        [SerializeField] private SpriteRenderer faceSprite;
        [SerializeField] private Transform nameplateAnchor;
        [SerializeField] private TextMesh nameText;
        [SerializeField] private GameObject topViewHighlight;

        [Header("First-person presentation")]
        [SerializeField] private Transform firstPersonPresentation;
        [SerializeField] private Transform firstPersonHands;
        [SerializeField] private Transform firstPersonLeftHand;
        [SerializeField] private Transform firstPersonRightHand;
        [SerializeField] private Transform firstPersonItemRoot;
        [SerializeField] private Transform firstPersonGestureRoot;

        [Header("Hit regions")]
        [SerializeField] private CapsuleCollider bodyHitbox;
        [SerializeField] private SphereCollider headHitbox;
        [SerializeField] private SphereCollider leftHandHitbox;
        [SerializeField] private SphereCollider rightHandHitbox;

        [Header("Runtime tint targets")]
        [SerializeField] private Renderer[] bodyTintRenderers =
            Array.Empty<Renderer>();

        public Transform WorldModel => worldModel;
        public Transform BodyAnchor => bodyAnchor;
        public Transform HeadAnchor => headAnchor;
        public Transform LeftHandAnchor => leftHandAnchor;
        public Transform RightHandAnchor => rightHandAnchor;
        public Transform LeftEyeAnchor => leftEyeAnchor;
        public Transform RightEyeAnchor => rightEyeAnchor;
        public Transform MouthAnchor => mouthAnchor;
        public Transform HatAnchor => hatAnchor;
        public Transform OutfitAnchor => outfitAnchor;
        public Transform WorldItemRoot => worldItemRoot;
        public Transform WorldGestureRoot => worldGestureRoot;
        public SpriteRenderer FaceSprite => faceSprite;
        public Transform NameplateAnchor => nameplateAnchor;
        public TextMesh NameText => nameText;
        public GameObject TopViewHighlight => topViewHighlight;
        public Transform FirstPersonPresentation => firstPersonPresentation;
        public Transform FirstPersonHands => firstPersonHands;
        public Transform FirstPersonLeftHand => firstPersonLeftHand;
        public Transform FirstPersonRightHand => firstPersonRightHand;
        public Transform FirstPersonItemRoot => firstPersonItemRoot;
        public Transform FirstPersonGestureRoot => firstPersonGestureRoot;
        public CapsuleCollider BodyHitbox => bodyHitbox;
        public SphereCollider HeadHitbox => headHitbox;
        public SphereCollider LeftHandHitbox => leftHandHitbox;
        public SphereCollider RightHandHitbox => rightHandHitbox;
        public Renderer[] BodyTintRenderers => bodyTintRenderers;

        public bool HasRequiredReferences =>
            worldModel != null &&
            bodyAnchor != null &&
            headAnchor != null &&
            leftHandAnchor != null &&
            rightHandAnchor != null &&
            leftEyeAnchor != null &&
            rightEyeAnchor != null &&
            mouthAnchor != null &&
            hatAnchor != null &&
            outfitAnchor != null &&
            worldItemRoot != null &&
            worldGestureRoot != null &&
            faceSprite != null &&
            nameplateAnchor != null &&
            nameText != null &&
            topViewHighlight != null &&
            firstPersonPresentation != null &&
            firstPersonHands != null &&
            firstPersonLeftHand != null &&
            firstPersonRightHand != null &&
            firstPersonItemRoot != null &&
            firstPersonGestureRoot != null &&
            bodyHitbox != null &&
            headHitbox != null &&
            leftHandHitbox != null &&
            rightHandHitbox != null &&
            HasAssignedRenderers(bodyTintRenderers) &&
            worldModel.IsChildOf(transform);

        public void Configure(
            Transform world,
            Transform body,
            Transform head,
            Transform leftHand,
            Transform rightHand,
            Transform leftEye,
            Transform rightEye,
            Transform mouth,
            Transform hat,
            Transform outfit,
            Transform worldItems,
            Transform worldGestures,
            SpriteRenderer face,
            Transform nameplate,
            TextMesh displayName,
            GameObject highlight,
            Transform firstPersonRoot,
            Transform firstPersonHandRoot,
            Transform firstPersonLeft,
            Transform firstPersonRight,
            Transform firstPersonItems,
            Transform firstPersonGestures,
            CapsuleCollider bodyZone,
            SphereCollider headZone,
            SphereCollider leftHandZone,
            SphereCollider rightHandZone,
            Renderer[] tintRenderers)
        {
            worldModel = world;
            bodyAnchor = body;
            headAnchor = head;
            leftHandAnchor = leftHand;
            rightHandAnchor = rightHand;
            leftEyeAnchor = leftEye;
            rightEyeAnchor = rightEye;
            mouthAnchor = mouth;
            hatAnchor = hat;
            outfitAnchor = outfit;
            worldItemRoot = worldItems;
            worldGestureRoot = worldGestures;
            faceSprite = face;
            nameplateAnchor = nameplate;
            nameText = displayName;
            topViewHighlight = highlight;
            firstPersonPresentation = firstPersonRoot;
            firstPersonHands = firstPersonHandRoot;
            firstPersonLeftHand = firstPersonLeft;
            firstPersonRightHand = firstPersonRight;
            firstPersonItemRoot = firstPersonItems;
            firstPersonGestureRoot = firstPersonGestures;
            bodyHitbox = bodyZone;
            headHitbox = headZone;
            leftHandHitbox = leftHandZone;
            rightHandHitbox = rightHandZone;
            bodyTintRenderers = tintRenderers ?? Array.Empty<Renderer>();
        }

        private static bool HasAssignedRenderers(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }
            for (var index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] == null)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
