using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// Step 実行中に共有される logger と値を保持します。
/// </summary>
public sealed class StepContext
{
    private readonly Dictionary<StepValueKey, object?> values = new();

    /// <summary>
    /// 既定 logger を持つ Step context を作成します。
    /// </summary>
    public StepContext()
        : this(null)
    {
    }

    /// <summary>
    /// 指定された logger を持つ Step context を作成します。
    /// </summary>
    /// <param name="logger">Step から使用する logger。null の場合は null logger。</param>
    public StepContext(ILogger? logger)
    {
        Logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Step から使用する logger を取得します。
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// 型だけで識別される context 値を取得します。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <returns>登録済みの context 値。</returns>
    public T Get<T>()
    {
        return Get<T>(StepValueKey.For<T>());
    }

    /// <summary>
    /// 型と名前で識別される context 値を取得します。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="name">取得する値の名前。</param>
    /// <returns>登録済みの context 値。</returns>
    public T Get<T>(string name)
    {
        return Get<T>(StepValueKey.For<T>(name));
    }

    /// <summary>
    /// 型だけで識別する context 値を設定します。
    /// </summary>
    /// <typeparam name="T">設定する値の型。</typeparam>
    /// <param name="value">設定する値。</param>
    public void Set<T>(T value)
    {
        values[StepValueKey.For<T>()] = value;
    }

    /// <summary>
    /// 型と名前で識別する context 値を設定します。
    /// </summary>
    /// <typeparam name="T">設定する値の型。</typeparam>
    /// <param name="name">設定する値の名前。</param>
    /// <param name="value">設定する値。</param>
    public void Set<T>(string name, T value)
    {
        values[StepValueKey.For<T>(name)] = value;
    }

    /// <summary>
    /// 型だけで識別される context 値の取得を試みます。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="value">取得できた context 値。</param>
    /// <returns>値を取得できた場合は true。</returns>
    public bool TryGet<T>(out T value)
    {
        return TryGet(StepValueKey.For<T>(), out value);
    }

    /// <summary>
    /// 型と名前で識別される context 値の取得を試みます。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="name">取得する値の名前。</param>
    /// <param name="value">取得できた context 値。</param>
    /// <returns>値を取得できた場合は true。</returns>
    public bool TryGet<T>(string name, out T value)
    {
        return TryGet(StepValueKey.For<T>(name), out value);
    }

    /// <summary>
    /// 指定された key で context 値を取得します。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="key">値を識別する key。</param>
    /// <returns>登録済みの context 値。</returns>
    private T Get<T>(StepValueKey key)
    {
        if (!TryGet(key, out T value))
        {
            throw new KeyNotFoundException($"No value is registered for {key}.");
        }

        return value;
    }

    /// <summary>
    /// 指定された key で context 値の取得を試みます。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="key">値を識別する key。</param>
    /// <param name="value">取得できた context 値。</param>
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
