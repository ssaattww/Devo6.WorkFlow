using System;
using System.Collections.Generic;
using System.IO;
using Devo6.WorkFlow.Abstractions;
using YamlDotNet.RepresentationModel;

/// <summary>
/// YAML front matter 付き文書を metadata と本文に分ける Step です。
/// </summary>
public sealed class ParseDocumentStep : IStep<ParsedDocument>
{
    /// <summary>
    /// front matter 解析設定です。
    /// </summary>
    public sealed class Config
    {
        /// <summary>
        /// front matter の区切り文字です。
        /// </summary>
        public string FrontMatterDelimiter { get; set; } = "---";

        /// <summary>
        /// front matter が無い文書を失敗にするかどうかを取得または設定します。
        /// </summary>
        public bool RequireFrontMatter { get; set; } = true;
    }

    /// <summary>
    /// 読み込んだ文字列から YAML front matter と本文を取り出します。
    /// </summary>
    /// <param name="input">Step 入力。</param>
    /// <returns>metadata と本文に分けた文書。</returns>
    public ParsedDocument Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        LoadTextResult loadResult = input.Get<LoadTextResult>();
        (string frontMatter, string body) = SplitFrontMatter(loadResult.Text, config.FrontMatterDelimiter, config.RequireFrontMatter);
        DocumentMetadata metadata = ParseMetadata(frontMatter);

        return new ParsedDocument(metadata, body);
    }

    /// <summary>
    /// 入力文字列を front matter と本文に分けます。
    /// </summary>
    /// <param name="text">分割する入力文字列。</param>
    /// <param name="delimiter">front matter の区切り文字。</param>
    /// <param name="requireFrontMatter">front matter が無い場合に失敗するかどうか。</param>
    /// <returns>front matter と本文。</returns>
    private static (string FrontMatter, string Body) SplitFrontMatter(string text, string delimiter, bool requireFrontMatter)
    {
        string normalized = NormalizeLineEndings(text);
        string openingDelimiter = delimiter + "\n";
        if (!normalized.StartsWith(openingDelimiter, StringComparison.Ordinal))
        {
            if (requireFrontMatter)
            {
                throw new InvalidOperationException("YAML front matter が見つかりません。");
            }

            return ("", normalized);
        }

        int closingStart = normalized.IndexOf("\n" + delimiter, openingDelimiter.Length, StringComparison.Ordinal);
        if (closingStart < 0)
        {
            throw new InvalidOperationException("YAML front matter の終了区切りが見つかりません。");
        }

        int bodyStart = closingStart + delimiter.Length + 1;
        if (bodyStart < normalized.Length && normalized[bodyStart] == '\n')
        {
            bodyStart++;
        }

        return (normalized[openingDelimiter.Length..closingStart], normalized[bodyStart..]);
    }

    /// <summary>
    /// 改行コードを LF に統一します。
    /// </summary>
    /// <param name="text">変換する文字列。</param>
    /// <returns>LF 改行へ統一した文字列。</returns>
    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    /// <summary>
    /// YAML front matter から文書 metadata を作成します。
    /// </summary>
    /// <param name="frontMatter">YAML front matter 文字列。</param>
    /// <returns>文書 metadata。</returns>
    private static DocumentMetadata ParseMetadata(string frontMatter)
    {
        var yaml = new YamlStream();
        var reader = new StringReader(frontMatter);
        yaml.Load(reader);
        var mapping = (YamlMappingNode)yaml.Documents[0].RootNode;

        return new DocumentMetadata(
            ReadScalar(mapping, "title"),
            ReadScalar(mapping, "category"),
            ReadStringSequence(mapping, "tags"));
    }

    /// <summary>
    /// YAML mapping から scalar 値を読み取ります。
    /// </summary>
    /// <param name="mapping">読み取り対象の YAML mapping。</param>
    /// <param name="key">読み取る key。</param>
    /// <returns>読み取った scalar 値。</returns>
    private static string ReadScalar(YamlMappingNode mapping, string key)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value))
        {
            return "";
        }

        return ((YamlScalarNode)value).Value ?? "";
    }

    /// <summary>
    /// YAML mapping から文字列 sequence を読み取ります。
    /// </summary>
    /// <param name="mapping">読み取り対象の YAML mapping。</param>
    /// <param name="key">読み取る key。</param>
    /// <returns>読み取った文字列一覧。</returns>
    private static IReadOnlyList<string> ReadStringSequence(YamlMappingNode mapping, string key)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value))
        {
            return [];
        }

        var sequence = (YamlSequenceNode)value;
        var values = new List<string>();
        foreach (YamlNode item in sequence.Children)
        {
            values.Add(((YamlScalarNode)item).Value ?? "");
        }

        return values;
    }
}
