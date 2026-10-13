using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Local-only red screen flash for item and combat damage.
    /// Its Canvas and graphics are authored by the prefab, never at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaCombatHitFlashView : MonoBehaviour
    {
        [SerializeField] private Canvas overlayCanvas;
        [SerializeField] private CanvasGroup flashGroup;
        [SerializeField] private Image flashImage;
        [SerializeField, Min(0.01f)] private float flashDuration = 0.42f;
        [SerializeField, Range(0f, 1f)] private float peakOpacity = 0.42f;

        private static readonly List<ArenaCombatHitFlashView> ActiveInstances =
            new List<ArenaCombatHitFlashView>();

        private float _remaining;

        public static ArenaCombatHitFlashView Instance { get; private set; }

        public bool HasRequiredReferences =>
            overlayCanvas != null &&
            flashGroup != null &&
            flashImage != null &&
            flashGroup.transform.IsChildOf(transform) &&
            (flashImage.transform == flashGroup.transform ||
             flashImage.transform.IsChildOf(flashGroup.transform));

        public float CurrentOpacity => flashGroup != null ? flashGroup.alpha : 0f;

        public void ConfigureUiBindings(
            Canvas canvas,
            CanvasGroup group,
            Image image)
        {
            overlayCanvas = canvas;
            flashGroup = group;
            flashImage = image;
        }

        private void Awake()
        {
            SetOpacity(0f);
        }

        private void OnEnable()
        {
            ActiveInstances.Remove(this);
            ActiveInstances.Add(this);
            Instance = this;
        }

        private void OnDisable()
        {
            ActiveInstances.Remove(this);
            if (Instance == this)
            {
                Instance = FindLatestActiveInstance();
            }

            _remaining = 0f;
            SetOpacity(0f);
        }

        private static ArenaCombatHitFlashView FindLatestActiveInstance()
        {
            for (var index = ActiveInstances.Count - 1; index >= 0; index--)
            {
                var candidate = ActiveInstances[index];
                if (candidate != null && candidate.isActiveAndEnabled)
                {
                    return candidate;
                }

                ActiveInstances.RemoveAt(index);
            }

            return null;
        }

        private void Update()
        {
            if (_remaining <= 0f)
            {
                return;
            }

            _remaining = Mathf.Max(0f, _remaining - Time.unscaledDeltaTime);
            var duration = Mathf.Max(0.01f, flashDuration);
            SetOpacity(
                peakOpacity *
                PresentationAccessibility.FlashIntensityScale *
                (_remaining / duration));
        }

        public void Flash()
        {
            if (!isActiveAndEnabled || !HasRequiredReferences)
            {
                return;
            }

            _remaining = Mathf.Max(0.01f, flashDuration);
            SetOpacity(
                peakOpacity *
                PresentationAccessibility.FlashIntensityScale);
        }

        private void SetOpacity(float opacity)
        {
            if (flashGroup != null)
            {
                flashGroup.alpha = Mathf.Clamp01(opacity);
            }
        }
    }
}
