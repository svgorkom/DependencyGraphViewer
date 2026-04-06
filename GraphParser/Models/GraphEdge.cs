namespace GraphParser.Models;

/// <summary>
/// Represents a directed edge in the dependency graph (Source depends on Target, or Source → Target).
/// </summary>
public record GraphEdge(string Source, string Target);
