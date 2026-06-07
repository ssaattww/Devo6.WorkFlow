namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// workflow Step が StepContext から参照できる command-line engine 引数を保持します。
/// </summary>
public sealed class EngineArguments
{
    /// <summary>
    /// workflow 実行に選択された entry .csx file path を取得します。
    /// </summary>
    public string EntryPath { get; init; } = "";

    /// <summary>
    /// 解決済み config file path を取得します。config file が指定されていない場合は空文字列です。
    /// </summary>
    public string ConfigPath { get; init; } = "";

    /// <summary>
    /// 複数の --set key=value 引数から渡された文字列 override 設定を取得します。
    /// </summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();
}
