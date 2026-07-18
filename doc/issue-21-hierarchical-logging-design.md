# Issue #21 階層ログ設計

## 1. 概要

Issue #21「logの改良」では、ログから現在実行中の Step 名を判別できること、およびネストした実行では階層構造を判別できることが求められている。

本設計では、ログメッセージ本文へ Step 名を個別に埋め込むのではなく、`ILogger.BeginScope` の scope chain をワークフローの実行階層として扱う。
CLI の `EngineLoggingProvider` は scope chain から実行コンテキストを組み立て、Text ログでは表示用パス、JSON ログでは構造化フィールドとして出力する。

関連 Issue: #21

## 2. 背景

現在の workflow 実行経路では、ルート Entry と各 Step の実行時に logger scope が作成される。
Step scope には `EntryName`、`StepName`、`Attempt` が設定されているが、CLI の logger provider は `EntryName` だけを参照しており、通常のログ行には Step 名が表示されない。

また、ネストした `CompositeStep` が `Execute` / `ExecuteAsync` で実行される経路では、内側 Composite およびその子 Step を表す scope が作成されない。
このため、外側 Step から内側 Composite を呼び出した場合、ログから次の関係を復元できない。

```text
Main
└─ RunTextPipelineStep
   └─ TextPipeline
      ├─ LoadTextStep
      ├─ ParseDocumentStep
      └─ BuildReportStep
```

ログメッセージを次のように変更するだけでは、Step 本体が出力したログやネストした Composite のログを一貫して扱えない。

```text
Step started
↓
LoadTextStep started
```

そのため、実行階層を scope として保持し、出力形式ごとに formatter が投影する方式を採用する。

## 3. 目的

本対応の目的は次のとおり。

1. engine が出力する Step lifecycle ログから、対象 Step 名を判別できること。
2. Step 本体が `StepContext.Logger` へ出力したログにも、実行中の Step 名が付与されること。
3. ネストした `CompositeStep`、`If`、`Switch` の親子関係をログから判別できること。
4. retry 中のログから試行回数を判別できること。
5. Text と JSON の両形式で同じ実行コンテキストを利用すること。
6. 公開 workflow API、`WorkflowResult`、`ExecutionTrace` の契約を変更しないこと。

## 4. 対象外

次の項目は本対応の対象外とする。

- `ExecutionTrace` を木構造へ変更すること。
- Step の並列実行を追加すること。
- 分散トレーシングや OpenTelemetry の span を導入すること。
- ログレベル設定や category filter の仕様変更。
- Step の表示名を変更する新しい公開 API の追加。
- ユーザーが Step 本体で作成した任意の logger provider に、CLI 固有の表示形式を強制すること。

## 5. 設計方針

### 5.1 scope を実行階層の唯一の情報源にする

ログ本文は、状態を表す短いメッセージのまま維持する。
Step 名、Entry 名、branch 名、attempt は logger scope に保持する。

```text
Message: Step started
Scope:   Entry=Main, Step=LoadTextStep, Attempt=1
```

これにより、Text formatter と JSON formatter が同じ構造化情報を利用できる。
また、Step 本体が出力する任意のメッセージにも、現在の scope が自動的に適用される。

### 5.2 scope の順序を階層順序として扱う

scope は外側から内側へ積み重ねる。
formatter は scope chain を古い scope から新しい scopeの順に走査し、実行パスを構成する。

```text
EntryName=Main
StepName=RunTextPipelineStep
CompositeName=TextPipeline
StepName=LoadTextStep
```

上記から次の実行パスを構成する。

```text
Main > RunTextPipelineStep > TextPipeline > LoadTextStep
```

### 5.3 公開 API を変更しない

本対応で追加する scope key、scope snapshot、branch metadata、logging context は engine/CLI 内部型とする。

次の公開型は変更しない。

- `CompositeStep<TOut>` の公開メソッド
- `StepContext`
- `WorkflowExecutionOptions`
- `WorkflowResult`
- `ExecutionTrace`
- `ExecutionTraceStep`

## 6. scope モデル

### 6.1 scope key

以下の key を使用する。

