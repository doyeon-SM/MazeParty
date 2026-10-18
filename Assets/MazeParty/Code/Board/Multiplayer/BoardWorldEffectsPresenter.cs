using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class BoardWorldEffectsPresenter : MonoBehaviour
    {
        [Serializable]
        public sealed class Impact
        {
            public GameObject prefab;
            public SoundCue sound;
            [Min(0.01f)] public float scale = 1f;
            public float height;
        }
        [SerializeField] private BoardTileEffectWorldView markerPrefab;
        [SerializeField] private BoardMinimapView mapAppearance;
        [SerializeField] private Impact[] impacts = new Impact[3];
        private readonly List<BoardTileEffectWorldView> _markers = new();
        private IReadOnlyList<BoardTile> _tiles;
        private GameplayCameraDirector _director;
        private readonly BoardDamageImpactPlayback _playback = new();
        private PooledOneShotVfx _activeImpact;
        private int _impactPlaybackRevision;
        private SoundHandle _sound;
        private int _activeRevision = -1;
        public BoardTileEffectWorldView MarkerPrefab => markerPrefab;
        public BoardMinimapView MapAppearance => mapAppearance;
        public IReadOnlyList<Impact> Impacts => impacts;
        public bool HasRequiredReferences
        {
            get
            {
                if (markerPrefab == null || !markerPrefab.HasRequiredReferences || mapAppearance == null ||
                    impacts == null || impacts.Length != 3) return false;
                foreach (var impact in impacts)
                    if (impact == null || impact.prefab == null || impact.prefab.GetComponent<PooledOneShotVfx>() == null ||
                        impact.sound == null || !impact.sound.HasClips) return false;
                return true;
            }
        }
        private void LateUpdate()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsSpawned || !match.GameplayEnabled || !HasRequiredReferences)
            {
                HideMarkers(); StopImpact(); _playback.Reset(); return;
            }
            if (_director == null) _director = GetComponent<GameplayCameraDirector>();
            var camera = _director != null ? _director.OutputCamera : Camera.main;
            var topology = match.PresentationTopology;
            if (topology != null && match.FlowState <= BoardFlowState.LandingEffectResolve)
            {
                if (!ReferenceEquals(_tiles, topology.Tiles))
                {
                    ClearMarkers(); _tiles = topology.Tiles;
                    foreach (var tile in _tiles) _markers.Add(Instantiate(markerPrefab, transform));
                }
                for (var i = 0; i < _markers.Count; i++)
                    _markers[i].Present(_tiles[i], mapAppearance, camera);
            }
            else HideMarkers();

            var feedback = match.LandingEffectFeedback;
            if (_activeRevision != feedback.Revision || match.FlowState != BoardFlowState.LandingEffectResolve ||
                !BoardLandingEffectFeedbackRules.IsEffectVisible(feedback, match.LandingFeedbackPresentationNow)) StopImpact();
            var avatar = match.GetAvatarForSlot(feedback.Slot);
            if (avatar == null) return;
            if (_playback.TryTake(feedback, match.LastLandingEffectType, match.FlowState,
                match.IsGlobalSimulationPaused || match.IsReconnectPaused,
                match.BoardEffectSeed, match.LandingFeedbackPresentationNow, out var choice))
            {
                var impact = impacts[choice];
                var position = avatar.transform.position + Vector3.up * impact.height;
                _activeImpact = OneShotVfxPool.Play(impact.prefab, position, Quaternion.identity, impact.scale);
                _impactPlaybackRevision = _activeImpact != null ? _activeImpact.PlaybackRevision : -1;
                _sound = GameSound.PlayAt(impact.sound.Key, position);
                _activeRevision = feedback.Revision;
            }
        }
        private void HideMarkers() { foreach (var marker in _markers) if (marker != null) marker.gameObject.SetActive(false); }
        private void ClearMarkers()
        {
            foreach (var marker in _markers)
                if (marker != null)
                {
                    if (Application.isPlaying) Destroy(marker.gameObject);
                    else DestroyImmediate(marker.gameObject);
                }
            _markers.Clear(); _tiles = null;
        }
        private void StopImpact()
        {
            if (_activeImpact != null) _activeImpact.StopPresentation(_impactPlaybackRevision);
            _activeImpact = null;
            if (_activeRevision >= 0) GameSound.Stop(_sound, 0.1f);
            _activeRevision = -1;
        }
        private void OnDisable() { ClearMarkers(); StopImpact(); _playback.Reset(); }
    }
}
