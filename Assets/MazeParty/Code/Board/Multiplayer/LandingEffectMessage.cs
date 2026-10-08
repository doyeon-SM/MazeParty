using System;
using System.Globalization;
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
                // Arguments are numbers or English data names (items).
                args[index] = GameText.T(fields[argumentIndex + index]);
            }

            var detail = GameText.F(fields[detailIndex], args);
            if (fields[1] == "1" &&
                int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
                int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            {
                return GameText.F("P{0} ({1},{2}): {3}", slot + 1, x, y, detail);
            }

            return GameText.F("P{0}: {1}", slot + 1, detail);
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
