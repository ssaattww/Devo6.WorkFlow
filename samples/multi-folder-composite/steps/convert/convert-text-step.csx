using Devo6.WorkFlow.Abstractions;

/// <summary>
/// 読み込んだ文字列を変換する Step です。
/// </summary>
public sealed class ConvertTextStep : IStep<ConvertTextResult>
{
    /// <summary>
    /// 変換設定です。
    /// </summary>
    public sealed class Config
    {
        /// <summary>
        /// 変換後の先頭に付ける文字列です。
        /// </summary>
        public string Prefix { get; set; } = "";

        /// <summary>
        /// 文字列を大文字へ変換するかどうかを取得または設定します。
        /// </summary>
        public bool ToUpper { get; set; }
    }

    /// <summary>
    /// 入力文字列に設定された変換を適用します。
    /// </summary>
    /// <param name="input">Step 入力。</param>
    /// <returns>変換済みの文字列。</returns>
    public ConvertTextResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        ConvertTextInput convertInput = input.Get<ConvertTextInput>();
        string text = config.ToUpper ? convertInput.Text.ToUpperInvariant() : convertInput.Text;

        return new ConvertTextResult($"{config.Prefix}{text}");
    }
}
