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
    /// 解決済み workflow config file path を取得します。workflow config file が指定されていない場合は空文字列です。
    /// </summary>
    public string WorkflowConfigPath { get; init; } = "";

    /// <summary>
    /// 解決済み engine config file path を取得します。engine config file が指定されていない場合は空文字列です。
    /// </summary>
    public string EngineConfigPath { get; init; } = "";

    /// <summary>
    /// workflow config override を文字列として保持する workflow 設定項目を取得します。
    /// </summary>
    public IReadOnlyDictionary<string, string> WorkflowSettings { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// engine config override を文字列として保持する engine 設定項目を取得します。
    /// </summary>
    public IReadOnlyDictionary<string, string> EngineSettings { get; init; } = new Dictionary<string, string>();

}
