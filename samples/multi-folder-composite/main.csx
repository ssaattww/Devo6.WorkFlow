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

var Main = CompositeStep.Define("Main")
    .Run<LoadTextStep, LoadTextResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadTextStep.Config>("Load")
        .Produce<ConvertTextInput>(x => new ConvertTextInput(x.Text))
    .Run<ConvertTextStep, ConvertTextResult>()
        .WithConfig<ConvertTextStep.Config>("Convert")
        .Produce<SaveTextInput>(x => new SaveTextInput(x.ConvertedText))
    .Run<SaveTextStep, Unit>()
        .WithConfig<SaveTextStep.Config>("Save")
        .Discard();
