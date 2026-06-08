/// <summary>
/// 読み込み Step の出力です。
/// </summary>
/// <param name="Text">読み込んだ文字列。</param>
public sealed record LoadTextResult(string Text);

/// <summary>
/// 文書 metadata です。
/// </summary>
/// <param name="Title">文書の title。</param>
/// <param name="Category">文書の category。</param>
/// <param name="Tags">文書の tag 一覧。</param>
public sealed record DocumentMetadata(string Title, string Category, IReadOnlyList<string> Tags);

/// <summary>
/// front matter と本文を分けた文書です。
/// </summary>
/// <param name="Metadata">文書 metadata。</param>
/// <param name="Body">本文。</param>
public sealed record ParsedDocument(DocumentMetadata Metadata, string Body);

/// <summary>
/// 整形済み本文を持つ文書です。
/// </summary>
/// <param name="Metadata">文書 metadata。</param>
/// <param name="Body">整形済み本文。</param>
public sealed record NormalizedDocument(DocumentMetadata Metadata, string Body);

/// <summary>
/// 本文の統計です。
/// </summary>
/// <param name="LineCount">行数。</param>
/// <param name="WordCount">語数。</param>
/// <param name="CharacterCount">文字数。</param>
/// <param name="TagCount">tag 数。</param>
public sealed record TextStatistics(int LineCount, int WordCount, int CharacterCount, int TagCount);

/// <summary>
/// 統計付き文書です。
/// </summary>
/// <param name="Metadata">文書 metadata。</param>
/// <param name="Body">整形済み本文。</param>
/// <param name="Statistics">本文と tag の統計。</param>
public sealed record AnalyzedDocument(DocumentMetadata Metadata, string Body, TextStatistics Statistics);

/// <summary>
/// レポート本文です。
/// </summary>
/// <param name="Text">保存するレポート文字列。</param>
public sealed record ReportTextResult(string Text);

/// <summary>
/// 保存 Step の入力です。
/// </summary>
/// <param name="Content">保存する文字列。</param>
public sealed record SaveTextInput(string Content);