| Key | 型 | 用途 |
|---|---|---|
| `EntryName` | `string` | ルート Entry の完全修飾名 |
| `CompositeName` | `string` | ネストして実行中の Composite 名 |
| `StepName` | `string` | 現在実行中の通常 Step、Lambda Step、`If`、`Switch` 名 |
| `BranchName` | `string` | 選択された `then`、`else`、`case=...`、`default` |
| `Attempt` | `int` | 現在の Step 試行番号 |

ルート Entry scope では `EntryName` だけを設定する。
`Attempt` は retry 対象である Step scope にだけ設定し、Entry scope には設定しない。

### 6.2 実行パスへ含める key

`ExecutionPath` は次の key の値を scope 順に連結して構成する。

1. `EntryName`
2. `CompositeName`
3. `StepName`
4. `BranchName`

`Attempt` は階層ではないため、パスへ含めず独立フィールドとして出力する。

### 6.3 重複値の扱い

移行中に同じ `EntryName` が複数 scope に存在しても、実行パスには最外側の `EntryName` を一度だけ追加する。
新しい実装では Step scope への `EntryName` の重複設定をやめる。

同名 Step が階層内に複数存在する場合は省略しない。
階層上の位置が異なるため、同じ文字列が連続してもそのまま出力する。

## 7. lifecycle と scope の構成

### 7.1 ルート Entry

`ExecuteWorkflowAsync` の開始時に Entry scope を作成し、workflow 全体を囲む。

```csharp
using IDisposable? entryScope = engineLogger.BeginScope(
    new Dictionary<string, object?>
    {
        ["EntryName"] = QualifiedName,
    });
```

出力イベントは現行の意味を維持する。

```text
Entry started
Entry succeeded
Entry failed ...
```

### 7.2 通常 Step

各 attempt の開始前に Step scope を作成する。
Step 本体、成功後の producer、失敗変換が同じ scope 内になるようにする。

```csharp
using IDisposable? stepScope = engineLogger.BeginScope(
    new Dictionary<string, object?>
    {
        ["StepName"] = step.Name,
        ["Attempt"] = attempt,
    });
```

Step 本体が `StepContext.Logger` へ出力するログにも、この scope chain が適用される。

lifecycle message は次を維持する。

```text
Step started for attempt {Attempt}
Step succeeded on attempt {Attempt}
Step skipped on attempt {Attempt}
Step failed after attempt {Attempt} with error code {ErrorCode}
```

Text formatter が `StepName` と `Attempt` を表示するため、message 本文へ Step 名を重複して追加しない。

### 7.3 ネストした CompositeStep

`CompositeStep<TOut>.ExecuteAsync(StepInput, CancellationToken)` は、内側の Step 列を実行する前に `CompositeName` scope を作成する。

```csharp
using IDisposable? compositeScope = input.Context.Logger.BeginScope(
    new Dictionary<string, object?>
    {
        ["CompositeName"] = QualifiedName,
    });
```

`ExecuteSimpleStepSequenceAsync` でも各 Step の `StepName` scope を作成する。
これにより、内側 Step が出力したログへ完全な階層が付与される。

ネスト経路では、Step 本体がログを出さない場合でも実行状況を確認できるよう、次の lifecycle ログを出力する。

```text
Composite started
Step started
Step succeeded / Step skipped / Step failed
Composite succeeded / Composite failed
```

ルート workflow の `Entry started` / `Entry succeeded` と二重にならないよう、`ExecuteWorkflowAsync` 自身は `Composite started` を出力しない。

ネスト経路の lifecycle logger は次の優先順位で解決する。

1. ルート workflow が `StepContext` に登録した engine 内部 logging context の engine logger。
2. logging context がない standalone 実行では `StepContext.Logger`。

内部 logging context は engine assembly 内部型として保持し、公開 API には追加しない。

### 7.4 If

`If` 制御単位の scope を作成した状態で条件を評価する。
branch 選択後、子 Step 列を実行する間だけ `BranchName` scope を追加する。

```text
Main > DocumentLength > then > KeepDetailedDocument
Main > DocumentLength > else > MarkShortDocument
```

branch 名は固定値とする。

```text
then
else
```

未選択 branch の scope および lifecycle ログは出力しない。

### 7.5 Switch

`Switch` 制御単位の scope を作成した状態で selector を評価する。
選択された branch に応じて、次の `BranchName` を使用する。

```text
case=Guide
case=Reference
default
```

case の表示値は branch 定義時の値から生成し、次の制約を適用する。

