using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed class StatisticsPortraitRenderer : MonoBehaviour
    {
        [SerializeField] private StatisticsPortraitRig rigPrefab;
        private StatisticsPortraitRig _rig;
        private readonly RenderTexture[] _textures = new RenderTexture[4];
        public bool HasRequiredReferences => rigPrefab != null && rigPrefab.PortraitCamera != null && rigPrefab.Visual != null && rigPrefab.Presentation != null;
        public void Configure(StatisticsPortraitRig prefab) => rigPrefab = prefab;
        public Texture Get(int slot) => _textures[slot];
        public void Render(MatchStatistics data)
        {
            if (!HasRequiredReferences || data == null) return;
            if (_rig == null) _rig = Instantiate(rigPrefab, new Vector3(-15000, -15000, -15000), Quaternion.identity);
            _rig.gameObject.SetActive(true);
            for (var slot = 0; slot < 4; slot++)
            {
                if (_textures[slot] == null)
                {
                    _textures[slot] = new RenderTexture(192, 192, 24, RenderTextureFormat.ARGB32);
                    _textures[slot].Create();
                }
                _rig.Render(data.players[slot], _textures[slot]);
            }
            _rig.gameObject.SetActive(false);
        }
        private void OnDestroy()
        {
            foreach (var texture in _textures) if (texture != null) { texture.Release(); Dispose(texture); }
            if (_rig != null) Dispose(_rig.gameObject);
        }
        private static void Dispose(Object target)
        {
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        }
    }
}
