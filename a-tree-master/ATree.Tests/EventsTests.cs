using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace ATree.Tests
{
    [TestClass]
    public class EventsTests
    {
        [TestMethod]
        public void CanCreateAnAttributeTableWithNoAttributes()
        {
            var attributes = new AttributeTable(new List<AttributeDefinition>());
            Assert.AreEqual(0, attributes.Count);
        }

        [TestMethod]
        public void CanCreateAnAttributeTableWithSomeAttributes()
        {
            var definitions = new List<AttributeDefinition>
            {
                new AttributeDefinition("private", AttributeKind.Boolean),
                new AttributeDefinition("deals", AttributeKind.StringList),
                new AttributeDefinition("exchange_id", AttributeKind.Integer),
                new AttributeDefinition("bidfloor", AttributeKind.Float),
                new AttributeDefinition("country", AttributeKind.String),
                new AttributeDefinition("segment_ids", AttributeKind.IntegerList)
            };
            var attributes = new AttributeTable(definitions);
            Assert.AreEqual(6, attributes.Count);
        }

        [TestMethod]
        public void ReturnAnErrorOnDuplicateDefinitions()
        {
            var definitions = new List<AttributeDefinition>
            {
                new AttributeDefinition("private", AttributeKind.Boolean),
                new AttributeDefinition("country", AttributeKind.String),
                new AttributeDefinition("deals", AttributeKind.StringList),
                new AttributeDefinition("exchange_id", AttributeKind.Integer),
                new AttributeDefinition("bidfloor", AttributeKind.Float),
                new AttributeDefinition("country", AttributeKind.Integer),
                new AttributeDefinition("segment_ids", AttributeKind.IntegerList)
            };
            Assert.ThrowsException<EventException>(() => new AttributeTable(definitions));
        }

        [TestMethod]
        public void CanAddABooleanAttributeValue()
        {
            var attributes = new AttributeTable(new List<AttributeDefinition> { new AttributeDefinition("private", AttributeKind.Boolean) });
            var strings = new StringTable();
            var builder = new EventBuilder(attributes, strings);
            builder.WithBoolean("private", true);
            var evt = builder.Build();
            Assert.AreEqual(AttributeKind.Boolean, evt.GetValue(new AttributeId(0)).Kind);
        }
    }
}