- `null` は `null` と表示する。
- `IFormattable` は invariant culture で文字列化する。
- その他は `ToString()` を使用する。
- 改行および制御文字は空白へ置換する。
- 最大 128 文字に制限する。

case 値の文字列化に失敗した場合は `case=<unavailable>` を使用し、workflow 定義自体を失敗させない。

### 7.6 retry

retry では execution path は変えず、`Attempt` だけを更新する。

```text
ExecutionPath: Main > FetchStep
Attempt: 1

ExecutionPath: Main > FetchStep
Attempt: 2
```

失敗した attempt の scope を dispose してから次の attempt scope を開始し、attempt が後続ログへ漏れないようにする。

### 7.7 timeout、cancellation、例外

失敗ログおよび例外ログは、成功時と同じ Step scope 内で出力する。
そのため、次のログにも `ExecutionPath` と `Attempt` が付与される。

- Step 例外
- producer 例外
- condition 評価例外
- Switch selector 例外
- timeout
- 外部 cancellation

Entry 失敗ログは、失敗した Step scope 内で出力する現行動作を維持する。
これにより、Entry の失敗原因となった Step を同じログ行から確認できる。

## 8. branch metadata の変更

現在の branch 実行計画へ表示用 branch 名を追加する。

```csharp
internal sealed record BranchExecutionPlan(
    IReadOnlyList<StepRegistration> Steps,
    int StartStepIndex,
    string BranchName);
```

`If` では `then` または `else`、`Switch` では `case=...` または `default` を設定する。

この変更は engine 内部型だけに閉じる。
Step config の flatten index および branch 選択規則は変更しない。

## 9. CLI scope snapshot

### 9.1 snapshot 型

CLI provider は現在の scope chain から、1 回のログ出力に必要な値を snapshot として構成する。

```csharp
internal sealed record EngineLogScopeSnapshot(
    string? EntryName,
    string? StepName,
    string? BranchName,
    int? Attempt,
    IReadOnlyList<string> ExecutionPath);
```

`StepName` は scope chain 内の最も内側の `StepName` とする。
`EntryName` は最も外側の有効な `EntryName` とする。
`BranchName` は最も内側の有効な `BranchName` とする。

### 9.2 scope chain の走査

`EngineLoggingScopeState` は `AsyncLocal` 上の linked list を保持している。
現在 node から親へ走査した後、順序を反転して外側から内側の順に処理する。

処理手順は次のとおり。

1. current node から root node までを一時配列へ格納する。
2. 配列を逆順に走査する。
3. 各 scope state が持つ key/value を読み取る。
4. `ExecutionPath` を構築する。
5. leaf の `StepName`、`BranchName`、`Attempt` を更新する。

scope state が対応する key/value collection でない場合は無視する。

### 9.3 scope の復元

`BeginScope` が返す `IDisposable` は、作成時点の親 node snapshot を保持する。
`Dispose` 時に current node を snapshot へ戻す現行方式を維持する。

次をテストで保証する。

- 直前の Step scope が次の兄弟 Step へ漏れない。
- 選択された branch scope が分岐後の Step へ漏れない。
- nested Composite scope が外側 Composite の後続 Step へ漏れない。
- 別 workflow 実行へ scope が漏れない。

## 10. Text ログ形式

### 10.1 基本形式

現行形式の category と message の間に、execution path と attempt を追加する。

```text
[HH:mm:ss] [Level] Category [ExecutionPath] [attempt=N] Message
```

execution path が存在しない provider 内部ログでは path 部分を省略する。
`Attempt` が存在しない Entry lifecycle ログでは attempt 部分を省略する。

### 10.2 出力例

ルート Entry:

```text
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main] Entry started
```

通常 Step:

```text
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main > MainStep] [attempt=1] Step started for attempt 1
[12:00:00] [Information] Devo6.WorkFlow.Step [Main > MainStep] [attempt=1] Loading input file
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main > MainStep] [attempt=1] Step succeeded on attempt 1
```

ネストした Composite:

```text
[12:00:00] [Information] Devo6.WorkFlow.Step [Main > RunTextPipelineStep > TextPipeline] Composite started
[12:00:00] [Information] Devo6.WorkFlow.Step [Main > RunTextPipelineStep > TextPipeline > LoadTextStep] Step started
[12:00:00] [Information] Devo6.WorkFlow.Step [Main > RunTextPipelineStep > TextPipeline > LoadTextStep] Loading source text from input/source.txt
```

