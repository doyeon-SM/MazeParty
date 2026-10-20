using System.Reflection;
using MazeParty.Gameplay.Minigames.ShellGame;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class ShellGameAuthorityTests
    {
        private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
        [Test]
        public void OwnerOnlySelections_AndAtomicPublicSnapshot_RoundTripWithoutChoiceLeak()
        {
            var root=new GameObject("ShellGame privacy contract");
            try
            {
                var avatar=root.AddComponent<NetworkPlayerAvatar>();
                var choice=(NetworkVariableBase)typeof(NetworkPlayerAvatar).GetField("_shellGameChoice",Flags).GetValue(avatar);
                Assert.That(choice.ReadPerm,Is.EqualTo(NetworkVariableReadPermission.Owner));
                Assert.That(choice.WritePerm,Is.EqualTo(NetworkVariableWritePermission.Server));
                foreach(var phase in new[]{ShellGamePhase.Choosing,ShellGamePhase.Reveal})
                {
                    var source=new ShellGamePublicSnapshot {Phase=phase,Round=3,Epoch=4,Seed=ulong.MaxValue,
                        RevealedChoices=phase==ShellGamePhase.Reveal?(byte)79:(byte)0,Score0=2,Score1=3,Paused=true,PausedRemaining=4.5,EndsAt=100};
                    using(var writer=new FastBufferWriter(128,Unity.Collections.Allocator.Temp))
                    {
                        writer.WriteNetworkSerializable(source);
                        using(var reader=new FastBufferReader(writer,Unity.Collections.Allocator.Temp))
                        { reader.ReadNetworkSerializable(out ShellGamePublicSnapshot copy); Assert.That(copy.Equals(source),Is.True); }
                    }
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void PauseResume_PreservesShufflePositionChoiceAndScore_AndChangesEpoch()
        {
            var root=new GameObject("ShellGame pause contract");
            var warning=typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",BindingFlags.Static|BindingFlags.NonPublic);
            var previous=warning.GetValue(null); warning.SetValue(null,true);
            var state=root.AddComponent<NetworkShellGameState>();
            try
            {
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state,true);
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(state,true);
                var sim=new ShellGameMatchState(77);
                NetworkCountdownTestAccess.SetPrivateField(state,"_simulation",sim);
                NetworkCountdownTestAccess.SetPrivateField(state,"_startedAt",0d);
                var publish=typeof(NetworkShellGameState).GetMethod("Publish",Flags);
                var advance=typeof(NetworkShellGameState).GetMethod("Advance",Flags);
                publish.Invoke(state,new object[]{false,true});
                while(sim.Phase!=ShellGamePhase.Shuffling) advance.Invoke(state,new object[]{sim.PhaseEndsAt});
                var at=sim.Elapsed+.4;
                var epoch=state.InputEpoch; state.PauseOnServer(at);
                var remaining=state.Remaining;
                Assert.That(state.InputEpoch,Is.Not.EqualTo(epoch));
                Assert.That(state.CanAcceptInputForSlot(0),Is.False);
                epoch=state.InputEpoch; state.ResumeOnServer(at+100);
                Assert.That(state.InputEpoch,Is.Not.EqualTo(epoch));
                var snapshot=(NetworkVariable<ShellGamePublicSnapshot>)typeof(NetworkShellGameState).GetField("_snapshot",Flags).GetValue(state);
                Assert.That(snapshot.Value.EndsAt,Is.EqualTo(at+100+remaining).Within(1e-9));
                advance.Invoke(state,new object[]{snapshot.Value.EndsAt});
                Assert.That(sim.Phase,Is.EqualTo(ShellGamePhase.Choosing));
                Assert.That(sim.TryChoose(0,sim.Plan.CoinBox),Is.True);
                publish.Invoke(state,new object[]{false,false});
                at=sim.Elapsed+100+1;
                state.PauseOnServer(at);
                Assert.That(state.Remaining,Is.EqualTo(9).Within(1e-9));
                state.ResumeOnServer(at+100);
                Assert.That(sim.GetChoice(0),Is.EqualTo(sim.Plan.CoinBox));
                Assert.That(snapshot.Value.RevealedChoices,Is.Zero);
                advance.Invoke(state,new object[]{snapshot.Value.EndsAt});
                Assert.That(state.Phase,Is.EqualTo(ShellGamePhase.Reveal));
                Assert.That(state.GetScore(0),Is.EqualTo(1));
            }
            finally
            {
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state,false);
                Object.DestroyImmediate(root); warning.SetValue(null,previous);
            }
        }
        [Test]
        public void InputEnvelope_RejectsNonServerStaleDuplicatePausedLateAndInvalidSlot()
        {
            var root=new GameObject("ShellGame input contract");
            var warning=typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",BindingFlags.Static|BindingFlags.NonPublic);
            var previous=warning.GetValue(null); warning.SetValue(null,true);
            var state=root.AddComponent<NetworkShellGameState>();
            try
            {
                NetworkCountdownTestAccess.SetPrivateField(state,"_simulation",new ShellGameMatchState(1));
                var snapshot=new ShellGamePublicSnapshot {Phase=ShellGamePhase.Choosing,Round=1,Epoch=7,EndsAt=Time.unscaledTimeAsDouble+10};
                NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                var gate=typeof(NetworkShellGameState).GetMethod("ValidateEnvelope",Flags);
                Assert.That(gate.Invoke(state,new object[]{1,7U,1U,0}),Is.False);
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state,true);
                Assert.That(gate.Invoke(state,new object[]{1,7U,1U,0}),Is.False);
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(state,true);
                foreach(var sample in new[]{(1,7U,1U,0,true),(2,7U,1U,0,false),(1,6U,1U,0,false),
                    (1,0U,1U,0,false),(1,7U,0U,0,false),(1,7U,1U,-1,false),(1,7U,1U,4,false)})
                    Assert.That(gate.Invoke(state,new object[]{sample.Item1,sample.Item2,sample.Item3,sample.Item4}),Is.EqualTo(sample.Item5));
                ((uint[])typeof(NetworkShellGameState).GetField("_sequences",Flags).GetValue(state))[0]=2;
                Assert.That(gate.Invoke(state,new object[]{1,7U,2U,0}),Is.False);
                snapshot.Paused=true; NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                Assert.That(gate.Invoke(state,new object[]{1,7U,3U,0}),Is.False);
                snapshot.Paused=false; snapshot.EndsAt=Time.unscaledTimeAsDouble;
                NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                Assert.That(gate.Invoke(state,new object[]{1,7U,3U,0}),Is.False);
                Assert.That(state.TrySetChoiceOnServer(null,0,1,7,3),Is.False);
            }
            finally
            {
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state,false);
                Object.DestroyImmediate(root); warning.SetValue(null,previous);
            }
        }
    }
}
