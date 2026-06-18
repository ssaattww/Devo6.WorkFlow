#r "nuget: Devo6.WorkFlow.Engine, 0.1.0"
#r "nuget: YamlDotNet, 16.3.0"

#load "./shared/contracts.csx"
#load "./steps/load/load-text-step.csx"
#load "./steps/parse/parse-document-step.csx"
#load "./steps/normalize/normalize-text-step.csx"
#load "./steps/analyze/analyze-text-step.csx"
#load "./steps/report/build-report-step.csx"
#load "./steps/save/save-text-step.csx"

using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

/// <summary>
/// 文書処理パイプラインの境界 Config です。
/// </summary>
public sealed class TextPipelineConfig
{
    /// <summary>
    /// 読み込み Step の Config です。
    /// </summary>
    public LoadTextStep.Config Load { get; set; } = new();

    /// <summary>
    /// front matter 解析 Step の Config です。
    /// </summary>
    public ParseDocumentStep.Config Parse { get; set; } = new();

    /// <summary>
    /// 本文整形 Step の Config です。
    /// </summary>
    public NormalizeTextStep.Config Normalize { get; set; } = new();

    /// <summary>
    /// 本文分析 Step の Config です。
    /// </summary>
    public AnalyzeTextStep.Config Analyze { get; set; } = new();

    /// <summary>
    /// レポート作成 Step の Config です。
    /// </summary>
    public BuildReportStep.Config Report { get; set; } = new();
}

/// <summary>
/// 複数フォルダに分かれた Step の外側境界 Config です。
/// </summary>
public sealed class MainConfig
{
    /// <summary>
    /// 文書処理パイプラインの Config です。
    /// </summary>
    public TextPipelineConfig Pipeline { get; set; } = new();

    /// <summary>
    /// 保存 Step の Config です。
    /// </summary>
    public SaveTextStep.Config Save { get; set; } = new();
}

/// <summary>
/// レポート向けの処理経路です。
/// </summary>
public enum ReportRoute
{
    /// <summary>
    /// guide 文書向けの経路です。
    /// </summary>
    Guide,

    /// <summary>
    /// reference 文書向けの経路です。
    /// </summary>
    Reference,

    /// <summary>
    /// 既定の経路です。
    /// </summary>
    Default,
}

/// <summary>
/// tag 要約を本文末尾へ追加する Step です。
/// </summary>
public sealed class AppendTagSummaryStep : IStep<AnalyzedDocument>
{
    /// <summary>
    /// tag 要約を本文末尾へ追加します。
    /// </summary>
    /// <param name="input">Step 入力。</param>
    /// <returns>tag 要約を追加した文書。</returns>
    public AnalyzedDocument Execute(StepInput input)
    {
        AnalyzedDocument document = input.Get<AnalyzedDocument>();
        input.Context.Logger.LogInformation("Adding tag summary to document body");

        // summary tag がある文書だけ、後続レポートで確認しやすいよう tag 一覧を本文へ追記します。
        string body = document.Body + Environment.NewLine + "Tags: " + string.Join(", ", document.Metadata.Tags);

        return document with { Body = body };
    }
}

/// <summary>
/// guide 文書の metadata が最低限そろっていることを確認する Step です。
/// </summary>
public sealed class ValidateGuideMetadataStep : IStep<Unit>
{
    /// <summary>
    /// guide 文書に title と tag があることを確認します。
    /// </summary>
    /// <param name="input">Step 入力。</param>
    /// <returns>値を返さないことを表す Unit。</returns>
    public Unit Execute(StepInput input)
    {
        AnalyzedDocument document = input.Get<AnalyzedDocument>();
        input.Context.Logger.LogInformation("Checking guide metadata before report output");

        // guide として扱う文書は、最低限の見出しと tag がない場合に出力前で止めます。
        if (string.IsNullOrWhiteSpace(document.Metadata.Title) || document.Metadata.Tags.Count == 0)
        {
            throw new InvalidOperationException("guide 文書には title と tag が必要です。");
        }

        return Unit.Value;
    }
}

