using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.CoinCollect;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class CoinCollectNetworkView : MonoBehaviour
    {
        [SerializeField] private NetworkCoinCollectState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private Transform playerRoot, itemRoot;
        [SerializeField] private GameObject coinPrefab, moneyPrefab, bombPrefab, shadowPrefab;
        [SerializeField] private BouncingBallsHudBindings scoreHud;
        private readonly PlayerAvatarVisual[] _players = new PlayerAvatarVisual[4];
        private readonly List<ItemView> _items = new List<ItemView>();
        private GameplayCameraDirector _cameraDirector;
        private NetworkCoinCollectState _subscribedState;
        private bool _practice;
        private static readonly Color[] Colors = { new Color(.16f,.48f,.95f), new Color(.92f,.2f,.16f),
            new Color(.18f,.78f,.32f), new Color(.7f,.26f,.9f) };
        public static Vector3 SharedCameraPosition => MinigameCameraFraming.CalculateSharedPosition(CoinCollectRules.ArenaCenterX, 26f);
        public static Quaternion SharedCameraRotation => MinigameCameraFraming.SharedRotation;
        public const float SharedCameraSize = 12f;
        public GameObject ArenaPresentation => arenaPresentation;
        public CinemachineCamera SharedCamera => sharedCamera;
        public BouncingBallsHudBindings ScoreHud => scoreHud;
        public bool HasRequiredReferences => state != null && sharedCamera != null && arenaPresentation != null &&
            playerRoot != null && itemRoot != null && coinPrefab != null && moneyPrefab != null && bombPrefab != null &&
            shadowPrefab != null && scoreHud != null && scoreHud.HasRequiredReferences;
        public void Configure(NetworkCoinCollectState authority, CinemachineCamera camera, GameObject arena,
            Transform players, Transform items, GameObject coin, GameObject money, GameObject bomb,
            GameObject shadow, BouncingBallsHudBindings hud)
        {
            state = authority; sharedCamera = camera; arenaPresentation = arena; playerRoot = players; itemRoot = items;
            coinPrefab = coin; moneyPrefab = money; bombPrefab = bomb; shadowPrefab = shadow; scoreHud = hud;
        }
        private void Awake() { SetVisible(false); }
        private void OnEnable() { EnsurePushSubscription(); }
        private void OnDisable()
        {
            UnsubscribeFromPush();
            SetVisible(false);
            if (_cameraDirector != null) _cameraDirector.ClearMinigameCamera(sharedCamera);
        }
        private void Update()
        {
            EnsurePushSubscription();
            if (_practice) return;
            var match = NetworkMatchState.Instance;
            var visible = state != null && state.IsSpawned && match != null && match.CurrentMinigame == ScheduledMinigameId.CoinCollect &&
                (match.FlowState == BoardFlowState.MinigamePlaying || match.FlowState == BoardFlowState.MinigameResult);
            SetVisible(visible);
            if (!visible)
            {
                if (_cameraDirector != null) _cameraDirector.ClearMinigameCamera(sharedCamera);
                return;
            }
            _cameraDirector ??= FindAnyObjectByType<GameplayCameraDirector>();
            _cameraDirector?.SetMinigameCamera(sharedCamera);
            EnsurePlayers();
            for (var slot = 0; slot < 4; slot++) RenderPlayer(slot, state.GetPlayer(slot), match.GetAvatarForSlot(slot));
            RenderItems(state.ItemCount, state.Elapsed, state.GetItem);
        }
        public void RenderPractice(CoinCollectMatchState simulation)
        {
            _practice = true; SetVisible(true); EnsurePlayers();
            for (var slot = 0; slot < 4; slot++) RenderPlayer(slot, simulation.GetPlayer(slot), null);
            RenderItems(simulation.ItemCount, simulation.Elapsed, simulation.GetItem);
        }
        public void PlayPushAnimation(int slot)
        {
            if (!isActiveAndEnabled || !CoinCollectRules.IsValidSlot(slot)) return;
            EnsurePlayers();
            _players[slot].TriggerPush();
        }
        private void EnsurePushSubscription()
        {
            if (_subscribedState == state) return;
            UnsubscribeFromPush();
            _subscribedState = state;
            if (_subscribedState != null) _subscribedState.PushPresentationRequested += PlayPushAnimation;
        }
        private void UnsubscribeFromPush()
        {
            if (_subscribedState != null) _subscribedState.PushPresentationRequested -= PlayPushAnimation;
            _subscribedState = null;
        }
        public Transform GetPlayerTransform(int slot) => slot >= 0 && slot < 4 && _players[slot] != null ? _players[slot].transform : null;
        private void SetVisible(bool value)
        {
            if (arenaPresentation != null) arenaPresentation.SetActive(value);
            if (playerRoot != null) playerRoot.gameObject.SetActive(value);
            if (itemRoot != null) itemRoot.gameObject.SetActive(value);
            if (scoreHud != null) scoreHud.RootCanvas.gameObject.SetActive(value);
        }
        private void EnsurePlayers()
        {
            for (var slot = 0; slot < 4; slot++)
            {
                if (_players[slot] != null) continue;
                var root = new GameObject("Coin Collect Player " + (slot + 1));
                root.transform.SetParent(playerRoot, false);
                var visual = root.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt(); visual.SetOwnerFirstPerson(false); visual.SetNameplateVisible(true);
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                _players[slot] = visual;
            }
        }
        private void RenderPlayer(int slot, CoinCollectPlayerSnapshot p, NetworkPlayerAvatar avatar)
        {
            var visual = _players[slot];
            visual.transform.position = new Vector3(p.Position.x, 1.05f, p.Position.y);
            if (p.Facing.sqrMagnitude > .001f)
                visual.transform.rotation = Quaternion.LookRotation(new Vector3(p.Facing.x, 0f, p.Facing.y), Vector3.up);
            var name = avatar != null ? avatar.DisplayName : GameText.F("Player {0}", slot + 1);
            var color = avatar != null ? avatar.Appearance.BodyColor : Colors[slot];
            visual.SetDisplayName(name); visual.SetBodyColor(color);
            if (avatar != null)
            {
                var appearance = avatar.Appearance;
                visual.ApplyAppearance(appearance.EyeId, appearance.MouthId, appearance.HatId, appearance.ExpressionId);
            }
            visual.SetKnockedDown(p.StunRemaining > 0d);
            scoreHud.SetPlayer(slot, name, p.Score, color);
        }
        private void RenderItems(int count, double elapsed, System.Func<int, CoinCollectItemSnapshot> get)
        {
            while (_items.Count < count) _items.Add(new ItemView());
            for (var i = 0; i < _items.Count; i++)
            {
                var view = _items[i];
                if (i >= count || !get(i).Active)
                {
                    if (view.Model != null) view.Model.SetActive(false);
                    if (view.Shadow != null) view.Shadow.SetActive(false);
                    continue;
                }
                var item = get(i);
                if (view.Model == null || view.Kind != item.Kind)
                {
                    if (view.Model != null) Destroy(view.Model);
                    var prefab = item.Kind == CoinCollectItemKind.Coin ? coinPrefab :
                        item.Kind == CoinCollectItemKind.Money ? moneyPrefab : bombPrefab;
                    view.Model = Instantiate(prefab, itemRoot); view.Kind = item.Kind;
                    view.Model.name = item.Kind + " " + i;
                }
                if (view.Shadow == null) view.Shadow = Instantiate(shadowPrefab, itemRoot);
                // Two visible/hidden cycles over the same simulation second as the pickup lock.
                // The landing shadow remains visible to keep the item position readable.
                var blinkPhase = (int)System.Math.Floor(System.Math.Max(0d, elapsed - item.BornAt) /
                    (CoinCollectRules.BombDropPickupLockSeconds / 4d));
                view.Model.SetActive(!item.IsPickupLockedAt(elapsed) || (blinkPhase & 1) == 0);
                view.Shadow.SetActive(true);
                view.Model.transform.position = new Vector3(item.Position.x, item.HeightAt(elapsed), item.Position.y);
                view.Model.transform.rotation = Quaternion.Euler(0f, (float)elapsed * 75f + i * 39f, 0f);
                view.Shadow.transform.position = new Vector3(item.Position.x, .015f, item.Position.y);
            }
        }
        private sealed class ItemView { public GameObject Model, Shadow; public CoinCollectItemKind Kind; }
    }
}
