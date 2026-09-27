using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class ServerEventTraceTests
    {
        [SetUp]
        public void SetUp()
        {
            ServerEventTrace.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ServerEventTrace.Clear();
        }

        [Test]
        public void Overflow_RetainsNewestEventsInChronologicalOrder()
        {
            const int overflow = 9;
            for (var index = 0;
                 index < ServerEventTrace.Capacity + overflow;
                 index++)
            {
                ServerEventTrace.Record(
                    ServerEventCode.MatchTransition,
                    turn: index,
                    value0: index);
            }

            var snapshot = ServerEventTrace.Snapshot();

            Assert.That(snapshot, Has.Length.EqualTo(ServerEventTrace.Capacity));
            Assert.That(snapshot[0].Value0, Is.EqualTo(overflow));
            Assert.That(
                snapshot[snapshot.Length - 1].Value0,
                Is.EqualTo(ServerEventTrace.Capacity + overflow - 1));
            Assert.That(
                snapshot.Select(entry => entry.Sequence),
                Is.Ordered.Ascending);
        }

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
