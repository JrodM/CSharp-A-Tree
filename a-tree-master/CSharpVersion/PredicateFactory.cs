using System;
using System.Collections.Generic;
using System.Linq;

namespace ATree
{
    public static class PredicateFactory
    {
        public static Node Eq(AttributeTable attributes, string name, string value, StringTable strings)
        {
            return new ValueNode(Predicate.Eq(attributes, name, value, strings));
        }

        public static Node Eq(AttributeTable attributes, string name, long value)
        {
            return new ValueNode(Predicate.Eq(attributes, name, value));
        }

        public static Node Eq(AttributeTable attributes, string name, bool value)
        {
            return new ValueNode(Predicate.Eq(attributes, name, value));
        }

        public static Node GreaterThan(AttributeTable attributes, string name, decimal value)
        {
            return new ValueNode(Predicate.GreaterThan(attributes, name, value));
        }

        public static Node GreaterThan(AttributeTable attributes, string name, long value)
        {
            return new ValueNode(Predicate.GreaterThan(attributes, name, value));
        }

        public static Node LessThan(AttributeTable attributes, string name, long value)
        {
            return new ValueNode(Predicate.LessThan(attributes, name, value));
        }

        public static Node LessThan(AttributeTable attributes, string name, decimal value)
        {
            return new ValueNode(Predicate.LessThan(attributes, name, value));
        }

        public static Node In(AttributeTable attributes, string name, IEnumerable<string> values, StringTable strings)
        {
            return new ValueNode(Predicate.In(attributes, name, values, strings));
        }

        public static Node In(AttributeTable attributes, string name, IEnumerable<long> values)
        {
            return new ValueNode(Predicate.In(attributes, name, values));
        }

        public static Node Variable(AttributeTable attributes, string name)
        {
            return new ValueNode(Predicate.Variable(attributes, name));
        }

        public static Node NegatedVariable(AttributeTable attributes, string name)
        {
            return new ValueNode(Predicate.NegatedVariable(attributes, name));
        }

        public static Node And(Node left, Node right)
        {
            return new AndNode(left, right);
        }

        public static Node Or(Node left, Node right)
        {
            return new OrNode(left, right);
        }

        public static Node Not(Node node)
        {
            return new NotNode(node);
        }

        public static Node And(params Node[] nodes)
        {
            if (nodes == null || nodes.Length == 0)
                throw new ArgumentException("At least one node is required", nameof(nodes));

            Node result = nodes[0];
            for (int i = 1; i < nodes.Length; i++)
            {
                result = new AndNode(result, nodes[i]);
            }
            return result;
        }

        public static Node Or(params Node[] nodes)
        {
            if (nodes == null || nodes.Length == 0)
                throw new ArgumentException("At least one node is required", nameof(nodes));

            Node result = nodes[0];
            for (int i = 1; i < nodes.Length; i++)
            {
                result = new OrNode(result, nodes[i]);
            }
            return result;
        }
    }
}
