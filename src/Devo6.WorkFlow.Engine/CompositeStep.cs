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
}

public sealed class CompositeStep<TOut> : IStep<TOut>
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
        ArgumentNullException.ThrowIfNull(input);

        object? currentValue = default(TOut);

        foreach (StepRegistration step in steps)
        {
            currentValue = step.Execute(input);
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
        options ??= new WorkflowExecutionOptions();

        ILoggerFactory loggerFactory = options.LoggerFactory ?? NullLoggerFactory.Instance;
        ILogger engineLogger = loggerFactory.CreateLogger("Devo6.WorkFlow.Engine");
        ILogger stepLogger = loggerFactory.CreateLogger("Devo6.WorkFlow.Step");
        var traceSteps = new List<ExecutionTraceStep>();
        var input = new StepInput(new StepContext(stepLogger));
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
                currentValue = step.Execute(input);
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
    private readonly Func<StepInput, object?> execute;
    private readonly IReadOnlyList<Action<StepInput, object?>> producers;

    private StepRegistration(
        string name,
        Func<StepInput, object?> execute,
        IReadOnlyList<Action<StepInput, object?>> producers)
    {
        this.name = name;
        this.execute = execute;
        this.producers = producers.ToArray();
    }

    public string Name => name;

    public static StepRegistration Create<TStep, TOut>()
        where TStep : IStep<TOut>, new()
    {
        return new StepRegistration(typeof(TStep).Name, input => new TStep().Execute(input), []);
    }

    public object? Execute(StepInput input)
    {
        return execute(input);
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

        return new StepRegistration(name, execute, nextProducers);
    }

    public StepRegistration ClearProducers()
    {
        return new StepRegistration(name, execute, []);
    }

    public void Produce(StepInput input, object? value)
    {
        foreach (Action<StepInput, object?> producer in producers)
        {
            producer(input, value);
        }
    }
}
