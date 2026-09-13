using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.TerritoryPaint;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class TerritoryPaintSoloTestController :
        MonoBehaviour
    {
        private static readonly Color32 UnpaintedColor =
            new Color32(58, 65, 73, 255);
        private static readonly Color32[] PlayerColors =
        {
            new Color32(45, 122, 242, 255),
            new Color32(235, 57, 48, 255),
            new Color32(46, 199, 82, 255),
            new Color32(177, 68, 232, 255)
        };

        private readonly Transform[] _players =
            new Transform[TerritoryPaintRules.PlayerCount];
        private readonly PlayerAvatarVisual[] _visuals =
            new PlayerAvatarVisual[TerritoryPaintRules.PlayerCount];
        private readonly Color32[] _pixels =
            new Color32[
                TerritoryPaintRules.SurfaceResolution *
                TerritoryPaintRules.SurfaceResolution];

        private TerritoryPaintSoloSession _session;
        private MinigameSoloHudView _hud;
        private NetworkTerritoryPaintState _productionState;
        private TerritoryPaintNetworkView _productionView;
        private GameObject _arenaPresentation;
        private GameObject _productionRunnerRoot;
        private GameObject _productionHud;
        private Renderer _paintRenderer;
        private Transform _runtimeRoot;
        private Texture2D _paintTexture;
        private Material _paintMaterial;
        private Camera _runtimeCamera;
        private uint _lastPaintRevision = uint.MaxValue;
        private bool _initialized;

        public bool IsInitialized => _initialized;
        public TerritoryPaintSoloSession Session => _session;
        public Camera RuntimeCamera => _runtimeCamera;
        public Transform LocalPlayer =>
            _players[TerritoryPaintSoloSession.LocalPlayerSlot];

        public void ConfigureHud(MinigameSoloHudView hud)
        {
            _hud = hud;
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "Territory Paint solo is already initialized.");
            }
            if (_hud == null || !_hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Territory Paint solo requires the developer HUD " +
                    "prefab instance.");
            }

            _hud.BindActions(
                RestartRound,
                StartNextSeed,
                StopSoloTest);
            ResolveAndDisableProduction();
            CreateRuntimePresentation();

            _session = new TerritoryPaintSoloSession();
            _session.Begin(seed);
            _initialized = true;
            RefreshPresentation(true);
            Debug.Log(
                "[Minigame Solo Test] Territory Paint started with " +
                "three deterministic practice players.");
        }

        private void Update()
        {
            if (!_initialized || _session == null)
            {
                return;
            }
            if (HandleKeyboardShortcuts())
            {
                return;
            }

            _session.Tick(
                Time.unscaledDeltaTime,
                ReadMove());
            RefreshPresentation(false);
        }

        private void OnDestroy()
        {
            if (_paintTexture != null)
            {
                Destroy(_paintTexture);
            }
            if (_paintMaterial != null)
            {
                Destroy(_paintMaterial);
            }
            if (_runtimeRoot != null)
            {
                Destroy(_runtimeRoot.gameObject);
            }
        }

        private void ResolveAndDisableProduction()
        {
            _productionState =
                FindAnyObjectByType<NetworkTerritoryPaintState>(
                    FindObjectsInactive.Include);
            _productionView =
                FindAnyObjectByType<TerritoryPaintNetworkView>(
                    FindObjectsInactive.Include);
            if (_productionState == null ||
                _productionView == null)
            {
                throw new InvalidOperationException(
                    "Territory Paint production state and view were not " +
                    "found in the scene.");
            }

            _productionState.enabled = false;
            _productionView.enabled = false;
            _arenaPresentation = FindDescendant(
                _productionState.transform,
                "Arena Presentation")?.gameObject;
            _productionRunnerRoot = FindDescendant(
                _productionState.transform,
                "Runtime Runners")?.gameObject;
            _productionHud = _productionState
                .GetComponentInChildren<TerritoryPaintHudBindings>(true)
                ?.gameObject;
            var paintSurface = FindDescendant(
                _productionState.transform,
                "Paint Surface");
            _paintRenderer =
                paintSurface != null
                    ? paintSurface.GetComponent<Renderer>()
                    : null;
            if (_arenaPresentation == null ||
                _productionRunnerRoot == null ||
                _paintRenderer == null)
            {
                throw new InvalidOperationException(
                    "Territory Paint scene presentation contract is " +
                    "incomplete.");
            }

            _arenaPresentation.SetActive(true);
            _productionRunnerRoot.SetActive(false);
            if (_productionHud != null)
            {
                _productionHud.SetActive(false);
            }
        }

        private void CreateRuntimePresentation()
        {
            _runtimeRoot =
                new GameObject("[Developer] Territory Paint Runtime")
                    .transform;
            CreatePaintTexture();
            CreatePlayers();
            CreateRuntimeCamera();
        }

        private void CreatePaintTexture()
        {
            _paintTexture = new Texture2D(
                TerritoryPaintRules.SurfaceResolution,
                TerritoryPaintRules.SurfaceResolution,
                TextureFormat.RGBA32,
                false)
            {
                name = "Territory Paint Solo Surface",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (var index = 0; index < _pixels.Length; index++)
            {
                _pixels[index] = UnpaintedColor;
            }
            _paintTexture.SetPixels32(_pixels);
            _paintTexture.Apply(false, false);

            _paintMaterial =
                new Material(_paintRenderer.sharedMaterial)
                {
                    name = "Territory Paint Solo Material"
                };
            _paintMaterial.mainTexture = _paintTexture;
            if (_paintMaterial.HasProperty("_BaseMap"))
            {
                _paintMaterial.SetTexture("_BaseMap", _paintTexture);
            }
            _paintRenderer.sharedMaterial = _paintMaterial;
        }

        private void CreatePlayers()
        {
            var root =
                new GameObject("Solo Runners").transform;
            root.SetParent(_runtimeRoot, false);
            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                var playerObject =
                    new GameObject("Solo Runner " + (slot + 1));
                playerObject.transform.SetParent(root, false);
                var visual =
                    playerObject.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.SetOwnerFirstPerson(false);
                visual.SetBodyColor(PlayerColors[slot]);
                visual.SetDisplayName(
                    slot == TerritoryPaintSoloSession.LocalPlayerSlot
                        ? "SOLO DEV"
                        : "PRACTICE " + (slot + 1));
                visual.SetTopViewHighlight(
                    slot ==
                    TerritoryPaintSoloSession.LocalPlayerSlot);
                visual.SetEliminated(false);
                DisableGeneratedHitColliders(playerObject);
                _players[slot] = playerObject.transform;
                _visuals[slot] = visual;
            }
        }

        private void CreateRuntimeCamera()
        {
            var cameraObject =
                new GameObject("Territory Paint Solo Camera");
            cameraObject.transform.SetParent(_runtimeRoot, false);
            cameraObject.transform.SetPositionAndRotation(
                TerritoryPaintNetworkView.SharedCameraPosition,
                TerritoryPaintNetworkView.SharedCameraRotation);
            _runtimeCamera = cameraObject.AddComponent<Camera>();
            _runtimeCamera.orthographic = true;
            _runtimeCamera.orthographicSize =
                TerritoryPaintNetworkView
                    .SharedCameraOrthographicSize;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 80f;
            _runtimeCamera.clearFlags =
                CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.018f, 0.024f, 0.036f);
            cameraObject.AddComponent<AudioListener>();
        }

        private void RefreshPresentation(bool snap)
        {
            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                var position =
                    _session.GetPlayerPosition(slot);
                var target = new Vector3(
                    NetworkTerritoryPaintState.ArenaCenterX +
                    position.x,
                    TerritoryPaintNetworkView
                        .RunnerPresentationHeight,
                    position.y);
                if (snap)
                {
                    _players[slot].position = target;
                }
                else
                {
                    _players[slot].position = Vector3.Lerp(
                        _players[slot].position,
                        target,
                        1f - Mathf.Exp(
                            -18f * Time.unscaledDeltaTime));
                }

                var facing =
                    _session.GetPlayerFacing(slot);
                if (facing.sqrMagnitude > 0.0001f)
                {
                    _players[slot].rotation =
                        Quaternion.LookRotation(
                            new Vector3(
                                facing.x,
                                0f,
                                facing.y),
                            Vector3.up);
                }
            }

            RefreshPaint();
            RefreshHud();
        }

        private void RefreshPaint()
        {
            if (_paintTexture == null ||
                _lastPaintRevision ==
                _session.PaintRevision)
            {
                return;
            }

            _lastPaintRevision =
                _session.PaintRevision;
            var resolution =
                TerritoryPaintRules.SurfaceResolution;
            for (var y = 0; y < resolution; y++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    var owner =
                        _session.GetPaintOwner(x, y);
                    _pixels[y * resolution + x] =
                        owner < PlayerColors.Length
                            ? PlayerColors[owner]
                            : UnpaintedColor;
                }
            }
            _paintTexture.SetPixels32(_pixels);
            _paintTexture.Apply(false, false);
        }

        private void RefreshHud()
        {
            var scores =
                "P1 " + _session.GetScore(0) +
                "  ·  P2 " + _session.GetScore(1) +
                "  ·  P3 " + _session.GetScore(2) +
                "  ·  P4 " + _session.GetScore(3);
            var final =
                _session.Phase ==
                TerritoryPaintSoloPhase.Complete
                    ? BuildFinalLabel()
                    : "Painted area is normalized to 1000.";
            _hud.SetContent(
                "TERRITORY PAINT · SOLO",
                FormatClock(_session.Remaining) +
                "  ·  YOU " +
                _session.GetScore(
                    TerritoryPaintSoloSession.LocalPlayerSlot),
                scores,
                final,
                "WASD MOVE  ·  R RESTART  ·  N NEXT SEED  ·  ESC STOP",
                string.Empty,
                MinigameSoloFeedbackStyle.Neutral);
        }

        private string BuildFinalLabel()
        {
            if (_session.Leaderboard == null)
            {
                return "COMPLETE";
            }
            for (var index = 0;
                 index < _session.Leaderboard.Count;
                 index++)
            {
                var entry = _session.Leaderboard[index];
                if (entry.PlayerSlot ==
                    TerritoryPaintSoloSession.LocalPlayerSlot)
                {
                    return "COMPLETE  ·  " +
                           ToOrdinal(entry.Rank) +
                           "  ·  " + entry.Score;
                }
            }
            return "COMPLETE";
        }

        private void RestartRound()
        {
            _session.Begin(_session.Seed);
            _lastPaintRevision = uint.MaxValue;
            RefreshPresentation(true);
        }

        private void StartNextSeed()
        {
            _session.Begin(unchecked(_session.Seed + 1));
            _lastPaintRevision = uint.MaxValue;
            RefreshPresentation(true);
        }

        private static Vector2 ReadMove()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }

            var input = Vector2.zero;
            if (keyboard.aKey.isPressed) input.x -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.wKey.isPressed) input.y += 1f;
            return Vector2.ClampMagnitude(input, 1f);
        }

        private bool HandleKeyboardShortcuts()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                StopSoloTest();
                return true;
            }
            if (keyboard.rKey.wasPressedThisFrame)
            {
                RestartRound();
                return true;
            }
            if (keyboard.nKey.wasPressedThisFrame)
            {
                StartNextSeed();
                return true;
            }
            return false;
        }

        private static string FormatClock(double seconds)
        {
            var whole =
                Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return (whole / 60).ToString("00") + ":" +
                   (whole % 60).ToString("00");
        }

        private static string ToOrdinal(int rank)
        {
            switch (rank)
            {
                case 1: return "1ST";
                case 2: return "2ND";
                case 3: return "3RD";
                case 4: return "4TH";
                default: return "--";
            }
        }

        private static void DisableGeneratedHitColliders(
            GameObject root)
        {
            var colliders =
                root.GetComponentsInChildren<Collider>(true);
            for (var index = 0;
                 index < colliders.Length;
                 index++)
            {
                colliders[index].enabled = false;
            }
        }

        private static Transform FindDescendant(
            Transform root,
            string name)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == name)
            {
                return root;
            }
            for (var index = 0;
                 index < root.childCount;
                 index++)
            {
                var found = FindDescendant(
                    root.GetChild(index),
                    name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void StopSoloTest()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
