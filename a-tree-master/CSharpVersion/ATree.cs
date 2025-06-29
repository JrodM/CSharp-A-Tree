using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.ObjectPool;

namespace ATree
{
    using ExpressionId = System.UInt64;

    public class ATree<T> where T : IEquatable<T>
    {
        private List<Entry<T>> _nodes;
        private readonly ObjectPool<Entry<T>> _entryPool;

        private readonly StringTable _strings;
        private readonly AttributeTable _attributes;

        public StringTable Strings => _strings;
        public AttributeTable Attributes => _attributes;
        private readonly Dictionary<ExpressionId, int> _expressionToNode;
        private readonly Dictionary<T, int> _nodesByIds;
        private readonly List<int> _roots;
        private readonly List<int> _predicates;
        private int _maxLevel;

        private const int DefaultPredicates = 1000;
        private const int DefaultNodes = 2000;
        private const int DefaultRoots = 50;

        public ATree(IEnumerable<AttributeDefinition> definitions)
        {
            _attributes = new AttributeTable(definitions);
            _strings = new StringTable();
            _nodes = new List<Entry<T>>(DefaultNodes);
            _expressionToNode = new Dictionary<ExpressionId, int>();
            _nodesByIds = new Dictionary<T, int>();
            _roots = new List<int>(DefaultRoots);
            _predicates = new List<int>(DefaultPredicates);
            _maxLevel = 1;

            var objectPoolProvider = new DefaultObjectPoolProvider();
            _entryPool = objectPoolProvider.Create<Entry<T>>();
        }

        public void AddRule(T subscriptionId, Node expressionRoot)
        {
            if (expressionRoot == null)
            {
                throw new ArgumentNullException(nameof(expressionRoot));
            }
            OptimizedNode optimizedAstRoot = expressionRoot.Optimize(_strings, _attributes);
            InsertRoot(subscriptionId, optimizedAstRoot);
        }

        private void InsertRoot(T subscriptionId, OptimizedNode rootNode)
        {
            ExpressionId expressionId = rootNode.Id;

            if (_expressionToNode.TryGetValue(expressionId, out int existingNodeId))
            {
                AddSubscriptionId(subscriptionId, existingNodeId);
                IncrementUseCount(existingNodeId);
                _nodesByIds[subscriptionId] = existingNodeId;
                return;
            }

            ulong cost = rootNode.Cost;
            int newNodeIndex;

            switch (rootNode)
            {
                case OptimizedNode.OptimizedAndNode andNode:
                    int leftIdAnd = InsertNodeInternal(andNode.Left);
                    int rightIdAnd = InsertNodeInternal(andNode.Right);

                    Entry<T> leftEntryAnd = _nodes[leftIdAnd];
                    Entry<T> rightEntryAnd = _nodes[rightIdAnd];

                    var childrenOrderAnd = new List<int>();
                    if (leftEntryAnd.Cost > rightEntryAnd.Cost)
                    {
                        childrenOrderAnd.Add(rightIdAnd);
                        childrenOrderAnd.Add(leftIdAnd);
                    }
                    else
                    {
                        childrenOrderAnd.Add(leftIdAnd);
                        childrenOrderAnd.Add(rightIdAnd);
                    }

                    RNode rnodeAnd = new RNode
                    {
                        Level = 1 + Math.Max(leftEntryAnd.Node!.Level, rightEntryAnd.Node!.Level),
                        Operator = Operator.And,
                        Children = childrenOrderAnd
                    };
                    newNodeIndex = AddNewNode(expressionId, new ATreeNode.R(rnodeAnd), subscriptionId, cost, true);
                    ChooseAccessChild(leftIdAnd, rightIdAnd, newNodeIndex);
                    break;
                case OptimizedNode.OptimizedOrNode orNode:
                    int leftIdOr = InsertNodeInternal(orNode.Left);
                    int rightIdOr = InsertNodeInternal(orNode.Right);

                    Entry<T> leftEntryOr = _nodes[leftIdOr];
                    Entry<T> rightEntryOr = _nodes[rightIdOr];

                    var childrenOrderOr = new List<int>();
                    if (leftEntryOr.Cost > rightEntryOr.Cost)
                    {
                        childrenOrderOr.Add(rightIdOr);
                        childrenOrderOr.Add(leftIdOr);
                    }
                    else
                    {
                        childrenOrderOr.Add(leftIdOr);
                        childrenOrderOr.Add(rightIdOr);
                    }

                    RNode rnodeOr = new RNode
                    {
                        Level = 1 + Math.Max(leftEntryOr.Node!.Level, rightEntryOr.Node!.Level),
                        Operator = Operator.Or,
                        Children = childrenOrderOr
                    };
                    newNodeIndex = AddNewNode(expressionId, new ATreeNode.R(rnodeOr), subscriptionId, cost, true);
                    AddParent(leftIdOr, newNodeIndex);
                    AddParent(rightIdOr, newNodeIndex);
                    AddPredicate(leftIdOr);
                    AddPredicate(rightIdOr);
                    break;
                case OptimizedNode.OptimizedValueNode valueNode:
                    LNode lnode = new LNode
                    {
                        Level = 1,
                        Parents = new List<int>(),
                        Predicate = valueNode.NodeValue
                    };
                    newNodeIndex = AddNewNode(expressionId, new ATreeNode.L(lnode), subscriptionId, cost, true);
                    if (!_predicates.Contains(newNodeIndex)) _predicates.Add(newNodeIndex);
                    break;
                default:
                    throw new InvalidOperationException("Unknown OptimizedNode type in InsertRoot factory: " + rootNode.GetType().FullName);
            }
            _roots.Add(newNodeIndex);
            _nodesByIds[subscriptionId] = newNodeIndex;
            UpdateMaxLevel(_nodes[newNodeIndex].Node!.Level);
        }

