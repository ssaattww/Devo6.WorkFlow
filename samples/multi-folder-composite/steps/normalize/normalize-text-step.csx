using System;
using System.Linq;
using System.Text.RegularExpressions;
using Devo6.WorkFlow.Abstractions;

/// <summary>
/// 本文の空白と大小文字を整形する Step です。
/// </summary>
public sealed class NormalizeTextStep : IStep<NormalizedDocument>
{
    /// <summary>
    /// 本文整形設定です。
    /// </summary>
    public sealed class Config
    {
        /// <summary>
        /// 行の前後空白を削除するかどうかを取得または設定します。
        /// </summary>
        public bool TrimLines { get; set; } = true;

        /// <summary>
        /// 連続する空白を 1 つにまとめるかどうかを取得または設定します。
        /// </summary>
        public bool CollapseWhitespace { get; set; } = true;

        /// <summary>
        /// 本文を大文字へ変換するかどうかを取得または設定します。
        /// </summary>
        public bool Uppercase { get; set; } = true;
    }

    /// <summary>
    /// 本文へ設定された整形処理を適用します。
    /// </summary>
    /// <param name="input">Step 入力。</param>
    /// <returns>整形済み本文を持つ文書。</returns>
    public NormalizedDocument Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        ParsedDocument document = input.Get<ParsedDocument>();
        string[] lines = document.Body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => NormalizeLine(line, config))
            .Where(line => line.Length > 0)
            .ToArray();
        string body = string.Join(Environment.NewLine, lines);

        return new NormalizedDocument(document.Metadata, body);
    }

    /// <summary>
    /// 1 行へ設定された整形処理を適用します。
    /// </summary>
    /// <param name="line">整形する行。</param>
    /// <param name="config">本文整形設定。</param>
    /// <returns>整形済みの行。</returns>
    private static string NormalizeLine(string line, Config config)
    {
        string normalized = config.TrimLines ? line.Trim() : line;
        if (config.CollapseWhitespace)
        {
            normalized = Regex.Replace(normalized, "\\s+", " ");
        }

        return config.Uppercase ? normalized.ToUpperInvariant() : normalized;
    }
}
