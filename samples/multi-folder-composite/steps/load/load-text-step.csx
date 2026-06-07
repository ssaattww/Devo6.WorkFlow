using System.IO;
using Devo6.WorkFlow.Abstractions;

/// <summary>
/// Entry ファイルからの相対パスでテキストを読み込む Step です。
/// </summary>
public sealed class LoadTextStep : IStep<LoadTextResult>
{
    /// <summary>
    /// 読み込み設定です。
    /// </summary>
    public sealed class Config
    {
        /// <summary>
        /// Entry ファイルからの相対入力パスです。
        /// </summary>
        public string Path { get; set; } = "";
    }

    /// <summary>
    /// 設定されたファイルから文字列を読み込みます。
    /// </summary>
    /// <param name="input">Step 入力。</param>
    /// <returns>読み込んだ文字列。</returns>
    public LoadTextResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        EngineArguments arguments = input.Context.Get<EngineArguments>();
        string entryDirectory = Path.GetDirectoryName(arguments.EntryPath)!;
        string inputPath = Path.Combine(entryDirectory, config.Path);

        return new LoadTextResult(File.ReadAllText(inputPath).Trim());
    }
}
