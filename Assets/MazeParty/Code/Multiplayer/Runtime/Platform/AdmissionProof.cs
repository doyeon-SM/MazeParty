using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MazeParty.Multiplayer
{
    // The public key is registered through the player's authenticated UGS member
    // properties. A self-signature alone is NOT an authenticated player identity.
    // Hosts must also match the verified key to the claimed UGS member's key.
    public sealed class AdmissionProof : IDisposable
    {
        public const string PlayerKeyProperty = "mpAdmissionKey";
        public const int MaximumPayloadBytes = 1024;
        public const int MaximumAgeSeconds = 60;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly RSACryptoServiceProvider _key = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false };
        public string PublicKey { get; }

        public AdmissionProof()
        {
            PublicKey = Convert.ToBase64String(_key.ExportParameters(false).Modulus);
            if (!IsPublicKey(PublicKey)) throw new CryptographicException("RSA-2048 is required for connection proofs.");
        }

        public byte[] Create(string channel, string environment, string version, string target, string playerId, long now)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(0x3341504d); // MPA3: UGS member-bound key; never accept old server-grant payloads.
                WriteText(writer, channel, 16);
                WriteText(writer, environment, 64);
                WriteText(writer, version, 64);
                WriteText(writer, target, 100);
                WriteText(writer, playerId, 128);
                writer.Write(now);
                var nonce = new byte[16];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
                writer.Write(nonce);
                writer.Write(_key.ExportParameters(false).Modulus);
                writer.Flush();
                writer.Write(_key.SignData(stream.ToArray(), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                writer.Flush();
                if (stream.Length > MaximumPayloadBytes) throw new InvalidOperationException("Admission payload exceeds its limit.");
                return stream.ToArray();
            }
        }

        public static bool TryVerify(byte[] payload, string channel, string environment, string version,
            string code, string sessionId, long now, out VerifiedAdmission admission)
        {
            admission = null;
            if (payload == null || payload.Length < 550 || payload.Length > MaximumPayloadBytes) return false;
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadInt32() != 0x3341504d || ReadText(reader, 16) != channel ||
                        ReadText(reader, 64) != environment || ReadText(reader, 64) != version) return false;
                    var target = ReadText(reader, 100);
                    if ((string.IsNullOrEmpty(code) || target != "code:" + code) &&
                        (string.IsNullOrEmpty(sessionId) || target != "id:" + sessionId)) return false;
                    var player = ReadText(reader, 128);
                    if (string.IsNullOrWhiteSpace(player)) return false;
                    var issuedAt = reader.ReadInt64();
                    if (issuedAt > now + 10 || issuedAt < now - MaximumAgeSeconds) return false;
                    var nonce = ReadExact(reader, 16);
                    var modulus = ReadExact(reader, 256);
                    var signedLength = (int)stream.Position;
                    var signature = ReadExact(reader, 256);
                    if (stream.Position != stream.Length) return false;
                    using (var key = RSA.Create())
                    {
                        key.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = new byte[] { 1, 0, 1 } });
                        var data = new byte[signedLength];
                        Buffer.BlockCopy(payload, 0, data, 0, data.Length);
                        if (!key.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return false;
                    }
                    admission = new VerifiedAdmission(player, Convert.ToBase64String(nonce), Convert.ToBase64String(modulus));
                    return true;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException ||
                exception is FormatException || exception is CryptographicException || exception is OverflowException)
            { return false; }
        }

        public static bool IsPublicKey(string value)
        {
            try { return value != null && value.Length == 344 && Convert.FromBase64String(value).Length == 256; }
            catch (FormatException) { return false; }
        }

        private static void WriteText(BinaryWriter writer, string value, int max)
        {
            var bytes = Utf8.GetBytes(value ?? "");
            if (bytes.Length > max) throw new ArgumentException("Admission field exceeds its limit.");
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
        private static string ReadText(BinaryReader reader, int max)
        {
            var length = reader.ReadUInt16();
            if (length > max) throw new InvalidDataException();
            return Utf8.GetString(ReadExact(reader, length));
        }
        private static byte[] ReadExact(BinaryReader reader, int length)
        {
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }
        public void Dispose() => _key.Dispose();
    }

    public sealed class VerifiedAdmission
    {
        public readonly string PlayerId;
        public readonly string Nonce;
        public readonly string PublicKey;
        public VerifiedAdmission(string playerId, string nonce, string publicKey)
        { PlayerId = playerId; Nonce = nonce; PublicKey = publicKey; }

        // Call only with member data read from UGS, never another client payload.
        public bool MatchesMember(string memberId, string registeredPublicKey) =>
            PlayerId == memberId && AdmissionProof.IsPublicKey(registeredPublicKey) &&
            string.Equals(PublicKey, registeredPublicKey, StringComparison.Ordinal);
    }
}
