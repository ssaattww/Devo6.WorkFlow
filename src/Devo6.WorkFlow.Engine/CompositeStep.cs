using Devo6.WorkFlow.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text.Json;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// composite entry を作成するための入口を提供します。
/// </summary>
public static class CompositeStep
{
    /// <summary>
    /// 指定した Entry 名で composite entry 定義を開始します。
    /// </summary>
    /// <param name="name">作成する短い Entry 名。</param>
    /// <param name="namespaceName">Entry の名前空間名。未指定の場合は名前空間なし Entry として扱います。</param>
    /// <returns>最初の Step を登録できる composite entry 定義。</returns>
    public static CompositeStepDefinition Define(string name, string? namespaceName = null)
    {
        return new CompositeStepDefinition(name, namespaceName);
    }
}

/// <summary>
/// 最初の Step を登録する前の composite entry 定義を表します。
/// </summary>
public sealed class CompositeStepDefinition
{
    /// <summary>
    /// composite entry 定義を初期化します。
    /// </summary>
    /// <param name="name">短い Entry 名。</param>
    /// <param name="namespaceName">Entry の名前空間名。</param>
    internal CompositeStepDefinition(string name, string? namespaceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (namespaceName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        }

        Name = name;
        NamespaceName = namespaceName;
        QualifiedName = CreateQualifiedName(name, namespaceName);
    }

    /// <summary>
    /// 短い Entry 名を取得します。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Entry の名前空間名を取得します。名前空間なし Entry の場合は null を返します。
    /// </summary>
    public string? NamespaceName { get; }

    /// <summary>
    /// Entry の完全修飾名を取得します。
    /// </summary>
    public string QualifiedName { get; }

    /// <summary>
    /// 最初の同期 Step を登録します。
    /// </summary>
    /// <typeparam name="TStep">実行する同期 Step 型。</typeparam>
    /// <typeparam name="TOut">同期 Step が返す出力型。</typeparam>
    /// <returns>拡張または実行できる composite step。</returns>
    public CompositeStep<TOut> Run<TStep, TOut>()
        where TStep : IStep<TOut>, new()
    {
        return new CompositeStep<TOut>(Name, NamespaceName, QualifiedName, [StepRegistration.Create<TStep, TOut>()]);
    }

    /// <summary>
    /// 最初の同期 Lambda Step を登録します。
    /// </summary>
    /// <typeparam name="TOut">Lambda Step が返す出力型。</typeparam>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="body">StepInput から出力値を作る処理。</param>
    /// <returns>拡張または実行できる composite step。</returns>
    public CompositeStep<TOut> Run<TOut>(string name, Func<StepInput, TOut> body)
    {
        return new CompositeStep<TOut>(Name, NamespaceName, QualifiedName, [StepRegistration.CreateLambda(name, body)]);
    }

    /// <summary>
    /// 最初の非同期 Step を登録します。
    /// </summary>
    /// <typeparam name="TStep">実行する非同期 Step 型。</typeparam>
    /// <typeparam name="TOut">非同期 Step が返す出力型。</typeparam>
    /// <returns>拡張または実行できる composite step。</returns>
    public CompositeStep<TOut> RunAsync<TStep, TOut>()
        where TStep : IAsyncStep<TOut>, new()
    {
        return new CompositeStep<TOut>(Name, NamespaceName, QualifiedName, [StepRegistration.CreateAsync<TStep, TOut>()]);
    }

    /// <summary>
    /// 最初の非同期 Lambda Step を登録します。
    /// </summary>
    /// <typeparam name="TOut">Lambda Step が返す出力型。</typeparam>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="body">StepInput と cancellation token から出力値を作る非同期処理。</param>
    /// <returns>拡張または実行できる composite step。</returns>
    public CompositeStep<TOut> RunAsync<TOut>(string name, Func<StepInput, CancellationToken, Task<TOut>> body)
    {
        return new CompositeStep<TOut>(Name, NamespaceName, QualifiedName, [StepRegistration.CreateLambdaAsync(name, body)]);
    }

    /// <summary>
    /// 短い Entry 名と名前空間名から完全修飾名を作成します。
    /// </summary>
    /// <param name="name">短い Entry 名。</param>
    /// <param name="namespaceName">Entry の名前空間名。</param>
    /// <returns>Entry の完全修飾名。</returns>
    private static string CreateQualifiedName(string name, string? namespaceName)
    {
        return namespaceName is null ? name : $"{namespaceName}.{name}";
    }
}

/// <summary>
/// 登録済み Step 列を実行できる composite entry を表します。
/// </summary>
/// <typeparam name="TOut">現在の末尾 Step が返す出力型。</typeparam>
public sealed class CompositeStep<TOut> : IStep<TOut>, IAsyncStep<TOut>
{
    private readonly IReadOnlyList<StepRegistration> steps;

    /// <summary>
    /// composite entry を初期化します。
    /// </summary>
    /// <param name="name">短い Entry 名。</param>
    /// <param name="namespaceName">Entry の名前空間名。</param>
    /// <param name="qualifiedName">Entry の完全修飾名。</param>
    /// <param name="steps">登録済み Step 列。</param>
    /// <param name="configType">Entry が要求する標準 Config 型。</param>
    /// <param name="stepConfigRegistrations">Step 登録単位 Config metadata の一覧。</param>
    internal CompositeStep(
        string name,
        string? namespaceName,
        string qualifiedName,
        IReadOnlyList<StepRegistration> steps,
        Type? configType = null,
        IReadOnlyList<StepConfigRegistration>? stepConfigRegistrations = null)
    {
        Name = name;
        NamespaceName = namespaceName;
        QualifiedName = qualifiedName;
        this.steps = steps.ToArray();
        ConfigType = configType;
        StepConfigRegistrations = stepConfigRegistrations?.ToArray() ?? [];
    }

    /// <summary>
    /// 短い Entry 名を取得します。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Entry の名前空間名を取得します。名前空間なし Entry の場合は null を返します。
    /// </summary>
    public string? NamespaceName { get; }

    /// <summary>
    /// Entry の完全修飾名を取得します。
    /// </summary>
    public string QualifiedName { get; }

    /// <summary>
    /// Entry が要求する標準 Config 型を取得します。未指定の場合は null を返します。
    /// </summary>
    public Type? ConfigType { get; }

    /// <summary>
    /// Step 登録単位 Config metadata の一覧を取得します。
    /// </summary>
    public IReadOnlyList<StepConfigRegistration> StepConfigRegistrations { get; }

    /// <summary>
    /// 同期 Step を末尾へ追加します。
    /// </summary>
    /// <typeparam name="TStep">追加する同期 Step 型。</typeparam>
    /// <typeparam name="TNext">追加した Step が返す出力型。</typeparam>
    /// <returns>末尾 Step の出力型を更新した composite step。</returns>
    public CompositeStep<TNext> Run<TStep, TNext>()
        where TStep : IStep<TNext>, new()
    {
        return new CompositeStep<TNext>(
            Name,
            NamespaceName,
            QualifiedName,
            Append(StepRegistration.Create<TStep, TNext>()),
            ConfigType,
            StepConfigRegistrations);
    }

    /// <summary>
    /// 現在値を受け取る同期 Lambda Step を末尾へ追加します。
    /// </summary>
    /// <typeparam name="TNext">追加した Lambda Step が返す出力型。</typeparam>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="body">現在値から次の出力値を作る処理。</param>
    /// <returns>末尾 Step の出力型を更新した composite step。</returns>
    public CompositeStep<TNext> Run<TNext>(string name, Func<TOut, TNext> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Run(name, (current, input) => body(current));
    }

    /// <summary>
    /// 現在値と StepInput を受け取る同期 Lambda Step を末尾へ追加します。
    /// </summary>
    /// <typeparam name="TNext">追加した Lambda Step が返す出力型。</typeparam>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="body">現在値と StepInput から次の出力値を作る処理。</param>
    /// <returns>末尾 Step の出力型を更新した composite step。</returns>
    public CompositeStep<TNext> Run<TNext>(string name, Func<TOut, StepInput, TNext> body)
    {
        return new CompositeStep<TNext>(
            Name,
            NamespaceName,
            QualifiedName,
            Append(StepRegistration.CreateLambda(name, body)),
            ConfigType,
            StepConfigRegistrations);
    }

    /// <summary>
    /// 非同期 Step を末尾へ追加します。
    /// </summary>
    /// <typeparam name="TStep">追加する非同期 Step 型。</typeparam>
    /// <typeparam name="TNext">追加した Step が返す出力型。</typeparam>
    /// <returns>末尾 Step の出力型を更新した composite step。</returns>
    public CompositeStep<TNext> RunAsync<TStep, TNext>()
        where TStep : IAsyncStep<TNext>, new()
    {
        return new CompositeStep<TNext>(
            Name,
            NamespaceName,
            QualifiedName,
            Append(StepRegistration.CreateAsync<TStep, TNext>()),
            ConfigType,
            StepConfigRegistrations);
    }

    /// <summary>
    /// 現在値、StepInput、cancellation token を受け取る非同期 Lambda Step を末尾へ追加します。
    /// </summary>
    /// <typeparam name="TNext">追加した Lambda Step が返す出力型。</typeparam>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="body">現在値、StepInput、cancellation token から次の出力値を作る非同期処理。</param>
    /// <returns>末尾 Step の出力型を更新した composite step。</returns>
    public CompositeStep<TNext> RunAsync<TNext>(string name, Func<TOut, StepInput, CancellationToken, Task<TNext>> body)
    {
        return new CompositeStep<TNext>(
            Name,
            NamespaceName,
            QualifiedName,
            Append(StepRegistration.CreateLambdaAsync(name, body)),
            ConfigType,
            StepConfigRegistrations);
    }

    /// <summary>
    /// 条件が true の場合だけ同期 Step を実行し、false の場合は代替値を現在値にします。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Step 型。</typeparam>
    /// <typeparam name="TNext">条件付き実行後の現在値型。</typeparam>
    /// <param name="when">現在値から実行可否を判定する処理。</param>
    /// <param name="otherwise">条件が false の場合に代替値を作る処理。</param>
    /// <returns>末尾 Step の出力型を更新した composite step。</returns>
    public CompositeStep<TNext> RunIf<TStep, TNext>(Func<TOut, bool> when, Func<TOut, TNext> otherwise)
        where TStep : IStep<TNext>, new()
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(otherwise);

