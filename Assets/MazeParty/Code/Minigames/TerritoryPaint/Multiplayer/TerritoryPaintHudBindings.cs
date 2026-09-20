using System;
using MazeParty.Gameplay.Minigames.TerritoryPaint;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Prefab-owned HUD contract for player names and area scores.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerritoryPaintHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Text[] playerRows =
            new Text[TerritoryPaintRules.PlayerCount];

        private Color[] _defaultRowColors;

        public Canvas RootCanvas => rootCanvas;
        public Text[] PlayerRows => playerRows;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            playerRows != null &&
            playerRows.Length == TerritoryPaintRules.PlayerCount &&
            Array.TrueForAll(playerRows, row => row != null);

        public void Configure(
            Canvas canvas,
            Text[] rows)
        {
            rootCanvas = canvas;
            playerRows = rows;
            _defaultRowColors = null;
            CaptureDefaults();
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
