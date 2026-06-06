using Devo6.WorkFlow.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Devo6.WorkFlow.Engine;

public static class CompositeStep
{
    public static CompositeStepDefinition Define(string name)
    {
        return new CompositeStepDefinition(name);
    }
}

public sealed class CompositeStepDefinition
{
    internal CompositeStepDefinition(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    public string Name { get; }

    public CompositeStep<TOut> Run<TStep, TOut>()
        where TStep : IStep<TOut>, new()
    {
        return new CompositeStep<TOut>(Name, [StepRegistration.Create<TStep, TOut>()]);
    }

    /// <summary>
    /// Registers the first asynchronous step in this composite entry.
    /// </summary>
    /// <typeparam name="TStep">The asynchronous step type to run.</typeparam>
    /// <typeparam name="TOut">The output type produced by the asynchronous step.</typeparam>
    /// <returns>A composite step that can be extended or executed.</returns>
    public CompositeStep<TOut> RunAsync<TStep, TOut>()
        where TStep : IAsyncStep<TOut>, new()
    {
        return new CompositeStep<TOut>(Name, [StepRegistration.CreateAsync<TStep, TOut>()]);
    }
}

public sealed class CompositeStep<TOut> : IStep<TOut>, IAsyncStep<TOut>
{
    private readonly IReadOnlyList<StepRegistration> steps;

    internal CompositeStep(string name, IReadOnlyList<StepRegistration> steps)
    {
        Name = name;
        this.steps = steps.ToArray();
    }

    public string Name { get; }

    public CompositeStep<TNext> Run<TStep, TNext>()
        where TStep : IStep<TNext>, new()
    {
        return new CompositeStep<TNext>(Name, Append(StepRegistration.Create<TStep, TNext>()));
    }

    /// <summary>
    /// Appends an asynchronous step to this composite entry.
    /// </summary>
    /// <typeparam name="TStep">The asynchronous step type to run.</typeparam>
    /// <typeparam name="TNext">The output type produced by the asynchronous step.</typeparam>
    /// <returns>A composite step whose current output type is the appended asynchronous step output.</returns>
    public CompositeStep<TNext> RunAsync<TStep, TNext>()
        where TStep : IAsyncStep<TNext>, new()
    {
        return new CompositeStep<TNext>(Name, Append(StepRegistration.CreateAsync<TStep, TNext>()));
    }

    public CompositeStep<TOut> Produce<TValue>(Func<TOut, TValue> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return WithCurrentStep(CurrentStep.AddProducer((input, value) => input.Add(selector((TOut)value!))));
    }

    public CompositeStep<TOut> Produce<TValue>(string name, Func<TOut, TValue> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return WithCurrentStep(CurrentStep.AddProducer((input, value) => input.Add(name, selector((TOut)value!))));
    }

    public CompositeStep<TOut> StoreAs()
    {
        return Produce<TOut>(value => value);
    }

    public CompositeStep<TOut> Discard()
    {
        return WithCurrentStep(CurrentStep.ClearProducers());
    }