        return RunIf<TStep, TNext>((current, input) => when(current), (current, input) => otherwise(current));
    }

    /// <summary>
    /// 条件が true の場合だけ同期 Step を実行し、false の場合は StepInput を使った代替値を現在値にします。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Step 型。</typeparam>
    /// <typeparam name="TNext">条件付き実行後の現在値型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <param name="otherwise">条件が false の場合に現在値と StepInput から代替値を作る処理。</param>
    /// <returns>末尾 Step の出力型を更新した composite step。</returns>
    public CompositeStep<TNext> RunIf<TStep, TNext>(Func<TOut, StepInput, bool> when, Func<TOut, StepInput, TNext> otherwise)
        where TStep : IStep<TNext>, new()
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(otherwise);

        return new CompositeStep<TNext>(
            Name,
            NamespaceName,
            QualifiedName,
            Append(StepRegistration.CreateRunIf<TStep, TOut, TNext>(when, otherwise)),
            ConfigType,
            StepConfigRegistrations);
    }

    /// <summary>
    /// 条件が true の場合だけ非同期 Step を実行し、false の場合は代替値を現在値にします。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Step 型。</typeparam>
    /// <typeparam name="TNext">条件付き実行後の現在値型。</typeparam>
    /// <param name="when">現在値から実行可否を判定する処理。</param>
    /// <param name="otherwise">条件が false の場合に代替値を作る処理。</param>
    /// <returns>末尾 Step の出力型を更新した composite step。</returns>
    public CompositeStep<TNext> RunIfAsync<TStep, TNext>(Func<TOut, bool> when, Func<TOut, TNext> otherwise)
        where TStep : IAsyncStep<TNext>, new()
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(otherwise);

        return RunIfAsync<TStep, TNext>((current, input) => when(current), (current, input) => otherwise(current));
    }

    /// <summary>
    /// 条件が true の場合だけ非同期 Step を実行し、false の場合は StepInput を使った代替値を現在値にします。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Step 型。</typeparam>
    /// <typeparam name="TNext">条件付き実行後の現在値型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <param name="otherwise">条件が false の場合に現在値と StepInput から代替値を作る処理。</param>
    /// <returns>末尾 Step の出力型を更新した composite step。</returns>
    public CompositeStep<TNext> RunIfAsync<TStep, TNext>(Func<TOut, StepInput, bool> when, Func<TOut, StepInput, TNext> otherwise)
        where TStep : IAsyncStep<TNext>, new()
    {
        ArgumentNullException.ThrowIfNull(otherwise);

        return RunIfAsync<TStep, TNext>(
            when,
            (current, input, cancellationToken) => Task.FromResult(otherwise(current, input)));
    }

    /// <summary>
    /// 条件が true の場合だけ非同期 Step を実行し、false の場合は非同期代替値を現在値にします。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Step 型。</typeparam>
    /// <typeparam name="TNext">条件付き実行後の現在値型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <param name="otherwiseAsync">条件が false の場合に代替値を作る非同期処理。</param>
    /// <returns>末尾 Step の出力型を更新した composite step。</returns>
    public CompositeStep<TNext> RunIfAsync<TStep, TNext>(
        Func<TOut, StepInput, bool> when,
        Func<TOut, StepInput, CancellationToken, Task<TNext>> otherwiseAsync)
        where TStep : IAsyncStep<TNext>, new()
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(otherwiseAsync);

        return new CompositeStep<TNext>(
            Name,
            NamespaceName,
            QualifiedName,
            Append(StepRegistration.CreateRunIfAsync<TStep, TOut, TNext>(when, otherwiseAsync)),
            ConfigType,
            StepConfigRegistrations);
    }

    /// <summary>
    /// 条件が true の場合だけ同一型同期 Step を実行し、false の場合は現在値を維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Step 型。</typeparam>
    /// <param name="when">現在値から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する composite step。</returns>
    public CompositeStep<TOut> RunIf<TStep>(Func<TOut, bool> when)
        where TStep : IStep<TOut>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return RunIf<TStep>((current, input) => when(current));
    }

    /// <summary>
    /// 条件が true の場合だけ同一型同期 Step を実行し、false の場合は現在値を維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Step 型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する composite step。</returns>
    public CompositeStep<TOut> RunIf<TStep>(Func<TOut, StepInput, bool> when)
        where TStep : IStep<TOut>, new()
    {
        return RunIf<TStep, TOut>(when, (current, input) => current);
    }

    /// <summary>
    /// 条件が true の場合だけ同一型非同期 Step を実行し、false の場合は現在値を維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Step 型。</typeparam>
    /// <param name="when">現在値から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する composite step。</returns>
    public CompositeStep<TOut> RunIfAsync<TStep>(Func<TOut, bool> when)
        where TStep : IAsyncStep<TOut>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return RunIfAsync<TStep>((current, input) => when(current));
    }

    /// <summary>
    /// 条件が true の場合だけ同一型非同期 Step を実行し、false の場合は現在値を維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Step 型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する composite step。</returns>
    public CompositeStep<TOut> RunIfAsync<TStep>(Func<TOut, StepInput, bool> when)
        where TStep : IAsyncStep<TOut>, new()
    {
        return RunIfAsync<TStep, TOut>(when, (current, input) => current);
    }

    /// <summary>
    /// 条件が true の場合だけ同期 Unit Step を実行し、現在値は維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Unit Step 型。</typeparam>
    /// <param name="when">現在値から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する composite step。</returns>
    public CompositeStep<TOut> TapIf<TStep>(Func<TOut, bool> when)
        where TStep : IStep<Unit>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return TapIf<TStep>((current, input) => when(current));
    }

    /// <summary>
    /// 条件が true の場合だけ同期 Unit Step を実行し、現在値は維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Unit Step 型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する composite step。</returns>
    public CompositeStep<TOut> TapIf<TStep>(Func<TOut, StepInput, bool> when)
        where TStep : IStep<Unit>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return new CompositeStep<TOut>(
            Name,
            NamespaceName,
            QualifiedName,
            Append(StepRegistration.CreateTapIf<TStep, TOut>(when)),
            ConfigType,
            StepConfigRegistrations);
    }

    /// <summary>
    /// 条件が true の場合だけ非同期 Unit Step を実行し、現在値は維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Unit Step 型。</typeparam>
    /// <param name="when">現在値から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する composite step。</returns>
    public CompositeStep<TOut> TapIfAsync<TStep>(Func<TOut, bool> when)
        where TStep : IAsyncStep<Unit>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return TapIfAsync<TStep>((current, input) => when(current));
    }

    /// <summary>
    /// 条件が true の場合だけ非同期 Unit Step を実行し、現在値は維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Unit Step 型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する composite step。</returns>
    public CompositeStep<TOut> TapIfAsync<TStep>(Func<TOut, StepInput, bool> when)
        where TStep : IAsyncStep<Unit>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return new CompositeStep<TOut>(
            Name,
            NamespaceName,
            QualifiedName,
            Append(StepRegistration.CreateTapIfAsync<TStep, TOut>(when)),
            ConfigType,
            StepConfigRegistrations);
    }

    /// <summary>
    /// 条件に応じて then branch または else branch のどちらか一方を実行します。
    /// </summary>
    /// <typeparam name="TNext">分岐実行後の現在値型。</typeparam>
    /// <param name="name">trace と log に記録する If 制御単位名。</param>
    /// <param name="condition">現在値から then branch を実行するかどうかを判定する処理。</param>
    /// <param name="thenFlow">条件が true の場合に実行する分岐を定義する処理。</param>
    /// <param name="elseFlow">条件が false の場合に実行する分岐を定義する処理。</param>
    /// <returns>分岐後の現在値型を持つ composite step。</returns>
    public CompositeStep<TNext> If<TNext>(
        string name,
        Func<TOut, bool> condition,
        Func<BranchBuilder<TOut>, BranchBuilder<TNext>> thenFlow,
        Func<BranchBuilder<TOut>, BranchBuilder<TNext>> elseFlow)
    {
        ArgumentNullException.ThrowIfNull(condition);

        return If(
            name,
            (current, input) => condition(current),
            thenFlow,
            elseFlow);
    }

    /// <summary>
    /// 条件に応じて StepInput を参照しながら then branch または else branch のどちらか一方を実行します。
    /// </summary>
    /// <typeparam name="TNext">分岐実行後の現在値型。</typeparam>
    /// <param name="name">trace と log に記録する If 制御単位名。</param>
    /// <param name="condition">現在値と StepInput から then branch を実行するかどうかを判定する処理。</param>
    /// <param name="thenFlow">条件が true の場合に実行する分岐を定義する処理。</param>
    /// <param name="elseFlow">条件が false の場合に実行する分岐を定義する処理。</param>
    /// <returns>分岐後の現在値型を持つ composite step。</returns>
    public CompositeStep<TNext> If<TNext>(
        string name,
        Func<TOut, StepInput, bool> condition,
        Func<BranchBuilder<TOut>, BranchBuilder<TNext>> thenFlow,
        Func<BranchBuilder<TOut>, BranchBuilder<TNext>> elseFlow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(thenFlow);
        ArgumentNullException.ThrowIfNull(elseFlow);

        BranchBuilder<TNext> thenBranch = thenFlow(new BranchBuilder<TOut>());
        BranchBuilder<TNext> elseBranch = elseFlow(new BranchBuilder<TOut>());
        EnsureBranchHasSteps(thenBranch, "then");
        EnsureBranchHasSteps(elseBranch, "else");

        int ifStepIndex = GetFlattenedStepCount(steps);
        int thenStartIndex = ifStepIndex + 1;
        int elseStartIndex = thenStartIndex + GetFlattenedStepCount(thenBranch.Steps);
        StepRegistration registration = StepRegistration.CreateIf(
            name,
            condition,
            thenBranch.Steps,
            elseBranch.Steps,
            thenStartIndex,
            elseStartIndex);
        IReadOnlyList<StepConfigRegistration> nextRegistrations = StepConfigRegistrations
            .Concat(RemapBranchConfigRegistrations(thenBranch.StepConfigRegistrations, thenStartIndex))
            .Concat(RemapBranchConfigRegistrations(elseBranch.StepConfigRegistrations, elseStartIndex))
            .ToArray();

        return new CompositeStep<TNext>(
            Name,
            NamespaceName,
            QualifiedName,
            Append(registration),
            ConfigType,
            nextRegistrations);
    }

    /// <summary>
    /// Entry が要求する標準 Config 型をメタ情報として設定します。
    /// </summary>
    /// <typeparam name="TConfig">StepContext に登録する標準 Config 型。</typeparam>
    /// <returns>標準 Config 型のメタ情報を持つ composite entry。</returns>
    public CompositeStep<TOut> WithConfig<TConfig>()
    {
        return new CompositeStep<TOut>(Name, NamespaceName, QualifiedName, steps, typeof(TConfig), StepConfigRegistrations);
    }

    /// <summary>
    /// 直前に登録した Step に対応する Step 登録単位 Config 型と境界 Config 型上のプロパティ パスをメタ情報として設定します。
    /// </summary>
    /// <typeparam name="TConfig">StepContext に登録する Step Config 型。</typeparam>
    /// <param name="sectionPath">境界 Config 型上のプロパティ パス。</param>
    /// <returns>Step 登録単位 Config のメタ情報を持つ composite entry。</returns>
    public CompositeStep<TOut> WithConfig<TConfig>(string sectionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);

        StepConfigRegistration[] nextRegistrations = StepConfigRegistrations
            .Append(new StepConfigRegistration(CurrentStep.StepType, sectionPath, typeof(TConfig), GetFlattenedStepCount(steps) - 1, null))
            .ToArray();

        return new CompositeStep<TOut>(Name, NamespaceName, QualifiedName, steps, ConfigType, nextRegistrations);
    }

    /// <summary>
    /// 直前に登録した Step に対応する Step 登録単位 Config 型、境界 Config 型上のプロパティ パス、既定 Config YAML パスをメタ情報として設定します。
    /// </summary>
    /// <typeparam name="TConfig">StepContext に登録する Step Config 型。</typeparam>
    /// <param name="sectionPath">境界 Config 型上のプロパティ パス。</param>
    /// <param name="defaultConfigPath">Entry .csx のディレクトリから解決する Step 既定 Config YAML パス。</param>
    /// <returns>明示した既定 Config YAML パスを含む Step 登録単位 Config のメタ情報を持つ composite entry。</returns>
    public CompositeStep<TOut> WithConfig<TConfig>(string sectionPath, string defaultConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultConfigPath);

        StepConfigRegistration[] nextRegistrations = StepConfigRegistrations
            .Append(new StepConfigRegistration(CurrentStep.StepType, sectionPath, typeof(TConfig), GetFlattenedStepCount(steps) - 1, defaultConfigPath))
            .ToArray();

        return new CompositeStep<TOut>(Name, NamespaceName, QualifiedName, steps, ConfigType, nextRegistrations);
    }

    /// <summary>
    /// 現在の Step 出力から後続 Step へ渡す型付き値を登録します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <returns>型付き値を生成する現在の composite step。</returns>
    public CompositeStep<TOut> Produce<TValue>(Func<TOut, TValue> selector)
    {
        return AddProducer(selector, null, ExecutionTraceValueSource.Produce, null);
    }

    /// <summary>
    /// 現在の Step 出力から後続 Step へ渡す名前付き値を登録します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="name">登録値の名前。</param>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <returns>名前付き値を生成する現在の composite step。</returns>
    public CompositeStep<TOut> Produce<TValue>(string name, Func<TOut, TValue> selector)
    {
        return AddProducer(selector, name, ExecutionTraceValueSource.Produce, null);
    }

    /// <summary>
    /// 現在の Step 出力から後続 Step へ渡す型付き値を登録し、trace value を記録します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>型付き値を生成する現在の composite step。</returns>
    public CompositeStep<TOut> Produce<TValue>(Func<TOut, TValue> selector, TraceValueCapture capture)
    {
        return AddProducer(selector, null, ExecutionTraceValueSource.Produce, capture);
    }

    /// <summary>
    /// 現在の Step 出力から後続 Step へ渡す名前付き値を登録し、trace value を記録します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="name">登録値の名前。</param>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>名前付き値を生成する現在の composite step。</returns>
    public CompositeStep<TOut> Produce<TValue>(string name, Func<TOut, TValue> selector, TraceValueCapture capture)
    {
        return AddProducer(selector, name, ExecutionTraceValueSource.Produce, capture);
    }

    /// <summary>
    /// 現在の Step 出力を後続 Step へ渡す値として登録します。
    /// </summary>
    /// <returns>現在の Step 出力を生成値として登録する composite step。</returns>
    public CompositeStep<TOut> StoreAs()
    {
        return AddProducer<TOut>(value => value, null, ExecutionTraceValueSource.StoreAs, null);
    }

    /// <summary>
    /// 現在の Step 出力を後続 Step へ渡す値として登録し、trace value を記録します。
    /// </summary>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>現在の Step 出力を生成値として登録する composite step。</returns>
    public CompositeStep<TOut> StoreAs(TraceValueCapture capture)
    {
        return AddProducer<TOut>(value => value, null, ExecutionTraceValueSource.StoreAs, capture);
    }

    /// <summary>
    /// 現在の Step に登録された値生成処理を削除します。
    /// </summary>
    /// <returns>現在の Step が値を生成しない composite step。</returns>
    public CompositeStep<TOut> Discard()
    {
        return WithCurrentStep(CurrentStep.ClearProducers());
    }

    /// <summary>
    /// 指定された入力値で composite step を同期実行します。
    /// </summary>
    /// <param name="input">最初の Step へ渡す入力値。</param>
    /// <returns>末尾 Step が返した出力値。</returns>
    public TOut Execute(StepInput input)
    {
        return ExecuteAsync(input, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 指定された入力値で composite step を非同期実行します。
    /// </summary>
    /// <param name="input">最初の Step へ渡す入力値。</param>
    /// <param name="cancellationToken">非同期 Step へ渡す cancellation token。</param>
    /// <returns>末尾 Step が返した出力値。</returns>
    public async Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        object? currentValue = default(TOut);

        currentValue = await ExecuteSimpleStepSequenceAsync(steps, input, currentValue, cancellationToken).ConfigureAwait(false);

        return (TOut)currentValue!;
    }

    /// <summary>
    /// engine 経路で composite entry を同期実行し、結果と trace を返します。
    /// </summary>
    /// <param name="options">実行時の依存関係。null の場合は既定値を使います。</param>
    /// <returns>成功、失敗、記録した trace を含む workflow 結果。</returns>
    public WorkflowResult ExecuteWorkflow(WorkflowExecutionOptions? options = null)
    {
        return ExecuteWorkflowAsync(options, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// engine 経路で composite entry を非同期実行し、結果と trace を返します。
    /// </summary>
    /// <param name="options">実行時の依存関係。null の場合は既定値を使います。</param>
    /// <param name="cancellationToken">非同期 Step へ渡す外部キャンセル用 token。</param>
    /// <returns>成功、失敗、記録した trace を含む workflow 結果。</returns>
    public async Task<WorkflowResult> ExecuteWorkflowAsync(
        WorkflowExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WorkflowExecutionOptions();

        ILoggerFactory loggerFactory = options.LoggerFactory ?? NullLoggerFactory.Instance;
        ILogger engineLogger = loggerFactory.CreateLogger("Devo6.WorkFlow.Engine");
        ILogger stepLogger = loggerFactory.CreateLogger("Devo6.WorkFlow.Step");
        var traceSteps = new List<ExecutionTraceStep>();
        var context = new StepContext(stepLogger);
        if (options.EngineArguments is not null)
        {
            context.Set(options.EngineArguments);
        }

        if (options.StandardConfig is not null)
        {
            SetStandardConfig(context, ConfigType, options.StandardConfig);
        }

        var input = new StepInput(context);
        object? currentValue = default(TOut);

        using IDisposable? entryScope = engineLogger.BeginScope(new Dictionary<string, object?>
        {
            ["EntryName"] = QualifiedName,
            ["Attempt"] = 1,
        });

        engineLogger.LogInformation("Entry started");

        int maxAttempts = GetMaxAttempts(options.Retry);
        WorkflowSequenceExecutionResult sequenceResult = await ExecuteWorkflowStepSequenceAsync(
            steps,
            0,
            input,
            currentValue,
            options,
            cancellationToken,
            traceSteps,
            engineLogger,
            maxAttempts).ConfigureAwait(false);

        if (!sequenceResult.Succeeded)
        {
            return sequenceResult.Failure!;
        }

        engineLogger.LogInformation("Entry succeeded");

        return new WorkflowResult
        {
            EntryName = QualifiedName,
            Succeeded = true,
            Trace = new ExecutionTrace(traceSteps),
        };
    }

    /// <summary>
    /// 通常の Execute 経路で Step 列と選択された分岐を実行します。
    /// </summary>
    /// <param name="stepSequence">実行する Step 登録列。</param>
    /// <param name="input">Step へ渡す入力値。</param>
    /// <param name="currentValue">Step 列の開始時点の現在値。</param>
    /// <param name="cancellationToken">非同期 Step へ渡す cancellation token。</param>
    /// <returns>Step 列の実行後の現在値。</returns>
    private static async Task<object?> ExecuteSimpleStepSequenceAsync(
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

    /// <summary>
    /// workflow 実行経路で Step 列を実行し、trace と失敗結果を構成します。
    /// </summary>
    /// <param name="stepSequence">実行する Step 登録列。</param>
    /// <param name="startStepIndex">Step Config 用の開始 Step index。</param>
    /// <param name="input">Step へ渡す入力値。</param>
    /// <param name="currentValue">Step 列の開始時点の現在値。</param>
    /// <param name="options">実行時 option。</param>
    /// <param name="cancellationToken">workflow 実行へ渡された外部キャンセル用 token。</param>
    /// <param name="traceSteps">追記対象の trace step 一覧。</param>
    /// <param name="engineLogger">engine 用 logger。</param>
    /// <param name="maxAttempts">Step 本体の最大試行回数。</param>
    /// <returns>Step 列の実行結果。</returns>
    private async Task<WorkflowSequenceExecutionResult> ExecuteWorkflowStepSequenceAsync(
        IReadOnlyList<StepRegistration> stepSequence,
        int startStepIndex,
        StepInput input,
        object? currentValue,
        WorkflowExecutionOptions options,
        CancellationToken cancellationToken,
        List<ExecutionTraceStep> traceSteps,
        ILogger engineLogger,
        int maxAttempts)
    {
        int stepIndex = startStepIndex;
        foreach (StepRegistration step in stepSequence)
        {
            if (step.IsConditionalBranch)
            {
                WorkflowSequenceExecutionResult branchResult = await ExecuteIfStepAsync(
                    step,
                    stepIndex,
                    input,
                    currentValue,
                    options,
                    cancellationToken,
                    traceSteps,
                    engineLogger,
                    maxAttempts).ConfigureAwait(false);
                if (!branchResult.Succeeded)
                {
                    return branchResult;
                }

                currentValue = branchResult.Value;
                stepIndex += step.FlattenedLength;
                continue;
            }

            var succeededAttempt = 1;
            Stopwatch? succeededAttemptStopwatch = null;
            ExecutionTraceStepStatus succeededStatus = ExecutionTraceStepStatus.Succeeded;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                using IDisposable? stepScope = engineLogger.BeginScope(new Dictionary<string, object?>
                {
                    ["EntryName"] = QualifiedName,
                    ["StepName"] = step.Name,
                    ["Attempt"] = attempt,
                });

                engineLogger.LogInformation("Step started for attempt {Attempt}", attempt);

                using StepExecutionCancellation stepCancellation = CreateStepExecutionCancellation(options.StepTimeout, cancellationToken);

                try
                {
                    SetStepConfig(input.Context, options.StepConfigs, stepIndex);
                    StepExecutionResult stepResult = await step.ExecuteAsync(input, currentValue, stepCancellation.Token).ConfigureAwait(false);
                    currentValue = stepResult.Value;

                    StepCancellationFailure? cancellationFailure = DetectCancellationFailure(
                        step,
                        stepCancellation,
                        cancellationToken);
                    if (cancellationFailure is not null)
                    {
                        stopwatch.Stop();

                        return WorkflowSequenceExecutionResult.Failed(ToCancellationWorkflowResult(
                            traceSteps,
                            step,
                            stopwatch.Elapsed,
                            attempt,
                            cancellationFailure,
                            engineLogger));
                    }

                    succeededAttempt = attempt;
                    succeededAttemptStopwatch = stopwatch;
                    succeededStatus = stepResult.Status;
                    break;
                }
                catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
                {
                    stopwatch.Stop();

                    return WorkflowSequenceExecutionResult.Failed(ToCancellationWorkflowResult(
                        traceSteps,
                        step,
                        stopwatch.Elapsed,
                        attempt,
                        StepCancellationFailure.Canceled(exception.Message),
                        engineLogger));
                }
                catch (OperationCanceledException exception) when (stepCancellation.TimeoutWasRequested)
                {
                    stopwatch.Stop();

                    return WorkflowSequenceExecutionResult.Failed(ToCancellationWorkflowResult(
                        traceSteps,
                        step,
                        stopwatch.Elapsed,
                        attempt,
                        StepCancellationFailure.TimedOut(step.Name, stepCancellation.Timeout!.Value, exception.Message),
                        engineLogger));
                }
                catch (StepConditionEvaluationException exception)
                {
                    stopwatch.Stop();
                    traceSteps.Add(new ExecutionTraceStep(
                        step.Name,
                        ExecutionTraceStepStatus.Failed,
                        stopwatch.Elapsed,
                        WorkflowErrorCodes.ConditionEvaluationFailed,
                        attempt));
                    engineLogger.LogError(
                        exception.InnerException,
                        "Step condition failed on attempt {Attempt} with error code {ErrorCode}",
                        attempt,
                        WorkflowErrorCodes.ConditionEvaluationFailed);
                    engineLogger.LogError(
                        exception.InnerException,
                        "Entry failed on attempt {Attempt} with error code {ErrorCode}",
                        attempt,
                        WorkflowErrorCodes.ConditionEvaluationFailed);

                    return WorkflowSequenceExecutionResult.Failed(new WorkflowResult
                    {
                        EntryName = QualifiedName,
                        Succeeded = false,
                        ErrorCode = WorkflowErrorCodes.ConditionEvaluationFailed,
                        ErrorMessage = exception.InnerException?.Message ?? exception.Message,
                        Trace = new ExecutionTrace(traceSteps),
                    });
                }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    traceSteps.Add(new ExecutionTraceStep(
                        step.Name,
                        ExecutionTraceStepStatus.Failed,
                        stopwatch.Elapsed,
                        WorkflowErrorCodes.StepExecutionFailed,
                        attempt));

                    if (attempt < maxAttempts)
                    {
                        engineLogger.LogWarning(
                            exception,
                            "Step attempt {Attempt} failed with error code {ErrorCode}; retrying",
                            attempt,
                            WorkflowErrorCodes.StepExecutionFailed);
                        continue;
                    }

                    engineLogger.LogError(
                        exception,
                        "Step failed after attempt {Attempt} with error code {ErrorCode}",
                        attempt,
                        WorkflowErrorCodes.StepExecutionFailed);
                    engineLogger.LogError(
                        exception,
                        "Entry failed after attempt {Attempt} with error code {ErrorCode}",
                        attempt,
                        WorkflowErrorCodes.StepExecutionFailed);

                    return WorkflowSequenceExecutionResult.Failed(new WorkflowResult
                    {
                        EntryName = QualifiedName,
                        Succeeded = false,
                        ErrorCode = WorkflowErrorCodes.StepExecutionFailed,
                        ErrorMessage = exception.Message,
                        Trace = new ExecutionTrace(traceSteps),
                    });
                }
            }

            if (succeededAttemptStopwatch is null)
            {
                throw new InvalidOperationException("Step retry loop completed without a terminal result.");
            }

            using IDisposable? produceScope = engineLogger.BeginScope(new Dictionary<string, object?>
            {
                ["EntryName"] = QualifiedName,
                ["StepName"] = step.Name,
                ["Attempt"] = succeededAttempt,
            });

            try
            {
                IReadOnlyList<ExecutionTraceValue> producedValues = step.Produce(input, currentValue);
                succeededAttemptStopwatch.Stop();
                traceSteps.Add(new ExecutionTraceStep(
                    step.Name,
                    succeededStatus,
                    succeededAttemptStopwatch.Elapsed,
                    null,
                    succeededAttempt,
                    producedValues));
                if (succeededStatus == ExecutionTraceStepStatus.Skipped)
                {
                    engineLogger.LogInformation("Step skipped on attempt {Attempt}", succeededAttempt);
                }
                else
                {
                    engineLogger.LogInformation("Step succeeded on attempt {Attempt}", succeededAttempt);
                }
            }
            catch (Exception exception)
            {
                succeededAttemptStopwatch.Stop();
                traceSteps.Add(new ExecutionTraceStep(
                    step.Name,
                    ExecutionTraceStepStatus.Failed,
                    succeededAttemptStopwatch.Elapsed,
                    WorkflowErrorCodes.StepExecutionFailed,
                    succeededAttempt));
                engineLogger.LogError(
                    exception,
                    "Step post-processing failed on attempt {Attempt} with error code {ErrorCode}",
                    succeededAttempt,
                    WorkflowErrorCodes.StepExecutionFailed);
                engineLogger.LogError(
                    exception,
                    "Entry failed on attempt {Attempt} with error code {ErrorCode}",
                    succeededAttempt,
                    WorkflowErrorCodes.StepExecutionFailed);

                return WorkflowSequenceExecutionResult.Failed(new WorkflowResult
                {
                    EntryName = QualifiedName,
                    Succeeded = false,
                    ErrorCode = WorkflowErrorCodes.StepExecutionFailed,
                    ErrorMessage = exception.Message,
                    Trace = new ExecutionTrace(traceSteps),
                });
            }

            stepIndex++;
        }

        return WorkflowSequenceExecutionResult.Success(currentValue);
    }

    /// <summary>
    /// If 制御単位を評価し、選択された branch の Step 列を実行します。
    /// </summary>
    /// <param name="step">If 制御単位の Step 登録情報。</param>
    /// <param name="stepIndex">If 制御単位の Step index。</param>
    /// <param name="input">Step へ渡す入力値。</param>
    /// <param name="currentValue">If 評価時点の現在値。</param>
    /// <param name="options">実行時 option。</param>
    /// <param name="cancellationToken">workflow 実行へ渡された外部キャンセル用 token。</param>
    /// <param name="traceSteps">追記対象の trace step 一覧。</param>
    /// <param name="engineLogger">engine 用 logger。</param>
    /// <param name="maxAttempts">branch 内 Step 本体の最大試行回数。</param>
    /// <returns>If と選択 branch の実行結果。</returns>
    private async Task<WorkflowSequenceExecutionResult> ExecuteIfStepAsync(
        StepRegistration step,
        int stepIndex,
        StepInput input,
        object? currentValue,
        WorkflowExecutionOptions options,
        CancellationToken cancellationToken,
        List<ExecutionTraceStep> traceSteps,
        ILogger engineLogger,
        int maxAttempts)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        using IDisposable? stepScope = engineLogger.BeginScope(new Dictionary<string, object?>
        {
            ["EntryName"] = QualifiedName,
            ["StepName"] = step.Name,
            ["Attempt"] = 1,
        });

        engineLogger.LogInformation("If condition started");

        BranchExecutionPlan branchPlan;
        try
        {
            SetStepConfig(input.Context, options.StepConfigs, stepIndex);
            branchPlan = step.GetBranch(input, currentValue);
        }
        catch (StepConditionEvaluationException exception)
        {
            stopwatch.Stop();
            traceSteps.Add(new ExecutionTraceStep(
                step.Name,
                ExecutionTraceStepStatus.Failed,
                stopwatch.Elapsed,
                WorkflowErrorCodes.ConditionEvaluationFailed,
                1));
            engineLogger.LogError(
                exception.InnerException,
                "If condition failed with error code {ErrorCode}",
                WorkflowErrorCodes.ConditionEvaluationFailed);
            engineLogger.LogError(
                exception.InnerException,
                "Entry failed with error code {ErrorCode}",
                WorkflowErrorCodes.ConditionEvaluationFailed);

            return WorkflowSequenceExecutionResult.Failed(new WorkflowResult
            {
                EntryName = QualifiedName,
                Succeeded = false,
                ErrorCode = WorkflowErrorCodes.ConditionEvaluationFailed,
                ErrorMessage = exception.InnerException?.Message ?? exception.Message,
                Trace = new ExecutionTrace(traceSteps),
            });
        }

        var branchTraceSteps = new List<ExecutionTraceStep>();
        WorkflowSequenceExecutionResult branchResult = await ExecuteWorkflowStepSequenceAsync(
            branchPlan.Steps,
            branchPlan.StartStepIndex,
            input,
            currentValue,
            options,
            cancellationToken,
            branchTraceSteps,
            engineLogger,
            maxAttempts).ConfigureAwait(false);
        if (!branchResult.Succeeded)
        {
            traceSteps.AddRange(branchTraceSteps);

            return branchResult;
        }

        currentValue = branchResult.Value;
        try
        {
            IReadOnlyList<ExecutionTraceValue> producedValues = step.Produce(input, currentValue);
            stopwatch.Stop();
            traceSteps.Add(new ExecutionTraceStep(
                step.Name,
                ExecutionTraceStepStatus.Succeeded,
                stopwatch.Elapsed,
                null,
                1,
                producedValues));
            traceSteps.AddRange(branchTraceSteps);
            engineLogger.LogInformation("If succeeded");

            return WorkflowSequenceExecutionResult.Success(currentValue);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            traceSteps.Add(new ExecutionTraceStep(
                step.Name,
                ExecutionTraceStepStatus.Failed,
                stopwatch.Elapsed,
                WorkflowErrorCodes.StepExecutionFailed,
                1));
            engineLogger.LogError(
                exception,
                "If post-processing failed with error code {ErrorCode}",
                WorkflowErrorCodes.StepExecutionFailed);
            engineLogger.LogError(
                exception,
                "Entry failed with error code {ErrorCode}",
                WorkflowErrorCodes.StepExecutionFailed);

            return WorkflowSequenceExecutionResult.Failed(new WorkflowResult
            {
                EntryName = QualifiedName,
                Succeeded = false,
                ErrorCode = WorkflowErrorCodes.StepExecutionFailed,
                ErrorMessage = exception.Message,
                Trace = new ExecutionTrace(traceSteps),
            });
        }
    }

    /// <summary>
    /// retry 設定から Step 本体の最大試行回数を取得します。
    /// </summary>
    /// <param name="retry">workflow 実行に適用する retry 設定。</param>
    /// <returns>Step 本体の最大試行回数。</returns>
    private static int GetMaxAttempts(RetryOptions? retry)
    {
        if (retry is null || retry.MaxAttempts <= 1)
        {
            return 1;
        }

        return retry.MaxAttempts;
    }

    /// <summary>
    /// Step 実行用に timeout と外部キャンセルを合成した token を作成します。
    /// </summary>
    /// <param name="stepTimeout">Step ごとに適用する timeout。</param>
    /// <param name="cancellationToken">workflow 実行へ渡された外部キャンセル用 token。</param>
    /// <returns>Step 実行中に使う cancellation 状態。</returns>
    private static StepExecutionCancellation CreateStepExecutionCancellation(
        TimeSpan? stepTimeout,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? timeoutSource = null;
        CancellationTokenSource? linkedSource = null;

        try
        {
            if (stepTimeout is null)
            {
                return new StepExecutionCancellation(cancellationToken, null, null, null);
            }

            timeoutSource = new CancellationTokenSource();
            timeoutSource.CancelAfter(stepTimeout.Value);
            linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

            return new StepExecutionCancellation(linkedSource.Token, stepTimeout, timeoutSource, linkedSource);
        }
        catch
        {
            linkedSource?.Dispose();
            timeoutSource?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Step 完了後に timeout または外部キャンセルとして扱うべき状態を判定します。
    /// </summary>
    /// <param name="step">完了した Step 登録情報。</param>
    /// <param name="stepCancellation">Step 実行に使った cancellation 状態。</param>
    /// <param name="externalCancellationToken">workflow 実行へ渡された外部キャンセル用 token。</param>
    /// <returns>cancellation 系の失敗情報。失敗として扱わない場合は null。</returns>
    private static StepCancellationFailure? DetectCancellationFailure(
        StepRegistration step,
        StepExecutionCancellation stepCancellation,
        CancellationToken externalCancellationToken)
    {
        if (externalCancellationToken.IsCancellationRequested)
        {
            return StepCancellationFailure.Canceled(null);
        }

        if (!stepCancellation.TimeoutWasRequested)
        {
            return null;
        }

        return StepCancellationFailure.TimedOut(step.Name, stepCancellation.Timeout!.Value, null);
    }

    /// <summary>
    /// timeout または外部キャンセルを WorkflowResult と trace に変換します。
    /// </summary>
    /// <param name="traceSteps">これまでに記録した trace step。</param>
    /// <param name="step">失敗した Step 登録情報。</param>
    /// <param name="elapsed">失敗までの経過時間。</param>
    /// <param name="attempt">失敗した試行番号。</param>
    /// <param name="failure">cancellation 系の失敗情報。</param>
    /// <param name="engineLogger">engine 用 logger。</param>
    /// <returns>cancellation 系失敗を表す workflow 結果。</returns>
    private WorkflowResult ToCancellationWorkflowResult(
        List<ExecutionTraceStep> traceSteps,
        StepRegistration step,
        TimeSpan elapsed,
        int attempt,
        StepCancellationFailure failure,
        ILogger engineLogger)
    {
        traceSteps.Add(new ExecutionTraceStep(
            step.Name,
            ExecutionTraceStepStatus.Failed,
            elapsed,
            failure.ErrorCode,
            attempt));
        engineLogger.LogWarning(
            "Step stopped on attempt {Attempt} with error code {ErrorCode}",
            attempt,
            failure.ErrorCode);
        engineLogger.LogWarning(
            "Entry failed on attempt {Attempt} with error code {ErrorCode}",
            attempt,
            failure.ErrorCode);

        return new WorkflowResult
        {
            EntryName = QualifiedName,
            Succeeded = false,
            ErrorCode = failure.ErrorCode,
            ErrorMessage = failure.Message,
            Trace = new ExecutionTrace(traceSteps),
        };
    }

    /// <summary>
    /// 値生成処理を追加または削除する対象の現在 Step を取得します。
    /// </summary>
    private StepRegistration CurrentStep
    {
        get
        {
            if (steps.Count == 0)
            {
                throw new InvalidOperationException("No step is registered.");
            }

            return steps[^1];
        }
    }

    /// <summary>
    /// 現在の Step に値生成処理を追加します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <param name="name">登録値の名前。</param>
    /// <param name="source">trace value に記録する生成元。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>値生成処理を追加した composite step。</returns>
    private CompositeStep<TOut> AddProducer<TValue>(
        Func<TOut, TValue> selector,
        string? name,
        ExecutionTraceValueSource source,
        TraceValueCapture? capture)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return WithCurrentStep(CurrentStep.AddProducer(
            StepValueProducer.Create(selector, name, source, capture)));
    }

    /// <summary>
    /// branch が少なくとも 1 つの実行単位を持つことを確認します。
    /// </summary>
    /// <typeparam name="TBranchOut">branch の末尾出力型。</typeparam>
    /// <param name="branch">確認する branch builder。</param>
    /// <param name="branchName">例外 message に含める branch 名。</param>
    private static void EnsureBranchHasSteps<TBranchOut>(BranchBuilder<TBranchOut> branch, string branchName)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (branch.Steps.Count == 0)
        {
            throw new InvalidOperationException($"If {branchName} branch must contain at least one step.");
        }
    }

    /// <summary>
    /// Step 列を flatten したときの実行単位数を取得します。
    /// </summary>
    /// <param name="stepSequence">数える Step 登録列。</param>
    /// <returns>If 配下の branch を含む実行単位数。</returns>
    private static int GetFlattenedStepCount(IReadOnlyList<StepRegistration> stepSequence)
    {
        return stepSequence.Sum(step => step.FlattenedLength);
    }

    /// <summary>
    /// branch 内の相対 Step Config index を composite entry 全体の index へ変換します。
    /// </summary>
    /// <param name="registrations">branch 内の Config 宣言一覧。</param>
    /// <param name="startStepIndex">branch の開始 Step index。</param>
    /// <returns>entry 全体の Step index を持つ Config 宣言一覧。</returns>
    private static IReadOnlyList<StepConfigRegistration> RemapBranchConfigRegistrations(
        IReadOnlyList<StepConfigRegistration> registrations,
        int startStepIndex)
    {
        return registrations
            .Select(registration => registration.WithStepIndex(startStepIndex + registration.StepIndex))
            .ToArray();
    }

    /// <summary>
    /// 末尾へ Step 登録情報を追加した配列を作成します。
    /// </summary>
    /// <param name="registration">追加する Step 登録情報。</param>
    /// <returns>追加後の Step 登録情報列。</returns>
    private IReadOnlyList<StepRegistration> Append(StepRegistration registration)
    {
        StepRegistration[] nextSteps = new StepRegistration[steps.Count + 1];

        for (int i = 0; i < steps.Count; i++)
        {
            nextSteps[i] = steps[i];
        }

        nextSteps[^1] = registration;

        return nextSteps;
    }

    /// <summary>
    /// 現在の Step 登録情報を差し替えた composite step を作成します。
    /// </summary>
    /// <param name="registration">差し替え後の Step 登録情報。</param>
    /// <returns>現在の Step 登録情報を差し替えた composite step。</returns>
    private CompositeStep<TOut> WithCurrentStep(StepRegistration registration)
    {
        StepRegistration[] nextSteps = steps.ToArray();
        nextSteps[^1] = registration;

        return new CompositeStep<TOut>(Name, NamespaceName, QualifiedName, nextSteps, ConfigType, StepConfigRegistrations);
    }

    /// <summary>
    /// 標準 Config instance を宣言された Config 型で StepContext に登録します。
    /// </summary>
    /// <param name="context">標準 Config instance を登録する StepContext。</param>
    /// <param name="configType">宣言された標準 Config 型。</param>
    /// <param name="standardConfig">登録する標準 Config instance。</param>
    private static void SetStandardConfig(StepContext context, Type? configType, object standardConfig)
    {
        Type targetType = configType ?? standardConfig.GetType();
        SetConfig(context, targetType, standardConfig);
    }

    /// <summary>
    /// 指定された Step index に対応する Step Config instance を StepContext に登録します。
    /// </summary>
    /// <param name="context">Step Config instance を登録する StepContext。</param>
    /// <param name="stepConfigs">検証済み Step Config instance の一覧。</param>
    /// <param name="stepIndex">実行直前の Step 登録順 index。</param>
    private static void SetStepConfig(StepContext context, IReadOnlyList<StepConfigValue> stepConfigs, int stepIndex)
    {
        foreach (StepConfigValue stepConfig in stepConfigs.Where(stepConfig => stepConfig.StepIndex == stepIndex))
        {
            SetConfig(context, stepConfig.ConfigType, stepConfig.Config);
        }
    }

    /// <summary>
    /// 指定された型で Config instance を StepContext に登録します。
    /// </summary>
    /// <param name="context">Config instance を登録する StepContext。</param>
    /// <param name="configType">StepContext 登録に使う Config 型。</param>
    /// <param name="config">登録する Config instance。</param>
    private static void SetConfig(StepContext context, Type configType, object config)
    {
        typeof(StepContext)
            .GetMethods()
            .Single(method => method.Name == nameof(StepContext.Set)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 1)
            .MakeGenericMethod(configType)
            .Invoke(context, [config]);
    }
}

