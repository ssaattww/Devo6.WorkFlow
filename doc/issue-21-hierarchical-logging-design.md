# 課題 #21 階層ログ設計

## 1. 概要

課題 #21「ログの改良」では、ログから現在実行中の Step 名を判別できること、およびネストした実行では階層構造を判別できることが求められている。

本設計では、ログ本文へ Step 名を個別に埋め込むのではなく、`ILogger.BeginScope` の scope chain をワークフローの実行階層として扱う。
CLI の `EngineLoggingProvider` は scope chain から実行情報を組み立て、Text ログでは表示用パス、JSON ログでは構造化した項目として出力する。

関連する課題: #21

## 2. 背景

現在のワークフロー実行経路では、最上位 Entry と各 Step の実行時にログスコープが作成される。
Step スコープには `EntryName`、`StepName`、`Attempt` が設定されているが、CLI の記録出力部品は `EntryName` だけを参照しており、通常のログ行には Step 名が表示されない。

また、ネストした `CompositeStep` が `Execute` / `ExecuteAsync` で実行される経路では、内側の複合処理およびその子 Step を表すスコープが作成されない。
このため、外側 Step から内側の複合処理を呼び出した場合、ログから次の関係を復元できない。

```text
Main
└─ RunTextPipelineStep
   └─ TextPipeline
      ├─ LoadTextStep
      ├─ ParseDocumentStep
      └─ BuildReportStep
```

ログ本文を次のように変更するだけでは、Step 本体が出力したログやネストした複合処理のログを一貫して扱えない。

```text
Step started
↓
LoadTextStep started
```

そのため、実行階層をスコープとして保持し、出力形式ごとに整形処理が投影する方式を採用する。

## 3. 目的

本対応の目的は次のとおり。

1. エンジンが出力する Step の実行過程ログから、対象 Step 名を判別できること。
2. Step 本体が `StepContext.Logger` へ出力したログにも、実行中の Step 名が付与されること。
3. ネストした `CompositeStep`、`If`、`Switch` の親子関係をログから判別できること。
4. 再試行中のログから試行回数を判別できること。
5. Text と JSON の両形式で同じ実行情報を利用すること。
6. 公開ワークフロー API、`WorkflowResult`、`ExecutionTrace` の契約を変更しないこと。

## 4. 対象外

次の項目は本対応の対象外とする。

- `ExecutionTrace` を木構造へ変更すること。
- Step の並列実行を追加すること。
- 分散追跡や OpenTelemetry の span を導入すること。
- ログの重要度設定や区分による絞り込みの仕様変更。
- Step の表示名を変更する新しい公開 API の追加。
- ユーザーが Step 本体で作成した任意の記録出力部品に、CLI 固有の表示形式を強制すること。

## 5. 設計方針

### 5.1 スコープを実行階層の唯一の情報源にする

ログ本文は、状態を表す短いメッセージのまま維持する。
Step 名、Entry 名、分岐名、試行番号はログスコープに保持する。

```text
Message: Step started
Scope:   Entry=Main, Step=LoadTextStep, Attempt=1
```

これにより、Text の整形処理と JSON の整形処理が同じ構造化情報を利用できる。
また、Step 本体が出力する任意のメッセージにも、現在のスコープが自動的に適用される。

### 5.2 スコープの順序を階層順序として扱う

スコープは外側から内側へ積み重ねる。
整形処理は scope chain を古いスコープから新しいスコープの順に走査し、実行パスを構成する。

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

本対応で追加するスコープの識別子、スコープの snapshot、分岐の付随情報、記録情報はエンジンと CLI の内部型とする。

次の公開型は変更しない。

- `CompositeStep<TOut>` の公開メソッド
- `StepContext`
- `WorkflowExecutionOptions`
- `WorkflowResult`
- `ExecutionTrace`
- `ExecutionTraceStep`

## 6. スコープの構造

### 6.1 スコープの識別子

以下の識別子を使用する。

| 識別子 | 型 | 用途 |
|---|---|---|
| `EntryName` | `string` | 最上位 Entry の完全修飾名 |
| `CompositeName` | `string` | ネストして実行中の複合処理名 |
| `StepName` | `string` | 現在実行中の通常 Step、Lambda Step、`If`、`Switch` 名 |
| `BranchName` | `string` | 選択された `then`、`else`、`case=...`、`default` |
| `Attempt` | `int` | 現在の Step 試行番号 |

最上位 Entry スコープでは `EntryName` だけを設定する。
`Attempt` は再試行対象である Step スコープにだけ設定し、Entry スコープには設定しない。

### 6.2 実行パスへ含める識別子

