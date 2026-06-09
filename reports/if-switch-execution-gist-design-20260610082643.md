# Devo6.WorkFlow 条件付きメソッドチェーン設計書

作成日: 2026-06-09  
対象: `Devo6.WorkFlow.Engine`  
対象外: DAG、`for` / `ForEach`、キャッシュ、incremental build、条件付き依存グラフ

---

## 1. 結論

`CompositeStep<TOut>` のメソッドチェーンは維持する。

追加する機能は次の 5 つに絞る。

```text
1. Lambda Step
2. RunIf
3. TapIf
4. If
5. Switch
```

`for` / `ForEach` は初期実装には含めない。  
繰り返し処理は通常の Step 内で C# の `foreach` を書けば実現できるため、ワークフロー DSL の機能としては後回しにする。

設計の中心ルールは次の通り。

```text
skip は「戻り値がない」ではなく、
「Step 本体を実行せず、代わりの戻り値を返す」として扱う。
```

これにより、`CompositeStep<TOut>` の型安全性を維持できる。

---

## 2. 現状前提

現在の `CompositeStep<TOut>` は、末尾 Step の出力型を `TOut` として保持する。

既存 API の基本形は次の通り。

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .Produce<ConvertInput>(x => new ConvertInput(x.Text))
    .Run<ConvertStep, ConvertResult>()
        .Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
    .Run<SaveStep, Unit>()
        .Discard();
```

既存の実装は、概念的には次の構造である。

```csharp
public sealed class CompositeStep<TOut> : IStep<TOut>, IAsyncStep<TOut>
{
    private readonly IReadOnlyList<StepRegistration> steps;

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

    public async Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
    {
        object? currentValue = default(TOut);

        foreach (StepRegistration step in steps)
        {
            currentValue = await step.ExecuteAsync(input, cancellationToken)
                .ConfigureAwait(false);

            step.Produce(input, currentValue);
        }

        return (TOut)currentValue!;
    }
}
```

この構造では、通常の `Run<TStep, TNext>()` を呼ぶとチェーンの現在値は `TNext` に変わる。

そのため、単純な skip API は危険である。

```csharp
// 採用しない
.Run<ConvertStep, ConvertResult>()
    .WithCriteria(x => x.ShouldConvert)
```

この場合、`ShouldConvert == false` のとき `ConvertStep` は実行されない。  
しかしチェーンの型は `CompositeStep<ConvertResult>` に変わっている。

つまり実行時に次の状態が起こる。

```text
型としては ConvertResult がある
実際には ConvertResult が生成されていない
```

これを避けるため、条件付き実行では false 時の戻り値を明示する。

```csharp
.RunIf<ConvertStep, ConvertResult>(
    when: x => x.ShouldConvert,
    otherwise: x => new ConvertResult(x.Text))
```

---

## 3. 用語

| 用語 | 意味 |
|---|---|
| current value | 現在のチェーンが保持している直前ノードの戻り値。`CompositeStep<TOut>` の `TOut`。 |
| node | 実行単位。通常 Step、Lambda Step、RunIf、TapIf、If、Switch をすべて node と呼ぶ。 |
| Step node | `IStep<TOut>` または `IAsyncStep<TOut>` を実行する通常ノード。 |
| Lambda Step | ラムダ式を Step として実行し、戻り値を current value にするノード。 |
| fallback | 条件付き Step を skip した場合に代わりに返す値。 |
| tap | current value を変更せず、副作用 Step だけを実行する処理。 |
| branch | `If` / `Switch` の then / else / case 内の部分チェーン。 |
| control node | `If` / `Switch` のように、内部 branch を選択して実行するノード。 |

---

## 4. 実装対象 API 一覧

### 4.1 Lambda Step

#### 4.1.1 CompositeStepDefinition から開始する Lambda Step

```csharp
public sealed class CompositeStepDefinition
{
    public CompositeStep<TOut> Run<TOut>(
        string name,
        Func<StepInput, TOut> body);

    public CompositeStep<TOut> RunAsync<TOut>(
        string name,
        Func<StepInput, CancellationToken, Task<TOut>> body);
}
```

#### 4.1.2 CompositeStep<TOut> の途中で使う Lambda Step

```csharp
public sealed class CompositeStep<TOut>
{
    public CompositeStep<TNext> Run<TNext>(
        string name,
        Func<TOut, TNext> body);

    public CompositeStep<TNext> Run<TNext>(
        string name,
        Func<TOut, StepInput, TNext> body);

    public CompositeStep<TNext> RunAsync<TNext>(
        string name,
        Func<TOut, StepInput, CancellationToken, Task<TNext>> body);
}
```

### 4.2 RunIf

#### 4.2.1 戻り値型が変わる RunIf

```csharp
public sealed class CompositeStep<TOut>
{
    public CompositeStep<TNext> RunIf<TStep, TNext>(
        Func<TOut, bool> when,
        Func<TOut, TNext> otherwise)
        where TStep : IStep<TNext>, new();

    public CompositeStep<TNext> RunIf<TStep, TNext>(
        Func<TOut, StepInput, bool> when,
        Func<TOut, StepInput, TNext> otherwise)
        where TStep : IStep<TNext>, new();

    public CompositeStep<TNext> RunIfAsync<TStep, TNext>(
        Func<TOut, bool> when,
        Func<TOut, TNext> otherwise)
        where TStep : IAsyncStep<TNext>, new();

    public CompositeStep<TNext> RunIfAsync<TStep, TNext>(
        Func<TOut, StepInput, bool> when,
        Func<TOut, StepInput, TNext> otherwise)
        where TStep : IAsyncStep<TNext>, new();

    public CompositeStep<TNext> RunIfAsync<TStep, TNext>(
        Func<TOut, StepInput, bool> when,
        Func<TOut, StepInput, CancellationToken, Task<TNext>> otherwiseAsync)
        where TStep : IAsyncStep<TNext>, new();
}
```

#### 4.2.2 戻り値型が変わらない RunIf

```csharp
public sealed class CompositeStep<TOut>
{
    public CompositeStep<TOut> RunIf<TStep>(
        Func<TOut, bool> when)
        where TStep : IStep<TOut>, new();

    public CompositeStep<TOut> RunIf<TStep>(
        Func<TOut, StepInput, bool> when)
        where TStep : IStep<TOut>, new();

    public CompositeStep<TOut> RunIfAsync<TStep>(
        Func<TOut, bool> when)
        where TStep : IAsyncStep<TOut>, new();

    public CompositeStep<TOut> RunIfAsync<TStep>(
        Func<TOut, StepInput, bool> when)
        where TStep : IAsyncStep<TOut>, new();
}
```

戻り値型が変わらない版では、条件が false の場合、現在の `TOut` をそのまま次へ流す。

```csharp
.RunIf<NormalizeStep>(x => x.ShouldNormalize)
```

これは次と同じ意味である。

```csharp
.RunIf<NormalizeStep, TextResult>(
    when: x => x.ShouldNormalize,
    otherwise: x => x)
```

### 4.3 TapIf

```csharp
public sealed class CompositeStep<TOut>
{
    public CompositeStep<TOut> TapIf<TStep>(
        Func<TOut, bool> when)
        where TStep : IStep<Unit>, new();

    public CompositeStep<TOut> TapIf<TStep>(
        Func<TOut, StepInput, bool> when)
        where TStep : IStep<Unit>, new();

    public CompositeStep<TOut> TapIfAsync<TStep>(
        Func<TOut, bool> when)
        where TStep : IAsyncStep<Unit>, new();

    public CompositeStep<TOut> TapIfAsync<TStep>(
        Func<TOut, StepInput, bool> when)
        where TStep : IAsyncStep<Unit>, new();
}
```

`TapIf` は `Unit` を返す Step のみ許可する。  
戻り値は無視し、current value は変更しない。

### 4.4 If

```csharp
public sealed class CompositeStep<TOut>
{
    public CompositeStep<TNext> If<TNext>(
        string name,
        Func<TOut, bool> condition,
        Func<BranchBuilder<TOut>, BranchBuilder<TNext>> thenFlow,
        Func<BranchBuilder<TOut>, BranchBuilder<TNext>> elseFlow);

    public CompositeStep<TNext> If<TNext>(
        string name,
        Func<TOut, StepInput, bool> condition,
        Func<BranchBuilder<TOut>, BranchBuilder<TNext>> thenFlow,
        Func<BranchBuilder<TOut>, BranchBuilder<TNext>> elseFlow);
}
```

`thenFlow` と `elseFlow` は必ず同じ `TNext` に畳む。

### 4.5 Switch

```csharp
public sealed class CompositeStep<TOut>
{
    public CompositeStep<TNext> Switch<TCase, TNext>(
        string name,
        Func<TOut, TCase> selector,
        Func<SwitchCaseBuilder<TOut, TCase, TNext>, SwitchCaseBuilder<TOut, TCase, TNext>> cases);

    public CompositeStep<TNext> Switch<TCase, TNext>(
        string name,
        Func<TOut, StepInput, TCase> selector,
        Func<SwitchCaseBuilder<TOut, TCase, TNext>, SwitchCaseBuilder<TOut, TCase, TNext>> cases);
}
```

`Switch` は `Default` を必須にする。  
`Default` が未定義の場合は定義時に例外を投げる。

---

## 5. 利用例

この章では、実装者が API の意図を確認できるように、各 API の使用例を示す。

### 5.1 共通サンプル Step

以下の型を以後の例で使う。

```csharp
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using System.IO;

public sealed class MainConfig
{
    public LoadStep.Config Load { get; set; } = new();

    public ConvertStep.Config Convert { get; set; } = new();

    public MarkdownConvertStep.Config Markdown { get; set; } = new();

    public HtmlConvertStep.Config Html { get; set; } = new();

    public NotifyStep.Config Notify { get; set; } = new();

    public SaveStep.Config Save { get; set; } = new();
}

public enum ConvertMode
{
    None,
    Plain,
    Markdown,
    Html,
}

public sealed record LoadResult(
    string Text,
    ConvertMode Mode,
    bool ShouldConvert,
    bool ShouldNotify);

public sealed record ConvertInput(string Text);

public sealed record ConvertResult(string ConvertedText);

public sealed record SaveInput(string Content);

public sealed class LoadStep : IStep<LoadResult>
{
    public sealed class Config
    {
        public string Path { get; set; } = "";

        public ConvertMode Mode { get; set; }

        public bool ShouldConvert { get; set; }

        public bool ShouldNotify { get; set; }
    }

    public LoadResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        string text = File.ReadAllText(config.Path);

        return new LoadResult(
            Text: text,
            Mode: config.Mode,
            ShouldConvert: config.ShouldConvert,
            ShouldNotify: config.ShouldNotify);
    }
}

public sealed class ConvertStep : IStep<ConvertResult>
{
    public sealed class Config
    {
        public string Prefix { get; set; } = "";
    }

    public ConvertResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        ConvertInput convertInput = input.Get<ConvertInput>();

        return new ConvertResult(config.Prefix + convertInput.Text.ToUpperInvariant());
    }
}

public sealed class MarkdownConvertStep : IStep<ConvertResult>
{
    public sealed class Config
    {
        public string HeadingPrefix { get; set; } = "# ";
    }

    public ConvertResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        ConvertInput convertInput = input.Get<ConvertInput>();

        return new ConvertResult(config.HeadingPrefix + convertInput.Text);
    }
}

public sealed class HtmlConvertStep : IStep<ConvertResult>
{
    public sealed class Config
    {
        public string TagName { get; set; } = "p";
    }

    public ConvertResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        ConvertInput convertInput = input.Get<ConvertInput>();

        string tag = config.TagName;
        return new ConvertResult($"<{tag}>{convertInput.Text}</{tag}>");
    }
}

public sealed class NotifyStep : IStep<Unit>
{
    public sealed class Config
    {
        public string MessagePrefix { get; set; } = "notify:";
    }

    public Unit Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        LoadResult loadResult = input.Get<LoadResult>();

        Console.WriteLine($"{config.MessagePrefix} {loadResult.Mode}");

        return Unit.Value;
    }
}

public sealed class SaveStep : IStep<Unit>
{
    public sealed class Config
    {
        public string Path { get; set; } = "";
    }

    public Unit Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        SaveInput saveInput = input.Get<SaveInput>();

        File.WriteAllText(config.Path, saveInput.Content);

