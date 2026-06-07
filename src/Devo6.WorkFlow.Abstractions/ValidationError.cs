namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// 利用者が原因を特定できる location と code 情報を持つ validation 問題を表します。
/// </summary>
public sealed class ValidationError
{
    /// <summary>
    /// 無効な entry、Step、入力、または config member への利用者向け path を取得します。
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// 安定した validation error code を取得します。
    /// </summary>
    public string Code { get; init; } = "";

    /// <summary>
    /// 人が読める validation message を取得します。
    /// </summary>
    public string Message { get; init; } = "";
}
