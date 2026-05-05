namespace CsprojParser.Models;

/// <summary>
/// Complete dependency graph containing all discovered project nodes and their dependency edges.
/// </summary>
public class ProjectDependencyGraph
{
    public Dictionary<string, ProjectNode> Nodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<DependencyEdge> Edges { get; } = [];
}
