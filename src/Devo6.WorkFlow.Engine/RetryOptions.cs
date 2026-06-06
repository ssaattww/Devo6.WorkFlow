namespace Devo6.WorkFlow.Engine;

/// <summary>
/// Step 本体の通常例外に適用する retry 設定を表します。
/// </summary>
public sealed class RetryOptions
{
    /// <summary>
    /// 初回を含む最大試行回数を取得または設定します。1 以下の場合は retry を行いません。
    /// </summary>
    public int MaxAttempts { get; set; }
}
