using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace ATree.Tests
{
    [TestClass]
    public class ATreeTests
    {
        [TestMethod]
        public void CanBuildAnATree()
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
            var atree = new ATree<ulong>(definitions);
            Assert.IsNotNull(atree);
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
            Assert.ThrowsException<EventException>(() => new ATree<ulong>(definitions));
        }

        [TestMethod]
        public void ReturnAnErrorOnInvalidBooleanExpression()
        {
            var definitions = new List<AttributeDefinition>
            {
                new AttributeDefinition("private", AttributeKind.Boolean),
                new AttributeDefinition("country", AttributeKind.String),
                new AttributeDefinition("deals", AttributeKind.StringList),
                new AttributeDefinition("exchange_id", AttributeKind.Integer),
                new AttributeDefinition("segment_ids", AttributeKind.IntegerList)
            };
            var atree = new ATree<ulong>(definitions);
            ValueNode node = null!; // Use null-forgiving operator to satisfy non-nullable reference
            Assert.ThrowsException<ArgumentNullException>(() => atree.AddRule(1, node));
        }

        [TestMethod]
        public void ReturnAnErrorOnEmptyBooleanExpression()
        {
            var definitions = new List<AttributeDefinition>
            {
                new AttributeDefinition("private", AttributeKind.Boolean),
                new AttributeDefinition("country", AttributeKind.String),
                new AttributeDefinition("deals", AttributeKind.StringList),
                new AttributeDefinition("exchange_id", AttributeKind.Integer),
                new AttributeDefinition("segment_ids", AttributeKind.IntegerList)
            };
            var atree = new ATree<ulong>(definitions);
            ValueNode node = null!;
            Assert.ThrowsException<ArgumentNullException>(() => atree.AddRule(1, node));
        }

        [TestMethod]
        public void CanInsertASimpleExpression()
        {
            var definitions = new List<AttributeDefinition>
            {
                new AttributeDefinition("private", AttributeKind.Boolean),
                new AttributeDefinition("country", AttributeKind.String),
                new AttributeDefinition("deals", AttributeKind.StringList),
                new AttributeDefinition("exchange_id", AttributeKind.Integer),
                new AttributeDefinition("segment_ids", AttributeKind.IntegerList)
            };
            var atree = new ATree<ulong>(definitions);
            var attributes = new AttributeTable(definitions);
            var predicate = Predicate.Eq(attributes, "exchange_id", 1);
            var node = new ValueNode(predicate);
            atree.AddRule(1, node);
            // If no exception, test passes
        }

        [TestMethod]
        public void CanInsertAnExpressionThatRefersToARNode()
        {
            var definitions = new List<AttributeDefinition>
            {
                new AttributeDefinition("private", AttributeKind.Boolean),
                new AttributeDefinition("exchange_id", AttributeKind.Integer),
                new AttributeDefinition("deal_ids", AttributeKind.StringList)
            };
            var atree = new ATree<ulong>(definitions);
            var attributes = new AttributeTable(definitions);

            var privatePredicate = Predicate.Eq(attributes, "private", true);
            var exchangePredicate = Predicate.Eq(attributes, "exchange_id", 1);
            var orNode = new OrNode(new ValueNode(privatePredicate), new ValueNode(exchangePredicate));
            atree.AddRule(1, orNode);

            var dealsPredicate = Predicate.ListOneOf(attributes, "deal_ids", new List<string> { "deal-1", "deal-2" }, atree.Strings);
            var anotherOrNode = new OrNode(orNode, new ValueNode(dealsPredicate));
            atree.AddRule(2, anotherOrNode);
        }
    }
}