        return Unit.Value;
    }
}
```

### 5.2 Lambda Step の利用例

#### 5.2.1 変換だけを Lambda Step で書く

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
    .Run<ConvertResult>(
        "use-original-text",
        x => new ConvertResult(x.Text))
    .Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

期待動作:

```text
LoadStep が LoadResult を返す
Lambda Step "use-original-text" が ConvertResult を返す
SaveStep が ConvertResult から生成された SaveInput を保存する
```

#### 5.2.2 StepInput と Config を読む Lambda Step

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
    .Run<ConvertResult>(
        "use-config-prefix",
        (x, input) =>
        {
            ConvertStep.Config config = input.Context.Get<ConvertStep.Config>();
            return new ConvertResult(config.Prefix + x.Text);
        })
        .WithConfig<ConvertStep.Config>("Convert")
    .Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

この場合、`WithConfig<ConvertStep.Config>("Convert")` は Lambda Step 実行前に `StepContext` へ登録される。

#### 5.2.3 Entry の最初を Lambda Step にする

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadResult>(
        "create-load-result",
        input =>
        {
            LoadStep.Config config = input.Context.Get<LoadStep.Config>();

            return new LoadResult(
                Text: File.ReadAllText(config.Path),
                Mode: config.Mode,
                ShouldConvert: config.ShouldConvert,
                ShouldNotify: config.ShouldNotify);
        })
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
    .Run<ConvertResult>(
        "use-original-text",
        x => new ConvertResult(x.Text))
    .Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

### 5.3 RunIf の利用例

#### 5.3.1 条件 true なら Step 実行、false なら Lambda fallback

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
        .StoreAs()
        .Produce<ConvertInput>(x => new ConvertInput(x.Text))
    .RunIf<ConvertStep, ConvertResult>(
        when: x => x.ShouldConvert,
        otherwise: x => new ConvertResult(x.Text))
        .WithConfig<ConvertStep.Config>("Convert")
    .Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

期待動作:

```text
ShouldConvert == true:
    ConvertStep を実行する
    ConvertStep の戻り値 ConvertResult を current value にする

ShouldConvert == false:
    ConvertStep は実行しない
    otherwise lambda の戻り値 ConvertResult を current value にする
```

#### 5.3.2 false 時の fallback が Config を読む

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
        .Produce<ConvertInput>(x => new ConvertInput(x.Text))
    .RunIf<ConvertStep, ConvertResult>(
        when: (x, input) => x.ShouldConvert,
        otherwise: (x, input) =>
        {
            ConvertStep.Config config = input.Context.Get<ConvertStep.Config>();
            return new ConvertResult(config.Prefix + x.Text);
        })
        .WithConfig<ConvertStep.Config>("Convert")
    .Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

実装上の必須動作:

```text
RunIf node の WithConfig は、condition 評価前に StepContext へ登録する。
```

そうしないと、`otherwise` が Config を読めない。

#### 5.3.3 戻り値型が変わらない RunIf

```csharp
public sealed class TrimStep : IStep<LoadResult>
{
    public LoadResult Execute(StepInput input)
    {
        LoadResult loadResult = input.Get<LoadResult>();

        return loadResult with
        {
            Text = loadResult.Text.Trim(),
        };
    }
}

var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
        .StoreAs()
    .RunIf<TrimStep>(x => x.Text.Length > 0)
        .StoreAs()
    .Run<ConvertResult>(
        "to-convert-result",
        x => new ConvertResult(x.Text))
    .Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

`RunIf<TrimStep>(...)` の false 時は、現在の `LoadResult` がそのまま流れる。

### 5.4 TapIf の利用例

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
        .StoreAs()
    .TapIf<NotifyStep>(x => x.ShouldNotify)
        .WithConfig<NotifyStep.Config>("Notify")
    .Run<ConvertResult>(
        "use-original-text",
        x => new ConvertResult(x.Text))
    .Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

期待動作:

```text
ShouldNotify == true:
    NotifyStep を実行する
    NotifyStep の戻り値 Unit は捨てる
    current value は LoadResult のまま

ShouldNotify == false:
    NotifyStep は実行しない
    current value は LoadResult のまま
```

`TapIf` の後の Lambda Step では、`x` は `LoadResult` である。

```csharp
.Run<ConvertResult>(
    "use-original-text",
    x => new ConvertResult(x.Text))
```

### 5.5 If の利用例

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
        .StoreAs()
        .Produce<ConvertInput>(x => new ConvertInput(x.Text))
    .If<ConvertResult>(
        name: "convert-if-required",
        condition: x => x.ShouldConvert,
        thenFlow: b => b
            .Run<ConvertStep, ConvertResult>()
                .WithConfig<ConvertStep.Config>("Convert"),
        elseFlow: b => b
            .Run<ConvertResult>(
                "skip-convert",
                x => new ConvertResult(x.Text)))
    .Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

期待動作:

```text
ShouldConvert == true:
    thenFlow を実行する
    ConvertStep の戻り値 ConvertResult を current value にする

ShouldConvert == false:
    elseFlow を実行する
    Lambda Step "skip-convert" の戻り値 ConvertResult を current value にする
```

`If` 後の current value は常に `ConvertResult` である。

### 5.6 Switch の利用例

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
        .StoreAs()
        .Produce<ConvertInput>(x => new ConvertInput(x.Text))
    .Switch<ConvertMode, ConvertResult>(
        name: "convert-by-mode",
        selector: x => x.Mode,
        cases: c => c
            .Case(ConvertMode.Markdown, b => b
                .Run<MarkdownConvertStep, ConvertResult>()
                    .WithConfig<MarkdownConvertStep.Config>("Markdown"))
            .Case(ConvertMode.Html, b => b
                .Run<HtmlConvertStep, ConvertResult>()
                    .WithConfig<HtmlConvertStep.Config>("Html"))
            .Case(ConvertMode.None, b => b
                .Run<ConvertResult>(
                    "skip-convert",
                    x => new ConvertResult(x.Text)))
            .Default(b => b
                .Run<ConvertResult>(
                    "plain-convert",
                    x => new ConvertResult(x.Text))))
    .TapIf<NotifyStep>(x => x.ConvertedText.Length > 0)
        .WithConfig<NotifyStep.Config>("Notify")
    .Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

注意点:

```text
Switch のすべての Case と Default は ConvertResult を返す。
Default は必須。
Case の重複は定義時に例外。
```

---

## 6. 内部設計

### 6.1 StepRegistration 列から ExecutionNode 列へ変更する

現在の `CompositeStep<TOut>` は `IReadOnlyList<StepRegistration>` を持つ。  
条件分岐や Lambda Step を扱うため、内部実行単位を `IExecutionNode` に置き換える。

```csharp
internal enum ExecutionNodeKind
{
    Step,
    Lambda,
    RunIf,
    TapIf,
    If,
    Switch,
}
```

```csharp
internal interface IExecutionNode
{
    int NodeIndex { get; }

    string Name { get; }

    Type OutputType { get; }

    Type? PrimaryStepType { get; }

    ExecutionNodeKind Kind { get; }

    IReadOnlyList<StepValueProducer> Producers { get; }

    IExecutionNode AddProducer(StepValueProducer producer);

    IExecutionNode ClearProducers();

    Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        object? currentValue,
        CancellationToken cancellationToken);
}
```

`PrimaryStepType` の意味:

| Node | PrimaryStepType |
|---|---|
| 通常 Step | `typeof(TStep)` |
| Lambda Step | `null` |
| RunIf | `typeof(TStep)` |
| TapIf | `typeof(TStep)` |
| If | `null` |
| Switch | `null` |

`WithConfig<TConfig>(sectionPath)` は、`PrimaryStepType` が null の node にも付けられるようにする。  
Lambda Step に Config を付けたいケースがあるためである。

### 6.2 NodeExecutionResult

```csharp
internal enum NodeExecutionOutcome
{
    Succeeded,
    Skipped,
}
```

```csharp
internal sealed record NodeExecutionResult(
    object? Value,
    NodeExecutionOutcome Outcome,
    string? Detail = null)
{
    public static NodeExecutionResult Succeeded(object? value)
    {
        return new NodeExecutionResult(value, NodeExecutionOutcome.Succeeded);
    }

    public static NodeExecutionResult Skipped(object? value, string detail)
    {
        return new NodeExecutionResult(value, NodeExecutionOutcome.Skipped, detail);
    }
}
```

`Skipped` は workflow 全体の失敗ではない。  
`RunIf` false や `TapIf` false の trace 用状態である。

### 6.3 NodeExecutionContext

`If` / `Switch` が内部 branch を実行するため、node 実行時に branch executor を渡す。

```csharp
internal sealed class NodeExecutionContext
{
    public NodeExecutionContext(
        string entryName,
        StepInput input,
        WorkflowExecutionOptions options,
        WorkflowNodeRunner runner,
        List<ExecutionTraceStep> traceSteps,
        ILogger engineLogger)
    {
        EntryName = entryName;
        Input = input;
        Options = options;
        Runner = runner;
        TraceSteps = traceSteps;
        EngineLogger = engineLogger;
    }

    public string EntryName { get; }

    public StepInput Input { get; }

    public WorkflowExecutionOptions Options { get; }

    public WorkflowNodeRunner Runner { get; }

    public List<ExecutionTraceStep> TraceSteps { get; }

    public ILogger EngineLogger { get; }
}
```

### 6.4 CompositeStep<TOut> のフィールド変更

変更前:

```csharp
private readonly IReadOnlyList<StepRegistration> steps;
```

変更後:

```csharp
private readonly IReadOnlyList<IExecutionNode> nodes;

private readonly int nextNodeIndex;
```

コンストラクタ例:

```csharp
internal CompositeStep(
    string name,
    string? namespaceName,
    string qualifiedName,
    IReadOnlyList<IExecutionNode> nodes,
    int nextNodeIndex,
    Type? configType = null,
    IReadOnlyList<StepConfigRegistration>? stepConfigRegistrations = null)
{
    Name = name;
    NamespaceName = namespaceName;
    QualifiedName = qualifiedName;
    this.nodes = nodes.ToArray();
    this.nextNodeIndex = nextNodeIndex;
    ConfigType = configType;
    StepConfigRegistrations = stepConfigRegistrations?.ToArray() ?? [];
}
```

`nextNodeIndex` は、通常 node と branch 内 node を含めたグローバル連番である。

---

## 7. Node 実装例

### 7.1 通常 Step node

既存の `StepRegistration` をラップすることで、実装範囲を小さくできる。

```csharp
internal sealed class StepExecutionNode<TOut> : IExecutionNode
{
    private readonly StepRegistration registration;

    public StepExecutionNode(int nodeIndex, StepRegistration registration)
    {
        this.registration = registration;
        NodeIndex = nodeIndex;
    }

    public int NodeIndex { get; }

    public string Name => registration.Name;

    public Type OutputType => typeof(TOut);

    public Type? PrimaryStepType => registration.StepType;

    public ExecutionNodeKind Kind => ExecutionNodeKind.Step;

    public IReadOnlyList<StepValueProducer> Producers => registration.Producers;

    public IExecutionNode AddProducer(StepValueProducer producer)
    {
        return new StepExecutionNode<TOut>(
            NodeIndex,
            registration.AddProducer(producer));
    }

    public IExecutionNode ClearProducers()
    {
        return new StepExecutionNode<TOut>(
            NodeIndex,
            registration.ClearProducers());
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        object? currentValue,
        CancellationToken cancellationToken)
    {
        object? value = await registration.ExecuteAsync(
                context.Input,
                cancellationToken)
            .ConfigureAwait(false);

        return NodeExecutionResult.Succeeded(value);
    }

    public IReadOnlyList<ExecutionTraceValue> Produce(StepInput input, object? value)
    {
        return registration.Produce(input, value);
    }
}
```

実際には `IExecutionNode` に `Produce` を入れる方が扱いやすい。

```csharp
internal interface IExecutionNode
{
    int NodeIndex { get; }

    string Name { get; }

    Type OutputType { get; }

    Type? PrimaryStepType { get; }

    ExecutionNodeKind Kind { get; }

    IReadOnlyList<StepValueProducer> Producers { get; }

    IExecutionNode AddProducer(StepValueProducer producer);

    IExecutionNode ClearProducers();

    Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        object? currentValue,
        CancellationToken cancellationToken);

    IReadOnlyList<ExecutionTraceValue> Produce(
        StepInput input,
        object? value);
}
```

### 7.2 Lambda Step node

```csharp
internal sealed class LambdaExecutionNode<TIn, TOut> : IExecutionNode
{
    private readonly Func<TIn, StepInput, CancellationToken, Task<TOut>> body;
    private readonly IReadOnlyList<StepValueProducer> producers;

    public LambdaExecutionNode(
        int nodeIndex,
        string name,
        Func<TIn, StepInput, CancellationToken, Task<TOut>> body,
        IReadOnlyList<StepValueProducer>? producers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(body);

        NodeIndex = nodeIndex;
        Name = name;
        this.body = body;
        this.producers = producers?.ToArray() ?? [];
    }

    public int NodeIndex { get; }

    public string Name { get; }

    public Type OutputType => typeof(TOut);

    public Type? PrimaryStepType => null;

    public ExecutionNodeKind Kind => ExecutionNodeKind.Lambda;

    public IReadOnlyList<StepValueProducer> Producers => producers;

    public IExecutionNode AddProducer(StepValueProducer producer)
    {
        ArgumentNullException.ThrowIfNull(producer);

        return new LambdaExecutionNode<TIn, TOut>(
            NodeIndex,
            Name,
            body,
            producers.Append(producer).ToArray());
    }

    public IExecutionNode ClearProducers()
    {
        return new LambdaExecutionNode<TIn, TOut>(
            NodeIndex,
            Name,
            body,
            []);
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        object? currentValue,
        CancellationToken cancellationToken)
    {
        TIn typedCurrentValue = CastCurrentValue<TIn>(currentValue, Name);

        TOut value = await body(
                typedCurrentValue,
                context.Input,
                cancellationToken)
            .ConfigureAwait(false);

        return NodeExecutionResult.Succeeded(value);
    }

    public IReadOnlyList<ExecutionTraceValue> Produce(
        StepInput input,
        object? value)
    {
        return StepValueProducer.ProduceAll(producers, input, value);
    }

    private static T CastCurrentValue<T>(object? currentValue, string nodeName)
    {
        if (currentValue is T typed)
        {
            return typed;
        }

        if (currentValue is null && default(T) is null)
        {
            return default!;
        }

        throw new InvalidOperationException(
            $"Node '{nodeName}' expected current value of type '{typeof(T).FullName}', " +
            $"but actual value was '{currentValue?.GetType().FullName ?? "<null>"}'.");
    }
}
```