If branch:

```text
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main > DocumentLength] If condition started
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main > DocumentLength > then > KeepDetailedDocument] [attempt=1] Step started for attempt 1
```

Switch branch:

```text
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main > ReportRoute > case=Guide > UseGuideReport] [attempt=1] Step started for attempt 1
```

retry:

```text
[12:00:00] [Warning] Devo6.WorkFlow.Engine [Main > FetchStep] [attempt=1] Step attempt 1 failed with error code STEP_EXECUTION_FAILED; retrying
[12:00:01] [Information] Devo6.WorkFlow.Engine [Main > FetchStep] [attempt=2] Step started for attempt 2
```

### 10.3 互換性

現行の timestamp、level、category、message の順序は維持し、その間へ追加情報を挿入する。
既存テストのように message の包含を検証する利用方法は継続して動作する。

ログ行全体を固定文字列として解析している利用者には形式変更となるため、リリースノートへ記載する。

## 11. JSON ログ形式

### 11.1 追加フィールド

既存フィールドを維持し、次を追加する。

```json
{
  "Timestamp": "2026-07-18T12:00:00.0000000Z",
  "Level": "Information",
  "Category": "Devo6.WorkFlow.Step",
  "EntryName": "Main",
  "StepName": "LoadTextStep",
  "BranchName": null,
  "Attempt": 1,
  "ExecutionPath": [
    "Main",
    "RunTextPipelineStep",
    "TextPipeline",
    "LoadTextStep"
  ],
  "Message": "Loading source text from input/source.txt",
  "Exception": null
}
```

### 11.2 null の扱い

該当する scope が存在しない場合も、JSON field は省略せず `null` を出力する。
`ExecutionPath` は scope がない場合に空配列を出力する。

固定 schema にすることで、ログ収集側の query を単純化する。

### 11.3 互換性

既存の次の field は名称と意味を維持する。

- `Timestamp`
- `Level`
- `Category`
- `Message`
- `Exception`

新しい field の追加だけを行う。
厳密な unknown-field 拒否を行う consumer には影響する可能性があるため、リリースノートへ記載する。

## 12. logger category

category は現行の区分を維持する。

| Category | 用途 |
|---|---|
| `Devo6.WorkFlow.Engine` | Entry、Step lifecycle、retry、timeout、branch 制御 |
| `Devo6.WorkFlow.Step` | Step 本体が `StepContext.Logger` へ出力するログ |

nested Composite の lifecycle は、ルート workflow の内部 logging context を取得できる場合は `Devo6.WorkFlow.Engine` を使用する。
standalone `Execute` / `ExecuteAsync` では `StepContext.Logger` を使用する。

## 13. ログファイル名

`Logging.File.NameFormat` の `{RootStepName}` は、常に最外側の `EntryName` を使用する。

```text
Main > RunTextPipelineStep > TextPipeline > LoadTextStep
```

上記の実行中も `{RootStepName}` は `Main` のままとする。
`CompositeName` や leaf `StepName` によって、実行途中で出力ファイルが切り替わってはならない。

## 14. 変更対象

主な変更対象は次のとおり。

### `src/Devo6.WorkFlow.Engine/CompositeStep.cs`

- Entry scope から不要な `Attempt` を除去する。
- 通常 Step scope から重複した `EntryName` を除去する。
- nested Composite の `CompositeName` scope を追加する。
- simple execution 経路へ Step lifecycle と `StepName` scope を追加する。
- `If` / `Switch` の branch scope を追加する。
- `BranchExecutionPlan` へ `BranchName` を追加する。
- root workflow 用の内部 logging context を `StepContext` へ登録する。

### `src/Devo6.WorkFlow.Cli/EngineLoggingProvider.cs`

- `EntryName` 単独取得を scope snapshot 取得へ置き換える。
- Text formatter へ execution path と attempt を追加する。
- JSON formatter へ構造化 scope field を追加する。
- `{RootStepName}` は snapshot の最外側 `EntryName` から解決する。

### `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`

- engine lifecycle scope と Step 本体ログの scope 伝播を検証する。
- retry、失敗、cancellation 時の scope を検証する。