`ExecutionPath` は次の識別子の値をスコープ順に連結して構成する。

1. `EntryName`
2. `CompositeName`
3. `StepName`
4. `BranchName`

`Attempt` は階層ではないため、パスへ含めず独立項目として出力する。

### 6.3 重複値の扱い

移行中に同じ `EntryName` が複数のスコープに存在しても、実行パスには最外側の `EntryName` を一度だけ追加する。
新しい実装では Step スコープへの `EntryName` の重複設定をやめる。

同名 Step が階層内に複数存在する場合は省略しない。
階層上の位置が異なるため、同じ文字列が連続してもそのまま出力する。

## 7. 実行過程とスコープの構成

### 7.1 最上位 Entry

`ExecuteWorkflowAsync` の開始時に Entry スコープを作成し、ワークフロー全体を囲む。

```csharp
using IDisposable? entryScope = engineLogger.BeginScope(
    new Dictionary<string, object?>
    {
        ["EntryName"] = QualifiedName,
    });
```

出力する出来事は現行の意味を維持する。

```text
Entry started
Entry succeeded
Entry failed ...
```

### 7.2 通常 Step

各試行の開始前に Step スコープを作成する。
Step 本体、成功後の出力生成処理、失敗変換が同じスコープ内になるようにする。

```csharp
using IDisposable? stepScope = engineLogger.BeginScope(
    new Dictionary<string, object?>
    {
        ["StepName"] = step.Name,
        ["Attempt"] = attempt,
    });
```

Step 本体が `StepContext.Logger` へ出力するログにも、この scope chain が適用される。

非協調の非同期 Step がキャンセルを観測せず正常に完了した場合でも、Step 本体の `await` 直後にキャンセル要求を確認する。ここでキャンセルが確定しているときは、出力生成処理と `Step succeeded` の出力を行わず失敗として扱う。同期処理である `step.Produce` の直前と直後にもキャンセル要求を確認し、確定しているときは `Step succeeded` を出力しない。したがって、キャンセル後に成功の記録を出力してはならない。

実行過程の本文は次を維持する。

```text
Step started for attempt {Attempt}
Step succeeded on attempt {Attempt}
Step skipped on attempt {Attempt}
Step failed after attempt {Attempt} with error code {ErrorCode}
```

Text の整形処理が `StepName` と `Attempt` を表示するため、メッセージ本文へ Step 名を重複して追加しない。

### 7.3 ネストした CompositeStep

`CompositeStep<TOut>.ExecuteAsync(StepInput, CancellationToken)` は、内側の Step 列を実行する前に `CompositeName` スコープを作成する。

```csharp
using IDisposable? compositeScope = input.Context.Logger.BeginScope(
    new Dictionary<string, object?>
    {
        ["CompositeName"] = QualifiedName,
    });
```

`ExecuteSimpleStepSequenceAsync` でも各 Step の `StepName` スコープを作成する。
これにより、内側 Step が出力したログへ完全な階層が付与される。

ネスト経路では、Step 本体がログを出さない場合でも実行状況を確認できるよう、次の実行過程ログを出力する。

```text
Composite started
Step started
Step succeeded / Step skipped / Step failed
Composite succeeded / Composite failed
```

最上位ワークフローの `Entry started` / `Entry succeeded` と二重にならないよう、`ExecuteWorkflowAsync` 自身は `Composite started` を出力しない。

ネスト経路の実行過程ログには `StepContext.Logger` を使用する。最上位ワークフローから呼び出された場合は外側の Entry と Step の scope chain を継承し、単独の `Execute` / `ExecuteAsync` でも同じ記録出力契約を維持する。

ネストした複合処理と単純実行の実行過程ログの区分は `Devo6.WorkFlow.Step` とし、最上位ワークフローの Entry、再試行、時間上限、分岐制御は `Devo6.WorkFlow.Engine` を維持する。内部の記録情報や公開 API は追加しない。

外側の Step が再試行中にネストした複合処理を実行するとき、内側の `StepName` スコープに `Attempt` がなければ外側の試行番号を継承する。内側で `Attempt` を明示した場合は、その値を優先する。

内側の Step 列の `await` 直後にキャンセルが確定しているときは、`Composite succeeded` を出力せず失敗として扱う。

### 7.4 If

`If` 制御単位のスコープを作成した状態で条件を評価する。
分岐の選択後、子 Step 列を実行する間だけ `BranchName` スコープを追加する。

```text
Main > DocumentLength > then > KeepDetailedDocument
Main > DocumentLength > else > MarkShortDocument
```

分岐名は固定値とする。

```text
then
else
```

未選択の分岐のスコープおよび実行過程ログは出力しない。

