using Devo6.WorkFlow.Abstractions;

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
    private readonly Func<StepInput, object?> execute;
    private readonly IReadOnlyList<Action<StepInput, object?>> producers;

    private StepRegistration(
        Func<StepInput, object?> execute,
        IReadOnlyList<Action<StepInput, object?>> producers)
    {
        this.execute = execute;
        this.producers = producers.ToArray();
    }

    public static StepRegistration Create<TStep, TOut>()
        where TStep : IStep<TOut>, new()
    {
        return new StepRegistration(input => new TStep().Execute(input), []);
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

        return new StepRegistration(execute, nextProducers);
    }

    public StepRegistration ClearProducers()
    {
        return new StepRegistration(execute, []);
    }

    public void Produce(StepInput input, object? value)
    {
        foreach (Action<StepInput, object?> producer in producers)
        {
            producer(input, value);
        }
    }
}