### `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`

- Text ログの単純 Step path を検証する。
- nested Composite path を検証する。
- `If` / `Switch` branch path を検証する。
- JSON field を検証する。
- `{RootStepName}` の互換性を検証する。

### `samples/multi-folder-composite/README.md`

- 新しい Text ログ例を追加する。
- nested `TextPipeline` の表示例を追加する。

## 15. テスト計画

### 15.1 単純な workflow

次の path が Entry、Step lifecycle、Step 本体ログへ出ることを確認する。

```text
Main > MainStep
```

### 15.2 Lambda Step

API に渡した名前が leaf Step 名として表示されることを確認する。

```text
Main > NormalizeText
```

### 15.3 nested Composite

次の完全な path が表示されることを確認する。

```text
Main > RunTextPipelineStep > TextPipeline > LoadTextStep
```

### 15.4 If

true の場合は `then`、false の場合は `else` だけが表示されることを確認する。
未選択 branch の Step 名がログへ出ないことも確認する。

### 15.5 Switch

一致 case と default の両方を検証する。
case 値の制御文字除去と長さ制限も unit test で確認する。

### 15.6 retry

各 attempt で同じ path と異なる attempt が出ることを確認する。
成功後の後続 Step に attempt が漏れないことを確認する。

### 15.7 timeout と cancellation

失敗ログに leaf Step path と attempt が付くことを確認する。

### 15.8 scope leak

兄弟 Step、branch 後続 Step、nested Composite 後続 Step、別 workflow 実行で scope が残らないことを確認する。

### 15.9 JSON

JSON を deserialize し、文字列包含ではなく field の型と値を検証する。

```text
EntryName      string or null
StepName       string or null
BranchName     string or null
Attempt        number or null
ExecutionPath  array
```

### 15.10 file name

nested Composite 実行中に最初のファイルが作られても、ファイル名が root Entry の `Main` を使用することを確認する。

## 16. 受け入れ条件

次をすべて満たした場合に Issue #21 の実装を完了とする。

1. lifecycle ログから実行中の Step 名を確認できる。
2. Step 本体ログから実行中の Step 名を確認できる。
3. nested Composite の親子関係を Text ログで確認できる。
4. `If` / `Switch` の選択 branch をログで確認できる。
5. retry の attempt をログで確認できる。
6. JSON ログに `EntryName`、`StepName`、`BranchName`、`Attempt`、`ExecutionPath` が含まれる。
7. `{RootStepName}` の既存動作が維持される。
8. scope が兄弟 Step や別実行へ漏れない。
9. 既存の `WorkflowResult` と `ExecutionTrace` の公開契約が変わらない。
10. 既存テストと追加テストがすべて成功する。

## 17. 実装順序

実装は次の順序で行う。

1. branch metadata と scope key を追加する。
2. root workflow と通常 Step の scope を整理する。
3. nested Composite と simple execution の scope を追加する。
4. `If` / `Switch` の branch scope を追加する。
5. CLI scope snapshot を実装する。
6. Text formatter を更新する。
7. JSON formatter を更新する。
8. engine contract test を追加する。
9. CLI integration test を追加する。
10. sample README とリリース向け説明を更新する。

## 18. リスクと対策

### scope の dispose 漏れ

`using` で scope lifetime を限定し、例外経路を含めて必ず復元する。
scope leak 専用テストを追加する。

### Text ログ形式の変更

既存 field と message は維持し、追加情報だけを挿入する。
変更内容をリリースノートへ記載する。

### JSON consumer の strict schema

既存 field は変更せず追加だけにする。
新規 field をリリースノートへ明記する。

### case 値による巨大または不正な表示

制御文字除去、長さ制限、文字列化失敗時 fallback を実装する。

### nested Composite の lifecycle category

root workflow から内部 logging context を渡し、可能な限り engine category を使用する。
standalone 実行だけは `StepContext.Logger` へ fallback する。

## 19. 将来拡張

本設計の scope model は、将来次の機能へ拡張できる。
ただし本 Issue では実装しない。

- `ExecutionTrace` への execution path 追加。
- OpenTelemetry span への scope 変換。
- path 単位のログ filter。
- Step ごとの duration を JSON field として追加。
- workflow run ID、correlation ID の追加。
