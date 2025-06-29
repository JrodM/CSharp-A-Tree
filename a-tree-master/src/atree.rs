use crate::{
    ast::*,
    error::ATreeError,
    evaluation::EvaluationResult,
    events::{AttributeDefinition, AttributeTable, Event, EventBuilder},
    parser,
    predicates::Predicate,
    strings::StringTable,
};
use slab::Slab;
use std::{collections::HashMap, fmt::Debug, hash::Hash};

type NodeId = usize;
type ExpressionId = u64;

/// The A-Tree data structure as described by the paper
///
/// See the [module documentation] for more details.
///
/// [module documentation]: index.html
#[derive(Clone, Debug)]
pub struct ATree<T> {
    nodes: Slab<Entry<T>>,
    strings: StringTable,
    attributes: AttributeTable,
    roots: Vec<NodeId>,
    max_level: usize,
    predicates: Vec<NodeId>,
    expression_to_node: HashMap<ExpressionId, NodeId>,
    nodes_by_ids: HashMap<T, NodeId>,
}

impl<T: Eq + Hash + Clone + Debug> ATree<T> {
    const DEFAULT_PREDICATES: usize = 1000;
    const DEFAULT_NODES: usize = 2000;
    const DEFAULT_ROOTS: usize = 50;

    /// Create a new [`ATree`] with the attributes that can be used by the inserted arbitrary
    /// boolean expressions along with their types.
    ///
    /// # Examples
    ///
    /// ```rust
    /// use a_tree::{ATree, AttributeDefinition};
    ///
    /// let definitions = [
    ///     AttributeDefinition::boolean("private"),
    ///     AttributeDefinition::integer("exchange_id")
    /// ];
    /// let result = ATree::<u64>::new(&definitions);
    /// assert!(result.is_ok());
    /// ```
    ///
    /// Duplicate attributes are not allowed and the [`ATree::new()`] function will return an error if there are some:
    ///
    /// ```rust
    /// use a_tree::{ATree, AttributeDefinition};
    ///
    /// let definitions = [
    ///     AttributeDefinition::boolean("private"),
    ///     AttributeDefinition::boolean("private"),
    /// ];
    /// let result = ATree::<u64>::new(&definitions);
    /// assert!(result.is_err());
    /// ```
    pub fn new(definitions: &[AttributeDefinition]) -> Result<Self, ATreeError> {
        let attributes = AttributeTable::new(definitions).map_err(ATreeError::Event)?;
        let strings = StringTable::new();
        Ok(Self {
            attributes,
            strings,
            max_level: 1,
            roots: Vec::with_capacity(Self::DEFAULT_ROOTS),
            predicates: Vec::with_capacity(Self::DEFAULT_PREDICATES),
            nodes: Slab::with_capacity(Self::DEFAULT_NODES),
            expression_to_node: HashMap::new(),
            nodes_by_ids: HashMap::new(),
        })
    }