/// <summary>
/// If branch 内で現在値から始まる部分的な Step 連鎖を構築します。
/// </summary>
/// <typeparam name="TOut">branch 内の現在値型。</typeparam>
public sealed class BranchBuilder<TOut>
{
    private readonly IReadOnlyList<StepRegistration> steps;
    private readonly IReadOnlyList<StepConfigRegistration> stepConfigRegistrations;

    /// <summary>
    /// 空の branch builder を初期化します。
    /// </summary>
    public BranchBuilder()
        : this([], [])
    {
    }

    /// <summary>
    /// 登録済み Step と Config 宣言を持つ branch builder を初期化します。
    /// </summary>
    /// <param name="steps">branch 内の Step 登録列。</param>
    /// <param name="stepConfigRegistrations">branch 内の Config 宣言一覧。</param>
    private BranchBuilder(
        IReadOnlyList<StepRegistration> steps,
        IReadOnlyList<StepConfigRegistration> stepConfigRegistrations)
    {
        this.steps = steps.ToArray();
        this.stepConfigRegistrations = stepConfigRegistrations.ToArray();
    }

    /// <summary>
    /// branch 内の Step 登録列を取得します。
    /// </summary>
    internal IReadOnlyList<StepRegistration> Steps => steps;

    /// <summary>
    /// branch 内の Step 登録単位 Config metadata の一覧を取得します。
    /// </summary>
    internal IReadOnlyList<StepConfigRegistration> StepConfigRegistrations => stepConfigRegistrations;

