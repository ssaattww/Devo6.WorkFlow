using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Devo6.WorkFlow.Abstractions;

public sealed class StepContext
{
    private readonly Dictionary<StepValueKey, object?> values = new();

    public StepContext()
        : this(null)
    {
    }

    public StepContext(ILogger? logger)
    {
        Logger = logger ?? NullLogger.Instance;
    }

    public ILogger Logger { get; }

    public T Get<T>()
    {
        return Get<T>(StepValueKey.For<T>());
    }

    public T Get<T>(string name)
    {
        return Get<T>(StepValueKey.For<T>(name));
    }

    public void Set<T>(T value)
    {
        values[StepValueKey.For<T>()] = value;
    }

    public void Set<T>(string name, T value)
    {
        values[StepValueKey.For<T>(name)] = value;
    }

    public bool TryGet<T>(out T value)
    {
        return TryGet(StepValueKey.For<T>(), out value);
    }

    public bool TryGet<T>(string name, out T value)
    {
        return TryGet(StepValueKey.For<T>(name), out value);
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
