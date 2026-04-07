namespace GraphParser.Models;

/// <summary>
/// Metadata about a job extracted from the CSV Parameters column.
/// </summary>
public record JobInfo(string Id, int SequenceNumber, string Level, string Exit);
