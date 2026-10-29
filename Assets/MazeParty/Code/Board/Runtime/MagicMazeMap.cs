using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public sealed class MagicMazeMap : MonoBehaviour
    {
        [SerializeField] private MagicMazeWall[] interiorWalls = Array.Empty<MagicMazeWall>();
        private int _seed, _generation = -1;
        public MagicMazeWall[] InteriorWalls => interiorWalls;
        public bool HasRequiredReferences => interiorWalls.Length > 0 &&
            Array.TrueForAll(interiorWalls, w => w != null && w.HasRequiredReferences);
        public void Configure(MagicMazeWall[] walls) { interiorWalls = walls; _generation = -1; }
        public void Apply(int seed, int generation)
        {
            if (generation < 0 || (_seed == seed && _generation == generation)) return;
            var disabled = MagicMazeRules.SelectWalls(interiorWalls.Length, seed, generation);
            for (var i = 0; i < interiorWalls.Length; i++) interiorWalls[i].SetInactive(disabled[i]);
            _seed = seed; _generation = generation;
        }
    }
}
