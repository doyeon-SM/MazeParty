using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Authored world-space sources used to show the final podium. The generated
    /// player models are local presentation copies and never move network avatars.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AwardCeremonyStageBindings : MonoBehaviour
    {
        [SerializeField] private GameObject presentationRoot;
        [SerializeField] private Transform runtimePlayerRoot;
        [SerializeField] private Transform[] podiumRoots;
        [SerializeField] private Transform[] playerAnchors;
        [SerializeField] private TextMesh[] rankLabels;
        [SerializeField] private Light[] winnerSpotlights;
        [SerializeField] private CinemachineCamera sharedCamera;

        public GameObject PresentationRoot => presentationRoot;
        public Transform RuntimePlayerRoot => runtimePlayerRoot;
        public Transform[] PodiumRoots => podiumRoots;
        public Transform[] PlayerAnchors => playerAnchors;
        public TextMesh[] RankLabels => rankLabels;
        public Light[] WinnerSpotlights => winnerSpotlights;
        public CinemachineCamera SharedCamera => sharedCamera;

        public bool HasRequiredReferences =>
            presentationRoot != null &&
            runtimePlayerRoot != null &&
            sharedCamera != null &&
            HasFourAssigned(podiumRoots) &&
            HasFourAssigned(playerAnchors) &&
            HasFourAssigned(rankLabels) &&
            HasFourAssigned(winnerSpotlights) &&
            presentationRoot.transform.IsChildOf(transform) &&
            runtimePlayerRoot.IsChildOf(presentationRoot.transform) &&
            sharedCamera.transform.IsChildOf(transform);

        public void Configure(
            GameObject root,
            Transform players,
            Transform[] podiums,
            Transform[] anchors,
            TextMesh[] labels,
            Light[] spotlights,
            CinemachineCamera camera)
        {
            presentationRoot = root;
            runtimePlayerRoot = players;
            podiumRoots = podiums;
            playerAnchors = anchors;
            rankLabels = labels;
            winnerSpotlights = spotlights;
            sharedCamera = camera;
        }

        private static bool HasFourAssigned<T>(T[] values)
            where T : Object
        {
            if (values == null || values.Length != MultiplayerConstants.MaxPlayers)
            {
                return false;
            }
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index] == null)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
