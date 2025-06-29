using Microsoft.VisualStudio.TestTools.UnitTesting;
using ATree;
using System.Collections.Generic;

namespace ATree.Tests
{
    [TestClass]
    public class AstTests
    {
        private readonly StringTable strings = new StringTable();
        private AttributeTable DefineAttributes()
        {
            var definitions = new List<AttributeDefinition>
            {
                new AttributeDefinition("deals", AttributeKind.StringList),
                new AttributeDefinition("deal", AttributeKind.String),
                new AttributeDefinition("price", AttributeKind.Integer),
                new AttributeDefinition("exchange_id", AttributeKind.Integer),
                new AttributeDefinition("private", AttributeKind.Boolean),
                new AttributeDefinition("deal_ids", AttributeKind.StringList),
                new AttributeDefinition("ids", AttributeKind.IntegerList),
                new AttributeDefinition("segment_ids", AttributeKind.IntegerList),
                new AttributeDefinition("continent", AttributeKind.String),
                new AttributeDefinition("country", AttributeKind.String),
                new AttributeDefinition("city", AttributeKind.String),
            };
            return new AttributeTable(definitions);
        }

        [TestMethod]
        public void CanOptimizeANegatedOrExpression()
        {
            var attributes = DefineAttributes();
            var aPredicate = new Predicate(attributes, "private", new PredicateKind.Variable());
            var expression = new NotNode(new OrNode(
                new ValueNode(aPredicate),
                new ValueNode(aPredicate.Negate())
            ));

            var expected = OptimizedNode.And(
                OptimizedNode.Value(aPredicate.Negate()),
                OptimizedNode.Value(aPredicate)
            );

            Assert.AreEqual(expected, expression.Optimize(strings, attributes));
        }

        [TestMethod]
        public void CanOptimizeANegatedAndExpression()
        {
            var attributes = DefineAttributes();
            var aPredicate = new Predicate(attributes, "private", new PredicateKind.NegatedVariable());
            var expression = new NotNode(new AndNode(
                new ValueNode(aPredicate),
                new ValueNode(aPredicate.Negate())
            ));

            var expected = OptimizedNode.Or(
                OptimizedNode.Value(aPredicate.Negate()),
                OptimizedNode.Value(aPredicate)
            );

            Assert.AreEqual(expected, expression.Optimize(strings, attributes));
        }

        [TestMethod]
        public void CanOptimizeANegatedExpression()
        {
            var attributes = DefineAttributes();
            var aPredicate = new Predicate(attributes, "private", new PredicateKind.Variable());
            var expression = new NotNode(new ValueNode(aPredicate));

            var expected = OptimizedNode.Value(aPredicate.Negate());

            Assert.AreEqual(expected, expression.Optimize(strings, attributes));
        }

        [TestMethod]
        public void CanOptimizeANegatedNegatedExpression()
        {
            var attributes = DefineAttributes();
            var aPredicate = new Predicate(attributes, "private", new PredicateKind.Variable());
            var expression = new NotNode(new NotNode(new ValueNode(aPredicate)));

            var expected = OptimizedNode.Value(aPredicate);

            Assert.AreEqual(expected, expression.Optimize(strings, attributes));
        }

        [TestMethod]
        public void LeaveUnnegatedValueAsIs()
        {
            var attributes = DefineAttributes();
            var aPredicate = new Predicate(attributes, "private", new PredicateKind.Variable());
            var expression = new ValueNode(aPredicate);

            var expected = OptimizedNode.Value(aPredicate);

            Assert.AreEqual(expected, expression.Optimize(strings, attributes));
        }

        [TestMethod]
        public void LeaveUnnegatedAndAsIs()
        {
            var attributes = DefineAttributes();
            var aPredicate = new Predicate(attributes, "private", new PredicateKind.Variable());
            var expression = new AndNode(new ValueNode(aPredicate), new ValueNode(aPredicate));

            var expected = OptimizedNode.And(
                OptimizedNode.Value(aPredicate),
                OptimizedNode.Value(aPredicate)
            );

            Assert.AreEqual(expected, expression.Optimize(strings, attributes));
        }

        [TestMethod]
        public void LeaveUnnegatedOrAsIs()
        {
            var attributes = DefineAttributes();
            var aPredicate = new Predicate(attributes, "private", new PredicateKind.Variable());
            var expression = new OrNode(new ValueNode(aPredicate), new ValueNode(aPredicate));

            var expected = OptimizedNode.Or(
                OptimizedNode.Value(aPredicate),
                OptimizedNode.Value(aPredicate)
            );

            Assert.AreEqual(expected, expression.Optimize(strings, attributes));
        }

        [TestMethod]
        public void CanOptimizeANegatedAndExpressionNotAtTheTopLevel()
        {
            var attributes = DefineAttributes();
            var aPredicate = new Predicate(attributes, "private", new PredicateKind.Variable());
            var expression = new AndNode(
                new NotNode(new AndNode(
                    new ValueNode(aPredicate),
                    new ValueNode(aPredicate)
                )),
                new ValueNode(aPredicate)
            );

            var expected = OptimizedNode.And(
                OptimizedNode.Or(
                    OptimizedNode.Value(aPredicate.Negate()),
                    OptimizedNode.Value(aPredicate.Negate())
                ),
                OptimizedNode.Value(aPredicate)
            );

            var actual = expression.Optimize(strings, attributes);

            Console.WriteLine("Expected Tree: " + expected.ToString());
            Console.WriteLine("Actual Tree:   " + actual.ToString());

            var has1 = actual.GetHashCode();
            var has2 = expected.GetHashCode();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void CanOptimizeANegatedOrExpressionNotAtTheTopLevel()
        {
            var attributes = DefineAttributes();
            var aPredicate = new Predicate(attributes, "private", new PredicateKind.Variable());
            var expression = new OrNode(
                new NotNode(new OrNode(
                    new ValueNode(aPredicate),
                    new ValueNode(aPredicate)
                )),
                new ValueNode(aPredicate)
            );

            var expected = OptimizedNode.Or(
                OptimizedNode.And(
                    OptimizedNode.Value(aPredicate.Negate()),
                    OptimizedNode.Value(aPredicate.Negate())
                ),
                OptimizedNode.Value(aPredicate)
            );

            Assert.AreEqual(expected, expression.Optimize(strings, attributes));
        }
        
        [TestMethod]
        public void CanRecursivelyApplyTheOptimizations()
        {
            var attributes = DefineAttributes();
            var aPredicate = new Predicate(attributes, "private", new PredicateKind.Variable());
            var expression = new NotNode(new AndNode(
                new NotNode(new OrNode(
                    new ValueNode(aPredicate),
                    new ValueNode(aPredicate)
                )),
                new AndNode(
                    new OrNode(new ValueNode(aPredicate), new ValueNode(aPredicate)),
                    new OrNode(new ValueNode(aPredicate), new ValueNode(aPredicate))
                )
            ));

            var expected = OptimizedNode.Or(
                OptimizedNode.Or(
                    OptimizedNode.Value(aPredicate),
                    OptimizedNode.Value(aPredicate)
                ),
                OptimizedNode.Or(
                    OptimizedNode.And(
                        OptimizedNode.Value(aPredicate.Negate()),
                        OptimizedNode.Value(aPredicate.Negate())
                    ),
                    OptimizedNode.And(
                        OptimizedNode.Value(aPredicate.Negate()),
                        OptimizedNode.Value(aPredicate.Negate())
                    )
                )
            );

            Assert.AreEqual(expected, expression.Optimize(strings, attributes));
        }
    }
}
