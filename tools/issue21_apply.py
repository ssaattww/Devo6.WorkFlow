from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


def patch_composite() -> None:
    path = ROOT / "src/Devo6.WorkFlow.Engine/CompositeStep.cs"
    text = path.read_text(encoding="utf-8")

    text = replace_once(
        text,
        "using System.Diagnostics;\nusing System.Text.Json;",
        "using System.Diagnostics;\nusing System.Globalization;\nusing System.Text.Json;",
        "add culture using",
    )

    old_execute_async = '''    public async Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        object? currentValue = default(TOut);

        currentValue = await ExecuteSimpleStepSequenceAsync(steps, input, currentValue, cancellationToken).ConfigureAwait(false);

        return (TOut)currentValue!;
    }
'''
    new_execute_async = '''    public async Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        ILogger logger = input.Context.Logger;
        using IDisposable? compositeScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CompositeName"] = QualifiedName,
        });
        logger.LogInformation("Composite started");

        try
        {
            object? currentValue = default(TOut);
            currentValue = await ExecuteSimpleStepSequenceAsync(
                steps,
                input,
                currentValue,
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Composite succeeded");

            return (TOut)currentValue!;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Composite failed");
            throw;
        }
    }
'''
    text = replace_once(text, old_execute_async, new_execute_async, "nested composite execution")

    old_simple = '''    private static async Task<object?> ExecuteSimpleStepSequenceAsync(
        IReadOnlyList<StepRegistration> stepSequence,
        StepInput input,
        object? currentValue,
        CancellationToken cancellationToken)
    {
        foreach (StepRegistration step in stepSequence)
        {
            if (step.TryGetBranch(input, currentValue, out BranchExecutionPlan? branchPlan))
            {
                currentValue = await ExecuteSimpleStepSequenceAsync(
                    branchPlan!.Steps,
                    input,
                    currentValue,
                    cancellationToken).ConfigureAwait(false);
                step.Produce(input, currentValue);
                continue;
            }

            StepExecutionResult result = await step.ExecuteAsync(input, currentValue, cancellationToken).ConfigureAwait(false);
            currentValue = result.Value;
            step.Produce(input, currentValue);
        }

        return currentValue;
    }
'''
    new_simple = '''    private static async Task<object?> ExecuteSimpleStepSequenceAsync(
        IReadOnlyList<StepRegistration> stepSequence,
        StepInput input,
        object? currentValue,
        CancellationToken cancellationToken)
    {
        ILogger logger = input.Context.Logger;
        foreach (StepRegistration step in stepSequence)
        {
            using IDisposable? stepScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["StepName"] = step.Name,
            });
            logger.LogInformation("Step started");

            try
            {
                if (step.TryGetBranch(input, currentValue, out BranchExecutionPlan? branchPlan))
                {
                    using IDisposable? branchScope = logger.BeginScope(new Dictionary<string, object?>
                    {
                        ["BranchName"] = branchPlan!.BranchName,
                    });
                    currentValue = await ExecuteSimpleStepSequenceAsync(
                        branchPlan.Steps,
                        input,
                        currentValue,
                        cancellationToken).ConfigureAwait(false);
                    step.Produce(input, currentValue);
                    logger.LogInformation("Step succeeded");
                    continue;
                }

                StepExecutionResult result = await step.ExecuteAsync(
                    input,
                    currentValue,
                    cancellationToken).ConfigureAwait(false);
                currentValue = result.Value;
                step.Produce(input, currentValue);
                if (result.Status == ExecutionTraceStepStatus.Skipped)
                {
                    logger.LogInformation("Step skipped");
                }
                else
                {
                    logger.LogInformation("Step succeeded");
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Step failed");
                throw;
            }
        }

        return currentValue;
    }
'''
    text = replace_once(text, old_simple, new_simple, "simple sequence logging")

    text = replace_once(
        text,
        '''        using IDisposable? entryScope = engineLogger.BeginScope(new Dictionary<string, object?>
        {
            ["EntryName"] = QualifiedName,
            ["Attempt"] = 1,
        });
''',
        '''        using IDisposable? entryScope = engineLogger.BeginScope(new Dictionary<string, object?>
        {
            ["EntryName"] = QualifiedName,
        });
''',
        "entry scope",
    )

    text = text.replace(
        '''                    ["EntryName"] = QualifiedName,
                    ["StepName"] = step.Name,
''',
        '''                    ["StepName"] = step.Name,
''',
    )
    text = text.replace(
        '''                ["EntryName"] = QualifiedName,
                ["StepName"] = step.Name,
''',
        '''                ["StepName"] = step.Name,
''',
    )

    old_branch_execution = '''        var branchTraceSteps = new List<ExecutionTraceStep>();
        WorkflowSequenceExecutionResult branchResult = await ExecuteWorkflowStepSequenceAsync(
            branchPlan.Steps,
            containingStartStepIndex + branchPlan.StartStepIndex,
            input,
            currentValue,
            options,
            cancellationToken,
            branchTraceSteps,
            engineLogger,
            maxAttempts).ConfigureAwait(false);
'''
    new_branch_execution = '''        using IDisposable? branchScope = engineLogger.BeginScope(new Dictionary<string, object?>
        {
            ["BranchName"] = branchPlan.BranchName,
        });
        var branchTraceSteps = new List<ExecutionTraceStep>();
        WorkflowSequenceExecutionResult branchResult = await ExecuteWorkflowStepSequenceAsync(
            branchPlan.Steps,
            containingStartStepIndex + branchPlan.StartStepIndex,
            input,
            currentValue,
            options,
            cancellationToken,
            branchTraceSteps,
            engineLogger,
            maxAttempts).ConfigureAwait(false);
'''
    text = replace_once(text, old_branch_execution, new_branch_execution, "workflow branch scope")

    text = replace_once(
        text,
        "        BranchExecutionPlan defaultPlan = new(defaultCase.Steps, nextStartIndex);",
        '        BranchExecutionPlan defaultPlan = new(defaultCase.Steps, nextStartIndex, "default");',
        "switch default branch name",
    )

    text = replace_once(
        text,
        '''                return selectedCase is null
                    ? defaultPlan
                    : new BranchExecutionPlan(selectedCase.Steps, selectedCase.StartStepIndex);
''',
        '''                return selectedCase is null
                    ? defaultPlan
                    : new BranchExecutionPlan(
                        selectedCase.Steps,
                        selectedCase.StartStepIndex,
                        FormatSwitchBranchName(selectedCase.Value));
''',
        "switch selected branch name",
    )

    text = replace_once(
        text,
        '''                return new BranchExecutionPlan(thenStepArray, thenStartStepIndex);
''',
        '''                return new BranchExecutionPlan(thenStepArray, thenStartStepIndex, "then");
''',
        "if then branch name",
    )
    text = replace_once(
        text,
        '''            return new BranchExecutionPlan(elseStepArray, elseStartStepIndex);
''',
        '''            return new BranchExecutionPlan(elseStepArray, elseStartStepIndex, "else");
''',
        "if else branch name",
    )

    format_helper = '''    /// <summary>
    /// Switch case 値からログ表示用の branch 名を作成します。
    /// </summary>
    /// <typeparam name="TCase">case 値の型。</typeparam>
    /// <param name="value">表示する case 値。</param>
    /// <returns>安全な表示文字列を含む branch 名。</returns>
    private static string FormatSwitchBranchName<TCase>(TCase value)
    {
        string displayValue;
        try
        {
            object? boxedValue = value;
            displayValue = boxedValue switch
            {
                null => "null",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "null",
                _ => boxedValue.ToString() ?? "null",
            };
        }
        catch
        {
            displayValue = "<unavailable>";
        }

        char[] characters = displayValue
            .Select(character => char.IsControl(character) ? ' ' : character)
            .Take(128)
            .ToArray();

        return $"case={new string(characters)}";
    }

'''
    text = replace_once(
        text,
        '''    /// <summary>
    /// 条件判定を実行し、例外を条件判定失敗として包みます。
''',
        format_helper + '''    /// <summary>
    /// 条件判定を実行し、例外を条件判定失敗として包みます。
''',
        "switch branch formatter",
    )

    text = replace_once(
        text,
        '''/// <param name="StartStepIndex">選択された branch の開始 Step index。</param>
internal sealed record BranchExecutionPlan(IReadOnlyList<StepRegistration> Steps, int StartStepIndex);
''',
        '''/// <param name="StartStepIndex">選択された branch の開始 Step index。</param>
/// <param name="BranchName">ログへ記録する選択 branch 名。</param>
internal sealed record BranchExecutionPlan(
    IReadOnlyList<StepRegistration> Steps,
    int StartStepIndex,
    string BranchName);
''',
        "branch plan record",
    )

    path.write_text(text, encoding="utf-8")


