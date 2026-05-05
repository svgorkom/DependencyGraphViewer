namespace CsprojParser.Models;

/// <summary>
/// Represents a directed dependency edge (Source depends on Target).
/// </summary>
public record DependencyEdge(string Source, string Target, DependencyType Type);
