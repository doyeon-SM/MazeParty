using System;
using MazeParty.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class BoardEventPanelView : MonoBehaviour
    {
        [SerializeField] private TMP_Text message;
        [SerializeField] private TMP_Text[] captions = new TMP_Text[3];
        [SerializeField] private TMP_Text[] values = new TMP_Text[3];
        [SerializeField] private Image[] borders = new Image[12];
        public bool HasRequiredReferences => message != null &&
            captions != null && captions.Length == 3 && Array.TrueForAll(captions, x => x != null) &&
            values != null && values.Length == 3 && Array.TrueForAll(values, x => x != null) &&
            borders != null && borders.Length == 12 && Array.TrueForAll(borders, x => x != null);
        public void Configure(TMP_Text title, TMP_Text[] labels, TMP_Text[] fields, Image[] frames)
        { message = title; captions = labels; values = fields; borders = frames; }

        public void Present(string encoded, Func<int, string> playerLabel)
        {
            if (!HasRequiredReferences || string.IsNullOrEmpty(encoded)) return;
            var fields = encoded.Split(LandingEffectMessage.Separator);
            if (fields.Length < 6) return;
            captions[0].text = GameText.T("TARGET");
            captions[1].text = GameText.T("RESOURCE");
            captions[2].text = GameText.T("METHOD");
            for (var index = 0; index < values.Length; index++) values[index].text = "?";
            var format = fields[5];
            Func<int, string> arg = index => fields.Length > index + 6
                ? LandingEffectMessage.FormatArgument(fields[index + 6], playerLabel) : "?";
            var result = format.StartsWith("EVENT RESULT:", StringComparison.Ordinal);
            if (result)
            {
                var transfer = format.Contains("GIVES") || format.Contains("STEALS");
                values[0].text = transfer ? arg(0) + " → " + arg(1) : arg(0);
                values[1].text = arg(transfer ? 2 : 1);
                values[2].text = GameText.T(format.Contains("GIVES") ? "GIVE" :
                    format.Contains("STEALS") ? "STEAL" :
                    format.Contains("RECEIVES") ? "RECEIVE" : "LOSE");
                var actor = LandingEffectMessage.GetSlot(encoded);
                message.text = (actor >= 0 ? playerLabel(actor) + "\n" : string.Empty) +
                    GameText.T("EVENT RESULT");
                return;
            }
            values[0].text = arg(0);
            if (format.Contains("RESOURCE >") || format.Contains("ACTION >")) values[1].text = arg(1);
            if (format.Contains("ACTION >")) values[2].text = arg(2);
            var slot = LandingEffectMessage.GetSlot(encoded);
            message.text = (slot >= 0 ? playerLabel(slot) + "\n" : string.Empty) + GameText.T("SPECIAL EVENT");
        }
    }
}
