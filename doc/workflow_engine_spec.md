# C# / YAML / csx ワークフローエンジン設計仕様書 rev4

## 1. 概要

本仕様書は、C#、YAML、C# Script（`.csx`）を用いて構成するワークフローエンジンの設計仕様を定義する。

本エンジンでは、ワークフロー構造をYAMLで定義し、Message型、Config型、Step処理をC# Scriptまたは登録済みBuilt-in Stepで定義する。エンジンはYAMLとcsxを読み込み、`Dotnet.Script.Core` を用いたcsx依存解決とコンパイル、型解決、型検証、Message検証、Config binding、Config検証、Step実行、Flow実行、retry、timeout、ログ出力、実行トレース生成を行う。

本仕様の中心方針は以下である。

- Workflowは複数のFlowから構成される。
- Flowは型付きのStep集合であり、Input型とOutput型を持つ。
- StepはC#ジェネリック型でInput / Config / Output型を明示する。
- Message型はYAMLではなくcsxで定義する。
- Config型もcsxで定義する。
- ConfigはWorkflow全体ConfigとStep個別Configに分ける。
- Workflow ConfigはYAML既定値、実行時Config、実行時Overrideから解決する。
- Step ConfigはYAML、Message、Workflow Config、またはそれらのmergeから解決する。
- MessageおよびConfigの検証はエンジン側で行う。
- csxの `#load`、`#r`、インラインNuGet参照は、初期実装では `Dotnet.Script.Core` を `IScriptCompiler` の一実装として利用して対応する。
- `dotnet script` CLIをStep実行ごとに外部プロセス起動する方式は通常実行経路では採用しない。
- If、ForEach、Whileなどの制御処理はYAML予約構文ではなく、通常のStepとして表現する。
- 初期実装のFlow実行モデルは単一路線とし、Stepの `next` は0または1件のみ許可する。
- ログは独自実装せず、`Microsoft.Extensions.Logging` を経由して外部logger providerへ委譲する。
- ExecutionTraceはログとは別の構造化実行履歴として扱う。
- 初期実装ではcsxは信頼済みWorkflowのみを対象とし、未信頼コード実行のサンドボックスは提供しない。

---

## 2. 設計原則

### 2.1 エンジンが特別な制御構文を持たない

本エンジンは、YAML上に `if`、`for`、`while`、`switch` などの予約制御構文を持たない。

条件分岐、繰り返し、Flow呼び出し、例外処理、並列実行などは、すべてStepとして表現する。

例:

```yaml
- id: branch
  use: Workflow.Control.IfStep
  config:
    provider: yaml
    value:
      condition: current.IsValid == true
      then: accepted-flow
      else: rejected-flow
  next:
    to: end
```

この `branch` は制御構文ではなく、`Workflow.Control.IfStep` という通常Stepである。

### 2.2 型の真実はC#側に置く

Script StepのInput / Config / Output型は、原則としてC#の継承階層から推論する。

```csharp
public sealed class ValidateOrderStep
    : WorkflowStep<OrderCreated, ValidationResult>
{
    public override Task<ValidationResult> ExecuteAsync(
        OrderCreated input,
        StepContext context)
    {
        ...
    }
}
```

この定義から、エンジンは以下を推論する。

```text
Input  = OrderCreated
Config = none
Output = ValidationResult
```

ConfigありStepでは以下のように推論する。

```csharp
public sealed class SendMailStep
    : WorkflowStep<SendMailRequest, SendMailConfig, SendMailResult>
{
    public override Task<SendMailResult> ExecuteAsync(
        SendMailRequest input,
        SendMailConfig config,
        StepContext context)
    {
        ...
    }
}
```

```text
Input  = SendMailRequest
Config = SendMailConfig
Output = SendMailResult
```

### 2.3 YAMLは構造とbindingを定義する

YAMLは以下を定義する。

- Workflow ID、version、schemaVersion
- 型定義csx一覧
- csx参照、復元、コンパイル方針
- Workflow Config型と既定値
- Entry Flow
- Flow一覧
- Step一覧
- Step間の接続
- Step input binding
- Step config binding
- retry、timeout、trace、検証、制限

YAMLはScript Stepの入出力型を原則として重複定義しない。ただし、デバッグまたは検証補助のため、`inputType`、`configType`、`outputType` を任意で明示できる。この場合、YAML明示型はC#から推論した型と一致しなければならない。

### 2.4 実行ラップはユーザーから隠す

ユーザーが通常利用する公開APIは以下に限定する。

```text
WorkflowStep<TInput, TOutput>
WorkflowStep<TInput, TConfig, TOutput>
StepContext
Unit
IFlowInvoker
WorkflowStepException
```

以下の内部実装詳細はユーザーAPIとして公開しない。

```text
ICompiledStep
CompiledStep<TInput, TOutput>
StepExecutionEnvelope
Reflection呼び出し
object変換
StepResult変換
型検証処理
retry/timeout/cancel制御
Config binding実装
ExecutionTrace構築処理
```

### 2.5 初期実装は単一路線Flowに限定する

初期実装では、Flow内のStep接続は単一路線とする。

- `next` は0または1件のみ指定できる。
- `next` 未指定は `end` への到達と同義とする。
- 複数後続Stepへのfan-outは初期実装では扱わない。
- 合流、並列接続、エッジ条件は初期実装では扱わない。
- 分岐は `IfStep` などのControl Stepが内部で子Flowを実行することで表現する。

これにより、`end` 到達時のFlow Outputは「その実行パスで最後に実行されたStepのOutput」と一意に定まる。

---

## 3. ファイル構成

標準的なWorkflow定義ディレクトリは以下とする。

```text
workflow-root/
├── workflow.yaml
├── NuGet.Config
├── messages/
│   └── order-messages.csx
├── configs/
│   └── mail-configs.csx
├── shared/
│   └── common.csx
├── lib/
│   └── custom-helper.dll
├── steps/
│   ├── validate-order.csx
│   ├── accepted.csx
│   ├── rejected.csx
│   ├── send-mail.csx
│   └── process-item.csx
└── README.md
```

`messages` と `configs` は物理ディレクトリとして分けてもよいが、どちらもC#型定義であるため、同一csxにまとめてもよい。

`shared` は `#load` で読み込む共通csxを置く任意ディレクトリである。`lib` は `#r` で参照するローカルassemblyを置く任意ディレクトリである。`NuGet.Config` はインラインNuGet参照を許可する場合のパッケージソース定義に使用できる。

相対パスの基準は、原則として `workflow.yaml` が存在するディレクトリとする。

---

## 4. 基本概念

### 4.1 Workflow

Workflowは実行定義全体を表す。

Workflowは以下を持つ。

- Workflow ID
- schemaVersion
- version
- 型定義csx一覧
- csx参照、復元、コンパイル方針
- Workflow Config型
- Workflow Config既定値
- 実行時Config merge方針
- Entry Flow
- Flow一覧
- 検証設定
- trace設定
- 実行制限

### 4.2 Flow

FlowはStepの集合である。

Flowは以下を持つ。

- Flow ID
- Input型
- Output型
- Start Step ID
- Step一覧

FlowはWorkflow内で名前付き定義として管理される。Flow自体はC#クラスではなく、YAMLで定義される。

Flowは以下の方法で実行できる。

- Workflowの `entryFlow` として実行する。
- `flow` 指定のFlow Call Stepとして実行する。
- ユーザーStepまたはBuilt-in Stepから `StepContext.Flows` 経由で呼び出す。

### 4.3 Step

Stepは最小実行単位である。

Stepには以下の種類がある。

| 種類 | 説明 |
|---|---|
| Script Step | ユーザーがcsxで定義するStep |
| Built-in Step | エンジンまたはライブラリが提供するStep |
| Flow Call Step | FlowをStepとして呼び出すStep |

これらはすべて、エンジン内部では同じ `StepDefinition` として扱う。

### 4.4 Message

MessageはStep間、Flow間で受け渡されるデータ型である。

Message型はYAMLではなくcsxで定義する。

```csharp
namespace Workflows.Messages;

public sealed record OrderCreated(
    string OrderId,
    string CustomerId,
    decimal Amount
);
```

Message型は原則として不変な `record` を推奨する。

### 4.5 Config

ConfigはWorkflowまたはStepに与える設定値である。

本仕様では、以下を区別する。

| 種類 | 説明 | 主な用途 |
|---|---|---|
| Workflow Config | Workflow実行全体で共有するRun単位の設定 | 対象フォルダ、出力先、環境名、外部ツールパス |
| Step Config | 特定Stepだけで使う設定 | SMTP設定、処理パラメータ、Step固有オプション |

Config型もcsxで定義する。

```csharp
namespace Workflows.Messages;

public sealed record FileWorkflowConfig(
    string TargetDirectory,
    string OutputDirectory,
    string? TemporaryDirectory
);

public sealed record SendMailConfig(
    string SmtpHost,
    int Port,
    bool UseSsl,
    string From
);
```

Workflow ConfigはRun 開始時に解決し、Run中は不変のスナップショットとして扱う。

Step ConfigはStep実行直前に解決し、Stepの `TConfig` として渡す。

---

## 5. YAML仕様

### 5.1 最小構成

```yaml
id: order-workflow
schemaVersion: 1
version: 1.0.0

scripts:
  - messages/order-messages.csx
  - configs/mail-configs.csx

scriptOptions:
  engine: dotnet-script-core
  allowLoad: true
  allowAssemblyReferences: true
  allowNuGetReferences: true
  load:
    allowOutsideWorkflowRoot: false
  references:
    allowedAssemblies:
      - System.Text.Json
    allowedPaths:
      - lib/
  nuget:
    requireExactVersion: true
    allowedPackages:
      - id: CsvHelper
        versions:
          - 33.0.1
    packageSources:
      - https://api.nuget.org/v3/index.json

entryFlow: main

config:
  type: Workflows.Messages.FileWorkflowConfig
  value:
    targetDirectory: ./input
    outputDirectory: ./output
    temporaryDirectory: ./tmp
  runtime:
    merge: deep
    precedence: runtime
    nullOverride: false

limits:
  maxFlowDepth: 32

flows:
  - id: main
    input: Workflows.Messages.OrderCreated
    output: Workflows.Messages.OrderProcessResult
    start: validate-order

    steps:
      - id: validate-order
        script: steps/validate-order.csx
        class: ValidateOrderStep
        next:
          to: branch

      - id: branch
        use: Workflow.Control.IfStep
        config:
          provider: yaml
          value:
            condition: current.IsValid == true
            then: accepted-flow
            else: rejected-flow
        next:
          to: end

  - id: accepted-flow
    input: Workflows.Messages.ValidationResult
    output: Workflows.Messages.OrderProcessResult
    start: accepted

    steps:
      - id: accepted
        script: steps/accepted.csx
        class: AcceptedStep
        next:
          to: end

  - id: rejected-flow
    input: Workflows.Messages.ValidationResult
    output: Workflows.Messages.OrderProcessResult
    start: rejected

    steps:
      - id: rejected
        script: steps/rejected.csx
        class: RejectedStep
        next:
          to: end
```