        private int InsertNodeInternal(OptimizedNode node)
        {
            ExpressionId exprId = node.Id;

            if (_expressionToNode.TryGetValue(exprId, out int resultingNodeId))
            {
                return resultingNodeId;
            }

            ulong cost = node.Cost;
            int newNodeId;

            switch (node)
            {
                case OptimizedNode.OptimizedAndNode andNode:
                    int leftIdAnd = InsertNodeInternal(andNode.Left);
                    int rightIdAnd = InsertNodeInternal(andNode.Right);

                    Entry<T> leftEntryAnd = _nodes[leftIdAnd];
                    Entry<T> rightEntryAnd = _nodes[rightIdAnd];

                    var childrenOrderAnd = new List<int>();
                    if (leftEntryAnd.Cost > rightEntryAnd.Cost)
                    {
                        childrenOrderAnd.Add(rightIdAnd);
                        childrenOrderAnd.Add(leftIdAnd);
                    }
                    else
                    {
                        childrenOrderAnd.Add(leftIdAnd);
                        childrenOrderAnd.Add(rightIdAnd);
                    }

                    INode inodeAnd = new INode
                    {
                        Parents = new List<int>(),
                        Level = 1 + Math.Max(leftEntryAnd.Node!.Level, rightEntryAnd.Node!.Level),
                        Operator = Operator.And,
                        Children = childrenOrderAnd
                    };
                    newNodeId = AddNewNode(exprId, new ATreeNode.I(inodeAnd), default(T), cost, false);
                    ChooseAccessChild(leftIdAnd, rightIdAnd, newNodeId);
                    break;
                case OptimizedNode.OptimizedOrNode orNode:
                    int leftIdOr = InsertNodeInternal(orNode.Left);
                    int rightIdOr = InsertNodeInternal(orNode.Right);

                    Entry<T> leftEntryOr = _nodes[leftIdOr];
                    Entry<T> rightEntryOr = _nodes[rightIdOr];

                    var childrenOrderOr = new List<int>();
                    if (leftEntryOr.Cost > rightEntryOr.Cost)
                    {
                        childrenOrderOr.Add(rightIdOr);
                        childrenOrderOr.Add(leftIdOr);
                    }
                    else
                    {
                        childrenOrderOr.Add(leftIdOr);
                        childrenOrderOr.Add(rightIdOr);
                    }

                    INode inodeOr = new INode
                    {
                        Parents = new List<int>(),
                        Level = 1 + Math.Max(leftEntryOr.Node!.Level, rightEntryOr.Node!.Level),
                        Operator = Operator.Or,
                        Children = childrenOrderOr
                    };
                    newNodeId = AddNewNode(exprId, new ATreeNode.I(inodeOr), default(T), cost, false);
                    AddParent(leftIdOr, newNodeId);
                    AddParent(rightIdOr, newNodeId);
                    AddPredicate(leftIdOr);
                    AddPredicate(rightIdOr);
                    break;
                case OptimizedNode.OptimizedValueNode valueNode:
                    LNode lnode = new LNode
                    {
                        Level = 1,
                        Parents = new List<int>(),
                        Predicate = valueNode.NodeValue
                    };
                    newNodeId = AddNewNode(exprId, new ATreeNode.L(lnode), default(T), cost, false);
                    break;
                default:
                    throw new InvalidOperationException("Unknown OptimizedNode type in InsertNodeInternal factory: " + node.GetType().FullName);
            }
            UpdateMaxLevel(_nodes[newNodeId].Node!.Level);
            return newNodeId;
        }

