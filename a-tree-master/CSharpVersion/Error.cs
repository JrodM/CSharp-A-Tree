using System;

namespace ATree
{
    public class ATreeException : Exception
    {
        public ATreeException(string message) : base(message) { }
        public ATreeException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class ParseException : ATreeException
    {
        public ParseException(string message) : base(message) { }
    }

    public class EventException : ATreeException
    {
        public EventException(string message) : base(message) { }
        public EventException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class EventError
    {
        public static EventException NonExistingAttribute(string attributeName)
        {
            return new EventException($"ABE refers to non-existing attribute '{attributeName}'");
        }

        public static EventException MismatchingTypes(string attributeName, AttributeKind expectedKind, PredicateKind actualKind)
        {
            return new EventException($"Attribute '{attributeName}' of kind {expectedKind} cannot be used with predicate kind {actualKind}");
        }
    }
}
