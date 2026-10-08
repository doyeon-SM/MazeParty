using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class ServerEventTraceTests
    {
        [Test]
        public void RecordShape_CannotCarryTextualSessionIdentity()
        {
            var textualFields = typeof(ServerEventRecord)
                .GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(string))
                .ToArray();

            Assert.That(textualFields, Is.Empty);
        }
    }
}
