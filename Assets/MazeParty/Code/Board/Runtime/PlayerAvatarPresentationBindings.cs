using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    [Serializable]
    public sealed class HandEmoteRotationPose
    {
        [SerializeField] private Vector3 worldLeftEuler;
        [SerializeField] private Vector3 worldRightEuler;
        [SerializeField] private Vector3 firstPersonLeftEuler;
        [SerializeField] private Vector3 firstPersonRightEuler;

        public Vector3 WorldLeftEuler => worldLeftEuler;
        public Vector3 WorldRightEuler => worldRightEuler;
        public Vector3 FirstPersonLeftEuler => firstPersonLeftEuler;
        public Vector3 FirstPersonRightEuler => firstPersonRightEuler;

        internal static HandEmoteRotationPose Create(
            Vector3 worldLeft,
            Vector3 worldRight,
            Vector3 firstPersonLeft,
            Vector3 firstPersonRight)
        {
            return new HandEmoteRotationPose
            {
                worldLeftEuler = worldLeft,
                worldRightEuler = worldRight,
                firstPersonLeftEuler = firstPersonLeft,
                firstPersonRightEuler = firstPersonRight
            };
        }
    }

    [Serializable]
    public sealed class HandEmoteRotationSettings
    {
        public const int CurrentSchemaVersion = 1;

        [SerializeField, HideInInspector] private int schemaVersion;
        [SerializeField] private HandEmoteRotationPose greeting;
        [SerializeField] private float greetingWaveDegrees;
        [SerializeField] private HandEmoteRotationPose salute;
        [SerializeField] private HandEmoteRotationPose insult;
        [SerializeField] private HandEmoteRotationPose heart;
        [SerializeField] private HandEmoteRotationPose surprise;
        [SerializeField] private HandEmoteRotationPose surrender;
        [SerializeField] private HandEmoteRotationPose pleading;
        [SerializeField] private HandEmoteRotationPose eyesCover;

        public int SchemaVersion => schemaVersion;
        public float GreetingWaveDegrees => greetingWaveDegrees;

        public bool TryGet(
            HandEmoteId id,
            out HandEmoteRotationPose pose)
        {
            pose = null;
            if (schemaVersion < CurrentSchemaVersion)
            {
                return false;
            }

            switch (id)
            {
                case HandEmoteId.Greeting: pose = greeting; break;
                case HandEmoteId.Salute: pose = salute; break;
                case HandEmoteId.Insult: pose = insult; break;
                case HandEmoteId.Heart: pose = heart; break;
                case HandEmoteId.Surprise: pose = surprise; break;
                case HandEmoteId.Surrender: pose = surrender; break;
                case HandEmoteId.Pleading: pose = pleading; break;
                case HandEmoteId.EyesCover: pose = eyesCover; break;
            }
            return pose != null;
        }

        public bool EnsureDefaults()
        {
            if (schemaVersion >= CurrentSchemaVersion)
            {
                return false;
            }

            var zero = Vector3.zero;
            greeting = HandEmoteRotationPose.Create(zero, zero, zero, zero);
            greetingWaveDegrees = 22f;
            salute = HandEmoteRotationPose.Create(
                zero,
                new Vector3(0f, 0f, -32f),
                zero,
                new Vector3(0f, 0f, -32f));
            insult = HandEmoteRotationPose.Create(zero, zero, zero, zero);
            heart = HandEmoteRotationPose.Create(
                new Vector3(0f, 0f, -42f),
                new Vector3(0f, 0f, 42f),
                new Vector3(0f, 0f, -42f),
                new Vector3(0f, 0f, 42f));
            surprise = HandEmoteRotationPose.Create(
                new Vector3(0f, 0f, 18f),
                new Vector3(0f, 0f, -18f),
                new Vector3(0f, 0f, 18f),
                new Vector3(0f, 0f, -18f));
            surrender = HandEmoteRotationPose.Create(
                new Vector3(0f, 0f, 10f),
                new Vector3(0f, 0f, -10f),
                new Vector3(0f, 0f, 10f),
                new Vector3(0f, 0f, -10f));
            pleading = HandEmoteRotationPose.Create(
                new Vector3(0f, 0f, -82f),
                new Vector3(0f, 0f, 82f),
                new Vector3(0f, 0f, -82f),
                new Vector3(0f, 0f, 82f));
            eyesCover = HandEmoteRotationPose.Create(
                new Vector3(0f, 0f, 8f),
                new Vector3(0f, 0f, -8f),
                new Vector3(0f, 0f, 8f),
                new Vector3(0f, 0f, -8f));
            schemaVersion = CurrentSchemaVersion;
            return true;
        }
    }

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
        [SerializeField] private SpriteRenderer lobbyHostIcon;
        [SerializeField] private Color lobbyReadyNameColor =
            new Color32(88, 220, 112, 255);
        [SerializeField] private GameObject topViewHighlight;
        [SerializeField] private GameObject shieldVfx;

        [Header("First-person presentation")]
        [SerializeField] private Transform firstPersonPresentation;
        [SerializeField] private Transform firstPersonHands;
        [SerializeField] private Transform firstPersonLeftHand;
        [SerializeField] private Transform firstPersonRightHand;
        [SerializeField] private Transform firstPersonItemRoot;
        [SerializeField] private Transform firstPersonGestureRoot;

        [Header("Hand Emote Rotations")]
        [Tooltip("Adjust the world and first-person hand Euler angles for each emote. These values are initialized once and are never replaced by project setup.")]
        [SerializeField] private HandEmoteRotationSettings handEmoteRotations =
            new HandEmoteRotationSettings();

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
        public SpriteRenderer LobbyHostIcon => lobbyHostIcon;
        public Color LobbyReadyNameColor => lobbyReadyNameColor;
        public GameObject TopViewHighlight => topViewHighlight;
        public GameObject ShieldVfx => shieldVfx;
        public Transform FirstPersonPresentation => firstPersonPresentation;
        public Transform FirstPersonHands => firstPersonHands;
        public Transform FirstPersonLeftHand => firstPersonLeftHand;
        public Transform FirstPersonRightHand => firstPersonRightHand;
        public Transform FirstPersonItemRoot => firstPersonItemRoot;
        public Transform FirstPersonGestureRoot => firstPersonGestureRoot;
        public HandEmoteRotationSettings HandEmoteRotations =>
            handEmoteRotations;
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
            lobbyHostIcon != null &&
            topViewHighlight != null &&
            shieldVfx != null &&
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
            SpriteRenderer hostIcon,
            GameObject highlight,
            GameObject shield,
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
            lobbyHostIcon = hostIcon;
            topViewHighlight = highlight;
            shieldVfx = shield;
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
            EnsureHandEmoteRotationSettings();
        }

        public bool EnsureHandEmoteRotationSettings()
        {
            if (handEmoteRotations == null)
            {
                handEmoteRotations = new HandEmoteRotationSettings();
            }
            return handEmoteRotations.EnsureDefaults();
        }

        public bool TryGetHandEmoteRotation(
            HandEmoteId id,
            out HandEmoteRotationPose pose,
            out float greetingWaveDegrees)
        {
            greetingWaveDegrees = 22f;
            if (handEmoteRotations == null ||
                !handEmoteRotations.TryGet(id, out pose))
            {
                pose = null;
                return false;
            }

            greetingWaveDegrees = handEmoteRotations.GreetingWaveDegrees;
            return true;
        }

        private void OnValidate()
        {
            // Project setup owns the one-time schema migration. Validation only
            // repairs the container so loading or inspecting an already-authored
            // prefab can never replace designer-tuned rotation values.
            if (handEmoteRotations == null)
            {
                handEmoteRotations = new HandEmoteRotationSettings();
            }
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
