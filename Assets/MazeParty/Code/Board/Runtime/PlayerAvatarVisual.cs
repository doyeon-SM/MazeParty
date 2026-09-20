using UnityEngine;
using UnityEngine.Rendering;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Builds and animates the modular clay avatar independently from the player
    /// network/movement root. Authored meshes can replace individual anchors later
    /// without changing gameplay or replication code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAvatarVisual : MonoBehaviour
    {
        public const float StandingControllerHeight = 2f;
        public const float CrouchingControllerHeight = 1.2f;
        public const float StandingControllerCenterY = 0f;
        public const float CrouchingControllerCenterY = -0.4f;
        public const float StandingEyeHeight = 0.75f;
        public const float CrouchingEyeHeight = 0.12f;

        private static readonly Color DefaultBodyColor = new Color(0.95f, 0.25f, 0.25f);
        private static readonly Color FeatureColor = new Color(0.025f, 0.02f, 0.02f);
        private static readonly Color HatColor = new Color(0.12f, 0.28f, 0.7f);
        private static readonly Color BlasterColor = new Color(0.18f, 0.72f, 1f);
        private static readonly Color MineColor = new Color(1f, 0.62f, 0.12f);
        private static readonly Color MedKitColor = new Color(0.92f, 0.16f, 0.18f);

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
        private Transform _firstPersonHands;
        private Transform _firstPersonLeftHand;
        private Transform _firstPersonRightHand;
        private Transform _worldItemRoot;
        private Transform _firstPersonItemRoot;
        private GameObject _topViewHighlight;
        private readonly Transform[] _worldItemModels = new Transform[4];
        private readonly Transform[] _firstPersonItemModels = new Transform[4];
        private CapsuleCollider _bodyHitbox;
        private SphereCollider _headHitbox;
        private SphereCollider _leftHandHitbox;
        private SphereCollider _rightHandHitbox;
        private Material _clayMaterial;
        private Material _featureMaterial;
        private Material _hatMaterial;
        private Material _blasterMaterial;
        private Material _mineMaterial;
        private Material _medKitMaterial;
        private Material _itemAccentMaterial;
        private MaterialPropertyBlock _bodyProperties;
        private Vector3 _previousPosition;
        private float _speed;
        private float _crouchBlend;
        private float _leftPunchTimer;
        private float _rightPunchTimer;
        private float _bodyHitTimer;
        private float _headHitTimer;
        private float _handHitTimer;
        private float _itemUseTimer;
        private PrototypeItemId _activeItemId;
        private bool _nextPunchUsesRightHand = true;
        private bool _crouching;
        private bool _eliminated;
        private bool _ownerFirstPerson;
        private byte _hatId;
        private Color _bodyColor = DefaultBodyColor;

        public bool IsBuilt => _worldModel != null;
        public bool IsCrouching => _crouching;
        public Color BodyColor => _bodyColor;
        public bool IsUsingItem => _itemUseTimer > 0f;

        private void Awake()
        {
            EnsureBuilt();
        }

        private void OnDestroy()
        {
            DestroyRuntimeMaterial(_clayMaterial);
            DestroyRuntimeMaterial(_featureMaterial);
            DestroyRuntimeMaterial(_hatMaterial);
            DestroyRuntimeMaterial(_blasterMaterial);
            DestroyRuntimeMaterial(_mineMaterial);
            DestroyRuntimeMaterial(_medKitMaterial);
            DestroyRuntimeMaterial(_itemAccentMaterial);
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

            _clayMaterial = CreateClayMaterial("MazeParty Clay Body", DefaultBodyColor, 0.22f);
            _featureMaterial = CreateClayMaterial("MazeParty Face", FeatureColor, 0.12f);
            _hatMaterial = CreateClayMaterial("MazeParty Test Hat", HatColor, 0.2f);
            _blasterMaterial = CreateClayMaterial("MazeParty Pulse Blaster", BlasterColor, 0.25f);
            _mineMaterial = CreateClayMaterial("MazeParty Push Mine", MineColor, 0.18f);
            _medKitMaterial = CreateClayMaterial("MazeParty Med Kit", MedKitColor, 0.18f);
            _itemAccentMaterial = CreateClayMaterial("MazeParty Item Accent", Color.white, 0.16f);
            _bodyProperties = new MaterialPropertyBlock();

            _visualRoot = CreateAnchor(transform, "VisualRoot");
            _worldModel = CreateAnchor(_visualRoot, "WorldModel");
            _body = CreatePart(_worldModel, "BodyAnchor", PrimitiveType.Capsule, _clayMaterial);
            _head = CreatePart(_worldModel, "HeadAnchor", PrimitiveType.Sphere, _clayMaterial);
            _leftHand = CreatePart(_worldModel, "LeftHandAnchor", PrimitiveType.Sphere, _clayMaterial);
            _rightHand = CreatePart(_worldModel, "RightHandAnchor", PrimitiveType.Sphere, _clayMaterial);
            _leftEye = CreatePart(_worldModel, "LeftEyeAnchor", PrimitiveType.Sphere, _featureMaterial);
            _rightEye = CreatePart(_worldModel, "RightEyeAnchor", PrimitiveType.Sphere, _featureMaterial);
            _mouth = CreatePart(_worldModel, "MouthAnchor", PrimitiveType.Sphere, _featureMaterial);
            _hat = CreateAnchor(_worldModel, "HatAnchor");
            _outfit = CreateAnchor(_worldModel, "OutfitAnchor");
            BuildTestHat();
            _worldItemRoot = CreateAnchor(_worldModel, "ItemUseAnchor");
            BuildItemModels(_worldItemRoot, _worldItemModels, false);
            BuildNameplate();
            BuildHitboxes();

            _previousPosition = transform.position;
            ApplyBodyColor();
            ApplyAppearance(0, 0, 0);
            UpdatePose(true);
            RefreshVisibility();
        }

        public void ConfigureEyePivot(Transform eyePivot)
        {
            EnsureBuilt();
            if (eyePivot == null || _firstPersonHands != null)
            {
                return;
            }

            _firstPersonHands = CreateAnchor(eyePivot, "FirstPersonHands");
            _firstPersonLeftHand = CreatePart(
                _firstPersonHands,
                "FirstPersonLeftHand",
                PrimitiveType.Sphere,
                _clayMaterial);
            _firstPersonRightHand = CreatePart(
                _firstPersonHands,
                "FirstPersonRightHand",
                PrimitiveType.Sphere,
                _clayMaterial);
            _firstPersonLeftHand.localPosition = new Vector3(-0.27f, -0.24f, 0.52f);
            _firstPersonRightHand.localPosition = new Vector3(0.27f, -0.24f, 0.52f);
            _firstPersonLeftHand.localScale = Vector3.one * 0.22f;
            _firstPersonRightHand.localScale = Vector3.one * 0.22f;
            _firstPersonItemRoot = CreateAnchor(eyePivot, "FirstPersonItemUseAnchor");
            BuildItemModels(_firstPersonItemRoot, _firstPersonItemModels, true);
            ApplyBodyColor();
            RefreshVisibility();
        }

        public void SetBodyColor(Color color)
        {
            EnsureBuilt();
            color.a = 1f;
            _bodyColor = color;
            ApplyBodyColor();
        }

        public void ApplyAppearance(byte eyeId, byte mouthId, byte hatId)
        {
            EnsureBuilt();
            // IDs are intentionally independent so additional face combinations can
            // be dropped into these fixed anchors without changing save/network data.
            _leftEye.gameObject.SetActive(eyeId == 0);
            _rightEye.gameObject.SetActive(eyeId == 0);
            _mouth.gameObject.SetActive(mouthId == 0);
            _hatId = hatId;
            _hat.gameObject.SetActive(hatId == 1);
        }

        public void SetDisplayName(string value)
        {
            EnsureBuilt();
            _nameText.text = string.IsNullOrWhiteSpace(value) ? "Player" : value.Trim();
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
            if (highlighted && _topViewHighlight == null)
            {
                _topViewHighlight = TopViewHighlightUtility.CreateSquareOutline(
                    transform,
                    "Local Player Top View Highlight",
                    0.72f,
                    0.09f,
                    -0.98f);
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
            if (useRightHand)
            {
                _rightPunchTimer = 1f;
            }
            else
            {
                _leftPunchTimer = 1f;
            }
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
            _bodyHitTimer = Mathf.MoveTowards(_bodyHitTimer, 0f, Time.deltaTime * 1.9f);
            _headHitTimer = Mathf.MoveTowards(_headHitTimer, 0f, Time.deltaTime * 1.9f);
            _handHitTimer = Mathf.MoveTowards(_handHitTimer, 0f, Time.deltaTime * 2.5f);
            var wasUsingItem = _itemUseTimer > 0f;
            _itemUseTimer = Mathf.MoveTowards(_itemUseTimer, 0f, Time.deltaTime);
            if (wasUsingItem && _itemUseTimer <= 0f)
            {
                _activeItemId = PrototypeItemId.None;
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
            _leftHand.localPosition = new Vector3(-0.58f, handY, swing + leftPunch * 0.62f);
            _rightHand.localPosition = new Vector3(0.58f, handY, -swing + rightPunch * 0.62f);
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
            _mouth.localScale = new Vector3(0.2f, 0.055f, 0.045f);
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
                    new Vector3(-0.27f, -0.24f, 0.52f + leftPunch * 0.45f);
                _firstPersonRightHand.localPosition =
                    new Vector3(0.27f, -0.24f, 0.52f + rightPunch * 0.45f);
            }
            if (_firstPersonItemRoot != null)
            {
                var itemLift = Mathf.Sin((0.55f - _itemUseTimer) * Mathf.PI / 0.55f) * 0.04f;
                _firstPersonItemRoot.localPosition = new Vector3(0f, -0.2f + itemLift, 0.58f);
            }

            UpdateHitboxPose(t);
        }

        private void BuildTestHat()
        {
            var crown = CreatePart(_hat, "TestHatCrown", PrimitiveType.Sphere, _hatMaterial);
            crown.localPosition = new Vector3(0f, 0.08f, 0f);
            crown.localScale = new Vector3(0.62f, 0.34f, 0.62f);
            var brim = CreatePart(_hat, "TestHatBrim", PrimitiveType.Cylinder, _hatMaterial);
            brim.localPosition = new Vector3(0f, -0.05f, 0.08f);
            brim.localScale = new Vector3(0.48f, 0.025f, 0.62f);
        }

        private void BuildItemModels(
            Transform parent,
            Transform[] models,
            bool firstPerson)
        {
            models[(int)PrototypeItemId.PulseBlaster] = BuildPulseBlaster(parent);
            models[(int)PrototypeItemId.PushMine] = BuildPushMine(parent);
            models[(int)PrototypeItemId.MedKit] = BuildMedKit(parent);
            parent.localScale = Vector3.one * (firstPerson ? 0.78f : 0.72f);
            SetItemModelVisibility(models, PrototypeItemId.None);
        }

        private Transform BuildPulseBlaster(Transform parent)
        {
            var root = CreateAnchor(parent, "PulseBlasterModel");
            var barrel = CreatePart(root, "Barrel", PrimitiveType.Cylinder, _blasterMaterial);
            barrel.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.localScale = new Vector3(0.16f, 0.34f, 0.16f);
            var muzzle = CreatePart(root, "Muzzle", PrimitiveType.Cylinder, _itemAccentMaterial);
            muzzle.localPosition = new Vector3(0f, 0f, 0.35f);
            muzzle.localRotation = Quaternion.Euler(90f, 0f, 0f);
            muzzle.localScale = new Vector3(0.11f, 0.055f, 0.11f);
            var grip = CreatePart(root, "Grip", PrimitiveType.Cube, _featureMaterial);
            grip.localPosition = new Vector3(0f, -0.22f, -0.08f);
            grip.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            grip.localScale = new Vector3(0.14f, 0.3f, 0.14f);
            return root;
        }

        private Transform BuildPushMine(Transform parent)
        {
            var root = CreateAnchor(parent, "PushMineModel");
            var shell = CreatePart(root, "Shell", PrimitiveType.Cylinder, _mineMaterial);
            shell.localScale = new Vector3(0.36f, 0.09f, 0.36f);
            var light = CreatePart(root, "Indicator", PrimitiveType.Sphere, _itemAccentMaterial);
            light.localPosition = new Vector3(0f, 0.12f, 0f);
            light.localScale = Vector3.one * 0.12f;
            return root;
        }

        private Transform BuildMedKit(Transform parent)
        {
            var root = CreateAnchor(parent, "MedKitModel");
            var casePart = CreatePart(root, "Case", PrimitiveType.Cube, _medKitMaterial);
            casePart.localScale = new Vector3(0.48f, 0.34f, 0.18f);
            var vertical = CreatePart(root, "CrossVertical", PrimitiveType.Cube, _itemAccentMaterial);
            vertical.localPosition = new Vector3(0f, 0f, 0.095f);
            vertical.localScale = new Vector3(0.09f, 0.24f, 0.025f);
            var horizontal = CreatePart(root, "CrossHorizontal", PrimitiveType.Cube, _itemAccentMaterial);
            horizontal.localPosition = new Vector3(0f, 0f, 0.096f);
            horizontal.localScale = new Vector3(0.23f, 0.09f, 0.025f);
            return root;
        }

        private void BuildNameplate()
        {
            _nameplate = CreateAnchor(_visualRoot, "NameplateAnchor");
            var textObject = new GameObject("PlayerName");
            textObject.transform.SetParent(_nameplate, false);
            _nameText = textObject.AddComponent<TextMesh>();
            _nameText.text = "Player";
            _nameText.anchor = TextAnchor.MiddleCenter;
            _nameText.alignment = TextAlignment.Center;
            _nameText.fontSize = 64;
            _nameText.characterSize = 0.025f;
            _nameText.color = Color.white;
            var font = Resources.Load<Font>("MazeParty/Fonts/PlayerNameFont");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            if (font != null)
            {
                _nameText.font = font;
            }
            WorldTextOcclusion.Apply(_nameText);
        }

        private void BuildHitboxes()
        {
            var root = CreateAnchor(transform, "HitboxRoot");
            _bodyHitbox = CreateHitbox<CapsuleCollider>(root, "BodyHitbox", PlayerHitRegion.Body);
            _bodyHitbox.radius = 0.36f;
            _headHitbox = CreateHitbox<SphereCollider>(root, "HeadHitbox", PlayerHitRegion.Head);
            _headHitbox.radius = 0.38f;
            _leftHandHitbox = CreateHitbox<SphereCollider>(root, "LeftHandHitbox", PlayerHitRegion.Hand);
            _leftHandHitbox.radius = 0.16f;
            _rightHandHitbox = CreateHitbox<SphereCollider>(root, "RightHandHitbox", PlayerHitRegion.Hand);
            _rightHandHitbox.radius = 0.16f;
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
            ApplyProperties(_body, _bodyProperties);
            ApplyProperties(_head, _bodyProperties);
            ApplyProperties(_leftHand, _bodyProperties);
            ApplyProperties(_rightHand, _bodyProperties);
            ApplyProperties(_firstPersonLeftHand, _bodyProperties);
            ApplyProperties(_firstPersonRightHand, _bodyProperties);
        }

        private void RefreshVisibility()
        {
            var showingItem = IsUsingItem &&
                              PrototypeItemCatalog.IsValid(_activeItemId);
            if (_worldModel != null)
            {
                _worldModel.gameObject.SetActive(!_ownerFirstPerson);
            }
            if (_nameplate != null)
            {
                _nameplate.gameObject.SetActive(!_ownerFirstPerson);
            }
            if (_firstPersonHands != null)
            {
                _firstPersonHands.gameObject.SetActive(_ownerFirstPerson && !showingItem);
            }
            if (_leftHand != null)
            {
                _leftHand.gameObject.SetActive(!showingItem);
            }
            if (_rightHand != null)
            {
                _rightHand.gameObject.SetActive(!showingItem);
            }
            if (_worldItemRoot != null)
            {
                _worldItemRoot.gameObject.SetActive(showingItem);
                SetItemModelVisibility(_worldItemModels, _activeItemId);
            }
            if (_firstPersonItemRoot != null)
            {
                _firstPersonItemRoot.gameObject.SetActive(_ownerFirstPerson && showingItem);
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

        private static Transform CreateAnchor(Transform parent, string name)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            return anchor;
        }

        private static Transform CreatePart(
            Transform parent,
            string name,
            PrimitiveType primitive,
            Material material)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            var collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return part.transform;
        }

        private static T CreateHitbox<T>(
            Transform parent,
            string name,
            PlayerHitRegion region)
            where T : Collider
        {
            var hitbox = new GameObject(name);
            hitbox.transform.SetParent(parent, false);
            var zone = hitbox.AddComponent<PlayerHitZone>();
            zone.Configure(region);
            var collider = hitbox.AddComponent<T>();
            collider.isTrigger = true;
            return collider;
        }

        private static Material CreateClayMaterial(
            string name,
            Color color,
            float smoothness)
        {
            var material = WorldTextOcclusion.CreateBuildSafeLitMaterial(name);
            if (material == null)
            {
                return null;
            }
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private static void ApplyProperties(Transform target, MaterialPropertyBlock properties)
        {
            if (target != null && target.TryGetComponent<Renderer>(out var renderer))
            {
                renderer.SetPropertyBlock(properties);
            }
        }

        private static void DestroyRuntimeMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }
    }
}
