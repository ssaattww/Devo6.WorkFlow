namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// Defines a workflow step that produces its output asynchronously.
/// </summary>
/// <typeparam name="TOut">The output type produced by the step.</typeparam>
public interface IAsyncStep<TOut>
{
    /// <summary>
    /// Executes the step with the current input values.
    /// </summary>
    /// <param name="input">The input values available to the step.</param>
    /// <param name="cancellationToken">The cancellation token passed by the engine.</param>
    /// <returns>The asynchronous output produced by the step.</returns>
    Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken);
}
