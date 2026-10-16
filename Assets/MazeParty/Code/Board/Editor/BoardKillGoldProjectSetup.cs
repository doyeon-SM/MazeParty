using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Editor
{
    public static class BoardKillGoldProjectSetup
    {
        internal static void EnsureView(GameObject topologyRoot)
        {
            var view = topologyRoot.GetComponent<BoardKillGoldWorldView>();
            if (view == null) view = topologyRoot.AddComponent<BoardKillGoldWorldView>();
            var coin = AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardFlowProjectSetup.ResourceTransferCoinPrefabPath);
            if (coin == null) throw new System.InvalidOperationException("Board event coin prefab is missing.");
            view.Configure(coin);
            EditorUtility.SetDirty(view);
        }
    }
}