### 7.5 Switch

`Switch` 制御単位のスコープを作成した状態で選択処理を評価する。
選択された分岐に応じて、次の `BranchName` を使用する。

```text
case=Guide
case=Reference
default
```

分岐条件の表示値は分岐定義時の値から生成し、次の制約を適用する。

- `null` は `null` と表示する。
- `IFormattable` は言語や地域に依存しない形式で文字列化する。
- その他は `ToString()` を使用する。
- 改行および制御文字は空白へ置換する。
- 最大 128 文字に制限する。

分岐条件値の文字列化に失敗した場合は `case=<unavailable>` を使用し、ワークフロー定義自体を失敗させない。

### 7.6 再試行

再試行では実行パスを変えず、`Attempt` だけを更新する。

```text
ExecutionPath: Main > FetchStep
Attempt: 1

ExecutionPath: Main > FetchStep
Attempt: 2
```

失敗した試行のスコープを破棄してから次の試行スコープを開始し、試行番号が後続ログへ漏れないようにする。

### 7.7 時間上限、キャンセル、例外

失敗ログおよび例外ログは、成功時と同じ Step スコープ内で出力する。
そのため、次のログにも `ExecutionPath` と `Attempt` が付与される。

- Step 例外
- 出力生成処理の例外
- 条件評価の例外
- Switch の選択処理の例外
- 時間上限
- 外部キャンセル

Entry 失敗ログは、失敗した Step スコープ内で出力する現行動作を維持する。
これにより、Entry の失敗原因となった Step を同じログ行から確認できる。

## 8. 分岐の付随情報の変更

現在の分岐実行計画へ表示用の分岐名を追加する。

```csharp
internal sealed record BranchExecutionPlan(
    IReadOnlyList<StepRegistration> Steps,
    int StartStepIndex,
    string BranchName);
```

`If` では `then` または `else`、`Switch` では `case=...` または `default` を設定する。

この変更はエンジンの内部型だけに閉じる。
Step の設定を平坦化した番号、および分岐の選択規則は変更しない。

## 9. CLI スコープの snapshot

### 9.1 snapshot 型

CLI の出力部品は現在の scope chain から、1 回のログ出力に必要な値を snapshot として構成する。

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

`EngineLoggingScopeState` は `AsyncLocal` 上の連結リストを保持している。
現在の要素から親へ走査した後、順序を反転して外側から内側の順に処理する。

処理手順は次のとおり。

1. 現在の要素から根の要素までを一時配列へ格納する。
2. 配列を逆順に走査する。
3. 各スコープ状態が持つ識別子と値を読み取る。
4. `ExecutionPath` を構築する。
5. 末端の `StepName`、`BranchName`、`Attempt` を更新する。

スコープ状態が対応する識別子と値の集合でない場合は無視する。

### 9.3 スコープの復元

`BeginScope` が返す `IDisposable` は、作成時点の親要素の snapshot を保持する。
`Dispose` 時に現在の要素を snapshot へ戻す現行方式を維持する。

次を検査で保証する。

- 直前の Step スコープが次の兄弟 Step へ漏れない。
- 選択された分岐スコープが分岐後の Step へ漏れない。
- ネストした複合処理のスコープが外側の複合処理の後続 Step へ漏れない。
- 別のワークフロー実行へスコープが漏れない。

## 10. Text ログ形式

### 10.1 基本形式

現行形式の区分と本文の間に、実行パスと試行番号を追加する。

```text
[HH:mm:ss] [Level] Category [ExecutionPath] [attempt=N] Message
```

実行パスが存在しない出力部品の内部ログでは、パス部分を省略する。
`Attempt` が存在しない Entry の実行過程ログでは、試行番号部分を省略する。

### 10.2 出力例

最上位 Entry:

```text
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main] Entry started
```

通常 Step:

```text
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main > MainStep] [attempt=1] Step started for attempt 1
[12:00:00] [Information] Devo6.WorkFlow.Step [Main > MainStep] [attempt=1] Loading input file
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main > MainStep] [attempt=1] Step succeeded on attempt 1
```

ネストした複合処理:

```text
[12:00:00] [Information] Devo6.WorkFlow.Step [Main > RunTextPipelineStep > TextPipeline] Composite started
[12:00:00] [Information] Devo6.WorkFlow.Step [Main > RunTextPipelineStep > TextPipeline > LoadTextStep] Step started
[12:00:00] [Information] Devo6.WorkFlow.Step [Main > RunTextPipelineStep > TextPipeline > LoadTextStep] Loading source text from input/source.txt
```

If の分岐:

