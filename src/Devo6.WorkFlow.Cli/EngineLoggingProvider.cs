using Devo6.WorkFlow.Abstractions;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Devo6.WorkFlow.Cli;

/// <summary>
/// CLI の engine/step ログを標準出力と任意のファイルへ出力する設定。
/// </summary>
internal sealed class EngineLoggingOptions
{
    /// <summary>コンソール出力を有効化するかどうか。</summary>
    public bool ConsoleEnabled { get; set; } = true;

    /// <summary>コンソール用のログ形式。</summary>
    public EngineLoggingFormat ConsoleFormat { get; set; } = EngineLoggingFormat.Text;

    /// <summary>ファイル出力を有効化するかどうか。</summary>
    public bool FileEnabled { get; set; }

    /// <summary>ファイル出力を行う相対/絶対パス。</summary>
    public string FileDirectory { get; set; } = string.Empty;

    /// <summary>ログファイル名のテンプレート。</summary>
    public string FileNameFormat { get; set; } = "{Timestamp:yyMMdd-HHmmss}_{RootStepName}.log";

    /// <summary>ファイル出力を行う場合のログ形式。</summary>
    public EngineLoggingFormat FileFormat { get; set; } = EngineLoggingFormat.Text;

    /// <summary>標準出力またはファイル出力が有効なとき true。</summary>
    public bool HasAnyOutput => ConsoleEnabled || FileEnabled;
}

/// <summary>
/// ログ形式を表す。
/// </summary>
internal enum EngineLoggingFormat
{
    /// <summary>テキスト形式。</summary>
    Text,

    /// <summary>JSON 文字列形式。</summary>
    Json,
}

/// <summary>
/// 1 件のログ出力時点における実行階層を保持します。
/// </summary>
internal sealed class EngineLogScopeSnapshot
{
    /// <summary>
    /// 実行階層の snapshot を初期化します。
    /// </summary>
    /// <param name="entryName">root Entry 名。</param>
    /// <param name="stepName">最も内側の Step 名。</param>
    /// <param name="branchName">最も内側の branch 名。</param>
    /// <param name="attempt">現在 Step の試行番号。</param>
    /// <param name="executionPath">外側から内側へ並べた実行 path。</param>
    public EngineLogScopeSnapshot(
        string? entryName,
        string? stepName,
        string? branchName,
        int? attempt,
        IReadOnlyList<string> executionPath)
    {
        EntryName = entryName;
        StepName = stepName;
        BranchName = branchName;
        Attempt = attempt;
        ExecutionPath = executionPath.ToArray();
    }

    /// <summary>root Entry 名を取得します。</summary>
    public string? EntryName { get; }

    /// <summary>最も内側の Step 名を取得します。</summary>
    public string? StepName { get; }

    /// <summary>最も内側の branch 名を取得します。</summary>
    public string? BranchName { get; }

    /// <summary>現在 Step の試行番号を取得します。</summary>
    public int? Attempt { get; }

    /// <summary>外側から内側へ並べた実行 path を取得します。</summary>
    public IReadOnlyList<string> ExecutionPath { get; }
}

/// <summary>
/// CLI run 向けの logger provider。
/// </summary>
internal sealed class EngineLoggingProvider : ILoggerProvider
{
    private readonly EngineLoggingOptions loggingOptions;
    private readonly string fileDirectory;
    private readonly Func<DateTimeOffset> nowProvider;
    private readonly string fallbackEntryName;
    private readonly EngineLoggingScopeState scopeState = new();
    private readonly object sync = new();
    private StreamWriter? fileWriter;
    private bool disposed;

    /// <summary>
    /// ログ出力の設定を指定して provider を初期化します。
    /// </summary>
    /// <param name="loggingOptions">ログ設定。</param>
    /// <param name="engineArguments">Entry 情報を取得するための engine arguments。</param>
    /// <param name="nowProvider">テスト時の時刻注入。</param>
    public EngineLoggingProvider(
        EngineLoggingOptions loggingOptions,
        EngineArguments? engineArguments,
        Func<DateTimeOffset>? nowProvider = null)
    {
        ArgumentNullException.ThrowIfNull(loggingOptions);

        this.loggingOptions = loggingOptions;
        this.nowProvider = nowProvider ?? (() => DateTimeOffset.UtcNow);
        fallbackEntryName = "Workflow";
        fileDirectory = ResolveFileDirectory(loggingOptions, engineArguments);
    }

