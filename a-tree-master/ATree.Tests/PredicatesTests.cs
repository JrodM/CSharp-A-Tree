using Microsoft.VisualStudio.TestTools.UnitTesting;
using ATree;
using System.Collections.Generic;

namespace ATree.Tests
{
    [TestClass]
    public class PredicatesTests
    {
        [TestMethod]
        public void ReturnTrueOnBooleanVariableThatIsTrue()
        {
            var definitions = new List<AttributeDefinition>
            {
                new AttributeDefinition("private", AttributeKind.Boolean)
            };
            var attributes = new AttributeTable(definitions);
            var strings = new StringTable();
            var evt = new EventBuilder(attributes, strings).WithBoolean("private", true).Build();
            var predicate = Predicate.Variable(attributes, "private");
            Assert.AreEqual(true, predicate.Evaluate(evt));
        }

        [TestMethod]
        public void ReturnFalseOnBooleanVariableThatIsFalse()
        {
            var definitions = new List<AttributeDefinition>
            {
                new AttributeDefinition("private", AttributeKind.Boolean)
            };
            var attributes = new AttributeTable(definitions);
            var strings = new StringTable();
            var evt = new EventBuilder(attributes, strings).WithBoolean("private", false).Build();
            var predicate = Predicate.Variable(attributes, "private");
            Assert.AreEqual(false, predicate.Evaluate(evt));
        }

        [TestMethod]
        public void ReturnFalseOnNegatedBooleanVariableThatIsTrue()
        {
            var definitions = new List<AttributeDefinition>
            {
                new AttributeDefinition("private", AttributeKind.Boolean)
            };
            var attributes = new AttributeTable(definitions);
            var strings = new StringTable();
            var evt = new EventBuilder(attributes, strings).WithBoolean("private", true).Build();
            var predicate = Predicate.NegatedVariable(attributes, "private");
            Assert.AreEqual(false, predicate.Evaluate(evt));
        }

        [TestMethod]
        public void ReturnTrueOnNegatedBooleanVariableThatIsFalse()
        {
            var definitions = new List<AttributeDefinition>
            {
                new AttributeDefinition("private", AttributeKind.Boolean)
            };
            var attributes = new AttributeTable(definitions);
            var strings = new StringTable();
            var evt = new EventBuilder(attributes, strings).WithBoolean("private", false).Build();
            var predicate = Predicate.NegatedVariable(attributes, "private");
            Assert.AreEqual(true, predicate.Evaluate(evt));
        }
    }
}
