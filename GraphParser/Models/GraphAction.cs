namespace GraphParser.Models;

/// <summary>
/// A single parsed row from the sequencing graph actions CSV file.
/// </summary>
public record GraphAction(
    DateTime Timestamp,
    GraphActionType ActionType,
    string Parameters,
    string Result,
    string AdditionalInfo,
    IReadOnlyList<GraphEdge> Edges);
