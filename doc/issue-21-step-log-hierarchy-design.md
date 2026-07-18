# 課題 #21 Step ログ階層表示設計

## 1. 位置付け

本書は `doc/workflow_engine_spec.md` を補足し、課題 #21 で扱う Step 名付きログと、ネストした実行階層の表示契約を定める。

対象は、現在の `CompositeStep` 実行経路、`StepContext.Logger`、`WorkflowExecutionOptions.LoggerFactory`、CLI の `EngineLoggingProvider` とする。

本書では、ログ本文へ Step 名を個別に埋め込むのではなく、`ILogger` のスコープへ実行構造を保持し、出力先が同じ構造から Text 形式と JSON 形式を作る方針を採用する。

## 2. 背景

現在のエンジンは、workflow 実行時に `EntryName`、`StepName`、`Attempt` を logger スコープへ設定している。Step 登録情報にも、クラス Step の型名、Lambda Step の指定名、`If` と `Switch` の制御単位名が保持されている。

一方、CLI の `EngineLoggingProvider` は、スコープからログファイル名に使う `EntryName` だけを取得する。Text 形式と JSON 形式の本文には `StepName` と `Attempt` を反映しないため、`Step started for attempt 1` や Step 本体のログだけを見ても、どの Step の処理かを判別しにくい。

また、外側の Step から `CompositeStep.Execute` または `CompositeStep.ExecuteAsync` を呼ぶネスト構成では、内側の単純実行経路が logger スコープを作らない。このため、内側 CompositeStep 名と、その配下で実行している Step 名の親子関係をログから復元できない。

`If` と `Switch` は選択された分岐を再帰実行できるが、現在の分岐実行計画は Step 列と開始位置だけを保持し、`then`、`else`、`case`、`default` のどの分岐かをログ用に識別する情報を持たない。

## 3. 目的

課題 #21 では、次を満たす。

- エンジンが出す Step 開始、成功、skip、失敗のログから、対象 Step 名を確認できる
- Step 本体が `StepContext.Logger` へ出したログから、実行中の Step 名を確認できる
- ネストした CompositeStep では、外側 Entry、外側 Step、内側 CompositeStep、内側 Step の順序を確認できる
- `If` と `Switch` では、選択された分岐を含む実行階層を確認できる
- retry 中は、現在の Step と試行番号を同時に確認できる
- Text 形式は人が読みやすい 1 行形式とする
- JSON 形式は階層を配列として保持し、文字列の再解析を不要にする
- ログファイル名の `{RootStepName}` は、従来どおり root Entry 名から解決する
- `WorkflowResult`、`ExecutionTrace`、Step の戻り値、Config の契約を変更しない

## 4. 対象外

初期対応には次を含めない。

- `ExecutionTraceStep` への親子識別子追加
- `Activity` または OpenTelemetry の span 生成
- Step の入力、出力、Config、Switch selector 値の自動記録
- ログ本文から機密値を検出して伏せ字にする処理
- 並列 Step 実行または分岐の並列実行
- workflow ごとの logger provider 分離や、同一 provider での複数ログファイル同時管理
- クラス Step に任意の表示名を付ける新しい公開 API
- Switch の case 値をログへ自動出力すること
- 外部 logger provider の表示形式をエンジン側で強制すること

## 5. 採用方式

### 5.1 構造化スコープを情報源にする

実行位置の正本は、logger スコープの親子関係とする。

ログ本文は、`Step started`、`Step succeeded`、Step 実装が出した利用者向けメッセージなど、出来事だけを表す。Entry 名、CompositeStep 名、Step 名、分岐名、試行番号を各メッセージへ文字列結合しない。

この方式により、CLI の Text 形式、CLI の JSON 形式、外部 logger provider が同じ実行情報を利用できる。

### 5.2 インデントだけを状態として持たない

logger provider 内の可変な深さカウンターだけで階層を表現しない。

インデント方式だけでは、非同期処理、例外経路、複数 workflow の同時利用で開始と終了の対応が崩れやすい。また、JSON 形式で親子要素を再構成するには、Text 表示用の空白を再解析する必要がある。

スコープの親 chain を外側から内側へ走査し、その時点の実行パスを毎回構成する。

### 5.3 Step 名をメッセージへ重複させない

`Step started for attempt {Attempt}` を `{StepName} started` のように変更するだけの方式は採用しない。

この方式では Step 本体が出したログへ名前が付かず、ネストした CompositeStep や分岐の親子関係も表現できないためである。

## 6. スコープ契約

### 6.1 スコープ要素

