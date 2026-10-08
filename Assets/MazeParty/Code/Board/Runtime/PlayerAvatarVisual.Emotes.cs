using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public sealed partial class PlayerAvatarVisual
    {
        private enum HandFingerPose
        {
            Closed,
            OpenPalm,
            MiddleOnly,
            Heart
        }

        private sealed class HandRigPose
        {
            public Transform Root;
            public Vector3 RootLocalPosition;
            public Quaternion RootLocalRotation;
            public Transform[] Bones = Array.Empty<Transform>();
            public Quaternion[] ClosedRotations = Array.Empty<Quaternion>();
            public bool[] MiddleFingerBones = Array.Empty<bool>();
        }

        private HandRigPose _worldLeftRig;
        private HandRigPose _worldRightRig;
        private HandRigPose _firstLeftRig;
        private HandRigPose _firstRightRig;
        private byte _baseExpression;
        private byte _gestureExpression;
        private float _gestureStartedAt;

        private void InitializeHandEmoteRigs()
        {
            _worldLeftRig = CacheHandRig(_leftHand);
            _worldRightRig = CacheHandRig(_rightHand);
            _firstLeftRig = CacheHandRig(_firstPersonLeftHand);
            _firstRightRig = CacheHandRig(_firstPersonRightHand);
        }

        private static HandRigPose CacheHandRig(Transform root)
        {
            if (root == null)
            {
                return null;
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            var count = 0;
            for (var index = 0; index < transforms.Length; index++)
            {
                if (IsFingerBone(transforms[index].name)) count++;
            }

            var result = new HandRigPose
            {
                Root = root,
                RootLocalPosition = root.localPosition,
                RootLocalRotation = root.localRotation,
                Bones = new Transform[count],
                ClosedRotations = new Quaternion[count],
                MiddleFingerBones = new bool[count]
            };
            var write = 0;
            for (var index = 0; index < transforms.Length; index++)
            {
                var bone = transforms[index];
                if (!IsFingerBone(bone.name)) continue;
                result.Bones[write] = bone;
                result.ClosedRotations[write] = bone.localRotation;
                result.MiddleFingerBones[write] =
                    bone.name.StartsWith("Middle", StringComparison.Ordinal);
                write++;
            }
            return result;
        }

        private static bool IsFingerBone(string name)
        {
            return name == "Thumb" || name == "Thumb2" ||
                   name.StartsWith("Index", StringComparison.Ordinal) ||
                   name.StartsWith("Middle", StringComparison.Ordinal) ||
                   name.StartsWith("Ring", StringComparison.Ordinal) ||
                   name.StartsWith("Little", StringComparison.Ordinal);
        }

        private void RefreshCurrentFace()
        {
            if (_faceSprite == null) return;
            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog == null || catalog.Faces.Length == 0) return;
            var id = _gesture > 0 &&
                     (HandEmoteId)_gesture != HandEmoteId.EyesCover
                ? _gestureExpression
                : _baseExpression;
            _faceSprite.sprite = catalog.Faces[
                PlayerExpressionCatalog.SanitizeFace(id)].Sprite;
            _leftEye.gameObject.SetActive(false);
            _rightEye.gameObject.SetActive(false);
            _mouth.gameObject.SetActive(false);
        }

        private void UpdateHandGesturePose()
        {
            if (_gesture == 0) return;

            var progress = Mathf.Clamp01(
                (Time.time - _gestureStartedAt) /
                (float)HandEmoteRules.Duration);
            var blend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Min(progress / 0.12f, (1f - progress) / 0.12f));
            HandEmoteRotationPose authoredRotations = null;
            var greetingWaveDegrees = 22f;
            bindings?.TryGetHandEmoteRotation(
                (HandEmoteId)_gesture,
                out authoredRotations,
                out greetingWaveDegrees);
            ApplyGestureToHands(
                _leftHand,
                _rightHand,
                _worldLeftRig,
                _worldRightRig,
                false,
                (HandEmoteId)_gesture,
                progress,
                blend,
                authoredRotations,
                greetingWaveDegrees);
            ApplyGestureToHands(
                _firstPersonLeftHand,
                _firstPersonRightHand,
                _firstLeftRig,
                _firstRightRig,
                true,
                (HandEmoteId)_gesture,
                progress,
                blend,
                authoredRotations,
                greetingWaveDegrees);
        }

        private static void ApplyGestureToHands(
            Transform left,
            Transform right,
            HandRigPose leftRig,
            HandRigPose rightRig,
            bool firstPerson,
            HandEmoteId gesture,
            float progress,
            float blend,
            HandEmoteRotationPose authoredRotations,
            float greetingWaveDegrees)
        {
            if (left == null || right == null) return;
            var leftPosition = left.localPosition;
            var rightPosition = right.localPosition;
            var leftRotation = left.localRotation;
            var rightRotation = right.localRotation;
            var leftFingerPose = HandFingerPose.Closed;
            var rightFingerPose = HandFingerPose.Closed;

            switch (gesture)
            {
                case HandEmoteId.Greeting:
                    rightPosition = firstPerson
                        ? new Vector3(0.28f, 0.04f, 0.48f)
                        : new Vector3(0.48f, 0.66f, 0.3f);
                    rightRotation = Quaternion.Euler(
                        0f,
                        0f,
                        Mathf.Sin(progress * Mathf.PI * 4f) * 22f);
                    rightFingerPose = HandFingerPose.OpenPalm;
                    break;
                case HandEmoteId.Salute:
                    rightPosition = firstPerson
                        ? new Vector3(0.2f, 0.08f, 0.43f)
                        : new Vector3(0.32f, 0.72f, 0.31f);
                    rightRotation = Quaternion.Euler(0f, 0f, -32f);
                    rightFingerPose = HandFingerPose.OpenPalm;
                    break;
                case HandEmoteId.Insult:
                    leftPosition = firstPerson
                        ? new Vector3(-0.15f, -0.03f, 0.55f)
                        : new Vector3(-0.28f, 0.3f, 0.48f);
                    rightPosition = firstPerson
                        ? new Vector3(0.15f, -0.03f, 0.55f)
                        : new Vector3(0.28f, 0.3f, 0.48f);
                    leftFingerPose = HandFingerPose.MiddleOnly;
                    rightFingerPose = HandFingerPose.MiddleOnly;
                    break;
                case HandEmoteId.Heart:
                    leftPosition = firstPerson
                        ? new Vector3(-0.09f, -0.04f, 0.53f)
                        : new Vector3(-0.15f, 0.2f, 0.45f);
                    rightPosition = firstPerson
                        ? new Vector3(0.09f, -0.04f, 0.53f)
                        : new Vector3(0.15f, 0.2f, 0.45f);
                    leftRotation = Quaternion.Euler(0f, 0f, -42f);
                    rightRotation = Quaternion.Euler(0f, 0f, 42f);
                    leftFingerPose = HandFingerPose.Heart;
                    rightFingerPose = HandFingerPose.Heart;
                    break;
                case HandEmoteId.Surprise:
                    leftPosition = firstPerson
                        ? new Vector3(-0.25f, 0.01f, 0.48f)
                        : new Vector3(-0.46f, 0.45f, 0.38f);
                    rightPosition = firstPerson
                        ? new Vector3(0.25f, 0.01f, 0.48f)
                        : new Vector3(0.46f, 0.45f, 0.38f);
                    leftRotation = Quaternion.Euler(0f, 0f, 18f);
                    rightRotation = Quaternion.Euler(0f, 0f, -18f);
                    leftFingerPose = HandFingerPose.OpenPalm;
                    rightFingerPose = HandFingerPose.OpenPalm;
                    break;
                case HandEmoteId.Surrender:
                    leftPosition = firstPerson
                        ? new Vector3(-0.29f, 0.1f, 0.44f)
                        : new Vector3(-0.47f, 0.76f, 0.27f);
                    rightPosition = firstPerson
                        ? new Vector3(0.29f, 0.1f, 0.44f)
                        : new Vector3(0.47f, 0.76f, 0.27f);
                    leftRotation = Quaternion.Euler(0f, 0f, 10f);
                    rightRotation = Quaternion.Euler(0f, 0f, -10f);
                    leftFingerPose = HandFingerPose.OpenPalm;
                    rightFingerPose = HandFingerPose.OpenPalm;
                    break;
                case HandEmoteId.Pleading:
                    leftPosition = firstPerson
                        ? new Vector3(-0.055f, -0.03f, 0.55f)
                        : new Vector3(-0.08f, 0.22f, 0.46f);
                    rightPosition = firstPerson
                        ? new Vector3(0.055f, -0.03f, 0.55f)
                        : new Vector3(0.08f, 0.22f, 0.46f);
                    leftRotation = Quaternion.Euler(0f, 0f, -82f);
                    rightRotation = Quaternion.Euler(0f, 0f, 82f);
                    leftFingerPose = HandFingerPose.OpenPalm;
                    rightFingerPose = HandFingerPose.OpenPalm;
                    break;
                case HandEmoteId.EyesCover:
                    leftPosition = firstPerson
                        ? new Vector3(-0.1f, 0.06f, 0.36f)
                        : new Vector3(-0.16f, 0.7f, 0.38f);
                    rightPosition = firstPerson
                        ? new Vector3(0.1f, 0.06f, 0.36f)
                        : new Vector3(0.16f, 0.7f, 0.38f);
                    leftRotation = Quaternion.Euler(0f, 0f, 8f);
                    rightRotation = Quaternion.Euler(0f, 0f, -8f);
                    leftFingerPose = HandFingerPose.OpenPalm;
                    rightFingerPose = HandFingerPose.OpenPalm;
                    break;
            }

            if (authoredRotations != null)
            {
                var leftEuler = firstPerson
                    ? authoredRotations.FirstPersonLeftEuler
                    : authoredRotations.WorldLeftEuler;
                var rightEuler = firstPerson
                    ? authoredRotations.FirstPersonRightEuler
                    : authoredRotations.WorldRightEuler;
                if (gesture == HandEmoteId.Greeting)
                {
                    rightEuler.z += Mathf.Sin(
                        progress * Mathf.PI * 4f) * greetingWaveDegrees;
                }
                leftRotation = Quaternion.Euler(leftEuler);
                rightRotation = Quaternion.Euler(rightEuler);
            }

            left.localPosition = Vector3.Lerp(left.localPosition, leftPosition, blend);
            right.localPosition = Vector3.Lerp(right.localPosition, rightPosition, blend);
            left.localRotation = Quaternion.Slerp(left.localRotation, leftRotation, blend);
            right.localRotation = Quaternion.Slerp(right.localRotation, rightRotation, blend);
            ApplyFingerPose(leftRig, leftFingerPose, blend);
            ApplyFingerPose(rightRig, rightFingerPose, blend);
        }

        private void RestoreHandEmotePose()
        {
            RestoreRoot(_worldLeftRig);
            RestoreRoot(_worldRightRig);
            RestoreRoot(_firstLeftRig);
            RestoreRoot(_firstRightRig);
            ResetRig(_worldLeftRig);
            ResetRig(_worldRightRig);
            ResetRig(_firstLeftRig);
            ResetRig(_firstRightRig);
        }

        private static void RestoreRoot(HandRigPose rig)
        {
            if (rig == null || rig.Root == null) return;
            rig.Root.localPosition = rig.RootLocalPosition;
            rig.Root.localRotation = rig.RootLocalRotation;
        }

        private static void ResetRig(HandRigPose rig)
        {
            if (rig == null) return;
            for (var index = 0; index < rig.Bones.Length; index++)
            {
                if (rig.Bones[index] != null)
                {
                    rig.Bones[index].localRotation = rig.ClosedRotations[index];
                }
            }
        }

        private static void ApplyFingerPose(
            HandRigPose rig,
            HandFingerPose pose,
            float blend)
        {
            if (rig == null) return;
            for (var index = 0; index < rig.Bones.Length; index++)
            {
                var bone = rig.Bones[index];
                var shouldOpen = pose == HandFingerPose.OpenPalm ||
                                 (pose == HandFingerPose.MiddleOnly &&
                                  rig.MiddleFingerBones[index]) ||
                                 (pose == HandFingerPose.Heart &&
                                  IsHeartFingerBone(bone.name));
                if (shouldOpen && rig.Bones[index] != null)
                {
                    rig.Bones[index].localRotation = Quaternion.Slerp(
                        rig.ClosedRotations[index],
                        GetOpenFingerRotation(bone.name),
                        blend);
                }
            }
        }

        private static bool IsHeartFingerBone(string name)
        {
            return name.StartsWith("Thumb", StringComparison.Ordinal) ||
                   name.StartsWith("Index", StringComparison.Ordinal);
        }

        private static Quaternion GetOpenFingerRotation(string name)
        {
            switch (name)
            {
                case "Thumb": return Quaternion.Euler(0f, 0f, 54.6f);
                case "Thumb2": return Quaternion.Euler(0f, 0f, -11.9f);
                case "IndexFinger": return Quaternion.Euler(0f, 0f, 5.35f);
                case "Index2": return Quaternion.Euler(0f, 0f, -0.28f);
                case "Index3": return Quaternion.Euler(0f, 0f, -1.43f);
                case "MiddleFinger": return Quaternion.Euler(0f, 0f, 0.11f);
                case "Middle2": return Quaternion.Euler(0f, 0f, -0.31f);
                case "Middle3": return Quaternion.Euler(0f, 0f, 0.35f);
                case "RingFinger": return Quaternion.Euler(0f, 0f, -2.75f);
                case "Ring2": return Quaternion.Euler(0f, 0f, -1.07f);
                case "Ring3": return Quaternion.Euler(0f, 0f, 1.95f);
                case "LittleFinger": return Quaternion.Euler(0f, 0f, -6.57f);
                case "Little2": return Quaternion.Euler(0f, 0f, -1.92f);
                case "Little3": return Quaternion.Euler(0f, 0f, 0.95f);
                default: return Quaternion.identity;
            }
        }
    }
}
