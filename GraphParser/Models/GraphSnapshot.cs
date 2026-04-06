namespace GraphParser.Models;

/// <summary>
/// A snapshot of the dependency graph at a specific point in time,
/// containing all accumulated nodes and edges up to that point.
/// </summary>
public class GraphSnapshot
{
    public Dictionary<string, JobInfo> Nodes { get; } = new();
    public List<GraphEdge> Edges { get; } = new();
}