エンジンと CLI の first-party 連携では、次のスコープ key を使う。

| 種類 | key | 値 | 実行パス要素 | 有効期間 |
| --- | --- | --- | --- | --- |
| root Entry | `EntryName` | Entry の完全修飾名 | 含める | Entry 実行全体 |
| nested CompositeStep | `CompositeName` | CompositeStep の完全修飾名 | 含める | `Execute` または `ExecuteAsync` の内側 |
| 実行単位 | `StepName` | 登録済み Step 名 | 含める | 対象 Step または制御単位の実行中 |
| 選択分岐 | `BranchName` | `then`、`else`、`case[n]`、`default` | 含める | 選択分岐の Step 列実行中 |
| 試行 | `Attempt` | 1 から始まる整数 | 含めない | 同じスコープ node の Step 実行中 |

これらの key は、エンジンと同梱 CLI の内部連携契約とする。初期対応では新しい公開定数型を追加しない。

### 6.2 実行パス

実行パスは、現在のスコープ chain を外側から内側へ走査し、`EntryName`、`CompositeName`、`StepName`、`BranchName` の値を順番に並べたものとする。

例:

```text
Main
  > RunTextPipelineStep
  > TextPipeline
  > LoadTextStep
```

JSON 形式では、次の配列として保持する。

```json
[
  "Main",
  "RunTextPipelineStep",
  "TextPipeline",
  "LoadTextStep"
]
```

Text 形式の区切りは ` > ` とする。Text 形式は人が読む表示用であり、機械的な再解析契約にはしない。構造化処理では JSON 形式の配列または logger スコープを利用する。

### 6.3 名前の決定規則

- root Entry は `CompositeStep.QualifiedName` を使う
- nested CompositeStep も `CompositeStep.QualifiedName` を使う
- クラス Step は現在と同じく `typeof(TStep).Name` を使う
- Lambda Step は `Run` または `RunAsync` に渡した名前を使う
- `If` と `Switch` は公開 API に渡した制御単位名を使う
- `RunIf` と `TapIf` は対象 Step 型名を使う
- 空文字または空白だけの値は実行パスへ追加しない

### 6.4 分岐名

`If` は `then` または `else` を使う。

`Switch` は登録順に `case[0]`、`case[1]` のような分岐名を割り当て、どの case にも一致しない場合は `default` を使う。

Switch の case 値は任意の型であり、文字列化に副作用がある場合や、機密値を含む場合がある。このため、初期対応では case 値を自動記録せず、登録順の識別子だけを使う。

### 6.5 試行番号

`Attempt` は、同じスコープ node にある `StepName` の試行番号として扱う。

CLI が現在 Step を求めるときは、最も内側にある `StepName` を採用し、その Step と同じスコープ node に `Attempt` がある場合だけ現在の試行番号として採用する。

この規則により、外側 Step が retry 中に nested CompositeStep を単純実行しても、外側 Step の試行番号を内側 Step の試行番号として誤表示しない。

## 7. エンジン実行設計

### 7.1 root workflow 実行

`ExecuteWorkflowAsync` は、Entry 実行全体を `EntryName` スコープで囲む。

通常 Step の各試行は、`StepName` と `Attempt` を同じスコープへ設定する。Step 本体、条件判定、retry 警告、timeout またはキャンセル判定、失敗ログは、このスコープ内で実行する。

成功した Step の `Produce`、`StoreAs`、`Discard` に相当する後処理は、成功した試行の `StepName` と `Attempt` を持つスコープ内で実行する。

Step スコープへ `EntryName` を重複設定しない。Entry スコープが外側に存在するため、子スコープでは Step 固有情報だけを追加する。

### 7.2 nested CompositeStep 実行

`CompositeStep<TOut>.ExecuteAsync` は、処理開始時に `CompositeName` スコープを開始し、内側の Step 列が終了するまで維持する。同期の `Execute` は `ExecuteAsync` を利用する現在の経路を維持する。

nested CompositeStep は、次のライフサイクルログを `StepContext.Logger` へ出す。

- `Composite started`
- `Composite succeeded`
- `Composite failed`

失敗時は例外を記録した後に再送出し、外側 workflow が現在と同じ規則で `STEP_EXECUTION_FAILED` などへ変換できるようにする。ログ追加によって例外型、戻り値、停止位置を変更しない。

root の `ExecuteWorkflowAsync` は `Entry started` と `Entry succeeded` を既に持つため、root Entry を `Composite started` として二重記録しない。

### 7.3 単純 Step 列実行

`ExecuteSimpleStepSequenceAsync` は、各 Step または制御単位を `StepName` スコープで囲む。

