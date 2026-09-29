using UnityEngine;

namespace MazeParty.Gameplay
{
    public sealed partial class PlayerAvatarVisual
    {
        private SpriteRenderer _faceSprite;
        private GameObject[] _hatModels;
        private Transform _worldGestureRoot, _firstGestureRoot;
        private GameObject[] _worldGestures, _firstGestures;
        private byte _gesture;
        public byte CurrentHandGesture => _gesture;
        private void BuildExpressionVisuals()
        {
            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog == null) return;
            if (bindings == null)
            {
                return;
            }
            _faceSprite = bindings.FaceSprite;
            _hatModels = new GameObject[catalog.Hats.Length];
            _worldGestureRoot = bindings.WorldGestureRoot;
            _firstGestureRoot = bindings.FirstPersonGestureRoot;
            _worldGestures = InstantiateGestures(_worldGestureRoot);
            if (_firstGestureRoot != null)
            {
                _firstGestures = InstantiateGestures(_firstGestureRoot);
            }
            SetFaceExpression(0);
            SetHat(0);
        }
        private GameObject EnsureHatModel(int index)
        {
            if (_hatModels == null || index < 0 || index >= _hatModels.Length)
            {
                return null;
            }
            if (_hatModels[index] != null)
            {
                return _hatModels[index];
            }

            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog == null || catalog.Hats == null || index >= catalog.Hats.Length)
            {
                return null;
            }
            var hat = catalog.Hats[index];
            if (hat == null || hat.Prefab == null)
            {
                return null;
            }

            var model = Instantiate(hat.Prefab, _hat, false);
            model.name = hat.Prefab.name;
            var hatTransform = model.transform;
            hatTransform.localPosition = hat.LocalPosition;
            hatTransform.localRotation = Quaternion.Euler(hat.LocalEulerAngles);
            hatTransform.localScale = hat.LocalScale;
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                for (var blendShape = 0; blendShape < mesh.blendShapeCount; blendShape++)
                {
                    if (mesh.GetBlendShapeName(blendShape) == "Scale")
                    {
                        renderer.SetBlendShapeWeight(blendShape, 0f);
                    }
                }
            }
            model.SetActive(false);
            _hatModels[index] = model;
            return model;
        }
        private GameObject[] InstantiateGestures(Transform root)
        {
            var catalog = PlayerExpressionCatalog.Instance;
            var result = new GameObject[catalog.Gestures.Length];
            for (int i = 0; i < result.Length; i++)
            {
                if (catalog.Gestures[i].HandsPrefab == null) continue;
                result[i] = Instantiate(catalog.Gestures[i].HandsPrefab, root, false);
                result[i].SetActive(false);
            }
            return result;
        }
        public void SetFaceExpression(byte id)
        {
            if (_faceSprite == null) return;
            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog == null) return;
            if (catalog.Faces.Length == 0) return;
            _faceSprite.sprite = catalog.Faces[PlayerExpressionCatalog.SanitizeFace(id)].Sprite;
            _leftEye.gameObject.SetActive(false); _rightEye.gameObject.SetActive(false); _mouth.gameObject.SetActive(false);
        }
        private void SetHat(byte id)
        {
            _hatId = PlayerExpressionCatalog.SanitizeHat(id);
            if (_hat == null) return;
            _hat.gameObject.SetActive(_hatId > 0);
            if (_hatModels == null) return;
            if (_hatId > 0) EnsureHatModel(_hatId - 1);
            for (int i = 0; i < _hatModels.Length; i++)
                if (_hatModels[i] != null) _hatModels[i].SetActive(i + 1 == _hatId);
        }
        public void SetHandGesture(byte id)
        {
            if (_gesture == id) return;
            _gesture = id; RefreshVisibility();
        }
        private void RefreshGestureVisibility(bool showingItem)
        {
            bool active = _gesture > 0 && !showingItem && !_eliminated;
            if (_worldGestureRoot != null) _worldGestureRoot.gameObject.SetActive(active);
            if (_firstGestureRoot != null) _firstGestureRoot.gameObject.SetActive(active && _ownerFirstPerson && !_hiddenFromViewer);
            SetGestureModels(_worldGestures); SetGestureModels(_firstGestures);
        }
        private void SetGestureModels(GameObject[] models)
        { if (models != null) for (int i = 0; i < models.Length; i++) if (models[i] != null) models[i].SetActive(i + 1 == _gesture); }
        private void ColorGestureModels()
        {
            foreach (var root in new[] { _worldGestureRoot, _firstGestureRoot })
                if (root != null) foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.SetPropertyBlock(_bodyProperties);
        }
    }
}
