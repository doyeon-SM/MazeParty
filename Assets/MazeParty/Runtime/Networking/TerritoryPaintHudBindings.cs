using System;
using MazeParty.Gameplay.Minigames.TerritoryPaint;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Prefab-owned HUD contract. Only time plus the four player names and
    /// normalized area scores are exposed during this minigame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerritoryPaintHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private MinigameTimerDial timerDial;
        [SerializeField] private Text timerText;
        [SerializeField] private Text[] playerRows =
            new Text[TerritoryPaintRules.PlayerCount];

        private Color[] _defaultRowColors;

        public Canvas RootCanvas => rootCanvas;
        public MinigameTimerDial TimerDial => timerDial;
        public Text TimerText => timerText;
        public Text[] PlayerRows => playerRows;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            timerDial != null &&
            timerDial.HasRequiredReferences &&
            timerText != null &&
            playerRows != null &&
            playerRows.Length == TerritoryPaintRules.PlayerCount &&
            Array.TrueForAll(playerRows, row => row != null);

        public void Configure(
            Canvas canvas,
            Text timer,
            Text[] rows)
        {
            rootCanvas = canvas;
            timerText = timer;
            playerRows = rows;
            _defaultRowColors = null;
            CaptureDefaults();
        }

        public void ConfigureTimerDial(MinigameTimerDial timer)
        {
            timerDial = timer;
        }


        public Color GetDefaultPlayerRowColor(int slot)
        {
            if (slot < 0 ||
                playerRows == null ||
                slot >= playerRows.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            CaptureDefaults();
            return _defaultRowColors[slot];
        }

        private void Awake()
        {
            CaptureDefaults();
        }

        private void CaptureDefaults()
        {
            if (_defaultRowColors != null &&
                playerRows != null &&
                _defaultRowColors.Length == playerRows.Length)
            {
                return;
            }

            var count = playerRows != null ? playerRows.Length : 0;
            _defaultRowColors = new Color[count];
            for (var slot = 0; slot < count; slot++)
            {
                if (playerRows[slot] != null)
                {
                    _defaultRowColors[slot] =
                        playerRows[slot].color;
                }
            }
        }
    }
}
