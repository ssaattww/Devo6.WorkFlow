using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using Microsoft.Extensions.Logging;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// WorkflowResult、ExecutionTrace、logger 連携に関する公開契約を検証します。
/// </summary>
public sealed class WorkflowResultContractTests
{
    /// <summary>
    /// WorkflowResult が成功状態、失敗状態、Entry 名、エラー詳細、Trace 参照を保持することを検証します。
    /// </summary>
    [Fact(DisplayName = "WorkflowResult は成功と失敗、Entry 名、エラー、Trace を保持できる")]
    public void WorkflowResultは成功と失敗Entry名エラーTraceを保持できる()
    {
        var trace = new ExecutionTrace(
        [
            new ExecutionTraceStep("FirstStep", ExecutionTraceStepStatus.Succeeded, TimeSpan.FromMilliseconds(1), null),
            new ExecutionTraceStep("SecondStep", ExecutionTraceStepStatus.Failed, TimeSpan.FromMilliseconds(2), WorkflowErrorCodes.StepExecutionFailed),
        ]);

        var success = new WorkflowResult
        {
            EntryName = "Main",
            Succeeded = true,
            Trace = trace,
        };
        var failure = new WorkflowResult
        {
            EntryName = "Main",
            Succeeded = false,
            ErrorCode = WorkflowErrorCodes.StepExecutionFailed,
            ErrorMessage = "boom",
            Trace = trace,
        };

        Assert.True(success.Succeeded);
        Assert.Equal("Main", success.EntryName);
        Assert.Same(trace, success.Trace);
        Assert.False(failure.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, failure.ErrorCode);
        Assert.Equal("boom", failure.ErrorMessage);
        Assert.Same(trace, failure.Trace);
    }

    /// <summary>
    /// ValidationError が発生位置、安定したエラーコード、利用者向けメッセージを保持することを検証します。
    /// </summary>
    [Fact(DisplayName = "ValidationError は Path Code Message を保持できる")]
    public void ValidationErrorはPathCodeMessageを保持できる()
    {
        var error = new ValidationError
        {
            Path = "Main.FirstStep",
            Code = WorkflowErrorCodes.StepInputNotFound,
            Message = "Input is missing.",
        };

        Assert.Equal("Main.FirstStep", error.Path);
        Assert.Equal(WorkflowErrorCodes.StepInputNotFound, error.Code);
        Assert.Equal("Input is missing.", error.Message);
    }

    /// <summary>
    /// 代表的な workflow エラーコードが安定した公開定数として提供されることを検証します。
    /// </summary>
    [Fact(DisplayName = "基本エラーコードは公開契約として提供される")]
    public void 基本エラーコードは公開契約として提供される()
    {
        Assert.Equal("ENTRY_SCRIPT_NOT_FOUND", WorkflowErrorCodes.EntryScriptNotFound);
        Assert.Equal("ENTRY_STEP_NOT_FOUND", WorkflowErrorCodes.EntryStepNotFound);
        Assert.Equal("DUPLICATE_STEP_NAME", WorkflowErrorCodes.DuplicateStepName);
        Assert.Equal("SCRIPT_COMPILE_FAILED", WorkflowErrorCodes.ScriptCompileFailed);
        Assert.Equal("SCRIPT_LOAD_FAILED", WorkflowErrorCodes.ScriptLoadFailed);
        Assert.Equal("SCRIPT_LOAD_CYCLE_DETECTED", WorkflowErrorCodes.ScriptLoadCycleDetected);
        Assert.Equal("SCRIPT_REFERENCE_NOT_ALLOWED", WorkflowErrorCodes.ScriptReferenceNotAllowed);
        Assert.Equal("SCRIPT_NUGET_RESTORE_FAILED", WorkflowErrorCodes.ScriptNugetRestoreFailed);
        Assert.Equal("SCRIPT_NUGET_LOCK_MISSING", WorkflowErrorCodes.ScriptNugetLockMissing);
        Assert.Equal("SCRIPT_NUGET_LOCK_MISMATCH", WorkflowErrorCodes.ScriptNugetLockMismatch);
        Assert.Equal("SCRIPT_API_IDENTITY_MISMATCH", WorkflowErrorCodes.ScriptApiIdentityMismatch);
        Assert.Equal("STEP_INPUT_NOT_FOUND", WorkflowErrorCodes.StepInputNotFound);
        Assert.Equal("STEP_INPUT_TYPE_MISMATCH", WorkflowErrorCodes.StepInputTypeMismatch);
        Assert.Equal("CONFIG_NOT_FOUND", WorkflowErrorCodes.ConfigNotFound);
        Assert.Equal("CONFIG_LOAD_FAILED", WorkflowErrorCodes.ConfigLoadFailed);
        Assert.Equal("STEP_EXECUTION_FAILED", WorkflowErrorCodes.StepExecutionFailed);
        Assert.Equal("STEP_TIMEOUT", WorkflowErrorCodes.StepTimeout);
        Assert.Equal("TRACE_SERIALIZATION_FAILED", WorkflowErrorCodes.TraceSerializationFailed);
    }

    /// <summary>
    /// ExecutionTrace が構造化された Step 履歴を公開しつつ、値本体を公開しないことを検証します。
    /// </summary>
    [Fact(DisplayName = "ExecutionTrace は構造化履歴を持つが値そのものを公開しない")]
    public void ExecutionTraceは構造化履歴を持つが値そのものを公開しない()
    {
        var trace = new ExecutionTrace(
        [
            new ExecutionTraceStep("FirstStep", ExecutionTraceStepStatus.Succeeded, TimeSpan.FromMilliseconds(3), null),
        ]);

        ExecutionTraceStep step = Assert.Single(trace.Steps);
        Assert.Equal("FirstStep", step.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Succeeded, step.Status);
        Assert.Equal(TimeSpan.FromMilliseconds(3), step.Duration);
        Assert.Null(step.ErrorCode);

        string[] publicPropertyNames = typeof(ExecutionTrace).GetProperties()
            .Concat(typeof(ExecutionTraceStep).GetProperties())
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("StepInput", publicPropertyNames);
        Assert.DoesNotContain("Config", publicPropertyNames);
        Assert.DoesNotContain("StepOutput", publicPropertyNames);
        Assert.DoesNotContain("Output", publicPropertyNames);
        Assert.DoesNotContain("Value", publicPropertyNames);
        Assert.DoesNotContain("Values", publicPropertyNames);
    }

