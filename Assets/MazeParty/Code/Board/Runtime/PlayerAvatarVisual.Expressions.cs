using UnityEngine;
using UnityEngine.Rendering;

namespace MazeParty.Gameplay
{
    public sealed partial class PlayerAvatarVisual
    {
        private SpriteRenderer _faceSprite;
        private Transform _worldGestureRoot, _firstGestureRoot;
        private GameObject[] _worldGestures, _firstGestures;
        private byte _gesture;
        public byte CurrentHandGesture => _gesture;
        private void BuildExpressionVisuals()
        {
            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog == null) return;
            var face = new GameObject("Face Sprite");
            face.transform.SetParent(_head, false);
            face.transform.localPosition = new Vector3(0, 0, .51f);
            face.transform.localScale = Vector3.one * .8f;
            _faceSprite = face.AddComponent<SpriteRenderer>();
            _faceSprite.shadowCastingMode = ShadowCastingMode.Off;
            _faceSprite.receiveShadows = false;
            _worldGestureRoot = CreateAnchor(_worldModel, "Gesture Hands");
            _worldGestures = InstantiateGestures(_worldGestureRoot);
            SetFaceExpression(0);
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
        private void BuildFirstPersonGestures(Transform eye)
        {
            if (PlayerExpressionCatalog.Instance == null) return;
            _firstGestureRoot = CreateAnchor(eye, "First Person Gesture Hands");
            _firstGestureRoot.localPosition = new Vector3(0, -.22f, .65f);
            _firstGestureRoot.localScale = Vector3.one * .55f;
            _firstGestureRoot.localRotation = Quaternion.Euler(0, 180, 0);
            _firstGestures = InstantiateGestures(_firstGestureRoot);
        }
        public void SetFaceExpression(byte id)
        {
            if (_faceSprite == null) return;
            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog.Faces.Length == 0) return;
            _faceSprite.sprite = catalog.Faces[PlayerExpressionCatalog.SanitizeFace(id)].Sprite;
            _leftEye.gameObject.SetActive(false); _rightEye.gameObject.SetActive(false); _mouth.gameObject.SetActive(false);
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
