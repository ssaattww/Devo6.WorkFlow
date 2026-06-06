namespace Devo6.WorkFlow.Abstractions;

public sealed class StepInput
{
    private readonly Dictionary<StepValueKey, object?> values = new();

    public StepInput()
        : this(null)
    {
    }

    public StepInput(StepContext? context)
    {
        Context = context ?? new StepContext();
        values.Add(StepValueKey.For<StepContext>(), Context);
    }

    public StepContext Context { get; }

    internal void Add<T>(T value)
    {
        Add(StepValueKey.For<T>(), value);
    }

    internal void Add<T>(string name, T value)
    {
        Add(StepValueKey.For<T>(name), value);
    }

    public T Get<T>()
    {
        return Get<T>(StepValueKey.For<T>());
    }

    public T Get<T>(string name)
    {
        return Get<T>(StepValueKey.For<T>(name));
    }

    public bool TryGet<T>(out T value)
    {
        return TryGet(StepValueKey.For<T>(), out value);
    }

    public bool TryGet<T>(string name, out T value)
    {
        return TryGet(StepValueKey.For<T>(name), out value);
    }

    private void Add<T>(StepValueKey key, T value)
    {
        if (values.ContainsKey(key))
        {
            throw new InvalidOperationException($"A value is already registered for {key}.");
        }

        values.Add(key, value);
    }

    private T Get<T>(StepValueKey key)
    {
        if (!TryGet(key, out T value))
        {
            throw new KeyNotFoundException($"No value is registered for {key}.");
        }

        return value;
    }

    private bool TryGet<T>(StepValueKey key, out T value)
    {
        if (values.TryGetValue(key, out object? registeredValue))
        {
            value = (T)registeredValue!;
            return true;
        }

        value = default!;
        return false;
    }
}