    /// <summary>
    /// 同期 Step を branch 末尾へ追加します。
    /// </summary>
    /// <typeparam name="TStep">追加する同期 Step 型。</typeparam>
    /// <typeparam name="TNext">追加した Step が返す出力型。</typeparam>
    /// <returns>末尾 Step の出力型を更新した branch builder。</returns>
    public BranchBuilder<TNext> Run<TStep, TNext>()
        where TStep : IStep<TNext>, new()
    {
        return WithAppended<TNext>(StepRegistration.Create<TStep, TNext>());
    }

    /// <summary>
    /// 現在値を受け取る同期 Lambda Step を branch 末尾へ追加します。
    /// </summary>
    /// <typeparam name="TNext">追加した Lambda Step が返す出力型。</typeparam>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="body">現在値から次の出力値を作る処理。</param>
    /// <returns>末尾 Step の出力型を更新した branch builder。</returns>
    public BranchBuilder<TNext> Run<TNext>(string name, Func<TOut, TNext> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Run(name, (current, input) => body(current));
    }

    /// <summary>
    /// 現在値と StepInput を受け取る同期 Lambda Step を branch 末尾へ追加します。
    /// </summary>
    /// <typeparam name="TNext">追加した Lambda Step が返す出力型。</typeparam>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="body">現在値と StepInput から次の出力値を作る処理。</param>
    /// <returns>末尾 Step の出力型を更新した branch builder。</returns>
    public BranchBuilder<TNext> Run<TNext>(string name, Func<TOut, StepInput, TNext> body)
    {
        return WithAppended<TNext>(StepRegistration.CreateLambda(name, body));
    }