        private int AddNewNode(ExpressionId expressionId, ATreeNode node, T? subscriptionId, ulong cost, bool addSubscriptionAsUse)
        {
            var entry = _entryPool.Get();
            entry.Initialize(expressionId, node, cost, _strings);

            if (addSubscriptionAsUse && subscriptionId != null && !subscriptionId.Equals(default(T)))
            {
                entry.AddSubscription(subscriptionId);
            }

            int nodeId = _nodes.Count;
            _nodes.Add(entry);
            _expressionToNode[expressionId] = nodeId;
            return nodeId;
        }

        private void AddSubscriptionId(T subscriptionId, int nodeId)
        {
            if (nodeId < 0 || nodeId >= _nodes.Count) return;
            Entry<T> entry = _nodes[nodeId];
            entry.AddSubscription(subscriptionId);
            _nodesByIds[subscriptionId] = nodeId;
        }

        private void IncrementUseCount(int nodeId)
        {
            if (nodeId < 0 || nodeId >= _nodes.Count) return;
            Entry<T> entry = _nodes[nodeId];
            entry.IncrementUseCount();
        }

        private void AddParent(int childNodeId, int parentNodeId)
        {
            if (childNodeId < 0 || childNodeId >= _nodes.Count || parentNodeId < 0 || parentNodeId >= _nodes.Count) return;
            Entry<T> childEntry = _nodes[childNodeId];
            childEntry.Node!.AddParent(parentNodeId);
        }

        private void AddPredicate(int nodeId)
        {
            if (nodeId < 0 || nodeId >= _nodes.Count) return;
            Entry<T> entry = _nodes[nodeId];
            if (entry.IsLeaf)
            {
                if (!_predicates.Contains(nodeId)) _predicates.Add(nodeId);
            }
            else
            {
                if (entry.Node == null) return;
                foreach (var childId in entry.Node.GetChildren())
                {
                    AddPredicate(childId);
                }
            }
        }

        private void ChooseAccessChild(int leftId, int rightId, int parentId)
        {
            if (leftId < 0 || leftId >= _nodes.Count || rightId < 0 || rightId >= _nodes.Count) return;

            Entry<T> leftEntry = _nodes[leftId];
            Entry<T> rightEntry = _nodes[rightId];

            AddParent(leftId, parentId);
            AddParent(rightId, parentId);

            int accessorId = (leftEntry.Cost < rightEntry.Cost) ? leftId : rightId;
            AddPredicate(accessorId);
        }

        private void UpdateMaxLevel(int newLevelCandidate)
        {
            if (newLevelCandidate > _maxLevel)
            {
                _maxLevel = newLevelCandidate;
            }
        }

        public EventBuilder MakeEvent()
        {
            return new EventBuilder(_attributes, _strings);
        }

