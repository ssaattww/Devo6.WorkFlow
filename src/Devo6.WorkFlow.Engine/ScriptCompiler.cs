using Dotnet.Script.Core;
using Dotnet.Script.DependencyModel.Logging;
using Dotnet.Script.DependencyModel.Runtime;
using Microsoft.CodeAnalysis.Scripting;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// Engine 内の既存呼び出しを保ったまま、dotnet-script 標準キャッシュを使うコンパイラです。
/// </summary>
internal sealed class ScriptCompiler
{
    private readonly Dotnet.Script.Core.ScriptCompiler innerCompiler;

    /// <summary>
    /// dotnet-script のコンパイラを作成します。
    /// </summary>
    /// <param name="logFactory">dotnet-script のログ生成器。</param>
    /// <param name="legacyWorkflowDirectory">旧実装がキャッシュとして渡していた workflow directory。</param>
    /// <param name="useRestoreCache">NuGet 復元キャッシュを使う場合は true。</param>
    public ScriptCompiler(LogFactory logFactory, string legacyWorkflowDirectory, bool useRestoreCache)
    {
        innerCompiler = new Dotnet.Script.Core.ScriptCompiler(
            logFactory,
            DotnetScriptCachePathPolicy.Resolve(legacyWorkflowDirectory),
            useRestoreCache);
    }

    /// <summary>
    /// Roslyn script の参照、import、resolver を作成します。
    /// </summary>
    /// <param name="context">dotnet-script のコンパイル文脈。</param>
    /// <param name="runtimeDependencies">解決済み実行時依存関係。</param>
    /// <returns>Roslyn script option。</returns>
    public ScriptOptions CreateScriptOptions(
        ScriptContext context,
        IList<RuntimeDependency> runtimeDependencies)
    {
        return innerCompiler.CreateScriptOptions(context, runtimeDependencies);
    }

    /// <summary>
    /// NuGet 復元済みの dotnet-script コンパイル文脈を作成します。
    /// </summary>
    /// <typeparam name="TReturn">script の戻り値型。</typeparam>
    /// <typeparam name="THost">script の host 型。</typeparam>
    /// <param name="context">dotnet-script のコンパイル文脈。</param>
    /// <returns>依存関係と Roslyn script を含むコンパイル文脈。</returns>
    public ScriptCompilationContext<TReturn> CreateCompilationContext<TReturn, THost>(ScriptContext context)
    {
        return innerCompiler.CreateCompilationContext<TReturn, THost>(context);
    }
}

/// <summary>
/// 旧 workflow directory 指定を dotnet-script 標準キャッシュ指定へ変換します。
/// </summary>
internal static class DotnetScriptCachePathPolicy
{
    /// <summary>
    /// dotnet-script に渡す既定キャッシュ path を解決します。
    /// </summary>
    /// <param name="legacyWorkflowDirectory">旧実装がキャッシュとして渡していた workflow directory。</param>
    /// <returns>標準キャッシュへ委譲するため常に null。</returns>
    public static string? Resolve(string legacyWorkflowDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyWorkflowDirectory);

        return null;
    }
}
