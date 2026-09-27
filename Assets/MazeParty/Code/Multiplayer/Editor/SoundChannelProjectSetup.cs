using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    /// <summary>
    /// Gives every scene AudioSource an <see cref="AudioChannelSource"/> so the
    /// settings menu effect and music sliders reach it. Existing assignments are
    /// kept; unsaved open scenes are skipped so user edits are never saved.
    /// </summary>
    public static class SoundChannelProjectSetup
    {
        public static readonly string[] ScenePaths =
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

        [MenuItem("MazeParty/Audio/Assign Sound Channels")]
        public static void AssignSoundChannels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Assign Sound Channels requires Edit Mode.");
                return;
            }

            var added = 0;
            var skipped = new List<string>();
            foreach (var path in ScenePaths)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    continue;
                }

                var scene = SceneManager.GetSceneByPath(path);
                var openedHere = !scene.IsValid() || !scene.isLoaded;
                if (!openedHere && scene.isDirty)
                {
                    skipped.Add(path);
                    continue;
                }

                if (openedHere)
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                }

                try
                {
                    var count = AssignInScene(scene);
                    if (count > 0)
                    {
                        added += count;
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }
                }
                finally
                {
                    if (openedHere)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }

            Debug.Log("Sound channels assigned: " + added + " AudioSource(s)." +
                      (skipped.Count > 0
                          ? " Skipped unsaved scenes: " + string.Join(", ", skipped)
                          : string.Empty));
        }

        /// <summary>Adds an effects channel to each unassigned AudioSource. Returns the count.</summary>
        internal static int AssignInScene(Scene scene)
        {
            var added = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                var assigned = new HashSet<AudioSource>(root
                    .GetComponentsInChildren<AudioChannelSource>(true)
                    .Select(channel => channel.Source)
                    .Where(source => source != null));
                foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
                {
                    if (assigned.Contains(source))
                    {
                        continue;
                    }

                    var channel = Undo.AddComponent<AudioChannelSource>(source.gameObject);
                    channel.Configure(source, AudioChannel.Sfx, source.volume);
                    EditorUtility.SetDirty(channel);
                    assigned.Add(source);
                    added++;
                }
            }

            return added;
        }
    }
}