    /// Insert an arbitrary boolean expression inside the [`ATree`].
    ///
    /// # Examples
    ///
    /// ```rust
    /// use a_tree::{ATree, AttributeDefinition};
    ///
    /// let definitions = [
    ///     AttributeDefinition::boolean("private"),
    ///     AttributeDefinition::integer("exchange_id")
    /// ];
    /// let mut atree = ATree::new(&definitions).unwrap();
    /// assert!(atree.insert(&1u64, "exchange_id = 5").is_ok());
    /// assert!(atree.insert(&2u64, "private").is_ok());
    /// ```
    #[inline]
    pub fn insert<'a>(
        &'a mut self,
        subscription_id: &T,
        expression: &'a str,
    ) -> Result<(), ATreeError<'a>> {
        let ast = parser::parse(expression, &self.attributes, &mut self.strings)
            .map_err(ATreeError::ParseError)?;
        let ast = ast.optimize();
        self.insert_root(subscription_id, ast);
        Ok(())
    }

    fn insert_root(&mut self, subscription_id: &T, root: OptimizedNode) {
        let expression_id = root.id();
        if let Some(node_id) = self.expression_to_node.get(&expression_id) {
            add_subscription_id(
                subscription_id,
                *node_id,
                &mut self.nodes,
                &mut self.nodes_by_ids,
            );
            increment_use_count(*node_id, &mut self.nodes);
            return;
        }

        let is_and = matches!(&root, OptimizedNode::And(_, _));
        let cost = root.cost();
        let node_id = match root {
            OptimizedNode::And(left, right) | OptimizedNode::Or(left, right) => {
                let left_id = self.insert_node(*left);
                let right_id = self.insert_node(*right);
                let left_entry = &self.nodes[left_id];
                let right_entry = &self.nodes[right_id];
                let rnode = ATreeNode::RNode(RNode {
                    level: 1 + std::cmp::max(left_entry.node.level(), right_entry.node.level()),
                    operator: if is_and { Operator::And } else { Operator::Or },
                    children: if left_entry.cost > right_entry.cost {
                        vec![right_id, left_id]
                    } else {
                        vec![left_id, right_id]
                    },
                });
                let node_id = insert_node(
                    &mut self.expression_to_node,
                    &mut self.nodes,
                    &expression_id,
                    rnode,
                    Some(subscription_id.clone()),
                    cost,
                );
                if is_and {
                    choose_access_child(
                        left_id,
                        right_id,
                        node_id,
                        &mut self.nodes,
                        &mut self.predicates,
                    );
                } else {
                    add_parent(&mut self.nodes[left_id], node_id);
                    add_parent(&mut self.nodes[right_id], node_id);
                    add_predicate(left_id, &self.nodes, &mut self.predicates);
                    add_predicate(right_id, &self.nodes, &mut self.predicates);
                }
                node_id
            }
            OptimizedNode::Value(value) => {
                let lnode = ATreeNode::lnode(&value);
                let node_id = insert_node(
                    &mut self.expression_to_node,
                    &mut self.nodes,
                    &expression_id,
                    lnode,
                    Some(subscription_id.clone()),
                    cost,
                );
                self.predicates.push(node_id);
                node_id
            }
        };
        self.nodes_by_ids.insert(subscription_id.clone(), node_id);
        self.roots.push(node_id);
        self.max_level = get_max_level(&self.roots, &self.nodes);
    }

    fn insert_node(&mut self, node: OptimizedNode) -> NodeId {
        let expression_id = node.id();
        if let Some(node_id) = self.expression_to_node.get(&expression_id) {
            change_rnode_to_inode(*node_id, &mut self.nodes);
            increment_use_count(*node_id, &mut self.nodes);
            return *node_id;
        }

        let is_and = matches!(node, OptimizedNode::And(_, _));
        let cost = node.cost();
        match node {
            OptimizedNode::And(left, right) | OptimizedNode::Or(left, right) => {
                let left_id = self.insert_node(*left);
                let right_id = self.insert_node(*right);
                let left_entry = &self.nodes[left_id];
                let right_entry = &self.nodes[right_id];
                let inode = INode {
                    parents: vec![],
                    level: 1 + std::cmp::max(left_entry.node.level(), right_entry.node.level()),
                    operator: if is_and { Operator::And } else { Operator::Or },
                    children: if left_entry.cost > right_entry.cost {
                        vec![right_id, left_id]
                    } else {
                        vec![left_id, right_id]
                    },
                };
                let inode = ATreeNode::INode(inode);
                let node_id = insert_node(
                    &mut self.expression_to_node,
                    &mut self.nodes,
                    &expression_id,
                    inode,
                    None,
                    cost,
                );
                if is_and {
                    choose_access_child(
                        left_id,
                        right_id,
                        node_id,
                        &mut self.nodes,
                        &mut self.predicates,
                    );
                } else {
                    add_parent(&mut self.nodes[left_id], node_id);
                    add_parent(&mut self.nodes[right_id], node_id);
                    add_predicate(left_id, &self.nodes, &mut self.predicates);
                    add_predicate(right_id, &self.nodes, &mut self.predicates);
                }
                node_id
            }
            OptimizedNode::Value(node) => {
                let lnode = ATreeNode::lnode(&node);
                insert_node(
                    &mut self.expression_to_node,
                    &mut self.nodes,
                    &expression_id,
                    lnode,
                    None,
                    cost,
                )
            }
        }
    }

    /// Create a new [`EventBuilder`] to be able to generate an [`Event`] that will be usable for
    /// finding the matching arbitrary boolean expressions inside the [`ATree`] via the
    /// [`ATree::search()`] function.
    #[inline]
    pub fn make_event(&self) -> EventBuilder {
        EventBuilder::new(&self.attributes, &self.strings)
    }

    /// Search the [`ATree`] for arbitrary boolean expressions that match the [`Event`].
    pub fn search(&self, event: &Event) -> Result<Report<T>, ATreeError> {
        let mut results = EvaluationResult::new(self.nodes.len());
        let mut matches = Vec::with_capacity(50);

        // Since the predicates will already be evaluated and their parents will be put into the
        // queues, then there is no need to keep a queue for them.
        let mut queues = vec![Vec::with_capacity(50); self.max_level - 1];
        process_predicates(
            &self.predicates,
            &self.nodes,
            event,
            &mut matches,
            &mut results,
            &mut queues,
        );

        for level in 0..queues.len() {
            while let Some((node_id, node)) = queues[level].pop() {
                if results.is_evaluated(node_id) {
                    continue;
                }

                let result = evaluate_node(
                    node_id,
                    event,
                    node,
                    &self.nodes,
                    &mut results,
                    &mut matches,
                );
                add_matches(result, node, &mut matches);

                if node.is_root() {
                    continue;
                }

                for parent_id in node.parents() {
                    let entry = &self.nodes[*parent_id];
                    let is_evaluated = results.is_evaluated(*parent_id);
                    if !is_evaluated
                        && matches!(entry.operator(), Operator::And)
                        && !result.unwrap_or(true)
                    {
                        results.set_result(*parent_id, Some(false));
                        continue;
                    }

                    if !is_evaluated {
                        queues[entry.level() - 2].push((*parent_id, entry));
                    }
                }
            }
        }

        Ok(Report::new(matches))
    }

    #[inline]
    /// Delete the specified expression
    pub fn delete(&mut self, subscription_id: &T) {
        if let Some(node_id) = self.nodes_by_ids.get(subscription_id) {
            self.delete_node(subscription_id, *node_id);
        }
    }

    #[inline]
    fn delete_node(&mut self, subscription_id: &T, node_id: NodeId) {
        let children = decrement_use_count(
            subscription_id,
            node_id,
            &mut self.nodes,
            &mut self.expression_to_node,
            &mut self.roots,
            &mut self.predicates,
            &mut self.nodes_by_ids,
            &mut self.max_level,
        );

        if let Some(children) = children {
            for child in children {
                self.delete_node(subscription_id, child);
            }
        }
    }

    /// Export the [`ATree`] to the Graphviz format.
    pub fn to_graphviz(&self) -> String {
        const DEFAULT_CAPACITY: usize = 100_000;
        let mut builder = String::with_capacity(DEFAULT_CAPACITY);
        builder.push_str("digraph {\n");
        builder.push_str("rankdir = TB;\n");
        builder.push_str(r#"node [shape = "record"];"#);
        builder.push('\n');
        let mut relations = Vec::with_capacity(DEFAULT_CAPACITY);
        let mut levels = vec![vec![]; self.max_level];
        for (id, entry) in &self.nodes {
            match &entry.node {
                ATreeNode::LNode(LNode {
                    parents, predicate, ..
                }) => {
                    let node = format!(
                        r#"node_{id} [label = "{{{id} | level: {} | {predicate} | subscriptions: {:?} | l-node}}", style = "rounded"];"#,
                        entry.level(),
                        entry.subscription_ids
                    );
                    levels[entry.level() - 1].push((id, node));

                    for parent_id in parents {
                        relations.push(format!("node_{id} -> node_{parent_id};"));
                    }
                }
                ATreeNode::INode(INode {
                    children,
                    parents,
                    operator,
                    ..
                }) => {
                    let node = format!(
                        r#"node_{id} [label = "{{{id} | level: {} | {operator:#?} | subscriptions: {:?} | i-node}}"];"#,
                        entry.level(),
                        entry.subscription_ids
                    );
                    levels[entry.level() - 1].push((id, node));

                    for parent_id in parents {
                        relations.push(format!("node_{id} -> node_{parent_id};"));
                    }

                    for child_id in children {
                        relations.push(format!("node_{id} -> node_{child_id};"));
                    }
                }
                ATreeNode::RNode(RNode {
                    children, operator, ..
                }) => {
                    let node = format!(
                        r#"node_{id} [label = "{{{id} | level: {} | {operator:#?} | subscriptions: {:?} | r-node}}"];"#,
                        entry.level(),
                        entry.subscription_ids
                    );
                    levels[entry.level() - 1].push((id, node));

                    for child_id in children {
                        relations.push(format!("node_{id} -> node_{child_id};"));
                    }
                }
            }
        }

        builder.push_str("\n// nodes\n");
        for entries in levels.into_iter().rev() {
            for (_, node) in entries.iter() {
                builder.push_str(node);
                builder.push('\n');
            }

            builder.push_str("{rank = same; ");
            for (id, _) in entries {
                builder.push_str(&format!("node_{id}; "));
            }
            builder.push_str("};\n");
        }

        builder.push_str("\n// edges\n");
        for relation in relations {
            builder.push_str(&relation);
            builder.push('\n');
        }

        builder.push('}');
        builder
    }
}

