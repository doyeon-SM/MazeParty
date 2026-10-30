using System;
using System.Linq;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed partial class BoardChestAuthorityTests
    {
        [Test]
        public void MagicMaze_ShufflesOnceAtFinalIcon_ServerOnly_AndRestoresExactLayout()
        {
            Set(_match,"_boardMapId",new FixedString64Bytes(MagicMazeRules.MapId));
            Set(_match,"_boardEffectSeed",42); Set(_match,"_boardEffectRevision",1);
            Invoke("InitializeMagicMazeOnServer");
            var flow=new BoardFlowStateMachine(totalTurns:15,magicMaze:true);
            flow.Start(0,3);flow.Tick(7);
            for(var i=0;i<4;i++)flow.TryReportPlayerArrived(i,8);
            flow.Tick(13);flow.TryCompleteCombat(13);flow.Tick(17);
            typeof(NetworkMatchState).GetField("_flow",Private).SetValue(_match,flow);
            var revision=_match.BoardEffectRevision;
            Invoke("AdvanceMagicMazeOnServer",21.499); Assert.That(_match.BoardEffectRevision,Is.EqualTo(revision));
            Authority(_match,false);Invoke("AdvanceMagicMazeOnServer",21.5);Assert.That(_match.BoardEffectRevision,Is.EqualTo(revision));
            Authority(_match,true);Invoke("SuspendSimulationOnServer",21.5);Assert.That(_match.BoardEffectRevision,Is.EqualTo(revision+1));
            Assert.That(flow.IsPaused, Is.True);
            Invoke("AdvanceMagicMazeOnServer",121.5);Assert.That(_match.BoardEffectRevision,Is.EqualTo(revision+1));
            flow.Resume(121.5);
            Invoke("AdvanceMagicMazeOnServer",122d);Assert.That(_match.BoardEffectRevision,Is.EqualTo(revision+1));
            var saved=(MagicMazeEffectSnapshot[])typeof(NetworkMatchState).GetMethod("CaptureMagicMazeEffects",Private).Invoke(_match,null);
            var snapshot=new MatchRecoverySnapshot{boardMapId=MagicMazeRules.MapId,boardEffectSeed=42,boardEffectRevision=revision+1,
                turn=3,scheduleTurnCount=15,magicMazeShuffledTurn=3,checkpoint=MatchRecoveryCheckpoint.MinigameIntroReady,magicMazeEffects=saved};
            Assert.That(NetworkMatchState.AreMagicMazeSnapshotsValid(snapshot,_topology),Is.True);
            Invoke("InitializeMagicMazeOnServer");Invoke("RestoreBoardEffects",snapshot);
            foreach(var pair in saved)
            {Assert.That(_match.TryGetBoardLandingEffect(pair.Coordinate,out var effect),Is.True);Assert.That((byte)effect,Is.EqualTo(pair.Effect));}
            snapshot.magicMazeWallGeneration=1;Assert.That(NetworkMatchState.AreMagicMazeSnapshotsValid(snapshot,_topology),Is.False);
            snapshot.magicMazeWallGeneration=0;snapshot.magicMazeEffects=saved.Concat(new[]{saved[0]}).ToArray();
            Assert.That(NetworkMatchState.AreMagicMazeSnapshotsValid(snapshot,_topology),Is.False);
            snapshot.magicMazeEffects=(MagicMazeEffectSnapshot[])saved.Clone(); snapshot.magicMazeEffects[0].Effect=0;
            Assert.That(NetworkMatchState.AreMagicMazeSnapshotsValid(snapshot,_topology),Is.False);
        }

        [Test]
        public void MagicMaze_ExcludesWallAndForestItems_FromRewardsShopsAndUse()
        {
            Set(_match,"_boardMapId",new FixedString64Bytes(MagicMazeRules.MapId));
            Assert.That(PrototypeItemCatalog.Get(PrototypeItemId.Wall).IsAvailableOnMap(MagicMazeRules.MapId),Is.False);
            for(var seed=0;seed<50;seed++)
            {
                var item=PrototypeItemCatalog.GetRandomId(new System.Random(seed),MagicMazeRules.MapId);
                Assert.That(item,Is.Not.EqualTo(PrototypeItemId.Wall).And.Not.EqualTo(PrototypeItemId.GhostPass));
                var shop=new ItemShopStock(seed,mapId:MagicMazeRules.MapId);
                for(var offer=0;offer<ItemShopRules.OfferCount;offer++)
                    Assert.That(shop.GetOffer(offer),Is.Not.EqualTo(PrototypeItemId.Wall).And.Not.EqualTo(PrototypeItemId.GhostPass));
            }
            Assert.That(_match.TryGetWallPlacement(_player,Vector3.zero,Vector3.forward,out _),Is.False);
        }
    }
}
