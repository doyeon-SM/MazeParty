using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Builds and animates the modular clay avatar independently from the player
    /// network/movement root. Authored meshes can replace individual anchors later
    /// without changing gameplay or replication code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class PlayerAvatarVisual : MonoBehaviour
    {
        // The root CharacterController is the sole solid movement collider.
        // Authored body, head, and hand colliders remain trigger hit zones.
        public const float MovementControllerRadius = 0.42f;
        public const float StandingControllerHeight = 2f;
        public const float CrouchingControllerHeight = 1.2f;
        public const float StandingControllerCenterY = 0f;
        public const float CrouchingControllerCenterY = -0.4f;
        public const float StandingEyeHeight = 0.75f;
        public const float CrouchingEyeHeight = 0.12f;

        private static readonly Color DefaultBodyColor = new Color(0.95f, 0.25f, 0.25f);

        [SerializeField]
        private PlayerAvatarPresentationBindings bindings;

        private Transform _visualRoot;
        private Transform _worldModel;
        private Transform _body;
        private Transform _head;
        private Transform _leftHand;
        private Transform _rightHand;
        private Transform _leftEye;
        private Transform _rightEye;
        private Transform _mouth;
        private Transform _hat;
        private Transform _outfit;
        private Transform _nameplate;
        private TextMesh _nameText;
        private Renderer _nameRenderer;
        private Transform _firstPersonHands;
        private Transform _firstPersonPresentation;
        private Transform _firstPersonLeftHand;
        private Transform _firstPersonRightHand;
        private Transform _worldItemRoot;
        private Transform _firstPersonItemRoot;
        private GameObject _topViewHighlight;
        private readonly Transform[] _worldItemModels = new Transform[13];
        private readonly Transform[] _firstPersonItemModels = new Transform[13];
        private CapsuleCollider _bodyHitbox;
        private SphereCollider _headHitbox;
        private SphereCollider _leftHandHitbox;
        private SphereCollider _rightHandHitbox;
        private MaterialPropertyBlock _bodyProperties;
        private Vector3 _previousPosition;
        private float _speed;
        private float _crouchBlend;
        private float _leftPunchTimer;
        private float _rightPunchTimer;
        private float _pushTimer;
        private float _bodyHitTimer;
        private float _headHitTimer;
        private float _handHitTimer;
        private float _itemUseTimer;
        private PrototypeItemId _activeItemId;
        private PrototypeItemId _equippedItemId;
        private bool _nextPunchUsesRightHand = true;
        private bool _crouching;
        private bool _eliminated;
        private bool _ownerFirstPerson;
        private bool _hiddenFromViewer;
        private bool _nameplateAllowed = true;
        private bool _nameplateOccluded;
        private bool _missingPresentationReported;
        public void SetHiddenFromViewer(bool hidden)
        {
            if (_hiddenFromViewer == hidden) return;
            _hiddenFromViewer = hidden;
            RefreshVisibility();
        }
        private byte _hatId;
        private Color _bodyColor = DefaultBodyColor;
        private bool _mouthBlowing;

        public bool IsBuilt => _worldModel != null;
        public PlayerAvatarPresentationBindings Bindings => bindings;
        public bool IsCrouching => _crouching;
        public Color BodyColor => _bodyColor;
        public bool IsUsingItem => _itemUseTimer > 0f || _equippedItemId != PrototypeItemId.None;
        public Vector3 NameplateOcclusionTarget => _nameplate != null
            ? _nameplate.position
            : _head != null
                ? _head.position
                : transform.position + Vector3.up * StandingEyeHeight;

        public void SetMouthBlowing(bool blowing)
        {
            _mouthBlowing = blowing;
        }

        public void SetEquippedItem(PrototypeItemId id)
        {
            if (_equippedItemId == id) return;
            _equippedItemId = id;
            _activeItemId = id;
            RefreshVisibility();
        }

        private void Awake()
        {
            EnsureBuilt();
        }

        public void ConfigurePresentationBindings(
            PlayerAvatarPresentationBindings value)
        {
            if (IsBuilt && bindings != value)
            {
                Debug.LogError(
                    "Player avatar presentation cannot be replaced after initialization.",
                    this);
                return;
            }
            bindings = value;
            _missingPresentationReported = false;
        }

        public void EnsureBuilt()
        {
            if (IsBuilt)
            {
                return;
            }

            DisableLegacyRenderers();
            if (GetComponent<PlayerHitZoneOwner>() == null)
            {
                gameObject.AddComponent<PlayerHitZoneOwner>();
            }

            if (TryInitializeAuthoredPresentation())
            {
                return;
            }

            ReportMissingPresentation();
        }

        public void ConfigureEyePivot(Transform eyePivot)
        {
            EnsureBuilt();
            if (!IsBuilt)
            {
                return;
            }
            if (eyePivot != null && _firstPersonPresentation != null)
            {
                if (_firstPersonPresentation.parent != eyePivot)
                {
                    _firstPersonPresentation.SetParent(eyePivot, false);
                }
                RefreshVisibility();
            }
        }

        public void SetNameplateVisible(bool visible)
        {
            EnsureBuilt();
            _nameplateAllowed = visible;
            if (_nameplate != null)
            {
                _nameplate.gameObject.SetActive(
                    visible && !_ownerFirstPerson && !_hiddenFromViewer);
            }
        }

        public void SetNameplateOccluded(bool occluded)
        {
            EnsureBuilt();
            _nameplateOccluded = occluded;
            if (_nameRenderer != null)
            {
                _nameRenderer.forceRenderingOff = occluded;
            }
        }

        private bool TryInitializeAuthoredPresentation()
        {
            if (bindings == null)
            {
                bindings = GetComponentInChildren<
                    PlayerAvatarPresentationBindings>(true);
            }
            if (bindings == null)
            {
                var assets = Resources.Load<PlayerAvatarPresentationAssets>(
                    PlayerAvatarPresentationAssets.ResourcePath);
                if (assets != null && assets.HasRequiredReferences)
                {
                    var source = assets.PresentationPrefab;
                    bindings = Instantiate(source, transform, false);
                    bindings.gameObject.name = source.gameObject.name;
                }
            }
            if (bindings == null)
            {
                return false;
            }
            if (!bindings.HasRequiredReferences)
            {
                return false;
            }

            _visualRoot = bindings.transform;
            _worldModel = bindings.WorldModel;
            _body = bindings.BodyAnchor;
            _head = bindings.HeadAnchor;
            _leftHand = bindings.LeftHandAnchor;
            _rightHand = bindings.RightHandAnchor;
            _leftEye = bindings.LeftEyeAnchor;
            _rightEye = bindings.RightEyeAnchor;
            _mouth = bindings.MouthAnchor;
            _hat = bindings.HatAnchor;
            _outfit = bindings.OutfitAnchor;
            _nameplate = bindings.NameplateAnchor;
            _nameText = bindings.NameText;
            _nameRenderer = _nameText != null
                ? _nameText.GetComponent<Renderer>()
                : null;
            if (_nameRenderer != null)
            {
                _nameRenderer.forceRenderingOff = _nameplateOccluded;
            }
            _worldItemRoot = bindings.WorldItemRoot;
            _firstPersonPresentation = bindings.FirstPersonPresentation;
            _firstPersonHands = bindings.FirstPersonHands;
            _firstPersonLeftHand = bindings.FirstPersonLeftHand;
            _firstPersonRightHand = bindings.FirstPersonRightHand;
            _firstPersonItemRoot = bindings.FirstPersonItemRoot;
            _topViewHighlight = bindings.TopViewHighlight;
            _bodyHitbox = bindings.BodyHitbox;
            _headHitbox = bindings.HeadHitbox;
            _leftHandHitbox = bindings.LeftHandHitbox;
            _rightHandHitbox = bindings.RightHandHitbox;
            _bodyProperties = new MaterialPropertyBlock();

            BuildItemModels(_worldItemRoot, _worldItemModels, false);
            BuildItemModels(_firstPersonItemRoot, _firstPersonItemModels, true);
            BuildExpressionVisuals();
            _previousPosition = transform.position;
            ApplyBodyColor();
            ApplyAppearance(0, 0, 0, 0);
            UpdatePose(true);
            RefreshVisibility();
            _missingPresentationReported = false;
            return true;
        }

        private void ReportMissingPresentation()
        {
            if (_missingPresentationReported)
            {
                return;
            }

            _missingPresentationReported = true;
            var reason = bindings == null
                ? "No authored presentation binding or Resources catalog entry was found."
                : "The assigned authored presentation bindings are incomplete.";
            Debug.LogError(
                "Player avatar presentation is unavailable. " + reason +
                " Run MazeParty/Multiplayer/Install Player And Lobby Prefabs; " +
                "runtime geometry will not be generated.",
                bindings != null ? bindings.gameObject : gameObject);
        }

        public void SetBodyColor(Color color)
        {
            EnsureBuilt();
            color.a = 1f;
            _bodyColor = color;
            ApplyBodyColor();
        }

        public void ApplyAppearance(
            byte eyeId,
            byte mouthId,
            byte hatId,
            byte expressionId)
        {
            EnsureBuilt();
            if (!IsBuilt)
            {
                return;
            }
            // IDs are intentionally independent so additional face combinations can
            // be dropped into these fixed anchors without changing save/network data.
            _leftEye.gameObject.SetActive(eyeId == 0);
            _rightEye.gameObject.SetActive(eyeId == 0);
            _mouth.gameObject.SetActive(mouthId == 0);
            SetFaceExpression(expressionId);
            SetHat(hatId);
        }

        public void SetDisplayName(string value)
        {
            EnsureBuilt();
            if (!IsBuilt)
            {
                return;
            }
            _nameText.text = string.IsNullOrWhiteSpace(value) ? GameText.T("Player") : value.Trim();
        }

        public void SetCrouching(bool crouching)
        {
            EnsureBuilt();
            _crouching = crouching;
        }

        public void SetEliminated(bool eliminated)
        {
            EnsureBuilt();
            _eliminated = eliminated;
        }

        public void SetOwnerFirstPerson(bool firstPerson)
        {
            EnsureBuilt();
            _ownerFirstPerson = firstPerson;
            RefreshVisibility();
        }

        public void SetTopViewHighlight(bool highlighted)
        {
            EnsureBuilt();
            if (!IsBuilt)
            {
                return;
            }

            if (_topViewHighlight != null &&
                _topViewHighlight.activeSelf != highlighted)
            {
                _topViewHighlight.SetActive(highlighted);
            }
        }

        public void TriggerPunch()
        {
            TriggerPunch(_nextPunchUsesRightHand);
            _nextPunchUsesRightHand = !_nextPunchUsesRightHand;
        }

        public void TriggerPunch(bool useRightHand)
        {
            EnsureBuilt();
            _pushTimer = 0f;
            if (useRightHand)
            {
                _rightPunchTimer = 1f;
            }
            else
            {
                _leftPunchTimer = 1f;
            }
        }

        public void TriggerPush()
        {
            EnsureBuilt();
            _leftPunchTimer = 0f;
            _rightPunchTimer = 0f;
            _pushTimer = 1f;
        }

        public void TriggerHit()
        {
            TriggerHit(PlayerHitRegion.Body);
        }

        public void TriggerHit(PlayerHitRegion region)
        {
            EnsureBuilt();
            switch (region)
            {
                case PlayerHitRegion.Head:
                    _headHitTimer = 1f;
                    break;
                case PlayerHitRegion.Hand:
                    _handHitTimer = 1f;
                    break;
                default:
                    _bodyHitTimer = 1f;
                    break;
            }
        }

        public void TriggerItemUse(PrototypeItemId itemId)
        {
            EnsureBuilt();
            if (!PrototypeItemCatalog.IsValid(itemId))
            {
                return;
            }

            _activeItemId = itemId;
            _itemUseTimer = 0.55f;
            RefreshVisibility();
        }

        private void Update()
        {
            if (!IsBuilt)
            {
                return;
            }

            var planarDelta = transform.position - _previousPosition;
            planarDelta.y = 0f;
            _speed = Mathf.MoveTowards(
                _speed,
                planarDelta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f),
                Time.deltaTime * 14f);
            _previousPosition = transform.position;
            _crouchBlend = Mathf.MoveTowards(
                _crouchBlend,
                _crouching ? 1f : 0f,
                Time.deltaTime * 8f);
            _leftPunchTimer = Mathf.MoveTowards(_leftPunchTimer, 0f, Time.deltaTime * 4.5f);
            _rightPunchTimer = Mathf.MoveTowards(_rightPunchTimer, 0f, Time.deltaTime * 4.5f);
            _pushTimer = Mathf.MoveTowards(_pushTimer, 0f, Time.deltaTime * 4.5f);
            _bodyHitTimer = Mathf.MoveTowards(_bodyHitTimer, 0f, Time.deltaTime * 1.9f);
            _headHitTimer = Mathf.MoveTowards(_headHitTimer, 0f, Time.deltaTime * 1.9f);
            _handHitTimer = Mathf.MoveTowards(_handHitTimer, 0f, Time.deltaTime * 2.5f);
            var wasUsingItem = _itemUseTimer > 0f;
            _itemUseTimer = Mathf.MoveTowards(_itemUseTimer, 0f, Time.deltaTime);
            if (wasUsingItem && _itemUseTimer <= 0f)
            {
                _activeItemId = _equippedItemId;
                RefreshVisibility();
            }
            UpdatePose(false);
        }

        private void LateUpdate()
        {
            if (_nameplate == null || !_nameplate.gameObject.activeInHierarchy)
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            var direction = _nameplate.position - camera.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                _nameplate.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }

        private void UpdatePose(bool immediate)
        {
            var t = immediate ? (_crouching ? 1f : 0f) : _crouchBlend;
            var moving = Mathf.Clamp01(_speed / 2f);
            var time = Time.time;
            var idleBob = Mathf.Sin(time * 2.1f) * 0.018f * (1f - moving);
            var moveBob = Mathf.Abs(Mathf.Sin(time * 8.5f)) * 0.055f * moving;
            var bob = idleBob + moveBob;
            var bodyY = Mathf.Lerp(-0.12f, -0.43f, t) + bob;
            var headY = Mathf.Lerp(0.75f, 0.08f, t) + bob;
            var handY = Mathf.Lerp(-0.05f, -0.47f, t) + bob;
            var swing = Mathf.Sin(time * 8.5f) * 0.12f * moving;
            var leftPunch = PunchAmount(_leftPunchTimer);
            var rightPunch = PunchAmount(_rightPunchTimer);
            var push = PunchAmount(_pushTimer);
            var bodyWobble = WobbleAngle(_bodyHitTimer, 22f);
            var headWobble = WobbleAngle(_headHitTimer, 30f);
            var handWobble = WobbleAngle(_handHitTimer, 34f);
            var headOffsetX = headWobble / 30f * 0.11f;

            _body.localPosition = new Vector3(0f, bodyY, 0f);
            _body.localScale = new Vector3(0.82f, Mathf.Lerp(0.63f, 0.37f, t), 0.82f);
            _body.localRotation = Quaternion.Euler(0f, 0f, bodyWobble);
            _head.localPosition = new Vector3(headOffsetX, headY, 0f);
            _head.localScale = Vector3.one * 0.78f;
            _head.localRotation = Quaternion.Euler(0f, 0f, headWobble);
            _leftHand.localPosition = new Vector3(-0.58f, handY, swing + (leftPunch + push) * 0.62f);
            _rightHand.localPosition = new Vector3(0.58f, handY, -swing + (rightPunch + push) * 0.62f);
            _leftHand.localScale = Vector3.one * 0.27f;
            _rightHand.localScale = Vector3.one * 0.27f;
            _leftHand.localRotation = Quaternion.Euler(0f, 0f, handWobble);
            _rightHand.localRotation = Quaternion.Euler(0f, 0f, -handWobble);

            _leftEye.localPosition = new Vector3(-0.15f + headOffsetX, headY + 0.07f, 0.355f);
            _rightEye.localPosition = new Vector3(0.15f + headOffsetX, headY + 0.07f, 0.355f);
            _leftEye.localScale = new Vector3(0.095f, 0.13f, 0.055f);
            _rightEye.localScale = new Vector3(0.095f, 0.13f, 0.055f);
            _leftEye.localRotation = Quaternion.Euler(0f, 0f, headWobble);
            _rightEye.localRotation = Quaternion.Euler(0f, 0f, headWobble);
            _mouth.localPosition = new Vector3(headOffsetX, headY - 0.17f, 0.37f);
            _mouth.localScale = _mouthBlowing
                ? new Vector3(0.115f, 0.14f, 0.045f)
                : new Vector3(0.2f, 0.055f, 0.045f);
            _mouth.localRotation = Quaternion.Euler(0f, 0f, headWobble);
            _hat.localPosition = new Vector3(headOffsetX, headY + 0.34f, 0f);
            _hat.localRotation = Quaternion.Euler(0f, 0f, headWobble);
            _outfit.localPosition = new Vector3(0f, bodyY, 0f);
            _outfit.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.62f, t), 1f);
            _outfit.localRotation = Quaternion.Euler(0f, 0f, bodyWobble);
            _nameplate.localPosition = new Vector3(0f, Mathf.Lerp(1.5f, 0.84f, t), 0f);
            if (_worldItemRoot != null)
            {
                _worldItemRoot.localPosition = new Vector3(0f, handY + 0.08f, 0.46f);
            }

            var eliminatedTilt = _eliminated ? 84f : 0f;
            _worldModel.localRotation = Quaternion.Euler(0f, 0f, eliminatedTilt);

            if (_firstPersonHands != null)
            {
                _firstPersonLeftHand.localPosition =
                    new Vector3(-0.27f, -0.24f,
                        0.52f + (leftPunch + push) * 0.45f);
                _firstPersonRightHand.localPosition =
                    new Vector3(0.27f, -0.24f,
                        0.52f + (rightPunch + push) * 0.45f);
            }
            if (_firstPersonItemRoot != null)
            {
                var itemLift = Mathf.Sin((0.55f - _itemUseTimer) * Mathf.PI / 0.55f) * 0.04f;
                _firstPersonItemRoot.localPosition = new Vector3(0f, -0.2f + itemLift, 0.58f);
            }

            if (_worldGestureRoot != null) _worldGestureRoot.localPosition = new Vector3(0f, handY + .2f, .35f);
            UpdateHitboxPose(t);
        }

        private void BuildItemModels(
            Transform parent,
            Transform[] models,
            bool firstPerson)
        {
            foreach (var item in PrototypeItemCatalog.All)
                if (item.HeldPrefab != null)
                    models[(int)item.Id] = Instantiate(item.HeldPrefab, parent, false).transform;
            parent.localScale = Vector3.one * (firstPerson ? 0.78f : 0.72f);
            SetItemModelVisibility(models, PrototypeItemId.None);
        }

        private void UpdateHitboxPose(float crouch)
        {
            _bodyHitbox.height = Mathf.Lerp(1.22f, 0.72f, crouch);
            _bodyHitbox.center = new Vector3(0f, Mathf.Lerp(-0.12f, -0.43f, crouch), 0f);
            _headHitbox.center = new Vector3(0f, Mathf.Lerp(0.75f, 0.08f, crouch), 0f);
            _leftHandHitbox.center = new Vector3(-0.58f, Mathf.Lerp(-0.05f, -0.47f, crouch), 0f);
            _rightHandHitbox.center = new Vector3(0.58f, Mathf.Lerp(-0.05f, -0.47f, crouch), 0f);
        }

        private void ApplyBodyColor()
        {
            if (_bodyProperties == null)
            {
                return;
            }

            _bodyProperties.Clear();
            _bodyProperties.SetColor("_BaseColor", _bodyColor);
            _bodyProperties.SetColor("_Color", _bodyColor);
            if (bindings == null)
            {
                return;
            }
            var renderers = bindings.BodyTintRenderers;
            for (var index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null)
                {
                    renderers[index].SetPropertyBlock(_bodyProperties);
                }
            }
            ColorGestureModels();
        }

        private void RefreshVisibility()
        {
            var showingItem = IsUsingItem &&
                              PrototypeItemCatalog.IsValid(_activeItemId) &&
                              _worldItemModels[(int)_activeItemId] != null;
            RefreshGestureVisibility(showingItem);
            if (_worldModel != null)
            {
                _worldModel.gameObject.SetActive(!_ownerFirstPerson && !_hiddenFromViewer);
            }
            if (_nameplate != null)
            {
                _nameplate.gameObject.SetActive(
                    _nameplateAllowed && !_ownerFirstPerson && !_hiddenFromViewer);
            }
            if (_firstPersonHands != null)
            {
                _firstPersonHands.gameObject.SetActive(_ownerFirstPerson && !showingItem && _gesture == 0 && !_hiddenFromViewer);
            }
            if (_leftHand != null)
            {
                _leftHand.gameObject.SetActive(!showingItem && _gesture == 0);
            }
            if (_rightHand != null)
            {
                _rightHand.gameObject.SetActive(!showingItem && _gesture == 0);
            }
            if (_worldItemRoot != null)
            {
                _worldItemRoot.gameObject.SetActive(showingItem);
                SetItemModelVisibility(_worldItemModels, _activeItemId);
            }
            if (_firstPersonItemRoot != null)
            {
                _firstPersonItemRoot.gameObject.SetActive(_ownerFirstPerson && showingItem && !_hiddenFromViewer);
                SetItemModelVisibility(_firstPersonItemModels, _activeItemId);
            }
        }

        private static void SetItemModelVisibility(
            Transform[] models,
            PrototypeItemId activeItem)
        {
            for (var index = 0; index < models.Length; index++)
            {
                if (models[index] != null)
                {
                    models[index].gameObject.SetActive(index == (int)activeItem);
                }
            }
        }

        private static float PunchAmount(float timer)
        {
            return Mathf.Sin((1f - timer) * Mathf.PI) * timer;
        }

        private static float WobbleAngle(float timer, float maximumAngle)
        {
            if (timer <= 0f)
            {
                return 0f;
            }

            var elapsed = 1f - timer;
            return Mathf.Sin(elapsed * Mathf.PI * 2f) * timer * maximumAngle;
        }

        private void DisableLegacyRenderers()
        {
            var renderers = GetComponents<Renderer>();
            for (var index = 0; index < renderers.Length; index++)
            {
                renderers[index].enabled = false;
            }
        }

    }
}
