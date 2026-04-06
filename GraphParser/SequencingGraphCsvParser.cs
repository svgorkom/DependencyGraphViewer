using System.Globalization;
using GraphParser.Models;

namespace GraphParser;

/// <summary>
/// Parses SZC.MPG.SequencingGraphActions.csv files and builds dependency graph snapshots.
/// </summary>
public static class SequencingGraphCsvParser
{
    private static readonly IReadOnlyList<GraphEdge> EmptyEdges = Array.Empty<GraphEdge>();

    /// <summary>
    /// Parses all rows of the CSV file into a list of <see cref="GraphAction"/> instances.
    /// </summary>
    public static List<GraphAction> Parse(string filePath)
    {
        var actions = new List<GraphAction>(EstimateLineCount(filePath));

        foreach (var line in File.ReadLines(filePath))
        {
            try
            {
                if (line.Length == 0 || line.AsSpan().IsWhiteSpace())
                    continue;

                var span = line.AsSpan();

                // Field 0: timestamp
                var sep0 = span.IndexOf(';');
                if (sep0 < 0) continue;
                var field0 = span[..sep0].Trim();

                if (!DateTime.TryParseExact(
                        field0,
                        "yyyy-MM-dd HH:mm:ss.fff",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var timestamp))
                    continue;

                // Field 1: action
                var rest = span[(sep0 + 1)..];
                var sep1 = rest.IndexOf(';');
                if (sep1 < 0) continue;
                var field1 = rest[..sep1].Trim();
                if (field1.IsEmpty) continue;

                // Field 2: parameters
                rest = rest[(sep1 + 1)..];
                var sep2 = rest.IndexOf(';');
                if (sep2 < 0) continue;
                var field2 = rest[..sep2].Trim();

                // Field 3: result
                rest = rest[(sep2 + 1)..];
                var sep3 = rest.IndexOf(';');
                ReadOnlySpan<char> field3;
                ReadOnlySpan<char> additionalSpan;
                if (sep3 >= 0)
                {
                    field3 = rest[..sep3].Trim();
                    additionalSpan = rest[(sep3 + 1)..].Trim();
                }
                else
                {
                    field3 = rest.Trim();
                    additionalSpan = [];
                }

                if (!Enum.TryParse<GraphActionType>(field1, out var actionType))
                    continue;

                var parameters = field2.ToString();
                var result = field3.ToString();
                var additionalInfo = additionalSpan.IsEmpty ? string.Empty : additionalSpan.ToString();

                var edges = ParseEdges(additionalSpan);

                actions.Add(new GraphAction(timestamp, actionType, parameters, result, additionalInfo, edges));
            }
            catch (Exception)
            {
                // Skip malformed rows so a single bad line does not abort the entire import.
                continue;
            }
        }

        return actions;
    }

    /// <summary>
    /// Builds a <see cref="GraphSnapshot"/> by accumulating all actions from index 0 up to
    /// (and including) <paramref name="upToIndex"/>.
    /// </summary>
    public static GraphSnapshot BuildSnapshot(IReadOnlyList<GraphAction> actions, int upToIndex)
    {
        var snapshot = new GraphSnapshot();

        for (var i = 0; i <= upToIndex && i < actions.Count; i++)
        {
            var action = actions[i];

            if (action.ActionType == GraphActionType.AddJob)
            {
                var jobInfo = ParseJobParameters(action.Parameters);
                if (jobInfo is not null)
                    snapshot.Nodes.TryAdd(jobInfo.Id, jobInfo);
            }
            else if (action.ActionType == GraphActionType.PickedUpFromOrtbByLift)
                {
                    var nodeId = ParseNodeId(action.Parameters);
                    if (nodeId is not null)
                    {
                        snapshot.Nodes.Remove(nodeId);
                        snapshot.Edges.RemoveAll(e => e.Source == nodeId || e.Target == nodeId);
                    }
                }

                if (action.ActionType != GraphActionType.PickedUpFromOrtbByLift)
                {
                    foreach (var edge in action.Edges)
                    {
                        snapshot.Nodes.TryAdd(edge.Source, new JobInfo(edge.Source, 0, 0, string.Empty));
                        snapshot.Nodes.TryAdd(edge.Target, new JobInfo(edge.Target, 0, 0, string.Empty));
                        snapshot.Edges.Add(edge);
                    }
                }
        }

        return snapshot;
    }

    private static IReadOnlyList<GraphEdge> ParseEdges(ReadOnlySpan<char> additionalInfo)
    {
        if (additionalInfo.IsEmpty || additionalInfo.IsWhiteSpace())
            return EmptyEdges;

        List<GraphEdge>? edges = null;
        while (!additionalInfo.IsEmpty)
        {
            ReadOnlySpan<char> segment;
            var hashIndex = additionalInfo.IndexOf('#');
            if (hashIndex >= 0)
            {
                segment = additionalInfo[..hashIndex];
                additionalInfo = additionalInfo[(hashIndex + 1)..];
            }
            else
            {
                segment = additionalInfo;
                additionalInfo = [];
            }

            segment = segment.Trim();
            if (segment.IsEmpty)
                continue;

            var arrowIndex = segment.IndexOf("->".AsSpan(), StringComparison.Ordinal);
            if (arrowIndex < 0)
                continue;

            var source = segment[..arrowIndex].Trim();
            var target = segment[(arrowIndex + 2)..].Trim();

            if (source.Length > 0 && target.Length > 0)
            {
                edges ??= [];
                edges.Add(new GraphEdge(source.ToString(), target.ToString()));
            }
        }

        return edges ?? EmptyEdges;
    }

    private static string? ParseNodeId(string parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return null;

        // Try the key-value format first (e.g. "ID:job1-SN:1-…").
        // Fall back to treating the entire parameter string as a bare job ID
        // (e.g. PickedUpFromOrtbByLift rows that contain only "Job_434641_e2M7wfVd").
        return ExtractValueAfterKey(parameters.AsSpan(), "ID:") ?? parameters.Trim();
    }

    private static JobInfo? ParseJobParameters(string parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return null;

        var span = parameters.AsSpan();

        var id = ExtractValueAfterKey(span, "ID:");
        if (id is null)
            return null;

        var snStr = ExtractValueAfterKey(span, "SN:");
        var levelStr = ExtractValueAfterKey(span, "LEVEL:");
        var exit = ExtractValueAfterKey(span, "EXIT:") ?? string.Empty;

        var sequenceNumber = snStr is not null && int.TryParse(snStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sn) ? sn : 0;
        var level = levelStr is not null && int.TryParse(levelStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv) ? lv : 0;

        return new JobInfo(id, sequenceNumber, level, exit);
    }

    private static string? ExtractValueAfterKey(ReadOnlySpan<char> span, ReadOnlySpan<char> key)
    {
        var idx = span.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0)
            return null;

        var valueStart = idx + key.Length;
        var valueSpan = span[valueStart..];

        var end = 0;
        while (end < valueSpan.Length && IsWordChar(valueSpan[end]))
            end++;

        return end > 0 ? valueSpan[..end].ToString() : null;
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    private static int EstimateLineCount(string filePath)
    {
        const int averageBytesPerLine = 120;
        try
        {
            var fileSize = new FileInfo(filePath).Length;
            return (int)Math.Min(fileSize / averageBytesPerLine, int.MaxValue);
        }
        catch
        {
            return 1024;
        }
    }
}
