using UnityEngine;

namespace MazeParty.Gameplay.Minigames.RedLightGreenLight
{
    /// <summary>
    /// Bloodless first-warning presentation shared with the Minefield visual
    /// language: the torso and outfit disappear while both hands stay beside
    /// the head. The network view supplies only the authoritative warning count.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class RedLightGreenLightPlayerPresentation : MonoBehaviour
    {
        [SerializeField] private PlayerAvatarVisual avatarVisual;
        [SerializeField] private Transform visualPoseRoot;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private GameObject[] hideAfterWarning = new GameObject[0];
        [SerializeField] private Vector3 warnedRootOffset =
            new Vector3(0f, -0.35f, 0f);
        [SerializeField] private Vector3 leftHandOffset =
            new Vector3(-0.48f, -0.04f, 0f);
        [SerializeField] private Vector3 rightHandOffset =
            new Vector3(0.48f, -0.04f, 0f);

        private Vector3 _initialRootLocalPosition;
        private Vector3 _initialHeadLocalPosition;
        private Quaternion _initialHeadLocalRotation;
        private Vector3 _initialLeftHandLocalPosition;
        private Quaternion _initialLeftHandLocalRotation;
        private Vector3 _initialRightHandLocalPosition;
        private Quaternion _initialRightHandLocalRotation;
        private bool[] _initialHiddenObjectStates;
        private bool _captured;
        private bool _warned;

        private void Awake()
        {
            ResolveDefaultReferences();
            CaptureInitialState();
        }

        private void LateUpdate()
        {
            if (_warned)
            {
                EnforceWarnedPose();
            }
        }

        public void ApplyState(int warningCount, bool eliminated)
        {
            ResolveDefaultReferences();
            CaptureInitialState();
            _warned = warningCount > 0;

            SetHiddenObjectsActive(!_warned);
            if (avatarVisual != null)
            {
                avatarVisual.SetCrouching(false);
                avatarVisual.SetEliminated(eliminated);
            }

            if (_warned)
            {
                EnforceWarnedPose();
            }
            else
            {
                RestorePose();
            }
        }

        private void ResolveDefaultReferences()
        {
            if (avatarVisual == null)
            {
                avatarVisual = GetComponent<PlayerAvatarVisual>();
            }
            if (avatarVisual != null)
            {
                avatarVisual.EnsureBuilt();
            }

            visualPoseRoot ??= FindDescendant(transform, "WorldModel");
            head ??= FindDescendant(transform, "HeadAnchor");
            leftHand ??= FindDescendant(transform, "LeftHandAnchor");
            rightHand ??= FindDescendant(transform, "RightHandAnchor");

            if (hideAfterWarning == null || hideAfterWarning.Length == 0)
            {
                var torso = FindDescendant(transform, "BodyAnchor");
                var outfit = FindDescendant(transform, "OutfitAnchor");
                if (torso != null && outfit != null)
                {
                    hideAfterWarning = new[]
                    {
                        torso.gameObject,
                        outfit.gameObject
                    };
                }
                else if (torso != null)
                {
                    hideAfterWarning = new[] { torso.gameObject };
                }
            }
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

            _initialHiddenObjectStates = new bool[hideAfterWarning.Length];
            for (var index = 0; index < hideAfterWarning.Length; index++)
            {
                _initialHiddenObjectStates[index] =
                    hideAfterWarning[index] != null &&
                    hideAfterWarning[index].activeSelf;
            }
            _captured = true;
        }

        private void SetHiddenObjectsActive(bool restoreInitialState)
        {
            for (var index = 0; index < hideAfterWarning.Length; index++)
            {
                var target = hideAfterWarning[index];
                if (target == null)
                {
                    continue;
                }

                target.SetActive(
                    restoreInitialState &&
                    index < _initialHiddenObjectStates.Length &&
                    _initialHiddenObjectStates[index]);
            }
        }

        private void EnforceWarnedPose()
        {
            if (visualPoseRoot != null)
            {
                visualPoseRoot.localPosition =
                    _initialRootLocalPosition + warnedRootOffset;
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
    }
}
