using Microsoft.Extensions.Logging;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// ログ本文を検査し、指定された通知に合わせて同期処理を起動する logger factory です。
/// </summary>
internal sealed class CancellationTriggerLoggerFactory : ILoggerFactory, ILogger
{
    /// <summary>
    /// 登録済みの通知先を初期化します。
    /// </summary>
    /// <param name="onMessage">受信したログ本文ごとに呼び出す通知先。</param>
    public CancellationTriggerLoggerFactory(Action<string> onMessage)
    {
        ArgumentNullException.ThrowIfNull(onMessage);
        this.onMessage = onMessage;
    }

    private readonly Action<string> onMessage;

    /// <summary>
    /// 外部 provider の登録はこの fixture では行いません。
    /// </summary>
    /// <param name="provider">登録を要求された logger provider。</param>
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <summary>
    /// 全 category でこの factory 自身を logger として返します。
    /// </summary>
    /// <param name="categoryName">logger の category 名。</param>
    /// <returns>factory 内の記録 logger。</returns>
    public ILogger CreateLogger(string categoryName) => this;

    /// <summary>
    /// この fixture に破棄する外部資源がないことを表します。
    /// </summary>
    public void Dispose()
    {
    }

    /// <summary>
    /// scope を記録せず、空の disposable scope を返します。
    /// </summary>
    /// <typeparam name="TState">scope に渡された state の型。</typeparam>
    /// <param name="state">scope を説明する state。</param>
    /// <returns>破棄処理が不要な null scope。</returns>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <summary>
    /// cancellation 境界試験ではすべての log level を有効にします。
    /// </summary>
    /// <param name="logLevel">確認する log level。</param>
    /// <returns>常に true。</returns>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <summary>
    /// formatter の本文を同期 callback へ渡します。
    /// </summary>
    /// <typeparam name="TState">logger に渡された state の型。</typeparam>
    /// <param name="logLevel">log level。</param>
    /// <param name="eventId">log event の識別子。</param>
    /// <param name="state">log formatter へ渡す state。</param>
    /// <param name="exception">log に付随する例外。</param>
    /// <param name="formatter">state と例外を本文へ変換する formatter。</param>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        onMessage(formatter(state, exception));
    }
}
