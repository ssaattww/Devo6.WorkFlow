namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// 非同期に出力を生成する workflow Step を定義します。
/// </summary>
/// <typeparam name="TOut">Step が生成する出力の型。</typeparam>
public interface IAsyncStep<TOut>
{
    /// <summary>
    /// 現在の入力値を使って Step を非同期に実行します。
    /// </summary>
    /// <param name="input">Step が参照できる入力値。</param>
    /// <param name="cancellationToken">engine から渡された cancel token。</param>
    /// <returns>Step が非同期に生成した出力。</returns>
    Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken);
}