#[inline]
#[allow(clippy::too_many_arguments)]
fn decrement_use_count<T: Eq + Hash>(
    subscription_id: &T,
    node_id: NodeId,
    nodes: &mut Slab<Entry<T>>,
    expression_to_node: &mut HashMap<ExpressionId, NodeId>,
    roots: &mut Vec<NodeId>,
    predicates: &mut Vec<NodeId>,
    nodes_by_ids: &mut HashMap<T, NodeId>,
    max_level: &mut usize,
) -> Option<Vec<NodeId>> {
    let node = &mut nodes[node_id];
    node.use_count -= 1;
    let mut children = None;
    node.subscription_ids.retain(|x| *x != *subscription_id);
    nodes_by_ids.remove(subscription_id);
    if node.use_count == 0 {
        if !node.is_leaf() {
            children = Some(node.children().to_vec());
        }
        let expression_id = node.id;
        roots.retain(|x| *x != node_id);
        predicates.retain(|x| *x != node_id);
        *max_level = get_max_level(roots, nodes);
        expression_to_node.remove(&expression_id);
        nodes.remove(node_id);
    }

    children
}

#[inline]
fn insert_node<T>(
    expression_to_node: &mut HashMap<ExpressionId, NodeId>,
    nodes: &mut Slab<Entry<T>>,
    expression_id: &ExpressionId,
    node: ATreeNode,
    subscription_id: Option<T>,
    cost: u64,
) -> NodeId {
    let entry = Entry::new(*expression_id, node, subscription_id, cost);
    let node_id = nodes.insert(entry);
    if expression_to_node.insert(*expression_id, node_id).is_some() {
        unreachable!("{expression_id} is already present; this is a bug");
    }
    node_id
}

