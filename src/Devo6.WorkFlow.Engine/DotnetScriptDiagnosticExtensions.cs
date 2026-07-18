using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// Roslyn script の診断を dotnet-script 2.0.1 と同じ失敗境界へ補正します。
/// </summary>
internal static class DotnetScriptDiagnosticExtensions
{
    /// <summary>
    /// 呼び出し元の抽出条件に加え、dotnet-script 2.0.1 が error へ昇格する nullable 診断を返します。
    /// </summary>
    /// <param name="diagnostics">Roslyn script が返した診断。</param>
    /// <param name="predicate">既存の診断抽出条件。</param>
    /// <returns>既存条件または dotnet-script nullable 診断条件に一致する診断。</returns>
    public static IEnumerable<Diagnostic> Where(
        this ImmutableArray<Diagnostic> diagnostics,
        Func<Diagnostic, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return System.Linq.Enumerable.Where(
            diagnostics,
            diagnostic => predicate(diagnostic) || IsDotnetScriptNullableDiagnostic(diagnostic));
    }

    /// <summary>
    /// dotnet-script 2.0.1 が error へ昇格する nullable 診断かどうかを判定します。
    /// </summary>
    /// <param name="diagnostic">判定する Roslyn 診断。</param>
    /// <returns>CS8600 から CS8655 の場合は true。</returns>
    private static bool IsDotnetScriptNullableDiagnostic(Diagnostic diagnostic)
    {
        return diagnostic.Id.Length == 6
            && diagnostic.Id.StartsWith("CS86", StringComparison.Ordinal)
            && int.TryParse(diagnostic.Id.AsSpan(2), out int diagnosticNumber)
            && diagnosticNumber is >= 8600 and <= 8655;
    }
}