### 5.2 トップレベル項目

| 項目 | 必須 | 説明 |
|---|---:|---|
| `id` | 必須 | Workflow ID |
| `schemaVersion` | 必須 | YAML schema version。初期値は `1` |
| `version` | 必須 | Workflow定義バージョン |
| `scripts` | 任意 | Message型、Config型、補助型のcsx一覧。未指定時は空配列 |
| `scriptOptions` | 任意 | `#load`、`#r`、インラインNuGet参照、コンパイルキャッシュ、AssemblyLoadContextの方針 |
| `entryFlow` | 必須 | 実行開始Flow ID |
| `config` | 任意 | Workflow Configの型、既定値、実行時merge方針 |
| `flows` | 必須 | Flow定義一覧 |
| `validation` | 任意 | 検証設定 |
| `trace` | 任意 | ExecutionTrace設定 |
| `limits` | 任意 | 実行制限 |

### 5.3 Flow定義

```yaml
flows:
  - id: main
    input: Workflows.Messages.OrderCreated
    output: Workflows.Messages.OrderProcessResult
    start: validate-order
    steps:
      - id: validate-order
        script: steps/validate-order.csx
        class: ValidateOrderStep
```

| 項目 | 必須 | 説明 |
|---|---:|---|
| `id` | 必須 | Flow ID |
| `input` | 必須 | Flow Input型の完全修飾名 |
| `output` | 必須 | Flow Output型の完全修飾名 |
| `start` | 必須 | 開始Step ID |
| `steps` | 必須 | Step定義一覧 |

Flow IDはWorkflow内で一意でなければならない。

### 5.4 Step定義

Step定義は以下の統一モデルを持つ。

```yaml
- id: some-step
  script: steps/some-step.csx
  class: SomeStep
  use: SomeBuiltInStep
  flow: some-flow
  input:
    from: previousOutput.Request
  config:
    provider: yaml
    value:
      key: value
  inputType: Workflows.Messages.SomeInput
  configType: Workflows.Messages.SomeConfig
  outputType: Workflows.Messages.SomeOutput
  retry:
    maxAttempts: 3
    interval: 00:00:05
  timeout: 00:00:30
  next:
    to: next-step
```

| 項目 | 必須 | 説明 |
|---|---:|---|
| `id` | 必須 | Step ID |
| `script` | 条件付き | Script Stepのcsxファイル |
| `class` | 条件付き | Script Stepのクラス名 |
| `use` | 条件付き | Built-in Step ID |
| `flow` | 条件付き | 呼び出すFlow ID |
| `input` | 任意 | Step input binding |
| `config` | 任意 | Step config binding |
| `inputType` | 任意 | YAML明示Input型 |
| `configType` | 任意 | YAML明示Config型 |
| `outputType` | 任意 | YAML明示Output型 |
| `retry` | 任意 | Step retry設定 |
| `timeout` | 任意 | Step timeout設定 |
| `next` | 任意 | 次Step定義。未指定時は `end` と同義 |

`script + class`、`use`、`flow` のいずれか1つだけを指定する。

Step IDはFlow内で一意でなければならない。

`end` は予約Step IDであり、ユーザー定義Stepの `id` として使用できない。

### 5.5 next定義

初期実装では、`next` は単一の後続Stepだけを表す。

```yaml
next:
  to: next-step
```

`end` に遷移する場合は以下のように書く。

```yaml
next:
  to: end
```

`next` を省略した場合は、以下と同義とする。

```yaml
next:
  to: end
```

初期実装では以下を検証時にエラーとする。

```yaml
next:
  - to: step-a
  - to: step-b
```

複数後続Step、条件付きedge、fan-out、合流、DAG実行は将来拡張とする。

---

## 6. 公開C# API

### 6.1 ConfigなしStep

```csharp
public abstract class WorkflowStep<TInput, TOutput>
{
    public abstract Task<TOutput> ExecuteAsync(
        TInput input,
        StepContext context);
}
```

### 6.2 ConfigありStep

```csharp
public abstract class WorkflowStep<TInput, TConfig, TOutput>
{
    public abstract Task<TOutput> ExecuteAsync(
        TInput input,
        TConfig config,
        StepContext context);
}
```

### 6.3 Unit

入力なし、または出力なしを表す型として `Unit` を提供する。

```csharp
public readonly record struct Unit
{
    public static readonly Unit Value = new();
}
```

入力なしStep:

```csharp
public sealed class StartStep
    : WorkflowStep<Unit, OrderCreated>
{
    public override Task<OrderCreated> ExecuteAsync(
        Unit input,
        StepContext context)
    {
        return Task.FromResult(new OrderCreated(...));
    }
}
```

出力なしStep:

```csharp
public sealed class SendNotificationStep
    : WorkflowStep<SendNotificationRequest, Unit>
{
    public override Task<Unit> ExecuteAsync(
        SendNotificationRequest input,
        StepContext context)
    {
        ...
        return Task.FromResult(Unit.Value);
    }
}
```

### 6.4 StepContext

```csharp
public sealed class StepContext
{
    public string WorkflowId { get; }
    public string RunId { get; }
    public string FlowId { get; }
    public string StepId { get; }

    public IReadOnlyDictionary<string, object?> Variables { get; }

    public object? WorkflowConfig { get; }

    public TWorkflowConfig GetWorkflowConfig<TWorkflowConfig>();

    public ILogger Logger { get; }

    public CancellationToken CancellationToken { get; }

    public IFlowInvoker Flows { get; }

    public IServiceProvider Services { get; }
}
```

`WorkflowConfig` はRun 開始時に解決済みのWorkflow Config スナップショットである。

ユーザーStepは `GetWorkflowConfig<TWorkflowConfig>()` により、csxで定義したWorkflow Config型として取得できる。

`Variables` はRun 開始時に渡された読み取り専用値である。初期実装では、Stepから `Variables` を更新できない。

### 6.5 Flow呼び出しAPI

```csharp
public interface IFlowInvoker
{
    Task<TOutput> RunAsync<TInput, TOutput>(
        string flowId,
        TInput input,
        CancellationToken cancellationToken = default);
}
```

このAPIにより、ユーザー定義StepやBuilt-in Stepから別Flowを呼び出せる。

### 6.6 WorkflowStepException

ユーザーStepで業務エラーを明示する場合は `WorkflowStepException` を投げる。

```csharp
public sealed class WorkflowStepException : Exception
{
    public string ErrorCode { get; }
    public bool Retryable { get; }
    public IReadOnlyDictionary<string, object?> Details { get; }

    public WorkflowStepException(
        string errorCode,
        string message,
        bool retryable = false,
        IReadOnlyDictionary<string, object?>? details = null,
        Exception? innerException = null);
}
```

例:

```csharp
throw new WorkflowStepException(
    errorCode: "INVALID_ORDER",
    message: "Order is invalid.",
    retryable: false);
```

---

## 7. Message型定義

Message型はcsxで定義する。

```csharp
#nullable enable

using System.ComponentModel.DataAnnotations;

namespace Workflows.Messages;

public sealed record OrderItem(
    [Required]
    string ItemId,

    [Range(1, int.MaxValue)]
    int Quantity
);

public sealed record OrderCreated(
    [Required]
    string OrderId,

    [Required]
    string CustomerId,

    [MinLength(1)]
    IReadOnlyList<OrderItem> Items
);

public sealed record ValidationResult(
    bool IsValid,
    string? Reason
);

public sealed record OrderProcessResult(
    bool Success,
    string Message
);
```

Message型は以下を推奨する。

- `#nullable enable` を有効にする。
- `record` を使う。
- 不変にする。
- `System.ComponentModel.DataAnnotations` を使う。
- 業務固有の複雑な検証が必要な場合は `IValidatableObject` を実装する。

---

## 8. Config型定義

Config型もcsxで定義する。

```csharp
#nullable enable

using System.ComponentModel.DataAnnotations;

namespace Workflows.Messages;

public sealed record FileWorkflowConfig(
    [Required]
    string TargetDirectory,

    [Required]
    string OutputDirectory,

    string? TemporaryDirectory,

    MailDefaults? Mail
);

public sealed record MailDefaults(
    string SmtpHost,
    int Port,
    bool UseSsl,
    string From
);

public sealed record SendMailConfig(
    [Required]
    string SmtpHost,

    [Range(1, 65535)]
    int Port,

    bool UseSsl,

    [Required]
    string From
);
```

Config型はMessage型と同じ検証対象である。

---

## 9. Step実装

### 9.1 ConfigなしStep

```csharp
#nullable enable

#load "../shared/common.csx"
#r "nuget: CsvHelper, 33.0.1"

using Workflow.Abstractions;
using Workflows.Messages;

public sealed class ValidateOrderStep
    : WorkflowStep<OrderCreated, ValidationResult>
{
    public override Task<ValidationResult> ExecuteAsync(
        OrderCreated input,
        StepContext context)
    {
        var result = input.Items.Count > 0
            ? new ValidationResult(true, null)
            : new ValidationResult(false, "Order must have at least one item.");

        return Task.FromResult(result);
    }
}
```

### 9.2 ConfigありStep