通常 Step は次を記録する。

- 実行前に `Step started`
- 成功後に `Step succeeded`
- skip 時に `Step skipped`
- 例外時に `Step failed`

単純実行経路には `WorkflowExecutionOptions.Retry` がないため、内側 Step のスコープへ `Attempt` を追加しない。

Step 本体が `StepContext.Logger` へ出したログは、同じ `StepName` スコープを継承する。

### 7.4 `If` と `Switch`

分岐実行計画へ、選択分岐の `BranchName` を追加する。

概念上の内部型は次の形とする。

```csharp
internal sealed record BranchExecutionPlan(
    IReadOnlyList<StepRegistration> Steps,
    int StartStepIndex,
    string BranchName);
```

`If` の条件評価または `Switch` の selector 評価は、制御単位の `StepName` スコープ内で行う。分岐が決定した後、選択分岐の Step 列だけを `BranchName` スコープで囲んで再帰実行する。

例:

```text
Main
  > DocumentLength
  > then
  > KeepDetailedDocument
```

未選択分岐は実行せず、ログスコープも作らない。既存の trace 契約と同様に、実際に選択された経路だけをログへ反映する。

分岐選択に失敗した場合は、制御単位の `StepName` までを実行パスへ含める。分岐が確定していないため、`BranchName` は追加しない。

### 7.5 retry

各 retry 試行は、同じ `StepName` と異なる `Attempt` を持つ独立スコープとする。

例:

```text
ExecutionPath: Main > DownloadStep
StepName: DownloadStep
Attempt: 2
```

失敗後に retry する警告、最終失敗、成功ログは、対象試行のスコープ内で記録する。

成功後の後処理は、成功した試行番号を使う。後続 Step の開始時には前 Step のスコープを破棄し、兄弟 Step 間で名前または試行番号を漏らさない。

### 7.6 timeout とキャンセル

`STEP_TIMEOUT` または `STEP_CANCELED` のログは、停止対象 Step の `StepName` と `Attempt` を保持する。

外部キャンセルと timeout の優先順位、retry を停止する規則、`Produce` を実行しない規則は変更しない。

## 8. CLI logger provider 設計

### 8.1 スコープ snapshot

`EngineLoggingScopeState` は、現在の `EntryName` だけでなく、現在のスコープ chain 全体から不変な snapshot を作る。

概念上の内部型は次の形とする。

```csharp
internal sealed record EngineLogScopeSnapshot(
    string? EntryName,
    string? StepName,
    int? Attempt,
    IReadOnlyList<string> ExecutionPath);
```

snapshot 作成時は次を行う。

1. `AsyncLocal` が保持する scope node を現在地点から root まで走査する
2. node 列を反転し、外側から内側の順にする
3. 既知 key の文字列値を実行パスへ追加する
4. 最も内側の `StepName` を現在 Step とする
5. 現在 Step と同じ node にある `Attempt` だけを現在試行とする
6. 未知の scope state または未知 key は無視する

`EngineLogger.Log` はログ出力のたびに snapshot を取得し、provider の `Write` へ渡す。

### 8.2 root Entry 名とログファイル名

`{RootStepName}` は snapshot の `EntryName` から解決する。

nested CompositeStep の `CompositeName` または現在の `StepName` でログファイル名を切り替えない。例えば root Entry が `Main` の場合、内側で `TextPipeline` を実行しても、ファイル名は従来どおり `Main` を使う。

Entry スコープがない logger 利用では、現在と同じ fallback 名 `Workflow` を使う。

### 8.3 耐障害性

スコープの解析失敗によって workflow 実行を失敗させない。

- null の state は無視する
- 既知 key の型が期待値と異なる場合は、その値だけを無視する
- path 要素の改行は Text 表示時に空白へ正規化する
- JSON 形式では元の文字列要素を配列として保持する
- 任意オブジェクトの `ToString()` を path 構成のために呼び出さない

## 9. 出力形式

### 9.1 Text 形式

既存の時刻、レベル、category の順序を維持し、category と本文の間へ実行パスを追加する。

```text
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main] Entry started
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main > MainStep] Step started for attempt 1
[12:00:00] [Information] Devo6.WorkFlow.Step [Main > MainStep] Loading input file
```

ネスト時の例:

```text
[12:00:00] [Information] Devo6.WorkFlow.Step [Main > RunTextPipelineStep > TextPipeline > LoadTextStep] Loading source text
```

分岐時の例:

