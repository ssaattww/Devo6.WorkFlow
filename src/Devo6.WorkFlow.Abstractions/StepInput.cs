namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// Step に渡される入力値と実行 context を保持します。
/// </summary>
public sealed class StepInput
{
    private readonly Dictionary<StepValueKey, object?> values = new();

    /// <summary>
    /// 既定の実行 context を持つ入力を作成します。
    /// </summary>
    public StepInput()
        : this(null)
    {
    }

    /// <summary>
    /// 指定された実行 context を持つ入力を作成します。
    /// </summary>
    /// <param name="context">入力に関連付ける実行 context。null の場合は既定 context。</param>
    public StepInput(StepContext? context)
    {
        Context = context ?? new StepContext();
        values.Add(StepValueKey.For<StepContext>(), Context);
    }

    /// <summary>
    /// 入力に関連付けられた実行 context を取得します。
    /// </summary>
    public StepContext Context { get; }

    /// <summary>
    /// 型だけで識別する入力値を追加します。
    /// </summary>
    /// <typeparam name="T">追加する値の型。</typeparam>
    /// <param name="value">追加する値。</param>
    internal void Add<T>(T value)
    {
        Add(StepValueKey.For<T>(), value);
    }

    /// <summary>
    /// 型と名前で識別する入力値を追加します。
    /// </summary>
    /// <typeparam name="T">追加する値の型。</typeparam>
    /// <param name="name">追加する値の名前。</param>
    /// <param name="value">追加する値。</param>
    internal void Add<T>(string name, T value)
    {
        Add(StepValueKey.For<T>(name), value);
    }

    /// <summary>
    /// 型だけで識別される入力値を取得します。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <returns>登録済みの入力値。</returns>
    public T Get<T>()
    {
        return Get<T>(StepValueKey.For<T>());
    }

    /// <summary>
    /// 型と名前で識別される入力値を取得します。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="name">取得する値の名前。</param>
    /// <returns>登録済みの入力値。</returns>
    public T Get<T>(string name)
    {
        return Get<T>(StepValueKey.For<T>(name));
    }

    /// <summary>
    /// 型だけで識別される入力値の取得を試みます。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="value">取得できた入力値。</param>
    /// <returns>値を取得できた場合は true。</returns>
    public bool TryGet<T>(out T value)
    {
        return TryGet(StepValueKey.For<T>(), out value);
    }

    /// <summary>
    /// 型と名前で識別される入力値の取得を試みます。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="name">取得する値の名前。</param>
    /// <param name="value">取得できた入力値。</param>
    /// <returns>値を取得できた場合は true。</returns>
    public bool TryGet<T>(string name, out T value)
    {
        return TryGet(StepValueKey.For<T>(name), out value);
    }

    /// <summary>
    /// 指定された key で入力値を追加します。
    /// </summary>
    /// <typeparam name="T">追加する値の型。</typeparam>
    /// <param name="key">値を識別する key。</param>
    /// <param name="value">追加する値。</param>
    private void Add<T>(StepValueKey key, T value)
    {
        if (values.ContainsKey(key))
        {
            throw new InvalidOperationException($"A value is already registered for {key}.");
        }

        values.Add(key, value);
    }

    /// <summary>
    /// 指定された key で入力値を取得します。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="key">値を識別する key。</param>
    /// <returns>登録済みの入力値。</returns>
    private T Get<T>(StepValueKey key)
    {
        if (!TryGet(key, out T value))
        {
            throw new KeyNotFoundException($"No value is registered for {key}.");
        }

        return value;
    }

    /// <summary>
    /// 指定された key で入力値の取得を試みます。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="key">値を識別する key。</param>
    /// <param name="value">取得できた入力値。</param>
    /// <returns>値を取得できた場合は true。</returns>
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