```text
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main > DocumentLength] If condition started
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main > DocumentLength > then > KeepDetailedDocument] [attempt=1] Step started for attempt 1
```

Switch の分岐:

```text
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main > ReportRoute > case=Guide > UseGuideReport] [attempt=1] Step started for attempt 1
```

再試行:

```text
[12:00:00] [Warning] Devo6.WorkFlow.Engine [Main > FetchStep] [attempt=1] Step attempt 1 failed with error code STEP_EXECUTION_FAILED; retrying
[12:00:01] [Information] Devo6.WorkFlow.Engine [Main > FetchStep] [attempt=2] Step started for attempt 2
```

### 10.3 互換性

現行の日時、重要度、区分、本文の順序は維持し、その間へ追加情報を挿入する。
既存検査のように本文の包含を検証する利用方法は継続して動作する。

ログ行全体を固定文字列として解析している利用者には形式変更となるため、リリースノートへ記載する。

## 11. JSON ログ形式

### 11.1 追加項目

既存項目を維持し、次を追加する。

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

該当するスコープが存在しない場合も、JSON の項目は省略せず `null` を出力する。
`ExecutionPath` はスコープがない場合に空配列を出力する。

固定した構造にすることで、ログ収集側の問い合わせを単純化する。

### 11.3 互換性

既存の次の項目は名称と意味を維持する。

- `Timestamp`
- `Level`
- `Category`
- `Message`
- `Exception`

新しい項目の追加だけを行う。
未知の項目を厳密に拒否する利用側には影響する可能性があるため、リリースノートへ記載する。

## 12. logger category

logger category は現行の区分を維持する。

| logger category | 用途 |
|---|---|
| `Devo6.WorkFlow.Engine` | Entry、Step の実行過程、再試行、時間上限、分岐制御 |
| `Devo6.WorkFlow.Step` | Step 本体が `StepContext.Logger` へ出力するログ |

ネストした複合処理と単純実行の実行過程ログは `StepContext.Logger` を使用し、`Devo6.WorkFlow.Step` の logger category で出力する。
最上位ワークフローの Entry、再試行、時間上限、分岐制御は引き続き `Devo6.WorkFlow.Engine` の logger category を使用する。

## 13. ログ出力先のファイル名

`Logging.File.NameFormat` の `{RootStepName}` は、常に最外側の `EntryName` を使用する。

```text
Main > RunTextPipelineStep > TextPipeline > LoadTextStep
```

上記の実行中も `{RootStepName}` は `Main` のままとする。
`CompositeName` や末端の `StepName` によって、実行途中で出力ファイルが切り替わってはならない。

## 14. 変更対象

主な変更対象は次のとおり。

### `src/Devo6.WorkFlow.Engine/CompositeStep.cs`

- Entry スコープから不要な `Attempt` を除去する。
- 通常 Step スコープから重複した `EntryName` を除去する。
- ネストした複合処理の `CompositeName` スコープを追加する。
- 単純実行経路へ Step の実行過程ログと `StepName` スコープを追加する。
- `If` / `Switch` の分岐スコープを追加する。
- `BranchExecutionPlan` へ `BranchName` を追加する。
- 単純実行の実行過程ログは `StepContext.Logger` へ出力する。
- Step 本体と内側の Step 列の `await` 直後、および同期処理である `step.Produce` の直前と直後にキャンセル要求を確認する。

### `src/Devo6.WorkFlow.Cli/EngineLoggingProvider.cs`

- `EntryName` 単独取得をスコープの snapshot 取得へ置き換える。
- Text の整形処理へ実行パスと試行番号を追加する。
- JSON の整形処理へ構造化したスコープ項目を追加する。
- `{RootStepName}` は snapshot の最外側 `EntryName` から解決する。

### `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`

- エンジンの実行過程ログのスコープと Step 本体ログへのスコープ伝播を検証する。
- 再試行、失敗、キャンセル時のスコープを検証する。

### `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`

- Text ログの単純 Step のパスを検証する。
- ネストした複合処理のパスを検証する。
- `If` / `Switch` の分岐パスを検証する。
- JSON の項目を検証する。
- `{RootStepName}` の互換性を検証する。

### `samples/multi-folder-composite/README.md`

- 新しい Text ログ例を追加する。
- ネストした `TextPipeline` の表示例を追加する。

## 15. 検査計画

### 15.1 単純なワークフロー

次のパスが Entry、Step の実行過程ログ、Step 本体ログへ出ることを確認する。

```text
Main > MainStep
```

### 15.2 Lambda Step

API に渡した名前が末端の Step 名として表示されることを確認する。

```text
Main > NormalizeText
```

### 15.3 ネストした複合処理

次の完全なパスが表示されることを確認する。

