using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Runtime-safe reference to the authored player presentation prefab. Keeping
    /// the prefab reference in a Resources asset lets local minigame presentation
    /// copies share the same source without hard-coded asset paths.
    /// </summary>
    [CreateAssetMenu(menuName = "MazeParty/Player/Presentation Assets")]
    public sealed class PlayerAvatarPresentationAssets : ScriptableObject
    {
        public const string ResourcePath =
            "MazeParty/Player/PlayerAvatarPresentationAssets";

        [SerializeField]
        private PlayerAvatarPresentationBindings presentationPrefab;

        public PlayerAvatarPresentationBindings PresentationPrefab =>
            presentationPrefab;

        public bool HasRequiredReferences =>
            presentationPrefab != null &&
            presentationPrefab.HasRequiredReferences;

        public void Configure(PlayerAvatarPresentationBindings prefab)
        {
            presentationPrefab = prefab;
        }

        public static PlayerAvatarPresentationAssets LoadRequired()
        {
            var assets = Resources.Load<PlayerAvatarPresentationAssets>(
                ResourcePath);
            if (assets == null || !assets.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Player presentation assets are missing or incomplete. " +
                    "Run MazeParty/Multiplayer/Install Player And Lobby Prefabs.");
            }
            return assets;
        }
    }
}
