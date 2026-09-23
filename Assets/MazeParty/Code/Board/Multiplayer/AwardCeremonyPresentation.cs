using System;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Local-only shared-camera podium presentation for the final ranking.
    /// Network player objects remain in their authoritative board positions.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class AwardCeremonyPresentation : MonoBehaviour
    {
        private static readonly Color[] FallbackPlayerColors =
        {
            new Color(0.95f, 0.25f, 0.25f),
            new Color(0.25f, 0.55f, 1f),
            new Color(0.25f, 0.85f, 0.4f),
            new Color(1f, 0.75f, 0.2f)
        };
        private static readonly Color[] RankColors =
        {
            new Color(1f, 0.72f, 0.12f),
            new Color(0.72f, 0.78f, 0.86f),
            new Color(0.75f, 0.38f, 0.14f),
            new Color(0.23f, 0.28f, 0.36f)
        };

        [SerializeField] private AwardCeremonyStageBindings bindings;

        private readonly PlayerAvatarVisual[] _players =
            new PlayerAvatarVisual[MultiplayerConstants.MaxPlayers];
        private GameplayCameraDirector _cameraDirector;
        private int _observedRevision = -1;
        private bool _presentationActive;

        public AwardCeremonyStageBindings Bindings => bindings;

        public void Configure(AwardCeremonyStageBindings value)
        {
            bindings = value;
        }

        private void Awake()
        {
            if (bindings == null)
            {
                bindings = GetComponent<AwardCeremonyStageBindings>();
            }
            SetPresentationActive(false);
        }

        private void OnDisable()
        {
            UnregisterCamera();
            SetPresentationActive(false);
        }

        private void Update()
        {
            var match = NetworkMatchState.Instance;
            var active = match != null &&
                         match.IsSpawned &&
                         match.IsAwardCeremonyActive;
            SetPresentationActive(active);
            if (!active || bindings == null ||
                !bindings.HasRequiredReferences)
            {
                _observedRevision = -1;
                return;
            }

            RegisterCamera();
            var showPodium =
                match.CeremonyPhase == AwardCeremonyPhase.FinalPodiumLocked ||
                match.CeremonyPhase == AwardCeremonyPhase.AwaitingReturn;
            SetPodiumPlayersVisible(showPodium);
            if (showPodium && _observedRevision != match.CeremonyRevision)
            {
                _observedRevision = match.CeremonyRevision;
                RefreshPodium(match);
            }
        }

        private void SetPresentationActive(bool active)
        {
            if (bindings == null)
            {
                return;
            }
            if (bindings.PresentationRoot != null &&
                bindings.PresentationRoot.activeSelf != active)
            {
                bindings.PresentationRoot.SetActive(active);
            }
            if (bindings.SharedCamera != null &&
                bindings.SharedCamera.gameObject.activeSelf != active)
            {
                bindings.SharedCamera.gameObject.SetActive(active);
            }
            if (_presentationActive && !active)
            {
                UnregisterCamera();
            }
            _presentationActive = active;
        }

        private void RegisterCamera()
        {
            if (_cameraDirector == null)
            {
                _cameraDirector = FindAnyObjectByType<GameplayCameraDirector>();
            }
            if (_cameraDirector != null && bindings.SharedCamera != null)
            {
                _cameraDirector.SetMinigameCamera(bindings.SharedCamera);
            }
        }

        private void UnregisterCamera()
        {
            if (_cameraDirector != null && bindings != null &&
                bindings.SharedCamera != null)
            {
                _cameraDirector.ClearMinigameCamera(bindings.SharedCamera);
            }
            if (bindings != null && bindings.SharedCamera != null)
            {
                bindings.SharedCamera.Priority = 0;
            }
        }

        private void RefreshPodium(NetworkMatchState match)
        {
            EnsurePlayers();
            var slots = new int[MultiplayerConstants.MaxPlayers];
            for (var slot = 0; slot < slots.Length; slot++)
            {
                slots[slot] = slot;
            }
            Array.Sort(slots, (left, right) =>
            {
                var rank = match.GetFinalCeremonyRank(left).CompareTo(
                    match.GetFinalCeremonyRank(right));
                return rank != 0 ? rank : left.CompareTo(right);
            });

            for (var podiumIndex = 0;
                 podiumIndex < MultiplayerConstants.MaxPlayers;
                 podiumIndex++)
            {
                var slot = slots[podiumIndex];
                var rank = Math.Max(1, match.GetFinalCeremonyRank(slot));
                ConfigurePodium(podiumIndex, rank);
                var player = _players[slot];
                var anchor = bindings.PlayerAnchors[podiumIndex];
                player.transform.position = anchor.position +
                                            Vector3.up *
                                            (PlayerAvatarVisual.StandingControllerHeight * 0.5f);
                player.transform.rotation = anchor.rotation;
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar != null)
                {
                    var appearance = avatar.Appearance;
                    player.SetBodyColor(appearance.BodyColor);
                    player.ApplyAppearance(
                        appearance.EyeId,
                        appearance.MouthId,
                        appearance.HatId);
                    player.SetDisplayName(avatar.DisplayName);
                }
                else
                {
                    player.SetBodyColor(FallbackPlayerColors[slot]);
                    player.SetDisplayName("Player " + (slot + 1));
                }
                player.SetOwnerFirstPerson(false);
                player.SetTopViewHighlight(false);
                player.SetEliminated(false);
                player.gameObject.SetActive(true);
            }
        }

        private void ConfigurePodium(int podiumIndex, int rank)
        {
            var height = HeightForRank(rank);
            var podium = bindings.PodiumRoots[podiumIndex];
            var localPosition = podium.localPosition;
            localPosition.y = height * 0.5f;
            podium.localPosition = localPosition;
            var localScale = podium.localScale;
            localScale.y = height;
            podium.localScale = localScale;

            var renderer = podium.GetComponent<Renderer>();
            if (renderer != null)
            {
                var properties = new MaterialPropertyBlock();
                var color = RankColors[Math.Min(rank - 1, RankColors.Length - 1)];
                properties.SetColor("_BaseColor", color);
                properties.SetColor("_Color", color);
                renderer.SetPropertyBlock(properties);
            }

            var rankLabel = bindings.RankLabels[podiumIndex];
            rankLabel.text = "#" + rank;
            var stageTransform = bindings.PresentationRoot.transform;
            rankLabel.transform.position = podium.position -
                                           stageTransform.forward * 1.12f;
            rankLabel.transform.rotation = stageTransform.rotation;
            var spotlight = bindings.WinnerSpotlights[podiumIndex];
            spotlight.gameObject.SetActive(rank == 1);
            if (rank == 1)
            {
                spotlight.transform.position =
                    bindings.PlayerAnchors[podiumIndex].position +
                    Vector3.up * 7f;
                spotlight.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        private void EnsurePlayers()
        {
            if (bindings.RuntimePlayerRoot == null)
            {
                return;
            }
            for (var slot = 0; slot < _players.Length; slot++)
            {
                if (_players[slot] != null)
                {
                    continue;
                }
                var root = new GameObject(
                    "Award Ceremony Player " + (slot + 1));
                root.transform.SetParent(bindings.RuntimePlayerRoot, false);
                var visual = root.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                var nameplate = root.transform.Find(
                    "VisualRoot/NameplateAnchor");
                if (nameplate != null)
                {
                    nameplate.gameObject.SetActive(false);
                }
                visual.SetBodyColor(FallbackPlayerColors[slot]);
                visual.SetDisplayName("Player " + (slot + 1));
                foreach (var collider in
                         root.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                }
                _players[slot] = visual;
            }
        }

        private void SetPodiumPlayersVisible(bool visible)
        {
            if (bindings == null || bindings.PodiumRoots == null)
            {
                return;
            }
            for (var index = 0;
                 index < bindings.PodiumRoots.Length;
                 index++)
            {
                if (bindings.PodiumRoots[index] != null)
                {
                    bindings.PodiumRoots[index].gameObject.SetActive(visible);
                }
                if (bindings.WinnerSpotlights != null &&
                    index < bindings.WinnerSpotlights.Length &&
                    bindings.WinnerSpotlights[index] != null && !visible)
                {
                    bindings.WinnerSpotlights[index].gameObject.SetActive(false);
                }
            }
            for (var slot = 0; slot < _players.Length; slot++)
            {
                if (_players[slot] != null)
                {
                    _players[slot].gameObject.SetActive(visible);
                }
            }
        }

        private static float HeightForRank(int rank)
        {
            switch (rank)
            {
                case 1: return 2.4f;
                case 2: return 1.8f;
                case 3: return 1.25f;
                default: return 0.85f;
            }
        }
    }
}
