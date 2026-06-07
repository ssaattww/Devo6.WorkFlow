#load "./shared/contracts.csx"
#load "./steps/load/load-text-step.csx"
#load "./steps/convert/convert-text-step.csx"
#load "./steps/save/save-text-step.csx"

using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

/// <summary>
/// 複数フォルダに分かれた Step の境界 Config です。
/// </summary>
public sealed class MainConfig
{
    /// <summary>
    /// 読み込み Step の Config です。
    /// </summary>
    public LoadTextStep.Config Load { get; set; } = new();

    /// <summary>
    /// 変換 Step の Config です。
    /// </summary>
    public ConvertTextStep.Config Convert { get; set; } = new();

    /// <summary>
    /// 保存 Step の Config です。
    /// </summary>
    public SaveTextStep.Config Save { get; set; } = new();
}

/// <summary>
/// 同じ StepInput を使って内側のテキスト処理 CompositeStep を実行する Step です。
/// </summary>
public sealed class RunTextPipelineStep : IStep<Unit>
{
    /// <summary>
    /// 外側 CompositeStep の StepContext を維持したまま内側 CompositeStep を実行します。
    /// </summary>
    /// <param name="input">外側 CompositeStep から渡された Step 入力。</param>
    /// <returns>値を返さないことを表す Unit。</returns>
    public Unit Execute(StepInput input)
    {
        CompositeStep<Unit> textPipeline = CompositeStep.Define("TextPipeline")
            .Run<LoadTextStep, LoadTextResult>()
                .Produce<ConvertTextInput>(x => new ConvertTextInput(x.Text))
            .Run<ConvertTextStep, ConvertTextResult>()
                .Produce<SaveTextInput>(x => new SaveTextInput(x.ConvertedText))
            .Run<SaveTextStep, Unit>()
                .Discard();

        return textPipeline.Execute(input);
    }
}

var Main = CompositeStep.Define("Main")
    .Run<RunTextPipelineStep, Unit>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadTextStep.Config>("Load")
        .WithConfig<ConvertTextStep.Config>("Convert")
        .WithConfig<SaveTextStep.Config>("Save")
        .Discard();
