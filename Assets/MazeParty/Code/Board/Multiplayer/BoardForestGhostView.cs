using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>Local presentation driven only by the replicated, pause-aware ghost timeline.</summary>
    public sealed class BoardForestGhostView : MonoBehaviour
    {
        [SerializeField] private Transform model;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform head;
        [SerializeField] private Transform captionAnchor;
        [SerializeField, Min(0f)] private float captionHeadOffset = 0.4f;
        [SerializeField] private Renderer[] ghostRenderers;
        [SerializeField] private Material normalMaterial, strongMaterial;
        [SerializeField] private AnimationClip idleClip, attackClip, dissolveClip;
        [SerializeField] private GameObject coin, key, healing, damageShield;
        [SerializeField] private Vector3 playerOffset = new Vector3(1.05f, 0.45f, 0.65f);
        [SerializeField] private float modelScale = 0.75f;
        [SerializeField] private float emptyTileScale = 1.5f;
        private MaterialPropertyBlock _properties;
        private int _revision = -1;
        public Transform Focus => transform;
        public Transform Head => head;
        public Transform CaptionAnchor => captionAnchor;
        public bool HasRequiredReferences => model != null && animator != null && animator.transform == model &&
            animator.enabled && head != null && captionAnchor != null && ghostRenderers != null && ghostRenderers.Length > 0 &&
            normalMaterial != null && strongMaterial != null && idleClip != null && attackClip != null &&
            dissolveClip != null && coin != null && key != null && healing != null && damageShield != null;

        public void Present(BoardForestGhostSnapshot snapshot, double now, GameplayCameraDirector director, PlayerAvatarVisual targetVisual = null)
        {
            transform.SetPositionAndRotation(snapshot.Position, snapshot.Facing);
            var elapsed = (float)snapshot.Elapsed(now);
            if (_revision != snapshot.Revision)
            {
                _revision = snapshot.Revision;
                foreach (var renderer in ghostRenderers)
                    renderer.sharedMaterial = snapshot.Strong ? strongMaterial : normalMaterial;
            }
            var resource = (ForestGhostResource)snapshot.Resource;
            var hasTarget = snapshot.Slot >= 0;
            var disappearing = elapsed >= 2.4f;
            var attacking = hasTarget && resource == ForestGhostResource.Health && snapshot.Amount < 0 && elapsed >= 0.6f;
            var clip = disappearing ? dissolveClip : attacking ? attackClip : idleClip;
            var animationTime = disappearing ? elapsed - 2.4f : attacking ? elapsed - 0.6f : elapsed;
            clip.SampleAnimation(model.gameObject, clip.isLooping ? animationTime % clip.length : Mathf.Min(animationTime, clip.length));
            Quaternion cameraRotation;
            if (hasTarget)
            {
                director.CalculateBoardResourceEventPose(Focus, targetVisual != null ? targetVisual.Bindings?.HeadAnchor : null,
                    0f, out _, out cameraRotation, out _);
                model.position = snapshot.Position + Vector3.up * PlayerAvatarVisual.StandingEyeHeight +
                    cameraRotation * playerOffset;
                model.rotation = Quaternion.LookRotation(snapshot.Position + Vector3.up - model.position, Vector3.up);
            }
            else
            {
                cameraRotation = SharedCameraFraming.Rotation;
                model.position = snapshot.Position + Vector3.up * 0.65f;
                model.rotation = snapshot.IsIntro ? snapshot.Facing : Quaternion.LookRotation(Vector3.ProjectOnPlane(
                    -(cameraRotation * Vector3.forward), Vector3.up), Vector3.up);
            }
            model.localScale = Vector3.one * (hasTarget ? modelScale : emptyTileScale);
            captionAnchor.position = head.position + Vector3.up * captionHeadOffset;
            _properties ??= new MaterialPropertyBlock();
            _properties.SetFloat("_Dissolve", Mathf.Min(Mathf.Clamp01(elapsed / 0.3f), Mathf.Clamp01((3f - elapsed) / 0.6f)));
            foreach (var renderer in ghostRenderers) renderer.SetPropertyBlock(_properties);

            var motion = hasTarget && elapsed >= 0.6f && elapsed < 1.65f;
            coin.SetActive(motion && resource == ForestGhostResource.Gold);
            key.SetActive(motion && resource == ForestGhostResource.Key);
            var prop = resource == ForestGhostResource.Gold ? coin.transform : key.transform;
            if (prop.gameObject.activeSelf)
            {
                var playerHead = targetVisual != null && targetVisual.Bindings != null
                    ? targetVisual.Bindings.HeadAnchor : null;
                var playerPoint = playerHead != null ? playerHead.position
                    : snapshot.Position + Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
                var ghostPoint = ghostRenderers[0].bounds.center;
                var from = snapshot.Amount > 0 ? ghostPoint : playerPoint;
                var to = snapshot.Amount > 0 ? playerPoint : ghostPoint;
                var progress = Mathf.Clamp01((elapsed - 0.6f) / ((float)BoardForestGhostRules.ApplyAt - 0.6f));
                prop.position = Vector3.Lerp(from, to, progress) + Vector3.up * (Mathf.Sin(progress * Mathf.PI) * 0.35f);
                prop.rotation = Quaternion.Euler(0f, elapsed * 180f, 0f);
                prop.gameObject.SetActive(progress < 1f);
            }
            var healthEffect = motion && resource == ForestGhostResource.Health;
            PresentShield(healing, healthEffect && snapshot.Amount > 0, snapshot.Position, elapsed);
            PresentShield(damageShield, healthEffect && snapshot.Amount < 0, snapshot.Position, elapsed);
        }

        private static void PresentShield(GameObject shield, bool visible, Vector3 playerPosition, float elapsed)
        {
            shield.SetActive(visible);
            if (!visible) return;
            shield.transform.position = playerPosition + Vector3.up;
            foreach (var particle in shield.GetComponentsInChildren<ParticleSystem>(true))
                particle.Simulate(elapsed - 0.6f, false, true);
        }
    }
}
