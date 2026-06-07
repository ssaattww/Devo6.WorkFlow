namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// 値を返さない Step 出力を表します。
/// </summary>
public readonly struct Unit
{
    /// <summary>
    /// Unit 型の既定値を表します。
    /// </summary>
    public static readonly Unit Value = new();
}