#[inline]
fn add_parent<T>(entry: &mut Entry<T>, node_id: NodeId) {
    entry.node.add_parent(node_id);
}

#[inline]
fn add_subscription_id<T: Eq + Hash + Clone>(
    subscription_id: &T,
    node_id: NodeId,
    nodes: &mut Slab<Entry<T>>,
    nodes_by_ids: &mut HashMap<T, NodeId>,
) {
    nodes[node_id]
        .subscription_ids
        .push(subscription_id.clone());
    nodes_by_ids.insert(subscription_id.clone(), node_id);
}

#[inline]
fn increment_use_count<T>(node_id: NodeId, nodes: &mut Slab<Entry<T>>) {
    nodes[node_id].use_count += 1;
}

#[inline]
fn get_max_level<T>(roots: &[NodeId], nodes: &Slab<Entry<T>>) -> usize {
    roots
        .iter()
        .map(|root_id| nodes[*root_id].level())
        .max()
        .unwrap_or(1)
}

#[inline]
fn change_rnode_to_inode<T>(node_id: NodeId, nodes: &mut Slab<Entry<T>>) {
    let entry = &mut nodes[node_id];
    if let ATreeNode::RNode(RNode {
        children,
        level,
        operator,
    }) = &entry.node
    {
        let inode = ATreeNode::INode(INode {
            parents: vec![],
            children: children.to_vec(),
            level: *level,
            operator: operator.clone(),
        });
        entry.node = inode;
    }
}

#[inline]
fn choose_access_child<T>(
    left_id: NodeId,
    right_id: NodeId,
    parent_id: NodeId,
    nodes: &mut Slab<Entry<T>>,
    predicates: &mut Vec<NodeId>,
) {
    let left_entry = &nodes[left_id];
    let right_entry = &nodes[right_id];
    let accessor_id = if left_entry.cost < right_entry.cost {
        left_id
    } else {
        right_id
    };
    add_parent(&mut nodes[accessor_id], parent_id);
    add_predicate(accessor_id, nodes, predicates);
}

#[inline]
fn add_predicate<T>(node_id: NodeId, nodes: &Slab<Entry<T>>, predicates: &mut Vec<NodeId>) {
    let entry = &nodes[node_id];
    if entry.is_leaf() && !predicates.contains(&node_id) {
        predicates.push(node_id);
    }
}

#[inline]
fn process_predicates<'a, T>(
    predicates: &[NodeId],
    nodes: &'a Slab<Entry<T>>,
    event: &Event,
    matches: &mut Vec<&'a T>,
    results: &mut EvaluationResult,
    queues: &mut [Vec<(NodeId, &'a Entry<T>)>],
) {
    for predicate_id in predicates {
        let node = &nodes[*predicate_id];
        // The evaluation is delayed as much as possible; if the predicate has no
        // subscribers and no parents, there is no point in evaluating eagerly and
        // it should only be evaluated if there is a need for it.
        let delay_evaluation = node.subscription_ids.is_empty() && node.parents().is_empty();
        if delay_evaluation || results.is_evaluated(*predicate_id) {
            continue;
        }

        let result = node.evaluate(event);
        results.set_result(*predicate_id, result);
        add_matches(result, node, matches);

        node.parents()
            .iter()
            .map(|parent_id| (*parent_id, &nodes[*parent_id]))
            .for_each(|(parent_id, parent)| {
                if matches!(parent.operator(), Operator::And) && !result.unwrap_or(true) {
                    results.set_result(parent_id, Some(false));
                } else {
                    queues[parent.level() - 2].push((parent_id, parent));
                }
            })
    }
}

#[inline]
fn evaluate_node<'a, T>(
    node_id: NodeId,
    event: &Event,
    node: &'a Entry<T>,
    nodes: &'a Slab<Entry<T>>,
    results: &mut EvaluationResult,
    matches: &mut Vec<&'a T>,
) -> Option<bool> {
    let operator = node.operator();
    let result = match operator {
        Operator::And => evaluate_and(node.children(), event, nodes, results, matches),
        Operator::Or => evaluate_or(node.children(), event, nodes, results, matches),
    };
    results.set_result(node_id, result);
    result
}

#[inline]
fn evaluate_and<'a, T>(
    children: &[NodeId],
    event: &Event,
    nodes: &'a Slab<Entry<T>>,
    results: &mut EvaluationResult,
    matches: &mut Vec<&'a T>,
) -> Option<bool> {
    let mut acc = Some(true);
    for child_id in children {
        let result = lazy_evaluate(*child_id, event, nodes, results, matches);
        match (acc, result) {
            (Some(false), _) => {
                acc = Some(false);
                break;
            }
            (_, Some(false)) => {
                acc = Some(false);
                break;
            }
            (Some(a), Some(b)) => {
                acc = Some(a && b);
            }
            (_, _) => {
                acc = None;
            }
        }
    }
    acc
}