`StepValueProducer.ProduceAll` が存在しない場合は追加する。

```csharp
internal static class StepValueProducerExtensions
{
    public static IReadOnlyList<ExecutionTraceValue> ProduceAll(
        IReadOnlyList<StepValueProducer> producers,
        StepInput input,
        object? value)
    {
        var traceValues = new List<ExecutionTraceValue>();

        foreach (StepValueProducer producer in producers)
        {
            ExecutionTraceValue? traceValue = producer.Produce(input, value);
            if (traceValue is not null)
            {
                traceValues.Add(traceValue);
            }
        }

        return traceValues;
    }
}
```

既存 `StepRegistration.Produce` に同等処理がある場合は、それを共通化する。

### 7.3 RunIf node

```csharp
internal sealed class RunIfExecutionNode<TIn, TStep, TOut> : IExecutionNode
    where TStep : IStep<TOut>, new()
{
    private readonly StepRegistration stepRegistration;
    private readonly Func<TIn, StepInput, bool> when;
    private readonly Func<TIn, StepInput, CancellationToken, Task<TOut>> otherwise;
    private readonly IReadOnlyList<StepValueProducer> producers;

    public RunIfExecutionNode(
        int nodeIndex,
        StepRegistration stepRegistration,
        Func<TIn, StepInput, bool> when,
        Func<TIn, StepInput, CancellationToken, Task<TOut>> otherwise,
        IReadOnlyList<StepValueProducer>? producers = null)
    {
        ArgumentNullException.ThrowIfNull(stepRegistration);
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(otherwise);

        NodeIndex = nodeIndex;
        this.stepRegistration = stepRegistration;
        this.when = when;
        this.otherwise = otherwise;
        this.producers = producers?.ToArray() ?? [];
    }

    public int NodeIndex { get; }

    public string Name => stepRegistration.Name;

    public Type OutputType => typeof(TOut);

    public Type? PrimaryStepType => typeof(TStep);

    public ExecutionNodeKind Kind => ExecutionNodeKind.RunIf;

    public IReadOnlyList<StepValueProducer> Producers => producers;

    public IExecutionNode AddProducer(StepValueProducer producer)
    {
        return new RunIfExecutionNode<TIn, TStep, TOut>(
            NodeIndex,
            stepRegistration,
            when,
            otherwise,
            producers.Append(producer).ToArray());
    }

    public IExecutionNode ClearProducers()
    {
        return new RunIfExecutionNode<TIn, TStep, TOut>(
            NodeIndex,
            stepRegistration,
            when,
            otherwise,
            []);
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        object? currentValue,
        CancellationToken cancellationToken)
    {
        TIn typedCurrentValue = CastCurrentValue<TIn>(currentValue, Name);

        bool shouldRun = when(typedCurrentValue, context.Input);

        if (shouldRun)
        {
            object? value = await stepRegistration.ExecuteAsync(
                    context.Input,
                    cancellationToken)
                .ConfigureAwait(false);

            return NodeExecutionResult.Succeeded(value);
        }

        TOut fallbackValue = await otherwise(
                typedCurrentValue,
                context.Input,
                cancellationToken)
            .ConfigureAwait(false);

        return NodeExecutionResult.Skipped(
            fallbackValue,
            "condition evaluated to false; fallback value was used");
    }

    public IReadOnlyList<ExecutionTraceValue> Produce(
        StepInput input,
        object? value)
    {
        return StepValueProducerExtensions.ProduceAll(producers, input, value);
    }

    private static T CastCurrentValue<T>(object? currentValue, string nodeName)
    {
        if (currentValue is T typed)
        {
            return typed;
        }

        if (currentValue is null && default(T) is null)
        {
            return default!;
        }

        throw new InvalidOperationException(
            $"Node '{nodeName}' expected current value of type '{typeof(T).FullName}', " +
            $"but actual value was '{currentValue?.GetType().FullName ?? "<null>"}'.");
    }
}
```

非同期 Step 用は `TStep : IAsyncStep<TOut>, new()` の node を別に用意する。

```csharp
internal sealed class RunIfAsyncExecutionNode<TIn, TStep, TOut> : IExecutionNode
    where TStep : IAsyncStep<TOut>, new()
{
    // RunIfExecutionNode と同じ構造。
    // stepRegistration は StepRegistration.CreateAsync<TStep, TOut>() で作る。
}
```

### 7.4 TapIf node

```csharp
internal sealed class TapIfExecutionNode<TIn, TStep> : IExecutionNode
    where TStep : IStep<Unit>, new()
{
    private readonly StepRegistration stepRegistration;
    private readonly Func<TIn, StepInput, bool> when;
    private readonly IReadOnlyList<StepValueProducer> producers;

    public TapIfExecutionNode(
        int nodeIndex,
        StepRegistration stepRegistration,
        Func<TIn, StepInput, bool> when,
        IReadOnlyList<StepValueProducer>? producers = null)
    {
        ArgumentNullException.ThrowIfNull(stepRegistration);
        ArgumentNullException.ThrowIfNull(when);

        NodeIndex = nodeIndex;
        this.stepRegistration = stepRegistration;
        this.when = when;
        this.producers = producers?.ToArray() ?? [];
    }

    public int NodeIndex { get; }

    public string Name => stepRegistration.Name;

    public Type OutputType => typeof(TIn);

    public Type? PrimaryStepType => typeof(TStep);

    public ExecutionNodeKind Kind => ExecutionNodeKind.TapIf;

    public IReadOnlyList<StepValueProducer> Producers => producers;

    public IExecutionNode AddProducer(StepValueProducer producer)
    {
        return new TapIfExecutionNode<TIn, TStep>(
            NodeIndex,
            stepRegistration,
            when,
            producers.Append(producer).ToArray());
    }

    public IExecutionNode ClearProducers()
    {
        return new TapIfExecutionNode<TIn, TStep>(
            NodeIndex,
            stepRegistration,
            when,
            []);
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        object? currentValue,
        CancellationToken cancellationToken)
    {
        TIn typedCurrentValue = CastCurrentValue<TIn>(currentValue, Name);

        bool shouldRun = when(typedCurrentValue, context.Input);

        if (shouldRun)
        {
            await stepRegistration.ExecuteAsync(
                    context.Input,
                    cancellationToken)
                .ConfigureAwait(false);

            return NodeExecutionResult.Succeeded(typedCurrentValue);
        }

        return NodeExecutionResult.Skipped(
            typedCurrentValue,
            "condition evaluated to false; tap step was not executed");
    }

    public IReadOnlyList<ExecutionTraceValue> Produce(
        StepInput input,
        object? value)
    {
        return StepValueProducerExtensions.ProduceAll(producers, input, value);
    }

    private static T CastCurrentValue<T>(object? currentValue, string nodeName)
    {
        if (currentValue is T typed)
        {
            return typed;
        }

        if (currentValue is null && default(T) is null)
        {
            return default!;
        }

        throw new InvalidOperationException(
            $"Node '{nodeName}' expected current value of type '{typeof(T).FullName}', " +
            $"but actual value was '{currentValue?.GetType().FullName ?? "<null>"}'.");
    }
}
```

`TapIf` は `OutputType => typeof(TIn)` とする。  
これにより、Tap 後の `.Produce(...)` や `.Run(...)` は元の current value 型を受け取る。

### 7.5 If node

```csharp
internal sealed class IfExecutionNode<TIn, TOut> : IExecutionNode
{
    private readonly string name;
    private readonly Func<TIn, StepInput, bool> condition;
    private readonly IReadOnlyList<IExecutionNode> thenNodes;
    private readonly IReadOnlyList<IExecutionNode> elseNodes;
    private readonly IReadOnlyList<StepValueProducer> producers;

    public IfExecutionNode(
        int nodeIndex,
        string name,
        Func<TIn, StepInput, bool> condition,
        IReadOnlyList<IExecutionNode> thenNodes,
        IReadOnlyList<IExecutionNode> elseNodes,
        IReadOnlyList<StepValueProducer>? producers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(thenNodes);
        ArgumentNullException.ThrowIfNull(elseNodes);

        NodeIndex = nodeIndex;
        this.name = name;
        this.condition = condition;
        this.thenNodes = thenNodes.ToArray();
        this.elseNodes = elseNodes.ToArray();
        this.producers = producers?.ToArray() ?? [];
    }

    public int NodeIndex { get; }

    public string Name => name;

    public Type OutputType => typeof(TOut);

    public Type? PrimaryStepType => null;

    public ExecutionNodeKind Kind => ExecutionNodeKind.If;

    public IReadOnlyList<StepValueProducer> Producers => producers;

    public IExecutionNode AddProducer(StepValueProducer producer)
    {
        return new IfExecutionNode<TIn, TOut>(
            NodeIndex,
            name,
            condition,
            thenNodes,
            elseNodes,
            producers.Append(producer).ToArray());
    }

    public IExecutionNode ClearProducers()
    {
        return new IfExecutionNode<TIn, TOut>(
            NodeIndex,
            name,
            condition,
            thenNodes,
            elseNodes,
            []);
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        object? currentValue,
        CancellationToken cancellationToken)
    {
        TIn typedCurrentValue = CastCurrentValue<TIn>(currentValue, Name);

        bool result = condition(typedCurrentValue, context.Input);

        IReadOnlyList<IExecutionNode> selectedNodes = result ? thenNodes : elseNodes;
        string selectedBranchName = result ? "then" : "else";

        object? branchValue = await context.Runner.ExecuteBranchAsync(
                selectedNodes,
                typedCurrentValue,
                context,
                selectedBranchName,
                cancellationToken)
            .ConfigureAwait(false);

        return NodeExecutionResult.Succeeded(branchValue);
    }

    public IReadOnlyList<ExecutionTraceValue> Produce(
        StepInput input,
        object? value)
    {
        return StepValueProducerExtensions.ProduceAll(producers, input, value);
    }

    private static T CastCurrentValue<T>(object? currentValue, string nodeName)
    {
        if (currentValue is T typed)
        {
            return typed;
        }

        if (currentValue is null && default(T) is null)
        {
            return default!;
        }

        throw new InvalidOperationException(
            $"Node '{nodeName}' expected current value of type '{typeof(T).FullName}', " +
            $"but actual value was '{currentValue?.GetType().FullName ?? "<null>"}'.");
    }
}
```

### 7.6 Switch node

```csharp
internal sealed class SwitchExecutionNode<TIn, TCase, TOut> : IExecutionNode
{
    private readonly string name;
    private readonly Func<TIn, StepInput, TCase> selector;
    private readonly IReadOnlyList<SwitchCaseDefinition<TCase>> cases;
    private readonly SwitchCaseDefinition<TCase> defaultCase;
    private readonly IEqualityComparer<TCase> comparer;
    private readonly IReadOnlyList<StepValueProducer> producers;

    public SwitchExecutionNode(
        int nodeIndex,
        string name,
        Func<TIn, StepInput, TCase> selector,
        IReadOnlyList<SwitchCaseDefinition<TCase>> cases,
        SwitchCaseDefinition<TCase> defaultCase,
        IEqualityComparer<TCase>? comparer = null,
        IReadOnlyList<StepValueProducer>? producers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(defaultCase);

        NodeIndex = nodeIndex;
        this.name = name;
        this.selector = selector;
        this.cases = cases.ToArray();
        this.defaultCase = defaultCase;
        this.comparer = comparer ?? EqualityComparer<TCase>.Default;
        this.producers = producers?.ToArray() ?? [];
    }

    public int NodeIndex { get; }

    public string Name => name;

    public Type OutputType => typeof(TOut);

    public Type? PrimaryStepType => null;

    public ExecutionNodeKind Kind => ExecutionNodeKind.Switch;

    public IReadOnlyList<StepValueProducer> Producers => producers;

    public IExecutionNode AddProducer(StepValueProducer producer)
    {
        return new SwitchExecutionNode<TIn, TCase, TOut>(
            NodeIndex,
            name,
            selector,
            cases,
            defaultCase,
            comparer,
            producers.Append(producer).ToArray());
    }

    public IExecutionNode ClearProducers()
    {
        return new SwitchExecutionNode<TIn, TCase, TOut>(
            NodeIndex,
            name,
            selector,
            cases,
            defaultCase,
            comparer,
            []);
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        object? currentValue,
        CancellationToken cancellationToken)
    {
        TIn typedCurrentValue = CastCurrentValue<TIn>(currentValue, Name);

        TCase selectedCaseValue = selector(typedCurrentValue, context.Input);

        SwitchCaseDefinition<TCase>? selectedCase = null;

        foreach (SwitchCaseDefinition<TCase> candidate in cases)
        {
            if (comparer.Equals(candidate.Value, selectedCaseValue))
            {
                selectedCase = candidate;
                break;
            }
        }

        selectedCase ??= defaultCase;

        object? branchValue = await context.Runner.ExecuteBranchAsync(
                selectedCase.Nodes,
                typedCurrentValue,
                context,
                selectedCase.Name,
                cancellationToken)
            .ConfigureAwait(false);

        return NodeExecutionResult.Succeeded(branchValue);
    }

    public IReadOnlyList<ExecutionTraceValue> Produce(
        StepInput input,
        object? value)
    {
        return StepValueProducerExtensions.ProduceAll(producers, input, value);
    }

    private static T CastCurrentValue<T>(object? currentValue, string nodeName)
    {
        if (currentValue is T typed)
        {
            return typed;
        }

        if (currentValue is null && default(T) is null)
        {
            return default!;
        }

        throw new InvalidOperationException(
            $"Node '{nodeName}' expected current value of type '{typeof(T).FullName}', " +
            $"but actual value was '{currentValue?.GetType().FullName ?? "<null>"}'.");
    }
}
```

