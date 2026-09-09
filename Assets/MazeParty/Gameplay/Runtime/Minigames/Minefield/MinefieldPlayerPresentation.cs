using UnityEngine;

namespace MazeParty.Gameplay.Minigames.Minefield
{
    /// <summary>
    /// Bloodless first-hit presentation: hides the torso, lowers the visual group,
    /// and keeps both hands beside the head. The pose is enforced after the base
    /// avatar animation so it also works with the generated prototype avatar.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class MinefieldPlayerPresentation : MonoBehaviour
    {
        [SerializeField] private MinefieldPlayerActor playerActor;
        [SerializeField] private PlayerAvatarVisual avatarVisual;
        [SerializeField] private Transform visualPoseRoot;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Transform crippledHeadAnchor;
        [SerializeField] private GameObject[] hideOnCripple = new GameObject[0];
        [SerializeField] private GameObject[] hideOnElimination = new GameObject[0];
        [SerializeField] private Vector3 crippledRootOffset =
            new Vector3(0f, -0.35f, 0f);
        [SerializeField] private Vector3 leftHandOffset =
            new Vector3(-0.48f, -0.04f, 0f);
        [SerializeField] private Vector3 rightHandOffset =
            new Vector3(0.48f, -0.04f, 0f);

        private MinefieldPlayerActor _subscribedActor;
        private Vector3 _initialRootLocalPosition;
        private Vector3 _initialHeadLocalPosition;
        private Quaternion _initialHeadLocalRotation;
        private Vector3 _initialLeftHandLocalPosition;
        private Quaternion _initialLeftHandLocalRotation;
        private Vector3 _initialRightHandLocalPosition;
        private Quaternion _initialRightHandLocalRotation;
        private bool[] _crippleInitialActive;
        private bool[] _eliminationInitialActive;
        private bool _captured;
        private bool _useCrippledPose;
        private bool _eliminated;

        private void Awake()
        {
            ResolveDefaultReferences();
        }

        private void OnEnable()
        {
            ResolveDefaultReferences();
            SubscribeToActor(playerActor);
            CaptureInitialState();
            if (playerActor != null)
            {
                ApplyState(playerActor.Snapshot);
            }
        }

        private void Start()
        {
            ResolveDefaultReferences();
            CaptureInitialState();
            if (playerActor != null)
            {
                ApplyState(playerActor.Snapshot);
            }
        }

        private void OnDisable()
        {
            SubscribeToActor(null);
        }

        private void LateUpdate()
        {
            if (!_useCrippledPose)
            {
                return;
            }

            EnforceCrippledPose();
        }

        public void Configure(
            MinefieldPlayerActor actor,
            Transform poseRoot,
            Transform headTransform,
            Transform leftHandTransform,
            Transform rightHandTransform,
            GameObject torso)
        {
            SubscribeToActor(actor);
            visualPoseRoot = poseRoot;
            head = headTransform;
            leftHand = leftHandTransform;
            rightHand = rightHandTransform;
            hideOnCripple = torso != null
                ? new[] { torso }
                : new GameObject[0];
            _captured = false;
            CaptureInitialState();
            if (playerActor != null)
            {
                ApplyState(playerActor.Snapshot);
            }
        }

        public void ConfigureHiddenObjects(
            GameObject[] crippleObjects,
            GameObject[] eliminationObjects)
        {
            hideOnCripple = crippleObjects ?? new GameObject[0];
            hideOnElimination = eliminationObjects ?? new GameObject[0];
            _captured = false;
            CaptureInitialState();
            if (playerActor != null)
            {
                ApplyState(playerActor.Snapshot);
            }
        }

        public void ApplyState(MinefieldActorSnapshot snapshot)
        {
            CaptureInitialState();
            _useCrippledPose = snapshot.MineHitCount > 0;
            _eliminated = snapshot.State == MinefieldPlayerState.Eliminated;

            SetObjectsActive(
                hideOnCripple,
                _crippleInitialActive,
                !_useCrippledPose);
            SetObjectsActive(
                hideOnElimination,
                _eliminationInitialActive,
                !_eliminated);

            if (avatarVisual != null)
            {
                // The root offset already lowers the head to floor level. The
                // base crouch pose would lower it a second time and bury it.
                avatarVisual.SetCrouching(false);
                avatarVisual.SetEliminated(_eliminated);
            }

            if (_useCrippledPose)
            {
                EnforceCrippledPose();
            }
            else
            {
                RestorePose();
            }
        }

        private void ResolveDefaultReferences()
        {
            if (playerActor == null)
            {
                playerActor = GetComponent<MinefieldPlayerActor>();
            }
            if (avatarVisual == null)
            {
                avatarVisual = GetComponent<PlayerAvatarVisual>();
            }
            if (avatarVisual != null)
            {
                avatarVisual.EnsureBuilt();
            }

            if (visualPoseRoot == null)
            {
                visualPoseRoot = FindDescendant(transform, "WorldModel");
            }
            if (head == null)
            {
                head = FindDescendant(transform, "HeadAnchor");
            }
            if (leftHand == null)
            {
                leftHand = FindDescendant(transform, "LeftHandAnchor");
            }
            if (rightHand == null)
            {
                rightHand = FindDescendant(transform, "RightHandAnchor");
            }

            if (hideOnCripple == null || hideOnCripple.Length == 0)
            {
                var torso = FindDescendant(transform, "BodyAnchor");
                var outfit = FindDescendant(transform, "OutfitAnchor");
                if (torso != null && outfit != null)
                {
                    hideOnCripple = new[]
                    {
                        torso.gameObject,
                        outfit.gameObject
                    };
                }
                else if (torso != null)
                {
                    hideOnCripple = new[] { torso.gameObject };
                }
            }
        }

        private void SubscribeToActor(MinefieldPlayerActor actor)
        {
            if (_subscribedActor != null)
            {
                _subscribedActor.StateChanged -= HandleActorStateChanged;
            }

            playerActor = actor;
            _subscribedActor = actor;
            if (_subscribedActor != null && isActiveAndEnabled)
            {
                _subscribedActor.StateChanged += HandleActorStateChanged;
            }
        }

        private void HandleActorStateChanged(
            MinefieldPlayerActor actor,
            MinefieldActorSnapshot snapshot)
        {
            ApplyState(snapshot);
        }

        private void CaptureInitialState()
        {
            if (_captured)
            {
                return;
            }

            if (visualPoseRoot != null)
            {
                _initialRootLocalPosition = visualPoseRoot.localPosition;
            }
            if (head != null)
            {
                _initialHeadLocalPosition = head.localPosition;
                _initialHeadLocalRotation = head.localRotation;
            }
            if (leftHand != null)
            {
                _initialLeftHandLocalPosition = leftHand.localPosition;
                _initialLeftHandLocalRotation = leftHand.localRotation;
            }
            if (rightHand != null)
            {
                _initialRightHandLocalPosition = rightHand.localPosition;
                _initialRightHandLocalRotation = rightHand.localRotation;
            }

            _crippleInitialActive = CaptureActiveStates(hideOnCripple);
            _eliminationInitialActive = CaptureActiveStates(hideOnElimination);
            _captured = true;
        }

        private void EnforceCrippledPose()
        {
            if (visualPoseRoot != null)
            {
                visualPoseRoot.localPosition =
                    _initialRootLocalPosition + crippledRootOffset;
            }

            if (head != null && crippledHeadAnchor != null)
            {
                head.SetPositionAndRotation(
                    crippledHeadAnchor.position,
                    crippledHeadAnchor.rotation);
            }

            if (head != null && leftHand != null)
            {
                leftHand.position = head.TransformPoint(leftHandOffset);
                leftHand.rotation = head.rotation;
            }
            if (head != null && rightHand != null)
            {
                rightHand.position = head.TransformPoint(rightHandOffset);
                rightHand.rotation = head.rotation;
            }
        }

        private void RestorePose()
        {
            if (!_captured)
            {
                return;
            }

            if (visualPoseRoot != null)
            {
                visualPoseRoot.localPosition = _initialRootLocalPosition;
            }
            if (head != null)
            {
                head.localPosition = _initialHeadLocalPosition;
                head.localRotation = _initialHeadLocalRotation;
            }
            if (leftHand != null)
            {
                leftHand.localPosition = _initialLeftHandLocalPosition;
                leftHand.localRotation = _initialLeftHandLocalRotation;
            }
            if (rightHand != null)
            {
                rightHand.localPosition = _initialRightHandLocalPosition;
                rightHand.localRotation = _initialRightHandLocalRotation;
            }
        }

        private static bool[] CaptureActiveStates(GameObject[] targets)
        {
            if (targets == null)
            {
                return new bool[0];
            }

            var values = new bool[targets.Length];
            for (var index = 0; index < targets.Length; index++)
            {
                values[index] = targets[index] != null &&
                                targets[index].activeSelf;
            }
            return values;
        }

        private static void SetObjectsActive(
            GameObject[] targets,
            bool[] initialStates,
            bool restoreInitialState)
        {
            if (targets == null)
            {
                return;
            }

            for (var index = 0; index < targets.Length; index++)
            {
                var target = targets[index];
                if (target == null)
                {
                    continue;
                }

                var shouldBeActive = restoreInitialState &&
                                     initialStates != null &&
                                     index < initialStates.Length &&
                                     initialStates[index];
                target.SetActive(shouldBeActive);
            }
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == objectName)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), objectName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
        public void ConfigureCrippledPose(
            Vector3 rootOffset,
            Vector3 leftOffset,
            Vector3 rightOffset,
            Transform optionalHeadAnchor = null)
        {
            crippledRootOffset = rootOffset;
            leftHandOffset = leftOffset;
            rightHandOffset = rightOffset;
            crippledHeadAnchor = optionalHeadAnchor;
            if (_useCrippledPose)
            {
                EnforceCrippledPose();
            }
        }
    }
}