    /// <summary>
    /// 非同期 Step を branch 末尾へ追加します。
    /// </summary>
    /// <typeparam name="TStep">追加する非同期 Step 型。</typeparam>
    /// <typeparam name="TNext">追加した Step が返す出力型。</typeparam>
    /// <returns>末尾 Step の出力型を更新した branch builder。</returns>
    public BranchBuilder<TNext> RunAsync<TStep, TNext>()
        where TStep : IAsyncStep<TNext>, new()
    {
        return WithAppended<TNext>(StepRegistration.CreateAsync<TStep, TNext>());
    }

    /// <summary>
    /// 現在値、StepInput、cancellation token を受け取る非同期 Lambda Step を branch 末尾へ追加します。
    /// </summary>
    /// <typeparam name="TNext">追加した Lambda Step が返す出力型。</typeparam>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="body">現在値、StepInput、cancellation token から次の出力値を作る非同期処理。</param>
    /// <returns>末尾 Step の出力型を更新した branch builder。</returns>
    public BranchBuilder<TNext> RunAsync<TNext>(string name, Func<TOut, StepInput, CancellationToken, Task<TNext>> body)
    {
        return WithAppended<TNext>(StepRegistration.CreateLambdaAsync(name, body));
    }

    /// <summary>
    /// 条件が true の場合だけ同期 Step を実行し、false の場合は代替値を現在値にします。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Step 型。</typeparam>
    /// <typeparam name="TNext">条件付き実行後の現在値型。</typeparam>
    /// <param name="when">現在値から実行可否を判定する処理。</param>
    /// <param name="otherwise">条件が false の場合に代替値を作る処理。</param>
    /// <returns>末尾 Step の出力型を更新した branch builder。</returns>
    public BranchBuilder<TNext> RunIf<TStep, TNext>(Func<TOut, bool> when, Func<TOut, TNext> otherwise)
        where TStep : IStep<TNext>, new()
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(otherwise);

        return RunIf<TStep, TNext>((current, input) => when(current), (current, input) => otherwise(current));
    }

    /// <summary>
    /// 条件が true の場合だけ同期 Step を実行し、false の場合は StepInput を使った代替値を現在値にします。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Step 型。</typeparam>
    /// <typeparam name="TNext">条件付き実行後の現在値型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <param name="otherwise">条件が false の場合に現在値と StepInput から代替値を作る処理。</param>
    /// <returns>末尾 Step の出力型を更新した branch builder。</returns>
    public BranchBuilder<TNext> RunIf<TStep, TNext>(Func<TOut, StepInput, bool> when, Func<TOut, StepInput, TNext> otherwise)
        where TStep : IStep<TNext>, new()
    {
        return WithAppended<TNext>(StepRegistration.CreateRunIf<TStep, TOut, TNext>(when, otherwise));
    }

    /// <summary>
    /// 条件が true の場合だけ非同期 Step を実行し、false の場合は代替値を現在値にします。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Step 型。</typeparam>
    /// <typeparam name="TNext">条件付き実行後の現在値型。</typeparam>
    /// <param name="when">現在値から実行可否を判定する処理。</param>
    /// <param name="otherwise">条件が false の場合に代替値を作る処理。</param>
    /// <returns>末尾 Step の出力型を更新した branch builder。</returns>
    public BranchBuilder<TNext> RunIfAsync<TStep, TNext>(Func<TOut, bool> when, Func<TOut, TNext> otherwise)
        where TStep : IAsyncStep<TNext>, new()
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(otherwise);

        return RunIfAsync<TStep, TNext>((current, input) => when(current), (current, input) => otherwise(current));
    }

    /// <summary>
    /// 条件が true の場合だけ非同期 Step を実行し、false の場合は StepInput を使った代替値を現在値にします。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Step 型。</typeparam>
    /// <typeparam name="TNext">条件付き実行後の現在値型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <param name="otherwise">条件が false の場合に現在値と StepInput から代替値を作る処理。</param>
    /// <returns>末尾 Step の出力型を更新した branch builder。</returns>
    public BranchBuilder<TNext> RunIfAsync<TStep, TNext>(Func<TOut, StepInput, bool> when, Func<TOut, StepInput, TNext> otherwise)
        where TStep : IAsyncStep<TNext>, new()
    {
        ArgumentNullException.ThrowIfNull(otherwise);

        return RunIfAsync<TStep, TNext>(
            when,
            (current, input, cancellationToken) => Task.FromResult(otherwise(current, input)));
    }

    /// <summary>
    /// 条件が true の場合だけ非同期 Step を実行し、false の場合は非同期代替値を現在値にします。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Step 型。</typeparam>
    /// <typeparam name="TNext">条件付き実行後の現在値型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <param name="otherwiseAsync">条件が false の場合に代替値を作る非同期処理。</param>
    /// <returns>末尾 Step の出力型を更新した branch builder。</returns>
    public BranchBuilder<TNext> RunIfAsync<TStep, TNext>(
        Func<TOut, StepInput, bool> when,
        Func<TOut, StepInput, CancellationToken, Task<TNext>> otherwiseAsync)
        where TStep : IAsyncStep<TNext>, new()
    {
        return WithAppended<TNext>(StepRegistration.CreateRunIfAsync<TStep, TOut, TNext>(when, otherwiseAsync));
    }

    /// <summary>
    /// 条件が true の場合だけ同一型同期 Step を実行し、false の場合は現在値を維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Step 型。</typeparam>
    /// <param name="when">現在値から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する branch builder。</returns>
    public BranchBuilder<TOut> RunIf<TStep>(Func<TOut, bool> when)
        where TStep : IStep<TOut>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return RunIf<TStep>((current, input) => when(current));
    }

    /// <summary>
    /// 条件が true の場合だけ同一型同期 Step を実行し、false の場合は現在値を維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Step 型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する branch builder。</returns>
    public BranchBuilder<TOut> RunIf<TStep>(Func<TOut, StepInput, bool> when)
        where TStep : IStep<TOut>, new()
    {
        return RunIf<TStep, TOut>(when, (current, input) => current);
    }

    /// <summary>
    /// 条件が true の場合だけ同一型非同期 Step を実行し、false の場合は現在値を維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Step 型。</typeparam>
    /// <param name="when">現在値から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する branch builder。</returns>
    public BranchBuilder<TOut> RunIfAsync<TStep>(Func<TOut, bool> when)
        where TStep : IAsyncStep<TOut>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return RunIfAsync<TStep>((current, input) => when(current));
    }

    /// <summary>
    /// 条件が true の場合だけ同一型非同期 Step を実行し、false の場合は現在値を維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Step 型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する branch builder。</returns>
    public BranchBuilder<TOut> RunIfAsync<TStep>(Func<TOut, StepInput, bool> when)
        where TStep : IAsyncStep<TOut>, new()
    {
        return RunIfAsync<TStep, TOut>(when, (current, input) => current);
    }

    /// <summary>
    /// 条件が true の場合だけ同期 Unit Step を実行し、現在値は維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Unit Step 型。</typeparam>
    /// <param name="when">現在値から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する branch builder。</returns>
    public BranchBuilder<TOut> TapIf<TStep>(Func<TOut, bool> when)
        where TStep : IStep<Unit>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return TapIf<TStep>((current, input) => when(current));
    }

    /// <summary>
    /// 条件が true の場合だけ同期 Unit Step を実行し、現在値は維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Unit Step 型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する branch builder。</returns>
    public BranchBuilder<TOut> TapIf<TStep>(Func<TOut, StepInput, bool> when)
        where TStep : IStep<Unit>, new()
    {
        return WithAppended<TOut>(StepRegistration.CreateTapIf<TStep, TOut>(when));
    }

    /// <summary>
    /// 条件が true の場合だけ非同期 Unit Step を実行し、現在値は維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Unit Step 型。</typeparam>
    /// <param name="when">現在値から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する branch builder。</returns>
    public BranchBuilder<TOut> TapIfAsync<TStep>(Func<TOut, bool> when)
        where TStep : IAsyncStep<Unit>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return TapIfAsync<TStep>((current, input) => when(current));
    }

    /// <summary>
    /// 条件が true の場合だけ非同期 Unit Step を実行し、現在値は維持します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Unit Step 型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <returns>現在値型を維持する branch builder。</returns>
    public BranchBuilder<TOut> TapIfAsync<TStep>(Func<TOut, StepInput, bool> when)
        where TStep : IAsyncStep<Unit>, new()
    {
        return WithAppended<TOut>(StepRegistration.CreateTapIfAsync<TStep, TOut>(when));
    }

    /// <summary>
    /// branch 内で入れ子の If を追加します。
    /// </summary>
    /// <typeparam name="TNext">分岐実行後の現在値型。</typeparam>
    /// <param name="name">trace と log に記録する If 制御単位名。</param>
    /// <param name="condition">現在値から then branch を実行するかどうかを判定する処理。</param>
    /// <param name="thenFlow">条件が true の場合に実行する分岐を定義する処理。</param>
    /// <param name="elseFlow">条件が false の場合に実行する分岐を定義する処理。</param>
    /// <returns>分岐後の現在値型を持つ branch builder。</returns>
    public BranchBuilder<TNext> If<TNext>(
        string name,
        Func<TOut, bool> condition,
        Func<BranchBuilder<TOut>, BranchBuilder<TNext>> thenFlow,
        Func<BranchBuilder<TOut>, BranchBuilder<TNext>> elseFlow)
    {
        ArgumentNullException.ThrowIfNull(condition);

        return If(name, (current, input) => condition(current), thenFlow, elseFlow);
    }

    /// <summary>
    /// branch 内で StepInput を参照する入れ子の If を追加します。
    /// </summary>
    /// <typeparam name="TNext">分岐実行後の現在値型。</typeparam>
    /// <param name="name">trace と log に記録する If 制御単位名。</param>
    /// <param name="condition">現在値と StepInput から then branch を実行するかどうかを判定する処理。</param>
    /// <param name="thenFlow">条件が true の場合に実行する分岐を定義する処理。</param>
    /// <param name="elseFlow">条件が false の場合に実行する分岐を定義する処理。</param>
    /// <returns>分岐後の現在値型を持つ branch builder。</returns>
    public BranchBuilder<TNext> If<TNext>(
        string name,
        Func<TOut, StepInput, bool> condition,
        Func<BranchBuilder<TOut>, BranchBuilder<TNext>> thenFlow,
        Func<BranchBuilder<TOut>, BranchBuilder<TNext>> elseFlow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(thenFlow);
        ArgumentNullException.ThrowIfNull(elseFlow);

        BranchBuilder<TNext> thenBranch = thenFlow(new BranchBuilder<TOut>());
        BranchBuilder<TNext> elseBranch = elseFlow(new BranchBuilder<TOut>());
        if (thenBranch.Steps.Count == 0 || elseBranch.Steps.Count == 0)
        {
            throw new InvalidOperationException("If branch must contain at least one step.");
        }

        int ifStepIndex = GetFlattenedStepCount(steps);
        int thenStartIndex = ifStepIndex + 1;
        int elseStartIndex = thenStartIndex + GetFlattenedStepCount(thenBranch.Steps);
        StepRegistration registration = StepRegistration.CreateIf(
            name,
            condition,
            thenBranch.Steps,
            elseBranch.Steps,
            thenStartIndex,
            elseStartIndex);
        IReadOnlyList<StepConfigRegistration> nextConfigRegistrations = stepConfigRegistrations
            .Concat(RemapBranchConfigRegistrations(thenBranch.StepConfigRegistrations, thenStartIndex))
            .Concat(RemapBranchConfigRegistrations(elseBranch.StepConfigRegistrations, elseStartIndex))
            .ToArray();

        return new BranchBuilder<TNext>(Append(registration), nextConfigRegistrations);
    }

    /// <summary>
    /// 直前に登録した Step に対応する Step 登録単位 Config 型と境界 Config 型上のプロパティ パスをメタ情報として設定します。
    /// </summary>
    /// <typeparam name="TConfig">StepContext に登録する Step Config 型。</typeparam>
    /// <param name="sectionPath">境界 Config 型上のプロパティ パス。</param>
    /// <returns>Step 登録単位 Config のメタ情報を持つ branch builder。</returns>
    public BranchBuilder<TOut> WithConfig<TConfig>(string sectionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);

        StepConfigRegistration[] nextRegistrations = stepConfigRegistrations
            .Append(new StepConfigRegistration(CurrentStep.StepType, sectionPath, typeof(TConfig), GetFlattenedStepCount(steps) - 1, null))
            .ToArray();

        return new BranchBuilder<TOut>(steps, nextRegistrations);
    }

    /// <summary>
    /// 直前に登録した Step に対応する Step 登録単位 Config 型、境界 Config 型上のプロパティ パス、既定 Config YAML パスをメタ情報として設定します。
    /// </summary>
    /// <typeparam name="TConfig">StepContext に登録する Step Config 型。</typeparam>
    /// <param name="sectionPath">境界 Config 型上のプロパティ パス。</param>
    /// <param name="defaultConfigPath">Entry .csx のディレクトリから解決する Step 既定 Config YAML パス。</param>
    /// <returns>明示した既定 Config YAML パスを含む Step 登録単位 Config のメタ情報を持つ branch builder。</returns>
    public BranchBuilder<TOut> WithConfig<TConfig>(string sectionPath, string defaultConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultConfigPath);

        StepConfigRegistration[] nextRegistrations = stepConfigRegistrations
            .Append(new StepConfigRegistration(CurrentStep.StepType, sectionPath, typeof(TConfig), GetFlattenedStepCount(steps) - 1, defaultConfigPath))
            .ToArray();

        return new BranchBuilder<TOut>(steps, nextRegistrations);
    }

    /// <summary>
    /// 現在の Step 出力から後続 Step へ渡す型付き値を登録します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <returns>型付き値を生成する現在の branch builder。</returns>
    public BranchBuilder<TOut> Produce<TValue>(Func<TOut, TValue> selector)
    {
        return AddProducer(selector, null, ExecutionTraceValueSource.Produce, null);
    }

    /// <summary>
    /// 現在の Step 出力から後続 Step へ渡す名前付き値を登録します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="name">登録値の名前。</param>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <returns>名前付き値を生成する現在の branch builder。</returns>
    public BranchBuilder<TOut> Produce<TValue>(string name, Func<TOut, TValue> selector)
    {
        return AddProducer(selector, name, ExecutionTraceValueSource.Produce, null);
    }

    /// <summary>
    /// 現在の Step 出力から後続 Step へ渡す型付き値を登録し、trace value を記録します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>型付き値を生成する現在の branch builder。</returns>
    public BranchBuilder<TOut> Produce<TValue>(Func<TOut, TValue> selector, TraceValueCapture capture)
    {
        return AddProducer(selector, null, ExecutionTraceValueSource.Produce, capture);
    }

    /// <summary>
    /// 現在の Step 出力から後続 Step へ渡す名前付き値を登録し、trace value を記録します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="name">登録値の名前。</param>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>名前付き値を生成する現在の branch builder。</returns>
    public BranchBuilder<TOut> Produce<TValue>(string name, Func<TOut, TValue> selector, TraceValueCapture capture)
    {
        return AddProducer(selector, name, ExecutionTraceValueSource.Produce, capture);
    }

    /// <summary>
    /// 現在の Step 出力を後続 Step へ渡す値として登録します。
    /// </summary>
    /// <returns>現在の Step 出力を生成値として登録する branch builder。</returns>
    public BranchBuilder<TOut> StoreAs()
    {
        return AddProducer<TOut>(value => value, null, ExecutionTraceValueSource.StoreAs, null);
    }

    /// <summary>
    /// 現在の Step 出力を後続 Step へ渡す値として登録し、trace value を記録します。
    /// </summary>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>現在の Step 出力を生成値として登録する branch builder。</returns>
    public BranchBuilder<TOut> StoreAs(TraceValueCapture capture)
    {
        return AddProducer<TOut>(value => value, null, ExecutionTraceValueSource.StoreAs, capture);
    }

    /// <summary>
    /// 現在の Step に登録された値生成処理を削除します。
    /// </summary>
    /// <returns>現在の Step が値を生成しない branch builder。</returns>
    public BranchBuilder<TOut> Discard()
    {
        return WithCurrentStep(CurrentStep.ClearProducers());
    }

    /// <summary>
    /// 値生成処理を追加または削除する対象の現在 Step を取得します。
    /// </summary>
    private StepRegistration CurrentStep
    {
        get
        {
            if (steps.Count == 0)
            {
                throw new InvalidOperationException("No step is registered.");
            }

            return steps[^1];
        }
    }

    /// <summary>
    /// Step 登録情報を末尾へ追加した branch builder を作成します。
    /// </summary>
    /// <typeparam name="TNext">追加後の末尾出力型。</typeparam>
    /// <param name="registration">追加する Step 登録情報。</param>
    /// <returns>Step 登録情報を追加した branch builder。</returns>
    private BranchBuilder<TNext> WithAppended<TNext>(StepRegistration registration)
    {
        return new BranchBuilder<TNext>(Append(registration), stepConfigRegistrations);
    }

    /// <summary>
    /// 現在の Step に値生成処理を追加します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <param name="name">登録値の名前。</param>
    /// <param name="source">trace value に記録する生成元。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>値生成処理を追加した branch builder。</returns>
    private BranchBuilder<TOut> AddProducer<TValue>(
        Func<TOut, TValue> selector,
        string? name,
        ExecutionTraceValueSource source,
        TraceValueCapture? capture)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return WithCurrentStep(CurrentStep.AddProducer(
            StepValueProducer.Create(selector, name, source, capture)));
    }

    /// <summary>
    /// 末尾へ Step 登録情報を追加した配列を作成します。
    /// </summary>
    /// <param name="registration">追加する Step 登録情報。</param>
    /// <returns>追加後の Step 登録情報列。</returns>
    private IReadOnlyList<StepRegistration> Append(StepRegistration registration)
    {
        StepRegistration[] nextSteps = new StepRegistration[steps.Count + 1];

        for (int i = 0; i < steps.Count; i++)
        {
            nextSteps[i] = steps[i];
        }

        nextSteps[^1] = registration;

        return nextSteps;
    }

    /// <summary>
    /// 現在の Step 登録情報を差し替えた branch builder を作成します。
    /// </summary>
    /// <param name="registration">差し替え後の Step 登録情報。</param>
    /// <returns>現在の Step 登録情報を差し替えた branch builder。</returns>
    private BranchBuilder<TOut> WithCurrentStep(StepRegistration registration)
    {
        StepRegistration[] nextSteps = steps.ToArray();
        nextSteps[^1] = registration;

        return new BranchBuilder<TOut>(nextSteps, stepConfigRegistrations);
    }

    /// <summary>
    /// Step 列を flatten したときの実行単位数を取得します。
    /// </summary>
    /// <param name="stepSequence">数える Step 登録列。</param>
    /// <returns>If 配下の branch を含む実行単位数。</returns>
    private static int GetFlattenedStepCount(IReadOnlyList<StepRegistration> stepSequence)
    {
        return stepSequence.Sum(step => step.FlattenedLength);
    }

    /// <summary>
    /// branch 内の相対 Step Config index を現在 branch の index へ変換します。
    /// </summary>
    /// <param name="registrations">入れ子 branch 内の Config 宣言一覧。</param>
    /// <param name="startStepIndex">入れ子 branch の開始 Step index。</param>
    /// <returns>現在 branch の Step index を持つ Config 宣言一覧。</returns>
    private static IReadOnlyList<StepConfigRegistration> RemapBranchConfigRegistrations(
        IReadOnlyList<StepConfigRegistration> registrations,
        int startStepIndex)
    {
        return registrations
            .Select(registration => registration.WithStepIndex(startStepIndex + registration.StepIndex))
            .ToArray();
    }
}

