using System;
using System.Collections.Generic;
using System.Linq;

namespace ATree
{
    public readonly struct AttributeId : IEquatable<AttributeId>, IComparable<AttributeId>
    {
        public int Value { get; }

        public AttributeId(int value)
        {
            Value = value;
        }

        public bool Equals(AttributeId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is AttributeId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(AttributeId other) => Value.CompareTo(other.Value);

        public static implicit operator int(AttributeId id) => id.Value;
        public static explicit operator AttributeId(int value) => new AttributeId(value);

        public override string ToString() => $"AttrId({Value})";
    }

    public enum AttributeKind
    {
        Boolean,
        Integer,
        Float,
        String,
        BooleanList,
        IntegerList,
        FloatList,
        StringList,
        Undefined
    }

    public abstract class AttributeValue
    {
        public abstract AttributeKind Kind { get; }
        public static readonly UndefinedAttributeValue Undefined = new UndefinedAttributeValue();

        public class BooleanValue : AttributeValue
        {
            public bool Value { get; }
            public override AttributeKind Kind => AttributeKind.Boolean;
            public BooleanValue(bool value) { Value = value; }
            public override string ToString() => Value.ToString();
        }

        public class IntegerValue : AttributeValue
        {
            public long Value { get; }
            public override AttributeKind Kind => AttributeKind.Integer;
            public IntegerValue(long value) { Value = value; }
            public override string ToString() => Value.ToString();
        }

        public class FloatValue : AttributeValue
        {
            public decimal Value { get; }
            public override AttributeKind Kind => AttributeKind.Float;
            public FloatValue(decimal value) { Value = value; }
            public override string ToString() => Value.ToString();
        }

        public class StringValue : AttributeValue
        {
            public int Value { get; } 
            public override AttributeKind Kind => AttributeKind.String;
            public StringValue(int value) { Value = value; }
        }
        public class BooleanListValue : AttributeValue
        {
            public IReadOnlyList<bool> Value { get; }
            public override AttributeKind Kind => AttributeKind.BooleanList;
            public BooleanListValue(IReadOnlyList<bool> value) { Value = value; }
        }

        public class IntegerListValue : AttributeValue
        {
            public IReadOnlyList<long> Value { get; }
            public override AttributeKind Kind => AttributeKind.IntegerList;
            public IntegerListValue(IReadOnlyList<long> value) { Value = value; }
        }

        public class FloatListValue : AttributeValue
        {
            public IReadOnlyList<decimal> Value { get; }
            public override AttributeKind Kind => AttributeKind.FloatList;
            public FloatListValue(IReadOnlyList<decimal> value) { Value = value; }
        }

        public class StringListValue : AttributeValue
        {
            public IReadOnlyList<int> Value { get; } 
            public override AttributeKind Kind => AttributeKind.StringList;
            public StringListValue(IReadOnlyList<int> value) { Value = value; }
        }

        public class UndefinedAttributeValue : AttributeValue
        {
            public override AttributeKind Kind => AttributeKind.Undefined;
            public override string ToString() => "Undefined";
        }
    }

    public class AttributeDefinition
    {
        public string Name { get; }
        public AttributeKind Kind { get; } 
        public AttributeId Id { get; internal set; }

        public AttributeDefinition(string name, AttributeKind kind)
        {
            Name = name;
            Kind = kind;
            Id = new AttributeId(-1);
        }

        public static AttributeDefinition Boolean(string name) => new AttributeDefinition(name, AttributeKind.Boolean);
        public static AttributeDefinition Integer(string name) => new AttributeDefinition(name, AttributeKind.Integer);
        public static AttributeDefinition Float(string name) => new AttributeDefinition(name, AttributeKind.Float);
        public static AttributeDefinition String(string name) => new AttributeDefinition(name, AttributeKind.String);
        public static AttributeDefinition BooleanList(string name) => new AttributeDefinition(name, AttributeKind.BooleanList);
        public static AttributeDefinition IntegerList(string name) => new AttributeDefinition(name, AttributeKind.IntegerList);
        public static AttributeDefinition FloatList(string name) => new AttributeDefinition(name, AttributeKind.FloatList);
        public static AttributeDefinition StringList(string name) => new AttributeDefinition(name, AttributeKind.StringList);
    }

    public class AttributeTable
    {
        private readonly Dictionary<string, AttributeDefinition> _byName = new Dictionary<string, AttributeDefinition>();
        private readonly List<AttributeDefinition> _byId = new List<AttributeDefinition>();

        public AttributeTable(IEnumerable<AttributeDefinition> definitions)
        {
            foreach (var def in definitions)
            {
                if (_byName.ContainsKey(def.Name))
                {
                    throw new EventException($"Attribute {def.Name} has already been defined");
                }
                def.Id = new AttributeId(_byId.Count);
                _byName[def.Name] = def;
                _byId.Add(def);
            }
        }

        public AttributeDefinition? GetDefinitionByName(string name)
        {
            _byName.TryGetValue(name, out var def);
            return def;
        }

        public AttributeDefinition GetById(AttributeId id)
        {
            if (id.Value < 0 || id.Value >= _byId.Count)
            {
                 throw new ArgumentOutOfRangeException(nameof(id), "Invalid attribute ID.");
            }
            return _byId[id.Value];
        }

        public int Count => _byId.Count;
    }

    public class Event
    {
        private readonly List<AttributeValue> _values;

        internal Event(List<AttributeValue> values)
        {
            _values = values;
        }

        public AttributeValue GetValue(AttributeId attributeId)
        {
            if (attributeId.Value < 0 || attributeId.Value >= _values.Count)
            {
                return AttributeValue.Undefined;
            }
            return _values[attributeId.Value];
        }
    }

    public class EventBuilder
    {
        private readonly List<AttributeValue> _byIds;
        private readonly AttributeTable _attributes;
        private readonly StringTable _strings; 

        public EventBuilder(AttributeTable attributes, StringTable strings)
        {
            _attributes = attributes;
            _strings = strings; 
            _byIds = Enumerable.Repeat((AttributeValue)AttributeValue.Undefined, attributes.Count).ToList();
        }


        public Event Build()
        {
            return new Event(new List<AttributeValue>(_byIds));
        }

        private void AddValue(string name, AttributeKind expectedKind, Func<AttributeValue> valueFactory)
        {
            var definition = _attributes.GetDefinitionByName(name);
            if (definition == null) // Added null check for definition
            {
                throw new EventException($"Attribute '{name}' not defined.");
            }
            if (definition.Kind != expectedKind)
            {
                throw new EventException($"{name}: wrong types => expected: {expectedKind}, found: {definition.Kind}");
            }
            _byIds[definition.Id] = valueFactory();
        }

        public EventBuilder WithBoolean(string name, bool value)
        {
            AddValue(name, AttributeKind.Boolean, () => new AttributeValue.BooleanValue(value));
            return this;
        }

        public EventBuilder WithInteger(string name, long value)
        {
            AddValue(name, AttributeKind.Integer, () => new AttributeValue.IntegerValue(value));
            return this;
        }

        public EventBuilder WithFloat(string name, decimal value)
        {
            AddValue(name, AttributeKind.Float, () => new AttributeValue.FloatValue(value));
            return this;
        }

        public EventBuilder WithString(string name, string value)
        {
            int stringId = _strings.Intern(value);
            AddValue(name, AttributeKind.String, () => new AttributeValue.StringValue(stringId));
            return this;
        }

        public EventBuilder WithBooleanList(string name, IReadOnlyList<bool> value)
        {
            AddValue(name, AttributeKind.BooleanList, () => new AttributeValue.BooleanListValue(value));
            return this;
        }

        public EventBuilder WithIntegerList(string name, IReadOnlyList<long> value)
        {
            AddValue(name, AttributeKind.IntegerList, () => new AttributeValue.IntegerListValue(value));
            return this;
        }

        public EventBuilder WithFloatList(string name, IReadOnlyList<decimal> value)
        {
            AddValue(name, AttributeKind.FloatList, () => new AttributeValue.FloatListValue(value));
            return this;
        }

        public EventBuilder WithStringList(string name, IReadOnlyList<string> value)
        {
            var stringIds = value.Select(s => _strings.Intern(s)).ToList();
            AddValue(name, AttributeKind.StringList, () => new AttributeValue.StringListValue(stringIds));
            return this;
        }

        public EventBuilder WithFloat(string name, long number, uint scale)
        {
            decimal value = new decimal(number) / (decimal)Math.Pow(10, scale);
            return WithFloat(name, value);
        }

        public EventBuilder Add(string name, bool value)
        {
            var definition = _attributes.GetDefinitionByName(name);
            if (definition == null) throw new EventException($"Attribute '{name}' not defined.");
            if (definition.Kind != AttributeKind.Boolean) throw new EventException($"Type mismatch for attribute '{name}'. Expected Boolean, got {value.GetType()}.");
            _byIds[definition.Id.Value] = new AttributeValue.BooleanValue(value);
            return this;
        }

        public EventBuilder Add(string name, long value)
        {
            var definition = _attributes.GetDefinitionByName(name);
            if (definition == null) throw new EventException($"Attribute '{name}' not defined.");
            if (definition.Kind != AttributeKind.Integer) throw new EventException($"Type mismatch for attribute '{name}'. Expected Integer, got {value.GetType()}.");
            _byIds[definition.Id.Value] = new AttributeValue.IntegerValue(value);
            return this;
        }

        public EventBuilder Add(string name, decimal value)
        {
            var definition = _attributes.GetDefinitionByName(name);
            if (definition == null) throw new EventException($"Attribute '{name}' not defined.");
            if (definition.Kind != AttributeKind.Float) throw new EventException($"Type mismatch for attribute '{name}'. Expected Float, got {value.GetType()}.");
            _byIds[definition.Id.Value] = new AttributeValue.FloatValue(value);
            return this;
        }
        
        public EventBuilder Add(string name, string value)
        {
            var definition = _attributes.GetDefinitionByName(name);
            if (definition == null) throw new EventException($"Attribute '{name}' not defined.");
            if (definition.Kind != AttributeKind.String) throw new EventException($"Type mismatch for attribute '{name}'. Expected String, got {value.GetType()}.");
            _byIds[definition.Id.Value] = new AttributeValue.StringValue(_strings.Intern(value));
            return this;
        }

        public EventBuilder Add(string name, IReadOnlyList<bool> value)
        {
            var definition = _attributes.GetDefinitionByName(name);
            if (definition == null) throw new EventException($"Attribute '{name}' not defined.");
            if (definition.Kind != AttributeKind.BooleanList) throw new EventException($"Type mismatch for attribute '{name}'. Expected BooleanList, got {value.GetType()}.");
            _byIds[definition.Id.Value] = new AttributeValue.BooleanListValue(value);
            return this;
        }
        public EventBuilder Add(string name, IReadOnlyList<long> value)
        {
            var definition = _attributes.GetDefinitionByName(name);
            if (definition == null) throw new EventException($"Attribute '{name}' not defined.");
            if (definition.Kind != AttributeKind.IntegerList) throw new EventException($"Type mismatch for attribute '{name}'. Expected IntegerList, got {value.GetType()}.");
            _byIds[definition.Id.Value] = new AttributeValue.IntegerListValue(value);
            return this;
        }
        public EventBuilder Add(string name, IReadOnlyList<decimal> value)
        {
            var definition = _attributes.GetDefinitionByName(name);
            if (definition == null) throw new EventException($"Attribute '{name}' not defined.");
            if (definition.Kind != AttributeKind.FloatList) throw new EventException($"Type mismatch for attribute '{name}'. Expected FloatList, got {value.GetType()}.");
            _byIds[definition.Id.Value] = new AttributeValue.FloatListValue(value);
            return this;
        }
        public EventBuilder Add(string name, IReadOnlyList<string> value)
        {
            var definition = _attributes.GetDefinitionByName(name);
            if (definition == null) throw new EventException($"Attribute '{name}' not defined.");
            if (definition.Kind != AttributeKind.StringList) throw new EventException($"Type mismatch for attribute '{name}'. Expected StringList, got {value.GetType()}.");
            _byIds[definition.Id.Value] = new AttributeValue.StringListValue(value.Select(s => _strings.Intern(s)).ToList());
            return this;
        }
    }
}