#[inline]
fn evaluate_or<'a, T>(
    children: &[NodeId],
    event: &Event,
    nodes: &'a Slab<Entry<T>>,
    results: &mut EvaluationResult,
    matches: &mut Vec<&'a T>,
) -> Option<bool> {
    let mut acc = Some(false);
    for child_id in children {
        let result = lazy_evaluate(*child_id, event, nodes, results, matches);
        match (acc, result) {
            (Some(true), _) => {
                acc = Some(true);
                break;
            }
            (_, Some(true)) => {
                acc = Some(true);
                break;
            }
            (Some(a), Some(b)) => {
                acc = Some(a || b);
            }
            (_, _) => {
                acc = None;
            }
        }
    }

    acc
}

#[inline]
fn lazy_evaluate<'a, T>(
    node_id: NodeId,
    event: &Event,
    nodes: &'a Slab<Entry<T>>,
    results: &mut EvaluationResult,
    matches: &mut Vec<&'a T>,
) -> Option<bool> {
    if results.is_evaluated(node_id) {
        return results.get_result(node_id);
    }
    let node = &nodes[node_id];
    let result = if node.is_leaf() {
        let result = node.evaluate(event);
        results.set_result(node_id, result);
        result
    } else {
        evaluate_node(node_id, event, node, nodes, results, matches)
    };
    add_matches(result, node, matches);
    result
}

#[inline]
fn add_matches<'a, T>(result: Option<bool>, node: &'a Entry<T>, matches: &mut Vec<&'a T>) {
    if !node.subscription_ids.is_empty() {
        if let Some(true) = result {
            for subscription_id in &node.subscription_ids {
                matches.push(subscription_id);
            }
        }
    }
}

#[derive(Clone, Debug)]
struct Entry<T> {
    id: ExpressionId,
    subscription_ids: Vec<T>,
    node: ATreeNode,
    use_count: usize,
    cost: u64,
}

impl<T> Entry<T> {
    fn new(id: ExpressionId, node: ATreeNode, subscription_id: Option<T>, cost: u64) -> Self {
        Self {
            id,
            node,
            use_count: 1,
            subscription_ids: subscription_id
                .map_or_else(Vec::new, |subscription_id| vec![subscription_id]),
            cost,
        }
    }

    #[inline]
    const fn is_leaf(&self) -> bool {
        matches!(self.node, ATreeNode::LNode(_))
    }

    #[inline]
    const fn is_root(&self) -> bool {
        matches!(self.node, ATreeNode::RNode(_))
    }

    #[inline]
    const fn level(&self) -> usize {
        self.node.level()
    }

    #[inline]
    fn evaluate(&self, event: &Event) -> Option<bool> {
        self.node.evaluate(event)
    }

    #[inline]
    fn operator(&self) -> Operator {
        self.node.operator()
    }

    #[inline]
    fn children(&self) -> &[NodeId] {
        self.node.children()
    }

    #[inline]
    fn parents(&self) -> &[NodeId] {
        self.node.parents()
    }
}

#[derive(Clone, Debug)]
#[allow(clippy::enum_variant_names)]
enum ATreeNode {
    LNode(LNode),
    INode(INode),
    RNode(RNode),
}

impl ATreeNode {
    #[inline]
    fn lnode(predicate: &Predicate) -> Self {
        Self::LNode(LNode {
            level: 1,
            parents: vec![],
            predicate: predicate.clone(),
        })
    }

    #[inline]
    const fn level(&self) -> usize {
        match self {
            Self::RNode(node) => node.level,
            Self::LNode(node) => node.level,
            Self::INode(node) => node.level,
        }
    }

    #[inline]
    fn evaluate(&self, event: &Event) -> Option<bool> {
        match self {
            Self::LNode(node) => node.predicate.evaluate(event),
            node => unreachable!("evaluating {node:?} which is not a predicate; this is a bug."),
        }
    }

    #[inline]
    fn operator(&self) -> Operator {
        match self {
            Self::LNode(_) => {
                unreachable!("trying to get the operator of leaf node; this is a bug");
            }
            Self::RNode(RNode { operator, .. }) | Self::INode(INode { operator, .. }) => {
                operator.clone()
            }
        }
    }

    #[inline]
    fn children(&self) -> &[NodeId] {
        match self {
            Self::INode(INode { children, .. }) | Self::RNode(RNode { children, .. }) => children,
            Self::LNode(_) => unreachable!("cannot get children for l-node; this is a bug"),
        }
    }

    #[inline]
    fn parents(&self) -> &[NodeId] {
        match self {
            Self::INode(INode { parents, .. }) | Self::LNode(LNode { parents, .. }) => parents,
            Self::RNode(_) => unreachable!("cannot get parents for r-node; this is a bug"),
        }
    }

