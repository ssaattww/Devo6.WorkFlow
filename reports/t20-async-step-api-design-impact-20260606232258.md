# Sub-agent実行レポート

## タスク

- 目的: T20 の非同期 Step API について、設計影響と採用方針候補を調査する。
- タスク種別: investigation / design impact

## sub-agentを使う理由

- 理由: ユーザー指示により、調査作業は sub-agent に委譲し、parent は設計判断、追跡、commit、push を担当するため。

## 対象範囲

- 対象: `doc/workflow_engine_spec.md` の非同期 API 関連節、現在の公開 API、`CompositeStep` 実行処理、既存検査を確認し、T20 で採用すべき設計更新案を提示する。

## 対象外

- 対象外: 設計本文の編集、コード実装、検査実装、tracking 更新、commit。

## 実行コマンド

- 実行コマンド:
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-doc-maintainer/SKILL.md`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/task-consistency-manager/SKILL.md`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/report-output-manager/SKILL.md`
  - `sed -n '1,260p' reports/t20-async-step-api-design-impact-20260606232258.md`
  - `rg -n "^(#|##|###) |^12\.|^19\.3|^21\.1|非同期|async|Async|Step" doc/workflow_engine_spec.md`
  - `rg --files`
  - `sed -n '1,220p' tasks-status.md`
  - `sed -n '1,220p' phases-status.md`
  - `git status --short -- reports/t20-async-step-api-design-impact-20260606232258.md doc/workflow_engine_spec.md src tests tasks-status.md phases-status.md tools/lint`
  - `sed -n '600,636p' doc/workflow_engine_spec.md`
  - `sed -n '1130,1176p' doc/workflow_engine_spec.md`
  - `sed -n '1204,1215p' doc/workflow_engine_spec.md`
  - `sed -n '1,220p' src/Devo6.WorkFlow.Abstractions/IStep.cs`
  - `sed -n '1,260p' src/Devo6.WorkFlow.Abstractions/StepInput.cs`
  - `sed -n '1,260p' src/Devo6.WorkFlow.Abstractions/WorkflowResult.cs`
  - `sed -n '1,320p' src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - `sed -n '1,220p' src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
  - `sed -n '1,320p' tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`
  - `sed -n '1,260p' tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
  - `sed -n '1,320p' tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`
  - `sed -n '1,240p' tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs`
  - `rg -n "IStep<|Execute\(|ExecuteWorkflow|Task<|CancellationToken|CompositeStep|WorkflowResult|StepExecutionFailed" src tests`
  - `sed -n '1,130p' src/Devo6.WorkFlow.Cli/Program.cs`
  - `sed -n '1,120p' src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - `sed -n '340,390p' src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - `sed -n '560,585p' src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - `sed -n '1,130p' src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
  - `sed -n '1,160p' src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
  - `sed -n '1,200p' package.json`
  - `sed -n '1,180p' tools/lint/README.md`
  - `sed -n '1,160p' tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj`
  - `dotnet test Devo6.WorkFlow.sln`
  - `npm run lint:md`
  - `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t20-async-step-api-design-impact-20260606232258.md`
  - `npx cspell --config cspell.config.jsonc reports/t20-async-step-api-design-impact-20260606232258.md`
  - `sed -n '1,220p' cspell.config.jsonc`
  - `node tools/lint/run-skill-script.js review-enforcer/scripts/list-markdown-targets.js`
  - `npx cspell --help`
  - `npx cspell check reports/t20-async-step-api-design-impact-20260606232258.md`

## 対象ファイル

- 変更または確認したファイル:
  - 変更: `reports/t20-async-step-api-design-impact-20260606232258.md`
  - 確認: `doc/workflow_engine_spec.md`
  - 確認: `tasks-status.md`
  - 確認: `phases-status.md`
  - 確認: `src/Devo6.WorkFlow.Abstractions/IStep.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/StepInput.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/WorkflowResult.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
  - 確認: `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - 確認: `src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
  - 確認: `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - 確認: `src/Devo6.WorkFlow.Cli/Program.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`
  - 確認: `package.json`
  - 確認: `tools/lint/README.md`

## 指摘事項

- 指摘要約または「指摘なし」:
  - T20 は `tasks-status.md` で未着手、P7 は `phases-status.md` で未着手として存在し、非同期 Step API の実装 task は追跡上は表現済み。
  - 現行の公開 Step 契約は `IStep<TOut>.Execute(StepInput input)` の同期 API のみ。`CompositeStep<TOut>` も `IStep<TOut>` を実装し、`Run<TStep, TOut>()` は `where TStep : IStep<TOut>, new()` に固定されている。
  - 現行の実行処理は `CompositeStep<TOut>.Execute` と `ExecuteWorkflow` の両方で `StepRegistration.Execute` を同期呼び出しし、戻り値取得後に `Produce` を実行する。例外は `STEP_EXECUTION_FAILED` の `WorkflowResult` と trace に変換される。
  - `CsxEntryLoader` は script 実行だけ `RunAsync(...).GetAwaiter().GetResult()` で同期化し、Entry 実行は反射で `ExecuteWorkflow(WorkflowExecutionOptions)` を呼ぶ。CLI も `Main` が同期で `loader.Execute(...)` を呼ぶ。
  - `WorkflowExecutionOptions` は logger と `EngineArguments` のみで、`CancellationToken` や timeout 設定はまだ保持しない。`WorkflowErrorCodes.StepTimeout` は定数だけ存在する。
  - 既存検査は同期 `IStep`、同期 `CompositeStep`、同期 CLI / csx 実行を広く固定している。T20 で `IStep<TOut>` を `Task<TOut>` 系へ統一すると、既存利用例、テスト、csx サンプル、反射実行経路を広範囲に書き換える破壊的変更になる。
  - `IAsyncStep<TOut>` 追加案は既存 `IStep<TOut>` を温存でき、T20 の「同期 Step との共存」「定義順待機」「Produce 値渡し」「例外結果化」という完了条件に合う。採用する場合は `RunAsync<TStep, TOut>()` など明示名の API にして、既存 `Run<TStep, TOut>()` の制約と呼び出し互換を維持するのが安全。
  - `IStep<TOut>` を `Task<TOut>` へ統一する案は API が単純になり、timeout と `CancellationToken` を全 Step に一律適用しやすい。一方で、T20 単体で既存同期 API を壊すため、次期 major または破壊的変更 phase の候補として扱う方がよい。
  - async 採用時は `IStep<Task<T>>` を非同期 Step とみなすのかを明記する必要がある。推奨は、同期 `IStep<Task<T>>` は単なる戻り値型として扱い、エンジンが await する対象は `IAsyncStep<TOut>` のみと定義すること。

## 結果

- 結果:
  - この調査では親側ワークフローへの再入、`codex exec`、nested Codex、src / tests / 設計本文 / tracking / tools/lint の編集は行っていない。
  - 推奨案: T20 では `IAsyncStep<TOut>` を追加し、既存 `IStep<TOut>` を維持する。`CompositeStep` には同期 Step 用の既存 `Run<TStep, TOut>()` を残し、非同期 Step 用に明示的な `RunAsync<TStep, TOut>()` を追加する案を第一候補にする。
  - 推奨実行契約: `IAsyncStep<TOut>.ExecuteAsync(StepInput input, CancellationToken cancellationToken)` を追加し、`CompositeStep` の engine 実行経路は await 後に `Produce` を実行する。sync Step と async Step が混在しても定義順に待機する。
  - 推奨公開 API: 既存 `CompositeStep<TOut>.Execute(StepInput)` と `ExecuteWorkflow(WorkflowExecutionOptions?)` は同期互換のため維持する。async Step を含む entry の通常実行には `ExecuteAsync` または `ExecuteWorkflowAsync` を追加し、`CsxEntryLoader` / CLI は async entry 実行を待機する。
  - 破壊的変更判定: `IAsyncStep<TOut>` 追加案は既存 `IStep<TOut>` 実装を壊さない additive change として設計できる。ただし `CompositeStep` の内部登録、反射実行、CLI 実行経路には実装変更が必要。`IStep<TOut>` の `Task<TOut>` 統一案は公開 API と既存 csx 利用例を壊す破壊的変更。
  - 設計更新対象: `doc/workflow_engine_spec.md` の 12.1 で `IAsyncStep<TOut>` 採用方針、同期 Step との共存、`CancellationToken` 受け渡し、`IStep<Task<T>>` の扱いを確定する。
  - 設計更新対象: `doc/workflow_engine_spec.md` の 14.1 と 14.4 相当の公開 API 案に `IAsyncStep<TOut>`、`RunAsync`、`ExecuteWorkflowAsync` を追加する。
  - 設計更新対象: `doc/workflow_engine_spec.md` の 17.2 相当の検証対象に `IStep<TOut>` または `IAsyncStep<TOut>` 実装確認を追加する。
  - 設計更新対象: `doc/workflow_engine_spec.md` の 18.1 から 18.4 相当で、async Step の await 後に成功 trace を記録し、例外時は現行と同じ `STEP_EXECUTION_FAILED` にすることを確認する。
  - 設計更新対象: `doc/workflow_engine_spec.md` の 19.3 と 21.1 で、非同期 Step API の未確定事項を T20 方針へ更新し、timeout と協調キャンセルの詳細は T21 に残す。
  - TDD 方針: 先に公開 API 検査として `IAsyncStep<TOut>` の形、`RunAsync` の型制約、既存 `Run` / `IStep` 互換が壊れないことを固定する。
  - TDD 方針: `CompositeStep` 検査で、sync -> async -> sync の順序、async 戻り値を await してから `Produce` すること、async 例外が `WorkflowResult` 失敗と trace の `STEP_EXECUTION_FAILED` になること、失敗後に後続 Step が実行されないことを先に書く。
  - E2E 方針: `.csx` で `IAsyncStep<TOut>` を実装する entry を `engine run` で実行し、非同期処理完了後の marker または戻り値効果を確認する。`validate` は async Step をコンパイル、識別するが Step 本体を実行しないことも確認する。
  - T21 先行検査案: T20 では `CancellationToken` が async Step へ渡ることだけを固定し、timeout 超過時の `STEP_TIMEOUT`、後続 Step 停止、trace / log 記録は T21 の failing test に分ける。
  - 実装 task 切り方: T20a 設計更新、T20b `IAsyncStep<TOut>` と公開 API 検査、T20c `CompositeStep` async 登録と engine 実行、T20d `CsxEntryLoader` / CLI async E2E、T20e 回帰検査と review に分ける。
  - 検査結果: `dotnet test Devo6.WorkFlow.sln` は成功。60 件成功、失敗 0、skip 0。
  - 検査結果: `npm run lint:md` は成功。ただし repo 通常 target は `AGENTS.md`、`doc/workflow_engine_spec.md`、`phases-status.md`、`tasks-status.md`、`tools/lint/README.md` で、`reports/**` は cspell 設定上の除外対象。
  - 検査結果: 明示ファイル指定の `npx textlint ... reports/t20-async-step-api-design-impact-20260606232258.md` は成功。
  - 検査結果: `npx cspell --config ... reports/t20-async-step-api-design-impact-20260606232258.md` は設定の `ignorePaths` により対象 0 件だった。代替として `npx cspell check reports/t20-async-step-api-design-impact-20260606232258.md` を実行し、問題は検出されなかった。

## リスク

- 未解決のリスクまたは後続対応:
  - このレポートでは設計本文を編集していないため、T20 実装前に design-doc-maintainer 経由で `doc/workflow_engine_spec.md` の更新が必要。
  - async Step を含む `CompositeStep<TOut>` を同期 `IStep<TOut>.Execute` から呼ぶ場合の扱いは要設計。推奨は、async Step を含む entry は async engine 経路で実行し、同期 `Execute` で暗黙にブロックしないこと。
  - nested `CompositeStep` の扱いに注意が必要。同期 composite は `IStep<TOut>` のまま扱えるが、async composite を下位 Step として使うなら `IAsyncStep<TOut>` 実装または専用登録経路が必要。
  - sync Step には `CancellationToken` を直接渡せないため、T21 の timeout / cancel で同期処理を協調停止させることはできない。同期 Step の timeout は実行結果化の範囲と限界を別途明記する必要がある。
  - `CsxEntryLoader` の反射実行は `Task<WorkflowResult>` を unwrap する実装が必要。CLI `Main` を async 化するか、明示的に待機するかを決める必要がある。
  - `IAsyncStep<TOut>` と `IStep<TOut>` の両方を同一 Step が実装した場合の優先順位は未確定。曖昧さを避けるには `Run` と `RunAsync` の明示 API で選択させるのがよい。
  - reports は通常 Markdown lint target から外れているため、report 向けの cspell は通常 gate ではなく明示 `check` の補助証跡として扱った。
