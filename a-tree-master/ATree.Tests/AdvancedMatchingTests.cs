using Microsoft.VisualStudio.TestTools.UnitTesting;
using ATree;
using System.Collections.Generic;
using System.Linq;

namespace ATree.Tests
{
    [TestClass]
    public class AdvancedMatchingTests
    {
        private List<AttributeDefinition> _attributeDefinitions = null!;
        private AttributeTable _attributeTable = null!;
        private ATree<string> _atree = null!;

        [TestInitialize]
        public void Setup()
        {
            // Define 10 attributes for our scenarios
            _attributeDefinitions = new List<AttributeDefinition>();
            for (int i = 0; i < 10; i++)
            {
                _attributeDefinitions.Add(AttributeDefinition.Integer($"Attr{i}"));
            }
            
            _attributeTable = new AttributeTable(_attributeDefinitions);
            _atree = new ATree<string>(_attributeDefinitions);

            // Create 5 subscribers with specific, two-level rules
            // Subscriber 1: Simple AND
            var rule1 = new AndNode(
                new ValueNode(Predicate.Eq(_attributeTable, "Attr0", 10)),
                new ValueNode(Predicate.Eq(_attributeTable, "Attr1", 20))
            );
            _atree.AddRule("Subscriber1", rule1);

            // Subscriber 2: Another simple AND, shares one attribute with Sub1
            var rule2 = new AndNode(
                new ValueNode(Predicate.Eq(_attributeTable, "Attr0", 10)),
                new ValueNode(Predicate.Eq(_attributeTable, "Attr2", 30))
            );
            _atree.AddRule("Subscriber2", rule2);

            // Subscriber 3: Simple OR
            var rule3 = new OrNode(
                new ValueNode(Predicate.Eq(_attributeTable, "Attr3", 40)),
                new ValueNode(Predicate.Eq(_attributeTable, "Attr4", 50))
            );
            _atree.AddRule("Subscriber3", rule3);

            // Subscriber 4: Complex rule with AND and OR
            var rule4 = new AndNode(
                new ValueNode(Predicate.Eq(_attributeTable, "Attr5", 60)),
                new OrNode(
                    new ValueNode(Predicate.Eq(_attributeTable, "Attr6", 70)),
                    new ValueNode(Predicate.Eq(_attributeTable, "Attr7", 80))
                )
            );
            _atree.AddRule("Subscriber4", rule4);
            
            // Subscriber 5: Single predicate rule
            var rule5 = new ValueNode(Predicate.Eq(_attributeTable, "Attr8", 90));
            _atree.AddRule("Subscriber5", rule5);
        }

        private Event BuildEvent(params (string, int)[] attributes)
        {
            var eventBuilder = _atree.MakeEvent();
            foreach (var (name, value) in attributes)
            {
                eventBuilder.WithInteger(name, value);
            }
            return eventBuilder.Build();
        }

        [TestMethod]
        public void Test_Scenario_MatchSubscriber1_Exact()
        {
            var anEvent = BuildEvent(("Attr0", 10), ("Attr1", 20));
            var results = _atree.MatchEvent(anEvent);

            Assert.AreEqual(1, results.Count, "Expected exactly one match.");
            Assert.IsTrue(results.Contains("Subscriber1"), "Subscriber1 should have matched.");
        }

        [TestMethod]
        public void Test_Scenario_MatchSubscriber2_Exact()
        {
            var anEvent = BuildEvent(("Attr0", 10), ("Attr2", 30));
            var results = _atree.MatchEvent(anEvent);

            Assert.AreEqual(1, results.Count, "Expected exactly one match.");
            Assert.IsTrue(results.Contains("Subscriber2"), "Subscriber2 should have matched.");
        }
        
