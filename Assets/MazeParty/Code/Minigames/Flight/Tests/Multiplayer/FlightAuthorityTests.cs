using System.Reflection;
using MazeParty.Gameplay.Minigames.Flight;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class FlightAuthorityTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test]
        public void Snapshot_RoundTripsAtomicFlightPhysicsAndInputAcknowledgements()
        {
            var source = new FlightSnapshot { Phase=FlightPhase.Running, Seed=88999, AliveMask=13,
                Epoch=7, Paused=true, RunElapsed=24, PausedRemaining=96, SampledAt=127, EndsAt=223,
                Player0=new FlightPlayerSnapshot {Height=2.1,Velocity=-3,Survival=12.5,Flaps=35,Sequence=38,Rank=2},
                Player1=new FlightPlayerSnapshot {Height=-1.1,Velocity=4.5,Survival=24,Flaps=40,Sequence=43,Rank=1} };
            using(var writer=new FastBufferWriter(512,Unity.Collections.Allocator.Temp))
            {
                writer.WriteNetworkSerializable(source);
                using(var reader=new FastBufferReader(writer,Unity.Collections.Allocator.Temp))
                {reader.ReadNetworkSerializable(out FlightSnapshot restored);Assert.That(restored.Equals(source),Is.True);}
            }
        }
        [Test]
        public void Authority_RejectsUnspawnedNonServerStaleDuplicateDeadPausedAndLateInput()
        {
            var root=new GameObject("Flight authority contract");
            var warning=typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",BindingFlags.Static|BindingFlags.NonPublic);
            var old=warning.GetValue(null);warning.SetValue(null,true);
            var state=root.AddComponent<NetworkFlightState>();
            try
            {
                NetworkCountdownTestAccess.SetPrivateField(state,"_simulation",new FlightMatchState(417));
                var snapshot=new FlightSnapshot{Phase=FlightPhase.Running,AliveMask=15,Epoch=7,EndsAt=Time.unscaledTimeAsDouble+120};
                NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                var gate=typeof(NetworkFlightState).GetMethod("ValidateEnvelope",Flags);
                Assert.That(gate.Invoke(state,new object[]{7U,1U,0}),Is.False);
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state,true);
                Assert.That(gate.Invoke(state,new object[]{7U,1U,0}),Is.False);
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(state,true);
                foreach(var sample in new[]{(7U,1U,0,true),(6U,1U,0,false),(0U,1U,0,false),
                    (7U,0U,0,false),(7U,1U,-1,false),(7U,1U,4,false)})
                    Assert.That(gate.Invoke(state,new object[]{sample.Item1,sample.Item2,sample.Item3}),Is.EqualTo(sample.Item4));
                ((uint[])typeof(NetworkFlightState).GetField("_sequences",Flags).GetValue(state))[0]=2;
                Assert.That(gate.Invoke(state,new object[]{7U,2U,0}),Is.False);
                foreach(var phase in new[]{FlightPhase.Countdown,FlightPhase.Result,FlightPhase.Complete})
                {
                    snapshot.Phase=phase;NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                    Assert.That(gate.Invoke(state,new object[]{7U,3U,0}),Is.False);
                }
                snapshot.Phase=FlightPhase.Running;snapshot.AliveMask=14;NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                Assert.That(gate.Invoke(state,new object[]{7U,3U,0}),Is.False);
                snapshot.AliveMask=15;snapshot.Paused=true;NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                Assert.That(gate.Invoke(state,new object[]{7U,3U,0}),Is.False);
                snapshot.Paused=false;snapshot.EndsAt=Time.unscaledTimeAsDouble;NetworkCountdownTestAccess.SetNetworkValue(state,"_snapshot",snapshot);
                Assert.That(gate.Invoke(state,new object[]{7U,3U,0}),Is.False);
                Assert.That(state.TryFlapOnServer(null,7,3),Is.False);
            }
            finally{typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state,false);Object.DestroyImmediate(root);warning.SetValue(null,old);}
        }
        [Test]
        public void PauseResume_PreservesFlightPhysicsAndCourse_AndInvalidatesOldEpochs()
        {
            var root=new GameObject("Flight pause contract");
            var warning=typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",BindingFlags.Static|BindingFlags.NonPublic);
            var old=warning.GetValue(null);warning.SetValue(null,true);var state=root.AddComponent<NetworkFlightState>();
            try
            {
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state,true);typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(state,true);
                var game=new FlightMatchState(417);NetworkCountdownTestAccess.SetPrivateField(state,"_simulation",game);
                NetworkCountdownTestAccess.SetPrivateField(state,"_startedAt",0d);
                typeof(NetworkFlightState).GetMethod("Publish",Flags).Invoke(state,new object[]{false,true});
                var epoch=state.InputEpoch;state.PauseOnServer(1.25);
                Assert.That(state.Remaining,Is.EqualTo(1.75));Assert.That(state.RunElapsed,Is.Zero);
                Assert.That(state.InputEpoch,Is.Not.EqualTo(epoch));epoch=state.InputEpoch;state.ResumeOnServer(101.25);
                Assert.That(state.InputEpoch,Is.Not.EqualTo(epoch));
                var snapshot=(NetworkVariable<FlightSnapshot>)typeof(NetworkFlightState).GetField("_snapshot",Flags).GetValue(state);
                Assert.That(snapshot.Value.EndsAt,Is.EqualTo(103));
                typeof(NetworkFlightState).GetMethod("Advance",Flags).Invoke(state,new object[]{103d});
                for(var i=0;i<4;i++)game.TryFlap(i);
                state.PauseOnServer(103.2);var height=game.Height(0);var velocity=game.Velocity(0);var elapsed=game.RunElapsed;var seed=state.Seed;epoch=state.InputEpoch;
                Assert.That(state.CanAcceptInputForSlot(0),Is.False);Assert.That(state.RunElapsed,Is.EqualTo(elapsed));
                state.ResumeOnServer(503.2);Assert.That(game.Height(0),Is.EqualTo(height));Assert.That(game.Velocity(0),Is.EqualTo(velocity));Assert.That(game.RunElapsed,Is.EqualTo(elapsed));Assert.That(state.Seed,Is.EqualTo(seed));
                Assert.That(state.InputEpoch,Is.Not.EqualTo(epoch));Assert.That(snapshot.Value.EndsAt,Is.EqualTo(623).Within(1e-7));
            }
            finally{typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state,false);Object.DestroyImmediate(root);warning.SetValue(null,old);}
        }
    }
}
