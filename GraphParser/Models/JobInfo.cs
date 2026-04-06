namespace GraphParser.Models;

/// <summary>
/// Metadata about a job extracted from the CSV Parameters column.
/// </summary>
public record JobInfo(string Id, int SequenceNumber, int Level, string Exit);