```text
Main > RunTextPipelineStep > TextPipeline > LoadTextStep
```

### 15.4 If

真の場合は `then`、偽の場合は `else` だけが表示されることを確認する。
未選択の分岐の Step 名がログへ出ないことも確認する。

### 15.5 Switch

一致する分岐条件と既定分岐の両方を検証する。
分岐条件値の制御文字除去と長さ制限も単体検査で確認する。

### 15.6 再試行

各試行で同じパスと異なる試行番号が出ることを確認する。
成功後の後続 Step に試行番号が漏れないことを確認する。
外側の Step が再試行中にネストした複合処理を実行した場合、`Attempt` を持たない内側の Step は外側の試行番号を継承し、内側で明示した値がある場合はその値を優先することを確認する。

### 15.7 時間上限とキャンセル

失敗ログに末端の Step のパスと試行番号が付くことを確認する。
非協調 Step の `await` 後、および同期処理である `step.Produce` の直前または直後にキャンセルが確定している場合、`Step succeeded` と `Composite succeeded` が出力されないことを確認する。

### 15.8 スコープ漏れ

兄弟 Step、分岐の後続 Step、ネストした複合処理の後続 Step、別のワークフロー実行でスコープが残らないことを確認する。

### 15.9 JSON

JSON を復号し、文字列包含ではなく項目の型と値を検証する。

```text
EntryName      string or null
StepName       string or null
BranchName     string or null
Attempt        number or null
ExecutionPath  array
```

### 15.10 ファイル名

ネストした複合処理の実行中に最初のファイルが作られても、ファイル名が最上位 Entry の `Main` を使用することを確認する。
Windows のファイル出力検査では、記録出力部品を破棄してファイルハンドルを解放した後にファイル内容を読み取る。

## 16. 受け入れ条件

次をすべて満たした場合に課題 #21 の実装を完了とする。

1. 実行過程ログから実行中の Step 名を確認できる。
2. Step 本体ログから実行中の Step 名を確認できる。
3. ネストした複合処理の親子関係を Text ログで確認できる。
4. `If` / `Switch` の選択分岐をログで確認できる。
5. 再試行の試行番号をログで確認でき、`Attempt` を持たない内側の Step は外側の試行番号を継承し、内側で明示した値を優先する。
6. JSON ログに `EntryName`、`StepName`、`BranchName`、`Attempt`、`ExecutionPath` が含まれる。
7. `{RootStepName}` の既存動作が維持される。
8. スコープが兄弟 Step や別実行へ漏れない。
9. 既存の `WorkflowResult` と `ExecutionTrace` の公開契約が変わらない。
10. 既存検査と追加検査がすべて成功する。
11. キャンセルの確定後に `Step succeeded` と `Composite succeeded` を出力しない。

## 17. 実装順序

実装は次の順序で行う。

1. 分岐の付随情報とスコープの識別子を追加する。
2. 最上位ワークフローと通常 Step のスコープを整理する。
3. ネストした複合処理と単純実行のスコープを追加する。
4. `If` / `Switch` の分岐スコープを追加する。
5. CLI のスコープの snapshot を実装する。
6. Text の整形処理を更新する。
7. JSON の整形処理を更新する。
8. エンジンの契約検査を追加する。
9. CLI の結合検査を追加する。
10. サンプルの説明文書と公開向け説明を更新する。

## 18. 想定される問題と対策

### スコープの破棄漏れ

`using` でスコープの有効期間を限定し、例外経路を含めて必ず復元する。
スコープ漏れの専用検査を追加する。

### Text ログ形式の変更

既存項目と本文は維持し、追加情報だけを挿入する。
変更内容をリリースノートへ記載する。

### JSON を利用する側の厳密な構造規則

既存項目は変更せず追加だけにする。
新規項目をリリースノートへ明記する。

### 分岐条件値による巨大または不正な表示

制御文字除去、長さ制限、文字列化失敗時の代替値を実装する。

### ネストした複合処理の実行過程ログの logger category

ネストした複合処理は `StepContext.Logger` を使い、外側 Step 本体ログと同じ `Devo6.WorkFlow.Step` の区分に統一する。
最上位ワークフローの実行過程ログの区分とは分け、公開 API や内部の情報型を増やさない。

## 19. 将来拡張

本設計のスコープの構造は、将来次の機能へ拡張できる。
ただし本課題では実装しない。

- `ExecutionTrace` への実行パス追加。
- OpenTelemetry span へのスコープ変換。
- パス単位のログの絞り込み。
- Step ごとの所要時間を JSON の項目として追加。
- ワークフロー実行 ID、相関 ID の追加。
