namespace Devo6.WorkFlow.Tests;

/// <summary>
/// 検査実行時のビルド構成を解決します。
/// </summary>
internal static class TestBuildConfiguration
{
    /// <summary>
    /// 現在の検査用アセンブリ配置先からビルド構成名を取得します。
    /// </summary>
    public static string Current => ResolveCurrent();

    /// <summary>
    /// 実行基準ディレクトリの bin 配下からビルド構成名を探索します。
    /// </summary>
    /// <returns>検出したビルド構成名。</returns>
    private static string ResolveCurrent()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory?.Parent is not null)
        {
            if (string.Equals(directory.Parent.Name, "bin", StringComparison.OrdinalIgnoreCase))
            {
                return directory.Name;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("検査実行時のビルド構成を特定できませんでした。");
    }
}