```csharp
#nullable enable

using Workflow.Abstractions;
using Workflows.Messages;

public sealed class SendMailStep
    : WorkflowStep<SendMailRequest, SendMailConfig, SendMailResult>
{
    public override Task<SendMailResult> ExecuteAsync(
        SendMailRequest input,
        SendMailConfig config,
        StepContext context)
    {
        context.Logger.LogInformation(
            "Sending mail. To={To}, SmtpHost={SmtpHost}, Port={Port}",
            input.To,
            config.SmtpHost,
            config.Port);

        return Task.FromResult(new SendMailResult(true));
    }
}
```

### 9.3 CancellationTokenの扱い

Step実装は `context.CancellationToken` を尊重する。

```csharp
public override async Task<SendMailResult> ExecuteAsync(
    SendMailRequest input,
    SendMailConfig config,
    StepContext context)
{
    context.CancellationToken.ThrowIfCancellationRequested();

    await mailClient.SendAsync(input, config, context.CancellationToken);

    return new SendMailResult(true);
}
```

Step timeoutは協調キャンセルである。C#の任意Taskを安全に強制停止することはできないため、StepがCancellationTokenを無視した場合、実処理が継続する可能性がある。Step実装は冪等に設計することを推奨する。

---

## 10. Binding式仕様

### 10.1 Binding式の目的

Binding式は、Step input、Step config、Control Stepの条件式、Workflow Config参照などで使用する軽量な式である。

Binding式はC#コードではない。初期実装では、プロパティ参照と基本演算のみをサポートする。

### 10.2 予約root識別子

式中で使用できるroot識別子は以下とする。

| 識別子 | 意味 |
|---|---|
| `flowInput` | 現在Flowの開始Input |
| `previousOutput` | 直前StepのOutput。開始Stepでは未定義またはnull |
| `current` | 現在Stepに渡されるInput。Step input binding解決後に使用可能 |
| `workflowConfig` | Run 開始時に解決済みのWorkflow Config スナップショット |
| `variables` | Run 開始時に渡された読み取り専用Variables |
| `config` | 現在Stepに渡されるConfig。条件式評価時に使用可能 |

`input` は曖昧さを避けるため、式root識別子として使用しない。

### 10.3 Step input bindingの評価スコープ

Step input bindingは、Step Inputを決定するためにStep実行前に評価する。

この時点で使用できるroot識別子は以下である。

```text
flowInput
previousOutput
workflowConfig
variables
```

`current` はStep input bindingの結果を表すため、Step input binding式の中では使用できない。

例:

```yaml
input:
  from: flowInput.Request
```

```yaml
input:
  from: previousOutput.Customer
```

```yaml
input:
  from: workflowConfig.TargetDirectory
```

### 10.4 Step input binding未指定時

`input.from` が未指定の場合、Step Inputは以下の規則で決定する。

| 対象Step | Step Input |
|---|---|
| Flowの開始Step | `flowInput` |
| 2番目以降のStep | `previousOutput` |

この規則により、線形Flowでは前StepのOutputが次StepのInputにそのまま渡る。

### 10.5 Config bindingと条件式の評価スコープ

Step Config binding、Control Step条件式では、Step Input解決後の `current` を使用できる。

例:

```yaml
config:
  provider: yaml
  value:
    condition: current.IsValid == true
    then: accepted-flow
    else: rejected-flow
```

### 10.6 サポートするプロパティ参照

初期実装では以下をサポートする。

```text
flowInput
flowInput.Property
flowInput.Property.Child
previousOutput
previousOutput.Property
current
current.Property
current.Property.Child
workflowConfig
workflowConfig.Property
workflowConfig.Property.Child
variables.Property
config.Property
```

### 10.7 条件式の演算子

条件式では以下をサポートする。

```text
==
!=
>
>=
<
<=
&&
||
!
()
```

文字列、数値、bool、nullリテラルを扱う。

例:

```yaml
condition: current.IsValid == true
condition: current.Amount > 1000 && current.CustomerId != null
condition: workflowConfig.Environment == "production"
```

---

## 11. Step Config仕様

### 11.1 Config bindingの基本形

Step Config bindingは `config.provider` で供給元を指定する。

```yaml
config:
  provider: yaml
  value:
    key: value
```

`provider` はConfig供給元を表すメタ情報である。Step固有Configの値は必ず `value` または各provider固有の項目に格納する。

これにより、ForEachStepなどのStep固有Configに `source`、`itemsFrom`、`body` などのプロパティがあっても、Config供給元の指定と衝突しない。

### 11.2 YAML config

```yaml
steps:
  - id: send-mail
    script: steps/send-mail.csx
    class: SendMailStep
    config:
      provider: yaml
      value:
        smtpHost: smtp.example.com
        port: 587
        useSsl: true
        from: noreply@example.com
```

YAML configは静的設定として扱う。

### 11.3 Message config

Flow inputまたは現在Step Inputからconfigを渡す。

```csharp
namespace Workflows.Messages;

public sealed record MailFlowInput(
    SendMailRequest Request,
    SendMailConfig Config
);
```

```yaml
steps:
  - id: send-mail
    script: steps/send-mail.csx
    class: SendMailStep
    input:
      from: flowInput.Request
    config:
      provider: message
      from: flowInput.Config
```

`provider: message` の `from` は、Binding式である。

### 11.4 Workflow ConfigからのStep Config binding

Step ConfigはWorkflow Configからも生成できる。

```yaml
steps:
  - id: send-mail
    script: steps/send-mail.csx
    class: SendMailStep
    config:
      provider: workflow
      from: workflowConfig.Mail
```

`provider: workflow` の `from` はWorkflow Configを起点とするBinding式である。

### 11.5 YAML / Message / Workflow Configのmerge

Step Configは複数供給元をmergeして生成できる。

```yaml
steps:
  - id: send-mail
    script: steps/send-mail.csx
    class: SendMailStep
    input:
      from: flowInput.Request
    config:
      provider: merge
      yaml:
        smtpHost: smtp.example.com
        port: 587
        useSsl: true
        from: noreply@example.com
      message:
        from: flowInput.ConfigOverride
      workflow:
        from: workflowConfig.Mail
      merge:
        strategy: deep
        precedence:
          - message
          - workflow
          - yaml
        nullOverride: false
```

`precedence` は優先度の高い順に指定する。上記では `message` が最優先、`yaml` が最下位である。

### 11.6 Step Config merge規則

Step Config mergeは以下の規則で行う。

| 項目 | 規則 |
|---|---|
| オブジェクト | `strategy: deep` の場合、プロパティ単位で再帰mergeする |
| スカラー | 優先度の高い値で置換する |
| 配列 / リスト | 優先度の高い値で全体を置換する |
| null | `nullOverride: false` の場合は未指定扱いとする |
| null | `nullOverride: true` の場合はnullで上書きする |
| 未知キー | `validation.strictUnknownProperties` がtrueの場合はエラー |

初期実装では `strategy: deep` のみ必須対応とする。`strategy: shallow` は将来拡張とする。

### 11.7 Step Configの解決順序

Step ConfigはStep実行前に以下の順で解決する。

```text
1. Step Input bindingを解決する
2. Step Input検証を実行する
3. Step定義のconfig.providerを読む
4. YAML / Message / Workflow / merge のいずれかからraw config値を生成する
5. Stepが要求するTConfigへbindする
6. Config検証を実行する
7. 検証済みConfigをStepへ渡す
```

ConfigなしStepに `config` が指定された場合は検証時にエラーとする。

ConfigありStepに `config` が指定されていない場合、エンジンは以下の扱いとする。

- `TConfig` が `Unit` の場合は `Unit.Value` を渡す。
- `TConfig` がnullableの場合はnullを許可する。
- それ以外の場合は `CONFIG_BINDING_FAILED` とする。

---

## 12. Workflow Config仕様

### 12.1 基本方針

Workflow Configは、Workflow実行全体で共有する設定である。

Workflow ConfigはStep間で受け渡す業務Messageではなく、Run単位の実行環境設定として扱う。

主な用途は以下である。

- Workflowを実行する対象フォルダ
- 成果物の出力先フォルダ
- 一時作業フォルダ
- 実行環境名
- 外部ツールのパス
- 複数Stepで共有する共通パラメータ

Workflow ConfigはWorkflow開始時に一度だけ解決し、Run中は不変のスナップショットとして扱う。

### 12.2 Workflow Config型

Workflow Config型もcsxで定義する。

```csharp
#nullable enable

using System.ComponentModel.DataAnnotations;

namespace Workflows.Messages;

public sealed record FileWorkflowConfig(
    [Required]
    string TargetDirectory,

    [Required]
    string OutputDirectory,

    string? TemporaryDirectory
);
```

Workflow Config型は、Message型やStep Config型と同じ検証対象である。

### 12.3 YAML既定値

Workflow定義には、Workflow Configの既定値を書ける。

```yaml
config:
  type: Workflows.Messages.FileWorkflowConfig
  value:
    targetDirectory: ./input
    outputDirectory: ./output
    temporaryDirectory: ./tmp
```

YAMLに書かれたWorkflow Configは、定義側の既定値として扱う。

### 12.4 実行時Config

Workflow Configは、実行時に差し替えられる。

CLI例:

```bash
workflow run workflow.yaml --input input.yaml --config run-config.yaml
```

`run-config.yaml` 例:

```yaml
targetDirectory: /work/orders/2026-06-02
outputDirectory: /work/results/2026-06-02
temporaryDirectory: /work/tmp/2026-06-02
```

同じWorkflow定義を使いながら、実行対象フォルダや出力先をRunごとに変更できる。

### 12.5 実行時Override

CLIではkey-value形式のoverrideを指定できる。

```bash
workflow run workflow.yaml \
  --input input.yaml \
  --config run-config.yaml \
  --config-override targetDirectory=/tmp/job-001 \
  --config-override temporaryDirectory=null
```

Overrideは常に最優先とする。

### 12.6 Workflow Config merge規則

Workflow Configは以下の層から構成する。

```text
Layer 1: workflow.yaml の config.value
Layer 2: WorkflowRunOptions.WorkflowConfig または --config run-config.yaml
Layer 3: WorkflowRunOptions.WorkflowConfigOverrides または --config-override
```

標準優先順位は以下とする。

```text
Layer 3 > Layer 2 > Layer 1
```

`config.runtime.precedence` はLayer 1とLayer 2の優先関係を指定する。Layer 3のOverrideは常に最優先であり、`precedence` の影響を受けない。

