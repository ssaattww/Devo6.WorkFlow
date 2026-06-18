using System;
using System.Linq;
using System.Text.RegularExpressions;
using Devo6.WorkFlow.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>
/// 整形済み本文の統計を算出する Step です。
/// </summary>
public sealed class AnalyzeTextStep : IStep<AnalyzedDocument>
{
    /// <summary>
    /// 本文分析設定です。
    /// </summary>
    public sealed class Config
    {
        /// <summary>
        /// 空行を行数に含めるかどうかを取得または設定します。
        /// </summary>
        public bool CountEmptyLines { get; set; }

        /// <summary>
        /// 改行を文字数に含めるかどうかを取得または設定します。
        /// </summary>
        public bool CountLineBreaksAsCharacters { get; set; } = true;
    }

    /// <summary>
    /// 整形済み本文と metadata から統計を算出します。
    /// </summary>
    /// <param name="input">Step 入力。</param>
    /// <returns>統計付き文書。</returns>
    public AnalyzedDocument Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        NormalizedDocument document = input.Get<NormalizedDocument>();
        input.Context.Logger.LogInformation("Analyzing normalized body text");

        // レポート本文に載せるため、行数、語数、文字数、tag 数をここでまとめて算出します。
        string[] lines = SplitLines(document.Body);
        int lineCount = config.CountEmptyLines ? lines.Length : lines.Count(line => line.Length > 0);
        int wordCount = Regex.Matches(document.Body, "\\S+").Count;
        int characterCount = config.CountLineBreaksAsCharacters
            ? document.Body.Length
            : lines.Sum(line => line.Length);
        var statistics = new TextStatistics(lineCount, wordCount, characterCount, document.Metadata.Tags.Count);

        return new AnalyzedDocument(document.Metadata, document.Body, statistics);
    }

    /// <summary>
    /// 本文を LF 基準の行に分割します。
    /// </summary>
    /// <param name="body">分割する本文。</param>
    /// <returns>分割した行。</returns>
    private static string[] SplitLines(string body)
    {
        return body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    }
}
