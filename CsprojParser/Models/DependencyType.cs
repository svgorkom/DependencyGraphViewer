namespace CsprojParser.Models;

/// <summary>
/// Classifies the type of a dependency node or edge.
/// </summary>
public enum DependencyType
{
    Project,
    NuGet,
    Dll
}