```yaml
config:
  type: Workflows.Messages.FileWorkflowConfig
  value:
    targetDirectory: ./input
    outputDirectory: ./output
    temporaryDirectory: ./tmp
  runtime:
    merge: deep
    precedence: runtime
    nullOverride: false
```

| 項目 | 値 | 説明 |
|---|---|---|
| `merge` | `deep` | objectを再帰mergeする |
| `precedence` | `runtime` | 実行時ConfigをYAML既定値より優先する |
| `precedence` | `yaml` | YAML既定値を実行時Configより優先する |
| `nullOverride` | `false` | nullは未指定扱いとする |
| `nullOverride` | `true` | nullで上書きする |

配列 / リストは結合せず、優先度の高い値で全体を置換する。

### 12.7 C# API

Workflow実行時にWorkflow Configを渡すため、Run単位のOptionsを持つ。

```csharp
public sealed class WorkflowRunOptions
{
    public object? WorkflowConfig { get; init; }

    public IReadOnlyDictionary<string, object?> WorkflowConfigOverrides { get; init; }
        = new Dictionary<string, object?>();

    public IReadOnlyDictionary<string, object?> Variables { get; init; }
        = new Dictionary<string, object?>();
}
```

エンジンは以下の順でWorkflow Configを解決する。

```text
1. workflow.yaml の config.value を読む
2. WorkflowRunOptions.WorkflowConfig を読む
3. WorkflowRunOptions.WorkflowConfigOverrides を読む
4. config.runtime.merge / precedence / nullOverride に基づきmergeする
5. Workflow Config型へbindする
6. Workflow Config検証を実行する
7. 解決済みsnapshotをRunに固定する
```

### 12.8 Stepからの参照

Stepは `StepContext` からWorkflow Configを参照できる。

```csharp
public sealed class ScanFilesStep
    : WorkflowStep<Unit, ScanFilesResult>
{
    public override Task<ScanFilesResult> ExecuteAsync(
        Unit input,
        StepContext context)
    {
        var workflowConfig =
            context.GetWorkflowConfig<FileWorkflowConfig>();

        var targetDirectory = workflowConfig.TargetDirectory;

        ...
    }
}
```

Workflow Configは読み取り専用として扱う。StepがWorkflow Configを書き換えて後続Stepの挙動を変えることはできない。

### 12.9 path値の扱い

Workflow Config内のフォルダやファイルパスは通常のConfig値として扱う。

相対パスの基準は、原則として `workflow.yaml` が存在するディレクトリである。

絶対パスが指定された場合、エンジンはその値をそのまま扱う。

実行時Configで対象フォルダを差し替える場合は、絶対パスを推奨する。

---

## 13. Binding / Config bindの命名規則

YAML keyとC# プロパティ / コンストラクタ パラメータの対応は以下とする。

- YAML keyはキャメルケースを推奨する。
- C# プロパティ / コンストラクタ パラメータはパスカルケースを推奨する。
- Bindingは大文字小文字を区別しないとする。
- キャメルケースとパスカルケースは同一名として扱う。
- スネークケース対応は初期実装では任意とする。
- 不明なkeyは `validation.strictUnknownProperties` がtrueの場合にエラーとする。

例:

```yaml
outputDirectory: ./output
```

は以下にbindできる。

```csharp
public sealed record FileWorkflowConfig(
    string OutputDirectory
);
```

C#レコードのプライマリコンストラクタ引数に対しても同じ規則を適用する。

---

## 14. 検証仕様

### 14.1 基本方針

Message型およびConfig型の検証はエンジン側で行う。

ユーザーStepは、入力MessageおよびConfigが検証済みであることを前提に実装できる。

### 14.2 検証実行タイミング

エンジンは以下のタイミングで検証を行う。

```text
Workflow Config解決後
Workflow開始Input
Flow呼び出し前Input
Flow終了時Output
Step実行前Input
Step実行前Config
Step実行後Output
Control Stepの参照先Flow入出力
```

### 14.3 検証対象

| 対象 | 内容 |
|---|---|
| 型一致 | 期待型と実際の型が一致するか |
| nullability | non-nullable参照型にnullが入っていないか |
| DataAnnotations | `[Required]`, `[Range]`, `[StringLength]`, `[MinLength]` など |
| ネスト型 | 子Message/Configも再帰的に検証する |
| コレクション要素 | `IEnumerable<T>` の各要素を検証する |
| IValidatableObject | 独自検証を実行する |
| 未知プロパティ | YAMLに未知のkeyが含まれていないか |

### 14.4 nullable参照型検証

csxでは `#nullable enable` を推奨する。

エンジンは `NullabilityInfoContext` を用いて、non-nullable参照型にnullが入っていないことを検証する。

`[Required]` とnon-nullable参照型の両方が指定されている場合、どちらか一方でも違反すれば検証失敗とする。

### 14.5 検証設定

```yaml
validation:
  strictUnknownProperties: true
  validateStepInputs: true
  validateStepConfigs: true
  validateStepOutputs: true
  validateFlowInputs: true
  validateFlowOutputs: true
  nullableReferenceTypes: true
```

初期実装の推奨デフォルトは以下とする。

```text
strictUnknownProperties = true
validateStepInputs = true
validateStepConfigs = true
validateStepOutputs = true
validateFlowInputs = true
validateFlowOutputs = true
nullableReferenceTypes = true
```

### 14.6 検証失敗時

Step実行前のInput / Config検証に失敗した場合、Stepは実行しない。

Step実行後のOutput検証に失敗した場合、そのStepは失敗扱いとする。

Workflow Config検証に失敗した場合、Workflowは開始しない。

### 14.7 検証エラー形式

```csharp
public sealed class ValidationError
{
    public string Path { get; init; } = "";
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
}
```

例:

```text
$.OrderId: Required
$.Items[0].Quantity: Range
$.Config.SmtpHost: Required
```

---

## 15. Flow仕様

### 15.1 Flow実行

Flowは以下の順で実行される。

```text
Flow Input検証
  ↓
start Step解決
  ↓
Step Input binding
  ↓
Step Input検証
  ↓
Step Config binding
  ↓
Step Config検証
  ↓
Step実行
  ↓
Step Output検証
  ↓
next解決
  ↓
次Step実行またはend到達
  ↓
Flow Output検証
```

### 15.2 end

`end` は仮想Stepである。

```yaml
next:
  to: end
```

`end` に到達した時点で、直前StepのOutputをFlow Outputとする。

`end` は予約Step IDであり、通常Stepの `id` として使用できない。

### 15.3 Flow Output型検証

Flow定義の `output` と、`end` に到達した直前StepのOutput型は一致しなければならない。

初期実装では完全一致を要求する。

```text
LastStep.OutputType == Flow.OutputType
```

将来的には `IsAssignableFrom` による代入互換を許可してもよい。

### 15.4 Flow接続制約

Step AからStep Bへ接続される場合、以下を満たす必要がある。

```text
StepA.OutputType == StepB.InputType
```

ただし、Step Bに `input.from` が指定されている場合は、`input.from` の評価結果型がStep BのInput型と一致しなければならない。

初期実装では完全一致を要求する。

---

## 16. 入れ子Flow仕様

FlowはStepとして呼び出せる。

```yaml
- id: run-invoice-flow
  flow: create-invoice-flow
  next:
    to: end
```

この定義は内部的には `FlowCallStep<TInput, TOutput>` として扱われる。

型推論規則は以下とする。

```text
FlowCallStep.InputType  = 呼び出し先Flow.InputType
FlowCallStep.OutputType = 呼び出し先Flow.OutputType
```

`input.from` が指定されている場合、binding結果型が呼び出し先Flow.InputTypeと一致しなければならない。

### 16.1 Flow呼び出し制限

循環呼び出しや無限再帰を避けるため、実行制限を持つ。

```yaml
limits:
  maxFlowDepth: 32
```

エンジンはFlow呼び出し深度が制限を超えた場合、実行を失敗させる。

エラーコード:

```text
FLOW_DEPTH_EXCEEDED
```

---

## 17. Control Step仕様

### 17.1 基本方針

If、ForEach、While、Switch、ParallelStepなどは、YAML予約構文ではなく通常Stepとして提供する。

エンジン本体はYAML構文としての制御構文を持たない。

一方で、Built-in Control Stepの型検証には、Stepごとの型推論規則が必要である。Built-in Stepは型推論Descriptorを提供できる。

```csharp
public interface IBuiltInStepTypeResolver
{
    StepTypeInfo ResolveTypes(
        StepDefinition step,
        FlowDefinition currentFlow,
        WorkflowDefinition workflow,
        TypeResolutionContext context);
}

public sealed record StepTypeInfo(
    Type InputType,
    Type? ConfigType,
    Type OutputType);
```

このDescriptorはYAML構文を特別扱いするものではなく、Built-in Stepの型メタデータを解決するための仕組みである。

### 17.2 IfStep

YAML例:

```yaml
- id: branch
  use: Workflow.Control.IfStep
  config:
    provider: yaml
    value:
      condition: current.IsValid == true
      then: accepted-flow
      else: rejected-flow
  next:
    to: end
```

IfStepのConfig型例:

```csharp
public sealed record IfStepConfig(
    string Condition,
    string Then,
    string Else
);
```

型推論規則:

```text
IfStep.InputType  = Stepに渡されるInput型
IfStep.ConfigType = IfStepConfig
IfStep.OutputType = then Flow OutputType
```

型制約:

```text
then Flow InputType  == IfStep InputType
else Flow InputType  == IfStep InputType
then Flow OutputType == else Flow OutputType
IfStep OutputType    == then Flow OutputType
```

初期実装では完全一致を要求する。

IfStepは実行時に `condition` を評価し、trueの場合は `then` Flow、falseの場合は `else` Flowを実行する。実行した子FlowのOutputをIfStepのOutputとして返す。

### 17.3 ForEachStep

YAML例:

```yaml
- id: process-items
  use: Workflow.Control.ForEachStep
  outputType: Workflows.Messages.ProcessItemsResult
  config:
    provider: yaml
    value:
      itemsFrom: current.Items
      body: process-item-flow
      resultProperty: Items
      mode: sequential
  next:
    to: end
```

ForEachStepのConfig型例:

