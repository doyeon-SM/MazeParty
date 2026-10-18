using System;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>Shared fixed camera and server placement of the real network players.</summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class AwardCeremonyPresentation : MonoBehaviour
    {
        [SerializeField] private AwardCeremonyStageBindings bindings;
        private readonly NetworkPlayerAvatar[] _placed = new NetworkPlayerAvatar[4];
        private readonly int[] _keys = new int[4];
        private GameplayCameraDirector _cameraDirector;
        private int _observedRevision = -1;
        private bool _presentationActive;
        public AwardCeremonyStageBindings Bindings => bindings;
        public void Configure(AwardCeremonyStageBindings value) => bindings = value;

        private void Awake()
        {
            if (bindings == null) bindings = GetComponent<AwardCeremonyStageBindings>();
            SetPresentationActive(false);
        }
        private void OnEnable() => GameText.LanguageChanged += RefreshWorldText;
        private void OnDisable()
        {
            GameText.LanguageChanged -= RefreshWorldText;
            SetPresentationActive(false);
        }

        private void Update()
        {
            var match = NetworkMatchState.Instance;
            var active = match != null && match.IsSpawned && match.IsAwardCeremonyActive;
            SetPresentationActive(active);
            if (!active || bindings == null || !bindings.HasRequiredReferences)
            {
                _observedRevision = -1;
                Array.Clear(_placed, 0, _placed.Length);
                Array.Clear(_keys, 0, _keys.Length);
                return;
            }
            if (_cameraDirector == null) _cameraDirector = FindAnyObjectByType<GameplayCameraDirector>();
            if (_cameraDirector != null) _cameraDirector.SetMinigameCamera(bindings.SharedCamera);
            var show = match.IsFinalRankingLocked;
            for (var rank = 0; rank < 3; rank++)
            {
                bindings.PodiumRoots[rank].gameObject.SetActive(show);
                bindings.RankLabels[rank].gameObject.SetActive(show);
                bindings.KeyLabels[rank].gameObject.SetActive(show);
                bindings.WinnerSpotlights[rank].gameObject.SetActive(show && rank == 0);
            }
            if (!show)
            {
                Array.Clear(_placed, 0, _placed.Length);
                return;
            }
            for (var slot = 0; slot < 4; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar == null) continue;
                if (_keys[slot] != avatar.KeyCount) _observedRevision = -1;
                _keys[slot] = avatar.KeyCount;
                if (!match.IsServer || _placed[slot] == avatar) continue;
                var rank = match.GetFinalCeremonyRank(slot);
                var tiedCount = 0;
                var tiedIndex = 0;
                for (var other = 0; other < 4; other++)
                {
                    if (match.GetFinalCeremonyRank(other) != rank) continue;
                    tiedCount++;
                    if (other < slot) tiedIndex++;
                }
                avatar.PrepareForAwardCeremonyOnServer(
                    bindings.GetPlayerPosition(rank, tiedIndex, tiedCount),
                    bindings.PlayerAnchors[Mathf.Clamp(rank, 1, 4) - 1].rotation);
                _placed[slot] = avatar;
            }
            if (_observedRevision != match.CeremonyRevision)
            {
                _observedRevision = match.CeremonyRevision;
                RefreshWorldText();
            }
        }

        private void LateUpdate()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsFinalRankingLocked) return;
            for (var slot = 0; slot < 4; slot++)
            {
                var visual = match.GetAvatarForSlot(slot)?.AvatarVisual;
                if (visual == null) continue;
                visual.SetOwnerFirstPerson(false);
                visual.SetTopViewHighlight(false);
                visual.SetHiddenFromViewer(false);
                visual.SetEliminated(false);
            }
        }

        private void RefreshWorldText()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsFinalRankingLocked || bindings == null ||
                !bindings.HasRequiredReferences) return;
            for (var rank = 1; rank <= 3; rank++)
            {
                var label = bindings.RankLabels[rank - 1];
                GameFonts.Apply(label);
                label.text = rank == 1 ? "1st" : rank == 2 ? "2nd" : "3rd";
                var keys = bindings.KeyLabels[rank - 1];
                GameFonts.Apply(keys);
                keys.text = string.Empty;
                for (var slot = 0; slot < 4; slot++)
                {
                    if (match.GetFinalCeremonyRank(slot) != rank) continue;
                    keys.text = GameText.F("KEYS: {0}", _keys[slot]);
                    break;
                }
            }
        }

        private void SetPresentationActive(bool active)
        {
            if (bindings == null) return;
            if (bindings.PresentationRoot != null && bindings.PresentationRoot.activeSelf != active)
                bindings.PresentationRoot.SetActive(active);
            if (bindings.SharedCamera != null && bindings.SharedCamera.gameObject.activeSelf != active)
                bindings.SharedCamera.gameObject.SetActive(active);
            if (_presentationActive && !active && bindings.SharedCamera != null)
            {
                if (_cameraDirector != null) _cameraDirector.ClearMinigameCamera(bindings.SharedCamera);
                bindings.SharedCamera.Priority = 0;
            }
            _presentationActive = active;
        }
    }
}