        public Report<T> Search(Event evt)
        {
            var results = new EvaluationResult<T>(_nodes.Count);
            var matches = new List<T>();

            if (_maxLevel == 0)
            {
                return new Report<T>(matches);
            }

            var stackCount = _maxLevel > 1 ? _maxLevel - 1 : 0;
            var stacks = new List<Stack<(int, Entry<T>)>>(stackCount);
            for (int i = 0; i < stackCount; i++)
            {
                stacks.Add(new Stack<(int, Entry<T>)>());
            }

            ProcessPredicates(evt, results, matches, stacks);

            for (int i = 0; i < stacks.Count; i++)
            {
                var stack = stacks[i];
                while (stack.Count > 0)
                {
                    var (nodeId, entry) = stack.Pop();
                    if (results.IsEvaluated(nodeId))
                    {
                        continue;
                    }

                    var result = EvaluateNode(nodeId, results, evt, matches);

                    if (result == true)
                    {
                        AddMatches(_nodes[nodeId], matches);
                    }

                    if (entry.IsRoot)
                    {
                        continue;
                    }

                    foreach (var parentId in entry.ParentsInternal)
                    {
                        var parentEntry = _nodes[parentId];
                        var isEvaluated = results.IsEvaluated(parentId);

                        if (!isEvaluated && parentEntry.Node != null && parentEntry.Node.GetOperator() == Operator.And && result == false)
                        {
                            results.SetResult(parentId, false);
                            continue;
                        }

                        if (!isEvaluated)
                        {
                            int parentLevel = GetLevel(parentEntry);
                            if (parentLevel > 1)
                            {
                                stacks[parentLevel - 2].Push((parentId, parentEntry));
                            }
                        }
                    }
                }
            }

            return new Report<T>(matches);
        }

        private void ProcessPredicates(Event evt, EvaluationResult<T> results, List<T> matches, List<Stack<(int, Entry<T>)>> stacks)
        {
            foreach (int nodeId in _predicates)
            {
                var entry = _nodes[nodeId];
                if (entry.Node is ATreeNode.L lNode)
                {
                    bool? result = lNode.Item.Predicate!.Evaluate(evt);
                    results.SetResult(nodeId, result);

                    if (result == true)
                    {
                        AddMatches(entry, matches);
                    }

                    if (entry.IsRoot)
                    {
                        continue;
                    }

                    foreach (var parentId in entry.ParentsInternal)
                    {
                        var parentEntry = _nodes[parentId];
                        var isEvaluated = results.IsEvaluated(parentId);

                        if (!isEvaluated && parentEntry.Node != null && parentEntry.Node.GetOperator() == Operator.And && result == false)
                        {
                            results.SetResult(parentId, false);
                            continue;
                        }

                        if (!isEvaluated)
                        {
                            int parentLevel = GetLevel(parentEntry);
                            if (parentLevel > 1)
                            {
                                stacks[parentLevel - 2].Push((parentId, parentEntry));
                            }
                        }
                    }
                }
            }
        }

        private bool? EvaluateNode(int nodeId, EvaluationResult<T> results, Event evt, List<T> matches)
        {
            var op = _nodes[nodeId].Node!.GetOperator();
            if (op == Operator.And)
            {
                return EvaluateAnd(nodeId, _nodes[nodeId].Node!.GetChildren(), results, evt, matches);
            }
            else  // Operator.Or
            {
                return EvaluateOr(nodeId, _nodes[nodeId].Node!.GetChildren(), results, evt, matches);
            }
        }

        private bool? LazyEvaluate(int nodeId, EvaluationResult<T> results, Event evt, List<T> matches)
        {
            if (results.IsEvaluated(nodeId))
            {
                return results.GetResult(nodeId);
            }

            var node = _nodes[nodeId];
            bool? result;
            if (node.IsLeaf)
            {
                if (node.Node is ATreeNode.L lNode)
                {
                    result = lNode.Item.Predicate!.Evaluate(evt);
                    results.SetResult(nodeId, result);
                }
                else
                {
                    result = null; // Should not happen
                }
            }
            else
            {
                result = EvaluateNode(nodeId, results, evt, matches);
            }

            if (result == true)
            {
                AddMatches(node, matches);
            }
            return result;
        }

