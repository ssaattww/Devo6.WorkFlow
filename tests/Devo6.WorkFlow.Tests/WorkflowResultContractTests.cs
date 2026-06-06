using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using Microsoft.Extensions.Logging;

namespace Devo6.WorkFlow.Tests;

public sealed class WorkflowResultContractTests
{
    /// <summary>
    /// Verifies that WorkflowResult preserves success and failure state, entry name, error details, and trace identity.
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
    /// Verifies that ValidationError preserves the path, stable error code, and user-facing message.
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
    /// Verifies that the initial representative workflow error codes are exposed as stable public constants.
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
    /// Verifies that ExecutionTrace exposes structured step history without public value-bearing properties.
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
    /// Verifies that engine execution supplies StepContext.Logger and records minimum entry and step state.
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
    /// Verifies that a thrown step exception becomes a failed WorkflowResult with STEP_EXECUTION_FAILED in the trace.
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

    private sealed class LoggingStep : IStep<string>
    {
        public string Execute(StepInput input)
        {
            input.Context.Logger.LogInformation("step-log");

            return "ok";
        }
    }

    private sealed class ThrowingStep : IStep<string>
    {
        public string Execute(StepInput input)
        {
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly RecordingLogger logger;

        public RecordingLoggerFactory()
        {
            logger = new RecordingLogger(Entries);
        }

        public List<LogEntry> Entries { get; } = new();

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return logger;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(List<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

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

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
