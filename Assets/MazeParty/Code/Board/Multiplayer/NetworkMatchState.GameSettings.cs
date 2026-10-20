using System;
using MazeParty.Gameplay.Minigames;
using Unity.Collections;
using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private readonly NetworkVariable<FixedString512Bytes> _gameSettingsPayload =
            new NetworkVariable<FixedString512Bytes>(new FixedString512Bytes(MatchGameSettings.Default.Encode()));
        public MatchGameSettings GameSettings { get; private set; } = MatchGameSettings.Default;
        public int TotalTurns => GameSettings.Turns;
        private void InitializeGameSettings()
        {
            _gameSettingsPayload.OnValueChanged += OnGameSettingsChanged;
            if (IsServer)
            {
                var settings = OnlineSessionController.Instance?.CurrentSession?.GameSettings ?? MatchGameSettings.Default;
                _gameSettingsPayload.Value = new FixedString512Bytes(settings.Encode());
            }
            OnGameSettingsChanged(default, _gameSettingsPayload.Value);
        }
        private void OnGameSettingsChanged(FixedString512Bytes previous, FixedString512Bytes next)
        {
            if (!MatchGameSettings.TryDecode(next.ToString(), out var settings))
                throw new InvalidOperationException("Invalid authoritative match settings.");
            GameSettings = settings;
            MinigameRuntimeSettings.Bind(this, settings);
        }
        private void ReleaseGameSettings()
        {
            _gameSettingsPayload.OnValueChanged -= OnGameSettingsChanged;
            MinigameRuntimeSettings.Release(this);
        }
    }
}
