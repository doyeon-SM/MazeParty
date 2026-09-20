using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Place one marker per physical face, with local up pointing away from the die
    /// center. Supports the board's shared D12 range without assuming a cube.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldDieFaceMarker : MonoBehaviour
    {
        [SerializeField, Range(
            WorldDieAuthorityModel.MinimumFace,
            WorldDieAuthorityModel.MaximumFace)]
        private int value = WorldDieAuthorityModel.MinimumFace;

        public int Value => value;
        public Vector3 LocalNormal
        {
            get
            {
                var die = GetComponentInParent<NetworkWorldDie>();
                return die != null
                    ? die.transform.InverseTransformDirection(transform.up).normalized
                    : transform.localRotation * Vector3.up;
            }
        }

        public void Configure(int faceValue)
        {
            value = Mathf.Clamp(
                faceValue,
                WorldDieAuthorityModel.MinimumFace,
                WorldDieAuthorityModel.MaximumFace);
        }
    }
}
