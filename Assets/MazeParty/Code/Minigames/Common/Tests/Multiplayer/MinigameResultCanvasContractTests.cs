using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameResultCanvasContractTests
    {
        [Test]
        public void EveryMinigameResult_DisplaysAllTiedPlayersWithTheirSharedReward()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var warning = typeof(Unity.Netcode.NetworkVariableBase).GetField("IgnoreInitializeWarning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var previousWarning = (bool)warning.GetValue(null);
            warning.SetValue(null, true);
            var previousMatch = NetworkMatchState.Instance;
            typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, null);
            var names = new[] { "Minefield", "WrongWay", "RedLightGreenLight", "StableFooting", "BalloonBlow", "GiftGrab", "TerritoryPaint", "TagChase", "Race", "SnowySpin", "ArenaCombat", "CliffBarrage", "BombPassing", "BouncingBalls", "SequenceMemory", "CoinCollect", "OddOneOut", "Race2", "ShellGame", "Flight" };
            try
            {
                foreach (var name in names)
                {
                    var root = new GameObject("Result contract " + name);
                    System.Type type = null;
                    object previousInstance = null;
                    try
                    {
                        type = typeof(NetworkMatchState).Assembly.GetType("MazeParty.Multiplayer.Network" + name + "State");
                        var state = root.AddComponent(type);
                        previousInstance = type.GetProperty("Instance").GetValue(null);
                        type.GetProperty("Instance").SetValue(null, state);
                        var field = type.GetField("_finalRanks", flags) ?? type.GetField("_finalRanksPacked", flags);
                        System.Action<uint> setRanks;
                        if (state is NetworkFlightState)
                        {
                            var snapshot = (Unity.Netcode.NetworkVariable<FlightSnapshot>)type.GetField("_snapshot", flags).GetValue(state);
                            setRanks = packed => snapshot.Value = packed == 0 ? default : new FlightSnapshot {
                                Phase = MazeParty.Gameplay.Minigames.Flight.FlightPhase.Complete,
                                Player0 = new FlightPlayerSnapshot { Rank = (byte)packed },
                                Player1 = new FlightPlayerSnapshot { Rank = (byte)(packed >> 8) },
                                Player2 = new FlightPlayerSnapshot { Rank = (byte)(packed >> 16) },
                                Player3 = new FlightPlayerSnapshot { Rank = (byte)(packed >> 24) } };
                        }
                        else if (state is NetworkRace2State)
                        {
                            var snapshot = (Unity.Netcode.NetworkVariable<Race2Snapshot>)type.GetField("_snapshot", flags).GetValue(state);
                            setRanks = packed => snapshot.Value = packed == 0 ? default : new Race2Snapshot {
                                Phase = MazeParty.Gameplay.Minigames.Race2.Race2Phase.Complete,
                                Scores = (4 - (packed & 255)) | ((4 - ((packed >> 8) & 255)) << 8) |
                                    ((4 - ((packed >> 16) & 255)) << 16) | ((4 - ((packed >> 24) & 255)) << 24) };
                        }
                        else if (state is NetworkShellGameState)
                        {
                            var snapshot = (Unity.Netcode.NetworkVariable<ShellGamePublicSnapshot>)type.GetField("_snapshot", flags).GetValue(state);
                            setRanks = packed => snapshot.Value = packed == 0 ? default : new ShellGamePublicSnapshot {
                                Phase = MazeParty.Gameplay.Minigames.ShellGame.ShellGamePhase.Complete,
                                Score0 = 4 - (int)(packed & 255), Score1 = 4 - (int)((packed >> 8) & 255),
                                Score2 = 4 - (int)((packed >> 16) & 255), Score3 = 4 - (int)((packed >> 24) & 255) };
                        }
                        else if (state is NetworkOddOneOutState)
                        {
                            var snapshot = (Unity.Netcode.NetworkVariable<OddOneOutPublicSnapshot>)type.GetField("_snapshot", flags).GetValue(state);
                            setRanks = packed => snapshot.Value = packed == 0 ? default : new OddOneOutPublicSnapshot {
                                Phase = MazeParty.Gameplay.Minigames.OddOneOut.OddOneOutPhase.Complete,
                                Score0 = 4 - (int)(packed & 255), Score1 = 4 - (int)((packed >> 8) & 255),
                                Score2 = 4 - (int)((packed >> 16) & 255), Score3 = 4 - (int)((packed >> 24) & 255) };
                        }
                        else if (state is NetworkCoinCollectState)
                        {
                            var players = (Unity.Netcode.NetworkList<CoinCollectPlayerNetworkSnapshot>)
                                type.GetField("_players", flags).GetValue(state);
                            setRanks = packed =>
                            {
                                players.Clear();
                                for (var slot = 0; slot < 4; slot++)
                                    players.Add(new CoinCollectPlayerNetworkSnapshot { Rank = (byte)(packed >> (slot * 8)) });
                            };
                        }
                        else
                        {
                            var ranks = (Unity.Netcode.NetworkVariable<uint>)field.GetValue(state);
                            setRanks = packed => ranks.Value = packed;
                        }
                        var id = (MazeParty.Gameplay.Minigames.ScheduledMinigameId)System.Enum.Parse(
                            typeof(MazeParty.Gameplay.Minigames.ScheduledMinigameId), name);
                        Assert.That(MinigameRuntimeRegistry.TryGet(id, out var runtime), Is.True);
                        var presenter = new MinigameResultPresenter();
                        foreach (var input in new[] { new[] { 4, 2, 2, 1 }, new[] { 1, 1, 1, 1 } })
                        {
                            uint packed = 0;
                            for (var slot = 0; slot < 4; slot++) packed |= (uint)input[slot] << (slot * 8);
                            setRanks(packed);
                            var lines = presenter.Present(runtime, null, slot => "P" + (slot + 1)).Split('\n');
                            Assert.That(lines.Length, Is.EqualTo(4), name);
                            var expected = input.OrderBy(value => value).ToArray();
                            for (var row = 0; row < 4; row++)
                            {
                                Assert.That(lines[row], Does.StartWith(expected[row] + "."), name);
                                Assert.That(lines[row], Does.Contain("+" + MazeParty.Gameplay.Minigames.MinigameRewardRules.GetFinalPlacementGold(expected[row])), name);
                            }
                        }
                        setRanks(0);
                        Assert.That(presenter.Present(runtime, null, slot => "P" + (slot + 1)), Does.Not.Contain("\n"), name + " waits for a complete snapshot");
                    }
                    finally
                    {
                        Object.DestroyImmediate(root);
                        type?.GetProperty("Instance").SetValue(null, previousInstance);
                    }
                }
            }
            finally
            {
                warning.SetValue(null, previousWarning);
                typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, previousMatch);
            }
        }


    }
}