    public TOut Execute(StepInput input)
    {
        return ExecuteAsync(input, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Executes this composite step asynchronously with the supplied input values.
    /// </summary>
    /// <param name="input">The input values available to the first step.</param>
    /// <param name="cancellationToken">The cancellation token passed to asynchronous steps.</param>
    /// <returns>The output produced by the final step in the composite entry.</returns>
    public async Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        object? currentValue = default(TOut);

        foreach (StepRegistration step in steps)
        {
            currentValue = await step.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
            step.Produce(input, currentValue);
        }

        return (TOut)currentValue!;
    }

    /// <summary>
    /// Executes this composite entry through the engine path and returns a workflow result with logs and trace history.
    /// </summary>
    /// <param name="options">The execution dependencies to use, or null for default options.</param>
    /// <returns>The workflow result describing success, failure, and captured trace history.</returns>
    public WorkflowResult ExecuteWorkflow(WorkflowExecutionOptions? options = null)
    {
        return ExecuteWorkflowAsync(options, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Executes this composite entry asynchronously through the engine path and returns a workflow result.
    /// </summary>
    /// <param name="options">The execution dependencies to use, or null for default options.</param>
    /// <param name="cancellationToken">The cancellation token passed to asynchronous steps.</param>
    /// <returns>The workflow result describing success, failure, and captured trace history.</returns>
    public async Task<WorkflowResult> ExecuteWorkflowAsync(
        WorkflowExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WorkflowExecutionOptions();

        ILoggerFactory loggerFactory = options.LoggerFactory ?? NullLoggerFactory.Instance;
        ILogger engineLogger = loggerFactory.CreateLogger("Devo6.WorkFlow.Engine");
        ILogger stepLogger = loggerFactory.CreateLogger("Devo6.WorkFlow.Step");
        var traceSteps = new List<ExecutionTraceStep>();
        var context = new StepContext(stepLogger);
        if (options.EngineArguments is not null)
        {
            context.Set(options.EngineArguments);
        }

        var input = new StepInput(context);
        object? currentValue = default(TOut);

        using IDisposable? entryScope = engineLogger.BeginScope(new Dictionary<string, object?>
        {
            ["EntryName"] = Name,
            ["Attempt"] = 1,
        });

        engineLogger.LogInformation("Entry started");

        foreach (StepRegistration step in steps)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            using IDisposable? stepScope = engineLogger.BeginScope(new Dictionary<string, object?>
            {
                ["EntryName"] = Name,
                ["StepName"] = step.Name,
                ["Attempt"] = 1,
            });

            engineLogger.LogInformation("Step started");

            try
            {
                currentValue = await step.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
                step.Produce(input, currentValue);
                stopwatch.Stop();
                traceSteps.Add(new ExecutionTraceStep(step.Name, ExecutionTraceStepStatus.Succeeded, stopwatch.Elapsed, null));
                engineLogger.LogInformation("Step succeeded");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                traceSteps.Add(new ExecutionTraceStep(
                    step.Name,
                    ExecutionTraceStepStatus.Failed,
                    stopwatch.Elapsed,
                    WorkflowErrorCodes.StepExecutionFailed));
                engineLogger.LogError(
                    exception,
                    "Step failed with error code {ErrorCode}",
                    WorkflowErrorCodes.StepExecutionFailed);
                engineLogger.LogError(
                    exception,
                    "Entry failed with error code {ErrorCode}",
                    WorkflowErrorCodes.StepExecutionFailed);

                return new WorkflowResult
                {
                    EntryName = Name,
                    Succeeded = false,
                    ErrorCode = WorkflowErrorCodes.StepExecutionFailed,
                    ErrorMessage = exception.Message,
                    Trace = new ExecutionTrace(traceSteps),
                };
            }
        }

        engineLogger.LogInformation("Entry succeeded");

        return new WorkflowResult
        {
            EntryName = Name,
            Succeeded = true,
            Trace = new ExecutionTrace(traceSteps),
        };
    }

    private StepRegistration CurrentStep
    {
        get
        {
            if (steps.Count == 0)
            {
                throw new InvalidOperationException("No step is registered.");
            }

            return steps[^1];
        }
    }

    private IReadOnlyList<StepRegistration> Append(StepRegistration registration)
    {
        StepRegistration[] nextSteps = new StepRegistration[steps.Count + 1];

        for (int i = 0; i < steps.Count; i++)
        {
            nextSteps[i] = steps[i];
        }

        nextSteps[^1] = registration;

        return nextSteps;
    }

    private CompositeStep<TOut> WithCurrentStep(StepRegistration registration)
    {
        StepRegistration[] nextSteps = steps.ToArray();
        nextSteps[^1] = registration;

        return new CompositeStep<TOut>(Name, nextSteps);
    }
}

internal sealed class StepRegistration
{
    private readonly string name;
    private readonly Func<StepInput, CancellationToken, Task<object?>> executeAsync;
    private readonly IReadOnlyList<Action<StepInput, object?>> producers;

    private StepRegistration(
        string name,
        Func<StepInput, CancellationToken, Task<object?>> executeAsync,
        IReadOnlyList<Action<StepInput, object?>> producers)
    {
        this.name = name;
        this.executeAsync = executeAsync;
        this.producers = producers.ToArray();
    }

    public string Name => name;

    public static StepRegistration Create<TStep, TOut>()
        where TStep : IStep<TOut>, new()
    {
        return new StepRegistration(
            typeof(TStep).Name,
            (input, cancellationToken) =>
            {
                return Task.FromResult<object?>(new TStep().Execute(input));
            },
            []);
    }

    public static StepRegistration CreateAsync<TStep, TOut>()
        where TStep : IAsyncStep<TOut>, new()
    {
        return new StepRegistration(
            typeof(TStep).Name,
            async (input, cancellationToken) => await new TStep().ExecuteAsync(input, cancellationToken).ConfigureAwait(false),
            []);
    }

    public Task<object?> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
    {
        return executeAsync(input, cancellationToken);
    }

    public StepRegistration AddProducer(Action<StepInput, object?> producer)
    {
        ArgumentNullException.ThrowIfNull(producer);

        Action<StepInput, object?>[] nextProducers = new Action<StepInput, object?>[producers.Count + 1];

        for (int i = 0; i < producers.Count; i++)
        {
            nextProducers[i] = producers[i];
        }

        nextProducers[^1] = producer;

        return new StepRegistration(name, executeAsync, nextProducers);
    }

    public StepRegistration ClearProducers()
    {
        return new StepRegistration(name, executeAsync, []);
    }

    public void Produce(StepInput input, object? value)
    {
        foreach (Action<StepInput, object?> producer in producers)
        {
            producer(input, value);
        }
    }
}