```csharp
public sealed record ForEachStepConfig(
    string ItemsFrom,
    string Body,
    string ResultProperty,
    string Mode = "sequential",
    int? MaxDegreeOfParallelism = null
);
```

body Flow例:

```yaml
- id: process-item-flow
  input: Workflows.Messages.OrderItem
  output: Workflows.Messages.ProcessedItem
  start: process-item
  steps:
    - id: process-item
      script: steps/process-item.csx
      class: ProcessItemStep
      next:
        to: end
```

型推論規則:

```text
ForEachStep.InputType        = Stepに渡されるInput型
ForEachStep.ConfigType       = ForEachStepConfig
itemsFrom評価結果型          = IEnumerable<TItem>
body Flow InputType          == TItem
body Flow OutputType         = TBodyOutput
ForEachStep.OutputType       = YAMLの outputType
```

`outputType` は必須とする。

`resultProperty` は、`outputType` のプロパティのうち、`IEnumerable<TBodyOutput>` または代入互換な型でなければならない。

初期実装では `mode: sequential` のみ必須対応とする。

将来拡張として以下を許可する。

```yaml
mode: parallel
maxDegreeOfParallelism: 4
```

### 17.4 WhileStep

WhileStepは将来拡張として提供する。

```yaml
- id: retry-until-complete
  use: Workflow.Control.WhileStep
  config:
    provider: yaml
    value:
      condition: current.IsCompleted == false
      body: polling-flow
      maxIterations: 10
```

初期実装ではWhileStepは必須対応範囲に含めない。

### 17.5 SwitchStep / ParallelStep / TryCatchStep

以下は将来拡張とする。

- `Workflow.Control.SwitchStep`
- `Workflow.Control.ParallelStep`
- `Workflow.Control.TryCatchStep`

これらを追加する場合も、YAML予約構文ではなく通常Stepとして実装する。

---

## 18. 型検証仕様

### 18.1 Script Step型推論

Script Stepでは、エンジンが継承元から型を推論する。

```csharp
public sealed class MyStep
    : WorkflowStep<MyInput, MyOutput>
```

または、

```csharp
public sealed class MyStep
    : WorkflowStep<MyInput, MyConfig, MyOutput>
```

### 18.2 Built-in Step型推論

Built-in Stepは、登録時に型情報または型推論Descriptorを提供する。

固定型のBuilt-in Stepは登録時に型を持つ。

Control StepのようにYAML定義や参照先Flowから型が決まるStepは、`IBuiltInStepTypeResolver` により型を解決する。

### 18.3 YAML明示型

デバッグ用途として、YAMLにInput / Config / Output型を任意で明示できる。

```yaml
- id: validate-order
  script: steps/validate-order.csx
  class: ValidateOrderStep
  inputType: Workflows.Messages.OrderCreated
  outputType: Workflows.Messages.ValidationResult
```

この場合、エンジンはC#またはBuilt-in Step Descriptorから推論した型とYAMLの型が一致するか検証する。

不一致の場合は `TYPE_MISMATCH` とする。

---

## 19. エラー処理

### 19.1 Step失敗条件

Stepは以下の場合に失敗する。

- csxコンパイルに失敗した。
- Step クラス探索に失敗した。
- Stepインスタンス作成に失敗した。
- input bindingに失敗した。
- input検証に失敗した。
- config bindingに失敗した。
- config検証に失敗した。
- Step実行中に例外が発生した。
- timeoutした。
- output検証に失敗した。
- next解決に失敗した。
- 型不一致が発生した。
- Control Stepの参照先Flow検証に失敗した。

### 19.2 retry

Step単位でretryを指定できる。

```yaml
steps:
  - id: send-mail
    script: steps/send-mail.csx
    class: SendMailStep
    retry:
      maxAttempts: 3
      interval: 00:00:05
```

`maxAttempts` は初回実行を含む回数とする。

retry対象は以下とする。

- Step実行中に発生した一時的例外。
- `WorkflowStepException` のうち `Retryable == true` のもの。
- timeoutはStep設定によりretry対象に含めてもよい。

retry対象外は以下とする。

- YAML 解析エラー
- Workflow schema エラー
- csx コンパイルエラー
- Step クラス未検出
- Type 不一致
- Input binding エラー
- Config binding エラー
- Input検証エラー
- Config検証エラー
- Output検証エラー
- Flow 未検出
- Step 未検出
- Condition 構文エラー

### 19.3 timeout

```yaml
steps:
  - id: send-mail
    script: steps/send-mail.csx
    class: SendMailStep
    timeout: 00:00:30
```

Step timeout時は `StepContext.CancellationToken` をキャンセルし、Stepを失敗扱いにする。

timeoutは協調キャンセルであり、強制停止ではない。

### 19.4 エラーコード

代表的なエラーコードは以下とする。

```text
YAML_PARSE_FAILED
WORKFLOW_SCHEMA_INVALID
SCRIPT_COMPILE_FAILED
SCRIPT_DIRECTIVE_NOT_ALLOWED
SCRIPT_LOAD_FAILED
SCRIPT_LOAD_CYCLE_DETECTED
SCRIPT_REFERENCE_NOT_ALLOWED
SCRIPT_NUGET_RESTORE_FAILED
SCRIPT_ABSTRACTIONS_IDENTITY_MISMATCH
STEP_CLASS_NOT_FOUND
STEP_CREATION_FAILED
FLOW_NOT_FOUND
STEP_NOT_FOUND
DUPLICATE_FLOW_ID
DUPLICATE_STEP_ID
RESERVED_STEP_ID
CONFIG_BINDING_FAILED
INPUT_BINDING_FAILED
CONDITION_EVALUATION_FAILED
CONDITION_SYNTAX_ERROR
NEXT_RESOLUTION_FAILED
MESSAGE_VALIDATION_FAILED
CONFIG_VALIDATION_FAILED
WORKFLOW_CONFIG_VALIDATION_FAILED
WORKFLOW_CONFIG_TYPE_MISMATCH
TYPE_MISMATCH
STEP_EXECUTION_FAILED
STEP_TIMEOUT
STEP_RETRY_EXHAUSTED
FLOW_DEPTH_EXCEEDED
UNSUPPORTED_CONFIG_PROVIDER
UNSUPPORTED_BUILTIN_STEP
CONTROL_STEP_TYPE_RESOLUTION_FAILED
TRACE_SERIALIZATION_FAILED
```

---

## 20. ログ仕様

### 20.1 基本方針

ログはエンジンで独自実装しない。

エンジンは `Microsoft.Extensions.Logging` の `ILogger` / `ILoggerFactory` を利用する。

ログ出力先、フォーマット、永続化、転送は以下のようなlogger providerに委譲する。

```text
Serilog
NLog
log4net
Console Logger
Debug Logger
OpenTelemetry
Application Insights
```

### 20.2 StepContext.Logger

ユーザーStepは `StepContext.Logger` を使ってログを出力する。

```csharp
context.Logger.LogInformation(
    "Processing order. OrderId={OrderId}",
    input.OrderId);
```

### 20.3 構造化ログ

ログは文字列連結ではなく、構造化ログを前提とする。

推奨:

```csharp
logger.LogInformation(
    "Step started. WorkflowId={WorkflowId}, RunId={RunId}, FlowId={FlowId}, StepId={StepId}",
    workflowId,
    runId,
    flowId,
    stepId);
```

非推奨:

```csharp
logger.LogInformation($"Step started. WorkflowId={workflowId}");
```

### 20.4 Log スコープ

Workflow / Flow / Step の文脈は `BeginScope` で付与する。

```csharp
using var workflowScope = logger.BeginScope(new Dictionary<string, object?>
{
    ["WorkflowId"] = workflowId,
    ["RunId"] = runId
});

using var flowScope = logger.BeginScope(new Dictionary<string, object?>
{
    ["FlowId"] = flowId
});

using var stepScope = logger.BeginScope(new Dictionary<string, object?>
{
    ["StepId"] = stepId,
    ["StepClass"] = stepClassName,
    ["Attempt"] = attempt
});
```

### 20.5 エンジン標準ログイベント

```csharp
public static class WorkflowLogEvents
{
    public static readonly EventId WorkflowStarted = new(1000, nameof(WorkflowStarted));
    public static readonly EventId WorkflowCompleted = new(1001, nameof(WorkflowCompleted));
    public static readonly EventId WorkflowFailed = new(1002, nameof(WorkflowFailed));

    public static readonly EventId FlowStarted = new(2000, nameof(FlowStarted));
    public static readonly EventId FlowCompleted = new(2001, nameof(FlowCompleted));
    public static readonly EventId FlowFailed = new(2002, nameof(FlowFailed));

    public static readonly EventId StepStarted = new(3000, nameof(StepStarted));
    public static readonly EventId StepCompleted = new(3001, nameof(StepCompleted));
    public static readonly EventId StepFailed = new(3002, nameof(StepFailed));
    public static readonly EventId StepRetrying = new(3003, nameof(StepRetrying));
    public static readonly EventId StepTimedOut = new(3004, nameof(StepTimedOut));

    public static readonly EventId MessageValidationFailed = new(4000, nameof(MessageValidationFailed));
    public static readonly EventId ConfigValidationFailed = new(4001, nameof(ConfigValidationFailed));
    public static readonly EventId TypeMismatch = new(4002, nameof(TypeMismatch));
}
```

### 20.6 ログ設定

エンジンは `ILoggerFactory` を外部から受け取る。

```csharp
public sealed class WorkflowEngineOptions
{
    public ILoggerFactory LoggerFactory { get; init; }
        = NullLoggerFactory.Instance;
}
```

エンジン本体はSerilog、NLog、log4netなどの具体実装に直接依存しない。

---

## 21. 実行結果とトレース

ログとExecutionTraceは分離する。

| 要素 | 役割 |
|---|---|
| Log | 実行中の観測、障害調査 |
| ExecutionTrace | 実行結果として保存・表示可能な構造化履歴 |

### 21.1 Trace 取得方針

Input / Output / ConfigをTraceに保存するかどうかは明示的な方針で制御する。

```yaml
trace:
  captureInputs: false
  captureOutputs: true
  captureConfigs: false
  maxValueSizeBytes: 32768
  redaction:
    paths:
      - $.password
      - $.token
      - $.smtpHost
      - $.connectionString
```

