namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// workflow entry 実行後に engine が返す観測可能な結果を表します。
/// </summary>
public sealed class WorkflowResult
{
    /// <summary>
    /// 実行された entry 名を取得します。
    /// </summary>
    public string EntryName { get; init; } = "";

    /// <summary>
    /// entry が engine level の失敗なく完了したかどうかを取得します。
    /// </summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// 実行が失敗した場合の workflow error code を取得します。
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// 実行が失敗した場合の人が読める失敗 message を取得します。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 実行で記録された構造化 trace を取得します。
    /// </summary>
    public ExecutionTrace? Trace { get; init; }
}
