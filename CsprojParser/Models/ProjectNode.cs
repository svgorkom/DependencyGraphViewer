namespace CsprojParser.Models;

/// <summary>
/// Represents a node in the project dependency graph.
/// </summary>
/// <param name="Id">Unique identifier used for graph rendering and edge matching.</param>
/// <param name="Name">Display name (project file name, package name, or assembly name).</param>
/// <param name="NodeType">Whether this is a project, NuGet package, or DLL reference.</param>
/// <param name="Version">Optional version string when available.</param>
/// <param name="FullPath">Full path on disk (only set for discovered .csproj files).</param>
public record ProjectNode(
    string Id,
    string Name,
    DependencyType NodeType,
    string? Version = null,
    string? FullPath = null);