`SwitchCaseDefinition<TCase>`:

```csharp
internal sealed record SwitchCaseDefinition<TCase>(
    string Name,
    TCase Value,
    IReadOnlyList<IExecutionNode> Nodes);
```

default 用には value が不要なので、別型にしてもよい。

```csharp
internal sealed record SwitchDefaultDefinition(
    string Name,
    IReadOnlyList<IExecutionNode> Nodes);
```

実装を単純にしたい場合は `SwitchCaseDefinition<TCase>` に `bool IsDefault` を持たせる。

---

## 8. BranchBuilder

### 8.1 目的

`If` / `Switch` 内で、現在の `TOut` から始まる部分チェーンを書くために使う。

```csharp
.If<ConvertResult>(
    name: "convert-if-required",
    condition: x => x.ShouldConvert,
    thenFlow: b => b
        .Run<ConvertStep, ConvertResult>()
            .WithConfig<ConvertStep.Config>("Convert"),
    elseFlow: b => b
        .Run<ConvertResult>(
            "skip-convert",
            x => new ConvertResult(x.Text)))
```

ここで `b` は `BranchBuilder<LoadResult>` である。  
`thenFlow` も `elseFlow` も `BranchBuilder<ConvertResult>` を返す。

### 8.2 クラス定義

```csharp
public sealed class BranchBuilder<TOut>
{
    private readonly string entryName;
    private readonly IReadOnlyList<IExecutionNode> nodes;
    private readonly int nextNodeIndex;
    private readonly IReadOnlyList<StepConfigRegistration> stepConfigRegistrations;

    internal BranchBuilder(
        string entryName,
        IReadOnlyList<IExecutionNode> nodes,
        int nextNodeIndex,
        IReadOnlyList<StepConfigRegistration> stepConfigRegistrations)
    {
        this.entryName = entryName;
        this.nodes = nodes.ToArray();
        this.nextNodeIndex = nextNodeIndex;
        this.stepConfigRegistrations = stepConfigRegistrations.ToArray();
    }

    internal IReadOnlyList<IExecutionNode> Nodes => nodes;

    internal int NextNodeIndex => nextNodeIndex;

    internal IReadOnlyList<StepConfigRegistration> StepConfigRegistrations => stepConfigRegistrations;

    public BranchBuilder<TNext> Run<TStep, TNext>()
        where TStep : IStep<TNext>, new()
    {
        int nodeIndex = nextNodeIndex;

        IExecutionNode node = new StepExecutionNode<TNext>(
            nodeIndex,
            StepRegistration.Create<TStep, TNext>());

        return new BranchBuilder<TNext>(
            entryName,
            Append(node),
            nodeIndex + 1,
            stepConfigRegistrations);
    }

    public BranchBuilder<TNext> RunAsync<TStep, TNext>()
        where TStep : IAsyncStep<TNext>, new()
    {
        int nodeIndex = nextNodeIndex;

        IExecutionNode node = new StepExecutionNode<TNext>(
            nodeIndex,
            StepRegistration.CreateAsync<TStep, TNext>());

        return new BranchBuilder<TNext>(
            entryName,
            Append(node),
            nodeIndex + 1,
            stepConfigRegistrations);
    }

    public BranchBuilder<TNext> Run<TNext>(
        string name,
        Func<TOut, TNext> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Run<TNext>(
            name,
            (current, input) => body(current));
    }

    public BranchBuilder<TNext> Run<TNext>(
        string name,
        Func<TOut, StepInput, TNext> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        int nodeIndex = nextNodeIndex;

        IExecutionNode node = new LambdaExecutionNode<TOut, TNext>(
            nodeIndex,
            name,
            (current, input, cancellationToken) =>
                Task.FromResult(body(current, input)));

        return new BranchBuilder<TNext>(
            entryName,
            Append(node),
            nodeIndex + 1,
            stepConfigRegistrations);
    }

    public BranchBuilder<TNext> RunAsync<TNext>(
        string name,
        Func<TOut, StepInput, CancellationToken, Task<TNext>> body)
    {
        int nodeIndex = nextNodeIndex;

        IExecutionNode node = new LambdaExecutionNode<TOut, TNext>(
            nodeIndex,
            name,
            body);

        return new BranchBuilder<TNext>(
            entryName,
            Append(node),
            nodeIndex + 1,
            stepConfigRegistrations);
    }

    public BranchBuilder<TOut> TapIf<TStep>(
        Func<TOut, bool> when)
        where TStep : IStep<Unit>, new()
    {
        ArgumentNullException.ThrowIfNull(when);

        return TapIf<TStep>((current, input) => when(current));
    }

    public BranchBuilder<TOut> TapIf<TStep>(
        Func<TOut, StepInput, bool> when)
        where TStep : IStep<Unit>, new()
    {
        int nodeIndex = nextNodeIndex;

        IExecutionNode node = new TapIfExecutionNode<TOut, TStep>(
            nodeIndex,
            StepRegistration.Create<TStep, Unit>(),
            when);

        return new BranchBuilder<TOut>(
            entryName,
            Append(node),
            nodeIndex + 1,
            stepConfigRegistrations);
    }

    public BranchBuilder<TOut> WithConfig<TConfig>(string sectionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);

        IExecutionNode currentNode = CurrentNode;

        StepConfigRegistration registration = new StepConfigRegistration(
            currentNode.PrimaryStepType,
            sectionPath,
            typeof(TConfig),
            currentNode.NodeIndex,
            null);

        return new BranchBuilder<TOut>(
            entryName,
            nodes,
            nextNodeIndex,
            stepConfigRegistrations.Append(registration).ToArray());
    }

    public BranchBuilder<TOut> WithConfig<TConfig>(
        string sectionPath,
        string defaultConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultConfigPath);

        IExecutionNode currentNode = CurrentNode;

        StepConfigRegistration registration = new StepConfigRegistration(
            currentNode.PrimaryStepType,
            sectionPath,
            typeof(TConfig),
            currentNode.NodeIndex,
            defaultConfigPath);

        return new BranchBuilder<TOut>(
            entryName,
            nodes,
            nextNodeIndex,
            stepConfigRegistrations.Append(registration).ToArray());
    }

    public BranchBuilder<TOut> Produce<TValue>(
        Func<TOut, TValue> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return AddProducer(
            StepValueProducer.Create(selector, null, ExecutionTraceValueSource.Produce, null));
    }

    public BranchBuilder<TOut> Produce<TValue>(
        string name,
        Func<TOut, TValue> selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(selector);

        return AddProducer(
            StepValueProducer.Create(selector, name, ExecutionTraceValueSource.Produce, null));
    }

    public BranchBuilder<TOut> StoreAs()
    {
        return AddProducer(
            StepValueProducer.Create<TOut>(
                value => value,
                null,
                ExecutionTraceValueSource.StoreAs,
                null));
    }

    public BranchBuilder<TOut> Discard()
    {
        IExecutionNode[] nextNodes = nodes.ToArray();
        nextNodes[^1] = CurrentNode.ClearProducers();

        return new BranchBuilder<TOut>(
            entryName,
            nextNodes,
            nextNodeIndex,
            stepConfigRegistrations);
    }

    private BranchBuilder<TOut> AddProducer(StepValueProducer producer)
    {
        IExecutionNode[] nextNodes = nodes.ToArray();
        nextNodes[^1] = CurrentNode.AddProducer(producer);

        return new BranchBuilder<TOut>(
            entryName,
            nextNodes,
            nextNodeIndex,
            stepConfigRegistrations);
    }

    private IExecutionNode CurrentNode
    {
        get
        {
            if (nodes.Count == 0)
            {
                throw new InvalidOperationException("No node is registered in the branch.");
            }

            return nodes[^1];
        }
    }

    private IReadOnlyList<IExecutionNode> Append(IExecutionNode node)
    {
        IExecutionNode[] nextNodes = new IExecutionNode[nodes.Count + 1];

        for (int i = 0; i < nodes.Count; i++)
        {
            nextNodes[i] = nodes[i];
        }

        nextNodes[^1] = node;

        return nextNodes;
    }
}
```

上記は基本形である。  
実際には `RunIf`、`RunIfAsync`、`TapIfAsync`、`If`、`Switch` も `BranchBuilder<TOut>` に追加する。

---

## 9. SwitchCaseBuilder

### 9.1 使用形

```csharp
.Switch<ConvertMode, ConvertResult>(
    name: "convert-by-mode",
    selector: x => x.Mode,
    cases: c => c
        .Case(ConvertMode.Markdown, b => b
            .Run<MarkdownConvertStep, ConvertResult>())
        .Case(ConvertMode.Html, b => b
            .Run<HtmlConvertStep, ConvertResult>())
        .Default(b => b
            .Run<ConvertResult>("plain", x => new ConvertResult(x.Text))))
```

### 9.2 クラス定義

```csharp
public sealed class SwitchCaseBuilder<TIn, TCase, TOut>
{
    private readonly string entryName;
    private readonly int nextNodeIndex;
    private readonly IReadOnlyList<SwitchCaseBuildResult<TCase>> cases;
    private readonly SwitchCaseBuildResult<TCase>? defaultCase;
    private readonly IReadOnlyList<StepConfigRegistration> stepConfigRegistrations;
    private readonly IEqualityComparer<TCase> comparer;

    internal SwitchCaseBuilder(
        string entryName,
        int nextNodeIndex,
        IReadOnlyList<SwitchCaseBuildResult<TCase>> cases,
        SwitchCaseBuildResult<TCase>? defaultCase,
        IReadOnlyList<StepConfigRegistration> stepConfigRegistrations,
        IEqualityComparer<TCase>? comparer = null)
    {
        this.entryName = entryName;
        this.nextNodeIndex = nextNodeIndex;
        this.cases = cases.ToArray();
        this.defaultCase = defaultCase;
        this.stepConfigRegistrations = stepConfigRegistrations.ToArray();
        this.comparer = comparer ?? EqualityComparer<TCase>.Default;
    }

    internal int NextNodeIndex => nextNodeIndex;

    internal IReadOnlyList<SwitchCaseBuildResult<TCase>> Cases => cases;

    internal SwitchCaseBuildResult<TCase>? DefaultCase => defaultCase;

    internal IReadOnlyList<StepConfigRegistration> StepConfigRegistrations => stepConfigRegistrations;

    public SwitchCaseBuilder<TIn, TCase, TOut> Case(
        TCase value,
        Func<BranchBuilder<TIn>, BranchBuilder<TOut>> branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        foreach (SwitchCaseBuildResult<TCase> existing in cases)
        {
            if (comparer.Equals(existing.Value, value))
            {
                throw new InvalidOperationException(
                    $"Switch case '{value}' is already registered.");
            }
        }

        var start = new BranchBuilder<TIn>(
            entryName,
            [],
            nextNodeIndex,
            []);

        BranchBuilder<TOut> built = branch(start);

        var result = new SwitchCaseBuildResult<TCase>(
            Name: $"case:{value}",
            Value: value,
            Nodes: built.Nodes);

        return new SwitchCaseBuilder<TIn, TCase, TOut>(
            entryName,
            built.NextNodeIndex,
            cases.Append(result).ToArray(),
            defaultCase,
            stepConfigRegistrations.Concat(built.StepConfigRegistrations).ToArray(),
            comparer);
    }

    public SwitchCaseBuilder<TIn, TCase, TOut> Default(
        Func<BranchBuilder<TIn>, BranchBuilder<TOut>> branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        if (defaultCase is not null)
        {
            throw new InvalidOperationException("Default case is already registered.");
        }

        var start = new BranchBuilder<TIn>(
            entryName,
            [],
            nextNodeIndex,
            []);

        BranchBuilder<TOut> built = branch(start);

        var result = new SwitchCaseBuildResult<TCase>(
            Name: "default",
            Value: default!,
            Nodes: built.Nodes);

        return new SwitchCaseBuilder<TIn, TCase, TOut>(
            entryName,
            built.NextNodeIndex,
            cases,
            result,
            stepConfigRegistrations.Concat(built.StepConfigRegistrations).ToArray(),
            comparer);
    }
}
```