        private bool? EvaluateAnd(int nodeId, IEnumerable<int> children, EvaluationResult<T> results, Event evt, List<T> matches)
        {
            bool? acc = true;
            foreach (var childId in children)
            {
                var result = LazyEvaluate(childId, results, evt, matches);
                if (result == false)
                {
                    results.SetResult(nodeId, false);
                    return false; // Short-circuit
                }
                if (result == null)
                {
                    acc = null;
                }
            }
            results.SetResult(nodeId, acc);
            return acc;
        }

        private bool? EvaluateOr(int nodeId, IEnumerable<int> children, EvaluationResult<T> results, Event evt, List<T> matches)
        {
            bool? acc = false;
            bool hasUnevaluated = false;
            foreach (var childId in children)
            {
                var result = LazyEvaluate(childId, results, evt, matches);
                if (result == true)
                {
                    results.SetResult(nodeId, true);
                    return true; // Short-circuit
                }
                if (result == null)
                {
                    hasUnevaluated = true;
                }
            }
            var finalResult = hasUnevaluated ? null : acc;
            results.SetResult(nodeId, finalResult);
            return finalResult;
        }

        private int GetLevel(Entry<T> entry)
        {
            return entry.Node!.Level;
        }

        private void AddMatches(Entry<T> entry, List<T> matches)
        {
            if (entry.SubscriptionIds != null)
            {
                matches.AddRange(entry.SubscriptionIds);
            }
        }

        public List<T> MatchEvent(Event evt)
        {
            Report<T> report = Search(evt);
            return report.SubscriptionIds.ToList();
        }

        public void RemoveRule(T subscriptionId)
        {
            if (_nodesByIds.TryGetValue(subscriptionId, out int nodeId))
            {
                DeleteNode(subscriptionId, nodeId);
            }
        }

        private void DeleteNode(T subscriptionId, int nodeId)
        {
            var children = DecrementUseCount(subscriptionId, nodeId);
            if (children != null)
            {
                foreach (var childId in children)
                {
                    DeleteNode(subscriptionId, childId);
                }
            }
        }

        private List<int>? DecrementUseCount(T subscriptionId, int nodeId)
        {
            if (nodeId < 0 || nodeId >= _nodes.Count) return null;

            Entry<T> entry = _nodes[nodeId];
            entry.RemoveSubscription(subscriptionId);
            _nodesByIds.Remove(subscriptionId);

            List<int>? children = null;
            if (entry.UseCount == 0)
            {
                if (!entry.IsLeaf && entry.Node != null)
                {
                    children = entry.Node.GetChildren().ToList();
                }

                _expressionToNode.Remove(entry.Id);
                _roots.Remove(nodeId);
                _predicates.Remove(nodeId);

                // In a real scenario with a slab-like allocator, we would remove the node.
                // With a List, we nullify it to keep indices stable, but this is not ideal.
                // For now, we'll just leave it, but a better implementation would use a more suitable data structure.

                UpdateMaxLevel();
            }
            return children;
        }

        private void UpdateMaxLevel()
        {
            if (_roots.Count == 0)
            {
                _maxLevel = 0;
                return;
            }
            _maxLevel = _roots.Select(id => _nodes[id].Node!.Level).Max();
        }

        private static string EscapeForDot(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input
                .Replace("\\", "\\\\")   // Escape backslashes
                .Replace("\"", "\\\"")   // Escape double quotes
                .Replace("<", "\\<")     // Escape angle brackets
                .Replace(">", "\\>")     // Escape angle brackets
                .Replace("{", "\\{")     // Escape curly braces
                .Replace("}", "\\}")     // Escape curly braces
                .Replace("|", "\\|")     // Escape field separators in records
                .Replace("\n", "\\n");   // Preserve line breaks
        }