/// <summary>
/// 同じ StepInput を使って内側の文書処理 CompositeStep を実行する Step です。
/// </summary>
public sealed class RunTextPipelineStep : IStep<ReportTextResult>
{
    /// <summary>
    /// 外側 CompositeStep の StepContext を維持したまま内側 CompositeStep を実行し、レポート本文を返します。
    /// </summary>
    /// <param name="input">外側 CompositeStep から渡された Step 入力。</param>
    /// <returns>作成したレポート本文。</returns>
    public ReportTextResult Execute(StepInput input)
    {
        input.Context.Logger.LogInformation("Starting inner text pipeline");

        // 外側 Main は保存だけを担当し、文書処理の詳細は内側 TextPipeline に閉じ込めます。
        CompositeStep<ReportTextResult> textPipeline = CompositeStep.Define("TextPipeline")
            .Run<LoadTextStep, LoadTextResult>()
                .Produce<LoadTextResult>(x => x)
            .Run<ParseDocumentStep, ParsedDocument>()
                .Produce<ParsedDocument>(x => x)
            .Run<NormalizeTextStep, NormalizedDocument>()
                .Produce<NormalizedDocument>(x => x)
            .Run<AnalyzeTextStep, AnalyzedDocument>()
                .Produce<AnalyzedDocument>(x => x)
            .RunIf<AppendTagSummaryStep>(x => x.Metadata.Tags.Contains("summary"))
            .TapIf<ValidateGuideMetadataStep>(x => x.Metadata.Category == "guide")
            .If(
                "DocumentLength",
                x => x.Statistics.WordCount >= 10,
                thenFlow => thenFlow.Run("KeepDetailedDocument", x => x),
                elseFlow => elseFlow.Run("MarkShortDocument", x => x with
                {
                    Body = x.Body + Environment.NewLine + "SHORT DOCUMENT",
                }))
            .Switch<ReportRoute, AnalyzedDocument>(
                "ReportRoute",
                x => x.Metadata.Category switch
                {
                    "guide" => ReportRoute.Guide,
                    "reference" => ReportRoute.Reference,
                    _ => ReportRoute.Default,
                },
                cases => cases
                    .Case(ReportRoute.Guide, branch => branch.Run("UseGuideReport", x => x))
                    .Case(ReportRoute.Reference, branch => branch.Run("UseReferenceReport", x => x))
                    .Default(branch => branch.Run("UseDefaultReport", x => x)))
            .Run<BuildReportStep, ReportTextResult>();

        // 同じ StepInput を渡すことで、外側 Main が読み込んだ Config を内側 Step でも使います。
        ReportTextResult result = textPipeline.Execute(input);
        input.Context.Logger.LogInformation("Finished inner text pipeline");

        return result;
    }
}

// Main の境界 Config から、内側 TextPipeline の各 Step Config と保存先 Config をまとめて指定します。
var Main = CompositeStep.Define("Main")
    .Run<RunTextPipelineStep, ReportTextResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadTextStep.Config>("Pipeline.Load", "steps/load/appsettings.yaml")
        .WithConfig<ParseDocumentStep.Config>("Pipeline.Parse", "steps/parse/appsettings.yaml")
        .WithConfig<NormalizeTextStep.Config>("Pipeline.Normalize", "steps/normalize/appsettings.yaml")
        .WithConfig<AnalyzeTextStep.Config>("Pipeline.Analyze", "steps/analyze/appsettings.yaml")
        .WithConfig<BuildReportStep.Config>("Pipeline.Report", "steps/report/appsettings.yaml")
        .Produce<SaveTextInput>(x => new SaveTextInput(x.Text))
    .Run<SaveTextStep, Unit>()
        .WithConfig<SaveTextStep.Config>("Save")
        .Discard();
