namespace SoccerSim.Core.World;

/// <summary>Raised when a geography edit would leave the tree in a state nothing can read.</summary>
public sealed class GeoTreeException : Exception
{
    public GeoTreeException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The edits the geography screen makes, with the rules that keep the tree a tree (ROADMAP.md
/// Sprint 9). All pure: each operation takes the nodes and returns the nodes, so the screen can
/// show the result before anything is written and the rules are testable without a database.
///
/// <para>The rules exist because each of them, broken, produces a world that looks fine in a
/// list and is unreadable as a hierarchy — a node that is its own ancestor, a club pointing at
/// something deleted, a city hanging off a confederation.</para>
/// </summary>
public static class GeoTree
{
    /// <summary>What a child of each kind is. Creating a child does not ask: the hierarchy has
    /// one shape, and offering a choice would be offering a way to break it.</summary>
    private static readonly IReadOnlyDictionary<GeoNodeKind, GeoNodeKind> NextKind =
        new Dictionary<GeoNodeKind, GeoNodeKind>
        {
            [GeoNodeKind.World] = GeoNodeKind.Confederation,
            [GeoNodeKind.Confederation] = GeoNodeKind.Country,
            [GeoNodeKind.SubRegion] = GeoNodeKind.Country,
            [GeoNodeKind.Country] = GeoNodeKind.Region,
            [GeoNodeKind.Region] = GeoNodeKind.City,
        };

    /// <summary>The kind a new child of this node takes.</summary>
    public static GeoNodeKind ChildKindOf(GeoNodeKind parent) =>
        NextKind.TryGetValue(parent, out GeoNodeKind child)
            ? child
            : throw new GeoTreeException($"Um nó do tipo {parent} é uma folha: não pode ter filhos.");

    public static bool CanHaveChildren(GeoNodeKind parent) => NextKind.ContainsKey(parent);

    /// <summary>Every node below this one, at any depth, including itself.</summary>
    public static IReadOnlySet<string> DescendantsOf(IReadOnlyList<GeoNode> nodes, string nodeId)
    {
        var found = new HashSet<string> { nodeId };
        ILookup<string?, GeoNode> children = nodes.ToLookup(node => node.ParentId);

        var queue = new Queue<string>([nodeId]);
        while (queue.Count > 0)
        {
            foreach (GeoNode child in children[queue.Dequeue()])
            {
                if (found.Add(child.GeoNodeId))
                    queue.Enqueue(child.GeoNodeId);
            }
        }

        return found;
    }

    /// <summary>
    /// Where a node may be moved: anything that can hold a child of its kind and is not inside
    /// its own subtree. The screen builds its select from this, so the impossible move is not
    /// offered — and the operation refuses it anyway, because a select is a convenience and a
    /// rule is a rule.
    /// </summary>
    public static IReadOnlyList<GeoNode> ValidParentsFor(IReadOnlyList<GeoNode> nodes, string nodeId)
    {
        GeoNode node = Require(nodes, nodeId);
        IReadOnlySet<string> subtree = DescendantsOf(nodes, nodeId);

        return nodes
            .Where(candidate => !subtree.Contains(candidate.GeoNodeId))
            .Where(candidate => CanHaveChildren(candidate.Kind) && ChildKindOf(candidate.Kind) == node.Kind)
            .ToList();
    }

    /// <summary>
    /// Moves a node under a new parent. A move into the node's own subtree would make the node
    /// its own ancestor: every walk upward becomes an infinite loop, and the breadcrumb on the
    /// club page never terminates.
    /// </summary>
    public static IReadOnlyList<GeoNode> Move(IReadOnlyList<GeoNode> nodes, string nodeId, string newParentId)
    {
        GeoNode node = Require(nodes, nodeId);
        GeoNode parent = Require(nodes, newParentId);

        if (DescendantsOf(nodes, nodeId).Contains(newParentId))
        {
            throw new GeoTreeException(
                $"'{parent.DisplayName}' está dentro de '{node.DisplayName}': mover para lá faria o nó "
                + "ser ancestral de si mesmo.");
        }

        if (!CanHaveChildren(parent.Kind) || ChildKindOf(parent.Kind) != node.Kind)
        {
            throw new GeoTreeException(
                $"Um {node.Kind} não pode ficar sob um {parent.Kind}"
                + (CanHaveChildren(parent.Kind) ? $" — ali cabe um {ChildKindOf(parent.Kind)}." : "."));
        }

        return Replace(nodes, node with { ParentId = newParentId });
    }

    /// <summary>Renames a node. The only edit with no rule attached: a name is a label, and no
    /// other record points at it.</summary>
    public static IReadOnlyList<GeoNode> Rename(IReadOnlyList<GeoNode> nodes, string nodeId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new GeoTreeException("Um nó precisa de nome.");

        return Replace(nodes, Require(nodes, nodeId) with { DisplayName = displayName.Trim() });
    }

    /// <summary>Adds a child of the kind the hierarchy says comes next.</summary>
    public static IReadOnlyList<GeoNode> AddChild(
        IReadOnlyList<GeoNode> nodes,
        string parentId,
        string childId,
        string displayName)
    {
        GeoNode parent = Require(nodes, parentId);

        if (nodes.Any(node => node.GeoNodeId == childId))
            throw new GeoTreeException($"Já existe um nó com o id '{childId}'.");

        if (string.IsNullOrWhiteSpace(displayName))
            throw new GeoTreeException("Um nó precisa de nome.");

        return [.. nodes, new GeoNode(childId, ChildKindOf(parent.Kind), parentId, displayName.Trim())];
    }

    /// <summary>
    /// Removes a node, refusing while anything still hangs off it. Deleting a node with children
    /// would orphan a whole branch; deleting one with clubs would leave those clubs pointing at
    /// nothing — which the batch audit would then report as GEO_DANGLING, one finding per club,
    /// for a mistake that took one click.
    /// </summary>
    public static IReadOnlyList<GeoNode> Delete(
        IReadOnlyList<GeoNode> nodes,
        IReadOnlyList<ClubIdentity> clubs,
        string nodeId)
    {
        GeoNode node = Require(nodes, nodeId);

        int children = nodes.Count(candidate => candidate.ParentId == nodeId);
        if (children > 0)
        {
            throw new GeoTreeException(
                $"'{node.DisplayName}' tem {children} nó(s) abaixo. Mova ou apague os filhos primeiro.");
        }

        var here = clubs.Where(club => club.Geography.GeoNodeId == nodeId).ToList();
        if (here.Count > 0)
        {
            throw new GeoTreeException(
                $"'{node.DisplayName}' tem {here.Count} clube(s): "
                + $"{string.Join(", ", here.Take(3).Select(club => club.Identity.ShortName))}"
                + (here.Count > 3 ? " e outros" : "") + ". Mova-os antes de apagar o nó.");
        }

        return nodes.Where(candidate => candidate.GeoNodeId != nodeId).ToList();
    }

    private static GeoNode Require(IReadOnlyList<GeoNode> nodes, string nodeId) =>
        nodes.FirstOrDefault(node => node.GeoNodeId == nodeId)
        ?? throw new GeoTreeException($"Não existe nó '{nodeId}'.");

    private static IReadOnlyList<GeoNode> Replace(IReadOnlyList<GeoNode> nodes, GeoNode updated) =>
        nodes.Select(node => node.GeoNodeId == updated.GeoNodeId ? updated : node).ToList();
}
