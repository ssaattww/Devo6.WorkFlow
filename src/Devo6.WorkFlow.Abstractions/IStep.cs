namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// 同期的に出力を生成する workflow Step を定義します。
/// </summary>
/// <typeparam name="TOut">Step が生成する出力の型。</typeparam>
public interface IStep<TOut>
{
    /// <summary>
    /// 現在の入力値を使って Step を実行します。
    /// </summary>
    /// <param name="input">Step が参照できる入力値。</param>
    /// <returns>Step が生成した出力。</returns>
    TOut Execute(StepInput input);
}
