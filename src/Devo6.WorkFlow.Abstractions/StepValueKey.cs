namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// Step 入出力値を型と任意の名前で識別する key を表します。
/// </summary>
public readonly struct StepValueKey : IEquatable<StepValueKey>
{
    /// <summary>
    /// 型と名前から Step 値 key を作成します。
    /// </summary>
    /// <param name="valueType">key が表す値の型。</param>
    /// <param name="name">名前付き値の名前。型だけで識別する場合は null。</param>
    private StepValueKey(Type valueType, string? name)
    {
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
        Name = name;
    }

    /// <summary>
    /// key が表す値の型を取得します。
    /// </summary>
    public Type ValueType { get; }

    /// <summary>
    /// 名前付き値の名前を取得します。
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// 型だけで値を識別する key を作成します。
    /// </summary>
    /// <typeparam name="T">識別対象の値の型。</typeparam>
    /// <returns>型だけで識別する Step 値 key。</returns>
    public static StepValueKey For<T>()
    {
        return new StepValueKey(typeof(T), null);
    }

    /// <summary>
    /// 型と名前で値を識別する key を作成します。
    /// </summary>
    /// <typeparam name="T">識別対象の値の型。</typeparam>
    /// <param name="name">識別対象の値の名前。</param>
    /// <returns>型と名前で識別する Step 値 key。</returns>
    public static StepValueKey For<T>(string name)
    {
        return new StepValueKey(typeof(T), ValidateName(name));
    }

    /// <summary>
    /// 指定された key と同じ型および名前を表すかどうかを判定します。
    /// </summary>
    /// <param name="other">比較対象の key。</param>
    /// <returns>同じ型および名前を表す場合は true。</returns>
    public bool Equals(StepValueKey other)
    {
        return ValueType == other.ValueType && string.Equals(Name, other.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// 指定された object が同じ Step 値 key かどうかを判定します。
    /// </summary>
    /// <param name="obj">比較対象の object。</param>
    /// <returns>同じ Step 値 key の場合は true。</returns>
    public override bool Equals(object? obj)
    {
        return obj is StepValueKey other && Equals(other);
    }

    /// <summary>
    /// key の hash code を取得します。
    /// </summary>
    /// <returns>型と名前に基づく hash code。</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(ValueType, Name);
    }

    /// <summary>
    /// key を診断用文字列として表します。
    /// </summary>
    /// <returns>型と名前を含む文字列表現。</returns>
    public override string ToString()
    {
        return Name is null
            ? ValueType.FullName ?? ValueType.Name
            : $"{ValueType.FullName ?? ValueType.Name} named '{Name}'";
    }

    /// <summary>
    /// 2 つの key が同じ型および名前を表すかどうかを判定します。
    /// </summary>
    /// <param name="left">左辺の key。</param>
    /// <param name="right">右辺の key。</param>
    /// <returns>同じ型および名前を表す場合は true。</returns>
    public static bool operator ==(StepValueKey left, StepValueKey right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// 2 つの key が異なる型または名前を表すかどうかを判定します。
    /// </summary>
    /// <param name="left">左辺の key。</param>
    /// <param name="right">右辺の key。</param>
    /// <returns>異なる型または名前を表す場合は true。</returns>
    public static bool operator !=(StepValueKey left, StepValueKey right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// 名前付き値の名前として使える文字列かどうかを検証します。
    /// </summary>
    /// <param name="name">検証対象の名前。</param>
    /// <returns>検証済みの名前。</returns>
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
