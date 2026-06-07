using System.Reflection;
using System.Reflection.Emit;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// 非同期 Step API と同期 Step の混在実行に関する公開契約を検証します。
/// </summary>
public sealed class AsyncStepApiContractTests
{
    /// <summary>
    /// IAsyncStep が StepInput と CancellationToken を受け取り、Task 戻り値で実装できることを検証します。
    /// </summary>
    [Fact(DisplayName = "IAsyncStep は StepInput と CancellationToken で Task 戻り値を実装できる")]
    public void IAsyncStepはStepInputとCancellationTokenでTask戻り値を実装できる()
    {
        Type asyncStepType = RequireAsyncStepType(typeof(string));
        MethodInfo? executeAsync = asyncStepType.GetMethod("ExecuteAsync");

        Assert.NotNull(executeAsync);
        Assert.Equal(typeof(Task<string>), executeAsync.ReturnType);
        Assert.Equal(
            [typeof(StepInput), typeof(CancellationToken)],
            executeAsync.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        Type implementation = AsyncStepTypeFactory.Create<string>(
            nameof(ImplementableAsyncStep),
            nameof(AsyncStepHandlers.ReturnStringAsync));

        Assert.Contains(asyncStepType, implementation.GetInterfaces());
        Assert.NotNull(implementation.GetConstructor(Type.EmptyTypes));

        IStep<string> syncStep = new SyncOnlyStep();
        Assert.Equal("sync", syncStep.Execute(new StepInput()));
    }

    /// <summary>
    /// RunAsync が同期、非同期、同期の順に実行し、await 後の Produce 結果を下流へ渡すことを検証します。
    /// </summary>
    [Fact(DisplayName = "RunAsync は sync async sync を定義順に実行し await 後の Produce を下流へ渡す")]
    public async Task RunAsyncはSyncAsyncSyncを定義順に実行しAwait後のProduceを下流へ渡す()
    {
        ExecutionLog.Clear();
        Type asyncStepType = AsyncStepTypeFactory.Create<AsyncOutput>(
            "ProducingAsyncStep",
            nameof(AsyncStepHandlers.ProduceAsyncOutputAsync));

        CompositeStep<FirstOutput> first = CompositeStep
            .Define("Main")
            .Run<FirstSyncStep, FirstOutput>()
                .Produce<AsyncInput>(value => new AsyncInput(value.Value + 1));
        object current = InvokeRunAsync<AsyncOutput>(first, asyncStepType);
        current = InvokeProduce<AsyncOutput, FinalInput>(current, value => new FinalInput(value.Value + 1));
        current = InvokeRun<FinalSyncStep, string>(current);
        var step = ((CompositeStep<string>)current).StoreAs();

        WorkflowResult result = await ExecuteWorkflowAsync(step);

        Assert.True(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(
            [
                "sync-first",
                "async-middle:start",
                "async-middle:end",
                "sync-final:44",
            ],
            ExecutionLog.Values);
        Assert.Equal(
            [nameof(FirstSyncStep), asyncStepType.Name, nameof(FinalSyncStep)],
            result.Trace!.Steps.Select(traceStep => traceStep.StepName).ToArray());
        Assert.All(result.Trace.Steps, traceStep => Assert.Equal(ExecutionTraceStepStatus.Succeeded, traceStep.Status));
    }

    /// <summary>
    /// 非同期 Step の例外が STEP_EXECUTION_FAILED になり、後続 Step を実行しないことを検証します。
    /// </summary>
    [Fact(DisplayName = "async Step 例外は ExecuteWorkflowAsync で STEP_EXECUTION_FAILED になり後続を止める")]
    public async Task AsyncStep例外はExecuteWorkflowAsyncでStepExecutionFailedになり後続を止める()
    {
        ExecutionLog.Clear();
        Type asyncStepType = AsyncStepTypeFactory.Create<AsyncOutput>(
            "ThrowingAsyncStep",
            nameof(AsyncStepHandlers.ThrowAsync));

        CompositeStep<FirstOutput> first = CompositeStep
            .Define("Main")
            .Run<FirstSyncStep, FirstOutput>()
                .Produce<AsyncInput>(value => new AsyncInput(value.Value + 1));
        object current = InvokeRunAsync<AsyncOutput>(first, asyncStepType);
        current = InvokeProduce<AsyncOutput, FinalInput>(current, value => new FinalInput(value.Value + 1));
        current = InvokeRun<ShouldNotRunStep, string>(current);
        var step = ((CompositeStep<string>)current).StoreAs();

        WorkflowResult result = await ExecuteWorkflowAsync(step);

        Assert.False(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, result.ErrorCode);
        Assert.Contains("async boom", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(["sync-first", "async-throw:start"], ExecutionLog.Values);
        Assert.Equal(
            [nameof(FirstSyncStep), asyncStepType.Name],
            result.Trace!.Steps.Select(traceStep => traceStep.StepName).ToArray());
        Assert.Equal(ExecutionTraceStepStatus.Succeeded, result.Trace.Steps[0].Status);
        Assert.Equal(ExecutionTraceStepStatus.Failed, result.Trace.Steps[1].Status);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, result.Trace.Steps[1].ErrorCode);
    }

    /// <summary>
    /// 事前にキャンセルされた workflow token が同期 Step 完了後に STEP_CANCELED へ変換されることを検証します。
    /// </summary>
    [Fact(DisplayName = "pre-cancelled token converts sync step completion to STEP_CANCELED")]
    public async Task PreCancelledTokenConvertsSyncStepCompletionToStepCanceled()
    {
        ExecutionLog.Clear();
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        CompositeStep<FirstOutput> step = CompositeStep
            .Define("Main")
            .Run<FirstSyncStep, FirstOutput>()
                .Produce<AsyncInput>(output =>
                {
                    ExecutionLog.Add("pre-cancel-produce");

                    return new AsyncInput(output.Value);
                });

        WorkflowResult result = await step.ExecuteWorkflowAsync(cancellationToken: cancellationTokenSource.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.Equal(["sync-first"], ExecutionLog.Values);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(FirstSyncStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, traceStep.ErrorCode);
    }

    /// <summary>
    /// Abstractions assembly から IAsyncStep&lt;T&gt; を取得し、指定した出力型で閉じた型を返します。
    /// </summary>
    private static Type RequireAsyncStepType(Type outputType)
    {
        Type? openType = typeof(IStep<>).Assembly.GetType("Devo6.WorkFlow.Abstractions.IAsyncStep`1");

        Assert.NotNull(openType);
        Assert.True(openType!.IsInterface);

        return openType.MakeGenericType(outputType);
    }

    /// <summary>
    /// 反射で RunAsync&lt;TStep, TOut&gt; を呼び出し、動的生成した非同期 Step を workflow に追加します。
    /// </summary>
    private static object InvokeRunAsync<TOut>(object composite, Type stepType)
    {
        MethodInfo? runAsync = composite
            .GetType()
            .GetMethods()
            .SingleOrDefault(method =>
                method.Name == "RunAsync"
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == 2
                && method.GetParameters().Length == 0);

        Assert.NotNull(runAsync);

        return runAsync!.MakeGenericMethod(stepType, typeof(TOut)).Invoke(composite, [])!;
    }

    /// <summary>
    /// 反射で Run&lt;TStep, TOut&gt; を呼び出し、同期 Step を workflow に追加します。
    /// </summary>
    private static object InvokeRun<TStep, TOut>(object composite)
        where TStep : IStep<TOut>, new()
    {
        MethodInfo run = composite
            .GetType()
            .GetMethods()
            .Single(method =>
                method.Name == "Run"
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == 2
                && method.GetParameters().Length == 0);

        return run.MakeGenericMethod(typeof(TStep), typeof(TOut)).Invoke(composite, [])!;
    }

    /// <summary>
    /// 反射で Produce&lt;TValue&gt; を呼び出し、現在の出力から次の入力値を生成する段を追加します。
    /// </summary>
    private static object InvokeProduce<TCurrent, TValue>(object composite, Func<TCurrent, TValue> selector)
    {
        MethodInfo produce = composite
            .GetType()
            .GetMethods()
            .Single(method =>
                method.Name == "Produce"
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == 1
                && method.GetParameters() is [{ ParameterType.IsGenericType: true } parameter]
                && parameter.ParameterType.GetGenericTypeDefinition() == typeof(Func<,>));

        return produce.MakeGenericMethod(typeof(TValue)).Invoke(composite, [selector])!;
    }

    /// <summary>
    /// ExecuteWorkflowAsync の戻り値型を確認し、既定の options と token で workflow を実行します。
    /// </summary>
    private static async Task<WorkflowResult> ExecuteWorkflowAsync<TOut>(CompositeStep<TOut> step)
    {
        MethodInfo? executeWorkflowAsync = typeof(CompositeStep<TOut>)
            .GetMethods()
            .SingleOrDefault(method => method.Name == "ExecuteWorkflowAsync");

        Assert.NotNull(executeWorkflowAsync);
        Assert.Equal(typeof(Task<WorkflowResult>), executeWorkflowAsync!.ReturnType);

        object?[] arguments = executeWorkflowAsync
            .GetParameters()
            .Select(parameter =>
            {
                if (parameter.ParameterType == typeof(WorkflowExecutionOptions))
                {
                    return new WorkflowExecutionOptions();
                }

                if (parameter.ParameterType == typeof(CancellationToken))
                {
                    return CancellationToken.None;
                }

                return parameter.HasDefaultValue ? parameter.DefaultValue : null;
            })
            .ToArray();

        return await (Task<WorkflowResult>)executeWorkflowAsync.Invoke(step, arguments)!;
    }

    /// <summary>
    /// テスト Step の実行順と到達点を記録します。
    /// </summary>
    private static class ExecutionLog
    {
        private static readonly List<string> Entries = new();

        /// <summary>
        /// 記録済みの実行ログを追加順に公開します。
        /// </summary>
        public static IReadOnlyList<string> Values => Entries;

        /// <summary>
        /// 前回のテストで記録された実行ログを消去します。
        /// </summary>
        public static void Clear()
        {
            Entries.Clear();
        }

        /// <summary>
        /// Step や handler から渡された実行ログを末尾へ追加します。
        /// </summary>
        public static void Add(string value)
        {
            Entries.Add(value);
        }
    }

    /// <summary>
    /// IAsyncStep&lt;T&gt; を実装するテスト用の動的型を生成します。
    /// </summary>
    private static class AsyncStepTypeFactory
    {
        private static readonly AssemblyBuilder Assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Devo6.WorkFlow.Tests.AsyncStepApiContractDynamic"),
            AssemblyBuilderAccess.Run);
        private static readonly ModuleBuilder Module = Assembly.DefineDynamicModule("Main");
        private static int counter;

        /// <summary>
        /// 指定した handler へ ExecuteAsync を委譲する IAsyncStep 実装型を生成します。
        /// </summary>
        public static Type Create<TOut>(string baseName, string handlerName)
        {
            Type interfaceType = RequireAsyncStepType(typeof(TOut));
            string typeName = $"{baseName}{Interlocked.Increment(ref counter)}";
            TypeBuilder typeBuilder = Module.DefineType(
                $"Devo6.WorkFlow.Tests.Dynamic.{typeName}",
                TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Class);

            typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
            typeBuilder.AddInterfaceImplementation(interfaceType);

            MethodBuilder executeAsync = typeBuilder.DefineMethod(
                "ExecuteAsync",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                typeof(Task<TOut>),
                [typeof(StepInput), typeof(CancellationToken)]);
            MethodInfo handler = typeof(AsyncStepHandlers).GetMethod(handlerName, BindingFlags.Public | BindingFlags.Static)!;

            Assert.Equal(typeof(Task<TOut>), handler.ReturnType);

            ILGenerator il = executeAsync.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, handler);
            il.Emit(OpCodes.Ret);

            typeBuilder.DefineMethodOverride(executeAsync, interfaceType.GetMethod("ExecuteAsync")!);

            return typeBuilder.CreateTypeInfo()!.AsType();
        }
    }

    /// <summary>
    /// 動的生成された非同期 Step から呼び出されるテスト用 handler をまとめます。
    /// </summary>
    public static class AsyncStepHandlers
    {
        /// <summary>
        /// キャンセルを確認したうえで固定文字列を Task として返します。
        /// </summary>
        public static Task<string> ReturnStringAsync(StepInput input, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult("async");
        }

        /// <summary>
        /// 非同期境界をまたいだ後に入力値を 1 増やし、実行ログに開始と終了を残します。
        /// </summary>
        public static async Task<AsyncOutput> ProduceAsyncOutputAsync(StepInput input, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("async-middle:start");

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            ExecutionLog.Add("async-middle:end");

            return new AsyncOutput(input.Get<AsyncInput>().Value + 1);
        }

        /// <summary>
        /// 非同期境界をまたいだ後に例外を送出し、失敗時の workflow 動作を検証できるようにします。
        /// </summary>
        public static async Task<AsyncOutput> ThrowAsync(StepInput input, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("async-throw:start");

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException("async boom");
        }
    }

    /// <summary>
    /// 動的生成した IAsyncStep 実装型の名前に使う空のテスト型です。
    /// </summary>
    private sealed class ImplementableAsyncStep
    {
    }

    /// <summary>
    /// 最初の同期 Step が生成する数値を保持します。
    /// </summary>
    private sealed record FirstOutput(int Value);

    /// <summary>
    /// 非同期 Step に渡す数値入力を保持します。
    /// </summary>
    private sealed record AsyncInput(int Value);

    /// <summary>
    /// 非同期 Step が生成する数値出力を保持します。
    /// </summary>
    public sealed record AsyncOutput(int Value);

    /// <summary>
    /// 最後の同期 Step に渡す数値入力を保持します。
    /// </summary>
    private sealed record FinalInput(int Value);

    /// <summary>
    /// 既存の同期 IStep 実装が非同期 API 追加後も利用できることを示します。
    /// </summary>
    private sealed class SyncOnlyStep : IStep<string>
    {
        /// <summary>
        /// 固定文字列を返し、同期 IStep の従来動作を検証します。
        /// </summary>
        public string Execute(StepInput input)
        {
            return "sync";
        }
    }

    /// <summary>
    /// workflow の先頭で実行される同期 Step です。
    /// </summary>
    private sealed class FirstSyncStep : IStep<FirstOutput>
    {
        /// <summary>
        /// 先頭 Step の実行を記録し、非同期 Step へ渡す初期値を返します。
        /// </summary>
        public FirstOutput Execute(StepInput input)
        {
            ExecutionLog.Add("sync-first");

            return new FirstOutput(41);
        }
    }

    /// <summary>
    /// 非同期 Step の後に実行される終端の同期 Step です。
    /// </summary>
    private sealed class FinalSyncStep : IStep<string>
    {
        /// <summary>
        /// 非同期 Step の出力から生成された入力値をログへ記録して返します。
        /// </summary>
        public string Execute(StepInput input)
        {
            string value = $"sync-final:{input.Get<FinalInput>().Value}";
            ExecutionLog.Add(value);

            return value;
        }
    }

    /// <summary>
    /// 失敗後に到達してはならない後続 Step です。
    /// </summary>
    private sealed class ShouldNotRunStep : IStep<string>
    {
        /// <summary>
        /// 実行された場合に後続到達を示すログと値を返します。
        /// </summary>
        public string Execute(StepInput input)
        {
            ExecutionLog.Add("sync-after-failure");

            return "unexpected";
        }
    }
}
