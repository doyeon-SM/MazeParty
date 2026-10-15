using System;
using System.Globalization;
using System.Text.RegularExpressions;
using MazeParty.Gameplay;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Wire format for the replicated landing-effect line. The host sends the
    /// English source format and its arguments instead of finished text so each
    /// client formats the line in its own language.
    /// Encoded fields are separated by U+001F: slot, hasTile, x, y, typed landing
    /// effect marker, detail format, then the detail arguments. Legacy payloads
    /// without the marker still format correctly and report a None effect type.
    /// </summary>
    public static class LandingEffectMessage
    {
        public const char Separator = '\u001F';
        private const int LegacyHeaderFieldCount = 5;
        private const int TypedHeaderFieldCount = 6;
        private const char EffectTypePrefix = 'T';

        public static string Encode(
            int slot,
            bool hasTile,
            int x,
            int y,
            string detailFormat,
            params string[] args)
        {
            return Encode(
                slot,
                hasTile,
                x,
                y,
                BoardLandingEffectType.None,
                detailFormat,
                args);
        }

        public static string Encode(
            int slot,
            bool hasTile,
            int x,
            int y,
            BoardLandingEffectType effectType,
            string detailFormat,
            params string[] args)
        {
            var fields = new string[
                TypedHeaderFieldCount + (args != null ? args.Length : 0)];
            fields[0] = slot.ToString(CultureInfo.InvariantCulture);
            fields[1] = hasTile ? "1" : "0";
            fields[2] = x.ToString(CultureInfo.InvariantCulture);
            fields[3] = y.ToString(CultureInfo.InvariantCulture);
            fields[4] = EffectTypePrefix +
                        ((byte)effectType).ToString(
                            CultureInfo.InvariantCulture);
            fields[5] = Sanitize(detailFormat);
            for (var index = 0; args != null && index < args.Length; index++)
            {
                fields[TypedHeaderFieldCount + index] = Sanitize(args[index]);
            }

            return string.Join(Separator.ToString(), fields);
        }

        /// <summary>
        /// Formats an encoded line in the current language. Plain text without
        /// the separator is returned unchanged.
        /// </summary>
        public static string Format(string encoded)
        {
            return FormatInternal(encoded, null);
        }

        public static string PlayerToken(int slot) => "@p" + slot;
        public static string PlayersToken(int mask) => "@m" + mask;
        public static string ResourceToken(BoardSpecialEventResource resource, int amount) =>
            (resource == BoardSpecialEventResource.Gold ? "@g" : "@k") +
            amount.ToString(CultureInfo.InvariantCulture);

        public static string FormatEventPopup(string encoded, Func<int, string> playerLabel)
        {
            if (playerLabel == null) throw new ArgumentNullException(nameof(playerLabel));
            return FormatInternal(encoded, playerLabel);
        }

        private static string FormatInternal(string encoded, Func<int, string> playerLabel)
        {
            if (string.IsNullOrEmpty(encoded) || encoded.IndexOf(Separator) < 0)
            {
                return encoded ?? string.Empty;
            }

            var fields = encoded.Split(Separator);
            if (fields.Length < LegacyHeaderFieldCount ||
                !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot))
            {
                return string.Empty;
            }

            var typed = TryReadEffectType(fields, out _);
            var detailIndex = typed ? 5 : 4;
            var argumentIndex = typed
                ? TypedHeaderFieldCount
                : LegacyHeaderFieldCount;
            var args = new object[fields.Length - argumentIndex];
            for (var index = 0; index < args.Length; index++)
            {
                // Resolve compact player/resource tokens locally, retaining item localization.
                args[index] = FormatArgument(fields[argumentIndex + index], playerLabel);
            }

            var detail = GameText.F(fields[detailIndex], args);
            if (playerLabel != null && slot >= 0 && slot < 4)
                return playerLabel(slot) + "\n" + detail;
            if (fields[1] == "1" &&
                int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
                int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            {
                return GameText.F("P{0} ({1},{2}): {3}", slot + 1, x, y, detail);
            }

            return GameText.F("P{0}: {1}", slot + 1, detail);
        }

        private static readonly Regex TokenPattern = new Regex(@"@(p|m|g|k)(\d+)");

        private static string FormatArgument(string value, Func<int, string> playerLabel)
        {
            return TokenPattern.Replace(GameText.T(value), match =>
            {
                if (!int.TryParse(match.Groups[2].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out var number))
                    return string.Empty;
                var kind = match.Groups[1].Value;
                if (kind == "p")
                    return number >= 0 && number < 4
                        ? playerLabel != null ? playerLabel(number) : "P" + (number + 1)
                        : string.Empty;
                if (kind == "m")
                {
                    var names = new System.Collections.Generic.List<string>();
                    for (var slot = 0; slot < 4; slot++)
                        if ((number & (1 << slot)) != 0)
                            names.Add(playerLabel != null ? playerLabel(slot) : "P" + (slot + 1));
                    return string.Join(" / ", names);
                }
                return playerLabel != null
                    ? "<color=#FFD11F><sprite name=\"" +
                      (kind == "g" ? "gold" : "key") + "\" tint=1> " +
                      number.ToString(CultureInfo.InvariantCulture) + "</color>"
                    : GameText.T(number.ToString(CultureInfo.InvariantCulture) +
                                 (kind == "g" ? " GOLD" : " KEY"));
            });
        }

        public static BoardLandingEffectType GetEffectType(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) ||
                encoded.IndexOf(Separator) < 0)
            {
                return BoardLandingEffectType.None;
            }

            return TryReadEffectType(encoded.Split(Separator), out var effectType)
                ? effectType
                : BoardLandingEffectType.None;
        }

        public static int GetSlot(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) ||
                encoded.IndexOf(Separator) < 0)
                return -1;

            var separator = encoded.IndexOf(Separator);
            return int.TryParse(
                encoded.Substring(0, separator),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var slot) && slot >= 0 && slot < 4
                    ? slot
                    : -1;
        }

        private static bool TryReadEffectType(
            string[] fields,
            out BoardLandingEffectType effectType)
        {
            effectType = BoardLandingEffectType.None;
            if (fields == null || fields.Length < TypedHeaderFieldCount)
            {
                return false;
            }

            var marker = fields[4];
            if (string.IsNullOrEmpty(marker) ||
                marker[0] != EffectTypePrefix ||
                !byte.TryParse(
                    marker.Substring(1),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var rawType))
            {
                return false;
            }

            effectType = (BoardLandingEffectType)rawType;
            return true;
        }

        private static string Sanitize(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace(Separator, ' ');
        }
    }
}
