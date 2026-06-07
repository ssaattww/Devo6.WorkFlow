using Devo6.WorkFlow.Abstractions;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// .csx workflow entry の実行前検証結果を表します。
/// </summary>
public sealed class WorkflowValidationResult
{
    /// <summary>
    /// 検証が error なしで完了したかどうかを取得します。
    /// </summary>
    public bool Succeeded => Errors.Count == 0;

    /// <summary>
    /// workflow 実行前に見つかった検証 error を取得します。
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];
}

/// <summary>
/// .csx workflow entry の実行前検証に使う追加入力を設定します。
/// </summary>
public sealed class CsxValidationOptions
{
    /// <summary>
    /// 存在を確認する config file path を取得します。相対 path は entry .csx directory から解決します。
    /// </summary>
    public IReadOnlyList<string> ConfigPaths { get; init; } = [];
}
