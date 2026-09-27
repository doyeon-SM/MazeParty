using System;
using System.Globalization;
using MazeParty.Gameplay;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Wire format for the replicated landing-effect line. The host sends the
    /// English source format and its arguments instead of finished text so each
    /// client formats the line in its own language.
    /// Encoded fields are separated by U+001F: slot, hasTile, x, y, detail format,
    /// then the detail arguments.
    /// </summary>
    public static class LandingEffectMessage
    {
        public const char Separator = '\u001F';
        private const int HeaderFieldCount = 5;

        public static string Encode(
            int slot,
            bool hasTile,
            int x,
            int y,
            string detailFormat,
            params string[] args)
        {
            var fields = new string[HeaderFieldCount + (args != null ? args.Length : 0)];
            fields[0] = slot.ToString(CultureInfo.InvariantCulture);
            fields[1] = hasTile ? "1" : "0";
            fields[2] = x.ToString(CultureInfo.InvariantCulture);
            fields[3] = y.ToString(CultureInfo.InvariantCulture);
            fields[4] = Sanitize(detailFormat);
            for (var index = 0; args != null && index < args.Length; index++)
            {
                fields[HeaderFieldCount + index] = Sanitize(args[index]);
            }

            return string.Join(Separator.ToString(), fields);
        }

        /// <summary>
        /// Formats an encoded line in the current language. Text without the
        /// separator (empty or legacy) is returned unchanged.
        /// </summary>
        public static string Format(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) || encoded.IndexOf(Separator) < 0)
            {
                return encoded ?? string.Empty;
            }

            var fields = encoded.Split(Separator);
            if (fields.Length < HeaderFieldCount ||
                !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot))
            {
                return string.Empty;
            }

            var args = new object[fields.Length - HeaderFieldCount];
            for (var index = 0; index < args.Length; index++)
            {
                // Arguments are numbers or English data names (items).
                args[index] = GameText.T(fields[HeaderFieldCount + index]);
            }

            var detail = GameText.F(fields[4], args);
            if (fields[1] == "1" &&
                int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
                int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            {
                return GameText.F("P{0} ({1},{2}): {3}", slot + 1, x, y, detail);
            }

            return GameText.F("P{0}: {1}", slot + 1, detail);
        }

        private static string Sanitize(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace(Separator, ' ');
        }
    }
}