推奨デフォルトは以下とする。

```text
captureInputs = false
captureOutputs = false
captureConfigs = false
```

理由は以下である。

- 個人情報や秘密情報がTraceに残る可能性がある。
- 巨大オブジェクトによりメモリを圧迫する可能性がある。
- 循環参照によりシリアライズできない可能性がある。
- Stream、ファイルハンドル、外部リソース参照が混ざる可能性がある。

### 21.2 WorkflowResult

```csharp
public sealed class WorkflowResult
{
    public string WorkflowId { get; init; } = "";
    public string RunId { get; init; } = "";
    public WorkflowStatus Status { get; init; }
    public FlowExecutionResult RootFlow { get; init; } = default!;
}
```

### 21.3 FlowExecutionResult

```csharp
public sealed class FlowExecutionResult
{
    public string FlowId { get; init; } = "";
    public WorkflowStatus Status { get; init; }
    public CapturedValue? Input { get; init; }
    public CapturedValue? Output { get; init; }
    public IReadOnlyList<ExecutionNode> Children { get; init; }
        = Array.Empty<ExecutionNode>();
}
```

### 21.4 ExecutionNode

```csharp
public abstract record ExecutionNode;

public sealed record StepExecutionNode(
    string StepId,
    string StepType,
    StepStatus Status,
    CapturedValue? Input,
    CapturedValue? Output,
    TimeSpan Duration,
    int AttemptCount,
    string? ErrorCode,
    string? ErrorMessage
) : ExecutionNode;

public sealed record FlowExecutionNode(
    FlowExecutionResult Flow
) : ExecutionNode;
```

### 21.5 CapturedValue

```csharp
public sealed class CapturedValue
{
    public string TypeName { get; init; } = "";
    public object? Value { get; init; }
    public string? Summary { get; init; }
    public bool Truncated { get; init; }
    public bool Redacted { get; init; }
}
```

Traceに値を保存しない場合でも、`TypeName`、`Summary`、`Duration`、`Status`、`ErrorCode` は記録できる。

---

## 22. エンジン内部構成

### 22.1 プロジェクト構成案

```text
src/
├── Workflow.Abstractions/
│   ├── WorkflowStep.cs
│   ├── StepContext.cs
│   ├── Unit.cs
│   ├── IFlowInvoker.cs
│   └── WorkflowStepException.cs
│
├── Workflow.Engine/
│   ├── WorkflowEngine.cs
│   ├── WorkflowLoader.cs
│   ├── WorkflowSchemaValidator.cs
│   ├── FlowRunner.cs
│   ├── StepExecutor.cs
│   ├── StepRegistry.cs
│   ├── StepTypeResolver.cs
│   ├── Scripting/
│   │   ├── IScriptCompiler.cs
│   │   ├── DotnetScriptCompiler.cs
│   │   ├── ScriptCompileRequest.cs
│   │   ├── CompiledScriptAssembly.cs
│   │   ├── ScriptReferencePolicy.cs
│   │   └── ScriptDirectiveScanner.cs
│   ├── MessageValidator.cs
│   ├── ConfigBinder.cs
│   ├── WorkflowConfigBinder.cs
│   ├── BindingExpressionEvaluator.cs
│   ├── ConditionEvaluator.cs
│   ├── TraceBuilder.cs
│   └── Logging/
│       └── WorkflowLogEvents.cs
│
├── Workflow.ControlSteps/
│   ├── IfStep.cs
│   ├── ForEachStep.cs
│   └── TypeResolvers/
│       ├── IfStepTypeResolver.cs
│       └── ForEachStepTypeResolver.cs
│
├── Workflow.Cli/
│   └── Program.cs
│
└── samples/
    └── order-workflow/
```

### 22.2 StepProvider

Step生成はProviderで抽象化する。

```csharp
public interface IStepProvider
{
    bool CanCreate(StepDefinition definition);

    Task<ICompiledStep> CreateAsync(
        StepDefinition definition,
        StepCompileContext context);
}
```

Provider例:

| Provider | 説明 |
|---|---|
| `ScriptStepProvider` | `script + class` からStepを作る |
| `BuiltInStepProvider` | `use` から登録済みStepを作る |
| `FlowCallStepProvider` | `flow` からFlow呼び出しStepを作る |

### 22.3 Stepインスタンス生成とDI

初期実装では、Script Stepは実行ごとに新規インスタンスを生成する。

Script Stepのコンストラクタは原則として公開された引数なしコンストラクタを要求する。

DI利用は `StepContext.Services` 経由を基本とする。

Built-in StepはDIコンテナから解決できる。

Step実行ごとに `IServiceScope` を作成するかどうかは `WorkflowEngineOptions` で制御する。

`IDisposable` または `IAsyncDisposable` を実装するStepインスタンスは、Step実行終了後に破棄する。

```csharp
public sealed class WorkflowEngineOptions
{
    public IServiceProvider Services { get; init; } = default!;
    public bool CreateServiceScopePerStep { get; init; } = true;
}
```

### 22.4 ScriptCompiler抽象

csxコンパイルは `IScriptCompiler` で抽象化する。

```csharp
public interface IScriptCompiler
{
    Task<CompiledScriptAssembly> CompileAsync(
        ScriptCompileRequest request,
        CancellationToken cancellationToken);
}

public sealed class ScriptCompileRequest
{
    public string WorkflowRoot { get; init; } = "";
    public string WorkflowYamlPath { get; init; } = "";
    public IReadOnlyList<string> SharedScripts { get; init; }
        = Array.Empty<string>();
    public string StepScript { get; init; } = "";
    public ScriptReferencePolicy ReferencePolicy { get; init; } = default!;
    public bool PreferSharedAbstractionsAssembly { get; init; } = true;
}

public sealed class CompiledScriptAssembly
{
    public Assembly Assembly { get; init; } = default!;
    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; }
        = Array.Empty<ScriptDiagnostic>();
    public IReadOnlyList<string> LoadedScripts { get; init; }
        = Array.Empty<string>();
    public IReadOnlyList<string> ResolvedReferences { get; init; }
        = Array.Empty<string>();
    public IReadOnlyList<ResolvedNuGetPackage> ResolvedPackages { get; init; }
        = Array.Empty<ResolvedNuGetPackage>();
}
```

初期実装では `DotnetScriptCompiler` を `IScriptCompiler` の標準実装とする。エンジンの型検証、Step探索、Config binding、検証、retry、timeout、ExecutionTraceは `IScriptCompiler` の外側で実行する。

---

## 23. csxコンパイル仕様

### 23.1 実装方針

初期実装では、csxの依存解決、`#load`、`#r`、インラインNuGet参照、コンパイルキャッシュを簡略化するため、`Dotnet.Script.Core` を利用する。

ただし、エンジンの公開仕様および内部実行モデルは `dotnet-script` 固有APIに直接依存しない。エンジン内部では `IScriptCompiler` を定義し、`DotnetScriptCompiler` はその一実装として扱う。

通常実行経路では、`dotnet script` CLIをStep実行ごとに外部プロセス起動する方式を採用しない。理由は以下である。

```text
- Step classを型として取り出しにくい
- WorkflowStep<TInput, TOutput> の型引数推論が困難になる
- StepContext、ILogger、IFlowInvoker、CancellationTokenを自然に注入できない
- 型付きInput / Config / Outputの受け渡しが複雑になる
- retry、timeout、ExecutionTraceとの統合が粗くなる
- プロセス起動コストがStepごとに発生する
```

`dotnet script` CLIは、開発者向けの単体検証補助やデバッグ補助として利用してよいが、エンジン本体のStep実行経路では利用しない。

### 23.2 scriptOptions

csx参照方針はYAMLの `scriptOptions` で指定できる。

```yaml
scriptOptions:
  engine: dotnet-script-core
  allowLoad: true
  allowAssemblyReferences: true
  allowNuGetReferences: true
  load:
    allowOutsideWorkflowRoot: false
    allowNuGetScriptPackages: false
  references:
    allowedAssemblies:
      - System.Text.Json
    allowedPaths:
      - lib/
  nuget:
    requireExactVersion: true
    allowedPackages:
      - id: CsvHelper
        versions:
          - 33.0.1
    packageSources:
      - https://api.nuget.org/v3/index.json
    restoreLockedMode: false
  cache:
    enabled: true
  assemblyLoadContext:
    isolation: workflow
    shareAbstractions: true
```

| 項目 | 説明 |
|---|---|
| `engine` | 初期実装では `dotnet-script-core` を標準値とする |
| `allowLoad` | `#load` を許可するか |
| `allowAssemblyReferences` | assembly参照の `#r` を許可するか |
| `allowNuGetReferences` | `#r "nuget: ..."` を許可するか |
| `load.allowOutsideWorkflowRoot` | `workflow-root` 外のcsx読み込みを許可するか。初期実装の推奨値は `false` |
| `load.allowNuGetScriptPackages` | `#load "nuget: ..."` を許可するか。初期実装の推奨値は `false` |
| `references.allowedAssemblies` | assembly名による `#r` の許可リスト |
| `references.allowedPaths` | ファイルパスによる `#r` の許可ディレクトリ |
| `nuget.requireExactVersion` | インラインNuGet参照に正確なバージョン指定を要求するか。推奨値は `true` |
| `nuget.allowedPackages` | `#r "nuget: ..."` で参照可能なパッケージIDとバージョン |
| `nuget.packageSources` | 復元に利用できるパッケージソース |
| `nuget.restoreLockedMode` | ロックファイル前提の復元に限定するか |
| `cache.enabled` | 依存キャッシュおよびコンパイルキャッシュを利用するか |
| `assemblyLoadContext.isolation` | `workflow`、`cache`、`none` のいずれか |
| `assemblyLoadContext.shareAbstractions` | `Workflow.Abstractions` をホスト側assemblyとして共有するか |

`scriptOptions` 未指定時の推奨デフォルトは以下とする。

```text
engine = dotnet-script-core
allowLoad = true
allowAssemblyReferences = true
allowNuGetReferences = false
load.allowOutsideWorkflowRoot = false
load.allowNuGetScriptPackages = false
nuget.requireExactVersion = true
cache.enabled = true
assemblyLoadContext.isolation = workflow
assemblyLoadContext.shareAbstractions = true
```