```text
[12:00:00] [Information] Devo6.WorkFlow.Step [Main > RunTextPipelineStep > TextPipeline > DocumentLength > then > KeepDetailedDocument] Keeping detailed document
```

実行パスが空の場合は角括弧を追加せず、現在の形式を維持する。

例外は現在と同じく本文の後へ出力する。実行パスは例外本文へ重複挿入しない。

### 9.2 JSON 形式

既存フィールドを維持し、次の構造化フィールドを追加する。

```json
{
  "Timestamp": "2026-07-18T03:00:00.0000000Z",
  "Level": "Information",
  "Category": "Devo6.WorkFlow.Step",
  "EntryName": "Main",
  "StepName": "LoadTextStep",
  "Attempt": null,
  "ExecutionPath": [
    "Main",
    "RunTextPipelineStep",
    "TextPipeline",
    "LoadTextStep"
  ],
  "Message": "Loading source text",
  "Exception": null
}
```

JSON 形式では次を固定する。

- `EntryName` は root Entry スコープがない場合に null
- `StepName` は現在 Step がない場合に null
- `Attempt` は現在 Step と同じスコープに試行番号がない場合に null
- `ExecutionPath` は実行パスがない場合に空配列
- 既存の `Timestamp`、`Level`、`Category`、`Message`、`Exception` の意味を変更しない
- null フィールドも出力し、JSON の field schema を安定させる

## 10. ログイベント

初期対応では、既存のログレベルと category を維持する。

- エンジンの Entry、workflow Step、retry、timeout、失敗は `Devo6.WorkFlow.Engine`
- Step 本体と nested CompositeStep の単純実行は `Devo6.WorkFlow.Step`

既存の主要メッセージは可能な限り維持し、Step 名の可視化は実行パスで行う。

nested CompositeStep と単純実行 Step のライフサイクルログは新規追加となるため、ネスト構成ではログ行数が増える。これは、現在位置を Step 本体の任意ログに依存せず確認するための意図した変更とする。

初期対応では新しい公開 `EventId` 契約を追加しない。

## 11. 互換性

### 11.1 公開 API

`IStep<TOut>`、`IAsyncStep<TOut>`、`StepInput`、`StepContext`、`CompositeStep` の公開 API は変更しない。

`WorkflowResult`、`ExecutionTrace`、`ExecutionTraceStep` の schema も変更しない。

実装変更は、内部の実行計画、logger スコープ、CLI formatter、検査に限定する。

### 11.2 Text ログ

既存の先頭部分は維持するが、category と本文の間に `[実行パス]` が追加される。

ログ全文を固定文字列として解析している利用者には影響がある。人向け Text ログを機械解析するのではなく、JSON 形式または structured logging provider を使うことを推奨する。

### 11.3 JSON ログ

既存フィールドを削除または改名せず、構造化フィールドを追加する。

未知フィールドを許容する一般的な JSON 利用者は互換動作を維持できる。field 一覧を厳密固定している利用者には追加フィールドの影響があるため、変更履歴と README に記載する。

### 11.4 外部 logger provider

外部から渡した `ILoggerFactory` に対しても、追加したスコープ情報を提供する。

外部 provider がスコープを表示するか、どの形式で保存するかは、その provider の設定に従う。エンジンは provider 固有の表示設定を変更しない。

### 11.5 機密情報

入力、出力、Config、例外以外の任意値を新しくログへ出さない。

Switch の selector 結果と case 値は出力せず、`case[n]` だけを表示する。Step 名と CompositeStep 名は workflow 定義に明示された識別情報として扱う。

## 12. 検査方針

### 12.1 エンジンのスコープ検査

少なくとも次を自動検査する。

- root Entry のログに完全修飾 Entry 名がある
- 通常 Step の開始、成功、失敗ログに Step 名がある
- `StepContext.Logger` のログが実行中 Step のスコープを継承する
- Lambda Step の指定名が使われる
- nested CompositeStep の子 Step ログに外側から内側までの実行パスがある
- `Composite started`、`Composite succeeded`、`Composite failed` の scope が正しい
- 単純実行の Step 例外が再送出され、既存 workflow 失敗契約を維持する
- 兄弟 Step のスコープが互いに漏れない
- 連続した workflow 実行のスコープが互いに漏れない

### 12.2 分岐検査

- `If` の true 経路が `then` を含む
- `If` の false 経路が `else` を含む
- `Switch` の一致経路が登録順に対応する `case[n]` を含む
- `Switch` の不一致経路が `default` を含む
- 未選択分岐の Step 名がログに出ない
- 条件または selector 失敗時は制御単位名までを含み、分岐名を含まない
- 入れ子の `If` と `Switch` で外側分岐と内側分岐の順序を維持する

