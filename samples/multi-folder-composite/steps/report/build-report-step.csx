using System;
using System.Linq;
using System.Text;
using Devo6.WorkFlow.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>
/// metadata、統計、本文をレポート文字列へまとめる Step です。
/// </summary>
public sealed class BuildReportStep : IStep<ReportTextResult>
{
    /// <summary>
    /// レポート作成設定です。
    /// </summary>
    public sealed class Config
    {
        /// <summary>
        /// レポートの見出しです。
        /// </summary>
        public string Heading { get; set; } = "Document report";

        /// <summary>
        /// 本文区画の見出しです。
        /// </summary>
        public string BodyHeading { get; set; } = "Body";
    }

    /// <summary>
    /// 統計付き文書からレポート文字列を作成します。
    /// </summary>
    /// <param name="input">Step 入力。</param>
    /// <returns>レポート文字列。</returns>
    public ReportTextResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        AnalyzedDocument document = input.Get<AnalyzedDocument>();
        input.Context.Logger.LogInformation("Building report with heading {Heading}", config.Heading);

        // 設定で差し替えられる見出しと、前段で作った metadata / 統計 / 本文を 1 つの出力にまとめます。
        return new ReportTextResult(BuildReport(config, document));
    }

    /// <summary>
    /// レポート文字列を組み立てます。
    /// </summary>
    /// <param name="config">レポート作成設定。</param>
    /// <param name="document">統計付き文書。</param>
    /// <returns>組み立てたレポート文字列。</returns>
    private static string BuildReport(Config config, AnalyzedDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# " + config.Heading);
        builder.AppendLine();
        builder.AppendLine("Title: " + document.Metadata.Title);
        builder.AppendLine("Category: " + document.Metadata.Category);
        builder.AppendLine("Tags: " + string.Join(", ", document.Metadata.Tags));
        builder.AppendLine("Line count: " + document.Statistics.LineCount);
        builder.AppendLine("Word count: " + document.Statistics.WordCount);
        builder.AppendLine("Character count: " + document.Statistics.CharacterCount);
        builder.AppendLine("Tag count: " + document.Statistics.TagCount);
        builder.AppendLine();
        builder.AppendLine("## " + config.BodyHeading);
        builder.Append(document.Body);

        return builder.ToString();
    }
}
