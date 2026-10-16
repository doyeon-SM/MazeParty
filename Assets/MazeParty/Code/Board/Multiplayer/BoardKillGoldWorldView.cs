using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class BoardKillGoldWorldView : MonoBehaviour
    {
        [SerializeField] private GameObject coinPrefab;
        private readonly Dictionary<int, Flight> _flights = new Dictionary<int, Flight>();
        private readonly HashSet<int> _activeIds = new HashSet<int>();
        private readonly List<int> _removedIds = new List<int>();

        private sealed class Flight
        {
            public GameObject[] Coins;
        }

        public bool HasRequiredReferences => coinPrefab != null;

#if UNITY_EDITOR
        public void Configure(GameObject prefab) { coinPrefab = prefab; }
#endif

        private void Update()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsSpawned || !match.GameplayEnabled)
            {
                Clear();
                return;
            }
            if (coinPrefab == null) return;
            _activeIds.Clear();
            var now = match.DeathGoldPresentationNow;
            for (var eventIndex = 0; eventIndex < match.DeathGoldTransfers.Count; eventIndex++)
            {
                var snapshot = match.DeathGoldTransfers[eventIndex];
                _activeIds.Add(snapshot.Id);
                var elapsed = now - snapshot.StartedAt;
                if (!_flights.TryGetValue(snapshot.Id, out var flight))
                {
                    flight = new Flight { Coins = new GameObject[snapshot.Amount] };
                    _flights.Add(snapshot.Id, flight);
                    for (var index = 0; index < flight.Coins.Length; index++)
                    {
                        // Rejoining clients do not replay coins that have already arrived.
                        if (now >= snapshot.StartedAt + BoardDeathGoldPresentationRules.ArrivalTime(index, snapshot.Amount))
                            continue;
                        var coin = Instantiate(coinPrefab, transform);
                        coin.name = "Death Gold " + snapshot.Id + " / " + index;
                        PrepareVisual(coin);
                        flight.Coins[index] = coin;
                    }
                }

                var recipient = match.GetAvatarForSlot(snapshot.RecipientSlot);
                var arrived = 0;
                for (var index = 0; index < flight.Coins.Length; index++)
                {
                    var coin = flight.Coins[index];
                    if (coin == null) continue;
                    if (now >= snapshot.StartedAt + BoardDeathGoldPresentationRules.ArrivalTime(index, snapshot.Amount))
                    {
                        Release(coin);
                        flight.Coins[index] = null;
                        arrived++;
                        continue;
                    }
                    if (recipient == null) continue;
                    coin.transform.position = BoardDeathGoldPresentationRules.Position(
                        snapshot.Origin, recipient.transform.position + Vector3.up * 0.6f,
                        index, snapshot.Amount, elapsed, snapshot.RecipientSlot * 31);
                    coin.transform.rotation = Quaternion.Euler(0f, (float)elapsed * 360f + index * 30f, 0f);
                }
                if (arrived > 0)
                    BoardFlowView.Instance?.PresentDeathGoldArrival(snapshot.RecipientSlot, arrived);
            }

            _removedIds.Clear();
            foreach (var pair in _flights)
                if (!_activeIds.Contains(pair.Key)) _removedIds.Add(pair.Key);
            foreach (var id in _removedIds)
            {
                Release(_flights[id]);
                _flights.Remove(id);
            }
        }

        private static void PrepareVisual(GameObject coin)
        {
            foreach (var behaviour in coin.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            foreach (var collider in coin.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var body in coin.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
        }

        private void OnDisable() { Clear(); }

        private void Clear()
        {
            foreach (var pair in _flights) Release(pair.Value);
            _flights.Clear();
        }

        private static void Release(Flight flight)
        {
            foreach (var coin in flight.Coins) if (coin != null) Release(coin);
        }

        private static void Release(GameObject coin)
        {
            if (Application.isPlaying) Destroy(coin);
            else DestroyImmediate(coin);
        }
    }
}
