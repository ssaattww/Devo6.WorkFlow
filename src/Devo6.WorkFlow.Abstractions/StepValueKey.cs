namespace Devo6.WorkFlow.Abstractions;

public readonly struct StepValueKey : IEquatable<StepValueKey>
{
    private StepValueKey(Type valueType, string? name)
    {
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
        Name = name;
    }

    public Type ValueType { get; }

    public string? Name { get; }

    public static StepValueKey For<T>()
    {
        return new StepValueKey(typeof(T), null);
    }

    public static StepValueKey For<T>(string name)
    {
        return new StepValueKey(typeof(T), ValidateName(name));
    }

    public bool Equals(StepValueKey other)
    {
        return ValueType == other.ValueType && string.Equals(Name, other.Name, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is StepValueKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ValueType, Name);
    }

    public override string ToString()
    {
        return Name is null
            ? ValueType.FullName ?? ValueType.Name
            : $"{ValueType.FullName ?? ValueType.Name} named '{Name}'";
    }

    public static bool operator ==(StepValueKey left, StepValueKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(StepValueKey left, StepValueKey right)
    {
        return !left.Equals(right);
    }

    private static string ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Value name must not be empty or whitespace.", nameof(name));
        }

        return name;
    }
}
