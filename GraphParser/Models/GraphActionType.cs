namespace GraphParser.Models;

/// <summary>
/// Action types that impact the dependency graph.
/// </summary>
public enum GraphActionType
{
    AddJob,
    PickedUpFromOrtbByLift,
    AssignJob,
    RemoveJob,
}