```csharp
internal sealed record SwitchCaseBuildResult<TCase>(
    string Name,
    TCase Value,
    IReadOnlyList<IExecutionNode> Nodes);
```

---

## 10. CompositeStep<TOut> への API 実装例

### 10.1 AppendNode

```csharp
private CompositeStep<TNext> AppendNode<TNext>(IExecutionNode node)
{
    return new CompositeStep<TNext>(
        Name,
        NamespaceName,
        QualifiedName,
        Append(node),
        nextNodeIndex + 1,
        ConfigType,
        StepConfigRegistrations);
}
```

ただし、`If` / `Switch` は branch 内 node のために `nextNodeIndex` を複数進める。  
そのため、汎用 helper は次の形にする。

```csharp
private CompositeStep<TNext> WithNodes<TNext>(
    IReadOnlyList<IExecutionNode> nextNodes,
    int nextIndex,
    IReadOnlyList<StepConfigRegistration> nextStepConfigRegistrations)
{
    return new CompositeStep<TNext>(
        Name,
        NamespaceName,
        QualifiedName,
        nextNodes,
        nextIndex,
        ConfigType,
        nextStepConfigRegistrations);
}
```

### 10.2 通常 Run

```csharp
public CompositeStep<TNext> Run<TStep, TNext>()
    where TStep : IStep<TNext>, new()
{
    int nodeIndex = nextNodeIndex;

    IExecutionNode node = new StepExecutionNode<TNext>(
        nodeIndex,
        StepRegistration.Create<TStep, TNext>());

    return WithNodes<TNext>(
        Append(node),
        nodeIndex + 1,
        StepConfigRegistrations);
}
```

### 10.3 Lambda Step

```csharp
public CompositeStep<TNext> Run<TNext>(
    string name,
    Func<TOut, TNext> body)
{
    ArgumentNullException.ThrowIfNull(body);

    return Run<TNext>(
        name,
        (current, input) => body(current));
}
```

```csharp
public CompositeStep<TNext> Run<TNext>(
    string name,
    Func<TOut, StepInput, TNext> body)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(body);

    int nodeIndex = nextNodeIndex;

    IExecutionNode node = new LambdaExecutionNode<TOut, TNext>(
        nodeIndex,
        name,
        (current, input, cancellationToken) =>
            Task.FromResult(body(current, input)));

    return WithNodes<TNext>(
        Append(node),
        nodeIndex + 1,
        StepConfigRegistrations);
}
```

```csharp
public CompositeStep<TNext> RunAsync<TNext>(
    string name,
    Func<TOut, StepInput, CancellationToken, Task<TNext>> body)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(body);

    int nodeIndex = nextNodeIndex;

    IExecutionNode node = new LambdaExecutionNode<TOut, TNext>(
        nodeIndex,
        name,
        body);

    return WithNodes<TNext>(
        Append(node),
        nodeIndex + 1,
        StepConfigRegistrations);
}
```

### 10.4 RunIf

```csharp
public CompositeStep<TNext> RunIf<TStep, TNext>(
    Func<TOut, bool> when,
    Func<TOut, TNext> otherwise)
    where TStep : IStep<TNext>, new()
{
    ArgumentNullException.ThrowIfNull(when);
    ArgumentNullException.ThrowIfNull(otherwise);

    return RunIf<TStep, TNext>(
        (current, input) => when(current),
        (current, input) => otherwise(current));
}
```

```csharp
public CompositeStep<TNext> RunIf<TStep, TNext>(
    Func<TOut, StepInput, bool> when,
    Func<TOut, StepInput, TNext> otherwise)
    where TStep : IStep<TNext>, new()
{
    ArgumentNullException.ThrowIfNull(when);
    ArgumentNullException.ThrowIfNull(otherwise);

    int nodeIndex = nextNodeIndex;

    IExecutionNode node = new RunIfExecutionNode<TOut, TStep, TNext>(
        nodeIndex,
        StepRegistration.Create<TStep, TNext>(),
        when,
        (current, input, cancellationToken) =>
            Task.FromResult(otherwise(current, input)));

    return WithNodes<TNext>(
        Append(node),
        nodeIndex + 1,
        StepConfigRegistrations);
}
```

戻り値型が同じ版:

```csharp
public CompositeStep<TOut> RunIf<TStep>(
    Func<TOut, bool> when)
    where TStep : IStep<TOut>, new()
{
    ArgumentNullException.ThrowIfNull(when);

    return RunIf<TStep, TOut>(
        when,
        otherwise: current => current);
}
```

```csharp
public CompositeStep<TOut> RunIf<TStep>(
    Func<TOut, StepInput, bool> when)
    where TStep : IStep<TOut>, new()
{
    ArgumentNullException.ThrowIfNull(when);

    return RunIf<TStep, TOut>(
        when,
        otherwise: (current, input) => current);
}
```

### 10.5 TapIf

```csharp
public CompositeStep<TOut> TapIf<TStep>(
    Func<TOut, bool> when)
    where TStep : IStep<Unit>, new()
{
    ArgumentNullException.ThrowIfNull(when);

    return TapIf<TStep>(
        (current, input) => when(current));
}
```

```csharp
public CompositeStep<TOut> TapIf<TStep>(
    Func<TOut, StepInput, bool> when)
    where TStep : IStep<Unit>, new()
{
    ArgumentNullException.ThrowIfNull(when);

    int nodeIndex = nextNodeIndex;

    IExecutionNode node = new TapIfExecutionNode<TOut, TStep>(
        nodeIndex,
        StepRegistration.Create<TStep, Unit>(),
        when);

    return WithNodes<TOut>(
        Append(node),
        nodeIndex + 1,
        StepConfigRegistrations);
}
```

### 10.6 If

```csharp
public CompositeStep<TNext> If<TNext>(
    string name,
    Func<TOut, bool> condition,
    Func<BranchBuilder<TOut>, BranchBuilder<TNext>> thenFlow,
    Func<BranchBuilder<TOut>, BranchBuilder<TNext>> elseFlow)
{
    ArgumentNullException.ThrowIfNull(condition);

    return If<TNext>(
        name,
        (current, input) => condition(current),
        thenFlow,
        elseFlow);
}
```

```csharp
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

    int ifNodeIndex = nextNodeIndex;

    var thenStart = new BranchBuilder<TOut>(
        QualifiedName,
        [],
        ifNodeIndex + 1,
        []);

    BranchBuilder<TNext> thenBuilt = thenFlow(thenStart);

    var elseStart = new BranchBuilder<TOut>(
        QualifiedName,
        [],
        thenBuilt.NextNodeIndex,
        []);

    BranchBuilder<TNext> elseBuilt = elseFlow(elseStart);

    IExecutionNode node = new IfExecutionNode<TOut, TNext>(
        ifNodeIndex,
        name,
        condition,
        thenBuilt.Nodes,
        elseBuilt.Nodes);

    IReadOnlyList<StepConfigRegistration> nextRegistrations =
        StepConfigRegistrations
            .Concat(thenBuilt.StepConfigRegistrations)
            .Concat(elseBuilt.StepConfigRegistrations)
            .ToArray();

    return WithNodes<TNext>(
        Append(node),
        elseBuilt.NextNodeIndex,
        nextRegistrations);
}
```

### 10.7 Switch

```csharp
public CompositeStep<TNext> Switch<TCase, TNext>(
    string name,
    Func<TOut, TCase> selector,
    Func<SwitchCaseBuilder<TOut, TCase, TNext>, SwitchCaseBuilder<TOut, TCase, TNext>> cases)
{
    ArgumentNullException.ThrowIfNull(selector);

    return Switch<TCase, TNext>(
        name,
        (current, input) => selector(current),
        cases);
}
```

```csharp
public CompositeStep<TNext> Switch<TCase, TNext>(
    string name,
    Func<TOut, StepInput, TCase> selector,
    Func<SwitchCaseBuilder<TOut, TCase, TNext>, SwitchCaseBuilder<TOut, TCase, TNext>> cases)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(selector);
    ArgumentNullException.ThrowIfNull(cases);

    int switchNodeIndex = nextNodeIndex;

    var start = new SwitchCaseBuilder<TOut, TCase, TNext>(
        QualifiedName,
        switchNodeIndex + 1,
        [],
        null,
        []);

    SwitchCaseBuilder<TOut, TCase, TNext> built = cases(start);

    if (built.DefaultCase is null)
    {
        throw new InvalidOperationException(
            $"Switch node '{name}' requires Default case.");
    }

    IExecutionNode node = new SwitchExecutionNode<TOut, TCase, TNext>(
        switchNodeIndex,
        name,
        selector,
        built.Cases.Select(x => new SwitchCaseDefinition<TCase>(
            x.Name,
            x.Value,
            x.Nodes)).ToArray(),
        new SwitchCaseDefinition<TCase>(
            built.DefaultCase.Name,
            built.DefaultCase.Value,
            built.DefaultCase.Nodes));

    IReadOnlyList<StepConfigRegistration> nextRegistrations =
        StepConfigRegistrations
            .Concat(built.StepConfigRegistrations)
            .ToArray();

    return WithNodes<TNext>(
        Append(node),
        built.NextNodeIndex,
        nextRegistrations);
}
```

---

## 11. WithConfig の変更

### 11.1 現状の問題

現状の `WithConfig<TConfig>(sectionPath)` は直前の `StepRegistration` に対して config metadata を追加する。

概念的には次のような実装である。

```csharp
StepConfigRegistration[] nextRegistrations = StepConfigRegistrations
    .Append(new StepConfigRegistration(
        CurrentStep.StepType,
        sectionPath,
        typeof(TConfig),
        steps.Count - 1,
        null))
    .ToArray();
```

条件分岐対応後は `steps.Count - 1` では不足する。

理由:

```text
- Lambda Step は StepType を持たない
- RunIf / TapIf は通常 Step を内包する
- If / Switch の branch 内 node にも Config を付けたい
- node index は top-level の順番だけでは表現できない
```

### 11.2 方針

`StepConfigRegistration` の index は StepIndex ではなく NodeIndex として扱う。

推奨変更:

```csharp
public sealed record StepConfigRegistration(
    Type? StepType,
    string SectionPath,
    Type ConfigType,
    int NodeIndex,
    string? DefaultConfigPath);
```

既存コンストラクタ名やプロパティ名を変えると影響が大きい場合は、互換用に `StepIndex` を残し、新しく `NodeIndex` を追加する。

```csharp
public sealed record StepConfigRegistration
{
    public StepConfigRegistration(
        Type? stepType,
        string sectionPath,
        Type configType,
        int nodeIndex,
        string? defaultConfigPath)
    {
        StepType = stepType;
        SectionPath = sectionPath;
        ConfigType = configType;
        NodeIndex = nodeIndex;
        DefaultConfigPath = defaultConfigPath;
    }

    public Type? StepType { get; }

    public string SectionPath { get; }

    public Type ConfigType { get; }

    public int NodeIndex { get; }

    [Obsolete("Use NodeIndex.")]
    public int StepIndex => NodeIndex;

    public string? DefaultConfigPath { get; }
}
```

### 11.3 CompositeStep<TOut>.WithConfig

```csharp
public CompositeStep<TOut> WithConfig<TConfig>(string sectionPath)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);

    IExecutionNode currentNode = CurrentNode;

    StepConfigRegistration[] nextRegistrations = StepConfigRegistrations
        .Append(new StepConfigRegistration(
            currentNode.PrimaryStepType,
            sectionPath,
            typeof(TConfig),
            currentNode.NodeIndex,
            null))
        .ToArray();

    return new CompositeStep<TOut>(
        Name,
        NamespaceName,
        QualifiedName,
        nodes,
        nextNodeIndex,
        ConfigType,
        nextRegistrations);
}
```

```csharp
public CompositeStep<TOut> WithConfig<TConfig>(
    string sectionPath,
    string defaultConfigPath)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);
    ArgumentException.ThrowIfNullOrWhiteSpace(defaultConfigPath);

    IExecutionNode currentNode = CurrentNode;

    StepConfigRegistration[] nextRegistrations = StepConfigRegistrations
        .Append(new StepConfigRegistration(
            currentNode.PrimaryStepType,
            sectionPath,
            typeof(TConfig),
            currentNode.NodeIndex,
            defaultConfigPath))
        .ToArray();

    return new CompositeStep<TOut>(
        Name,
        NamespaceName,
        QualifiedName,
        nodes,
        nextNodeIndex,
        ConfigType,
        nextRegistrations);
}
```

