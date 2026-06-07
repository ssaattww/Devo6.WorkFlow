using System.IO;
using Devo6.WorkFlow.Abstractions;

/// <summary>
/// 変換結果を Entry ファイルからの相対パスへ保存する Step です。
/// </summary>
public sealed class SaveTextStep : IStep<Unit>
{
    /// <summary>
    /// 保存設定です。
    /// </summary>
    public sealed class Config
    {
        /// <summary>
        /// Entry ファイルからの相対出力パスです。
        /// </summary>
        public string Path { get; set; } = "";
    }

    /// <summary>
    /// 変換済みの文字列をファイルへ保存します。
    /// </summary>
    /// <param name="input">Step 入力。</param>
    /// <returns>値を返さないことを表す Unit。</returns>
    public Unit Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        EngineArguments arguments = input.Context.Get<EngineArguments>();
        SaveTextInput saveInput = input.Get<SaveTextInput>();
        string entryDirectory = Path.GetDirectoryName(arguments.EntryPath)!;
        string outputPath = Path.Combine(entryDirectory, config.Path);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, saveInput.Content);

        return Unit.Value;
    }
}