    /// <summary>
    /// category ごとに logger を作成します。
    /// </summary>
    /// <param name="categoryName">logger の category 名。</param>
    /// <returns>指定 category に紐づく logger。</returns>
    public ILogger CreateLogger(string categoryName)
    {
        return new EngineLogger(this, categoryName);
    }

    /// <summary>
    /// 設定されたログメッセージを出力します。
    /// </summary>
    /// <param name="category">category 名。</param>
    /// <param name="scope">scope chain から作成した実行階層。</param>
    /// <param name="level">ログレベル。</param>
    /// <param name="message">出力本文。</param>
    /// <param name="exception">例外情報。</param>
    public void Write(
        string category,
        EngineLogScopeSnapshot scope,
        LogLevel level,
        string? message,
        Exception? exception)
    {
        if (disposed || string.IsNullOrEmpty(message))
        {
            return;
        }

        DateTimeOffset timestamp = nowProvider();
        WriteConsole(category, scope, level, message, exception, timestamp);

        if (loggingOptions.FileEnabled)
        {
            WriteFile(category, scope, level, message, exception, timestamp);
        }
    }

    /// <summary>
    /// Scope を開始します。
    /// </summary>
    /// <param name="state">scope state。</param>
    /// <returns>終了ハンドル。</returns>
    public IDisposable BeginScope(object? state)
    {
        return scopeState.BeginScope(state);
    }

    /// <summary>
    /// 現在の scope chain から実行階層の snapshot を作成します。
    /// </summary>
    /// <returns>現在の実行階層。</returns>
    public EngineLogScopeSnapshot GetCurrentScopeSnapshot()
    {
        return scopeState.CreateSnapshot();
    }