### 11.4 実行時 SetStepConfig

変更前の概念:

```csharp
SetStepConfig(context, options.StepConfigs, stepIndex);
```

変更後:

```csharp
SetStepConfig(context, options.StepConfigs, node.NodeIndex);
```

実装例:

```csharp
private static void SetNodeConfig(
    StepContext context,
    IReadOnlyList<StepConfigValue>? stepConfigs,
    int nodeIndex)
{
    if (stepConfigs is null)
    {
        return;
    }

    foreach (StepConfigValue stepConfig in stepConfigs)
    {
        if (stepConfig.NodeIndex != nodeIndex)
        {
            continue;
        }

        context.Set(stepConfig.ConfigType, stepConfig.Value);
    }
}
```

既存の `StepConfigValue` が `StepIndex` を持つ場合は、同じ互換方針で `NodeIndex` に移行する。

---

## 12. 実行エンジンの変更

### 12.1 WorkflowNodeRunner を追加する

`CompositeStep<TOut>.ExecuteWorkflowAsync` に巨大なループを持ち続けると、`If` / `Switch` の branch 実行で重複する。  
node 列実行を専用クラスに切り出す。

```csharp
internal sealed class WorkflowNodeRunner
{
    public async Task<object?> ExecuteNodesAsync(
        IReadOnlyList<IExecutionNode> nodes,
        object? initialValue,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        object? currentValue = initialValue;

        foreach (IExecutionNode node in nodes)
        {
            currentValue = await ExecuteNodeWithPolicyAsync(
                    node,
                    currentValue,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return currentValue;
    }

    public Task<object?> ExecuteBranchAsync(
        IReadOnlyList<IExecutionNode> nodes,
        object? initialValue,
        NodeExecutionContext context,
        string branchName,
        CancellationToken cancellationToken)
    {
        using IDisposable? scope = context.EngineLogger.BeginScope(
            new Dictionary<string, object?>
            {
                ["EntryName"] = context.EntryName,
                ["BranchName"] = branchName,
            });

        return ExecuteNodesAsync(
            nodes,
            initialValue,
            context,
            cancellationToken);
    }

    private async Task<object?> ExecuteNodeWithPolicyAsync(
        IExecutionNode node,
        object? currentValue,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (node.Kind is ExecutionNodeKind.If or ExecutionNodeKind.Switch)
        {
            return await ExecuteControlNodeAsync(
                    node,
                    currentValue,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await ExecuteRegularNodeWithRetryAsync(
                node,
                currentValue,
                context,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
```

### 12.2 通常 node の retry / timeout

```csharp
private async Task<object?> ExecuteRegularNodeWithRetryAsync(
    IExecutionNode node,
    object? currentValue,
    NodeExecutionContext context,
    CancellationToken cancellationToken)
{
    int maxAttempts = GetMaxAttempts(context.Options.Retry);

    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        using IDisposable? stepScope = context.EngineLogger.BeginScope(
            new Dictionary<string, object?>
            {
                ["EntryName"] = context.EntryName,
                ["StepName"] = node.Name,
                ["NodeIndex"] = node.NodeIndex,
                ["Attempt"] = attempt,
            });

        context.EngineLogger.LogInformation(
            "Node started for attempt {Attempt}",
            attempt);

        using StepExecutionCancellation stepCancellation =
            CreateStepExecutionCancellation(
                context.Options.StepTimeout,
                cancellationToken);

        try
        {
            SetNodeConfig(
                context.Input.Context,
                context.Options.StepConfigs,
                node.NodeIndex);

            NodeExecutionResult result = await node.ExecuteAsync(
                    context,
                    currentValue,
                    stepCancellation.Token)
                .ConfigureAwait(false);

            StepCancellationFailure? cancellationFailure =
                DetectCancellationFailure(
                    node.Name,
                    stepCancellation,
                    cancellationToken);

            if (cancellationFailure is not null)
            {
                stopwatch.Stop();

                throw new WorkflowNodeCancellationException(
                    cancellationFailure,
                    stopwatch.Elapsed,
                    attempt);
            }

            IReadOnlyList<ExecutionTraceValue> producedValues =
                node.Produce(context.Input, result.Value);

            stopwatch.Stop();

            ExecutionTraceStepStatus status =
                result.Outcome == NodeExecutionOutcome.Skipped
                    ? ExecutionTraceStepStatus.Skipped
                    : ExecutionTraceStepStatus.Succeeded;

            context.TraceSteps.Add(new ExecutionTraceStep(
                node.Name,
                status,
                stopwatch.Elapsed,
                null,
                attempt,
                producedValues));

            context.EngineLogger.LogInformation(
                "Node finished on attempt {Attempt} with status {Status}",
                attempt,
                status);

            return result.Value;
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            throw new WorkflowNodeCancellationException(
                StepCancellationFailure.Canceled(exception.Message),
                stopwatch.Elapsed,
                attempt);
        }
        catch (OperationCanceledException exception)
            when (stepCancellation.TimeoutWasRequested)
        {
            stopwatch.Stop();

            throw new WorkflowNodeCancellationException(
                StepCancellationFailure.TimedOut(
                    node.Name,
                    stepCancellation.Timeout!.Value,
                    exception.Message),
                stopwatch.Elapsed,
                attempt);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            context.TraceSteps.Add(new ExecutionTraceStep(
                node.Name,
                ExecutionTraceStepStatus.Failed,
                stopwatch.Elapsed,
                WorkflowErrorCodes.StepExecutionFailed,
                attempt));

            if (attempt < maxAttempts)
            {
                context.EngineLogger.LogWarning(
                    exception,
                    "Node attempt {Attempt} failed with error code {ErrorCode}; retrying",
                    attempt,
                    WorkflowErrorCodes.StepExecutionFailed);

                continue;
            }

            throw;
        }
    }

    throw new InvalidOperationException(
        "Node retry loop completed without a terminal result.");
}
```

### 12.3 If / Switch は全体 retry しない

`If` / `Switch` は control node として扱う。  
control node 自体は retry 対象にしない。

理由:

```text
- If / Switch の内部 branch は複数 node を含むことがある
- If 全体を retry すると、branch 内で成功済みの Step まで再実行される
- retry は Step / Lambda / RunIf / TapIf のような単一実行 node に適用する方が分かりやすい
```

実装例:

```csharp
private async Task<object?> ExecuteControlNodeAsync(
    IExecutionNode node,
    object? currentValue,
    NodeExecutionContext context,
    CancellationToken cancellationToken)
{
    Stopwatch stopwatch = Stopwatch.StartNew();

    using IDisposable? stepScope = context.EngineLogger.BeginScope(
        new Dictionary<string, object?>
        {
            ["EntryName"] = context.EntryName,
            ["StepName"] = node.Name,
            ["NodeIndex"] = node.NodeIndex,
            ["Attempt"] = 1,
        });

    try
    {
        SetNodeConfig(
            context.Input.Context,
            context.Options.StepConfigs,
            node.NodeIndex);

        NodeExecutionResult result = await node.ExecuteAsync(
                context,
                currentValue,
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ExecutionTraceValue> producedValues =
            node.Produce(context.Input, result.Value);

        stopwatch.Stop();

        context.TraceSteps.Add(new ExecutionTraceStep(
            node.Name,
            ExecutionTraceStepStatus.Succeeded,
            stopwatch.Elapsed,
            null,
            1,
            producedValues));

        return result.Value;
    }
    catch (Exception exception)
    {
        stopwatch.Stop();

        context.TraceSteps.Add(new ExecutionTraceStep(
            node.Name,
            ExecutionTraceStepStatus.Failed,
            stopwatch.Elapsed,
            WorkflowErrorCodes.ConditionEvaluationFailed,
            1));

        context.EngineLogger.LogError(
            exception,
            "Control node failed with error code {ErrorCode}",
            WorkflowErrorCodes.ConditionEvaluationFailed);

        throw;
    }
}
```

`Switch` selector 失敗と `If` condition 失敗を区別したい場合は、node から専用例外を投げる。

```csharp
internal sealed class ConditionEvaluationException : Exception
{
    public ConditionEvaluationException(string nodeName, Exception innerException)
        : base($"Condition evaluation failed in node '{nodeName}'.", innerException)
    {
        NodeName = nodeName;
    }

    public string NodeName { get; }
}
```

```csharp
internal sealed class SwitchSelectorEvaluationException : Exception
{
    public SwitchSelectorEvaluationException(string nodeName, Exception innerException)
        : base($"Switch selector evaluation failed in node '{nodeName}'.", innerException)
    {
        NodeName = nodeName;
    }

    public string NodeName { get; }
}
```

---

## 13. ExecutionTrace の変更

### 13.1 ExecutionTraceStepStatus に Skipped を追加する

```csharp
public enum ExecutionTraceStepStatus
{
    Succeeded,
    Failed,
    Skipped,
}
```

`Skipped` は workflow 失敗を意味しない。

対象:

```text
- RunIf false
- TapIf false
```

`If` / `Switch` は selected branch を実行する control node なので、control node 自体は `Succeeded` とする。  
未選択 branch 内の node は trace に出さない。

### 13.2 Trace 例

#### RunIf false

```text
LoadStep: Succeeded
ConvertStep: Skipped
SaveStep: Succeeded
```

このとき `ConvertStep` の trace value には fallback の `ConvertResult` から生成された値が含まれてよい。

#### TapIf false

```text
LoadStep: Succeeded
NotifyStep: Skipped
SaveStep: Succeeded
```

`NotifyStep` は実行されない。  
current value は変わらない。

#### If

```text
LoadStep: Succeeded
convert-if-required: Succeeded
ConvertStep: Succeeded
SaveStep: Succeeded
```

または branch 名を node name に含める。

```text
convert-if-required:then: Succeeded
ConvertStep: Succeeded
```

最小実装では、control node の詳細 metadata は必須にしない。  
ただし将来の可観測性を考えるなら、`ExecutionTraceStep` に `Metadata` を追加する。

```csharp
public sealed record ExecutionTraceStep(
    string StepName,
    ExecutionTraceStepStatus Status,
    TimeSpan Duration,
    string? ErrorCode,
    int Attempt,
    IReadOnlyList<ExecutionTraceValue>? Values = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
```

`If` の場合:

```csharp
Metadata = new Dictionary<string, string>
{
    ["selectedBranch"] = "then",
}
```

`Switch` の場合:

```csharp
Metadata = new Dictionary<string, string>
{
    ["selectedCase"] = "Markdown",
}
```

---

## 14. ErrorCode

追加候補:

```csharp
public static class WorkflowErrorCodes
{
    public const string ConditionEvaluationFailed = "CONDITION_EVALUATION_FAILED";

    public const string SwitchSelectorFailed = "SWITCH_SELECTOR_FAILED";

    public const string SwitchCaseNotFound = "SWITCH_CASE_NOT_FOUND";
}
```

ただし初期実装では、`Switch` は `Default` 必須なので `SWITCH_CASE_NOT_FOUND` は通常発生しない。

エラー扱い:

| 状況 | ErrorCode |
|---|---|
| Lambda Step body が例外 | `STEP_EXECUTION_FAILED` |
| RunIf condition が例外 | `CONDITION_EVALUATION_FAILED` |
| RunIf fallback が例外 | `STEP_EXECUTION_FAILED` |
| TapIf condition が例外 | `CONDITION_EVALUATION_FAILED` |
| If condition が例外 | `CONDITION_EVALUATION_FAILED` |
| Switch selector が例外 | `SWITCH_SELECTOR_FAILED` |
| Switch Default 未定義 | 定義時 `InvalidOperationException` |
| Switch Case 重複 | 定義時 `InvalidOperationException` |

---

## 15. Config 読み込み方針

### 15.1 初期実装では eager validation

分岐内の Step Config も、実行前に読み込み・変換・検証する。

つまり、実行されない branch の Config が壊れていても、workflow は開始前に失敗する。

理由:

```text
- 現在の設計が「最初の Step 実行前に Config を検証する」方針
- lazy validation にすると、実行途中で Config エラーが発生する
- 初期実装では既存仕様との整合性を優先する
```

将来的に必要になった場合のみ、branch 選択後に Config を検証する lazy mode を検討する。

### 15.2 Lambda Step に Config を付けられるようにする

例:

```csharp
.Run<ConvertResult>(
    "skip-convert",
    (x, input) =>
    {
        ConvertStep.Config config = input.Context.Get<ConvertStep.Config>();
        return new ConvertResult(config.Prefix + x.Text);
    })
    .WithConfig<ConvertStep.Config>("Convert")
```

このため、`StepConfigRegistration.StepType` は nullable にする。

```csharp
public Type? StepType { get; }
```

Config の対応先は StepType ではなく NodeIndex で決める。

---

## 16. StepInput / Produce / StoreAs の扱い

### 16.1 共通ルール

すべての node は、実行後に `Produce` / `StoreAs` を適用できる。

