using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using System.Collections;
using System.Reflection;

namespace Devo6.WorkFlow.Tests;

public sealed class CompositeStepTests
{
    /// <summary>
    /// 名前空間なし Entry metadata が短い Entry 名だけで構成されることを検査します。
    /// </summary>
    [Fact(DisplayName = "名前空間なし Entry は短い名前と完全修飾名が一致する")]
    public void DefineWithoutNamespaceUsesShortNameAsQualifiedName()
    {
        CompositeStepDefinition definition = CompositeStep.Define("Build");

        Assert.Equal("Build", definition.Name);
        Assert.Null(definition.NamespaceName);
        Assert.Equal("Build", definition.QualifiedName);
    }

    /// <summary>
    /// 名前空間付き Entry metadata が chain 後も維持されることを検査します。
    /// </summary>
    [Fact(DisplayName = "名前空間付き Entry metadata は chain 後も維持される")]
    public void DefineWithNamespacePreservesMetadataAfterChain()
    {
        CompositeStep<FirstOutput> step = CompositeStep
            .Define("Build", namespaceName: "Deploy")
            .Run<FirstStep, FirstOutput>()
            .WithConfig<string>();

        Assert.Equal("Build", step.Name);
        Assert.Equal("Deploy", step.NamespaceName);
        Assert.Equal("Deploy.Build", step.QualifiedName);
        Assert.Equal(typeof(string), step.ConfigType);
    }

    /// <summary>
    /// 境界 Config 型と Step 登録単位 Config metadata の関係を検査します。
    /// </summary>
    [Fact(DisplayName = "CompositeStep は境界 Config 型と Step Config metadata を公開する")]
    public void CompositeStepExposesBoundaryConfigAndStepConfigRegistrationMetadata()
    {
        MethodInfo? boundaryWithConfig = typeof(CompositeStep<FirstOutput>)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(method => method.Name == "WithConfig"
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 0);
        MethodInfo? withConfig = typeof(CompositeStep<FirstOutput>)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(method => method.Name == "WithConfig"
                && method.IsGenericMethodDefinition
                && method.GetParameters() is [{ ParameterType: Type parameterType }]
                && parameterType == typeof(string));
        PropertyInfo? boundaryConfigType = typeof(CompositeStep<FirstOutput>).GetProperty("ConfigType", BindingFlags.Instance | BindingFlags.Public);
        Type? registrationType = typeof(CompositeStep<FirstOutput>).Assembly.GetType("Devo6.WorkFlow.Engine.StepConfigRegistration");

        Assert.NotNull(boundaryWithConfig);
        Assert.NotNull(withConfig);
        Assert.Equal(typeof(CompositeStep<FirstOutput>), withConfig!.ReturnType);
        Assert.NotNull(boundaryConfigType);
        Assert.Equal(typeof(Type), boundaryConfigType!.PropertyType);
        Assert.NotNull(registrationType);
        Assert.NotNull(registrationType!.GetProperty("StepType", BindingFlags.Instance | BindingFlags.Public));
        Assert.NotNull(registrationType.GetProperty("SectionPath", BindingFlags.Instance | BindingFlags.Public));
        Assert.NotNull(registrationType.GetProperty("ConfigType", BindingFlags.Instance | BindingFlags.Public));

        PropertyInfo? registrationsProperty = typeof(CompositeStep<FirstOutput>).GetProperty(
            "StepConfigRegistrations",
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(registrationsProperty);
        Assert.Equal(typeof(IReadOnlyList<>).MakeGenericType(registrationType), registrationsProperty!.PropertyType);

        CompositeStep<FirstOutput> step = CompositeStep
            .Define("Main")
            .Run<FirstStep, FirstOutput>()
            .WithConfig<MetadataMainConfig>();
        object configuredStep = withConfig.MakeGenericMethod(typeof(FirstStep.Config)).Invoke(step, ["Load"])!;
        IEnumerable registrations = (IEnumerable)registrationsProperty.GetValue(configuredStep)!;
        object registration = Assert.Single(registrations.Cast<object>());

        Assert.Equal(typeof(MetadataMainConfig), boundaryConfigType.GetValue(configuredStep));
        Assert.Equal(typeof(FirstStep), registrationType.GetProperty("StepType")!.GetValue(registration));
        Assert.Equal("Load", registrationType.GetProperty("SectionPath")!.GetValue(registration));
        Assert.Equal(typeof(FirstStep.Config), registrationType.GetProperty("ConfigType")!.GetValue(registration));
    }

    [Fact(DisplayName = "CompositeStep は定義順に Step を実行し Produce で型付き値を下流へ渡す")]
    public void CompositeStepは定義順にStepを実行しProduceで型付き値を下流へ渡す()
    {
        ExecutionLog.Clear();

        CompositeStep<int> step = CompositeStep
            .Define("Main")
            .Run<FirstStep, FirstOutput>()
                .Produce<SecondInput>(x => new SecondInput(x.Value + 1))
            .Run<SecondStep, int>()
                .StoreAs();

        int result = step.Execute(new StepInput());

        Assert.Equal(43, result);
        Assert.Equal(["first", "second"], ExecutionLog.Values);
    }

    [Fact(DisplayName = "名前付き Produce は下流 Step に名前付き値を渡す")]
    public void 名前付きProduceは下流Stepに名前付き値を渡す()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<TitleStep, string>()
                .Produce<string>("title", x => x)
            .Run<BodyStep, string>()
                .Produce<string>("body", x => x)
            .Run<MergeStep, string>()
                .StoreAs();

        string result = step.Execute(new StepInput());

        Assert.Equal("title:body", result);
    }

