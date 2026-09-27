using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    /// <summary>
    /// Every scene AudioSource must follow the settings menu sliders through
    /// exactly one AudioChannelSource (MazeParty/Audio/Assign Sound Channels).
    /// </summary>
    public sealed class SoundChannelContractTests
    {
        private static readonly string[] ScenePaths =
        {
            "Assets/MazeParty/Scenes/Multiplayer/OnlineBootstrap.unity",
            "Assets/MazeParty/Scenes/Board/Board.unity",
            "Assets/MazeParty/Scenes/Minigames/Minefield/Minefield.unity",
            "Assets/MazeParty/Scenes/Minigames/WrongWay/WrongWay.unity",
            "Assets/MazeParty/Scenes/Minigames/RedLightGreenLight/RedLightGreenLight.unity",
            "Assets/MazeParty/Scenes/Minigames/StableFooting/StableFooting.unity",
            "Assets/MazeParty/Scenes/Minigames/BalloonBlow/BalloonBlow.unity",
            "Assets/MazeParty/Scenes/Minigames/GiftGrab/GiftGrab.unity",
            "Assets/MazeParty/Scenes/Minigames/TerritoryPaint/TerritoryPaint.unity",
            "Assets/MazeParty/Scenes/Minigames/TagChase/TagChase.unity",
            "Assets/MazeParty/Scenes/Minigames/Race/Race.unity",
            "Assets/MazeParty/Scenes/Minigames/SequenceMemory/SequenceMemory.unity",
            "Assets/MazeParty/Scenes/Minigames/BouncingBalls/BouncingBalls.unity",
            "Assets/MazeParty/Scenes/Minigames/BombPassing/BombPassing.unity",
            "Assets/MazeParty/Scenes/Minigames/SnowySpin/SnowySpin.unity",
            "Assets/MazeParty/Scenes/Minigames/ArenaCombat/ArenaCombat.unity",
            "Assets/MazeParty/Scenes/Minigames/CliffBarrage/CliffBarrage.unity"
        };

        [Test]
        public void EverySceneAudioSource_HasExactlyOneSoundChannel()
        {
            var problems = new List<string>();
            foreach (var path in ScenePaths)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    continue;
                }

                var scene = SceneManager.GetSceneByPath(path);
                var openedForTest = !scene.IsValid() || !scene.isLoaded;
                if (openedForTest)
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                }

                try
                {
                    var roots = scene.GetRootGameObjects();
                    var channels = roots
                        .SelectMany(root => root.GetComponentsInChildren<AudioChannelSource>(true))
                        .ToArray();
                    foreach (var source in roots.SelectMany(root =>
                                 root.GetComponentsInChildren<AudioSource>(true)))
                    {
                        var count = channels.Count(channel => channel.Source == source);
                        if (count != 1)
                        {
                            problems.Add(path + " :: " + source.name + " has " + count + " channel(s)");
                        }
                    }
                }
                finally
                {
                    if (openedForTest)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
    }
}
