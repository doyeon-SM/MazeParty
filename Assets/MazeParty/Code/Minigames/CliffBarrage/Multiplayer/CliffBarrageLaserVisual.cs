using UnityEngine;
using MazeParty.Gameplay.Minigames.CliffBarrage;

namespace MazeParty.Multiplayer
{
    /// <summary>Authored Beam Blue parts follow the authoritative hazard phase.</summary>
    public sealed class CliffBarrageLaserVisual : MonoBehaviour
    {
        [SerializeField] private GameObject chargeSphere;
        [SerializeField] private GameObject warningRoot;
        [SerializeField] private GameObject firingRoot;
        [SerializeField] private Transform forwardBeam;
        [SerializeField] private Transform backwardBeam;
        [SerializeField] private float meshLength = 13.4f;
        [SerializeField] private float beamWidth = 0.65f;

        public bool HasRequiredReferences => chargeSphere != null && warningRoot != null &&
            firingRoot != null && forwardBeam != null && backwardBeam != null;

        public void Present(CliffBarrageLaserPhase phase, float length)
        {
            var warning = phase == CliffBarrageLaserPhase.Warning;
            var firing = phase == CliffBarrageLaserPhase.Firing;
            chargeSphere.SetActive(warning || firing);
            warningRoot.SetActive(warning);
            firingRoot.SetActive(firing);
            warningRoot.transform.localScale = new Vector3(0.14f, 0.08f, length);
            var scale = new Vector3(beamWidth * 0.5f, beamWidth * 0.5f,
                length * 0.5f / Mathf.Max(0.01f, meshLength));
            forwardBeam.localScale = scale;
            backwardBeam.localScale = scale;
        }
    }
}
