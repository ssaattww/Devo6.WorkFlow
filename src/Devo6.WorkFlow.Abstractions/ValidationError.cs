namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// Describes a validation problem with enough location and code information for users to identify the cause.
/// </summary>
public sealed class ValidationError
{
    /// <summary>
    /// Gets the user-facing path to the invalid entry, step, input, or configuration member.
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// Gets the stable validation error code.
    /// </summary>
    public string Code { get; init; } = "";

    /// <summary>
    /// Gets the human-readable validation message.
    /// </summary>
    public string Message { get; init; } = "";
}
