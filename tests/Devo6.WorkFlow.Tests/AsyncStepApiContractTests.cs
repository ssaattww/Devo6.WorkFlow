using System.Reflection;
using System.Reflection.Emit;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

public sealed class AsyncStepApiContractTests
{
    /// <summary>
    /// Verifies that IAsyncStep can be implemented with StepInput, CancellationToken, and a Task return value.
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
    /// Verifies that RunAsync executes sync, async, and sync steps in definition order and forwards produced values after await.
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
    /// Verifies that an asynchronous step exception becomes STEP_EXECUTION_FAILED and stops downstream steps.
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
    /// Verifies that a pre-cancelled workflow token is not converted to STEP_EXECUTION_FAILED before a synchronous step runs.
    /// </summary>
    [Fact(DisplayName = "同期 Step は実行前 cancellation requested だけでは STEP_EXECUTION_FAILED にならない")]
    public async Task 同期Stepは実行前CancellationRequestedだけではStepExecutionFailedにならない()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        CompositeStep<FirstOutput> step = CompositeStep
            .Define("Main")
            .Run<FirstSyncStep, FirstOutput>()
                .StoreAs();

        WorkflowResult result = await step.ExecuteWorkflowAsync(cancellationToken: cancellationTokenSource.Token);

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(FirstSyncStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Succeeded, traceStep.Status);
    }

    private static Type RequireAsyncStepType(Type outputType)
    {
        Type? openType = typeof(IStep<>).Assembly.GetType("Devo6.WorkFlow.Abstractions.IAsyncStep`1");

        Assert.NotNull(openType);
        Assert.True(openType!.IsInterface);

        return openType.MakeGenericType(outputType);
    }

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

    private static class ExecutionLog
    {
        private static readonly List<string> Entries = new();

        public static IReadOnlyList<string> Values => Entries;

        public static void Clear()
        {
            Entries.Clear();
        }

        public static void Add(string value)
        {
            Entries.Add(value);
        }
    }

    private static class AsyncStepTypeFactory
    {
        private static readonly AssemblyBuilder Assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Devo6.WorkFlow.Tests.AsyncStepApiContractDynamic"),
            AssemblyBuilderAccess.Run);
        private static readonly ModuleBuilder Module = Assembly.DefineDynamicModule("Main");
        private static int counter;

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

    public static class AsyncStepHandlers
    {
        public static Task<string> ReturnStringAsync(StepInput input, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult("async");
        }

        public static async Task<AsyncOutput> ProduceAsyncOutputAsync(StepInput input, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("async-middle:start");

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            ExecutionLog.Add("async-middle:end");

            return new AsyncOutput(input.Get<AsyncInput>().Value + 1);
        }

        public static async Task<AsyncOutput> ThrowAsync(StepInput input, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("async-throw:start");

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException("async boom");
        }
    }

    private sealed class ImplementableAsyncStep
    {
    }

    private sealed record FirstOutput(int Value);

    private sealed record AsyncInput(int Value);

    public sealed record AsyncOutput(int Value);

    private sealed record FinalInput(int Value);

    private sealed class SyncOnlyStep : IStep<string>
    {
        public string Execute(StepInput input)
        {
            return "sync";
        }
    }

    private sealed class FirstSyncStep : IStep<FirstOutput>
    {
        public FirstOutput Execute(StepInput input)
        {
            ExecutionLog.Add("sync-first");

            return new FirstOutput(41);
        }
    }

    private sealed class FinalSyncStep : IStep<string>
    {
        public string Execute(StepInput input)
        {
            string value = $"sync-final:{input.Get<FinalInput>().Value}";
            ExecutionLog.Add(value);

            return value;
        }
    }

    private sealed class ShouldNotRunStep : IStep<string>
    {
        public string Execute(StepInput input)
        {
            ExecutionLog.Add("sync-after-failure");

            return "unexpected";
        }
    }
}