### 23.3 型定義csx

`scripts` に指定されたcsxは、Message型、Config型、補助型を定義するために使用する。

```yaml
scripts:
  - messages/order-messages.csx
  - configs/mail-configs.csx
```

これらは直接の実行対象ではなく、Step csxのコンパイル時に読み込まれる共有csxとして扱う。

`DotnetScriptCompiler` は、Stepごとに合成entry csxを内部生成して、`scripts` とStep csxを `#load` する。

例:

```csharp
#load "messages/order-messages.csx"
#load "configs/mail-configs.csx"
#load "steps/validate-order.csx"
```

合成entry csxはエンジン内部の実装詳細であり、ユーザーは直接作成しない。

### 23.4 Step csx

Step csxは、`WorkflowStep<...>` を継承したStepクラスを含む。

```yaml
- id: validate-order
  script: steps/validate-order.csx
  class: ValidateOrderStep
```

Step csxには型定義、using、名前空間、補助メソッド、Stepクラスを記述できる。

Step csxでは、コンパイル時またはロード時に副作用を起こすトップレベルステートメントを避ける。初期実装では、Step csxのトップレベルの実行可能ステートメントを検証時に警告またはエラーとして扱ってよい。

推奨:

```csharp
#nullable enable
#load "../shared/common.csx"
#r "nuget: CsvHelper, 33.0.1"

using Workflow.Abstractions;
using Workflows.Messages;

public sealed class ValidateOrderStep
    : WorkflowStep<OrderCreated, ValidationResult>
{
    public override Task<ValidationResult> ExecuteAsync(
        OrderCreated input,
        StepContext context)
    {
        ...
    }
}
```

非推奨:

```csharp
Console.WriteLine("This statement may run during script evaluation.");
File.Delete("some-file.txt");
```

### 23.5 `#load` 対応

初期実装では、ローカルファイルの `#load` を対応する。

```csharp
#load "../shared/common.csx"
#load "helpers/formatting.csx"
```

`#load` の解決規則は以下とする。

| 項目 | 規則 |
|---|---|
| 相対パス基準 | `#load` を書いたcsxファイルのディレクトリ |
| パス正規化 | `..`、シンボリックリンクを解決して正規パス化する |
| root制限 | `load.allowOutsideWorkflowRoot: false` の場合、正規パスが `workflow-root` 配下でなければならない |
| 循環読み込み | 検出して `SCRIPT_LOAD_CYCLE_DETECTED` とする |
| 重複読み込み | 同一正規パスは1回だけ読み込む |
| 変更検知 | キャッシュキーに読み込まれた全csxの内容ハッシュを含める |

`#load "nuget: ..."` は `Dotnet.Script.Core` で扱えるが、初期実装の推奨デフォルトでは無効にする。有効化する場合は、`load.allowNuGetScriptPackages: true` とし、`nuget.allowedPackages` にパッケージIDとバージョンを明示する。

### 23.6 `#r` assembly参照

初期実装では、assembly名またはファイルパスによる `#r` を対応する。

```csharp
#r "System.Text.Json"
#r "../lib/custom-helper.dll"
```

`#r` assembly参照の解決規則は以下とする。

| 項目 | 規則 |
|---|---|
| assembly名参照 | `references.allowedAssemblies` に含まれる場合のみ許可する |
| ファイルパス参照 | 正規パスが `references.allowedPaths` のいずれかの配下にある場合のみ許可する |
| workflow-root外参照 | 明示許可がない限り禁止する |
| `Workflow.Abstractions` | ホスト側の同一assemblyを共有する |
| 不許可参照 | `SCRIPT_REFERENCE_NOT_ALLOWED` とする |

`Workflow.Abstractions` は特別扱いする。Script側が別コピーの `Workflow.Abstractions.dll` を読み込むと、型名が同じでもCLR上は別型になり、`WorkflowStep<...>` 継承判定や `StepContext` 受け渡しが壊れるためである。

### 23.7 `#r "nuget: ..."` 対応

初期実装では、`scriptOptions.allowNuGetReferences: true` の場合に限り、インラインNuGet参照を対応する。

```csharp
#r "nuget: CsvHelper, 33.0.1"
```

NuGet参照の規則は以下とする。

| 項目 | 規則 |
|---|---|
| パッケージID | `nuget.allowedPackages` に含まれる必要がある |
| バージョン | `nuget.requireExactVersion: true` の場合、正確なバージョン指定を必須とする |
| 浮動バージョン | 初期実装では禁止する |
| プレリリース | `allowedPackages.versions` に明示された場合のみ許可する |
| パッケージソース | `scriptOptions.nuget.packageSources` または `workflow-root/NuGet.Config` を使う |
| ロックファイル | `restoreLockedMode: true` の場合、ロックファイルに存在しない復元を禁止する |
| 復元キャッシュ | `Dotnet.Script.Core` の依存復元機構を利用してよい |

`#r "nuget: ..."` は実装を大幅に簡略化できる一方で、未信頼コードの攻撃面を広げる。初期実装では信頼済みWorkflowのみを対象とし、パッケージID、バージョン、参照元を明示制御する。

### 23.8 コンパイル順序

`DotnetScriptCompiler` は以下の順でコンパイルする。

```text
1. workflow.yaml と scriptOptions を読み込む
2. Workflow.Abstractions assemblyをホスト側共有assemblyとして登録する
3. scripts に指定された共有csxを列挙する
4. Step csxを列挙する
5. `#load` / `#r` / `#r "nuget: ..."` directiveをscanする
6. ScriptReferencePolicyに基づき参照可否を検証する
7. Stepごとに合成entry csxを生成する
8. Dotnet.Script.Coreで依存解決とコンパイルを行う
9. コンパイル済みassemblyを制御されたAssemblyLoadContextに読み込む
10. Step classを探索する
11. WorkflowStep<...> 継承型から型引数を推論する
12. YAML明示型がある場合は一致検証する
```

`Dotnet.Script.Core` の利用範囲は、依存解決、script 読み込み、NuGet 復元、Roslyn コンパイル、キャッシュ利用に限定する。Step実行、型検証、Config binding、検証、retry、timeout、ExecutionTraceはエンジン側で制御する。

### 23.9 AssemblyLoadContext方針

初期実装では、Workflow単位またはキャッシュ単位でAssemblyLoadContextを分離してよい。

ただし、以下のassemblyはホスト側と共有する。

```text
Workflow.Abstractions
Microsoft.Extensions.Logging.Abstractions
System.ComponentModel.Annotations
```

`Workflow.Abstractions` がScript側で別assemblyとして解決された場合、検証時に失敗させる。

エラーコード:

```text
SCRIPT_ABSTRACTIONS_IDENTITY_MISMATCH
```

`assemblyLoadContext.shareAbstractions: true` は初期実装で必須扱いとしてよい。

### 23.10 コンパイルキャッシュ

エンジンは `Dotnet.Script.Core` の依存キャッシュおよび実行キャッシュ / コンパイルキャッシュを利用してよい。

エンジン独自のキャッシュキーは以下を含む。

```text
workflow.yaml path
workflow.yaml content hash
scriptOptions hash
scripts file path + content hash
step csx file path + content hash
#load された全csx file path + content hash
#r assembly参照一覧
#r file path + content hash
#r nuget package ID + version + source
Workflow.Abstractions assembly identity
engine version
schemaVersion
```

ファイル更新、参照更新、package version変更、`scriptOptions` 変更が検知された場合、該当キャッシュは無効化する。

### 23.11 csx信頼境界

初期実装では、csxは信頼済みWorkflowのみを実行対象とする。

未信頼ユーザーがアップロードしたcsxを、このエンジンで直接実行してはならない。

初期実装では以下を提供しない。

- 完全サンドボックス
- プロセス分離
- OS権限制御
- ネットワーク制限
- ファイルI/O制限
- NuGet利用制限の完全強制
- 署名検証
- シークレットアクセス制限

csxは任意のC#コードであり、ファイルI/O、ネットワークアクセス、環境変数参照、プロセス起動、リフレクション、秘密情報アクセスを行える可能性がある。

`Dotnet.Script.Core` により `#load` と `#r` の対応は簡略化されるが、セキュリティ境界は提供されない。エンジンはディレクティブ走査と許可一覧で参照方針を検証するが、未信頼コード実行を安全化するものではない。

### 23.12 csx参照方針まとめ

| 項目 | 初期実装方針 |
|---|---|
| `#load "relative.csx"` | 対応。workflow-root配下のみ許可 |
| `#load "nuget: ..."` | デフォルト無効。明示許可時のみ対応 |
| `#r "AssemblyName"` | 許可一覧に登録されたアセンブリのみ許可 |
| `#r "path/to.dll"` | 許可一覧に登録されたパス配下のみ許可 |
| `#r "nuget: Package, Version"` | 明示許可一覧、正確なバージョン指定、許可済み参照元の場合のみ対応 |
| NuGet 復元 | `Dotnet.Script.Core` の仕組みを利用 |
| workflow-root外ファイル参照 | 原則禁止 |
| AssemblyLoadContext | Workflow単位またはキャッシュ単位で分離し、Abstractionsは共有 |
| アンロード | 可能な範囲でAssemblyLoadContextのアンロードを行う |

---

## 24. CLI仕様

### 24.1 実行

```bash
workflow run workflow.yaml --input input.yaml
workflow run workflow.yaml --input input.yaml --config run-config.yaml
workflow run workflow.yaml --input input.yaml --config run-config.yaml --config-override targetDirectory=/tmp/job-001
```

### 24.2 検証

```bash
workflow validate workflow.yaml
workflow validate workflow.yaml --config run-config.yaml
```

検証対象:

- YAML構文
- Workflow schema
- Flow参照
- Step参照
- `end` 予約ID違反
- 重複Flow ID
- 重複Step ID
- csxディレクティブ走査
- `#load` 参照解決
- `#r` assembly参照解決
- `#r "nuget: ..."` 参照解決
- csxコンパイル
- Step クラス探索
- 型整合性
- Built-in Step型推論
- Control Step参照先Flow整合性
- Step Config bind可能性
- Workflow Config bind可能性
- Message検証可能性
- Workflow Config検証可能性
- retry / timeout形式
- trace / 検証 / 制限設定形式
- scriptOptions設定形式