/// <summary>
/// Step 登録単位 Config の宣言メタ情報を保持します。
/// </summary>
public sealed class StepConfigRegistration
{
    /// <summary>
    /// Step 登録単位 Config の宣言メタ情報を初期化します。
    /// </summary>
    /// <param name="stepType">Config を使う Step 型。</param>
    /// <param name="sectionPath">境界 Config 型上のプロパティ パス。</param>
    /// <param name="configType">StepContext へ登録する Config 型。</param>
    /// <param name="stepIndex">Config を登録する Step の登録順 index。</param>
    /// <param name="defaultConfigPath">Entry .csx のディレクトリから解決する Step 既定 Config YAML パス。</param>
    internal StepConfigRegistration(Type stepType, string sectionPath, Type configType, int stepIndex, string? defaultConfigPath)
    {
        ArgumentNullException.ThrowIfNull(stepType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);
        ArgumentNullException.ThrowIfNull(configType);
        if (defaultConfigPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(defaultConfigPath);
        }

        StepType = stepType;
        SectionPath = sectionPath;
        ConfigType = configType;
        StepIndex = stepIndex;
        DefaultConfigPath = defaultConfigPath;
    }

    /// <summary>
    /// Config を使う Step 型を取得します。
    /// </summary>
    public Type StepType { get; }

    /// <summary>
    /// 境界 Config 型上のプロパティ パスを取得します。
    /// </summary>
    public string SectionPath { get; }

    /// <summary>
    /// StepContext へ登録する Config 型を取得します。
    /// </summary>
    public Type ConfigType { get; }

    /// <summary>
    /// Entry .csx のディレクトリから解決する Step 既定 Config YAML パスを取得します。明示されていない場合は null を返します。
    /// </summary>
    public string? DefaultConfigPath { get; }

    /// <summary>
    /// Config を登録する Step の登録順 index を取得します。
    /// </summary>
    internal int StepIndex { get; }

    /// <summary>
    /// Step index だけを差し替えた Config 宣言を作成します。
    /// </summary>
    /// <param name="stepIndex">差し替え後の Step index。</param>
    /// <returns>Step index を差し替えた Config 宣言。</returns>
    internal StepConfigRegistration WithStepIndex(int stepIndex)
    {
        return new StepConfigRegistration(StepType, SectionPath, ConfigType, stepIndex, DefaultConfigPath);
    }
}

/// <summary>
/// 1 つの Step 実行と値生成処理の登録情報を保持します。
/// </summary>
internal sealed class StepRegistration
{
    private readonly string name;
    private readonly Type stepType;
    private readonly Func<StepInput, object?, CancellationToken, Task<StepExecutionResult>> executeAsync;
    private readonly IReadOnlyList<StepValueProducer> producers;
    private readonly ConditionalBranchRegistration? conditionalBranch;

    /// <summary>
    /// Step 登録情報を初期化します。
    /// </summary>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="stepType">登録された Step 型。</param>
    /// <param name="executeAsync">登録済み Step を実行する処理。</param>
    /// <param name="producers">Step 成功後に実行する値生成処理。</param>
    private StepRegistration(
        string name,
        Type stepType,
        Func<StepInput, object?, CancellationToken, Task<StepExecutionResult>> executeAsync,
        IReadOnlyList<StepValueProducer> producers,
        ConditionalBranchRegistration? conditionalBranch = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stepType);
        ArgumentNullException.ThrowIfNull(executeAsync);
        ArgumentNullException.ThrowIfNull(producers);

