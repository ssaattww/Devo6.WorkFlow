/// <summary>
/// 読み込み Step の出力です。
/// </summary>
/// <param name="Text">読み込んだ文字列。</param>
public sealed record LoadTextResult(string Text);

/// <summary>
/// 変換 Step の入力です。
/// </summary>
/// <param name="Text">変換する文字列。</param>
public sealed record ConvertTextInput(string Text);

/// <summary>
/// 変換 Step の出力です。
/// </summary>
/// <param name="ConvertedText">変換済みの文字列。</param>
public sealed record ConvertTextResult(string ConvertedText);

/// <summary>
/// 保存 Step の入力です。
/// </summary>
/// <param name="Content">保存する文字列。</param>
public sealed record SaveTextInput(string Content);
