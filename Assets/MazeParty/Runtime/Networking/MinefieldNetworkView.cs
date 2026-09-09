using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.Minefield;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client-side presentation for the replicated Minefield simulation. The
    /// authoritative runners remain logical values in NetworkMinefieldState.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinefieldNetworkView : MonoBehaviour
    {
        private const float CameraHeight = 38f;
        private const float CameraOrthographicSize = 24.5f;
        private const float RunnerInterpolationSpeed = 16f;

        [SerializeField] private NetworkMinefieldState state;
        [SerializeField] private CinemachineCamera topDownCamera;
        [SerializeField] private Transform runnerRoot;
        [SerializeField] private Transform mineRoot;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private GameObject crusherPlaceholder;

        private readonly RunnerView[] _runners =
            new RunnerView[MinefieldRules.PlayerCount];
        private readonly List<MineView> _mines = new List<MineView>();
        private GameplayCameraDirector _cameraDirector;
        private Canvas _hudCanvas;
        private Text _phaseText;
        private Text _instructionText;
        private Text[] _scoreRows;
        private int _mineLayoutHash;

        private void Awake()
        {
            ResolveSceneReferences();
            ConfigureCamera();
            EnsurePresentation();
            SetWorldPresentationActive(false);
        }

        private void OnEnable()
        {
            ResolveSceneReferences();
            RegisterCamera();
        }

        private void OnDisable()
        {
            SetWorldPresentationActive(false);
            if (_cameraDirector != null && topDownCamera != null)
            {
                _cameraDirector.ClearMinigameCamera(topDownCamera);
            }
        }

        private void OnDestroy()
        {
            for (var slot = 0; slot < _runners.Length; slot++)
            {
                if (_runners[slot] != null && _runners[slot].PulseMaterial != null)
                {
                    Destroy(_runners[slot].PulseMaterial);
                }
            }
        }

        private void Update()
        {
            if (state == null)
            {
                state = GetComponent<NetworkMinefieldState>();
            }

            RegisterCamera();
            EnsurePresentation();

            var match = NetworkMatchState.Instance;
            var shouldShowWorld = state != null && state.IsSpawned && match != null &&
                                  (match.FlowState == BoardFlowState.MinigamePlaying ||
                                   match.FlowState == BoardFlowState.SkippedResult);
            var shouldShowHud = shouldShowWorld &&
                                match.FlowState == BoardFlowState.MinigamePlaying;
            if (_hudCanvas != null && _hudCanvas.gameObject.activeSelf != shouldShowHud)
            {
                _hudCanvas.gameObject.SetActive(shouldShowHud);
            }

            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                return;
            }

            SetWorldPresentationActive(true);
            RefreshRunners(match);
            RefreshMines();
            RefreshCrusher();
            RefreshHud(match);
        }

        private void ResolveSceneReferences()
        {
            if (state == null)
            {
                state = GetComponent<NetworkMinefieldState>();
            }
            if (topDownCamera == null)
            {
                topDownCamera = GetComponentInChildren<CinemachineCamera>(true);
            }
            if (crusherPlaceholder == null)
            {
                var crusher = FindDescendant(transform, "Crusher Placeholder");
                crusherPlaceholder = crusher != null ? crusher.gameObject : null;
            }
            if (arenaPresentation == null)
            {
                var arena = FindDescendant(transform, "Arena Presentation");
                arenaPresentation = arena != null ? arena.gameObject : null;
            }
            if (runnerRoot == null)
            {
                runnerRoot = EnsureChild(transform, "Runtime Runners");
            }
            if (mineRoot == null)
            {
                mineRoot = EnsureChild(transform, "Runtime Mine Markers");
            }
        }

        private void ConfigureCamera()
        {
            if (topDownCamera == null)
            {
                return;
            }

            var lens = topDownCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = CameraOrthographicSize;
            topDownCamera.Lens = lens;
            topDownCamera.ForceCameraPosition(
                new Vector3(NetworkMinefieldState.ArenaCenterX, CameraHeight, 0f),
                Quaternion.Euler(90f, 0f, 0f));
        }

        private void RegisterCamera()
        {
            if (_cameraDirector == null)
            {
                _cameraDirector = FindAnyObjectByType<GameplayCameraDirector>();
            }

            if (_cameraDirector != null && topDownCamera != null)
            {
                ConfigureCamera();
                _cameraDirector.SetMinigameCamera(topDownCamera);
            }
        }

        private void EnsurePresentation()
        {
            ResolveSceneReferences();
            for (var slot = 0; slot < _runners.Length; slot++)
            {
                if (_runners[slot] == null)
                {
                    _runners[slot] = CreateRunner(slot);
                }
            }

            if (_hudCanvas == null)
            {
                CreateHud();
            }
        }

        private RunnerView CreateRunner(int slot)
        {
            var runnerObject = new GameObject("Runner " + (slot + 1));
            runnerObject.transform.SetParent(runnerRoot, false);

            var actor = runnerObject.AddComponent<MinefieldPlayerActor>();
            actor.ConfigurePlayerSlot(slot);
            var avatarVisual = runnerObject.AddComponent<PlayerAvatarVisual>();
            avatarVisual.EnsureBuilt();
            var presentation = runnerObject.AddComponent<MinefieldPlayerPresentation>();

            DisableGeneratedHitColliders(runnerObject);

            var head = FindDescendant(runnerObject.transform, "HeadAnchor");
            var sirenRoot = new GameObject("Proximity Siren").transform;
            sirenRoot.SetParent(head != null ? head : runnerObject.transform, false);
            sirenRoot.localPosition = new Vector3(0f, 0.72f, 0f);

            var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseObject.name = "Siren Base";
            baseObject.transform.SetParent(sirenRoot, false);
            baseObject.transform.localPosition = Vector3.zero;
            baseObject.transform.localScale = new Vector3(0.28f, 0.1f, 0.28f);
            RemoveCollider(baseObject);
            ApplyColor(baseObject.GetComponent<Renderer>(), new Color(0.12f, 0.12f, 0.14f));

            var lensObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lensObject.name = "Siren Red Lens";
            lensObject.transform.SetParent(sirenRoot, false);
            lensObject.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            lensObject.transform.localScale = new Vector3(0.34f, 0.26f, 0.34f);
            RemoveCollider(lensObject);
            var lensRenderer = lensObject.GetComponent<Renderer>();

            var lightObject = new GameObject("Siren Red Light");
            lightObject.transform.SetParent(sirenRoot, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            var warningLight = lightObject.AddComponent<Light>();
            warningLight.type = LightType.Point;
            warningLight.color = Color.red;
            warningLight.range = 3f;
            warningLight.intensity = 0f;
            warningLight.enabled = false;

            var pulseObject = new GameObject("Sonar Pulse");
            pulseObject.transform.SetParent(runnerObject.transform, false);
            pulseObject.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            var pulse = pulseObject.AddComponent<LineRenderer>();
            pulse.loop = true;
            pulse.useWorldSpace = false;
            pulse.widthMultiplier = 0.09f;
            pulse.positionCount = 65;
            pulse.startColor = new Color(1f, 0.2f, 0.15f, 0.9f);
            pulse.endColor = pulse.startColor;
            var pulseShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (pulseShader == null)
            {
                pulseShader = Shader.Find("Sprites/Default");
            }
            var pulseMaterial = pulseShader != null
                ? new Material(pulseShader) { name = "Minefield Sonar Pulse (Runtime)" }
                : null;
            if (pulseMaterial != null)
            {
                pulse.material = pulseMaterial;
            }
            for (var point = 0; point < pulse.positionCount; point++)
            {
                var angle = point / 64f * Mathf.PI * 2f;
                pulse.SetPosition(
                    point,
                    new Vector3(
                        Mathf.Cos(angle) * NetworkMinefieldState.SonarRadius,
                        0f,
                        Mathf.Sin(angle) * NetworkMinefieldState.SonarRadius));
            }
            pulse.enabled = false;

            presentation.ApplyState(new MinefieldActorSnapshot(
                0,
                slot,
                0,
                MinefieldPlayerState.Healthy,
                MinefieldEliminationCause.None,
                true));

            return new RunnerView(
                runnerObject.transform,
                actor,
                presentation,
                avatarVisual,
                lensRenderer,
                warningLight,
                pulse,
                pulseMaterial);
        }

        private void RefreshRunners(NetworkMatchState match)
        {
            for (var slot = 0; slot < _runners.Length; slot++)
            {
                var view = _runners[slot];
                var targetPosition = state.GetRunnerPosition(slot);
                if (!view.HasReceivedPosition ||
                    Vector3.SqrMagnitude(view.Root.position - targetPosition) > 64f ||
                    state.Phase == NetworkMinefieldPhase.Countdown)
                {
                    view.Root.position = targetPosition;
                    view.HasReceivedPosition = true;
                }
                else
                {
                    var previous = view.Root.position;
                    view.Root.position = Vector3.Lerp(
                        previous,
                        targetPosition,
                        1f - Mathf.Exp(-RunnerInterpolationSpeed * Time.unscaledDeltaTime));
                    var direction = view.Root.position - previous;
                    direction.y = 0f;
                    if (direction.sqrMagnitude > 0.00001f)
                    {
                        view.Root.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    }
                }

                var playerState = state.GetPlayerState(slot);
                var mineHitCount = state.GetMineHitCount(slot);
                var eliminationCause = state.GetEliminationCause(slot);
                if (view.LastState != playerState ||
                    view.LastMineHitCount != mineHitCount ||
                    view.LastEliminationCause != eliminationCause)
                {
                    view.Presentation.ApplyState(new MinefieldActorSnapshot(
                        ++view.StateRevision,
                        slot,
                        mineHitCount,
                        playerState,
                        eliminationCause,
                        playerState == MinefieldPlayerState.Healthy ||
                        playerState == MinefieldPlayerState.Crippled));
                    view.LastState = playerState;
                    view.LastMineHitCount = mineHitCount;
                    view.LastEliminationCause = eliminationCause;
                }

                var boardAvatar = match.GetAvatarForSlot(slot);
                if (boardAvatar != null)
                {
                    var appearance = boardAvatar.Appearance;
                    view.AvatarVisual.SetBodyColor(appearance.BodyColor);
                    view.AvatarVisual.ApplyAppearance(
                        appearance.EyeId,
                        appearance.MouthId,
                        appearance.HatId);
                    view.AvatarVisual.SetDisplayName(boardAvatar.DisplayName);
                }

                var intensity = playerState == MinefieldPlayerState.Eliminated ||
                                playerState == MinefieldPlayerState.Finished
                    ? 0f
                    : state.GetSirenIntensity(slot);
                ApplySiren(view, intensity);
                view.SonarPulse.enabled = state.IsSonarActive(slot);
            }
        }

        private void RefreshMines()
        {
            var minePositions = state.GetMineWorldPositions();
            var layoutHash = ComputeMineLayoutHash(minePositions);
            if (layoutHash != _mineLayoutHash || _mines.Count != minePositions.Count)
            {
                RebuildMineMarkers(minePositions);
                _mineLayoutHash = layoutHash;
            }

            for (var mineIndex = 0; mineIndex < _mines.Count; mineIndex++)
            {
                var mine = _mines[mineIndex];
                var detected = false;
                for (var slot = 0; slot < _runners.Length && !detected; slot++)
                {
                    if (!state.IsSonarActive(slot))
                    {
                        continue;
                    }

                    var runnerPosition = state.GetRunnerPosition(slot);
                    var delta = mine.Position - runnerPosition;
                    delta.y = 0f;
                    detected = delta.sqrMagnitude <=
                               NetworkMinefieldState.SonarRadius *
                               NetworkMinefieldState.SonarRadius;
                }

                mine.Root.SetActive(detected);
            }
        }

        private void RebuildMineMarkers(IReadOnlyList<Vector3> positions)
        {
            for (var index = 0; index < _mines.Count; index++)
            {
                if (_mines[index].Root != null)
                {
                    Destroy(_mines[index].Root);
                }
            }
            _mines.Clear();

            for (var index = 0; index < positions.Count; index++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "Detected Mine " + (index + 1);
                marker.transform.SetParent(mineRoot, false);
                marker.transform.position = positions[index] + Vector3.up * 0.08f;
                marker.transform.localScale = new Vector3(0.7f, 0.14f, 0.7f);
                RemoveCollider(marker);
                ApplyColor(marker.GetComponent<Renderer>(), new Color(1f, 0.12f, 0.06f));
                marker.SetActive(false);
                _mines.Add(new MineView(marker, positions[index]));
            }
        }

        private void RefreshCrusher()
        {
            if (crusherPlaceholder == null)
            {
                return;
            }

            var position = crusherPlaceholder.transform.position;
            position.x = NetworkMinefieldState.ArenaCenterX;
            position.z = state.CrusherWorldZ;
            crusherPlaceholder.transform.position = position;
        }

        private void CreateHud()
        {
            var canvasObject = new GameObject("Minefield HUD");
            canvasObject.transform.SetParent(transform, false);
            _hudCanvas = canvasObject.AddComponent<Canvas>();
            _hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _hudCanvas.sortingOrder = 40;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject("Minefield HUD Panel");
            panel.transform.SetParent(canvasObject.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -24f);
            panelRect.sizeDelta = new Vector2(950f, 265f);
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.025f, 0.035f, 0.055f, 0.88f);
            panelImage.raycastTarget = false;

            _phaseText = CreateText(
                "Minefield Phase",
                panel.transform,
                new Vector2(0f, -20f),
                new Vector2(900f, 48f),
                34,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            _instructionText = CreateText(
                "Minefield Instructions",
                panel.transform,
                new Vector2(0f, -68f),
                new Vector2(900f, 34f),
                20,
                TextAnchor.MiddleCenter,
                FontStyle.Normal);
            _instructionText.text = "WASD MOVE   |   STOP + RMB SONAR   |   FIRST MINE: CRIPPLED   |   SECOND: OUT";

            _scoreRows = new Text[MinefieldRules.PlayerCount];
            for (var slot = 0; slot < _scoreRows.Length; slot++)
            {
                _scoreRows[slot] = CreateText(
                    "Minefield Score " + slot,
                    panel.transform,
                    new Vector2(-330f + slot * 220f, -133f),
                    new Vector2(205f, 105f),
                    19,
                    TextAnchor.UpperCenter,
                    FontStyle.Bold);
            }
        }

        private void RefreshHud(NetworkMatchState match)
        {
            switch (state.Phase)
            {
                case NetworkMinefieldPhase.Countdown:
                    _phaseText.text = "MINEFIELD  ·  ROUND " + state.RoundNumber + " / 3  ·  START IN " +
                                      Mathf.CeilToInt((float)state.Remaining);
                    break;
                case NetworkMinefieldPhase.Running:
                    _phaseText.text = "MINEFIELD  ·  ROUND " + state.RoundNumber + " / 3  ·  " +
                                      FormatClock(state.Remaining);
                    break;
                case NetworkMinefieldPhase.RoundResult:
                    _phaseText.text = "ROUND " + state.RoundNumber + " RESULTS  ·  NEXT IN " +
                                      Mathf.CeilToInt((float)state.Remaining);
                    break;
                case NetworkMinefieldPhase.Complete:
                    _phaseText.text = "MINEFIELD  ·  FINAL RESULTS";
                    break;
                default:
                    _phaseText.text = "MINEFIELD";
                    break;
            }

            for (var slot = 0; slot < _scoreRows.Length; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                var displayName = avatar != null ? avatar.DisplayName : "PLAYER " + (slot + 1);
                var playerState = state.GetPlayerState(slot);
                var finalRank = state.GetFinalRank(slot);
                var stateLabel = playerState == MinefieldPlayerState.Crippled
                    ? "CRIPPLED"
                    : playerState == MinefieldPlayerState.Eliminated
                        ? "OUT"
                        : playerState == MinefieldPlayerState.Finished
                            ? "FINISHED"
                            : "RUNNING";
                _scoreRows[slot].text = displayName + "\n" +
                                        stateLabel + "\n" +
                                        "+" + state.GetRoundPoints(slot) +
                                        "  ·  TOTAL " + state.GetScore(slot) +
                                        (finalRank > 0
                                            ? "\n#" + finalRank + "  ·  GOLD +" +
                                              MinefieldRules.GetPointsForRank(finalRank)
                                            : string.Empty);
                if (avatar != null)
                {
                    _scoreRows[slot].color = avatar.Appearance.BodyColor;
                }
            }
        }

        private void SetWorldPresentationActive(bool active)
        {
            if (arenaPresentation != null &&
                arenaPresentation.activeSelf != active)
            {
                arenaPresentation.SetActive(active);
            }
            if (runnerRoot != null && runnerRoot.gameObject.activeSelf != active)
            {
                runnerRoot.gameObject.SetActive(active);
            }
            if (mineRoot != null && mineRoot.gameObject.activeSelf != active)
            {
                mineRoot.gameObject.SetActive(active);
            }
            if (crusherPlaceholder != null && crusherPlaceholder.activeSelf != active)
            {
                crusherPlaceholder.SetActive(active);
            }
        }

        private static void ApplySiren(RunnerView view, float intensity)
        {
            intensity = Mathf.Clamp01(intensity);
            ApplyColor(
                view.SirenRenderer,
                Color.Lerp(Color.black, new Color(1f, 0.03f, 0.02f), intensity),
                Color.red * (intensity * 4f));
            view.SirenLight.intensity = intensity * 4f;
            view.SirenLight.enabled = intensity > 0.001f;
        }

        private static int ComputeMineLayoutHash(IReadOnlyList<Vector3> positions)
        {
            unchecked
            {
                var hash = 17;
                for (var index = 0; index < positions.Count; index++)
                {
                    hash = hash * 31 + Mathf.RoundToInt(positions[index].x * 100f);
                    hash = hash * 31 + Mathf.RoundToInt(positions[index].z * 100f);
                }
                return hash;
            }
        }

        private static Text CreateText(
            string name,
            Transform parent,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
            TextAnchor alignment,
            FontStyle style)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            var rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            var text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                return existing;
            }

            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == name)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void DisableGeneratedHitColliders(GameObject runner)
        {
            var colliders = runner.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
        }

        private static void RemoveCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private static void ApplyColor(
            Renderer renderer,
            Color color,
            Color emission = default)
        {
            if (renderer == null)
            {
                return;
            }

            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            properties.SetColor("_EmissionColor", emission);
            renderer.SetPropertyBlock(properties);
        }

        private static string FormatClock(double seconds)
        {
            var safeSeconds = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return (safeSeconds / 60).ToString("00") + ":" +
                   (safeSeconds % 60).ToString("00");
        }

        private sealed class RunnerView
        {
            public RunnerView(
                Transform root,
                MinefieldPlayerActor actor,
                MinefieldPlayerPresentation presentation,
                PlayerAvatarVisual avatarVisual,
                Renderer sirenRenderer,
                Light sirenLight,
                LineRenderer sonarPulse,
                Material pulseMaterial)
            {
                Root = root;
                Actor = actor;
                Presentation = presentation;
                AvatarVisual = avatarVisual;
                SirenRenderer = sirenRenderer;
                SirenLight = sirenLight;
                SonarPulse = sonarPulse;
                PulseMaterial = pulseMaterial;
                LastState = (MinefieldPlayerState)byte.MaxValue;
                LastMineHitCount = -1;
                LastEliminationCause = (MinefieldEliminationCause)byte.MaxValue;
            }

            public Transform Root { get; }
            public MinefieldPlayerActor Actor { get; }
            public MinefieldPlayerPresentation Presentation { get; }
            public PlayerAvatarVisual AvatarVisual { get; }
            public Renderer SirenRenderer { get; }
            public Light SirenLight { get; }
            public LineRenderer SonarPulse { get; }
            public Material PulseMaterial { get; }
            public MinefieldPlayerState LastState { get; set; }
            public int LastMineHitCount { get; set; }
            public MinefieldEliminationCause LastEliminationCause { get; set; }
            public int StateRevision { get; set; }
            public bool HasReceivedPosition { get; set; }
        }

        private sealed class MineView
        {
            public MineView(GameObject root, Vector3 position)
            {
                Root = root;
                Position = position;
            }

            public GameObject Root { get; }
            public Vector3 Position { get; }
        }
    }
}
