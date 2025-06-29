using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ATree
{
    public class Predicate : IEquatable<Predicate>
    {
        public AttributeId Attribute { get; }
        public PredicateKind Kind { get; }

        private ulong? _idCache;
        private ulong? _costCache;

        public Predicate(AttributeTable attributes, string name, PredicateKind kind)
        {
            var attributeDefinition = attributes.GetDefinitionByName(name) 
                ?? throw EventError.NonExistingAttribute(name);
            
            ValidatePredicate(name, kind, attributeDefinition.Kind);
            Attribute = attributeDefinition.Id;
            Kind = kind;
        }

        private Predicate(AttributeId attribute, PredicateKind kind)
        {
            Attribute = attribute;
            Kind = kind;
        }

        public ulong Id
        {
            get
            {
                if (!_idCache.HasValue)
                {
                    _idCache = CalculateIdInternal();
                }
                return _idCache.Value;
            }
        }

        private ulong CalculateIdInternal()
        {
            // Mimic Rust\'s DefaultHasher approach for Predicate
            using (var sha256 = SHA256.Create())
            {
                // Combine hashes of Attribute and Kind.Id
                // This is a simplified version. Rust\'s hashing is more complex.
                // For a closer match, a more robust hashing strategy would be needed.
                // Here, we combine the ulong ID of Kind and the int ID of Attribute.
                byte[] attributeBytes = BitConverter.GetBytes(Attribute.Value);
                byte[] kindIdBytes = BitConverter.GetBytes(Kind.Id);
                byte[] combined = new byte[attributeBytes.Length + kindIdBytes.Length];
                Buffer.BlockCopy(attributeBytes, 0, combined, 0, attributeBytes.Length);
                Buffer.BlockCopy(kindIdBytes, 0, combined, attributeBytes.Length, kindIdBytes.Length);
                
                var hashBytes = sha256.ComputeHash(combined);
                return BitConverter.ToUInt64(hashBytes, 0);
            }
        }


        public ulong Cost
        {
            get
            {
                if (!_costCache.HasValue)
                {
                    _costCache = Kind.Cost;
                }
                return _costCache.Value;
            }
        }

        public Predicate Negate()
        {
            return new Predicate(Attribute, Kind.Negate());
        }

        public bool? Evaluate(Event e)
        {
            AttributeValue value = e.GetValue(Attribute);

            return (Kind, value) switch
            {
                (PredicateKind.Null nullKind, _) => nullKind.Operator.Evaluate(value),
                (_, AttributeValue.UndefinedAttributeValue) => null,
                (PredicateKind.Variable _, AttributeValue.BooleanValue boolVal) => boolVal.Value,
                (PredicateKind.NegatedVariable _, AttributeValue.BooleanValue boolVal) => !boolVal.Value,
                (PredicateKind.Set setKind, _) => setKind.Operator.Evaluate(setKind.Haystack, value),
                (PredicateKind.Comparison comparisonKind, _) => comparisonKind.Operator.Evaluate(comparisonKind.ValueToCompare, value),
                (PredicateKind.Equality equalityKind, _) => equalityKind.Operator.Evaluate(equalityKind.ValueToCompare, value),
                (PredicateKind.List listKind, _) => listKind.Operator.Evaluate(listKind.ListToCompare, value),
                _ => throw new InvalidOperationException($"Invalid predicate kind: {Kind} or unhandled AttributeValue type: {value.GetType()} for evaluation.")
            };
        }

        public override string ToString() => $"<{Attribute}, {Kind}>";

        private static void ValidatePredicate(string name, PredicateKind kind, AttributeKind attributeKind)
        {
            bool isValid = (kind, attributeKind) switch
            {
                (PredicateKind.Set { Haystack: ListLiteral.StringList _ }, AttributeKind.String) => true,
                (PredicateKind.Set { Haystack: ListLiteral.IntegerList _ }, AttributeKind.Integer) => true,

                (PredicateKind.Comparison { ValueToCompare: ComparisonValue.Integer _ }, AttributeKind.Integer) => true,
                (PredicateKind.Comparison { ValueToCompare: ComparisonValue.Float _ }, AttributeKind.Float) => true,

                (PredicateKind.Equality { ValueToCompare: PrimitiveLiteral.Integer _ }, AttributeKind.Integer) => true,
                (PredicateKind.Equality { ValueToCompare: PrimitiveLiteral.Float _ }, AttributeKind.Float) => true,
                (PredicateKind.Equality { ValueToCompare: PrimitiveLiteral.String _ }, AttributeKind.String) => true,
                // Boolean equality is not explicitly listed in Rust validation but implied by Variable/NegatedVariable
                (PredicateKind.Equality { ValueToCompare: PrimitiveLiteral.Boolean _ }, AttributeKind.Boolean) => true,


                (PredicateKind.List { ListToCompare: ListLiteral.IntegerList _ }, AttributeKind.IntegerList) => true,
                (PredicateKind.List { ListToCompare: ListLiteral.StringList _ }, AttributeKind.StringList) => true,

                (PredicateKind.Variable _, AttributeKind.Boolean) => true,
                (PredicateKind.NegatedVariable _, AttributeKind.Boolean) => true,

                (PredicateKind.Null { Operator: NullOperator.IsEmpty }, AttributeKind.StringList) => true,
                (PredicateKind.Null { Operator: NullOperator.IsEmpty }, AttributeKind.IntegerList) => true,
                (PredicateKind.Null { Operator: NullOperator.IsNotEmpty }, AttributeKind.StringList) => true,
                (PredicateKind.Null { Operator: NullOperator.IsNotEmpty }, AttributeKind.IntegerList) => true,
                (PredicateKind.Null { Operator: NullOperator.IsNull }, AttributeKind.Integer) => true,
                (PredicateKind.Null { Operator: NullOperator.IsNull }, AttributeKind.Float) => true,
                (PredicateKind.Null { Operator: NullOperator.IsNull }, AttributeKind.String) => true,
                (PredicateKind.Null { Operator: NullOperator.IsNull }, AttributeKind.Boolean) => true,
                (PredicateKind.Null { Operator: NullOperator.IsNotNull }, AttributeKind.Integer) => true,
                (PredicateKind.Null { Operator: NullOperator.IsNotNull }, AttributeKind.Float) => true,
                (PredicateKind.Null { Operator: NullOperator.IsNotNull }, AttributeKind.String) => true,
                (PredicateKind.Null { Operator: NullOperator.IsNotNull }, AttributeKind.Boolean) => true,

                _ => false
            };

            if (!isValid)
            {
                throw EventError.MismatchingTypes(name, attributeKind, kind);
            }
        }

        public bool Equals(Predicate? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Attribute.Equals(other.Attribute) && Kind.Equals(other.Kind);
        }

        public override bool Equals(object? obj) => Equals(obj as Predicate);

        public override int GetHashCode() => HashCode.Combine(Attribute, Kind);
        
        public static Predicate Eq(AttributeTable attributes, string name, string value, StringTable strings)
        {
            var literal = new PrimitiveLiteral.String(strings.Intern(value));
            var kind = new PredicateKind.Equality(EqualityOperator.Equal, literal);
            return new Predicate(attributes, name, kind);
        }

        public static Predicate Eq(AttributeTable attributes, string name, long value)
        {
            var kind = new PredicateKind.Equality(EqualityOperator.Equal, new PrimitiveLiteral.Integer(value));
            return new Predicate(attributes, name, kind);
        }
        
        public static Predicate Eq(AttributeTable attributes, string name, bool value)
        {
            // In Rust, boolean equality is typically handled by Variable or NegatedVariable
            // If direct boolean equality predicate is needed:
            var kind = new PredicateKind.Equality(EqualityOperator.Equal, new PrimitiveLiteral.Boolean(value));
            return new Predicate(attributes, name, kind);
        }


        public static Predicate In(AttributeTable attributes, string name, IEnumerable<string> values, StringTable strings)
        {
            var stringIds = values.Select(v => strings.Intern(v)).ToList();
            var kind = new PredicateKind.Set(SetOperator.In, new ListLiteral.StringList(stringIds));
            return new Predicate(attributes, name, kind);
        }

        public static Predicate In(AttributeTable attributes, string name, IEnumerable<long> values)
        {
            var kind = new PredicateKind.Set(SetOperator.In, new ListLiteral.IntegerList(values.ToList()));
            return new Predicate(attributes, name, kind);
        }
        
        public static Predicate Variable(AttributeTable attributes, string name)
        {
            return new Predicate(attributes, name, new PredicateKind.Variable());
        }

        public static Predicate NegatedVariable(AttributeTable attributes, string name)
        {
            return new Predicate(attributes, name, new PredicateKind.NegatedVariable());
        }
         public static Predicate IsNull(AttributeTable attributes, string name)
        {
            return new Predicate(attributes, name, new PredicateKind.Null(NullOperator.IsNull));
        }

        public static Predicate IsNotNull(AttributeTable attributes, string name)
        {
            return new Predicate(attributes, name, new PredicateKind.Null(NullOperator.IsNotNull));
        }
        
        public static Predicate IsEmpty(AttributeTable attributes, string name)
        {
            return new Predicate(attributes, name, new PredicateKind.Null(NullOperator.IsEmpty));
        }

        public static Predicate IsNotEmpty(AttributeTable attributes, string name)
        {
            return new Predicate(attributes, name, new PredicateKind.Null(NullOperator.IsNotEmpty));
        }

        public static Predicate LessThan(AttributeTable attributes, string name, long value)
        {
            var kind = new PredicateKind.Comparison(ComparisonOperator.LessThan, new ComparisonValue.Integer(value));
            return new Predicate(attributes, name, kind);
        }
        public static Predicate LessThan(AttributeTable attributes, string name, decimal value)
        {
            var kind = new PredicateKind.Comparison(ComparisonOperator.LessThan, new ComparisonValue.Float(value));
            return new Predicate(attributes, name, kind);
        }
        public static Predicate LessThanOrEqual(AttributeTable attributes, string name, long value)
        {
            var kind = new PredicateKind.Comparison(ComparisonOperator.LessThanEqual, new ComparisonValue.Integer(value));
            return new Predicate(attributes, name, kind);
        }
        public static Predicate LessThanOrEqual(AttributeTable attributes, string name, decimal value)
        {
            var kind = new PredicateKind.Comparison(ComparisonOperator.LessThanEqual, new ComparisonValue.Float(value));
            return new Predicate(attributes, name, kind);
        }
        public static Predicate GreaterThan(AttributeTable attributes, string name, long value)
        {
            var kind = new PredicateKind.Comparison(ComparisonOperator.GreaterThan, new ComparisonValue.Integer(value));
            return new Predicate(attributes, name, kind);
        }
        public static Predicate GreaterThan(AttributeTable attributes, string name, decimal value)
        {
            var kind = new PredicateKind.Comparison(ComparisonOperator.GreaterThan, new ComparisonValue.Float(value));
            return new Predicate(attributes, name, kind);
        }
        public static Predicate GreaterThanOrEqual(AttributeTable attributes, string name, long value)
        {
            var kind = new PredicateKind.Comparison(ComparisonOperator.GreaterThanEqual, new ComparisonValue.Integer(value));
            return new Predicate(attributes, name, kind);
        }
        public static Predicate GreaterThanOrEqual(AttributeTable attributes, string name, decimal value)
        {
            var kind = new PredicateKind.Comparison(ComparisonOperator.GreaterThanEqual, new ComparisonValue.Float(value));
            return new Predicate(attributes, name, kind);
        }

        public static Predicate ListOneOf(AttributeTable attributes, string name, IEnumerable<string> values, StringTable strings)
        {
            var stringIds = values.Select(v => strings.Intern(v)).ToList();
            var kind = new PredicateKind.List(ListOperator.OneOf, new ListLiteral.StringList(stringIds));
            return new Predicate(attributes, name, kind);
        }

        public static Predicate ListAllOf(AttributeTable attributes, string name, IEnumerable<string> values, StringTable strings)
        {
            var stringIds = values.Select(v => strings.Intern(v)).ToList();
            var kind = new PredicateKind.List(ListOperator.AllOf, new ListLiteral.StringList(stringIds));
            return new Predicate(attributes, name, kind);
        }
    }

    public abstract class PredicateKind : IEquatable<PredicateKind>
    {
        // Costs from Rust version
        protected const ulong ConstantCost = 0;
        protected const ulong LogarithmicCost = 1; // Per element for set operations
        protected const ulong ListCost = 2;        // Per element for list operations

        private ulong? _idCache;

        public ulong Id
        {
            get
            {
                if (!_idCache.HasValue)
                {
                    _idCache = CalculateIdInternal();
                }
                return _idCache.Value;
            }
        }
        
        public abstract ulong Cost { get; }
        public abstract PredicateKind Negate();
        protected abstract ulong CalculateIdInternal();

        public abstract bool Equals(PredicateKind? other);
        public override bool Equals(object? obj) => Equals(obj as PredicateKind);
        public override int GetHashCode() => Id.GetHashCode();

        public sealed class Variable : PredicateKind
        {
            public override ulong Cost => ConstantCost;
            public override PredicateKind Negate() => new NegatedVariable();
            public override string ToString() => "id, variable";
            protected override ulong CalculateIdInternal() => OptimizedNode.HashString("Variable");
            public override bool Equals(PredicateKind? other) => other is Variable;
        }

        public sealed class NegatedVariable : PredicateKind
        {
            public override ulong Cost => ConstantCost;
            public override PredicateKind Negate() => new Variable();
            public override string ToString() => "not, variable";
            protected override ulong CalculateIdInternal() => OptimizedNode.HashString("NegatedVariable");
            public override bool Equals(PredicateKind? other) => other is NegatedVariable;
        }

        public sealed class Set : PredicateKind
        {
            public SetOperator Operator { get; }
            public ListLiteral Haystack { get; }

            public Set(SetOperator op, ListLiteral haystack)
            {
                Operator = op;
                Haystack = haystack;
            }

            public override ulong Cost => (ulong)Haystack.Count * LogarithmicCost;
            public override PredicateKind Negate() => new Set(Operator.Negate(), Haystack);
            public override string ToString() => $"{Operator}, {Haystack}";
            protected override ulong CalculateIdInternal() => OptimizedNode.CombineHashes(OptimizedNode.HashString(Operator.ToString()), Haystack.Id);
            public override bool Equals(PredicateKind? other) => other is Set s && Operator == s.Operator && Haystack.Equals(s.Haystack);
        }

        public sealed class Comparison : PredicateKind
        {
            public ComparisonOperator Operator { get; }
            public ComparisonValue ValueToCompare { get; }

            public Comparison(ComparisonOperator op, ComparisonValue valueToCompare)
            {
                Operator = op;
                ValueToCompare = valueToCompare;
            }

            public override ulong Cost => ConstantCost;
            public override PredicateKind Negate() => new Comparison(Operator.Negate(), ValueToCompare);
            public override string ToString() => $"{Operator}, {ValueToCompare}";
            protected override ulong CalculateIdInternal() => OptimizedNode.CombineHashes(OptimizedNode.HashString(Operator.ToString()), ValueToCompare.Id);
            public override bool Equals(PredicateKind? other) => other is Comparison c && Operator == c.Operator && ValueToCompare.Equals(c.ValueToCompare);
        }

        public sealed class Equality : PredicateKind
        {
            public EqualityOperator Operator { get; }
            public PrimitiveLiteral ValueToCompare { get; }

            public Equality(EqualityOperator op, PrimitiveLiteral valueToCompare)
            {
                Operator = op;
                ValueToCompare = valueToCompare;
            }

            public override ulong Cost => ConstantCost;
            public override PredicateKind Negate() => new Equality(Operator.Negate(), ValueToCompare);
            public override string ToString() => $"{Operator}, {ValueToCompare}";
            protected override ulong CalculateIdInternal() => OptimizedNode.CombineHashes(OptimizedNode.HashString(Operator.ToString()), ValueToCompare.Id);
            public override bool Equals(PredicateKind? other) => other is Equality e && Operator == e.Operator && ValueToCompare.Equals(e.ValueToCompare);
        }

        public sealed class List : PredicateKind
        {
            public ListOperator Operator { get; }
            public ListLiteral ListToCompare { get; }

            public List(ListOperator op, ListLiteral listToCompare)
            {
                Operator = op;
                ListToCompare = listToCompare;
            }
            public override ulong Cost => (ulong)ListToCompare.Count * ListCost;
            public override PredicateKind Negate() => new List(Operator.Negate(), ListToCompare);
            public override string ToString() => $"{Operator}, {ListToCompare}";
            protected override ulong CalculateIdInternal() => OptimizedNode.CombineHashes(OptimizedNode.HashString(Operator.ToString()), ListToCompare.Id);
            public override bool Equals(PredicateKind? other) => other is List l && Operator == l.Operator && ListToCompare.Equals(l.ListToCompare);
        }

        public sealed class Null : PredicateKind
        {
            public NullOperator Operator { get; }
            public Null(NullOperator op) { Operator = op; }
            public override ulong Cost => ConstantCost;
            public override PredicateKind Negate() => new Null(Operator.Negate());
            public override string ToString() => $"{Operator}, variable";
            protected override ulong CalculateIdInternal() => OptimizedNode.HashString("Null_" + Operator.ToString());
            public override bool Equals(PredicateKind? other) => other is Null n && Operator == n.Operator;
        }
    }

    public enum SetOperator { NotIn, In }
    public static class SetOperatorExtensions
    {
        public static bool Evaluate(this SetOperator op, ListLiteral haystack, AttributeValue needle)
        {
            return (haystack, needle) switch
            {
                (ListLiteral.StringList sHaystack, AttributeValue.StringValue sNeedle) => op.Apply(sHaystack.Values, sNeedle.Value),
                (ListLiteral.IntegerList iHaystack, AttributeValue.IntegerValue iNeedle) => op.Apply(iHaystack.Values, iNeedle.Value),
                _ => throw new InvalidOperationException($"Set operation ({op}) in haystack {haystack} for {needle} should never happen.")
            };
        }
        private static bool Apply<T>(this SetOperator op, IReadOnlyList<T> haystack, T needle) where T : IComparable<T>
        {
            var index = haystack.BinarySearch(needle); // Assumes haystack is sorted (guaranteed by ListLiteral constructors)
            return op == SetOperator.In ? index >= 0 : index < 0;
        }
        public static SetOperator Negate(this SetOperator op) => op == SetOperator.In ? SetOperator.NotIn : SetOperator.In;
        public static string Display(this SetOperator op) => op == SetOperator.In ? "in" : "not in";
    }

    public enum ComparisonOperator { LessThan, LessThanEqual, GreaterThanEqual, GreaterThan }
    public static class ComparisonOperatorExtensions
    {
        public static bool Evaluate(this ComparisonOperator op, ComparisonValue a, AttributeValue b)
        {
            return (a, b) switch
            {
                (ComparisonValue.Float fcv, AttributeValue.FloatValue fv) => op.Apply(fv.Value, fcv.Value),
                (ComparisonValue.Integer icv, AttributeValue.IntegerValue iv) => op.Apply(iv.Value, icv.Value),
                _ => throw new InvalidOperationException($"Comparison ({op}) between {a} and {b} should never happen.")
            };
        }
        private static bool Apply<T>(this ComparisonOperator op, T eventValue, T predicateValue) where T : IComparable<T>
        {
            int comparisonResult = eventValue.CompareTo(predicateValue);
            return op switch
            {
                ComparisonOperator.LessThan => comparisonResult < 0,
                ComparisonOperator.LessThanEqual => comparisonResult <= 0,
                ComparisonOperator.GreaterThan => comparisonResult > 0,
                ComparisonOperator.GreaterThanEqual => comparisonResult >= 0,
                _ => throw new ArgumentOutOfRangeException(nameof(op))
            };
        }
        public static ComparisonOperator Negate(this ComparisonOperator op) => op switch
        {
            ComparisonOperator.LessThan => ComparisonOperator.GreaterThanEqual,
            ComparisonOperator.LessThanEqual => ComparisonOperator.GreaterThan,
            ComparisonOperator.GreaterThan => ComparisonOperator.LessThanEqual,
            ComparisonOperator.GreaterThanEqual => ComparisonOperator.LessThan,
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
        public static string Display(this ComparisonOperator op) => op switch
        {
            ComparisonOperator.GreaterThanEqual => ">=",
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessThanEqual => "<=",
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
    }

    public abstract class ComparisonValue : IEquatable<ComparisonValue>
    {
        public abstract ulong Id { get; }
        public abstract bool Equals(ComparisonValue? other);
        public override bool Equals(object? obj) => Equals(obj as ComparisonValue);
        public override int GetHashCode() => Id.GetHashCode();

        public sealed class Integer : ComparisonValue
        {
            public long Value { get; }
            public Integer(long value) { Value = value; }
            public override ulong Id => OptimizedNode.CombineHashes(OptimizedNode.HashString("IntegerComparisonValue"), (ulong)Value.GetHashCode());
            public override bool Equals(ComparisonValue? other) => other is Integer i && Value == i.Value;
            public override string ToString() => Value.ToString();
        }
        public sealed class Float : ComparisonValue
        {
            public decimal Value { get; }
            public Float(decimal value) { Value = value; }
            public override ulong Id => OptimizedNode.CombineHashes(OptimizedNode.HashString("FloatComparisonValue"), (ulong)Value.GetHashCode());
            public override bool Equals(ComparisonValue? other) => other is Float f && Value == f.Value;
            public override string ToString() => Value.ToString();
        }
    }

    public enum EqualityOperator { NotEqual, Equal }
    public static class EqualityOperatorExtensions
    {
        public static bool Evaluate(this EqualityOperator op, PrimitiveLiteral a, AttributeValue b)
        {
            bool result = (a, b) switch
            {
                (PrimitiveLiteral.Boolean bl, AttributeValue.BooleanValue bv) => bl.Value == bv.Value,
                (PrimitiveLiteral.Float fl, AttributeValue.FloatValue fv) => fl.Value == fv.Value,
                (PrimitiveLiteral.Integer il, AttributeValue.IntegerValue iv) => il.Value == iv.Value,
                (PrimitiveLiteral.String sl, AttributeValue.StringValue sv) => sl.Value == sv.Value,
                _ => throw new InvalidOperationException($"Equality ({op}) between {a} and {b} should never happen.")
            };
            return op == EqualityOperator.Equal ? result : !result;
        }
        public static EqualityOperator Negate(this EqualityOperator op) => op == EqualityOperator.Equal ? EqualityOperator.NotEqual : EqualityOperator.Equal;
        public static string Display(this EqualityOperator op) => op == EqualityOperator.Equal ? "==" : "!=";
    }

    public abstract class PrimitiveLiteral : IEquatable<PrimitiveLiteral>
    {
        public abstract ulong Id { get; }
        public abstract bool Equals(PrimitiveLiteral? other);
        public override bool Equals(object? obj) => Equals(obj as PrimitiveLiteral);
        public override int GetHashCode() => Id.GetHashCode();

        public sealed class Boolean : PrimitiveLiteral
        {
            public bool Value { get; }
            public Boolean(bool value) { Value = value; }
            public override ulong Id => OptimizedNode.CombineHashes(OptimizedNode.HashString("BooleanLiteral"), (ulong)(Value ? 1 : 0));
            public override bool Equals(PrimitiveLiteral? other) => other is Boolean b && Value == b.Value;
            public override string ToString() => Value.ToString();
        }
        public sealed class Integer : PrimitiveLiteral
        {
            public long Value { get; }
            public Integer(long value) { Value = value; }
            public override ulong Id => OptimizedNode.CombineHashes(OptimizedNode.HashString("IntegerLiteral"), (ulong)Value.GetHashCode());
            public override bool Equals(PrimitiveLiteral? other) => other is Integer i && Value == i.Value;
            public override string ToString() => Value.ToString();
        }
        public sealed class Float : PrimitiveLiteral
        {
            public decimal Value { get; }
            public Float(decimal value) { Value = value; }
            public override ulong Id => OptimizedNode.CombineHashes(OptimizedNode.HashString("FloatLiteral"), (ulong)Value.GetHashCode());
            public override bool Equals(PrimitiveLiteral? other) => other is Float f && Value == f.Value;
            public override string ToString() => Value.ToString();
        }
        public sealed class String : PrimitiveLiteral
        {
            public int Value { get; } // StringId
            public String(int value) { Value = value; }
            public override ulong Id => OptimizedNode.CombineHashes(OptimizedNode.HashString("StringLiteral"), (ulong)Value.GetHashCode());
            public override bool Equals(PrimitiveLiteral? other) => other is String s && Value == s.Value;
            public override string ToString() => $"StringId({Value})";
        }
    }

    public enum ListOperator { NoneOf, OneOf, AllOf, NotAllOf } // Added NotAllOf
    public static class ListOperatorExtensions
    {
        public static bool Evaluate(this ListOperator op, ListLiteral listToCompare, AttributeValue eventValue)
        {
            return (listToCompare, eventValue) switch
            {
                (ListLiteral.StringList predList, AttributeValue.StringListValue eventList) => op.Apply(predList.Values, eventList.Value),
                (ListLiteral.IntegerList predList, AttributeValue.IntegerListValue eventList) => op.Apply(predList.Values, eventList.Value),
                _ => throw new InvalidOperationException($"List operation ({op}) with {listToCompare} and {eventValue} should never happen.")
            };
        }

        private static bool Apply<T>(this ListOperator op, IReadOnlyList<T> predicateList, IReadOnlyList<T> eventList) where T : IEquatable<T>
        {
            // Ensure predicateList is sorted for efficient operations if needed, though current ops are fine.
            // For "OneOf", any common element means true.
            // For "AllOf", all elements of predicateList must be in eventList.
            // For "NoneOf", no common elements.
            switch (op)
            {
                case ListOperator.OneOf:
                    return predicateList.Any(pItem => eventList.Contains(pItem));
                case ListOperator.AllOf:
                    if (!predicateList.Any()) return true; // All of an empty set is vacuously true
                    return predicateList.All(pItem => eventList.Contains(pItem));
                case ListOperator.NoneOf:
                    return !predicateList.Any(pItem => eventList.Contains(pItem));
                case ListOperator.NotAllOf: // Added case for NotAllOf
                    if (!predicateList.Any()) return false; // Not all of an empty set is false (as AllOf is true)
                    return !predicateList.All(pItem => eventList.Contains(pItem));
                default:
                    throw new ArgumentOutOfRangeException(nameof(op));
            }
        }
        public static ListOperator Negate(this ListOperator op) => op switch
        {
            ListOperator.OneOf => ListOperator.NoneOf,
            ListOperator.NoneOf => ListOperator.OneOf,
            ListOperator.AllOf => ListOperator.NotAllOf, // Changed to NotAllOf
            ListOperator.NotAllOf => ListOperator.AllOf, // Added negation for NotAllOf
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
        public static string Display(this ListOperator op) => op switch
        {
            ListOperator.OneOf => "one of",
            ListOperator.NoneOf => "none of",
            ListOperator.AllOf => "all of",
            ListOperator.NotAllOf => "not all of", // Added display for NotAllOf
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
    }

    public abstract class ListLiteral : IEquatable<ListLiteral>
    {
        public abstract int Count { get; }
        public abstract ulong Id { get; }
        public abstract bool Equals(ListLiteral? other);
        public override bool Equals(object? obj) => Equals(obj as ListLiteral);
        public override int GetHashCode() => Id.GetHashCode();

        public sealed class StringList : ListLiteral
        {
            public IReadOnlyList<int> Values { get; } // StringIds, sorted
            public StringList(List<int> values) { values.Sort(); Values = values.AsReadOnly(); }
            public override int Count => Values.Count;
            public override ulong Id => OptimizedNode.HashList(Values.Select(v => (ulong)v), "StringListLiteral");
            public override bool Equals(ListLiteral? other) => other is StringList sl && Values.SequenceEqual(sl.Values);
            public override string ToString() => $"[{string.Join(", ", Values.Select(id => $"StringId({id})"))}]";
        }

        public sealed class IntegerList : ListLiteral
        {
            public IReadOnlyList<long> Values { get; } // Sorted
            public IntegerList(List<long> values) { values.Sort(); Values = values.AsReadOnly(); }
            public override int Count => Values.Count;
            public override ulong Id => OptimizedNode.HashList(Values.Select(v => (ulong)v), "IntegerListLiteral");
            public override bool Equals(ListLiteral? other) => other is IntegerList il && Values.SequenceEqual(il.Values);
            public override string ToString() => $"[{string.Join(", ", Values)}]";
        }
    }

    public enum NullOperator { IsNull, IsNotNull, IsEmpty, IsNotEmpty }
    public static class NullOperatorExtensions
    {
        public static bool Evaluate(this NullOperator op, AttributeValue value)
        {
            return op switch
            {
                NullOperator.IsNull => value is AttributeValue.UndefinedAttributeValue,
                NullOperator.IsNotNull => !(value is AttributeValue.UndefinedAttributeValue),
                NullOperator.IsEmpty => value switch
                {
                    AttributeValue.StringListValue slv => slv.Value.Count == 0,
                    AttributeValue.IntegerListValue ilv => ilv.Value.Count == 0,
                    //AttributeValue.FloatListValue flv => flv.Value.Count == 0, // If FloatList is supported for IsEmpty
                    //AttributeValue.BooleanListValue blv => blv.Value.Count == 0, // If BooleanList is supported for IsEmpty
                    _ => false // Or throw, if type is not list-like and IsEmpty is used
                },
                NullOperator.IsNotEmpty => value switch
                {
                    AttributeValue.StringListValue slv => slv.Value.Count > 0,
                    AttributeValue.IntegerListValue ilv => ilv.Value.Count > 0,
                    //AttributeValue.FloatListValue flv => flv.Value.Count > 0,
                    //AttributeValue.BooleanListValue blv => blv.Value.Count > 0,
                    _ => false // Or throw
                },
                _ => throw new ArgumentOutOfRangeException(nameof(op))
            };
        }
        public static NullOperator Negate(this NullOperator op) => op switch
        {
            NullOperator.IsNull => NullOperator.IsNotNull,
            NullOperator.IsNotNull => NullOperator.IsNull,
            NullOperator.IsEmpty => NullOperator.IsNotEmpty,
            NullOperator.IsNotEmpty => NullOperator.IsEmpty,
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
        public static string Display(this NullOperator op) => op switch
        {
            NullOperator.IsNull => "is null",
            NullOperator.IsNotNull => "is not null",
            NullOperator.IsEmpty => "is empty",
            NullOperator.IsNotEmpty => "is not empty",
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
    }
}