### 12.3 retry、timeout、キャンセル検査

- retry の各試行が同じ Step 名と異なる `Attempt` を持つ
- retry 警告が失敗した試行番号を持つ
- 成功後処理が成功した試行番号を持つ
- timeout と外部キャンセルが停止対象 Step 名を持つ
- nested CompositeStep の内側 Step に外側 Step の `Attempt` を誤適用しない

### 12.4 CLI Text 検査

- Text 形式に `[Main > StepName]` が含まれる
- nested CompositeStep の全階層が順序どおり表示される
- 分岐名が表示される
- 実行パスがないログは現在の形式を維持する
- 例外出力で実行パスを重複しない
- Console と File が同じ path 表示規則を使う
- `{RootStepName}` が nested CompositeStep 名へ変化しない

### 12.5 CLI JSON 検査

- 1 行が有効な JSON として読み込める
- `EntryName` と `StepName` が文字列または null
- `Attempt` が数値または null
- `ExecutionPath` が文字列配列
- 既存フィールドが残る
- Entry、Step、nested CompositeStep、分岐、retry の各経路が期待値と一致する

### 12.6 回帰検査

- `WorkflowResultContractTests`
- `RetryExecutionContractTests`
- `TimeoutCancellationContractTests`
- `IfBranchContractTests`
- `SwitchBranchContractTests`
- `ConditionalFlowIntegrationTests`
- `CliRunValidateTests`
- `SampleWorkflowTests`
- solution 全体検査
- format、Markdown、差分検査

## 13. 利用者への影響

対応後は、Step 実装が固有メッセージを出していなくても、エンジンのライフサイクルログから現在の Step 名を確認できる。

複数フォルダサンプルでは、次のように処理位置を確認できる。

```text
Main
  > RunTextPipelineStep
  > TextPipeline
  > AnalyzeTextStep
```

障害時は、例外メッセージだけでなく実行パスと試行番号から停止位置を特定できる。

Text ログの 1 行構造は変わるため、README とサンプルの出力例を実装 task で更新する。

## 14. task 分解

既存の作業計画との番号衝突を避け、課題 #21 は T73 から採番する。

### T73 設計

- 本書を追加する
- 現行 Engine と CLI logger provider の差を記録する
- スコープ key、実行パス、Text 形式、JSON 形式、互換性を確定する
- 設計点検を記録する

### T74 Engine スコープと nested CompositeStep

- root workflow の Step スコープを整理する
- nested CompositeStep の `CompositeName` スコープを追加する
- 単純 Step 列へ Step ライフサイクルログを追加する
- 例外再送出とスコープ復元を検査する

### T75 分岐と retry の階層統合

- 分岐実行計画へ `BranchName` を追加する
- `If` と `Switch` の選択分岐スコープを追加する
- retry、timeout、キャンセルと実行パスを統合検査する
- nested 分岐の順序と未選択分岐非表示を検査する

### T76 CLI Text と JSON 出力

- 現在スコープの snapshot 構築を実装する
- Text 形式へ実行パスを追加する
- JSON 形式へ `EntryName`、`StepName`、`Attempt`、`ExecutionPath` を追加する
- Console、File、`{RootStepName}` の回帰検査を追加する

### T77 文書、サンプル、統合検証

- README と複数フォルダサンプルのログ例を更新する
- solution 全体、format、Markdown、差分検査を実行する
- 既存失敗がある場合は今回起因か分類する
- `tasks-status.md` と `phases-status.md` を実結果へ同期する
- 課題 #21 の成果を取り込み依頼へまとめる

## 15. 受入条件

設計全体の完了条件は次のとおりとする。

- 通常 Step のすべてのライフサイクルログから Step 名を確認できる
- Step 本体の `StepContext.Logger` ログから実行中 Step 名を確認できる
- nested CompositeStep のログが外側から内側までの実行パスを持つ
- `If` と `Switch` の選択分岐だけが実行パスへ含まれる
- retry のログが現在 Step と試行番号を持つ
- timeout、キャンセル、例外のログが停止位置を持つ
- Text 形式が 1 行の読みやすい実行パスを表示する
- JSON 形式が実行パスを文字列配列として保持する
- Switch の case 値、入力、出力、Config を新たに自動記録しない
- `{RootStepName}` とログファイル名の契約を維持する
- 公開 Step API、`WorkflowResult`、`ExecutionTrace` の契約を変更しない
- 既存の retry、timeout、分岐、Config、サンプル検査に今回起因の回帰がない