        this.name = name;
        this.stepType = stepType;
        this.executeAsync = executeAsync;
        this.producers = producers.ToArray();
        this.conditionalBranch = conditionalBranch;
    }

    /// <summary>
    /// trace と log に記録する Step 名を取得します。
    /// </summary>
    public string Name => name;

    /// <summary>
    /// 登録された Step 型を取得します。
    /// </summary>
    public Type StepType => stepType;

    /// <summary>
    /// If 制御単位かどうかを取得します。
    /// </summary>
    public bool IsConditionalBranch => conditionalBranch is not null;

    /// <summary>
    /// If 配下の branch を含めて flatten した実行単位数を取得します。
    /// </summary>
    public int FlattenedLength => conditionalBranch?.FlattenedLength ?? 1;

    /// <summary>
    /// 同期 Step の登録情報を作成します。
    /// </summary>
    /// <typeparam name="TStep">登録する同期 Step 型。</typeparam>
    /// <typeparam name="TOut">同期 Step が返す出力型。</typeparam>
    /// <returns>作成した Step 登録情報。</returns>
    public static StepRegistration Create<TStep, TOut>()
        where TStep : IStep<TOut>, new()
    {
        return new StepRegistration(
            typeof(TStep).Name,
            typeof(TStep),
            (input, currentValue, cancellationToken) =>
            {
                return Task.FromResult(StepExecutionResult.Succeeded(new TStep().Execute(input)));
            },
            []);
    }

    /// <summary>
    /// 非同期 Step の登録情報を作成します。
    /// </summary>
    /// <typeparam name="TStep">登録する非同期 Step 型。</typeparam>
    /// <typeparam name="TOut">非同期 Step が返す出力型。</typeparam>
    /// <returns>作成した Step 登録情報。</returns>
    public static StepRegistration CreateAsync<TStep, TOut>()
        where TStep : IAsyncStep<TOut>, new()
    {
        return new StepRegistration(
            typeof(TStep).Name,
            typeof(TStep),
            async (input, currentValue, cancellationToken) =>
                StepExecutionResult.Succeeded(await new TStep().ExecuteAsync(input, cancellationToken).ConfigureAwait(false)),
            []);
    }

    /// <summary>
    /// 最初に実行する同期 Lambda Step の登録情報を作成します。
    /// </summary>
    /// <typeparam name="TOut">Lambda Step が返す出力型。</typeparam>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="body">StepInput から出力値を作る処理。</param>
    /// <returns>作成した Step 登録情報。</returns>
    public static StepRegistration CreateLambda<TOut>(string name, Func<StepInput, TOut> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(body);

        return new StepRegistration(
            name,
            typeof(LambdaStepRegistrationMarker),
            (input, currentValue, cancellationToken) => Task.FromResult(StepExecutionResult.Succeeded(body(input))),
            []);
    }

    /// <summary>
    /// 現在値を受け取る同期 Lambda Step の登録情報を作成します。
    /// </summary>
    /// <typeparam name="TCurrent">Lambda Step へ渡す現在値の型。</typeparam>
    /// <typeparam name="TNext">Lambda Step が返す出力型。</typeparam>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="body">現在値と StepInput から次の出力値を作る処理。</param>
    /// <returns>作成した Step 登録情報。</returns>
    public static StepRegistration CreateLambda<TCurrent, TNext>(string name, Func<TCurrent, StepInput, TNext> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(body);

        return new StepRegistration(
            name,
            typeof(LambdaStepRegistrationMarker),
            (input, currentValue, cancellationToken) =>
                Task.FromResult(StepExecutionResult.Succeeded(body((TCurrent)currentValue!, input))),
            []);
    }

    /// <summary>
    /// 最初に実行する非同期 Lambda Step の登録情報を作成します。
    /// </summary>
    /// <typeparam name="TOut">Lambda Step が返す出力型。</typeparam>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="body">StepInput と cancellation token から出力値を作る非同期処理。</param>
    /// <returns>作成した Step 登録情報。</returns>
    public static StepRegistration CreateLambdaAsync<TOut>(
        string name,
        Func<StepInput, CancellationToken, Task<TOut>> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(body);

        return new StepRegistration(
            name,
            typeof(LambdaStepRegistrationMarker),
            async (input, currentValue, cancellationToken) =>
                StepExecutionResult.Succeeded(await body(input, cancellationToken).ConfigureAwait(false)),
            []);
    }

    /// <summary>
    /// 現在値を受け取る非同期 Lambda Step の登録情報を作成します。
    /// </summary>
    /// <typeparam name="TCurrent">Lambda Step へ渡す現在値の型。</typeparam>
    /// <typeparam name="TNext">Lambda Step が返す出力型。</typeparam>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="body">現在値、StepInput、cancellation token から次の出力値を作る非同期処理。</param>
    /// <returns>作成した Step 登録情報。</returns>
    public static StepRegistration CreateLambdaAsync<TCurrent, TNext>(
        string name,
        Func<TCurrent, StepInput, CancellationToken, Task<TNext>> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(body);

        return new StepRegistration(
            name,
            typeof(LambdaStepRegistrationMarker),
            async (input, currentValue, cancellationToken) =>
                StepExecutionResult.Succeeded(await body((TCurrent)currentValue!, input, cancellationToken).ConfigureAwait(false)),
            []);
    }

    /// <summary>
    /// 条件付き同期 Step の登録情報を作成します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Step 型。</typeparam>
    /// <typeparam name="TCurrent">条件判定に渡す現在値型。</typeparam>
    /// <typeparam name="TNext">条件付き実行後の現在値型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <param name="otherwise">条件が false の場合に代替値を作る処理。</param>
    /// <returns>作成した Step 登録情報。</returns>
    public static StepRegistration CreateRunIf<TStep, TCurrent, TNext>(
        Func<TCurrent, StepInput, bool> when,
        Func<TCurrent, StepInput, TNext> otherwise)
        where TStep : IStep<TNext>, new()
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(otherwise);

        return new StepRegistration(
            typeof(TStep).Name,
            typeof(TStep),
            (input, currentValue, cancellationToken) =>
            {
                TCurrent current = (TCurrent)currentValue!;
                if (!EvaluateCondition(() => when(current, input)))
                {
                    return Task.FromResult(StepExecutionResult.Skipped(otherwise(current, input)));
                }

                return Task.FromResult(StepExecutionResult.Succeeded(new TStep().Execute(input)));
            },
            []);
    }

    /// <summary>
    /// 条件付き非同期 Step の登録情報を作成します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Step 型。</typeparam>
    /// <typeparam name="TCurrent">条件判定に渡す現在値型。</typeparam>
    /// <typeparam name="TNext">条件付き実行後の現在値型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <param name="otherwiseAsync">条件が false の場合に代替値を作る非同期処理。</param>
    /// <returns>作成した Step 登録情報。</returns>
    public static StepRegistration CreateRunIfAsync<TStep, TCurrent, TNext>(
        Func<TCurrent, StepInput, bool> when,
        Func<TCurrent, StepInput, CancellationToken, Task<TNext>> otherwiseAsync)
        where TStep : IAsyncStep<TNext>, new()
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(otherwiseAsync);

        return new StepRegistration(
            typeof(TStep).Name,
            typeof(TStep),
            async (input, currentValue, cancellationToken) =>
            {
                TCurrent current = (TCurrent)currentValue!;
                if (!EvaluateCondition(() => when(current, input)))
                {
                    return StepExecutionResult.Skipped(await otherwiseAsync(current, input, cancellationToken).ConfigureAwait(false));
                }

                return StepExecutionResult.Succeeded(await new TStep().ExecuteAsync(input, cancellationToken).ConfigureAwait(false));
            },
            []);
    }

    /// <summary>
    /// 条件付き同期 Unit Step の登録情報を作成します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する同期 Unit Step 型。</typeparam>
    /// <typeparam name="TCurrent">条件判定に渡す現在値型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <returns>作成した Step 登録情報。</returns>
    public static StepRegistration CreateTapIf<TStep, TCurrent>(Func<TCurrent, StepInput, bool> when)
        where TStep : IStep<Unit>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return new StepRegistration(
            typeof(TStep).Name,
            typeof(TStep),
            (input, currentValue, cancellationToken) =>
            {
                TCurrent current = (TCurrent)currentValue!;
                if (!EvaluateCondition(() => when(current, input)))
                {
                    return Task.FromResult(StepExecutionResult.Skipped(current));
                }

                new TStep().Execute(input);

                return Task.FromResult(StepExecutionResult.Succeeded(current));
            },
            []);
    }

    /// <summary>
    /// 条件付き非同期 Unit Step の登録情報を作成します。
    /// </summary>
    /// <typeparam name="TStep">条件が true の場合に実行する非同期 Unit Step 型。</typeparam>
    /// <typeparam name="TCurrent">条件判定に渡す現在値型。</typeparam>
    /// <param name="when">現在値と StepInput から実行可否を判定する処理。</param>
    /// <returns>作成した Step 登録情報。</returns>
    public static StepRegistration CreateTapIfAsync<TStep, TCurrent>(Func<TCurrent, StepInput, bool> when)
        where TStep : IAsyncStep<Unit>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return new StepRegistration(
            typeof(TStep).Name,
            typeof(TStep),
            async (input, currentValue, cancellationToken) =>
            {
                TCurrent current = (TCurrent)currentValue!;
                if (!EvaluateCondition(() => when(current, input)))
                {
                    return StepExecutionResult.Skipped(current);
                }

                await new TStep().ExecuteAsync(input, cancellationToken).ConfigureAwait(false);

                return StepExecutionResult.Succeeded(current);
            },
            []);
    }

    /// <summary>
    /// If 制御単位の登録情報を作成します。
    /// </summary>
    /// <typeparam name="TCurrent">条件判定に渡す現在値型。</typeparam>
    /// <param name="name">trace と log に記録する If 制御単位名。</param>
    /// <param name="condition">現在値と StepInput から then branch を実行するかどうかを判定する処理。</param>
    /// <param name="thenSteps">条件が true の場合に実行する Step 登録列。</param>
    /// <param name="elseSteps">条件が false の場合に実行する Step 登録列。</param>
    /// <param name="thenStartStepIndex">then branch の開始 Step index。</param>
    /// <param name="elseStartStepIndex">else branch の開始 Step index。</param>
    /// <returns>作成した If 制御単位の登録情報。</returns>
    public static StepRegistration CreateIf<TCurrent>(
        string name,
        Func<TCurrent, StepInput, bool> condition,
        IReadOnlyList<StepRegistration> thenSteps,
        IReadOnlyList<StepRegistration> elseSteps,
        int thenStartStepIndex,
        int elseStartStepIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(thenSteps);
        ArgumentNullException.ThrowIfNull(elseSteps);

        var conditionalBranch = new ConditionalBranchRegistration(
            (input, currentValue) => EvaluateCondition(() => condition((TCurrent)currentValue!, input)),
            thenSteps,
            elseSteps,
            thenStartStepIndex,
            elseStartStepIndex);

        return new StepRegistration(
            name,
            typeof(IfStepRegistrationMarker),
            (input, currentValue, cancellationToken) => Task.FromResult(StepExecutionResult.Succeeded(currentValue)),
            [],
            conditionalBranch);
    }

    /// <summary>
    /// If 条件を評価し、選択された branch 実行計画を取得します。
    /// </summary>
    /// <param name="input">条件判定へ渡す StepInput。</param>
    /// <param name="currentValue">条件判定へ渡す現在値。</param>
    /// <returns>選択された branch の実行計画。</returns>
    public BranchExecutionPlan GetBranch(StepInput input, object? currentValue)
    {
        if (conditionalBranch is null)
        {
            throw new InvalidOperationException("Step is not an If branch.");
        }

        return conditionalBranch.GetBranch(input, currentValue);
    }

    /// <summary>
    /// If 制御単位なら条件を評価し、選択された branch 実行計画を返します。
    /// </summary>
    /// <param name="input">条件判定へ渡す StepInput。</param>
    /// <param name="currentValue">条件判定へ渡す現在値。</param>
    /// <param name="branchPlan">選択された branch の実行計画。</param>
    /// <returns>If 制御単位の場合は true。</returns>
    public bool TryGetBranch(StepInput input, object? currentValue, out BranchExecutionPlan? branchPlan)
    {
        if (conditionalBranch is null)
        {
            branchPlan = null;

            return false;
        }

        branchPlan = conditionalBranch.GetBranch(input, currentValue);

        return true;
    }

    /// <summary>
    /// 登録済み Step を実行します。
    /// </summary>
    /// <param name="input">Step へ渡す入力値。</param>
    /// <param name="currentValue">直前の Step が返した現在値。</param>
    /// <param name="cancellationToken">Step へ渡す cancellation token。</param>
    /// <returns>Step が返した出力値。</returns>
    public Task<StepExecutionResult> ExecuteAsync(StepInput input, object? currentValue, CancellationToken cancellationToken)
    {
        return executeAsync(input, currentValue, cancellationToken);
    }

    /// <summary>
    /// Step 成功後に入力へ値を追加する producer を加えます。
    /// </summary>
    /// <param name="producer">追加する値生成処理。</param>
    /// <returns>値生成処理を追加した Step 登録情報。</returns>
    public StepRegistration AddProducer(StepValueProducer producer)
    {
        ArgumentNullException.ThrowIfNull(producer);

        StepValueProducer[] nextProducers = new StepValueProducer[producers.Count + 1];

        for (int i = 0; i < producers.Count; i++)
        {
            nextProducers[i] = producers[i];
        }

        nextProducers[^1] = producer;

        return new StepRegistration(name, stepType, executeAsync, nextProducers, conditionalBranch);
    }

    /// <summary>
    /// 登録済み producer を削除した Step 登録情報を作成します。
    /// </summary>
    /// <returns>値生成処理を削除した Step 登録情報。</returns>
    public StepRegistration ClearProducers()
    {
        return new StepRegistration(name, stepType, executeAsync, [], conditionalBranch);
    }

    /// <summary>
    /// 登録済み producer を実行し、成功した場合だけ trace value を返します。
    /// </summary>
    /// <param name="input">値を追加する StepInput。</param>
    /// <param name="value">現在の Step 出力。</param>
    /// <returns>作成された trace value の一覧。</returns>
    public IReadOnlyList<ExecutionTraceValue> Produce(StepInput input, object? value)
    {
        var producedValues = new List<ExecutionTraceValue>();

        foreach (StepValueProducer producer in producers)
        {
            ExecutionTraceValue? producedValue = producer.Produce(input, value);
            if (producedValue is not null)
            {
                producedValues.Add(producedValue);
            }
        }

        return producedValues;
    }

    /// <summary>
    /// 条件判定を実行し、例外を条件判定失敗として包みます。
    /// </summary>
    /// <param name="condition">実行する条件判定。</param>
    /// <returns>条件判定の戻り値。</returns>
    private static bool EvaluateCondition(Func<bool> condition)
    {
        try
        {
            return condition();
        }
        catch (Exception exception)
        {
            throw new StepConditionEvaluationException(exception);
        }
    }
}

/// <summary>
/// If branch の条件と分岐 Step 列を保持します。
/// </summary>
internal sealed class ConditionalBranchRegistration
{
    private readonly Func<StepInput, object?, bool> condition;
    private readonly IReadOnlyList<StepRegistration> thenSteps;
    private readonly IReadOnlyList<StepRegistration> elseSteps;
    private readonly int thenStartStepIndex;
    private readonly int elseStartStepIndex;

    /// <summary>
    /// If branch の条件と分岐 Step 列を初期化します。
    /// </summary>
    /// <param name="condition">then branch を選ぶかどうかを判定する処理。</param>
    /// <param name="thenSteps">条件が true の場合に実行する Step 登録列。</param>
    /// <param name="elseSteps">条件が false の場合に実行する Step 登録列。</param>
    /// <param name="thenStartStepIndex">then branch の開始 Step index。</param>
    /// <param name="elseStartStepIndex">else branch の開始 Step index。</param>
    public ConditionalBranchRegistration(
        Func<StepInput, object?, bool> condition,
        IReadOnlyList<StepRegistration> thenSteps,
        IReadOnlyList<StepRegistration> elseSteps,
        int thenStartStepIndex,
        int elseStartStepIndex)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(thenSteps);
        ArgumentNullException.ThrowIfNull(elseSteps);