### 24.3 Flow一覧

```bash
workflow flows workflow.yaml
```

### 24.4 Step一覧

```bash
workflow steps workflow.yaml
```

### 24.5 型情報表示

```bash
workflow types workflow.yaml
workflow types workflow.yaml --flow main
```

表示内容:

- Flow Input / Output型
- Step Input / Config / Output型
- Built-in Step型推論結果
- YAML明示型との一致結果

---

## 25. 実装範囲

### 25.1 MVP範囲

MVPで対応する範囲は以下とする。

- workflow.yaml読み込み
- schemaVersion 1
- Flow定義
- 単一路線Flow実行
- `next.to` 0または1件
- `end` 仮想Step
- Script Step
- Flow Call Step
- `Dotnet.Script.Core` を使ったcsxコンパイル
- ローカルファイル `#load`
- 許可一覧に登録されたassembly / ファイルパス `#r`
- 許可一覧に登録された `#r "nuget: Package, Version"`
- Message型csx定義
- Config型csx定義
- `WorkflowStep<TInput, TOutput>`
- `WorkflowStep<TInput, TConfig, TOutput>`
- YAML config
- Workflow Config
- YAML既定値 + 実行時Workflow Config merge
- Step input binding
- Step Config binding
- Message検証
- Config検証
- Workflow Config検証
- 型整合性チェック
- timeout
- `Microsoft.Extensions.Logging` 統合
- CLI `validate` コマンド
- CLI run
- scriptOptions検証

### 25.2 次フェーズ範囲

次フェーズで対応する範囲は以下とする。

- Built-in IfStep
- Built-in ForEachStep sequential
- YAML / Message / Workflow Config merge
- retry
- ExecutionTrace
- trace 秘匿化
- `#load "nuget: ..."` スクリプトパッケージ対応
- NuGet ロックファイル / ロック済み復元強制
- CLI `flows` コマンド
- CLI `steps` コマンド
- CLI `types` コマンド

### 25.3 将来拡張

以下は将来拡張とする。

- 並列ForEach
- ParallelStep
- TryCatchStep
- SwitchStep
- WhileStep
- DAG実行
- 複数next
- エッジ条件
- fan-out / 合流
- 分散実行
- 永続化されたWorkflow再開
- 外部キュー連携
- Web UI
- スケジューラ
- message型の自動生成
- 高度なサンドボックス
- 署名検証
- 無制限の NuGet パッケージ復元

---

## 26. 使用ライブラリ候補

| 用途 | 候補 |
|---|---|
| YAML読み込み | `YamlDotNet` |
| csxコンパイル | `Dotnet.Script.Core`（標準実装） / `Microsoft.CodeAnalysis.CSharp.Scripting` / Roslyn（代替実装） |
| csx依存解決 / NuGet 復元 | `Dotnet.Script.DependencyModel` / `Dotnet.Script.DependencyModel.NuGet` |
| Logging抽象化 | `Microsoft.Extensions.Logging` |
| Logging実装 | Serilog / NLog / log4net / OpenTelemetry |
| DI | `Microsoft.Extensions.DependencyInjection` |
| Options | `Microsoft.Extensions.Options` |
| Validation | `System.ComponentModel.DataAnnotations` |
| Nullable メタデータ | `System.Reflection.NullabilityInfoContext` |
| Trace シリアライズ | `System.Text.Json` |

---

## 27. サンプル全体

### 27.1 workflow.yaml

```yaml
id: order-workflow
schemaVersion: 1
version: 1.0.0

scripts:
  - messages/order-messages.csx

scriptOptions:
  engine: dotnet-script-core
  allowLoad: true
  allowAssemblyReferences: true
  allowNuGetReferences: true
  load:
    allowOutsideWorkflowRoot: false
  references:
    allowedAssemblies:
      - System.Text.Json
    allowedPaths:
      - lib/
  nuget:
    requireExactVersion: true
    allowedPackages:
      - id: CsvHelper
        versions:
          - 33.0.1
    packageSources:
      - https://api.nuget.org/v3/index.json

entryFlow: main

config:
  type: Workflows.Messages.FileWorkflowConfig
  value:
    targetDirectory: ./input
    outputDirectory: ./output
    temporaryDirectory: ./tmp
  runtime:
    merge: deep
    precedence: runtime
    nullOverride: false

validation:
  strictUnknownProperties: true
  validateStepInputs: true
  validateStepConfigs: true
  validateStepOutputs: true
  validateFlowInputs: true
  validateFlowOutputs: true
  nullableReferenceTypes: true

trace:
  captureInputs: false
  captureOutputs: false
  captureConfigs: false
  maxValueSizeBytes: 32768
  redaction:
    paths:
      - $.password
      - $.token
      - $.connectionString

limits:
  maxFlowDepth: 32

flows:
  - id: main
    input: Workflows.Messages.OrderCreated
    output: Workflows.Messages.OrderProcessResult
    start: validate-order

    steps:
      - id: validate-order
        script: steps/validate-order.csx
        class: ValidateOrderStep
        next:
          to: branch

      - id: branch
        use: Workflow.Control.IfStep
        config:
          provider: yaml
          value:
            condition: current.IsValid == true
            then: accepted-flow
            else: rejected-flow
        next:
          to: end

  - id: accepted-flow
    input: Workflows.Messages.ValidationResult
    output: Workflows.Messages.OrderProcessResult
    start: accepted

    steps:
      - id: accepted
        script: steps/accepted.csx
        class: AcceptedStep
        next:
          to: end

  - id: rejected-flow
    input: Workflows.Messages.ValidationResult
    output: Workflows.Messages.OrderProcessResult
    start: rejected

    steps:
      - id: rejected
        script: steps/rejected.csx
        class: RejectedStep
        next:
          to: end
```

### 27.2 messages/order-messages.csx

```csharp
#nullable enable

using System.ComponentModel.DataAnnotations;

namespace Workflows.Messages;

public sealed record FileWorkflowConfig(
    [Required]
    string TargetDirectory,

    [Required]
    string OutputDirectory,

    string? TemporaryDirectory
);

public sealed record OrderItem(
    [Required]
    string ItemId,

    [Range(1, int.MaxValue)]
    int Quantity
);

public sealed record OrderCreated(
    [Required]
    string OrderId,

    [Required]
    string CustomerId,

    [MinLength(1)]
    IReadOnlyList<OrderItem> Items,

    [Range(0.01, double.MaxValue)]
    decimal Amount
);

public sealed record ValidationResult(
    bool IsValid,
    string? Reason
);

public sealed record OrderProcessResult(
    bool Success,
    string Message
);
```

### 27.3 steps/validate-order.csx

```csharp
#nullable enable

#load "../shared/common.csx"
#r "nuget: CsvHelper, 33.0.1"

using Workflow.Abstractions;
using Workflows.Messages;

public sealed class ValidateOrderStep
    : WorkflowStep<OrderCreated, ValidationResult>
{
    public override Task<ValidationResult> ExecuteAsync(
        OrderCreated input,
        StepContext context)
    {
        context.Logger.LogInformation(
            "Validating order. OrderId={OrderId}",
            input.OrderId);

        if (input.Amount <= 0)
        {
            return Task.FromResult(
                new ValidationResult(false, "Amount must be greater than zero."));
        }

        if (input.Items.Count == 0)
        {
            return Task.FromResult(
                new ValidationResult(false, "Order must have at least one item."));
        }

        return Task.FromResult(
            new ValidationResult(true, null));
    }
}
```

### 27.4 steps/accepted.csx

```csharp
#nullable enable

using Workflow.Abstractions;
using Workflows.Messages;

public sealed class AcceptedStep
    : WorkflowStep<ValidationResult, OrderProcessResult>
{
    public override Task<OrderProcessResult> ExecuteAsync(
        ValidationResult input,
        StepContext context)
    {
        return Task.FromResult(
            new OrderProcessResult(true, "Order accepted."));
    }
}
```

### 27.5 steps/rejected.csx

```csharp
#nullable enable

using Workflow.Abstractions;
using Workflows.Messages;

public sealed class RejectedStep
    : WorkflowStep<ValidationResult, OrderProcessResult>
{
    public override Task<OrderProcessResult> ExecuteAsync(
        ValidationResult input,
        StepContext context)
    {
        return Task.FromResult(
            new OrderProcessResult(false, input.Reason ?? "Order rejected."));
    }
}
```

---

## 28. 最終方針

本エンジンの仕様は以下に集約される。

```text
Workflow:
  複数Flowを持つ実行定義
  Workflow ConfigをRun単位の不変snapshotとして持つ

Flow:
  型付きのStep集合
  Input型とOutput型を持つ
  Stepから呼び出し可能
  初期実装では単一路線実行

Step:
  すべての処理単位
  通常処理も制御処理も同じ扱い
  C#ジェネリックでInput / Config / Output型を明示

Message:
  csxでrecord/classとして定義
  検証はエンジン側で実行

Config:
  csxで型定義
  Workflow ConfigとStep Configを区別
  Workflow ConfigはYAML既定値、実行時Config、Overrideから供給
  Step ConfigはYAML、Message、Workflow Config、またはmergeから供給
  検証はエンジン側で実行

Binding:
  flowInput / previousOutput / current / workflowConfig / variables を明確に区別
  曖昧な input 識別子は使用しない

Control Step:
  YAML予約構文ではなく通常Stepとして提供
  IfStepやForEachStepは型推論Descriptorを持つ

Scripting:
  Dotnet.Script.CoreをIScriptCompilerの標準実装として利用
  #load / #r / #r nuget はscriptOptionsとallowlistで制御
  dotnet script CLIの外部プロセス起動は通常実行経路では使わない

Log:
  Microsoft.Extensions.Loggingを利用
  出力先・永続化・転送はlogger providerに委譲

Trace:
  ログとは別の構造化実行履歴
  Input / Output / Configの保存はpolicyで明示制御

Security:
  初期実装では信頼済みWorkflowのみを実行対象とする
  csxの完全サンドボックスは提供しない
  Dotnet.Script.Coreは依存解決とコンパイルを簡略化するが、セキュリティ境界ではない
```
