using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ATree
{
    public enum Operator
    {
        And,
        Or
    }

    public enum AttributeType
    {
        Boolean,
        String,
        StringList,
        Integer,
        IntegerList,
        Float
    }

    public abstract class Node
    {
        public abstract OptimizedNode Optimize(StringTable strings, AttributeTable attributes);
        internal abstract OptimizedNode OptimizeInternal(bool negate, StringTable strings, AttributeTable attributes);
    }

    public class AndNode : Node
    {
        public Node Left { get; }
        public Node Right { get; }

        public AndNode(Node left, Node right)
        {
            Left = left;
            Right = right;
        }

        public override OptimizedNode Optimize(StringTable strings, AttributeTable attributes)
        {
            return OptimizeInternal(false, strings, attributes);
        }

        internal override OptimizedNode OptimizeInternal(bool negate, StringTable strings, AttributeTable attributes)
        {
            if (negate) // De Morgan's Law: !(A && B) => !A || !B
            {
                var optLeft = Left.OptimizeInternal(true, strings, attributes);
                var optRight = Right.OptimizeInternal(true, strings, attributes);
                return OptimizedNode.Or(optLeft, optRight);
            }
            else
            {
                var optLeft = Left.OptimizeInternal(false, strings, attributes);
                var optRight = Right.OptimizeInternal(false, strings, attributes);
                return OptimizedNode.And(optLeft, optRight);
            }
        }
    }

    public class OrNode : Node
    {
        public Node Left { get; }
        public Node Right { get; }

        public OrNode(Node left, Node right)
        {
            Left = left;
            Right = right;
        }

        public override OptimizedNode Optimize(StringTable strings, AttributeTable attributes)
        {
            return OptimizeInternal(false, strings, attributes);
        }

        internal override OptimizedNode OptimizeInternal(bool negate, StringTable strings, AttributeTable attributes)
        {
            if (negate) // De Morgan's Law: !(A || B) => !A && !B
            {
                var optLeft = Left.OptimizeInternal(true, strings, attributes);
                var optRight = Right.OptimizeInternal(true, strings, attributes);
                return OptimizedNode.And(optLeft, optRight);
            }
            else
            {
                var optLeft = Left.OptimizeInternal(false, strings, attributes);
                var optRight = Right.OptimizeInternal(false, strings, attributes);
                return OptimizedNode.Or(optLeft, optRight);
            }
        }
    }

    public class NotNode : Node
    {
        public Node Value { get; }

        public NotNode(Node value)
        {
            Value = value;
        }

        public override OptimizedNode Optimize(StringTable strings, AttributeTable attributes)
        {
            // Start with negation flag set to true
            return Value.OptimizeInternal(true, strings, attributes);
        }
        
        internal override OptimizedNode OptimizeInternal(bool negate, StringTable strings, AttributeTable attributes)
        {
            // Flip the negation flag for the child node
            return Value.OptimizeInternal(!negate, strings, attributes);
        }
    }

    public class ValueNode : Node
    {
        public Predicate Value { get; }

        public ValueNode(Predicate value)
        {
            Value = value;
        }

        public override OptimizedNode Optimize(StringTable strings, AttributeTable attributes)
        {
             return OptimizeInternal(false, strings, attributes);
        }

        internal override OptimizedNode OptimizeInternal(bool negate, StringTable strings, AttributeTable attributes)
        {
            return OptimizedNode.Value(negate ? Value.Negate() : Value);
        }
    }

    public abstract class OptimizedNode : IComparable<OptimizedNode>, IEquatable<OptimizedNode>
    {
        private ulong? _idCache = null;

        public ulong Id
        {
            get
            {
                if (!_idCache.HasValue)
                {
                    _idCache = CalculateId();
                }
                return _idCache.Value;
            }
        }

        public abstract ulong Cost { get; }
        
        protected abstract ulong CalculateId();
        protected abstract ulong CalculateCost();

        // Primary sort by Cost, secondary by Id to ensure deterministic order
        public int CompareTo(OptimizedNode? other)
        {
            if (other == null) return 1;
            int costComparison = Cost.CompareTo(other.Cost);
            if (costComparison != 0) return costComparison;
            return Id.CompareTo(other.Id);
        }

        public abstract bool Equals(OptimizedNode? other);

        public override bool Equals(object? obj) => Equals(obj as OptimizedNode);

        public override int GetHashCode() => Id.GetHashCode();

        public static ulong CombineHashes(ulong h1, ulong h2)
        {
            // A common way to combine hash codes.
            // Similar to System.HashCode.Combine, but for ulong.
            // Uses FNV offset basis and prime.
            const ulong fnvOffsetBasis = 14695981039346656037UL;
            const ulong fnvPrime = 1099511628211UL;

            ulong hash = fnvOffsetBasis;
            hash = (hash ^ h1) * fnvPrime;
            hash = (hash ^ h2) * fnvPrime;
            return hash;
        }
        
        public static ulong HashString(string str)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(str));
                return BitConverter.ToUInt64(bytes, 0);
            }
        }

        public static ulong HashList<T>(IEnumerable<T> list, string salt) where T : IEquatable<T>
        {
            const ulong fnvOffsetBasis = 14695981039346656037UL;
            const ulong fnvPrime = 1099511628211UL;

            ulong hash = fnvOffsetBasis;
            hash = (hash ^ HashString(salt)) * fnvPrime;

            foreach (var item in list)
            {
                if (item is ulong ulongItem)
                {
                     hash = (hash ^ ulongItem) * fnvPrime;
                }
                else
                {
                    hash = (hash ^ (ulong)item.GetHashCode()) * fnvPrime;
                }
            }
            return hash;
        }

        public static OptimizedNode And(OptimizedNode left, OptimizedNode right)
        {
            // Ensure canonical order for AND nodes
            if (left.CompareTo(right) > 0)
            {
                return new OptimizedAndNode(right, left);
            }
            return new OptimizedAndNode(left, right);
        }

        public static OptimizedNode Or(OptimizedNode left, OptimizedNode right)
        {
            // Ensure canonical order for OR nodes
            if (left.CompareTo(right) > 0)
            {
                return new OptimizedOrNode(right, left);
            }
            return new OptimizedOrNode(left, right);
        }

        public static OptimizedNode Value(Predicate predicate)
        {
            return new OptimizedValueNode(predicate);
        }

        public sealed class OptimizedAndNode : OptimizedNode
        {
            public OptimizedNode Left { get; }
            public OptimizedNode Right { get; }

            internal OptimizedAndNode(OptimizedNode left, OptimizedNode right)
            {
                Left = left;
                Right = right;
            }

            public override ulong Cost => CalculateCost();

            protected override ulong CalculateId()
            {
                ulong operatorSalt = 3; // Prime for AND
                return CombineHashes(CombineHashes(Left.Id, Right.Id), operatorSalt);
            }

            protected override ulong CalculateCost() => Left.Cost + Right.Cost + 50; // Cost of children + cost of AND operation
            
            public override bool Equals(OptimizedNode? other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                if (other is OptimizedAndNode andNode)
                {
                    return Left.Equals(andNode.Left) && Right.Equals(andNode.Right);
                }
                return false;
            }

            public override int GetHashCode() => base.GetHashCode();

            public override string ToString() => $"({Left} AND {Right})";
        }

        public sealed class OptimizedOrNode : OptimizedNode
        {
            public OptimizedNode Left { get; }
            public OptimizedNode Right { get; }

            internal OptimizedOrNode(OptimizedNode left, OptimizedNode right)
            {
                Left = left;
                Right = right;
            }

            public override ulong Cost => CalculateCost();

            protected override ulong CalculateId()
            {
                ulong operatorSalt = 5; // Prime for OR
                return CombineHashes(CombineHashes(Left.Id, Right.Id), operatorSalt);
            }
            protected override ulong CalculateCost() => Left.Cost + Right.Cost + 60; // Cost of children + cost of OR operation

            public override bool Equals(OptimizedNode? other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                if (other is OptimizedOrNode orNode)
                {
                    return Left.Equals(orNode.Left) && Right.Equals(orNode.Right);
                }
                return false;
            }
            public override int GetHashCode() => base.GetHashCode();

            public override string ToString() => $"({Left} OR {Right})";
        }

        public sealed class OptimizedValueNode : OptimizedNode
        {
            public Predicate NodeValue { get; } 

            internal OptimizedValueNode(Predicate value)
            {
                NodeValue = value; 
            }

            public override ulong Cost => CalculateCost();

            protected override ulong CalculateId() => NodeValue.Id; 
            protected override ulong CalculateCost() => NodeValue.Cost; 

            public override bool Equals(OptimizedNode? other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return other is OptimizedValueNode valueNode && NodeValue.Equals(valueNode.NodeValue);
            }
            public override int GetHashCode() => base.GetHashCode();

            public override string ToString() => NodeValue.ToString();
        }
    }
}
