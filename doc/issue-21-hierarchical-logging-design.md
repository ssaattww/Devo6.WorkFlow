# 課題 #21 階層ログ設計

## 1. 目的

この設計は、ワークフローの実行位置をログから復元できるようにする。
開始位置、実行中の処理単位、ネストした `CompositeStep`、選択した分岐、再試行回数を、本文ではなく `ILogger.BeginScope` のスコープで保持する。

公開 API、`WorkflowResult`、`ExecutionTrace` の契約は変更しない。

## 2. 実行情報

次の識別子をスコープへ設定する。

| 識別子 | 用途 |
| --- | --- |
| `EntryName` | 最上位の開始位置の完全修飾名 |
| `CompositeName` | 実行中のネストした `CompositeStep` 名 |
| `StepName` | 実行中の処理単位名 |
| `BranchName` | 選択した分岐名 |
| `Attempt` | 現在の試行番号 |

`EntryName`、`CompositeName`、`StepName`、`BranchName` を外側から内側の順に連結し、`ExecutionPath` を構成する。`Attempt` は階層に含めず、独立した値として出力する。

## 3. 実行時の契約

### 3.1 最上位実行

`ExecuteWorkflowAsync` は `EntryName` のスコープを作成し、`Entry started`、`Entry succeeded`、失敗記録を出力する。

### 3.2 通常の処理単位

各試行の開始前に `StepName` と `Attempt` のスコープを作成する。処理本体、出力の生成処理、成功または失敗の記録は同じスコープ内で行う。

### 3.3 ネストした `CompositeStep`

`CompositeStep<TOut>.ExecuteAsync(StepInput, CancellationToken)` は `CompositeName` のスコープを作成する。内側の処理列も `StepName` のスコープを作成し、`Composite started`、`Step started`、成功または失敗の記録、`Composite succeeded` または `Composite failed` を出力する。

非協調の非同期処理単位がキャンセルを観測せず正常に完了した場合でも、各 `await` の直後に中止要求を確認する。キャンセルが確定しているときは、出力の生成処理、`Step succeeded`、`Composite succeeded` を出力せず失敗として扱う。したがって、キャンセル後に成功の記録を出力してはならない。

外側の処理単位が再試行中にネストした `CompositeStep` を実行するとき、内側 `StepName` のスコープに `Attempt` がなければ外側の試行番号を継承する。内側で `Attempt` を明示した場合は、その値を優先する。

### 3.4 分岐

`If` は `then` または `else` を、`Switch` は `case=...` または `default` を `BranchName` に設定する。選択されない分岐のスコープと記録は出力しない。

## 4. 出力形式

`EngineLoggingProvider` はスコープを読み取り、最外側の `EntryName`、最も内側の `StepName` と `BranchName`、最も内側で有効な `Attempt`、`ExecutionPath` を取得する。

`EngineLoggingFormat.Text` は、記録の区分と本文の間に実行パスと試行番号を出力する。

```text
[12:00:00] [Information] Devo6.WorkFlow.Engine [Main > FetchStep] [attempt=2] Step started for attempt 2
```

`EngineLoggingFormat.Json` は既存の日時、重要度、記録の区分、本文、例外に加え、`EntryName`、`StepName`、`BranchName`、`Attempt`、`ExecutionPath` を出力する。該当するスコープがない値は `null`、実行パスは空配列とする。

`Logging.File.NameFormat` の `{RootStepName}` は常に最外側の `EntryName` を使う。ネストした処理単位によって出力先を切り替えてはならない。

## 5. 検査計画

次を検査する。

- 単純な処理単位、匿名関数の処理単位、ネストした `CompositeStep` の実行パス。
- `If` と `Switch` で選択した分岐だけが出力されること。
- 再試行ごとの `Attempt` と、ネストした Step への試行番号の継承。
- 非協調 Step の時間上限または外部キャンセル後に成功記録を出力しないこと。
- 兄弟 Step、分岐後続 Step、別実行へスコープが漏れないこと。
- ファイル出力では記録出力部品を破棄してから内容を読むこと。

## 6. 対象外

- `ExecutionTrace` を木構造へ変更しない。
- Step の並列実行、分散追跡、公開 API の追加は行わない。
- 利用者が独自に作成した記録出力部品へ、CLI 固有の表示形式を強制しない。

## 7. 分岐値の安全化

`Switch` の分岐値が `null` の場合は `null` と表示する。`IFormattable` の場合は `InvariantCulture` で文字列化し、それ以外は `ToString` を使う。改行と制御文字を空白へ置き換え、128文字までに制限する。文字列化に失敗したときは `case=<unavailable>` を使い、ワークフロー定義を失敗させない。

重複する `EntryName` は最外側の値だけを実行パスへ追加する。同名の処理単位は階層上の位置が異なるため省略しない。分岐用の内部情報を追加しても、平坦化した番号と Config の対応は変更しない。

## 8. 記録の区分と互換性

最上位の開始位置、再試行、時間上限、分岐制御は `Devo6.WorkFlow.Engine` を使う。ネストした `CompositeStep` と内側の処理単位の記録は `Devo6.WorkFlow.Step` を使う。

既存の日時、重要度、記録の区分、本文、例外は保持する。文字列形式には実行パスと試行番号を追加し、構造化形式には実行情報を追加する。既存の固定文字列としてログ全体を解析する利用者には形式変更となる。

文字列形式の変更と構造化形式への項目追加は、公開時の説明文書に記載する。

## 9. 変更対象

- `CompositeStep.cs` はスコープ、キャンセル確認、分岐名を扱う。
- `EngineLoggingProvider.cs` はスコープから実行情報を組み立てる。
- 記録出力の検査は本文、構造化形式、再試行、時間上限、ファイル出力を確認する。
- `README.md` は利用者向けの出力例を示す。

## 10. 受入条件

1. Step 本体と開始・成功・失敗の記録から実行位置を確認できる。
2. ネスト、分岐、再試行の実行位置と試行番号を確認できる。
3. キャンセルまたは時間上限の確定後に成功記録を出力しない。
4. `WorkflowResult`、`ExecutionTrace`、公開 API の契約を維持する。
5. 文字列形式と構造化形式の検査が成功する。

## 11. 実装順序と対策

最初にスコープと分岐名を整備し、次にネストと再試行の記録を追加する。その後に出力形式と検査を追加する。各スコープは `using` で囲み、兄弟 Step や別実行へ残らないようにする。ファイルを読む検査では、記録出力部品を先に破棄する。

## 12. 将来の拡張

将来は `ExecutionTrace` への実行パス追加、分散追跡への変換、実行単位ごとの時間計測、ワークフロー実行識別子の追加を検討できる。本対応では実装しない。