def patch_provider() -> None:
    path = ROOT / "src/Devo6.WorkFlow.Cli/EngineLoggingProvider.cs"
    text = path.read_text(encoding="utf-8")

    text = text.replace(
        '    /// <param name="entryName">scope から解決した EntryName。</param>\n',
        '    /// <param name="scope">scope chain から作成した実行 snapshot。</param>\n',
        1,
    )
    text = text.replace(
        '    /// <param name="level">ログレベル。</param>\n'
        '    /// <param name="message">出力する本文。</param>\n'
        '    /// <param name="exception">ログに含める例外情報。</param>\n'
        '    private void WriteConsole',
        '    /// <param name="scope">ログ出力時点の実行 snapshot。</param>\n'
        '    /// <param name="level">ログレベル。</param>\n'
        '    /// <param name="message">出力する本文。</param>\n'
        '    /// <param name="exception">ログに含める例外情報。</param>\n'
        '    private void WriteConsole',
        1,
    )
    text = text.replace(
        '    /// <param name="entryName">ログファイル名に使う EntryName。</param>\n',
        '    /// <param name="scope">ログ出力時点の実行 snapshot。</param>\n',
        1,
    )
    text = text.replace(
        '    /// <param name="category">category 名。</param>\n'
        '    /// <param name="level">ログレベル。</param>\n'
        '    /// <param name="message">ログ本文。</param>\n'
        '    /// <param name="exception">ログに含める例外情報。</param>\n'
        '    /// <returns>出力先へ書き込むログ文字列。</returns>\n'
        '    private static string FormatLog',
        '    /// <param name="category">category 名。</param>\n'
        '    /// <param name="scope">ログ出力時点の実行 snapshot。</param>\n'
        '    /// <param name="level">ログレベル。</param>\n'
        '    /// <param name="message">ログ本文。</param>\n'
        '    /// <param name="exception">ログに含める例外情報。</param>\n'
        '    /// <returns>出力先へ書き込むログ文字列。</returns>\n'
        '    private static string FormatLog',
        1,
    )
    text = text.replace(
        '    /// AsyncLocal で logger scope の EntryName を保持します。\n',
        '    /// AsyncLocal で logger scope chain を保持します。\n',
        1,
    )

    enum_block = '''internal enum EngineLoggingFormat
{
    /// <summary>テキスト形式。</summary>
    Text,

    /// <summary>JSON 文字列形式。</summary>
    Json,
}
'''
    snapshot_block = enum_block + '''
/// <summary>
/// 1 件のログ出力時点における実行 scope の snapshot を表します。
/// </summary>
/// <param name="EntryName">root Entry 名。</param>
/// <param name="StepName">最も内側の Step 名。</param>
/// <param name="BranchName">最も内側の選択 branch 名。</param>
/// <param name="Attempt">現在 Step の試行番号。</param>
/// <param name="ExecutionPath">外側から内側へ並んだ実行パス。</param>
internal sealed record EngineLogScopeSnapshot(
    string? EntryName,
    string? StepName,
    string? BranchName,
    int? Attempt,
    IReadOnlyList<string> ExecutionPath);
'''
    text = replace_once(text, enum_block, snapshot_block, "scope snapshot record")

    old_write = '''    public void Write(
        string category,
        string? entryName,
        LogLevel level,
        string? message,
        Exception? exception)
    {
        if (disposed)
        {
            return;
        }

        if (!string.IsNullOrEmpty(message))
        {
            WriteConsole(category, level, message, exception);
        }

        if (loggingOptions.FileEnabled)
        {
            WriteFile(category, entryName, level, message, exception);
        }
    }
'''
    new_write = '''    public void Write(
        string category,
        EngineLogScopeSnapshot scope,
        LogLevel level,
        string? message,
        Exception? exception)
    {
        if (disposed)
        {
            return;
        }

        if (!string.IsNullOrEmpty(message))
        {
            WriteConsole(category, scope, level, message, exception);
        }

        if (loggingOptions.FileEnabled)
        {
            WriteFile(category, scope, level, message, exception);
        }
    }
'''
    text = replace_once(text, old_write, new_write, "provider write")

    old_get_entry = '''    /// <summary>
    /// 現在の scope から EntryName を取得します。
    /// 見つからない場合は fallback 名を返します。
    /// </summary>
    /// <returns>現在の EntryName または fallback 名。</returns>
    public string GetCurrentEntryName()
    {
        return scopeState.CurrentEntryName ?? fallbackEntryName;
    }
'''
    new_get_scope = '''    /// <summary>
    /// 現在の scope chain からログ出力用 snapshot を作成します。
    /// </summary>
    /// <returns>現在の実行位置を保持する snapshot。</returns>
    public EngineLogScopeSnapshot GetCurrentScopeSnapshot()
    {
        return scopeState.CreateSnapshot();
    }
'''
    text = replace_once(text, old_get_entry, new_get_scope, "scope snapshot accessor")

    old_console = '''    private void WriteConsole(string category, LogLevel level, string message, Exception? exception)
    {
        if (!loggingOptions.ConsoleEnabled)
        {
            return;
        }

        string output = FormatLog(loggingOptions.ConsoleFormat, category, level, message, exception);
        Console.WriteLine(output);
    }
'''
    new_console = '''    private void WriteConsole(
        string category,
        EngineLogScopeSnapshot scope,
        LogLevel level,
        string message,
        Exception? exception)
    {
        if (!loggingOptions.ConsoleEnabled)
        {
            return;
        }

        string output = FormatLog(loggingOptions.ConsoleFormat, category, scope, level, message, exception);
        Console.WriteLine(output);
    }
'''
    text = replace_once(text, old_console, new_console, "console writer")

    old_file = '''    private void WriteFile(string category, string? entryName, LogLevel level, string? message, Exception? exception)
    {
        if (!loggingOptions.FileEnabled || string.IsNullOrEmpty(message))
        {
            return;
        }

        StreamWriter targetWriter = EnsureFileWriter(entryName);
        string output = FormatLog(loggingOptions.FileFormat, category, level, message, exception);
        lock (sync)
        {
            targetWriter.WriteLine(output);
        }
    }
'''
    new_file = '''    private void WriteFile(
        string category,
        EngineLogScopeSnapshot scope,
        LogLevel level,
        string? message,
        Exception? exception)
    {
        if (!loggingOptions.FileEnabled || string.IsNullOrEmpty(message))
        {
            return;
        }

        StreamWriter targetWriter = EnsureFileWriter(scope.EntryName);
        string output = FormatLog(loggingOptions.FileFormat, category, scope, level, message, exception);
        lock (sync)
        {
            targetWriter.WriteLine(output);
        }
    }
'''
    text = replace_once(text, old_file, new_file, "file writer")

    old_format = '''    private static string FormatLog(
        EngineLoggingFormat format,
        string category,
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
                    Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Level = level.ToString(),
                    Category = category,
                    Message = body,
                    Exception = exception?.ToString(),
                });
        }

        return $"[{DateTime.UtcNow:HH:mm:ss}] [{level}] {category} {logMessage}";
    }
'''
    new_format = '''    private static string FormatLog(
        EngineLoggingFormat format,
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
                    Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
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

        string path = scope.ExecutionPath.Count == 0
            ? string.Empty
            : $" [{string.Join(" > ", scope.ExecutionPath.Select(NormalizeTextPathElement))}]";
        string attempt = scope.Attempt is null ? string.Empty : $" [attempt={scope.Attempt.Value}]";

        return $"[{DateTime.UtcNow:HH:mm:ss}] [{level}] {category}{path}{attempt} {logMessage}";
    }

    /// <summary>
    /// Text ログ用に実行パス要素の制御文字を空白へ置換します。
    /// </summary>
    /// <param name="value">正規化する実行パス要素。</param>
    /// <returns>1 行表示に適した実行パス要素。</returns>
    private static string NormalizeTextPathElement(string value)
    {
        return new string(value.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
    }
'''
    text = replace_once(text, old_format, new_format, "log formatter")

    text = replace_once(
        text,
        '''            string? message = formatter(state, exception);
            provider.Write(categoryName, provider.GetCurrentEntryName(), logLevel, message, exception);
''',
        '''            string? message = formatter(state, exception);
            provider.Write(categoryName, provider.GetCurrentScopeSnapshot(), logLevel, message, exception);
''',
        "logger snapshot write",
    )

    text = replace_once(
        text,
        '''        /// <summary>現在の scope chain から見つかった EntryName。</summary>
        public string? CurrentEntryName => FindEntryName(current.Value);

''',
        '',
        "remove current entry property",
    )

    old_find = '''        /// <summary>
        /// scope chain から EntryName key を持つ文字列値を探します。
        /// </summary>
        /// <param name="node">検索開始 node。</param>
        /// <returns>見つかった EntryName。存在しない場合は null。</returns>
        private static string? FindEntryName(ScopeNode? node)
        {
            for (ScopeNode? current = node; current is not null; current = current.Parent)
            {
                if (current.State is IEnumerable<KeyValuePair<string, object?>> statePairs)
                {
                    foreach (KeyValuePair<string, object?> pair in statePairs)
                    {
                        if (!string.Equals(pair.Key, "EntryName", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (pair.Value is string entryName && !string.IsNullOrWhiteSpace(entryName))
                        {
                            return entryName;
                        }
                    }
                }
            }

            return null;
        }

'''
    new_snapshot = '''        /// <summary>
        /// 現在の scope chain を外側から内側へ走査して snapshot を作成します。
        /// </summary>
        /// <returns>現在の実行位置を保持する snapshot。</returns>
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
                        case "Attempt" when pair.Value is int attemptValue:
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
                    attempt = nodeAttempt;
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

'''
    text = replace_once(text, old_find, new_snapshot, "scope snapshot builder")

    path.write_text(text, encoding="utf-8")


if __name__ == "__main__":
    patch_composite()
    patch_provider()
