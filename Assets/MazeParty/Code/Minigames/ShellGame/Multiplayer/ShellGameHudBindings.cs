using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class ShellGameHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Text secondsText;
        public Canvas RootCanvas => rootCanvas;
        public Text SecondsText => secondsText;
        public bool HasRequiredReferences => rootCanvas != null && secondsText != null && secondsText.transform.IsChildOf(transform);
        public void Configure(Canvas canvas, Text seconds) { rootCanvas=canvas; secondsText=seconds; }
        public void SetSeconds(double remaining, bool choosing)
        {
            secondsText.gameObject.SetActive(choosing);
            secondsText.text=choosing ? Math.Max(0,(int)Math.Ceiling(remaining)).ToString() : "";
        }
    }
}