    #[inline]
    fn add_parent(&mut self, parent_id: NodeId) {
        match self {
            ATreeNode::INode(node) => {
                node.parents.push(parent_id);
            }
            ATreeNode::LNode(node) => {
                node.parents.push(parent_id);
            }
            ATreeNode::RNode(node) => {
                unreachable!("trying to insert parents to r-node {node:?} which cannot have any parents; this is a bug");
            }
        }
    }
}

#[derive(Clone, Debug)]
struct LNode {
    parents: Vec<NodeId>,
    level: usize,
    predicate: Predicate,
}

#[derive(Clone, Debug)]
struct INode {
    parents: Vec<NodeId>,
    children: Vec<NodeId>,
    level: usize,
    operator: Operator,
}

#[derive(Clone, Debug)]
struct RNode {
    children: Vec<NodeId>,
    level: usize,
    operator: Operator,
}

#[derive(Debug)]
/// Structure that holds the search results from the [`ATree::search()`] function
pub struct Report<'a, T> {
    matches: Vec<&'a T>,
}

impl<'a, T> Report<'a, T> {
    const fn new(matches: Vec<&'a T>) -> Self {
        Self { matches }
    }

    #[inline]
    /// Get the search matches
    pub fn matches(&self) -> &[&'a T] {
        &self.matches
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const AN_INVALID_BOOLEAN_EXPRESSION: &str = "invalid in (1, 2, 3 and";
    const AN_EXPRESSION: &str = "exchange_id = 1";
    const A_NOT_EXPRESSION: &str = "not private";
    const AN_EXPRESSION_WITH_AND_OPERATORS: &str =
        r#"exchange_id = 1 and deals one of ["deal-1", "deal-2"]"#;
    const AN_EXPRESSION_WITH_OR_OPERATORS: &str =
        r#"exchange_id = 1 or deals one of ["deal-1", "deal-2"]"#;
    const A_COMPLEX_EXPRESSION: &str = r#"exchange_id = 1 and not private and deal_ids one of ["deal-1", "deal-2"] and segment_ids one of [1, 2, 3] and country = 'CA' and city in ['QC'] or country = 'US' and city in ['AZ']"#;
    const ANOTHER_COMPLEX_EXPRESSION: &str = r#"exchange_id = 1 and not private and deal_ids one of ["deal-1", "deal-2"] and segment_ids one of [1, 2, 3] and country in ['FR', 'GB']"#;

    fn is_sync_and_send<T: Send + Sync>() {}

    #[test]
    fn support_sync_and_send_traits() {
        is_sync_and_send::<ATree<u64>>();
    }

    #[test]
    fn can_build_an_atree() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::float("bidfloor"),
            AttributeDefinition::string("country"),
            AttributeDefinition::integer_list("segment_ids"),
        ];

        let result = ATree::<u64>::new(&definitions);