| Node | Produce の入力 |
|---|---|
| 通常 Step | Step の戻り値 |
| Lambda Step | Lambda の戻り値 |
| RunIf true | Step の戻り値 |
| RunIf false | fallback の戻り値 |
| TapIf true | 元の current value |
| TapIf false | 元の current value |
| If | selected branch の戻り値 |
| Switch | selected case branch の戻り値 |

### 16.2 branch 内 Produce の注意

`If` / `Switch` の branch 内で `Produce` した値は、選択された branch のものだけが `StepInput` に登録される。

```csharp
.If<ConvertResult>(
    name: "convert",
    condition: x => x.ShouldConvert,
    thenFlow: b => b
        .Run<ConvertStep, ConvertResult>()
            .Produce<SaveInput>(x => new SaveInput(x.ConvertedText)),
    elseFlow: b => b
        .Run<ConvertResult>("skip", x => new ConvertResult(x.Text)))
```

上記は非推奨である。  
`thenFlow` のときだけ `SaveInput` が登録され、`elseFlow` のときは登録されないためである。

推奨は、branch の外で `Produce` すること。

```csharp
.If<ConvertResult>(
    name: "convert",
    condition: x => x.ShouldConvert,
    thenFlow: b => b
        .Run<ConvertStep, ConvertResult>(),
    elseFlow: b => b
        .Run<ConvertResult>("skip", x => new ConvertResult(x.Text)))
.Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
```

---

## 17. ExecuteAsync 非 workflow 経路

既存の `ExecuteAsync(StepInput input, CancellationToken cancellationToken)` は、trace / retry / config loading を使わない簡易実行である。  
この経路にも node 実行を適用する。

```csharp
public async Task<TOut> ExecuteAsync(
    StepInput input,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(input);

    object? currentValue = default(TOut);

    var runner = new SimpleNodeRunner();

    currentValue = await runner.ExecuteNodesAsync(
            nodes,
            currentValue,
            input,
            cancellationToken)
        .ConfigureAwait(false);

    return (TOut)currentValue!;
}
```

`SimpleNodeRunner`:

```csharp
internal sealed class SimpleNodeRunner
{
    public async Task<object?> ExecuteNodesAsync(
        IReadOnlyList<IExecutionNode> nodes,
        object? initialValue,
        StepInput input,
        CancellationToken cancellationToken)
    {
        object? currentValue = initialValue;

        foreach (IExecutionNode node in nodes)
        {
            var context = new NodeExecutionContext(
                entryName: "",
                input: input,
                options: new WorkflowExecutionOptions(),
                runner: new WorkflowNodeRunner(),
                traceSteps: [],
                engineLogger: NullLogger.Instance);

            NodeExecutionResult result = await node.ExecuteAsync(
                    context,
                    currentValue,
                    cancellationToken)
                .ConfigureAwait(false);

            node.Produce(input, result.Value);

            currentValue = result.Value;
        }

        return currentValue;
    }
}
```

実装時には `SimpleNodeRunner` と `WorkflowNodeRunner` の重複を減らしてよい。  
ただし、初期実装では分けた方が安全である。

---

## 18. 実装順序

### Phase 1: 内部 node 化

1. `ExecutionNodeKind` を追加する。
2. `IExecutionNode` を追加する。
3. `StepExecutionNode<TOut>` を追加する。
4. `CompositeStep<TOut>` の `steps` を `nodes` に置き換える。
5. 既存 `Run<TStep,TNext>()` / `RunAsync<TStep,TNext>()` を `StepExecutionNode<TNext>` 追加に変更する。
6. `Produce` / `StoreAs` / `Discard` を `CurrentNode` に対して動作させる。
7. 既存テストをすべて通す。

この Phase では新 API を追加しない。  
既存挙動が壊れていないことを先に確認する。

### Phase 2: Lambda Step

1. `LambdaExecutionNode<TIn,TOut>` を追加する。
2. `CompositeStepDefinition.Run<TOut>(string, Func<StepInput,TOut>)` を追加する。
3. `CompositeStep<TOut>.Run<TNext>(string, Func<TOut,TNext>)` を追加する。
4. `StepInput` overload を追加する。
5. async overload を追加する。
6. Lambda Step に `WithConfig` を付けられるようにする。
7. Lambda Step の trace / producer テストを追加する。

### Phase 3: RunIf / TapIf

1. `RunIfExecutionNode<TIn,TStep,TOut>` を追加する。
2. `RunIfAsyncExecutionNode<TIn,TStep,TOut>` を追加する。
3. `TapIfExecutionNode<TIn,TStep>` を追加する。
4. `TapIfAsyncExecutionNode<TIn,TStep>` を追加する。
5. `ExecutionTraceStepStatus.Skipped` を追加する。
6. false 時の fallback / current value 維持をテストする。
7. Config が condition 前に登録されることをテストする。

### Phase 4: BranchBuilder / If

1. `BranchBuilder<TOut>` を追加する。
2. branch 内 `Run` / `RunAsync` / Lambda Step / `RunIf` / `TapIf` を実装する。
3. `IfExecutionNode<TIn,TOut>` を追加する。
4. `CompositeStep<TOut>.If<TNext>` を追加する。
5. selected branch のみ実行されることをテストする。
6. unselected branch の Step が実行されないことをテストする。
7. branch 内 Config の登録と検証をテストする。

### Phase 5: Switch

1. `SwitchCaseBuilder<TIn,TCase,TOut>` を追加する。
2. `SwitchExecutionNode<TIn,TCase,TOut>` を追加する。
3. `CompositeStep<TOut>.Switch<TCase,TNext>` を追加する。
4. `Default` 必須チェックを追加する。
5. duplicate case チェックを追加する。
6. case / default の選択テストを追加する。

---

## 19. テスト設計

### 19.1 Lambda Step

```csharp
[Fact]
public void LambdaStep_returns_value()
{
    var workflow = CompositeStep.Define("Main")
        .Run<NumberResult>(
            "start",
            input => new NumberResult(1))
        .Run<NumberResult>(
            "increment",
            x => new NumberResult(x.Value + 1));

    NumberResult result = workflow.Execute(new StepInput(new StepContext()));

    Assert.Equal(2, result.Value);
}

public sealed record NumberResult(int Value);
```

### 19.2 Lambda Step can Produce

```csharp
[Fact]
public void LambdaStep_can_produce_value()
{
    var workflow = CompositeStep.Define("Main")
        .Run<NumberResult>(
            "start",
            input => new NumberResult(10))
            .Produce<string>(x => x.Value.ToString())
        .Run<ReadProducedStringStep, string>();

    string result = workflow.Execute(new StepInput(new StepContext()));

    Assert.Equal("10", result);
}

public sealed class ReadProducedStringStep : IStep<string>
{
    public string Execute(StepInput input)
    {
        return input.Get<string>();
    }
}
```

### 19.3 RunIf true executes Step

```csharp
[Fact]
public void RunIf_true_executes_step()
{
    CountingConvertStep.ExecuteCount = 0;

    var workflow = CompositeStep.Define("Main")
        .Run<LoadResult>(
            "start",
            input => new LoadResult(
                Text: "abc",
                Mode: ConvertMode.Plain,
                ShouldConvert: true,
                ShouldNotify: false))
            .Produce<ConvertInput>(x => new ConvertInput(x.Text))
        .RunIf<CountingConvertStep, ConvertResult>(
            when: x => x.ShouldConvert,
            otherwise: x => new ConvertResult(x.Text));

    ConvertResult result = workflow.Execute(new StepInput(new StepContext()));

    Assert.Equal("ABC", result.ConvertedText);
    Assert.Equal(1, CountingConvertStep.ExecuteCount);
}

public sealed class CountingConvertStep : IStep<ConvertResult>
{
    public static int ExecuteCount { get; set; }

    public ConvertResult Execute(StepInput input)
    {
        ExecuteCount++;
        ConvertInput convertInput = input.Get<ConvertInput>();
        return new ConvertResult(convertInput.Text.ToUpperInvariant());
    }
}
```

### 19.4 RunIf false uses fallback

```csharp
[Fact]
public void RunIf_false_uses_fallback()
{
    CountingConvertStep.ExecuteCount = 0;

    var workflow = CompositeStep.Define("Main")
        .Run<LoadResult>(
            "start",
            input => new LoadResult(
                Text: "abc",
                Mode: ConvertMode.Plain,
                ShouldConvert: false,
                ShouldNotify: false))
            .Produce<ConvertInput>(x => new ConvertInput(x.Text))
        .RunIf<CountingConvertStep, ConvertResult>(
            when: x => x.ShouldConvert,
            otherwise: x => new ConvertResult(x.Text));

    ConvertResult result = workflow.Execute(new StepInput(new StepContext()));

    Assert.Equal("abc", result.ConvertedText);
    Assert.Equal(0, CountingConvertStep.ExecuteCount);
}
```

### 19.5 RunIf same type false keeps current value

```csharp
[Fact]
public void RunIf_same_type_false_keeps_current_value()
{
    TrimStep.ExecuteCount = 0;

    var workflow = CompositeStep.Define("Main")
        .Run<TextResult>(
            "start",
            input => new TextResult("  abc  ", ShouldTrim: false))
        .RunIf<TrimStep>(x => x.ShouldTrim);

    TextResult result = workflow.Execute(new StepInput(new StepContext()));

    Assert.Equal("  abc  ", result.Text);
    Assert.Equal(0, TrimStep.ExecuteCount);
}

public sealed record TextResult(string Text, bool ShouldTrim);

public sealed class TrimStep : IStep<TextResult>
{
    public static int ExecuteCount { get; set; }

    public TextResult Execute(StepInput input)
    {
        ExecuteCount++;
        TextResult result = input.Get<TextResult>();
        return result with { Text = result.Text.Trim() };
    }
}
```

### 19.6 TapIf true executes Step and keeps current value

```csharp
[Fact]
public void TapIf_true_executes_step_and_keeps_current_value()
{
    NotifyCounterStep.ExecuteCount = 0;

    var workflow = CompositeStep.Define("Main")
        .Run<LoadResult>(
            "start",
            input => new LoadResult(
                Text: "abc",
                Mode: ConvertMode.Plain,
                ShouldConvert: false,
                ShouldNotify: true))
            .StoreAs()
        .TapIf<NotifyCounterStep>(x => x.ShouldNotify)
        .Run<ConvertResult>(
            "to-convert-result",
            x => new ConvertResult(x.Text));

    ConvertResult result = workflow.Execute(new StepInput(new StepContext()));

    Assert.Equal("abc", result.ConvertedText);
    Assert.Equal(1, NotifyCounterStep.ExecuteCount);
}

public sealed class NotifyCounterStep : IStep<Unit>
{
    public static int ExecuteCount { get; set; }

    public Unit Execute(StepInput input)
    {
        ExecuteCount++;
        return Unit.Value;
    }
}
```

### 19.7 TapIf false skips Step and keeps current value

```csharp
[Fact]
public void TapIf_false_skips_step_and_keeps_current_value()
{
    NotifyCounterStep.ExecuteCount = 0;

    var workflow = CompositeStep.Define("Main")
        .Run<LoadResult>(
            "start",
            input => new LoadResult(
                Text: "abc",
                Mode: ConvertMode.Plain,
                ShouldConvert: false,
                ShouldNotify: false))
            .StoreAs()
        .TapIf<NotifyCounterStep>(x => x.ShouldNotify)
        .Run<ConvertResult>(
            "to-convert-result",
            x => new ConvertResult(x.Text));

    ConvertResult result = workflow.Execute(new StepInput(new StepContext()));

    Assert.Equal("abc", result.ConvertedText);
    Assert.Equal(0, NotifyCounterStep.ExecuteCount);
}
```

### 19.8 If selects then branch

```csharp
[Fact]
public void If_selects_then_branch()
{
    var workflow = CompositeStep.Define("Main")
        .Run<LoadResult>(
            "start",
            input => new LoadResult(
                Text: "abc",
                Mode: ConvertMode.Plain,
                ShouldConvert: true,
                ShouldNotify: false))
            .Produce<ConvertInput>(x => new ConvertInput(x.Text))
        .If<ConvertResult>(
            name: "convert-if-required",
            condition: x => x.ShouldConvert,
            thenFlow: b => b
                .Run<UpperConvertStep, ConvertResult>(),
            elseFlow: b => b
                .Run<ConvertResult>(
                    "skip-convert",
                    x => new ConvertResult(x.Text)));

    ConvertResult result = workflow.Execute(new StepInput(new StepContext()));

    Assert.Equal("ABC", result.ConvertedText);
}

public sealed class UpperConvertStep : IStep<ConvertResult>
{
    public ConvertResult Execute(StepInput input)
    {
        ConvertInput convertInput = input.Get<ConvertInput>();
        return new ConvertResult(convertInput.Text.ToUpperInvariant());
    }
}
```

### 19.9 If selects else branch