        [TestMethod]
        public void Test_Scenario_MatchSubscriber1And2_PartialOverlap()
        {
            // This event satisfies both Subscriber1 and Subscriber2
            var anEvent = BuildEvent(("Attr0", 10), ("Attr1", 20), ("Attr2", 30));
            var results = _atree.MatchEvent(anEvent);

            Assert.AreEqual(2, results.Count, "Expected two matches.");
            Assert.IsTrue(results.Contains("Subscriber1"), "Subscriber1 should have matched.");
            Assert.IsTrue(results.Contains("Subscriber2"), "Subscriber2 should have matched.");
        }

        [TestMethod]
        public void Test_Scenario_MatchSubscriber3_OrCondition_First()
        {
            var anEvent = BuildEvent(("Attr3", 40));
            var results = _atree.MatchEvent(anEvent);

            Assert.AreEqual(1, results.Count, "Expected exactly one match for the first OR condition.");
            Assert.IsTrue(results.Contains("Subscriber3"), "Subscriber3 should have matched.");
        }

        [TestMethod]
        public void Test_Scenario_MatchSubscriber3_OrCondition_Second()
        {
            var anEvent = BuildEvent(("Attr4", 50));
            var results = _atree.MatchEvent(anEvent);

            Assert.AreEqual(1, results.Count, "Expected exactly one match for the second OR condition.");
            Assert.IsTrue(results.Contains("Subscriber3"), "Subscriber3 should have matched.");
        }

        [TestMethod]
        public void Test_Scenario_MatchSubscriber4_ComplexRule_FirstOrBranch()
        {
            var anEvent = BuildEvent(("Attr5", 60), ("Attr6", 70));
            var results = _atree.MatchEvent(anEvent);

            Assert.AreEqual(1, results.Count, "Expected one match for the complex rule.");
            Assert.IsTrue(results.Contains("Subscriber4"), "Subscriber4 should have matched.");
        }

        [TestMethod]
        public void Test_Scenario_MatchSubscriber4_ComplexRule_SecondOrBranch()
        {
            var anEvent = BuildEvent(("Attr5", 60), ("Attr7", 80));
            var results = _atree.MatchEvent(anEvent);

            Assert.AreEqual(1, results.Count, "Expected one match for the complex rule.");
            Assert.IsTrue(results.Contains("Subscriber4"), "Subscriber4 should have matched.");
        }
        
        [TestMethod]
        public void Test_Scenario_MatchSubscriber5_SinglePredicate()
        {
            var anEvent = BuildEvent(("Attr8", 90));
            var results = _atree.MatchEvent(anEvent);

            Assert.AreEqual(1, results.Count, "Expected one match for the single predicate rule.");
            Assert.IsTrue(results.Contains("Subscriber5"), "Subscriber5 should have matched.");
        }

        [TestMethod]
        public void Test_Scenario_NoMatch_WrongValue()
        {
            var anEvent = BuildEvent(("Attr0", 999));
            var results = _atree.MatchEvent(anEvent);

            Assert.AreEqual(0, results.Count, "Expected no matches when attribute value is wrong.");
        }

        [TestMethod]
        public void Test_Scenario_NoMatch_PartialAnd()
        {
            // This event only satisfies half of Subscriber1's AND condition
            var anEvent = BuildEvent(("Attr0", 10));
            var results = _atree.MatchEvent(anEvent);

            Assert.AreEqual(0, results.Count, "Expected no matches for a partial AND condition.");
        }
        
        [TestMethod]
        public void Test_Scenario_MultipleDisjointMatches()
        {
            // This event satisfies Subscriber3 and Subscriber5, whose rules are completely separate
            var anEvent = BuildEvent(("Attr4", 50), ("Attr8", 90));
            var results = _atree.MatchEvent(anEvent);

            Assert.AreEqual(2, results.Count, "Expected two disjoint matches.");
            Assert.IsTrue(results.Contains("Subscriber3"), "Subscriber3 should have matched.");
            Assert.IsTrue(results.Contains("Subscriber5"), "Subscriber5 should have matched.");
        }
    }
}