    /// <summary>
    /// CompositeStep 実行時に Step が StepContext.Logger を使え、Engine が Entry と Step の状態を記録することを検証します。
    /// </summary>
    [Fact(DisplayName = "CompositeStep 実行時に Step は StepContext.Logger を使え Engine は状態を記録できる")]
    public void CompositeStep実行時にStepはStepContextLoggerを使えEngineは状態を記録できる()
    {
        var loggerFactory = new RecordingLoggerFactory();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<LoggingStep, string>()
                .StoreAs();

        WorkflowResult result = step.ExecuteWorkflow(new WorkflowExecutionOptions(loggerFactory));

        Assert.True(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(LoggingStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Succeeded, traceStep.Status);
        Assert.Null(traceStep.ErrorCode);
        Assert.Contains(loggerFactory.Entries, entry => entry.Message.Contains("step-log", StringComparison.Ordinal));
        Assert.Contains(loggerFactory.Entries, entry => entry.Message.Contains("Entry started", StringComparison.Ordinal));
        Assert.Contains(loggerFactory.Entries, entry => entry.Message.Contains("Step started", StringComparison.Ordinal));
        Assert.Contains(loggerFactory.Entries, entry => entry.Message.Contains("Entry succeeded", StringComparison.Ordinal));
    }

    /// <summary>
    /// Step の例外が失敗した WorkflowResult と STEP_EXECUTION_FAILED の Trace として記録されることを検証します。
    /// </summary>
    [Fact(DisplayName = "Step 例外は WorkflowResult 失敗と STEP_EXECUTION_FAILED の Trace になる")]
    public void Step例外はWorkflowResult失敗とStepExecutionFailedのTraceになる()
    {
        var loggerFactory = new RecordingLoggerFactory();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<ThrowingStep, string>()
                .StoreAs();

        WorkflowResult result = step.ExecuteWorkflow(new WorkflowExecutionOptions(loggerFactory));

        Assert.False(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, result.ErrorCode);
        Assert.Contains("boom", result.ErrorMessage, StringComparison.Ordinal);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(ThrowingStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, traceStep.ErrorCode);
        Assert.Contains(loggerFactory.Entries, entry => entry.Message.Contains("Step failed", StringComparison.Ordinal));
        Assert.Contains(loggerFactory.Entries, entry => entry.Exception?.Message == "boom");
    }

    /// <summary>
    /// StepContext.Logger へログを書き込むテスト用 Step です。
    /// </summary>
    private sealed class LoggingStep : IStep<string>
    {
        /// <summary>
        /// StepContext.Logger に識別用メッセージを出力し、正常終了値を返します。
        /// </summary>
        public string Execute(StepInput input)
        {
            input.Context.Logger.LogInformation("step-log");

            return "ok";
        }
    }

    /// <summary>
    /// 実行時例外を送出するテスト用 Step です。
    /// </summary>
    private sealed class ThrowingStep : IStep<string>
    {
        /// <summary>
        /// workflow の失敗変換を検証するため固定メッセージの例外を送出します。
        /// </summary>
        public string Execute(StepInput input)
        {
            throw new InvalidOperationException("boom");
        }
    }

    /// <summary>
    /// Engine と Step から出力されたログをメモリ上に収集する logger factory です。
    /// </summary>
    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly RecordingLogger logger;

        /// <summary>
        /// 共有のログ収集先に書き込む RecordingLogger を初期化します。
        /// </summary>
        public RecordingLoggerFactory()
        {
            logger = new RecordingLogger(Entries);
        }

        /// <summary>
        /// 収集したログ entry を追加順に保持します。
        /// </summary>
        public List<LogEntry> Entries { get; } = new();

        /// <summary>
        /// テスト用 factory では外部 provider を受け付けても状態を変更しません。
        /// </summary>
        public void AddProvider(ILoggerProvider provider)
        {
        }

        /// <summary>
        /// category にかかわらず同じ記録用 logger を返します。
        /// </summary>
        public ILogger CreateLogger(string categoryName)
        {
            return logger;
        }

        /// <summary>
        /// テスト用 factory に破棄対象のリソースがないことを表します。
        /// </summary>
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// ILogger 呼び出しを LogEntry として収集先へ保存します。
    /// </summary>
    private sealed class RecordingLogger(List<LogEntry> entries) : ILogger
    {
        /// <summary>
        /// scope を使う呼び出しに対して破棄可能な空 scope を返します。
        /// </summary>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        /// <summary>
        /// すべてのログレベルを記録対象として扱います。
        /// </summary>
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <summary>
        /// formatter の結果と例外を LogEntry に変換して収集先へ追加します。
        /// </summary>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    /// <summary>
    /// 状態を持たない logger scope を表します。
    /// </summary>
    private sealed class NullScope : IDisposable
    {
        /// <summary>
        /// 共有して使える空 scope インスタンスを返します。
        /// </summary>
        public static NullScope Instance { get; } = new();

        /// <summary>
        /// 空 scope のため破棄時に何も行いません。
        /// </summary>
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 記録されたログレベル、メッセージ、例外を保持します。
    /// </summary>
    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
