using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MagicMazeRulesTests
    {
        [Test]
        public void MapChange_AfterLandingBeforeReady_RespectsPeriodsFinalTurnAndPause()
        {
            foreach (var magic in new[]{false,true})
            foreach (var turn in new[]{1,3,5,6,10,15,18,20})
            {
                var flow = new BoardFlowStateMachine(totalTurns:20, magicMaze:magic);
                flow.Start(0, turn); flow.Tick(7);
                for(var slot=0;slot<4;slot++) flow.TryReportPlayerArrived(slot,8);
                flow.Tick(13); Assert.That(flow.TryCompleteCombat(13), Is.True);
                flow.Tick(16.999); Assert.That(flow.State, Is.EqualTo(BoardFlowState.LandingEffectResolve));
                flow.Tick(17);
                var changes = magic && turn < 20 && (turn%3==0 || turn%5==0);
                Assert.That(flow.State, Is.EqualTo(changes ? BoardFlowState.MagicMazeChange : BoardFlowState.MinigameIntroReady));
                if (!changes) continue;
                var duration = MagicMazeRules.Duration(turn,20);
                if (duration>0)
                {
                    flow.Pause(18); var remaining=flow.GetStateRemaining(18);
                    flow.Tick(118); Assert.That(flow.GetStateRemaining(118), Is.EqualTo(remaining));
                    flow.Resume(118); flow.Tick(117+duration-.001);
                    Assert.That(flow.State, Is.EqualTo(BoardFlowState.MagicMazeChange));
                    flow.Tick(117+duration);
                }
                else flow.Tick(17);
                Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameIntroReady));
            }
            foreach (var total in new[]{5,10,15,20})
                Assert.That(MagicMazeRules.ChangesMap(total,total), Is.False);
            Assert.That(MagicMazeRules.PreviewStep(1.999), Is.EqualTo(-1));
            for (var i=0;i<5;i++) Assert.That(MagicMazeRules.PreviewStep(2+i*.5), Is.EqualTo(i));
            Assert.That(MagicMazeRules.PreviewStep(4.5), Is.EqualTo(-1));
        }
        [Test]
        public void WallRotation_PreservesCount_ExcludesPrevious_AndReplaysExactly()
        {
            foreach(var count in new[]{4,31,94,100,104})
            foreach(var seed in new[]{1,42,int.MinValue,int.MaxValue})
            {
                bool[] previous=null;
                for(var generation=0;generation<5;generation++)
                {
                    var result=MagicMazeRules.SelectWalls(count,seed,generation);
                    Assert.That(result.Count(x=>x), Is.EqualTo((count*3+5)/10));
                    Assert.That(MagicMazeRules.SelectWalls(count,seed,generation), Is.EqualTo(result));
                    if(previous!=null) Assert.That(result.Where((value,i)=>value&&previous[i]), Is.Empty);
                    previous=result;
                }
            }
        }
        [Test]
        public void TileShuffle_PreservesCountsAndFixedShops_IndependentOfEnumerationOrder()
        {
            var source = Enumerable.Range(0,60).ToDictionary(i=>new Vector2Int(i%8,i/8),i=>(BoardLandingEffectType)(1+i%8));
            var fixedTiles=new HashSet<Vector2Int>{new Vector2Int(0,0),new Vector2Int(1,0),new Vector2Int(2,0)};
            foreach(var seed in new[]{1,42,int.MinValue})
            {
                var result=MagicMazeRules.ShuffleEffects(source,fixedTiles,seed);
                var reversed=MagicMazeRules.ShuffleEffects(source.Reverse().ToDictionary(p=>p.Key,p=>p.Value),fixedTiles,seed);
                Assert.That(result, Is.EquivalentTo(reversed));
                foreach(var tile in fixedTiles) Assert.That(result[tile],Is.EqualTo(source[tile]));
                Assert.That(result.Values,Is.EquivalentTo(source.Values));
                Assert.That(result.Any(pair=>pair.Value!=source[pair.Key]),Is.True);
            }
        }
        [Test]
        public void InactiveWall_StillStopsMovement_ButAllowsProjectilesBlastAndMeleeSweeps()
        {
            var root=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var outline=new GameObject("Authored outline"); outline.transform.SetParent(root.transform);
            try
            {
                root.transform.position=new Vector3(1000,2,1000); root.transform.localScale=new Vector3(6,4,.8f);
                var collider=root.GetComponent<Collider>(); var renderer=root.GetComponent<Renderer>();
                var wall=root.AddComponent<MagicMazeWall>(); wall.Configure(new[]{collider},new[]{renderer},outline);
                var origin=root.transform.position-Vector3.forward*3; var target=origin+Vector3.forward*6;
                Physics.SyncTransforms();
                foreach(var inactive in new[]{false,true,false})
                {
                    wall.SetInactive(inactive);
                    Assert.That(Physics.Raycast(origin,Vector3.forward,6), Is.True, "Physical wall remains solid.");
                    foreach(var radius in new[]{0f,.15f,.35f})
                        Assert.That(BoardItemPhysics.Cast(origin,Vector3.forward,6,null,out _,radius),Is.EqualTo(!inactive));
                    Assert.That(BoardItemPhysics.HasBlastLineOfSight(origin,target),Is.EqualTo(inactive));
                    Assert.That(MagicMazeWall.AllowsSightAndAttacks(collider),Is.EqualTo(inactive));
                    Assert.That(renderer.enabled,Is.EqualTo(!inactive));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
