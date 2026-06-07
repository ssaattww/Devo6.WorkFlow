using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// C# の関数とプロパティに XML コメントがあるかを検査します。
/// </summary>
internal static class Program
{
    private static readonly Regex TypeDeclarationRegex = new(
        @"^\s*(?:(?:public|private|protected|internal|static|abstract|sealed|partial|readonly|unsafe|new)\s+)*(?<kind>record\s+(?:class|struct)|record|class|struct|interface|enum|delegate)\s+(?<name>[A-Za-z_\u3040-\u30ff\u3400-\u9fff][A-Za-z0-9_\u3040-\u30ff\u3400-\u9fff]*)\b",
        RegexOptions.Compiled);
    private static readonly Regex FunctionDeclarationRegex = new(
        @"^\s*(?:(?:public|private|protected|internal|static|abstract|virtual|override|sealed|async|extern|partial|unsafe|readonly|new)\s+)*(?:(?:[A-Za-z_][A-Za-z0-9_<>,\[\]\.?]*|void)\s+)?(?<name>[A-Za-z_\u3040-\u30ff\u3400-\u9fff][A-Za-z0-9_\u3040-\u30ff\u3400-\u9fff]*|operator\s*[^\s(]+|(?:implicit|explicit)\s+operator\s+[^\s(]+)\s*(?:<[^>]+>)?\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex UnmodifiedFunctionDeclarationRegex = new(
        @"^(?:[A-Za-z_][A-Za-z0-9_<>,\[\]\.?]*|void)\s+[A-Za-z_\u3040-\u30ff\u3400-\u9fff][A-Za-z0-9_\u3040-\u30ff\u3400-\u9fff]*(?:<[^>]+>)?\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex PropertyCandidateRegex = new(
        @"^\s*(?:(?:public|private|protected|internal|static|abstract|virtual|override|sealed|required|readonly|new)\s+)*(?:[A-Za-z_][A-Za-z0-9_<>,\[\]\.?]*|\w+\?)\s+(?<name>[A-Za-z_\u3040-\u30ff\u3400-\u9fff][A-Za-z0-9_\u3040-\u30ff\u3400-\u9fff]*)\s*(?:\{|=>|$)",
        RegexOptions.Compiled);
    private static readonly Regex AccessorRegex = new(@"\b(?:get|set|init)\b", RegexOptions.Compiled);
    private static readonly Regex ParamNameRegex = new("<param\\s+name=\"(?<name>[^\"]+)\"", RegexOptions.Compiled);
    private static readonly string[] DeclarationFolders = ["src", "tests", "tools"];
    private static readonly string[] IgnoredDirectoryNames = ["bin", "obj"];