```csharp
[Fact]
public void If_selects_else_branch()
{
    var workflow = CompositeStep.Define("Main")
        .Run<LoadResult>(
            "start",
            input => new LoadResult(
                Text: "abc",
                Mode: ConvertMode.Plain,
                ShouldConvert: false,
                ShouldNotify: false))
            .Produce<ConvertInput>(x => new ConvertInput(x.Text))
        .If<ConvertResult>(
            name: "convert-if-required",
            condition: x => x.ShouldConvert,
            thenFlow: b => b
                .Run<UpperConvertStep, ConvertResult>(),
            elseFlow: b => b
                .Run<ConvertResult>(
                    "skip-convert",
                    x => new ConvertResult(x.Text)));

    ConvertResult result = workflow.Execute(new StepInput(new StepContext()));

    Assert.Equal("abc", result.ConvertedText);
}
```

### 19.10 Switch selects matching case

```csharp
[Fact]
public void Switch_selects_matching_case()
{
    var workflow = CompositeStep.Define("Main")
        .Run<LoadResult>(
            "start",
            input => new LoadResult(
                Text: "abc",
                Mode: ConvertMode.Markdown,
                ShouldConvert: true,
                ShouldNotify: false))
            .Produce<ConvertInput>(x => new ConvertInput(x.Text))
        .Switch<ConvertMode, ConvertResult>(
            name: "convert-by-mode",
            selector: x => x.Mode,
            cases: c => c
                .Case(ConvertMode.Markdown, b => b
                    .Run<ConvertResult>(
                        "markdown",
                        x => new ConvertResult("# " + x.Text)))
                .Case(ConvertMode.Html, b => b
                    .Run<ConvertResult>(
                        "html",
                        x => new ConvertResult("<p>" + x.Text + "</p>")))
                .Default(b => b
                    .Run<ConvertResult>(
                        "plain",
                        x => new ConvertResult(x.Text))));

    ConvertResult result = workflow.Execute(new StepInput(new StepContext()));

    Assert.Equal("# abc", result.ConvertedText);
}
```

### 19.11 Switch selects default

```csharp
[Fact]
public void Switch_selects_default()
{
    var workflow = CompositeStep.Define("Main")
        .Run<LoadResult>(
            "start",
            input => new LoadResult(
                Text: "abc",
                Mode: ConvertMode.None,
                ShouldConvert: false,
                ShouldNotify: false))
            .Produce<ConvertInput>(x => new ConvertInput(x.Text))
        .Switch<ConvertMode, ConvertResult>(
            name: "convert-by-mode",
            selector: x => x.Mode,
            cases: c => c
                .Case(ConvertMode.Markdown, b => b
                    .Run<ConvertResult>(
                        "markdown",
                        x => new ConvertResult("# " + x.Text)))
                .Case(ConvertMode.Html, b => b
                    .Run<ConvertResult>(
                        "html",
                        x => new ConvertResult("<p>" + x.Text + "</p>")))
                .Default(b => b
                    .Run<ConvertResult>(
                        "plain",
                        x => new ConvertResult(x.Text))));

    ConvertResult result = workflow.Execute(new StepInput(new StepContext()));

    Assert.Equal("abc", result.ConvertedText);
}
```

### 19.12 Switch duplicate case throws

```csharp
[Fact]
public void Switch_duplicate_case_throws()
{
    InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
    {
        _ = CompositeStep.Define("Main")
            .Run<LoadResult>(
                "start",
                input => new LoadResult(
                    Text: "abc",
                    Mode: ConvertMode.Markdown,
                    ShouldConvert: true,
                    ShouldNotify: false))
            .Switch<ConvertMode, ConvertResult>(
                name: "convert-by-mode",
                selector: x => x.Mode,
                cases: c => c
                    .Case(ConvertMode.Markdown, b => b
                        .Run<ConvertResult>(
                            "markdown1",
                            x => new ConvertResult("# " + x.Text)))
                    .Case(ConvertMode.Markdown, b => b
                        .Run<ConvertResult>(
                            "markdown2",
                            x => new ConvertResult("## " + x.Text)))
                    .Default(b => b
                        .Run<ConvertResult>(
                            "plain",
                            x => new ConvertResult(x.Text))));
    });

    Assert.Contains("already registered", exception.Message);
}
```

### 19.13 Switch without default throws

```csharp
[Fact]
public void Switch_without_default_throws()
{
    InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
    {
        _ = CompositeStep.Define("Main")
            .Run<LoadResult>(
                "start",
                input => new LoadResult(
                    Text: "abc",
                    Mode: ConvertMode.Markdown,
                    ShouldConvert: true,
                    ShouldNotify: false))
            .Switch<ConvertMode, ConvertResult>(
                name: "convert-by-mode",
                selector: x => x.Mode,
                cases: c => c
                    .Case(ConvertMode.Markdown, b => b
                        .Run<ConvertResult>(
                            "markdown",
                            x => new ConvertResult("# " + x.Text))));
    });

    Assert.Contains("requires Default", exception.Message);
}
```

---

## 20. 非採用 API

### 20.1 WithCriteria

採用しない。

```csharp
.Run<ConvertStep, ConvertResult>()
    .WithCriteria(x => x.ShouldConvert)
```

理由:

```text
- skip 時の ConvertResult が存在しない
- 型付きチェーンと相性が悪い
- Cake の Task graph では成立しても、Devo6.WorkFlow の typed pipeline では危険
```

代わりに次を使う。

```csharp
.RunIf<ConvertStep, ConvertResult>(
    when: x => x.ShouldConvert,
    otherwise: x => new ConvertResult(x.Text))
```

### 20.2 for / ForEach

初期実装では採用しない。

理由:

```text
- 逐次か並列かを決める必要がある
- item ごとの StepInput をどう分離するか決める必要がある
- item ごとの retry / timeout / trace 仕様が必要になる
- 失敗時に全体を止めるか、item 単位で続行するかを決める必要がある
- 通常 Step 内の foreach で代替できる
```

### 20.3 DAG

採用しない。

理由:

```text
- メソッドチェーン DSL の価値が薄くなる
- node handle 方式に寄せると別の DSL になる
- 現時点の要件は if / switch / skip で満たせる
```

---

## 21. 実装時の注意点

### 21.1 current value の型チェック

Lambda Step、RunIf、TapIf、If、Switch は `object? currentValue` を `TIn` に cast する。  
cast 失敗時は実装バグまたは内部状態不整合なので `InvalidOperationException` を投げる。

```csharp
private static T CastCurrentValue<T>(object? currentValue, string nodeName)
{
    if (currentValue is T typed)
    {
        return typed;
    }

    if (currentValue is null && default(T) is null)
    {
        return default!;
    }

    throw new InvalidOperationException(
        $"Node '{nodeName}' expected current value of type '{typeof(T).FullName}', " +
        $"but actual value was '{currentValue?.GetType().FullName ?? "<null>"}'.");
}
```

### 21.2 branch の空定義

次は許可しない。

```csharp
.If<ConvertResult>(
    name: "invalid",
    condition: x => x.ShouldConvert,
    thenFlow: b => b,
    elseFlow: b => b.Run<ConvertResult>("fallback", x => new ConvertResult(x.Text)))
```

ただし `TNext == TOut` の場合は `b => b` を許可できる。

実装を単純にするため、初期実装では branch の空定義を禁止してよい。

禁止する場合:

```csharp
if (thenBuilt.Nodes.Count == 0)
{
    throw new InvalidOperationException(
        $"Then branch of If node '{name}' must contain at least one node.");
}
```

ただし、`TNext == TOut` の passthrough が欲しくなる可能性が高い。  
その場合は明示的な Lambda Step を書く。

```csharp
elseFlow: b => b.Run<LoadResult>(
    "return-current",
    x => x)
```

この設計書では、初期実装は「branch は 1 node 以上必須」とする。

### 21.3 condition / selector は副作用なしを推奨

`RunIf` の condition は retry 時に複数回評価される可能性がある。  
そのため、副作用のある処理は condition に書かない。

これはドキュメントに明記する。

```text
condition / selector は純粋関数として書くことを推奨する。
```

### 21.4 `TapIf` の Step 出力は破棄する

`TapIf<TStep>` は `TStep : IStep<Unit>` のみ許可する。  
`Unit` 以外を許可すると、「戻り値を変えない」という API の意味が曖昧になるためである。

### 21.5 Lambda Step の name は必須

Lambda Step は型名がないため、trace 名が作れない。  
そのため name は必須にする。

```csharp
.Run<ConvertResult>("skip-convert", x => new ConvertResult(x.Text))
```

次のような overload は作らない。

```csharp
// 採用しない
.Run<ConvertResult>(x => new ConvertResult(x.Text))
```

---

## 22. README 追記案

### 条件付き実行

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
        .Produce<ConvertInput>(x => new ConvertInput(x.Text))
    .RunIf<ConvertStep, ConvertResult>(
        when: x => x.ShouldConvert,
        otherwise: x => new ConvertResult(x.Text))
        .WithConfig<ConvertStep.Config>("Convert")
    .Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

`RunIf` は、条件を満たす場合だけ Step を実行する。  
条件を満たさない場合は `otherwise` の戻り値を次へ流す。

### 副作用だけを条件付きで実行する

```csharp
var Main = CompositeStep.Define("Main")
    .Run<BuildStep, BuildResult>()
        .StoreAs()
    .TapIf<NotifyStep>(x => x.ShouldNotify)
    .Run<SaveBuildResultStep, Unit>();
```

`TapIf` は current value を変更しない。  
通知、ログ、メトリクス送信などに使う。

### 複数 Step を条件分岐する

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .Produce<ConvertInput>(x => new ConvertInput(x.Text))
    .If<ConvertResult>(
        name: "convert-if-required",
        condition: x => x.ShouldConvert,
        thenFlow: b => b
            .Run<ConvertStep, ConvertResult>(),
        elseFlow: b => b
            .Run<ConvertResult>(
                "skip-convert",
                x => new ConvertResult(x.Text)))
    .Run<SaveStep, Unit>();
```

### switch で分岐する

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .Produce<ConvertInput>(x => new ConvertInput(x.Text))
    .Switch<ConvertMode, ConvertResult>(
        name: "convert-by-mode",
        selector: x => x.Mode,
        cases: c => c
            .Case(ConvertMode.Markdown, b => b
                .Run<MarkdownConvertStep, ConvertResult>())
            .Case(ConvertMode.Html, b => b
                .Run<HtmlConvertStep, ConvertResult>())
            .Default(b => b
                .Run<ConvertResult>(
                    "plain",
                    x => new ConvertResult(x.Text))))
    .Run<SaveStep, Unit>();
```

---

## 23. 受け入れ条件

この設計の実装完了条件は次の通り。

```text
- 既存の CompositeStep API が壊れていない
- 既存テストがすべて通る
- Lambda Step が top-level と branch 内で使える
- RunIf が true/false 両方で正しい戻り値を返す
- RunIf false 時も Produce が fallback 値に対して動く
- TapIf true で Step が実行される
- TapIf false で Step が実行されない
- TapIf の前後で current value 型が変わらない
- If は then/else の選択 branch だけ実行する
- Switch は matching case または default だけ実行する
- Switch の duplicate case は定義時に失敗する
- Switch の default 未定義は定義時に失敗する
- branch 内 WithConfig が実行前 Config 読み込み対象になる
- RunIf / TapIf の WithConfig が condition 評価前に StepContext へ登録される
- RunIf false / TapIf false は trace で Skipped として記録される
- If / Switch の unselected branch は trace に出ない
- retry / timeout は通常 Step / Lambda Step / RunIf / TapIf に適用される
- If / Switch 全体は retry 単位にせず、branch 内 node 単位で retry される
```

---

## 24. 最終方針

実装対象は次の 5 つに限定する。

```text
1. Lambda Step
2. RunIf
3. TapIf
4. If
5. Switch
```

設計上の最重要ルールは次である。

```text
条件により Step を skip しても、current value は必ず存在する。
```

このため、`WithCriteria` のような単純 skip API は採用しない。  
代わりに `RunIf` では fallback を必須にする。

```csharp
.RunIf<ConvertStep, ConvertResult>(
    when: x => x.ShouldConvert,
    otherwise: x => new ConvertResult(x.Text))
```

`TapIf` は副作用専用とし、戻り値型を変えない。

```csharp
.TapIf<NotifyStep>(x => x.ShouldNotify)
```

複数 Step の分岐は `If` / `Switch` で表現する。

```csharp
.If<ConvertResult>(
    name: "convert-if-required",
    condition: x => x.ShouldConvert,
    thenFlow: b => b.Run<ConvertStep, ConvertResult>(),
    elseFlow: b => b.Run<ConvertResult>("skip", x => new ConvertResult(x.Text)))
```

```csharp
.Switch<ConvertMode, ConvertResult>(
    name: "convert-by-mode",
    selector: x => x.Mode,
    cases: c => c
        .Case(ConvertMode.Markdown, b => b.Run<MarkdownConvertStep, ConvertResult>())
        .Case(ConvertMode.Html, b => b.Run<HtmlConvertStep, ConvertResult>())
        .Default(b => b.Run<ConvertResult>("plain", x => new ConvertResult(x.Text))))
```

`for` / `ForEach` と DAG は今回の設計対象外とする。  
これにより、メソッドチェーン DSL の単純さを保ったまま、実用上必要な条件分岐と skip を実装できる。