        public string ToGraphviz()
        {
            var builder = new StringBuilder();
            builder.AppendLine("digograph {");
            builder.AppendLine("    rankdir = TB;");
            builder.AppendLine("    node [shape = record];");

            var relations = new List<string>();
            var levels = new Dictionary<int, List<(int, string)>>();

            for (int id = 0; id < _nodes.Count; id++)
            {
                var entry = _nodes[id];
                if (entry == null || entry.UseCount == 0 || entry.Node == null) continue;

                string nodeString = "";
                string subscriptions = string.Join(", ", entry.SubscriptionIds.Select(s => EscapeForDot(s?.ToString() ?? "")));
                
                switch (entry.Node)
                {
                    case ATreeNode.L lNode:
                        string predicateString = "";
                        if (lNode.Item.Predicate != null)
                        {
                            var p = lNode.Item.Predicate;
                            var attrDef = _attributes.GetById(p.Attribute);
                            string predicateDetails = "";

                            switch (p.Kind)
                            {
                                case PredicateKind.Equality eq:
                                    string val;
                                    if (eq.ValueToCompare is PrimitiveLiteral.String s)
                                    {
                                        val = _strings.GetString(s.Value);
                                    }
                                    else
                                    {
                                        val = eq.ValueToCompare?.ToString() ?? "";
                                    }
                                    predicateDetails = $"{attrDef.Name} ({attrDef.Kind}) {EqualityOperatorExtensions.Display(eq.Operator)} {val}";
                                    break;
                                case PredicateKind.Comparison comp:
                                    predicateDetails = $"{attrDef.Name} ({attrDef.Kind}) {ComparisonOperatorExtensions.Display(comp.Operator)} {comp.ValueToCompare}";
                                    break;
                                case PredicateKind.Set set:
                                    predicateDetails = $"{attrDef.Name} ({attrDef.Kind}) {SetOperatorExtensions.Display(set.Operator)} {set.Haystack}";
                                    break;
                                case PredicateKind.List list:
                                    predicateDetails = $"{attrDef.Name} ({attrDef.Kind}) {ListOperatorExtensions.Display(list.Operator)} {list.ListToCompare}";
                                    break;
                                case PredicateKind.Null nullPred:
                                    predicateDetails = $"{attrDef.Name} ({attrDef.Kind}) {NullOperatorExtensions.Display(nullPred.Operator)}";
                                    break;
                                case PredicateKind.Variable:
                                    predicateDetails = $"{attrDef.Name} ({attrDef.Kind}) is true";
                                    break;
                                case PredicateKind.NegatedVariable:
                                    predicateDetails = $"{attrDef.Name} ({attrDef.Kind}) is false";
                                    break;
                                default:
                                    predicateDetails = p.ToString();
                                    break;
                            }
                            predicateString = EscapeForDot(predicateDetails);
                        }
                        nodeString = $@"node_{id} [label = ""{{{id} | level: {entry.Node.Level} | {predicateString} | subscriptions: {subscriptions} ({entry.UseCount}) | l-node}}"", style = ""rounded""];";
                        foreach (var parentId in lNode.Item.Parents)
                        {
                            relations.Add($"node_{id} -> node_{parentId};");
                        }
                        break;
                    case ATreeNode.I iNode:
                        nodeString = $@"node_{id} [label = ""{{{id} | level: {entry.Node.Level} | {iNode.Item.Operator} | subscriptions: {subscriptions} ({entry.UseCount}) | i-node}}""];";
                        foreach (var parentId in iNode.Item.Parents)
                        {
                            relations.Add($"node_{id} -> node_{parentId};");
                        }
                        foreach (var childId in iNode.Item.Children)
                        {
                            relations.Add($"node_{id} -> node_{childId};");
                        }
                        break;
                    case ATreeNode.R rNode:
                        nodeString = $@"node_{id} [label = ""{{{id} | level: {entry.Node.Level} | {rNode.Item.Operator} | subscriptions: {subscriptions} ({entry.UseCount}) | r-node}}""];";
                        foreach (var childId in rNode.Item.Children)
                        {
                            relations.Add($"node_{id} -> node_{childId};");
                        }
                        break;
                }
                if (entry.Node.Level > 0)
                {
                    if (!levels.ContainsKey(entry.Node.Level))
                    {
                        levels[entry.Node.Level] = new List<(int, string)>();
                    }
                    levels[entry.Node.Level].Add((id, nodeString));
                }
            }

            builder.AppendLine();
            builder.AppendLine("// nodes");
            foreach (var level in levels.OrderByDescending(kv => kv.Key))
            {
                foreach (var (_, node) in level.Value)
                {
                    builder.AppendLine(node);
                }
                builder.Append("{rank = same; ");
                foreach (var (id, _) in level.Value)
                {
                    builder.Append($"node_{id}; ");
                }
                builder.AppendLine("};");
            }

            builder.AppendLine();
            builder.AppendLine("// edges");
            foreach (var relation in relations)
            {
                builder.AppendLine(relation);
            }

            builder.AppendLine("}");
            return builder.ToString();
        }