    /// <summary>
    /// repository root を受け取り、違反があれば非 0 で終了します。
    /// </summary>
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: dotnet run --project tools/csharp-xml-doc-checker -- <repository-root>");
            return 2;
        }

        string repositoryRoot = Path.GetFullPath(args[0]);
        if (!Directory.Exists(repositoryRoot))
        {
            Console.Error.WriteLine($"Repository root does not exist: {repositoryRoot}");
            return 2;
        }

        List<StandardsViolation> violations = [];
        foreach (string sourcePath in EnumerateSourceFiles(repositoryRoot))
        {
            violations.AddRange(InspectFile(repositoryRoot, sourcePath));
        }

        foreach (StandardsViolation violation in violations)
        {
            Console.WriteLine(violation.Format());
        }

        return violations.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// repository root から検査対象の C# file を列挙します。
    /// </summary>
    private static IEnumerable<string> EnumerateSourceFiles(string repositoryRoot)
    {
        foreach (string folderName in DeclarationFolders)
        {
            string folderPath = Path.Combine(repositoryRoot, folderName);
            if (!Directory.Exists(folderPath))
            {
                continue;
            }

            foreach (string sourcePath in Directory.EnumerateFiles(folderPath, "*.cs", SearchOption.AllDirectories))
            {
                if (IsIgnoredPath(sourcePath))
                {
                    continue;
                }

                yield return sourcePath;
            }
        }
    }

    /// <summary>
    /// bin と obj の生成物を検査対象から外します。
    /// </summary>
    private static bool IsIgnoredPath(string sourcePath)
    {
        string[] segments = sourcePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => IgnoredDirectoryNames.Contains(segment, StringComparer.Ordinal));
    }

    /// <summary>
    /// C# file に含まれる通常宣言を検査します。
    /// </summary>
    private static IReadOnlyList<StandardsViolation> InspectFile(string repositoryRoot, string sourcePath)
    {
        string[] originalLines = File.ReadAllLines(sourcePath);
        string[] codeLines = CSharpTextMasker.MaskNonCode(originalLines);
        List<StandardsViolation> violations = [];
        string relativePath = Path.GetRelativePath(repositoryRoot, sourcePath);

        for (int index = 0; index < codeLines.Length; index++)
        {
            string codeLine = codeLines[index];
            if (string.IsNullOrWhiteSpace(codeLine))
            {
                continue;
            }

            Match typeMatch = TypeDeclarationRegex.Match(codeLine);

            Match functionMatch = FunctionDeclarationRegex.Match(codeLine);
            if (functionMatch.Success && IsFunctionDeclarationLine(codeLine, functionMatch.Groups["name"].Value))
            {
                string functionName = functionMatch.Groups["name"].Value;
                XmlCommentInfo xmlComment = GetXmlComment(originalLines, index);
                AddMissingXmlCommentViolation(violations, relativePath, index, "関数", functionName, xmlComment);

                continue;
            }

            Match propertyMatch = PropertyCandidateRegex.Match(codeLine);
            if (!typeMatch.Success && propertyMatch.Success && IsPropertyDeclaration(codeLines, index))
            {
                XmlCommentInfo xmlComment = GetXmlComment(originalLines, index);
                AddMissingXmlCommentViolation(violations, relativePath, index, "プロパティ", propertyMatch.Groups["name"].Value, xmlComment);
                continue;
            }

            if (typeMatch.Success)
            {
                XmlCommentInfo xmlComment = GetXmlComment(originalLines, index);
                AddRecordParamViolations(violations, relativePath, index, codeLines, typeMatch, xmlComment);
            }
        }

        return violations;
    }

    /// <summary>
    /// record primary constructor property の param コメントを検査します。
    /// </summary>
    private static void AddRecordParamViolations(
        List<StandardsViolation> violations,
        string relativePath,
        int lineIndex,
        string[] codeLines,
        Match typeMatch,
        XmlCommentInfo xmlComment)
    {
        if (!typeMatch.Groups["kind"].Value.StartsWith("record", StringComparison.Ordinal))
        {
            return;
        }

        string declarationText = CollectRecordDeclarationText(codeLines, lineIndex);
        int openParenthesisIndex = declarationText.IndexOf('(', StringComparison.Ordinal);
        if (openParenthesisIndex < 0)
        {
            return;
        }

        int closeParenthesisIndex = FindMatchingParenthesis(declarationText, openParenthesisIndex);
        if (closeParenthesisIndex < 0)
        {
            return;
        }

        HashSet<string> documentedParamNames = xmlComment.ParamNames;
        foreach (string paramName in GetPrimaryConstructorParamNames(declarationText, openParenthesisIndex, closeParenthesisIndex))
        {
            if (!documentedParamNames.Contains(paramName))
            {
                violations.Add(new StandardsViolation(
                    relativePath,
                    lineIndex + 1,
                    $"record primary constructor property `{paramName}` に `<param name=\"{paramName}\">` がありません。"));
            }
        }
    }

    /// <summary>
    /// XML コメントが存在しない場合に違反を追加します。
    /// </summary>
    private static void AddMissingXmlCommentViolation(
        List<StandardsViolation> violations,
        string relativePath,
        int lineIndex,
        string declarationKind,
        string declarationName,
        XmlCommentInfo xmlComment)
    {
        if (!xmlComment.Exists)
        {
            violations.Add(new StandardsViolation(relativePath, lineIndex + 1, $"{declarationKind} `{declarationName}` に XML コメントがありません。"));
        }
    }

    /// <summary>
    /// record 宣言の primary constructor 部分を含む文字列を集めます。
    /// </summary>
    private static string CollectRecordDeclarationText(string[] codeLines, int lineIndex)
    {
        StringBuilder builder = new();
        int depth = 0;
        bool sawParenthesis = false;

        for (int index = lineIndex; index < codeLines.Length; index++)
        {
            string line = codeLines[index];
            builder.Append(' ').Append(line.Trim());

            foreach (char character in line)
            {
                if (character == '(')
                {
                    depth++;
                    sawParenthesis = true;
                }
                else if (character == ')')
                {
                    depth--;
                }
            }

            if (sawParenthesis && depth <= 0)
            {
                break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 対応する閉じ括弧の位置を探します。
    /// </summary>
    private static int FindMatchingParenthesis(string text, int openParenthesisIndex)
    {
        int depth = 0;
        for (int index = openParenthesisIndex; index < text.Length; index++)
        {
            if (text[index] == '(')
            {
                depth++;
            }
            else if (text[index] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// primary constructor の parameter 名を抽出します。
    /// </summary>
    private static IEnumerable<string> GetPrimaryConstructorParamNames(string declarationText, int openParenthesisIndex, int closeParenthesisIndex)
    {
        string parameterList = declarationText.Substring(openParenthesisIndex + 1, closeParenthesisIndex - openParenthesisIndex - 1);
        foreach (string parameter in SplitTopLevelParameters(parameterList))
        {
            string normalizedParameter = parameter.Trim();
            if (normalizedParameter.Length == 0)
            {
                continue;
            }

            string[] tokens = normalizedParameter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length > 0)
            {
                yield return tokens[^1].TrimEnd(')');
            }
        }
    }

    /// <summary>
    /// generic 型引数内の comma を除いて parameter list を分割します。
    /// </summary>
    private static IEnumerable<string> SplitTopLevelParameters(string parameterList)
    {
        StringBuilder builder = new();
        int angleDepth = 0;

        foreach (char character in parameterList)
        {
            if (character == '<')
            {
                angleDepth++;
            }
            else if (character == '>' && angleDepth > 0)
            {
                angleDepth--;
            }

            if (character == ',' && angleDepth == 0)
            {
                string parameter = builder.ToString().Trim();
                if (parameter.Length > 0)
                {
                    yield return parameter;
                }

                builder.Clear();
                continue;
            }

            builder.Append(character);
        }

        string finalParameter = builder.ToString().Trim();
        if (finalParameter.Length > 0)
        {
            yield return finalParameter;
        }
    }

    /// <summary>
    /// 宣言直前の XML comment を取得します。
    /// </summary>
    private static XmlCommentInfo GetXmlComment(string[] originalLines, int declarationLineIndex)
    {
        int index = declarationLineIndex - 1;
        while (index >= 0 && IsAttributeLine(originalLines[index]))
        {
            index--;
        }

        List<string> commentLines = [];
        while (index >= 0)
        {
            string trimmedLine = originalLines[index].TrimStart();
            if (!trimmedLine.StartsWith("///", StringComparison.Ordinal))
            {
                break;
            }

            commentLines.Add(trimmedLine[3..].Trim());
            index--;
        }

        commentLines.Reverse();
        return new XmlCommentInfo(commentLines);
    }

    /// <summary>
    /// XML comment と宣言の間に置ける属性行かどうかを判定します。
    /// </summary>
    private static bool IsAttributeLine(string line)
    {
        string trimmedLine = line.Trim();
        return trimmedLine.StartsWith("[", StringComparison.Ordinal) || trimmedLine.EndsWith("]", StringComparison.Ordinal);
    }

    /// <summary>
    /// property 宣言として扱う行かどうかを判定します。
    /// </summary>
    private static bool IsPropertyDeclaration(string[] codeLines, int lineIndex)
    {
        string codeLine = codeLines[lineIndex];
        if (codeLine.Contains("=>", StringComparison.Ordinal))
        {
            return true;
        }

        int openingBraceIndex = codeLine.IndexOf('{', StringComparison.Ordinal);
        if (openingBraceIndex >= 0)
        {
            return ContainsAccessor(codeLine[(openingBraceIndex + 1)..]);
        }

        int blockStartIndex = FindNextCodeLineIndex(codeLines, lineIndex + 1);
        if (blockStartIndex < 0)
        {
            return false;
        }

        string blockStartLine = codeLines[blockStartIndex].TrimStart();
        if (!blockStartLine.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        return ContainsAccessorBlock(codeLines, blockStartIndex);
    }

    /// <summary>
    /// 空行を飛ばして次の code 行を探します。
    /// </summary>
    private static int FindNextCodeLineIndex(string[] codeLines, int startIndex)
    {
        for (int index = startIndex; index < codeLines.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(codeLines[index]))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// property accessor block に accessor が含まれるかを確認します。
    /// </summary>
    private static bool ContainsAccessorBlock(string[] codeLines, int blockStartIndex)
    {
        int depth = 0;
        for (int index = blockStartIndex; index < codeLines.Length; index++)
        {
            string line = codeLines[index];
            int scanStart = index == blockStartIndex ? line.IndexOf('{', StringComparison.Ordinal) + 1 : 0;
            if (scanStart >= 0 && ContainsAccessor(line[scanStart..]))
            {
                return true;
            }

            foreach (char character in line)
            {
                if (character == '{')
                {
                    depth++;
                }
                else if (character == '}')
                {
                    depth--;
                    if (depth <= 0)
                    {
                        return false;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 行に property accessor が含まれるかを確認します。
    /// </summary>
    private static bool ContainsAccessor(string codeLine)
    {
        return AccessorRegex.IsMatch(codeLine);
    }

    /// <summary>
    /// 行頭の呼び出し式を除いて関数宣言だけを許可します。
    /// </summary>
    private static bool IsFunctionDeclarationLine(string codeLine, string functionName)
    {
        string trimmedLine = codeLine.TrimStart();
        if (IsControlStatement(functionName))
        {
            return false;
        }

        if (StartsWithNonDeclarationExpression(trimmedLine))
        {
            return false;
        }

        if (StartsWithDeclarationModifier(trimmedLine))
        {
            return true;
        }

        return UnmodifiedFunctionDeclarationRegex.IsMatch(trimmedLine);
    }

    /// <summary>
    /// 宣言ではない式の行頭 keyword を除外します。
    /// </summary>
    private static bool StartsWithNonDeclarationExpression(string trimmedLine)
    {
        return trimmedLine.StartsWith("return ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("new ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("throw ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("await ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("yield ", StringComparison.Ordinal);
    }

    /// <summary>
    /// 関数宣言に使える modifier で始まるかを確認します。
    /// </summary>
    private static bool StartsWithDeclarationModifier(string trimmedLine)
    {
        return trimmedLine.StartsWith("public ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("private ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("protected ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("internal ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("static ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("abstract ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("virtual ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("override ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("sealed ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("extern ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("partial ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("unsafe ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("readonly ", StringComparison.Ordinal);
    }

    /// <summary>
    /// 制御構文を関数宣言から除外します。
    /// </summary>
    private static bool IsControlStatement(string functionName)
    {
        return functionName is "if" or "for" or "foreach" or "while" or "switch" or "catch" or "using" or "lock" or "return" or "typeof" or "nameof";
    }

    /// <summary>
    /// XML comment の param 名を保持します。
    /// </summary>
    private sealed class XmlCommentInfo
    {
        /// <summary>
        /// XML comment 行から検査用情報を作ります。
        /// </summary>
        public XmlCommentInfo(IReadOnlyList<string> lines)
        {
            string text = string.Join(Environment.NewLine, lines);
            Exists = lines.Count > 0;
            ParamNames = ParamNameRegex.Matches(text)
                .Select(match => match.Groups["name"].Value)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>
        /// XML comment が存在するかどうかを示します。
        /// </summary>
        public bool Exists { get; }

        /// <summary>
        /// XML comment に含まれる param 名を保持します。
        /// </summary>
        public HashSet<string> ParamNames { get; }
    }

    /// <summary>
    /// coding standard 違反の位置と理由を保持します。
    /// </summary>
    private sealed class StandardsViolation
    {
        private readonly string relativePath;
        private readonly int lineNumber;
        private readonly string reason;

        /// <summary>
        /// 違反の位置と理由を初期化します。
        /// </summary>
        public StandardsViolation(string relativePath, int lineNumber, string reason)
        {
            this.relativePath = relativePath;
            this.lineNumber = lineNumber;
            this.reason = reason;
        }

        /// <summary>
        /// 検査失敗 message 用の文字列を作ります。
        /// </summary>
        public string Format()
        {
            return $"{relativePath}:{lineNumber}: {reason}";
        }
    }

    /// <summary>
    /// 文字列と通常 comment を空白化して宣言検出から除外します。
    /// </summary>
    private static class CSharpTextMasker
    {
        /// <summary>
        /// C# text の非 code 領域を空白化します。
        /// </summary>
        public static string[] MaskNonCode(string[] lines)
        {
            List<string> maskedLines = new(lines.Length);
            MaskState state = new();

            foreach (string line in lines)
            {
                maskedLines.Add(MaskLine(line, state));
            }

            return maskedLines.ToArray();
        }

        /// <summary>
        /// 1 行分の非 code 領域を空白化します。
        /// </summary>
        private static string MaskLine(string line, MaskState state)
        {
            StringBuilder builder = new(line.Length);
            int index = 0;

            while (index < line.Length)
            {
                if (state.RawStringQuoteCount > 0)
                {
                    if (HasQuoteRun(line, index, state.RawStringQuoteCount))
                    {
                        AppendSpaces(builder, state.RawStringQuoteCount);
                        index += state.RawStringQuoteCount;
                        state.RawStringQuoteCount = 0;
                    }
                    else
                    {
                        builder.Append(' ');
                        index++;
                    }

                    continue;
                }

                if (state.InBlockComment)
                {
                    if (index + 1 < line.Length && line[index] == '*' && line[index + 1] == '/')
                    {
                        builder.Append("  ");
                        index += 2;
                        state.InBlockComment = false;
                    }
                    else
                    {
                        builder.Append(' ');
                        index++;
                    }

                    continue;
                }

                if (state.InVerbatimString)
                {
                    if (line[index] == '"' && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        builder.Append("  ");
                        index += 2;
                    }
                    else if (line[index] == '"')
                    {
                        builder.Append(' ');
                        index++;
                        state.InVerbatimString = false;
                    }
                    else
                    {
                        builder.Append(' ');
                        index++;
                    }

                    continue;
                }

                if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/')
                {
                    AppendSpaces(builder, line.Length - index);
                    break;
                }

                if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '*')
                {
                    builder.Append("  ");
                    index += 2;
                    state.InBlockComment = true;
                    continue;
                }

                int rawStringQuoteCount = GetRawStringQuoteCount(line, index);
                if (rawStringQuoteCount > 0)
                {
                    AppendSpaces(builder, rawStringQuoteCount);
                    index += rawStringQuoteCount;
                    state.RawStringQuoteCount = rawStringQuoteCount;
                    continue;
                }

                if (line[index] == '@' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append("  ");
                    index += 2;
                    state.InVerbatimString = true;
                    continue;
                }

                if (line[index] == '$' && index + 2 < line.Length && line[index + 1] == '@' && line[index + 2] == '"')
                {
                    builder.Append("   ");
                    index += 3;
                    state.InVerbatimString = true;
                    continue;
                }

                if (line[index] == '@' && index + 2 < line.Length && line[index + 1] == '$' && line[index + 2] == '"')
                {
                    builder.Append("   ");
                    index += 3;
                    state.InVerbatimString = true;
                    continue;
                }

                if (line[index] == '"')
                {
                    builder.Append(' ');
                    index = MaskRegularString(line, index + 1, builder);
                    continue;
                }

                if (line[index] == '\'')
                {
                    builder.Append(' ');
                    index = MaskCharacterLiteral(line, index + 1, builder);
                    continue;
                }

                builder.Append(line[index]);
                index++;
            }

            return builder.ToString();
        }

        /// <summary>
        /// 通常 string literal を空白化します。
        /// </summary>
        private static int MaskRegularString(string line, int index, StringBuilder builder)
        {
            while (index < line.Length)
            {
                if (line[index] == '\\' && index + 1 < line.Length)
                {
                    builder.Append("  ");
                    index += 2;
                }
                else if (line[index] == '"')
                {
                    builder.Append(' ');
                    return index + 1;
                }
                else
                {
                    builder.Append(' ');
                    index++;
                }
            }

            return index;
        }

        /// <summary>
        /// char literal を空白化します。
        /// </summary>
        private static int MaskCharacterLiteral(string line, int index, StringBuilder builder)
        {
            while (index < line.Length)
            {
                if (line[index] == '\\' && index + 1 < line.Length)
                {
                    builder.Append("  ");
                    index += 2;
                }
                else if (line[index] == '\'')
                {
                    builder.Append(' ');
                    return index + 1;
                }
                else
                {
                    builder.Append(' ');
                    index++;
                }
            }

            return index;
        }

        /// <summary>
        /// raw string literal の quote 数を取得します。
        /// </summary>
        private static int GetRawStringQuoteCount(string line, int index)
        {
            int probe = index;
            while (probe < line.Length && line[probe] == '$')
            {
                probe++;
            }

            int quoteCount = 0;
            while (probe + quoteCount < line.Length && line[probe + quoteCount] == '"')
            {
                quoteCount++;
            }

            return quoteCount >= 3 ? quoteCount : 0;
        }

        /// <summary>
        /// 指定位置に必要数の quote があるかを確認します。
        /// </summary>
        private static bool HasQuoteRun(string line, int index, int quoteCount)
        {
            if (index + quoteCount > line.Length)
            {
                return false;
            }

            for (int offset = 0; offset < quoteCount; offset++)
            {
                if (line[index + offset] != '"')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 指定数の空白を追加します。
        /// </summary>
        private static void AppendSpaces(StringBuilder builder, int count)
        {
            builder.Append(' ', count);
        }

        /// <summary>
        /// 複数行 mask の状態を保持します。
        /// </summary>
        private sealed class MaskState
        {
            /// <summary>
            /// block comment 内にいるかどうかを示します。
            /// </summary>
            public bool InBlockComment;

            /// <summary>
            /// verbatim string 内にいるかどうかを示します。
            /// </summary>
            public bool InVerbatimString;

            /// <summary>
            /// raw string literal の終了に必要な quote 数を保持します。
            /// </summary>
            public int RawStringQuoteCount;
        }
    }
}
