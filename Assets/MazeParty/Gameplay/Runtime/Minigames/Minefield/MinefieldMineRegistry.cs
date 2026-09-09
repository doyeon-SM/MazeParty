using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames.Minefield
{
    /// <summary>
    /// Scene-local mine index used by sonar and sirens. It deliberately avoids a
    /// static singleton so additive minigame scenes cannot leak mines into each other.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinefieldMineRegistry : MonoBehaviour
    {
        [SerializeField] private bool collectChildMinesOnEnable = true;
        [SerializeField] private List<MinefieldMine> mines = new List<MinefieldMine>();

        public IReadOnlyList<MinefieldMine> Mines => mines;

        private void OnEnable()
        {
            if (!collectChildMinesOnEnable)
            {
                PruneMissingMines();
                return;
            }

            var childMines = GetComponentsInChildren<MinefieldMine>(true);
            for (var index = 0; index < childMines.Length; index++)
            {
                Register(childMines[index]);
            }
        }

        public bool Register(MinefieldMine mine)
        {
            if (mine == null || mines.Contains(mine))
            {
                return false;
            }

            mines.Add(mine);
            return true;
        }

        public bool Unregister(MinefieldMine mine)
        {
            return mine != null && mines.Remove(mine);
        }

        public bool TryGetNearestArmedMine(
            Vector3 origin,
            out MinefieldMine nearest,
            out float planarDistance)
        {
            PruneMissingMines();
            nearest = null;
            var nearestSquaredDistance = float.PositiveInfinity;

            for (var index = 0; index < mines.Count; index++)
            {
                var candidate = mines[index];
                if (candidate == null || !candidate.IsArmed)
                {
                    continue;
                }

                var delta = candidate.transform.position - origin;
                delta.y = 0f;
                var squaredDistance = delta.sqrMagnitude;
                if (squaredDistance >= nearestSquaredDistance)
                {
                    continue;
                }

                nearest = candidate;
                nearestSquaredDistance = squaredDistance;
            }

            planarDistance = nearest != null
                ? Mathf.Sqrt(nearestSquaredDistance)
                : float.PositiveInfinity;
            return nearest != null;
        }

        public int GetArmedMinesWithinRadius(
            Vector3 origin,
            float radius,
            List<MinefieldMine> results)
        {
            if (results == null)
            {
                return 0;
            }

            results.Clear();
            PruneMissingMines();
            var squaredRadius = Mathf.Max(0f, radius);
            squaredRadius *= squaredRadius;

            for (var index = 0; index < mines.Count; index++)
            {
                var candidate = mines[index];
                if (candidate == null || !candidate.IsArmed)
                {
                    continue;
                }

                var delta = candidate.transform.position - origin;
                delta.y = 0f;
                if (delta.sqrMagnitude <= squaredRadius)
                {
                    results.Add(candidate);
                }
            }

            return results.Count;
        }

        private void PruneMissingMines()
        {
            for (var index = mines.Count - 1; index >= 0; index--)
            {
                if (mines[index] == null)
                {
                    mines.RemoveAt(index);
                }
            }
        }
    }
}
