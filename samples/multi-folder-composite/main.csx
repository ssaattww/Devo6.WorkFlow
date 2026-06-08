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
        CompositeStep<ReportTextResult> textPipeline = CompositeStep.Define("TextPipeline")
            .Run<LoadTextStep, LoadTextResult>()
                .Produce<LoadTextResult>(x => x)
            .Run<ParseDocumentStep, ParsedDocument>()
                .Produce<ParsedDocument>(x => x)
            .Run<NormalizeTextStep, NormalizedDocument>()
                .Produce<NormalizedDocument>(x => x)
            .Run<AnalyzeTextStep, AnalyzedDocument>()
                .Produce<AnalyzedDocument>(x => x)
            .Run<BuildReportStep, ReportTextResult>();

        return textPipeline.Execute(input);
    }
}

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
