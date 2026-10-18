using System;
using MazeParty.Gameplay;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>Authored, physical podium and movement area for network players.</summary>
    [DisallowMultipleComponent]
    public sealed class AwardCeremonyStageBindings : MonoBehaviour
    {
        [SerializeField] private GameObject presentationRoot;
        [SerializeField] private Transform[] podiumRoots;
        [SerializeField] private Transform[] playerAnchors;
        [SerializeField] private TextMesh[] rankLabels;
        [SerializeField] private TextMesh[] keyLabels;
        [SerializeField] private Light[] winnerSpotlights;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private BoxCollider stageFloor;
        [SerializeField, Min(0.5f)] private float tiedPlayerSpacing = 1.1f;

        public static AwardCeremonyStageBindings Instance { get; private set; }
        public GameObject PresentationRoot => presentationRoot;
        public Transform[] PodiumRoots => podiumRoots;
        public Transform[] PlayerAnchors => playerAnchors;
        public TextMesh[] RankLabels => rankLabels;
        public TextMesh[] KeyLabels => keyLabels;
        public Light[] WinnerSpotlights => winnerSpotlights;
        public CinemachineCamera SharedCamera => sharedCamera;
        public BoxCollider StageFloor => stageFloor;
        public bool HasRequiredReferences => presentationRoot != null && sharedCamera != null &&
            stageFloor != null && !stageFloor.isTrigger && stageFloor.enabled &&
            Assigned(podiumRoots, 3) && Assigned(playerAnchors, 4) &&
            Assigned(rankLabels, 3) && Assigned(keyLabels, 3) && Assigned(winnerSpotlights, 3) &&
            presentationRoot.transform.IsChildOf(transform) && sharedCamera.transform.IsChildOf(transform) &&
            stageFloor.transform.IsChildOf(presentationRoot.transform) &&
            Array.TrueForAll(podiumRoots, podium => podium.GetComponent<BoxCollider>() != null);

        private void OnEnable() => Instance = this;
        private void OnDisable() { if (Instance == this) Instance = null; }

        public void Configure(GameObject root, Transform[] podiums, Transform[] anchors,
            TextMesh[] labels, TextMesh[] keys, Light[] spotlights, CinemachineCamera camera, BoxCollider floor)
        {
            presentationRoot = root;
            podiumRoots = podiums;
            playerAnchors = anchors;
            rankLabels = labels;
            keyLabels = keys;
            winnerSpotlights = spotlights;
            sharedCamera = camera;
            stageFloor = floor;
        }

        public Vector3 GetPlayerPosition(int rank, int indexAmongTied, int tiedCount)
        {
            var anchor = playerAnchors[Mathf.Clamp(rank, 1, 4) - 1];
            var offset = AwardCeremonyFlowRules.GetTiedPlayerOffset(indexAmongTied, tiedCount, tiedPlayerSpacing);
            return anchor.position + transform.right * offset +
                Vector3.up * (PlayerAvatarVisual.StandingControllerHeight * 0.5f + 0.02f);
        }

        public Vector3 ClampPosition(Vector3 position, float radius)
        {
            var floor = stageFloor.transform;
            var local = floor.InverseTransformPoint(position);
            var half = stageFloor.size * 0.5f;
            var center = stageFloor.center;
            var marginX = radius / Mathf.Max(0.001f, Mathf.Abs(floor.lossyScale.x));
            var marginZ = radius / Mathf.Max(0.001f, Mathf.Abs(floor.lossyScale.z));
            local.x = Mathf.Clamp(local.x, center.x - half.x + marginX, center.x + half.x - marginX);
            local.z = Mathf.Clamp(local.z, center.z - half.z + marginZ, center.z + half.z - marginZ);
            return floor.TransformPoint(local);
        }

        private static bool Assigned<T>(T[] values, int count) where T : UnityEngine.Object =>
            values != null && values.Length == count && Array.TrueForAll(values, value => value != null);
    }
}