        assert!(result.is_ok());
    }

    #[test]
    fn return_an_error_on_duplicate_definitions() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::float("bidfloor"),
            AttributeDefinition::integer("country"),
            AttributeDefinition::integer_list("segment_ids"),
        ];

        let result = ATree::<u64>::new(&definitions);

        assert!(result.is_err());
    }

    #[test]
    fn return_an_error_on_invalid_boolean_expression() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::integer_list("segment_ids"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();

        let result = atree.insert(&1u64, AN_INVALID_BOOLEAN_EXPRESSION);

        assert!(result.is_err());
    }

    #[test]
    fn return_an_error_on_empty_boolean_expression() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::integer_list("segment_ids"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();

        let result = atree.insert(&1u64, "");

        assert!(result.is_err());
    }

    #[test]
    fn can_insert_a_simple_expression() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::integer_list("segment_ids"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();

        let result = atree.insert(&1u64, AN_EXPRESSION);

        assert!(result.is_ok());
    }

    #[test]
    fn can_insert_an_expression_that_refers_to_a_rnode() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::string_list("deal_ids"),
        ];
        let an_expression = "private or exchange_id = 1";
        let another_expression =
            r#"private or exchange_id = 1 or deal_ids one of ["deal-1", "deal-2"]"#;
        let mut atree = ATree::new(&definitions).unwrap();
        assert!(atree.insert(&1u64, an_expression).is_ok());
        assert!(atree.insert(&2u64, another_expression).is_ok());
    }

    #[test]
    fn can_insert_the_same_expression_multiple_times() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::integer_list("segment_ids"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();

        assert!(atree.insert(&1u64, AN_EXPRESSION).is_ok());
        assert!(atree.insert(&2u64, AN_EXPRESSION).is_ok());
    }

    #[test]
    fn can_insert_a_negative_expression() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::integer_list("segment_ids"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();

        let result = atree.insert(&1u64, A_NOT_EXPRESSION);

        assert!(result.is_ok());
    }

    #[test]
    fn can_insert_an_expression_with_and_operators() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::integer_list("segment_ids"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();

        let result = atree.insert(&1u64, AN_EXPRESSION_WITH_AND_OPERATORS);

        assert!(result.is_ok());
    }

    #[test]
    fn can_insert_an_expression_with_or_operators() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::integer_list("segment_ids"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();

        let result = atree.insert(&1u64, AN_EXPRESSION_WITH_OR_OPERATORS);

        assert!(result.is_ok());
    }

    #[test]
    fn can_insert_an_expression_with_mixed_operators() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::string_list("deal_ids"),
            AttributeDefinition::integer_list("segment_ids"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string("city"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();

        let result = atree.insert(&1u64, A_COMPLEX_EXPRESSION);

        assert!(result.is_ok());
    }

    #[test]
    fn can_insert_multiple_expressions_with_mixed_operators() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::string_list("deal_ids"),
            AttributeDefinition::integer_list("segment_ids"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string("city"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();

        assert!(atree.insert(&1u64, A_COMPLEX_EXPRESSION).is_ok());
        assert!(atree.insert(&2u64, ANOTHER_COMPLEX_EXPRESSION).is_ok());
    }

    #[test]
    fn can_search_an_empty_tree() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::string_list("deal_ids"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer_list("segment_ids"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string("city"),
        ];
        let atree = ATree::new(&definitions).unwrap();
        let mut builder = atree.make_event();
        builder.with_boolean("private", false).unwrap();
        let event = builder.build().unwrap();

        let expected: Vec<&u64> = vec![];
        let actual = atree.search(&event).unwrap().matches().to_vec();
        assert_eq!(expected, actual);
    }

    #[test]
    fn can_search_a_single_predicate() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::string_list("deal_ids"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer_list("segment_ids"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string("city"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();
        atree.insert(&1u64, "private").unwrap();
        let mut builder = atree.make_event();
        builder.with_boolean("private", true).unwrap();
        let event = builder.build().unwrap();

        let expected = vec![&1u64];
        let actual = atree.search(&event).unwrap().matches().to_vec();
        assert_eq!(expected, actual);
    }

    #[test]
    fn ignore_results_that_are_not_matched() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::string_list("deal_ids"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer_list("segment_ids"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string("city"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();
        atree.insert(&1u64, "private").unwrap();
        atree.insert(&2u64, A_COMPLEX_EXPRESSION).unwrap();
        let mut builder = atree.make_event();
        builder.with_boolean("private", false).unwrap();
        let event = builder.build().unwrap();

        let expected: Vec<&u64> = vec![];
        let actual = atree.search(&event).unwrap().matches().to_vec();
        assert_eq!(expected, actual);
    }

    #[test]
    fn can_search_simple_expressions() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::string_list("deal_ids"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer_list("segment_ids"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string("city"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();
        atree.insert(&1u64, "private").unwrap();
        atree.insert(&2u64, "not private").unwrap();
        let mut builder = atree.make_event();
        builder.with_boolean("private", true).unwrap();
        let event = builder.build().unwrap();

        let expected = vec![&1u64];
        let mut actual = atree.search(&event).unwrap().matches().to_vec();
        actual.sort();
        assert_eq!(expected, actual);
    }

    #[test]
    fn can_search_complex_expressions() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::string_list("deal_ids"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer_list("segment_ids"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string("city"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();

        atree.insert(&1, A_COMPLEX_EXPRESSION).unwrap();
        atree.insert(&2, AN_EXPRESSION_WITH_AND_OPERATORS).unwrap();
        atree.insert(&3, AN_EXPRESSION_WITH_OR_OPERATORS).unwrap();
        let mut builder = atree.make_event();
        builder.with_integer("exchange_id", 1).unwrap();
        builder.with_boolean("private", true).unwrap();
        builder
            .with_string_list("deal_ids", &["deal-1", "deal-2"])
            .unwrap();
        builder
            .with_string_list("deals", &["deal-1", "deal-2"])
            .unwrap();
        builder.with_integer_list("segment_ids", &[2, 3]).unwrap();
        builder.with_string("country", "FR").unwrap();
        let event = builder.build().unwrap();

        let expected = vec![&2, &3];
        let mut actual = atree.search(&event).unwrap().matches().to_vec();
        actual.sort();
        assert_eq!(expected, actual);
    }

    #[test]
    fn can_search_a_tree_with_multiple_shared_sub_expressions() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer_list("segment_ids"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string("city"),
        ];
        let mut atree = ATree::new(&definitions).unwrap();
        [
            (
                1,
                r#"exchange_id = 1 and not private and deals one of ["deal-1", "deal-2"]"#,
            ),
            (
                2,
                r#"exchange_id = 1 and not private and deals one of ["deal-2", "deal-3"]"#,
            ),
            (
                3,
                r#"exchange_id = 1 and not private and deals one of ["deal-2", "deal-3"] and segment_ids one of [1, 2, 3, 4]"#,
            ),
            (
                4,
                r#"exchange_id = 1 and not private and deals one of ["deal-2", "deal-3"] and segment_ids one of [5, 6, 7, 8] and country in ["CA", "US"]"#,
            ),
        ].into_iter().for_each(|(id, expression)| {
                atree.insert(&id, expression).unwrap()
        });

        let mut builder = atree.make_event();
        builder.with_boolean("private", false).unwrap();
        builder.with_integer("exchange_id", 1).unwrap();
        builder
            .with_string_list("deals", &["deal-1", "deal-3"])
            .unwrap();
        builder.with_integer_list("segment_ids", &[2, 3]).unwrap();
        builder.with_string("country", "CA").unwrap();
        let event = builder.build().unwrap();

        let mut matches = atree.search(&event).unwrap().matches().to_vec();
        matches.sort();
        assert_eq!(vec![&1, &2, &3], matches);
    }

    #[test]
    fn can_delete_a_single_predicate() {
        let definitions = [AttributeDefinition::boolean("private")];
        let mut atree = ATree::new(&definitions).unwrap();
        atree.insert(&1u64, "private").unwrap();
        let mut builder = atree.make_event();
        builder.with_boolean("private", true).unwrap();
        let event = builder.build().unwrap();

        let results = atree.search(&event).unwrap().matches().to_vec();
        assert_eq!(vec![&1u64], results);

        atree.delete(&1u64);
        let mut builder = atree.make_event();
        builder.with_boolean("private", true).unwrap();
        let event = builder.build().unwrap();
        let results = atree.search(&event).unwrap().matches().to_vec();
        assert!(results.is_empty());
    }

    #[test]
    fn deleting_an_expression_only_removes_the_id_not_the_expression_if_it_is_still_referenced() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::string_list("deal_ids"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer_list("segment_ids"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string("city"),
        ];
        let an_expression = "private or exchange_id = 1";
        let another_expression =
            r#"private or exchange_id = 1 or deal_ids one of ["deal-1", "deal-2"]"#;
        let mut atree = ATree::new(&definitions).unwrap();
        atree.insert(&1u64, an_expression).unwrap();
        atree.insert(&2u64, another_expression).unwrap();
        let mut builder = atree.make_event();
        builder.with_integer("exchange_id", 1).unwrap();
        let event = builder.build().unwrap();

        let results = atree.search(&event).unwrap().matches().to_vec();
        assert_eq!(vec![&1u64, &2u64], results);

        atree.delete(&1u64);
        let mut builder = atree.make_event();
        builder.with_integer("exchange_id", 1).unwrap();
        let event = builder.build().unwrap();
        let results = atree.search(&event).unwrap().matches().to_vec();
        assert_eq!(vec![&2u64], results);
    }

    #[test]
    fn deleting_an_expression_only_removes_the_id_not_the_expression_if_it_has_multiple_subscription_ids(
    ) {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
        ];
        let an_expression = "private or exchange_id = 1";
        let mut atree = ATree::new(&definitions).unwrap();
        atree.insert(&1u64, an_expression).unwrap();
        atree.insert(&2u64, an_expression).unwrap();
        let mut builder = atree.make_event();
        builder.with_integer("exchange_id", 1).unwrap();
        let event = builder.build().unwrap();

        let results = atree.search(&event).unwrap().matches().to_vec();
        assert_eq!(vec![&1u64, &2u64], results);

        atree.delete(&1u64);
        let mut builder = atree.make_event();
        builder.with_integer("exchange_id", 1).unwrap();
        let event = builder.build().unwrap();
        let results = atree.search(&event).unwrap().matches().to_vec();
        assert_eq!(vec![&2u64], results);
    }

    #[test]
    fn can_delete_root_node_when_all_references_are_deleted() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
        ];
        let an_expression = "private or exchange_id = 1";
        let mut atree = ATree::new(&definitions).unwrap();
        atree.insert(&1u64, an_expression).unwrap();
        atree.insert(&2u64, an_expression).unwrap();
        let mut builder = atree.make_event();
        builder.with_integer("exchange_id", 1).unwrap();
        let event = builder.build().unwrap();

        let results = atree.search(&event).unwrap().matches().to_vec();
        assert_eq!(vec![&1u64, &2u64], results);

        atree.delete(&1u64);
        atree.delete(&2u64);
        let mut builder = atree.make_event();
        builder.with_integer("exchange_id", 1).unwrap();
        let event = builder.build().unwrap();
        let results = atree.search(&event).unwrap().matches().to_vec();
        assert!(results.is_empty());
    }

    #[test]
    fn can_render_to_graphviz() {
        let definitions = [
            AttributeDefinition::boolean("private"),
            AttributeDefinition::integer("exchange_id"),
            AttributeDefinition::string_list("deal_ids"),
            AttributeDefinition::string_list("deals"),
            AttributeDefinition::integer_list("segment_ids"),
            AttributeDefinition::string("country"),
            AttributeDefinition::string("city"),
        ];
        let an_expression = "private or exchange_id = 1";
        let another_expression =
            r#"private or exchange_id = 1 or deal_ids one of ["deal-1", "deal-2"]"#;
        let mut atree = ATree::new(&definitions).unwrap();
        atree.insert(&1u64, an_expression).unwrap();
        atree.insert(&2u64, another_expression).unwrap();

        assert!(!atree.to_graphviz().is_empty());
    }
}
