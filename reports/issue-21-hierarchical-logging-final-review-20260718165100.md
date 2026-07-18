# Issue #21 階層ログ最終レビュー

## 1. 対象

- 課題: #21 `logの改良`
- 作業枝: `agent/issue-21-logging-hierarchy-design`
- 取り込み依頼: #25
- task / phase: T73-T77 / P31

## 2. レビュー範囲

次の実装と利用者契約を横断して確認した。

- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `src/Devo6.WorkFlow.Cli/EngineLoggingProvider.cs`
- `tests/Devo6.WorkFlow.Tests/HierarchicalLoggingContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/EngineLoggingHierarchyTests.cs`
- `tests/Devo6.WorkFlow.Tests/SwitchBranchLoggingSafetyTests.cs`
- `doc/issue-21-hierarchical-logging-design.md`
- `samples/multi-folder-composite/README.md`
- `.github/workflows/pr-xunit-tests.yml`
- `tasks-status.md` / `phases-status.md`

## 3. 確認結果

### 3.1 Engine

- root Entry、通常 Step、nested `CompositeStep`、選択 branch、retry attempt が別 scope として構成される。
- simple execution は開始、成功、skip、失敗を `StepContext.Logger` へ記録する。
- `If` は `then` / `else`、`Switch` は `case=<value>` / `default` を選択経路だけに付与する。
- branch scope、nested Composite scope、Step scope は `using` で復元され、兄弟 Step や後続実行へ漏れない。
- timeout、cancellation、retry、producer、trace の公開結果契約を変更していない。

### 3.2 CLI logger provider

- `AsyncLocal` scope chain を外側から内側へ走査し、不変な snapshot をログ出力ごとに作成する。
- Text は execution path と attempt を1行へ追加する。
- JSON は既存 field を維持したまま `EntryName`、`StepName`、`BranchName`、`Attempt`、`ExecutionPath` を追加する。
- 内側 Step に attempt がない場合、外側 retry Step の attempt を誤って継承しない。
- `{RootStepName}` は最外側の `EntryName` を使う。

### 3.3 Switch case 表示

- invariant culture で文字列化する。
- 制御文字を空白へ置換する。
- 128文字へ制限する。
- 文字列化失敗時は `<unavailable>` を使い、workflow実行を失敗させない。
- branch定義時の重複検査と空branch検査ではcase値を文字列化せず、`ToString()`が例外を送出する値でも定義と実行を継続できる。

### 3.4 文書と追跡

- nested Composite の lifecycle category は実装どおり `StepContext.Logger` / `Devo6.WorkFlow.Step` として設計書へ同期した。
- サンプル README に通常 Step、nested Composite、`If`、`Switch`、JSON の出力例がある。
- T73-T77 と P31 が完了状態で追跡されている。
- PR xUnit workflow は`contents: read`で動作し、ソースを変更、commit、pushしない。

## 4. 検証証跡

- Red: run `29635422686` / artifact `8426932606`
  - 43件中40件成功、既存skip 1件、意図した未実装失敗2件。
- Green focused: run `29635559813` / artifact `8426981336`
  - 階層ログ10/10成功、関連回帰32件成功、既存skip 1件、失敗0件。
- コード規約修正後: run `29636057186` / artifact `8427137522`
  - 階層ログ、関連回帰、コード規約45件成功、既存skip 1件、失敗0件。
- 最終solution検証: run `29637016261` / artifact `8427438009`
  - restore成功。
  - `dotnet format --verify-no-changes`成功。
  - solution全体303件中300件成功、既存skip 3件、失敗0件。
  - `git diff --check`成功。
  - TRX、restore、format、test、diff checkの各ログをartifactへ保存した。

## 5. 互換性と残リスク

- 公開 Step API、`WorkflowResult`、`ExecutionTrace` のschemaは変更しない。
- Textログを行全体の固定文字列として解析する利用者には表示形式変更の影響がある。機械処理にはJSONまたはstructured logger providerを推奨する。
- JSONはfield追加であり、未知fieldを拒否する厳密consumerには影響し得る。
- artifact保持期間は14日であるため、長期証跡は本報告とPR本文に要約して残す。

## 6. 結論

設計、実装、先行失敗検査、focused / 回帰 / コード規約 / solution全体検査、利用者文書、進捗同期、検証artifactがそろった。Issue #21の受け入れ条件を満たしており、PR #25は取り込み可能な状態である。
