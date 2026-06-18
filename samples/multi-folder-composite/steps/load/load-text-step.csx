using System.IO;
using Devo6.WorkFlow.Abstractions;
using Microsoft.Extensions.Logging;

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

        // サンプルを別ディレクトリから実行してもよいよう、入力は Entry .csx の場所から解決します。
        input.Context.Logger.LogInformation("Loading source text from {Path}", config.Path);

        return new LoadTextResult(File.ReadAllText(inputPath));
    }
}
