# Issue #21 階層ログ実装報告

## 1. 対象

- 課題: #21 `logの改良`
- 作業枝: `agent/issue-21-logging-hierarchy-design`
- 取り込み依頼: #25
- task: T73-T77

## 2. TDD の実施

### 2.1 Red

実装前に `HierarchicalLoggingContractTests` を追加し、次を利用者目線の契約として固定した。

- Text ログの Entry、Step、attempt 表示
- JSON ログの構造化 scope field
- `StepContext.Logger` による Step 本体ログ
- nested `CompositeStep` の完全な実行 path
- `If` / `Switch` の選択 branch 表示
- retry の試行番号

GitHub Actions run `29635422686` の artifact `8426932606` で、43 件中 40 件成功、既存 skip 1 件、意図した未実装箇所 2 件失敗を確認した。失敗は nested `CompositeStep` の階層欠落と、選択 branch 名の欠落だった。

### 2.2 Green

実装後の run `29635559813`、artifact `8426981336` で次を確認した。

- 階層ログ focused: 10/10 成功
- 関連回帰: 32 件成功、既存 skip 1 件、失敗 0 件
- format 成功
- `git diff --check` 成功

コード規約修正後の run `29636057186`、artifact `8427137522` では、階層ログ、関連回帰、`CodingStandardsContractTests` を合わせて 45 件成功、既存 skip 1 件、失敗 0 件だった。

## 3. 実装内容

### 3.1 Engine

`CompositeStep` の実行時 scope を整理した。

- root Entry は `EntryName`
- workflow Step は `StepName` と `Attempt`
- nested `CompositeStep` は `CompositeName`
- `If` は `then` / `else`
- `Switch` は `case=<value>` / `default`
- simple execution は Step 開始、成功、skip、失敗を記録
- retry、timeout、例外、producer の既存結果契約を維持

`Switch` の case 表示は invariant culture を使い、制御文字を空白へ置換し、128 文字へ制限する。文字列化に失敗した場合は `<unavailable>` を使う。

### 3.2 CLI logger provider

`EngineLoggingProvider` が `AsyncLocal` の scope chain から immutable snapshot を作成するようにした。

Text 形式は次を追加する。

```text
[Main > RunTextPipelineStep > TextPipeline > LoadTextStep] [attempt=1]
```

JSON 形式は既存 field を維持し、次を追加する。

- `EntryName`
- `StepName`
- `BranchName`
- `Attempt`
- `ExecutionPath`

ログファイル名の `{RootStepName}` は最外側の `EntryName` を使う既存契約を維持する。

### 3.3 文書と CI

- 課題専用設計書を追加
- 複数フォルダサンプル README に Text / JSON 例を追加
- `tasks-status.md` に T73-T77 を反映
- `phases-status.md` に P31 を反映
- PR xUnit workflow を検証専用に維持
- Restore、format、solution test、diff check のログと TRX を `TestResults` artifact として14日間保存
- 前段が失敗しても取得済み証跡を upload してから、最後の判定 step で workflow を失敗にする

PR xUnit workflow の権限は `contents: read` とし、ソースの変更、commit、pushを行わない。artifactで失敗原因を確認した後の修正は、GitHubコネクタ経由で対象ファイルへ明示的にcommitする。

## 4. 公開契約

次の公開契約は変更していない。

- `IStep<TOut>` / `IAsyncStep<TOut>`
- `StepInput` / `StepContext`
- `CompositeStep` の公開 API
- `WorkflowResult`
- `ExecutionTrace` / `ExecutionTraceStep`

Text ログの1行形式には実行 path が追加される。機械処理では JSON 形式または structured logging provider の利用を推奨する。

## 5. 全体検証の途中結果

PR xUnit run `29635784971` の artifact `8427061404` では、300 件中 296 件成功、既存 skip 3 件、コード規約1件失敗だった。失敗内容は pattern variable と record 宣言に起因する解析指摘であり、後続修正後の artifact `8427137522` で対象規約検査の成功を確認した。

最終 head では、検証専用の PR xUnit workflow が solution 全体を実行し、Restore、format、test、diff check の結果を同じ artifactへ保存する。最終結果と artifact ID はPRの検証欄へ反映する。

## 6. 作業方法の整理

一時的に追加されたソース自動修正用workflowは削除した。

今後の分担は次のとおりとする。

1. GitHub Actionsは検査とartifact保存だけを行う。
2. 失敗原因はartifactのログとTRXから調査する。
3. コード、文書、CI定義の修正はGitHubコネクタ経由で行う。
4. 修正後のcommitを起点にPR CIを再実行する。

## 7. skill 振り返り

本課題は既存の development-orchestrator、tdd-executor、review、progress、GitHub workflow の組み合わせで処理できた。新しい skill の追加は不要と判断する。