        this.condition = condition;
        this.thenSteps = thenSteps.ToArray();
        this.elseSteps = elseSteps.ToArray();
        this.thenStartStepIndex = thenStartStepIndex;
        this.elseStartStepIndex = elseStartStepIndex;
    }

    /// <summary>
    /// If と両 branch を flatten した実行単位数を取得します。
    /// </summary>
    public int FlattenedLength => 1 + GetFlattenedStepCount(thenSteps) + GetFlattenedStepCount(elseSteps);

    /// <summary>
    /// 条件を評価し、選択された branch の実行計画を取得します。
    /// </summary>
    /// <param name="input">条件判定へ渡す StepInput。</param>
    /// <param name="currentValue">条件判定へ渡す現在値。</param>
    /// <returns>選択された branch の実行計画。</returns>
    public BranchExecutionPlan GetBranch(StepInput input, object? currentValue)
    {
        if (condition(input, currentValue))
        {
            return new BranchExecutionPlan(thenSteps, thenStartStepIndex);
        }

        return new BranchExecutionPlan(elseSteps, elseStartStepIndex);
    }

    /// <summary>
    /// Step 列を flatten したときの実行単位数を取得します。
    /// </summary>
    /// <param name="stepSequence">数える Step 登録列。</param>
    /// <returns>If 配下の branch を含む実行単位数。</returns>
    private static int GetFlattenedStepCount(IReadOnlyList<StepRegistration> stepSequence)
    {
        return stepSequence.Sum(step => step.FlattenedLength);
    }
}

/// <summary>
/// 選択された branch の Step 列と開始 Step index を保持します。
/// </summary>
/// <param name="Steps">選択された branch の Step 登録列。</param>
/// <param name="StartStepIndex">選択された branch の開始 Step index。</param>
internal sealed record BranchExecutionPlan(IReadOnlyList<StepRegistration> Steps, int StartStepIndex);

/// <summary>
/// workflow Step 列の内部実行結果を保持します。
/// </summary>
internal sealed class WorkflowSequenceExecutionResult
{
    /// <summary>
    /// workflow Step 列の内部実行結果を初期化します。
    /// </summary>
    /// <param name="succeeded">Step 列が成功したかどうか。</param>
    /// <param name="value">成功時の現在値。</param>
    /// <param name="failure">失敗時の workflow 結果。</param>
    private WorkflowSequenceExecutionResult(bool succeeded, object? value, WorkflowResult? failure)
    {
        Succeeded = succeeded;
        Value = value;
        Failure = failure;
    }

    /// <summary>
    /// Step 列が成功したかどうかを取得します。
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// 成功時の現在値を取得します。
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// 失敗時の workflow 結果を取得します。
    /// </summary>
    public WorkflowResult? Failure { get; }

    /// <summary>
    /// 成功した Step 列の内部実行結果を作成します。
    /// </summary>
    /// <param name="value">成功時の現在値。</param>
    /// <returns>成功を表す内部実行結果。</returns>
    public static WorkflowSequenceExecutionResult Success(object? value)
    {
        return new WorkflowSequenceExecutionResult(true, value, null);
    }

    /// <summary>
    /// 失敗した Step 列の内部実行結果を作成します。
    /// </summary>
    /// <param name="failure">失敗時の workflow 結果。</param>
    /// <returns>失敗を表す内部実行結果。</returns>
    public static WorkflowSequenceExecutionResult Failed(WorkflowResult failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new WorkflowSequenceExecutionResult(false, null, failure);
    }
}

/// <summary>
/// Step 実行後の現在値と trace 状態を保持します。
/// </summary>
internal sealed class StepExecutionResult
{
    /// <summary>
    /// Step 実行結果を初期化します。
    /// </summary>
    /// <param name="value">次の Step へ渡す現在値。</param>
    /// <param name="status">trace に記録する実行状態。</param>
    private StepExecutionResult(object? value, ExecutionTraceStepStatus status)
    {
        Value = value;
        Status = status;
    }

    /// <summary>
    /// 次の Step へ渡す現在値を取得します。
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// trace に記録する実行状態を取得します。
    /// </summary>
    public ExecutionTraceStepStatus Status { get; }

    /// <summary>
    /// 成功した Step 実行結果を作成します。
    /// </summary>
    /// <param name="value">次の Step へ渡す現在値。</param>
    /// <returns>成功状態の Step 実行結果。</returns>
    public static StepExecutionResult Succeeded(object? value)
    {
        return new StepExecutionResult(value, ExecutionTraceStepStatus.Succeeded);
    }

    /// <summary>
    /// skip した Step 実行結果を作成します。
    /// </summary>
    /// <param name="value">次の Step へ渡す現在値。</param>
    /// <returns>skip 状態の Step 実行結果。</returns>
    public static StepExecutionResult Skipped(object? value)
    {
        return new StepExecutionResult(value, ExecutionTraceStepStatus.Skipped);
    }
}

/// <summary>
/// 条件判定中の例外を engine の条件判定失敗へ変換するために保持します。
/// </summary>
internal sealed class StepConditionEvaluationException : Exception
{
    /// <summary>
    /// 元の例外を保持して条件判定失敗例外を作成します。
    /// </summary>
    /// <param name="innerException">条件判定中に発生した元の例外。</param>
    public StepConditionEvaluationException(Exception innerException)
        : base("Step condition evaluation failed.", innerException)
    {
    }
}

/// <summary>
/// Lambda Step の登録単位 Config metadata で使う内部 Step 型を表します。
/// </summary>
internal sealed class LambdaStepRegistrationMarker
{
    /// <summary>
    /// 外部から生成しない marker 型として初期化を隠します。
    /// </summary>
    private LambdaStepRegistrationMarker()
    {
    }
}

/// <summary>
/// If 制御単位の登録情報で使う内部 Step 型を表します。
/// </summary>
internal sealed class IfStepRegistrationMarker
{
    /// <summary>
    /// 外部から生成しない marker 型として初期化を隠します。
    /// </summary>
    private IfStepRegistrationMarker()
    {
    }
}

/// <summary>
/// Step 出力から後続 Step 用の値と trace value を生成します。
/// </summary>
internal sealed class StepValueProducer
{
    private readonly Type valueType;
    private readonly string? name;
    private readonly ExecutionTraceValueSource source;
    private readonly TraceValueCapture? capture;
    private readonly Func<object?, object?> selectValue;
    private readonly Action<StepInput, object?> addValue;

    /// <summary>
    /// 値生成処理を初期化します。
    /// </summary>
    /// <param name="valueType">登録する値の型。</param>
    /// <param name="name">登録値の名前。</param>
    /// <param name="source">trace value に記録する生成元。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <param name="selectValue">Step 出力から登録値を選択する処理。</param>
    /// <param name="addValue">選択済み値を StepInput へ登録する処理。</param>
    private StepValueProducer(
        Type valueType,
        string? name,
        ExecutionTraceValueSource source,
        TraceValueCapture? capture,
        Func<object?, object?> selectValue,
        Action<StepInput, object?> addValue)
    {
        this.valueType = valueType;
        this.name = name;
        this.source = source;
        this.capture = capture;
        this.selectValue = selectValue;
        this.addValue = addValue;
    }

    /// <summary>
    /// 型付き selector から値生成処理を作成します。
    /// </summary>
    /// <typeparam name="TCurrent">現在の Step 出力型。</typeparam>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <param name="name">登録値の名前。</param>
    /// <param name="source">trace value に記録する生成元。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>作成した値生成処理。</returns>
    public static StepValueProducer Create<TCurrent, TValue>(
        Func<TCurrent, TValue> selector,
        string? name,
        ExecutionTraceValueSource source,
        TraceValueCapture? capture)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ValidateCapture(capture);

        return new StepValueProducer(
            typeof(TValue),
            name,
            source,
            capture,
            value => selector((TCurrent)value!),
            (input, producedValue) =>
            {
                if (name is null)
                {
                    input.Add((TValue)producedValue!);
                    return;
                }

                input.Add(name, (TValue)producedValue!);
            });
    }

    /// <summary>
    /// StepInput へ値を追加し、設定されている場合だけ trace value を作成します。
    /// </summary>
    /// <param name="input">値を追加する StepInput。</param>
    /// <param name="stepOutput">現在の Step 出力。</param>
    /// <returns>作成した trace value。capture が未指定の場合は null。</returns>
    public ExecutionTraceValue? Produce(StepInput input, object? stepOutput)
    {
        object? producedValue = selectValue(stepOutput);
        addValue(input, producedValue);

        if (capture is null)
        {
            return null;
        }

        return CreateTraceValue(producedValue, capture.Value);
    }

    /// <summary>
    /// trace value の記録方法が有効値か確認します。
    /// </summary>
    /// <param name="capture">確認する記録方法。</param>
    private static void ValidateCapture(TraceValueCapture? capture)
    {
        if (capture is null
            or TraceValueCapture.Serialized
            or TraceValueCapture.Redacted)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(capture), capture, "Unsupported trace value capture.");
    }

    /// <summary>
    /// 登録済み値から trace value を作成します。
    /// </summary>
    /// <param name="producedValue">trace に記録する対象値。</param>
    /// <param name="traceCapture">trace value の記録方法。</param>
    /// <returns>作成した trace value。</returns>
    private ExecutionTraceValue CreateTraceValue(object? producedValue, TraceValueCapture traceCapture)
    {
        string typeName = valueType.FullName ?? valueType.Name;

        if (traceCapture == TraceValueCapture.Redacted)
        {
            return new ExecutionTraceValue(
                typeName,
                name,
                source,
                ExecutionTraceValueCaptureStatus.Redacted,
                null,
                null);
        }

        try
        {
            return new ExecutionTraceValue(
                typeName,
                name,
                source,
                ExecutionTraceValueCaptureStatus.Serialized,
                JsonSerializer.Serialize(producedValue, valueType),
                null);
        }
        catch (Exception exception)
        {
            return new ExecutionTraceValue(
                typeName,
                name,
                source,
                ExecutionTraceValueCaptureStatus.NotSerializable,
                null,
                BuildSerializationFailureReason(exception));
        }
    }

    /// <summary>
    /// 直列化失敗の理由を trace value 用の短い文字列に変換します。
    /// </summary>
    /// <param name="exception">直列化中に発生した例外。</param>
    /// <returns>利用者へ返す直列化失敗理由。</returns>
    private static string BuildSerializationFailureReason(Exception exception)
    {
        return $"Trace value serialization failed: {exception.GetType().Name}.";
    }
}

/// <summary>
/// Step 実行中に使う timeout と外部キャンセルの合成状態を保持します。
/// </summary>
internal sealed class StepExecutionCancellation : IDisposable
{
    private readonly CancellationTokenSource? timeoutSource;
    private readonly CancellationTokenSource? linkedSource;

    /// <summary>
    /// Step 実行中に使う cancellation token と所有する source を初期化します。
    /// </summary>
    /// <param name="token">Step へ渡す cancellation token。</param>
    /// <param name="timeout">設定された Step timeout。</param>
    /// <param name="timeoutSource">timeout 発火を管理する source。</param>
    /// <param name="linkedSource">外部キャンセルと timeout を合成した source。</param>
    public StepExecutionCancellation(
        CancellationToken token,
        TimeSpan? timeout,
        CancellationTokenSource? timeoutSource,
        CancellationTokenSource? linkedSource)
    {
        Token = token;
        Timeout = timeout;
        this.timeoutSource = timeoutSource;
        this.linkedSource = linkedSource;
    }

    /// <summary>
    /// Step へ渡す合成済み cancellation token を取得します。
    /// </summary>
    public CancellationToken Token { get; }

    /// <summary>
    /// timeout が発火したかどうかを取得します。
    /// </summary>
    public bool TimeoutWasRequested => timeoutSource?.IsCancellationRequested == true;

    /// <summary>
    /// 設定された Step timeout を取得します。
    /// </summary>
    public TimeSpan? Timeout { get; }

    /// <summary>
    /// Step 実行用に作成した cancellation source を解放します。
    /// </summary>
    public void Dispose()
    {
        linkedSource?.Dispose();
        timeoutSource?.Dispose();
    }
}

/// <summary>
/// timeout または外部キャンセルによる Step 失敗情報を表します。
/// </summary>
internal sealed class StepCancellationFailure
{
    /// <summary>
    /// cancellation 系の失敗情報を初期化します。
    /// </summary>
    /// <param name="errorCode">WorkflowResult と trace に記録する error code。</param>
    /// <param name="message">WorkflowResult に記録する説明文。</param>
    private StepCancellationFailure(string errorCode, string message)
    {
        ErrorCode = errorCode;
        Message = message;
    }

    /// <summary>
    /// WorkflowResult と trace に記録する error code を取得します。
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// WorkflowResult に記録する説明文を取得します。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// timeout 失敗を表す値を作成します。
    /// </summary>
    /// <param name="stepName">timeout した Step 名。</param>
    /// <param name="timeout">設定された Step timeout。</param>
    /// <param name="message">例外から取得した説明文。</param>
    /// <returns>timeout 失敗情報。</returns>
    public static StepCancellationFailure TimedOut(string stepName, TimeSpan timeout, string? message)
    {
        return new StepCancellationFailure(
            WorkflowErrorCodes.StepTimeout,
            message ?? $"Step '{stepName}' timed out after {timeout}.");
    }

    /// <summary>
    /// 外部キャンセル失敗を表す値を作成します。
    /// </summary>
    /// <param name="message">例外から取得した説明文。</param>
    /// <returns>外部キャンセル失敗情報。</returns>
    public static StepCancellationFailure Canceled(string? message)
    {
        return new StepCancellationFailure(
            WorkflowErrorCodes.StepCanceled,
            message ?? "Step was canceled.");
    }
}