    /// <summary>
    /// provider と scope を終了します。
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lock (sync)
        {
            fileWriter?.Dispose();
            fileWriter = null;
        }
    }

    /// <summary>
    /// コンソール出力が有効な場合にログを標準出力へ書き込みます。
    /// </summary>
    /// <param name="category">category 名。</param>
    /// <param name="scope">現在の実行階層。</param>
    /// <param name="level">ログレベル。</param>
    /// <param name="message">出力する本文。</param>
    /// <param name="exception">ログに含める例外情報。</param>
    /// <param name="timestamp">ログ時刻。</param>
    private void WriteConsole(
        string category,
        EngineLogScopeSnapshot scope,
        LogLevel level,
        string message,
        Exception? exception,
        DateTimeOffset timestamp)
    {
        if (!loggingOptions.ConsoleEnabled)
        {
            return;
        }

        string output = FormatLog(loggingOptions.ConsoleFormat, timestamp, category, scope, level, message, exception);
        Console.WriteLine(output);
    }

    /// <summary>
    /// ファイル出力が有効な場合にログをファイルへ書き込みます。
    /// </summary>
    /// <param name="category">category 名。</param>
    /// <param name="scope">現在の実行階層。</param>
    /// <param name="level">ログレベル。</param>
    /// <param name="message">出力する本文。</param>
    /// <param name="exception">ログに含める例外情報。</param>
    /// <param name="timestamp">ログ時刻。</param>
    private void WriteFile(
        string category,
        EngineLogScopeSnapshot scope,
        LogLevel level,
        string message,
        Exception? exception,
        DateTimeOffset timestamp)
    {
        StreamWriter targetWriter = EnsureFileWriter(scope.EntryName);
        string output = FormatLog(loggingOptions.FileFormat, timestamp, category, scope, level, message, exception);
        lock (sync)
        {
            targetWriter.WriteLine(output);
        }
    }

    /// <summary>
    /// ファイル writer を必要に応じて初期化し、共有 writer を返します。
    /// </summary>
    /// <param name="entryName">初回作成時のログファイル名に使う EntryName。</param>
    /// <returns>ログファイルへ書き込む writer。</returns>
    private StreamWriter EnsureFileWriter(string? entryName)
    {
        if (fileWriter is not null)
        {
            return fileWriter;
        }

        lock (sync)
        {
            if (fileWriter is not null)
            {
                return fileWriter;
            }

            string logFilePath = CreateLogFilePath(entryName);
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
            fileWriter = new StreamWriter(new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true,
            };
            return fileWriter;
        }
    }

    /// <summary>
    /// 設定済みディレクトリとファイル名テンプレートからログファイル path を作成します。
    /// </summary>
    /// <param name="entryName">ファイル名テンプレートに反映する EntryName。</param>
    /// <returns>書き込み対象のログファイル path。</returns>
    private string CreateLogFilePath(string? entryName)
    {
        string fileName = CreateFileName(entryName);
        return Path.Combine(fileDirectory, fileName);
    }

    /// <summary>
    /// EntryName と時刻をテンプレートに反映して安全なログファイル名を作成します。
    /// </summary>
    /// <param name="entryName">RootStepName placeholder に使う EntryName。</param>
    /// <returns>ファイル名として利用可能なログファイル名。</returns>
    private string CreateFileName(string? entryName)
    {
        DateTimeOffset timestamp = nowProvider();
        string rootStepName = string.IsNullOrWhiteSpace(entryName) ? fallbackEntryName : entryName;
        string fileName = loggingOptions.FileNameFormat;
        fileName = fileName.Replace("{RootStepName}", rootStepName, StringComparison.Ordinal);
        fileName = Regex.Replace(
            fileName,
            @"\{Timestamp(?::(?<format>[^}]+))?\}",
            match =>
            {
                string format = match.Groups["format"].Success ? match.Groups["format"].Value : "yyMMdd-HHmmss";
                return timestamp.ToString(format, CultureInfo.InvariantCulture);
            },
            RegexOptions.None);

        return SanitizeFileName(fileName);
    }

    /// <summary>
    /// ログファイル出力ディレクトリを entry path 基準で解決します。
    /// </summary>
    /// <param name="loggingOptions">ログ設定。</param>
    /// <param name="engineArguments">entry path を含む engine arguments。</param>
    /// <returns>絶対 path に解決したログ出力ディレクトリ。</returns>
    private static string ResolveFileDirectory(EngineLoggingOptions loggingOptions, EngineArguments? engineArguments)
    {
        string entryDirectory = string.IsNullOrWhiteSpace(engineArguments?.EntryPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(engineArguments.EntryPath))
                ?? Directory.GetCurrentDirectory();

        if (string.IsNullOrWhiteSpace(loggingOptions.FileDirectory))
        {
            return entryDirectory;
        }

        if (Path.IsPathRooted(loggingOptions.FileDirectory))
        {
            return Path.GetFullPath(loggingOptions.FileDirectory);
        }

        return Path.GetFullPath(Path.Combine(entryDirectory, loggingOptions.FileDirectory));
    }

    /// <summary>
    /// OS のファイル名禁止文字をアンダースコアへ置換します。
    /// </summary>
    /// <param name="fileName">置換対象のファイル名。</param>
    /// <returns>ファイル名として安全に使える文字列。</returns>
    private static string SanitizeFileName(string fileName)
    {
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);
        foreach (char character in fileName)
        {
            builder.Append(Array.IndexOf(invalidCharacters, character) >= 0 ? '_' : character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 指定されたログ形式で 1 行分のログ文字列を作成します。
    /// </summary>
    /// <param name="format">出力形式。</param>
    /// <param name="timestamp">ログ時刻。</param>
    /// <param name="category">category 名。</param>
    /// <param name="scope">現在の実行階層。</param>
    /// <param name="level">ログレベル。</param>
    /// <param name="message">ログ本文。</param>
    /// <param name="exception">ログに含める例外情報。</param>
    /// <returns>出力先へ書き込むログ文字列。</returns>
    private static string FormatLog(
        EngineLoggingFormat format,
        DateTimeOffset timestamp,
        string category,
        EngineLogScopeSnapshot scope,
        LogLevel level,
        string message,
        Exception? exception)
    {
        string body = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
        string logMessage = exception is null ? body : $"{body}{Environment.NewLine}{exception}";

        if (format == EngineLoggingFormat.Json)
        {
            return JsonSerializer.Serialize(
                new
                {
                    Timestamp = timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                    Level = level.ToString(),
                    Category = category,
                    EntryName = scope.EntryName,
                    StepName = scope.StepName,
                    BranchName = scope.BranchName,
                    Attempt = scope.Attempt,
                    ExecutionPath = scope.ExecutionPath,
                    Message = body,
                    Exception = exception?.ToString(),
                });
        }

        var builder = new StringBuilder();
        builder.Append($"[{timestamp.UtcDateTime:HH:mm:ss}] [{level}] {category}");
        if (scope.ExecutionPath.Count > 0)
        {
            builder.Append(" [");
            builder.Append(string.Join(" > ", scope.ExecutionPath.Select(NormalizeTextPathSegment)));
            builder.Append(']');
        }

        if (scope.Attempt is int attempt)
        {
            builder.Append(" [attempt=");
            builder.Append(attempt.ToString(CultureInfo.InvariantCulture));
            builder.Append(']');
        }

        if (!string.IsNullOrEmpty(logMessage))
        {
            builder.Append(' ');
            builder.Append(logMessage);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Text 表示用の path 要素から改行と制御文字を除去します。
    /// </summary>
    /// <param name="value">正規化する path 要素。</param>
    /// <returns>1 行へ正規化した path 要素。</returns>
    private static string NormalizeTextPathSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// EngineLoggingProvider に処理を委譲する ILogger 実装。
    /// </summary>
    private sealed class EngineLogger : ILogger
    {
        private readonly EngineLoggingProvider provider;
        private readonly string categoryName;

        /// <summary>
        /// provider と category を指定して logger を初期化します。
        /// </summary>
        /// <param name="provider">ログ出力先 provider。</param>
        /// <param name="categoryName">logger の category 名。</param>
        public EngineLogger(EngineLoggingProvider provider, string categoryName)
        {
            this.provider = provider;
            this.categoryName = categoryName;
        }

        /// <summary>
        /// provider の scope 管理へ新しい scope を登録します。
        /// </summary>
        /// <typeparam name="TState">scope state の型。</typeparam>
        /// <param name="state">scope state。</param>
        /// <returns>scope 終了用ハンドル。</returns>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return provider.BeginScope(state);
        }

        /// <summary>
        /// 指定ログレベルが有効かどうかを返します。
        /// </summary>
        /// <param name="logLevel">判定対象のログレベル。</param>
        /// <returns>この logger では常に true。</returns>
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <summary>
        /// formatter で作成したログ本文を provider へ渡します。
        /// </summary>
        /// <typeparam name="TState">ログ state の型。</typeparam>
        /// <param name="logLevel">ログレベル。</param>
        /// <param name="eventId">イベント ID。</param>
        /// <param name="state">ログ state。</param>
        /// <param name="exception">例外情報。</param>
        /// <param name="formatter">state と例外から本文を作る formatter。</param>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (formatter is null)
            {
                return;
            }

            string? message = formatter(state, exception);
            provider.Write(categoryName, provider.GetCurrentScopeSnapshot(), logLevel, message, exception);
        }
    }

    /// <summary>
    /// AsyncLocal で logger scope の実行階層を保持します。
    /// </summary>
    private sealed class EngineLoggingScopeState
    {
        private readonly AsyncLocal<ScopeNode?> current = new();

        /// <summary>
        /// 新しい scope node を現在の scope chain に追加します。
        /// </summary>
        /// <param name="state">scope state。</param>
        /// <returns>scope を元に戻す disposable。</returns>
        public IDisposable BeginScope(object? state)
        {
            current.Value = new ScopeNode(state, current.Value);
            return new DisposableScope(this, current.Value.Parent);
        }

        /// <summary>
        /// 現在の scope chain を外側から内側へ走査して snapshot を作成します。
        /// </summary>
        /// <returns>現在の実行階層。</returns>
        public EngineLogScopeSnapshot CreateSnapshot()
        {
            var nodes = new List<ScopeNode>();
            for (ScopeNode? node = current.Value; node is not null; node = node.Parent)
            {
                nodes.Add(node);
            }

            nodes.Reverse();

            string? entryName = null;
            string? stepName = null;
            string? branchName = null;
            int? attempt = null;
            var executionPath = new List<string>();

            foreach (ScopeNode node in nodes)
            {
                if (node.State is not IEnumerable<KeyValuePair<string, object?>> statePairs)
                {
                    continue;
                }

                string? nodeEntryName = null;
                string? nodeCompositeName = null;
                string? nodeStepName = null;
                string? nodeBranchName = null;
                int? nodeAttempt = null;

                foreach (KeyValuePair<string, object?> pair in statePairs)
                {
                    switch (pair.Key)
                    {
                        case "EntryName" when pair.Value is string entryValue && !string.IsNullOrWhiteSpace(entryValue):
                            nodeEntryName = entryValue;
                            break;
                        case "CompositeName" when pair.Value is string compositeValue && !string.IsNullOrWhiteSpace(compositeValue):
                            nodeCompositeName = compositeValue;
                            break;
                        case "StepName" when pair.Value is string stepValue && !string.IsNullOrWhiteSpace(stepValue):
                            nodeStepName = stepValue;
                            break;
                        case "BranchName" when pair.Value is string branchValue && !string.IsNullOrWhiteSpace(branchValue):
                            nodeBranchName = branchValue;
                            break;
                        case "Attempt" when pair.Value is int attemptValue && attemptValue > 0:
                            nodeAttempt = attemptValue;
                            break;
                    }
                }

                if (entryName is null && nodeEntryName is not null)
                {
                    entryName = nodeEntryName;
                    executionPath.Add(nodeEntryName);
                }

                if (nodeCompositeName is not null)
                {
                    executionPath.Add(nodeCompositeName);
                }

                if (nodeStepName is not null)
                {
                    stepName = nodeStepName;
                    if (nodeAttempt is not null)
                    {
                        attempt = nodeAttempt;
                    }

                    executionPath.Add(nodeStepName);
                }

                if (nodeBranchName is not null)
                {
                    branchName = nodeBranchName;
                    executionPath.Add(nodeBranchName);
                }
            }

            return new EngineLogScopeSnapshot(entryName, stepName, branchName, attempt, executionPath);
        }

        /// <summary>
        /// 1 つの logger scope state と親 node を保持します。
        /// </summary>
        /// <param name="state">scope state。</param>
        /// <param name="parent">親 scope node。</param>
        private sealed class ScopeNode(object? state, ScopeNode? parent)
        {
            /// <summary>親 scope node。</summary>
            public ScopeNode? Parent { get; } = parent;

            /// <summary>この scope に渡された state。</summary>
            public object? State { get; } = state;
        }

        /// <summary>
        /// Dispose 時に AsyncLocal の現在 scope を取得時点の snapshot へ戻します。
        /// </summary>
        private sealed class DisposableScope : IDisposable
        {
            private readonly EngineLoggingScopeState scopeState;
            private readonly ScopeNode? snapshot;
            private bool disposed;

            /// <summary>
            /// 復元対象の scope state と snapshot を指定して disposable を初期化します。
            /// </summary>
            /// <param name="scopeState">復元対象の scope state。</param>
            /// <param name="snapshot">Dispose 時に戻す scope node。</param>
            public DisposableScope(EngineLoggingScopeState scopeState, ScopeNode? snapshot)
            {
                this.scopeState = scopeState;
                this.snapshot = snapshot;
            }

            /// <summary>
            /// 現在 scope を snapshot に戻します。
            /// </summary>
            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                scopeState.current.Value = snapshot;
            }
        }
    }
}

/// <summary>
/// CLI 用最小 logger factory 実装。
/// </summary>
internal sealed class EngineLoggerFactory : ILoggerFactory
{
    private readonly EngineLoggingProvider provider;

    /// <summary>
    /// CLI logger provider を作成して factory を初期化します。
    /// </summary>
    /// <param name="loggingOptions">ログ設定。</param>
    /// <param name="engineArguments">entry 情報を含む engine arguments。</param>
    public EngineLoggerFactory(EngineLoggingOptions loggingOptions, EngineArguments? engineArguments)
    {
        provider = new EngineLoggingProvider(loggingOptions, engineArguments);
    }

    /// <summary>
    /// 外部 provider 追加はサポートしないため何もしません。
    /// </summary>
    /// <param name="provider">追加要求された logger provider。</param>
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <summary>
    /// provider に委譲して logger を作成します。
    /// </summary>
    /// <param name="categoryName">logger の category 名。</param>
    /// <returns>指定 category の logger。</returns>
    public ILogger CreateLogger(string categoryName)
    {
        return provider.CreateLogger(categoryName);
    }

    /// <summary>
    /// 保持している provider を破棄します。
    /// </summary>
    public void Dispose()
    {
        provider.Dispose();
    }
}