    [Fact(DisplayName = "StoreAs は戻り値全体を登録する")]
    public void StoreAsは戻り値全体を登録する()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<FirstStep, FirstOutput>()
                .StoreAs()
            .Run<ReadsStoredOutputStep, string>()
                .StoreAs();

        string result = step.Execute(new StepInput());

        Assert.Equal("42", result);
    }

    [Fact(DisplayName = "Discard は戻り値を登録しない")]
    public void Discardは戻り値を登録しない()
    {
        CompositeStep<bool> step = CompositeStep
            .Define("Main")
            .Run<FirstStep, FirstOutput>()
                .Discard()
            .Run<StoredOutputMissingStep, bool>()
                .StoreAs();

        bool result = step.Execute(new StepInput());

        Assert.True(result);
    }

    [Fact(DisplayName = "Produce は同じ型と名前の重複登録を失敗させる")]
    public void Produceは同じ型と名前の重複登録を失敗させる()
    {
        CompositeStep<int> step = CompositeStep
            .Define("Main")
            .Run<FirstStep, FirstOutput>()
                .Produce<string>("same", x => x.Value.ToString())
            .Run<DuplicateNamedValueStep, string>()
                .Produce<string>("same", x => x)
            .Run<SecondStep, int>()
                .StoreAs();

        Assert.Throws<InvalidOperationException>(() => step.Execute(new StepInput()));
    }

    [Fact(DisplayName = "CompositeStep 自体を IStep として実行できる")]
    public void CompositeStep自体をIStepとして実行できる()
    {
        IStep<int> step = CompositeStep
            .Define("Main")
            .Run<FirstStep, FirstOutput>()
                .Produce<SecondInput>(x => new SecondInput(x.Value))
            .Run<SecondStep, int>()
                .StoreAs();

        int result = step.Execute(new StepInput());

        Assert.Equal(42, result);
    }

    [Fact(DisplayName = "CompositeStep は保持時点の Step 列と戻り値型を後続 Run から守る")]
    public void CompositeStepは保持時点のStep列と戻り値型を後続Runから守る()
    {
        ExecutionLog.Clear();

        CompositeStep<FirstOutput> first = CompositeStep
            .Define("Main")
            .Run<FirstStep, FirstOutput>();
        IStep<FirstOutput> held = first;

        CompositeStep<int> second = first
            .Produce<SecondInput>(x => new SecondInput(x.Value))
            .Run<SecondStep, int>()
            .StoreAs();

        FirstOutput heldResult = held.Execute(new StepInput());
        int secondResult = second.Execute(new StepInput());

        Assert.Equal(42, heldResult.Value);
        Assert.Equal(42, secondResult);
        Assert.Equal(["first", "first", "second"], ExecutionLog.Values);
    }

    /// <summary>
    /// StoreAs overload が型引数を要求せず、公開引数だけで分岐することを確認します。
    /// </summary>
    [Fact(DisplayName = "StoreAs は型引数を受け取らない")]
    public void StoreAsは型引数を受け取らない()
    {
        var storeAsMethods = typeof(CompositeStep<FirstOutput>)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(CompositeStep<FirstOutput>))
            .Where(method => method.Name == "StoreAs")
            .OrderBy(method => method.GetParameters().Length)
            .ToArray();

        Assert.Collection(
            storeAsMethods,
            parameterless =>
            {
                Assert.False(parameterless.IsGenericMethodDefinition);
                Assert.Empty(parameterless.GetParameters());
            },
            captureOverload =>
            {
                Assert.False(captureOverload.IsGenericMethodDefinition);
                var parameter = Assert.Single(captureOverload.GetParameters());
                Assert.Equal(typeof(TraceValueCapture), parameter.ParameterType);
            });
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

    private sealed record FirstOutput(int Value);

    private sealed record SecondInput(int Value);

    /// <summary>
    /// Step Config metadata 検査で使う境界 Config 型です。
    /// </summary>
    private sealed class MetadataMainConfig
    {
        /// <summary>
        /// 最初の Step 用 Config を取得または設定します。
        /// </summary>
        public FirstStep.Config Load { get; set; } = new();
    }

    private sealed class FirstStep : IStep<FirstOutput>
    {
        /// <summary>
        /// 最初の Step 用 Config です。
        /// </summary>
        public sealed class Config
        {
        }

        public FirstOutput Execute(StepInput input)
        {
            ExecutionLog.Add("first");

            return new FirstOutput(42);
        }
    }

    private sealed class SecondStep : IStep<int>
    {
        public int Execute(StepInput input)
        {
            ExecutionLog.Add("second");

            return input.Get<SecondInput>().Value;
        }
    }

    private sealed class TitleStep : IStep<string>
    {
        public string Execute(StepInput input)
        {
            return "title";
        }
    }

    private sealed class BodyStep : IStep<string>
    {
        public string Execute(StepInput input)
        {
            return "body";
        }
    }

    private sealed class MergeStep : IStep<string>
    {
        public string Execute(StepInput input)
        {
            return $"{input.Get<string>("title")}:{input.Get<string>("body")}";
        }
    }

    private sealed class ReadsStoredOutputStep : IStep<string>
    {
        public string Execute(StepInput input)
        {
            return input.Get<FirstOutput>().Value.ToString();
        }
    }

    private sealed class StoredOutputMissingStep : IStep<bool>
    {
        public bool Execute(StepInput input)
        {
            return !input.TryGet<FirstOutput>(out _);
        }
    }

    private sealed class DuplicateNamedValueStep : IStep<string>
    {
        public string Execute(StepInput input)
        {
            return "duplicate";
        }
    }
}
