using System.Xml.Linq;
using CsprojParser.Models;

namespace CsprojParser;

/// <summary>
/// Recursively scans a folder for .csproj files and builds a <see cref="ProjectDependencyGraph"/>
/// containing project, NuGet, and DLL reference relationships.
/// </summary>
public static class CsprojDependencyParser
{
    /// <summary>
    /// Parses all .csproj files found recursively under <paramref name="folderPath"/>
    /// and returns the complete dependency graph.
    /// </summary>
    public static ProjectDependencyGraph Parse(string folderPath)
    {
        var graph = new ProjectDependencyGraph();
        var csprojFiles = Directory.EnumerateFiles(folderPath, "*.csproj", SearchOption.AllDirectories).ToList();

        // First pass: register all discovered project nodes.
        foreach (var path in csprojFiles)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var version = TryGetProjectVersion(path);
            EnsureNode(graph, name, name, DependencyType.Project, version, path);
        }

        // Second pass: parse each project's dependencies.
        foreach (var path in csprojFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(path);
            var sourceId = GetCanonicalId(graph, projectName);
            ParseProjectDependencies(graph, path, sourceId);
        }

        return graph;
    }

    /// <summary>
    /// Ensures a node exists in the graph. Returns the canonical (first-seen) ID for edge consistency.
    /// </summary>
    private static string EnsureNode(
        ProjectDependencyGraph graph,
        string id,
        string name,
        DependencyType type,
        string? version = null,
        string? fullPath = null)
    {
        if (graph.Nodes.TryGetValue(id, out var existing))
        {
            if (version is not null && existing.Version is null)
                graph.Nodes[existing.Id] = existing with { Version = version };
            return existing.Id;
        }

        var node = new ProjectNode(id, name, type, version, fullPath);
        graph.Nodes[id] = node;
        return id;
    }

    private static string GetCanonicalId(ProjectDependencyGraph graph, string id) =>
        graph.Nodes.TryGetValue(id, out var node) ? node.Id : id;

    private static void ParseProjectDependencies(ProjectDependencyGraph graph, string csprojPath, string sourceId)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);

            foreach (var elem in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
            {
                var include = elem.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(include))
                    continue;

                var refName = Path.GetFileNameWithoutExtension(include);
                var targetId = EnsureNode(graph, refName, refName, DependencyType.Project);
                graph.Edges.Add(new DependencyEdge(sourceId, targetId, DependencyType.Project));
            }

            foreach (var elem in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
            {
                var include = elem.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(include))
                    continue;

                var version = elem.Attribute("Version")?.Value
                              ?? elem.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value;

                var targetId = EnsureNode(graph, include, include, DependencyType.NuGet, version);
                graph.Edges.Add(new DependencyEdge(sourceId, targetId, DependencyType.NuGet));
            }

            foreach (var elem in doc.Descendants().Where(e => e.Name.LocalName == "Reference"))
            {
                var include = elem.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(include))
                    continue;

                var parts = include.Split(',');
                var assemblyName = parts[0].Trim();
                string? version = null;

                foreach (var part in parts.Skip(1))
                {
                    var kv = part.Trim();
                    if (kv.StartsWith("Version=", StringComparison.OrdinalIgnoreCase))
                    {
                        version = kv["Version=".Length..];
                        break;
                    }
                }

                var targetId = EnsureNode(graph, assemblyName, assemblyName, DependencyType.Dll, version);
                graph.Edges.Add(new DependencyEdge(sourceId, targetId, DependencyType.Dll));
            }
        }
        catch
        {
            // Skip malformed csproj files so a single bad file does not abort the entire scan.
        }
    }

    private static string? TryGetProjectVersion(string csprojPath)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value
                   ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "AssemblyVersion")?.Value;
        }
        catch
        {
            return null;
        }
    }
}
