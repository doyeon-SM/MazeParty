using System.Reflection;
using MazeParty.Gameplay.Minigames.Race2;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class Race2AuthorityTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test]
        public void Snapshot_RoundTripsAtomicLanesAliveClockAndInputAcknowledgements()
        {
            var source=new Race2Snapshot{Phase=Race2Phase.Running,Round=2,Seed=88999,AliveMask=13,LaneMask=6,
                Scores=0x03010203,Epoch=7,Sequence0=1,Sequence1=2,Sequence2=3,Sequence3=4,
                Paused=true,RoundElapsed=24,PausedRemaining=96,SampledAt=127,EndsAt=223};
            using(var writer=new FastBufferWriter(256,Unity.Collections.Allocator.Temp))
            {
                writer.WriteNetworkSerializable(source);
                using(var reader=new FastBufferReader(writer,Unity.Collections.Allocator.Temp))
                {reader.ReadNetworkSerializable(out Race2Snapshot restored);Assert.That(restored.Equals(source),Is.True);}
            }
        }
        [Test]
        public void Authority_RejectsUnspawnedNonServerStaleDuplicateDeadPausedAndLateInput()
        {
            var root=new GameObject("Race2 authority contract");
            var warning=typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",BindingFlags.Static|BindingFlags.NonPublic);
            var old=warning.GetValue(null);warning.SetValue(null,true);
            var state=root.AddComponent<NetworkRace2State>();
            try
            {
                NetworkCountdownTestAccess.SetPrivateField(state,"_simulation",new Race2MatchState(417));
                var snapshot=new Race2Snapshot{Phase=Race2Phase.Running,Round=1,AliveMask=15,Epoch=7,EndsAt=Time.unscaledTimeAsDouble+120};
                NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                var gate=typeof(NetworkRace2State).GetMethod("ValidateEnvelope",Flags);
                Assert.That(gate.Invoke(state,new object[]{1,7U,1U,0}),Is.False);
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state,true);
                Assert.That(gate.Invoke(state,new object[]{1,7U,1U,0}),Is.False);
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(state,true);
                foreach(var sample in new[]{(1,7U,1U,0,true),(2,7U,1U,0,false),(1,6U,1U,0,false),(1,0U,1U,0,false),
                    (1,7U,0U,0,false),(1,7U,1U,-1,false),(1,7U,1U,4,false)})
                    Assert.That(gate.Invoke(state,new object[]{sample.Item1,sample.Item2,sample.Item3,sample.Item4}),Is.EqualTo(sample.Item5));
                ((uint[])typeof(NetworkRace2State).GetField("_sequences",Flags).GetValue(state))[0]=2;
                Assert.That(gate.Invoke(state,new object[]{1,7U,2U,0}),Is.False);
                foreach(var phase in new[]{Race2Phase.Countdown,Race2Phase.RoundResult,Race2Phase.Complete})
                {
                    snapshot.Phase=phase;NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                    Assert.That(gate.Invoke(state,new object[]{1,7U,3U,0}),Is.False);
                }
                snapshot.Phase=Race2Phase.Running;snapshot.AliveMask=14;NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                Assert.That(gate.Invoke(state,new object[]{1,7U,3U,0}),Is.False);
                snapshot.AliveMask=15;snapshot.Paused=true;NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                Assert.That(gate.Invoke(state,new object[]{1,7U,3U,0}),Is.False);
                snapshot.Paused=false;snapshot.EndsAt=Time.unscaledTimeAsDouble;NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                Assert.That(gate.Invoke(state,new object[]{1,7U,3U,0}),Is.False);
                Assert.That(state.TrySetLaneOnServer(null,1,1,7,3),Is.False);
            }
            finally{typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state,false);Object.DestroyImmediate(root);warning.SetValue(null,old);}
        }
        [Test]
        public void PauseResume_PreservesPreviewAndRunningBoxes_AndInvalidatesOldEpochs()
        {
            var root=new GameObject("Race2 pause contract");
            var warning=typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",BindingFlags.Static|BindingFlags.NonPublic);
            var old=warning.GetValue(null);warning.SetValue(null,true);var state=root.AddComponent<NetworkRace2State>();
            try
            {
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state,true);typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(state,true);
                var game=new Race2MatchState(417);NetworkCountdownTestAccess.SetPrivateField(state,"_simulation",game);
                NetworkCountdownTestAccess.SetPrivateField(state,"_startedAt",0d);
                typeof(NetworkRace2State).GetMethod("Publish",Flags).Invoke(state,new object[]{false,true});
                var epoch=state.InputEpoch;state.PauseOnServer(1.25);
                Assert.That(state.Remaining,Is.EqualTo(1.75));Assert.That(state.RoundElapsed,Is.Zero);
                Assert.That(state.InputEpoch,Is.Not.EqualTo(epoch));epoch=state.InputEpoch;state.ResumeOnServer(101.25);
                Assert.That(state.InputEpoch,Is.Not.EqualTo(epoch));
                var snapshot=(NetworkVariable<Race2Snapshot>)typeof(NetworkRace2State).GetField("_snapshot",Flags).GetValue(state);
                Assert.That(snapshot.Value.EndsAt,Is.EqualTo(103));
                typeof(NetworkRace2State).GetMethod("Advance",Flags).Invoke(state,new object[]{103d});
                for(var i=0;i<4;i++)game.TrySetLane(i,(byte)(1-game.Boxes.Lane(0)));
                state.PauseOnServer(103.2);var elapsed=game.RoundElapsed;var seed=state.Seed;epoch=state.InputEpoch;
                Assert.That(state.CanAcceptInputForSlot(0),Is.False);Assert.That(state.RoundElapsed,Is.EqualTo(elapsed));
                state.ResumeOnServer(503.2);Assert.That(game.RoundElapsed,Is.EqualTo(elapsed));Assert.That(state.Seed,Is.EqualTo(seed));
                Assert.That(state.InputEpoch,Is.Not.EqualTo(epoch));Assert.That(snapshot.Value.EndsAt,Is.EqualTo(563).Within(1e-7));
            }
            finally{typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state,false);Object.DestroyImmediate(root);warning.SetValue(null,old);}
        }
    }
}