        public void DumpTreeToDotFile(string path)
        {
            System.IO.File.WriteAllText(path, ToGraphviz());
        }
    }

    public class Report<T>
    {
        public IReadOnlyList<T> SubscriptionIds { get; }

        public Report(IReadOnlyList<T> subscriptionIds)
        {
            SubscriptionIds = subscriptionIds;
        }
    }

    public abstract class ATreeNode
    {
        public abstract int Level { get; }
        public abstract void AddParent(int parentId);
        public abstract Operator GetOperator();
        public abstract IEnumerable<int> GetChildren();

        public class L : ATreeNode
        {
            public LNode Item { get; }
            public L(LNode item) { Item = item; }
            public override int Level => Item.Level;
            public override void AddParent(int parentId) => Item.Parents.Add(parentId);
            public override Operator GetOperator() => throw new NotSupportedException("L-nodes do not have an operator.");
            public override IEnumerable<int> GetChildren() => Enumerable.Empty<int>();
        }

        public class I : ATreeNode
        {
            public INode Item { get; }
            public I(INode item) { Item = item; }
            public override int Level => Item.Level;
            public override void AddParent(int parentId) => Item.Parents.Add(parentId);
            public override Operator GetOperator() => Item.Operator;
            public override IEnumerable<int> GetChildren() => Item.Children;
        }

        public class R : ATreeNode
        {
            public RNode Item { get; }
            public R(RNode item) { Item = item; }
            public override int Level => Item.Level;
            public override void AddParent(int parentId) { }
            public override Operator GetOperator() => Item.Operator;
            public override IEnumerable<int> GetChildren() => Item.Children;
        }
    }

    public class LNode
    {
        public int Level { get; set; }
        public List<int> Parents { get; set; } = new List<int>();
        public Predicate? Predicate { get; set; }
    }

    public class INode
    {
        public List<int> Parents { get; set; } = new List<int>();
        public int Level { get; set; }
        public Operator Operator { get; set; }
        public List<int> Children { get; set; } = new List<int>();
    }

    public class RNode
    {
        public int Level { get; set; }
        public Operator Operator { get; set; }
        public List<int> Children { get; set; } = new List<int>();
    }

    public class Entry<T> where T : IEquatable<T>
    {
        public ExpressionId Id { get; private set; }
        public ATreeNode? Node { get; private set; }
        public ulong Cost { get; private set; }
        public List<T> SubscriptionIds { get; } = new List<T>();
        public int UseCount { get; private set; }

        public bool IsRoot => Node is ATreeNode.R;
        public bool IsLeaf => Node is ATreeNode.L;

        public IEnumerable<int> ParentsInternal
        {
            get
            {
                return Node switch
                {
                    ATreeNode.L l => l.Item.Parents,
                    ATreeNode.I i => i.Item.Parents,
                    _ => Enumerable.Empty<int>()
                };
            }
        }

        public void Initialize(ExpressionId id, ATreeNode node, ulong cost, StringTable strings)
        {
            Id = id;
            Node = node;
            Cost = cost;
            SubscriptionIds.Clear();
            UseCount = 1;
        }

        public void AddSubscription(T subscriptionId)
        {
            if (!SubscriptionIds.Contains(subscriptionId))
            {
                SubscriptionIds.Add(subscriptionId);
            }
            IncrementUseCount();
        }

        public void IncrementUseCount()
        {
            UseCount++;
        }

        public void DecrementUseCount()
        {
            UseCount--;
        }

        public void RemoveSubscription(T subscriptionId)
        {
            SubscriptionIds.Remove(subscriptionId);
            DecrementUseCount();
        }
    }
}