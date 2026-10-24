using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>Authored world-space portrait stage. The runtime only applies appearance and captures it.</summary>
    public sealed class StatisticsPortraitRig : MonoBehaviour
    {
        public Camera PortraitCamera;
        public PlayerAvatarVisual Visual;
        public PlayerAvatarPresentationBindings Presentation;

        public void Render(StatisticsPlayer player, RenderTexture target)
        {
            Visual.EnsureBuilt();
            Visual.SetBodyColor(player.color);
            Visual.ApplyAppearance(player.eye, player.mouth, player.hat, player.expression);
            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var t in Visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            foreach (var r in Visual.GetComponentsInChildren<Renderer>(true))
            {
                var t = r.transform;
                var head = t.IsChildOf(Presentation.HeadAnchor) || t.IsChildOf(Presentation.HatAnchor) ||
                    t.IsChildOf(Presentation.LeftEyeAnchor) || t.IsChildOf(Presentation.RightEyeAnchor) || t.IsChildOf(Presentation.MouthAnchor);
                if (!head) { r.enabled = false; continue; }
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; } else bounds.Encapsulate(r.bounds);
            }
            if (!hasBounds) return;
            PortraitCamera.transform.position = bounds.center + new Vector3(0, .12f, 4f);
            PortraitCamera.transform.LookAt(bounds.center);
            PortraitCamera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.17f;
            PortraitCamera.targetTexture = target;
            PortraitCamera.Render();
            PortraitCamera.targetTexture = null;
        }
    }
}
