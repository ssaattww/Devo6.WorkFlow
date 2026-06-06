# Sub-agent実行レポート

## タスク

- 目的: T21 の timeout と協調キャンセルについて、設計影響と採用方針候補を調査する。
- タスク種別: investigation / design impact

## sub-agentを使う理由

- 理由: ユーザー指示により、調査作業は sub-agent に委譲し、parent は設計判断、追跡、commit、push を担当するため。

## 対象範囲

- 対象: `doc/workflow_engine_spec.md` の timeout / 協調キャンセル関連節、現在の `IAsyncStep<TOut>`、`WorkflowExecutionOptions`、`CompositeStep` 実行処理、既存検査を確認し、T21 の設計更新案を提示する。

## 対象外

- 対象外: 設計本文の編集、コード実装、検査実装、tracking 更新、commit。

## 実行コマンド

- 実行コマンド:
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-doc-maintainer/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/task-consistency-manager/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/report-output-manager/SKILL.md`
- `sed -n '1,260p' reports/t21-timeout-cancellation-design-impact-20260607001742.md`
- `rg -n "^(##? )?(11\\.4|12\\.1|18\\.|19\\.|21\\.1)|timeout|Timeout|CancellationToken|cancel|キャンセル|停止" doc/workflow_engine_spec.md`
- `rg -n "timeout|Timeout|CancellationToken|Cancel|Canceled|Cancelled|cancellation|cancel|CompositeStep|WorkflowExecutionOptions|WorkflowResult|ExecutionTrace|WorkflowErrorCodes|IAsyncStep" src tests`
- `rg --files src tests | sort`
- `git status --short`
- `sed -n '586,646p' doc/workflow_engine_spec.md`
- `sed -n '1124,1182p' doc/workflow_engine_spec.md`
- `sed -n '1213,1312p' doc/workflow_engine_spec.md`
- `nl -ba src/Devo6.WorkFlow.Engine/CompositeStep.cs | sed -n '96,230p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CompositeStep.cs | sed -n '260,310p'`
- `nl -ba src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
- `nl -ba src/Devo6.WorkFlow.Abstractions/IAsyncStep.cs`
- `nl -ba src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs | sed -n '1,100p'`
- `nl -ba src/Devo6.WorkFlow.Abstractions/WorkflowResult.cs`
- `nl -ba src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
- `nl -ba tests/Devo6.WorkFlow.Tests/AsyncStepApiContractTests.cs | sed -n '1,140p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs | sed -n '1,180p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs | sed -n '45,90p'`
- `nl -ba src/Devo6.WorkFlow.Cli/Program.cs | sed -n '1,90p'`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `test -f package.json && sed -n '1,200p' package.json || true`
- `test -f tools/lint/README.md && sed -n '1,200p' tools/lint/README.md || true`
- `rg --files tools/lint 2>/dev/null | sort || true`
- `npm run lint:md`
- `npx textlint reports/t21-timeout-cancellation-design-impact-20260607001742.md --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)"`
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t21-timeout-cancellation-design-impact-20260607001742.md`
- `node tools/lint/run-skill-script.js review-enforcer/scripts/check-markdown-whitelist.js reports/t21-timeout-cancellation-design-impact-20260607001742.md`

## 対象ファイル

- 変更または確認したファイル:
- 変更: `reports/t21-timeout-cancellation-design-impact-20260607001742.md`
- 確認: `doc/workflow_engine_spec.md`
- 確認: `src/Devo6.WorkFlow.Abstractions/IAsyncStep.cs`
- 確認: `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
- 確認: `src/Devo6.WorkFlow.Abstractions/WorkflowResult.cs`
- 確認: `src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
- 確認: `src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
- 確認: `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- 確認: `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- 確認: `src/Devo6.WorkFlow.Cli/Program.cs`
- 確認: `tests/Devo6.WorkFlow.Tests/AsyncStepApiContractTests.cs`
- 確認: `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
- 確認: `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
- 確認: `package.json`
- 確認: `tools/lint/README.md`

## 指摘事項

- 指摘要約または「指摘なし」:
- 設計書 11.4、12.1、18.2、19.2、21.1 は timeout と協調キャンセルを T21 の未確定事項として明示している。実装前に設計更新が必要。
- 現在の `WorkflowExecutionOptions` は `LoggerFactory` と `EngineArguments` のみを持ち、timeout 指定場所は未実装。
- 現在の `CompositeStep.ExecuteWorkflowAsync` は受け取った `CancellationToken` を `StepRegistration.ExecuteAsync` にそのまま渡すだけで、timeout 用 token 合成、`OperationCanceledException` の分類、後続 Step 停止規則を持たない。
- 現在の例外処理は全 `Exception` を `STEP_EXECUTION_FAILED` に変換するため、timeout や外部 cancellation を導入する場合は catch 順序と結果化規則を設計で固定する必要がある。
- `WorkflowErrorCodes.StepTimeout` は既に公開定数として存在するが、外部 cancellation 用の公開エラーコードは存在しない。
- `ExecutionTraceStepStatus` は `Succeeded` と `Failed` だけで、timeout/canceled/skipped 専用状態はない。T21 では状態 enum を増やすより、`Status = Failed` と `ErrorCode` で区別する案が最小影響。
- 既存テストには「async Step 例外は後続 Step を止める」「同期 Step は pre-cancelled token だけでは `STEP_EXECUTION_FAILED` にならない」がある。T21 ではこれらを壊すか維持するかを明示する必要がある。

## 結果

- 結果:
- 推奨案: timeout 指定場所は `WorkflowExecutionOptions` に nullable な per-step timeout、例: `TimeSpan? StepTimeout`、を追加する。既定は `null` で現行動作を維持する。CLI option、DSL per-step 指定、retry との統合は T21 では対象外に残す。
- 推奨案: `ExecuteWorkflowAsync` の外部 `CancellationToken` と timeout token は Step 実行ごとに `CancellationTokenSource.CreateLinkedTokenSource(...)` で合成し、timeout 設定時だけ `CancelAfter(timeout)` する。async Step には合成 token を渡す。sync Step は現在の `IStep<TOut>.Execute(StepInput)` 契約上、実行中の timeout や cancellation で強制中断しない。
- 推奨案: timeout による `OperationCanceledException` は `WorkflowResult.Succeeded = false`、`ErrorCode = WorkflowErrorCodes.StepTimeout`、`ErrorMessage = "Step '<StepName>' timed out after <timeout>."` 相当に変換する。trace は対象 Step を `ExecutionTraceStepStatus.Failed`、`ErrorCode = STEP_TIMEOUT` として記録し、後続 Step と `Produce` は実行しない。log は Step timeout と Entry failure を warning 以上で残す。
- 推奨案: 外部 token による協調キャンセルは timeout と区別する。公開契約としては `STEP_CANCELED` などの cancellation 用 error code 追加を設計更新候補にする。新 error code を追加しない場合でも、`STEP_EXECUTION_FAILED` に誤分類しない方針を T21 で明示する必要がある。
- 推奨案: 後続 Step 停止規則は、timeout または cancellation を検出した時点で workflow を失敗結果として終了し、後続 Step は開始しない。sync Step 実行中に cancellation が要求された場合は実行中 Step の完了を待ち、完了後に後続 Step を開始しない。sync Step の強制中断はしない。
- 推奨案: `ExecutionTraceStepStatus` に `TimedOut` / `Canceled` / `Skipped` は追加しない。現行 trace 構造を維持し、実行された Step だけを記録する。未実行の後続 Step は trace に合成しない。
- 設計更新対象: `doc/workflow_engine_spec.md` 11.4 に timeout は retry と独立した per-step 協調キャンセルであること、retry と強制停止は対象外であることを追記する。
- 設計更新対象: 12.1 に `CancellationToken` の合成元、async Step へ渡す token、sync Step では強制停止しない方針を追記する。
- 設計更新対象: 18.1 / 18.2 / 18.3 に timeout/cancellation の `WorkflowResult`、error code、trace、log の結果化規則を追記する。
- 設計更新対象: 19.1 / 19.2 / 19.3 に T21 で採用する範囲と残す範囲を反映する。
- 設計更新対象: 21.1 の未確定事項を、採用方針または残課題へ整理する。
- TDD 先行検査案: timeout 付き async Step が token を監視して待機し、timeout 超過で `STEP_TIMEOUT`、trace failed、後続 Step 未実行、`Produce` 未実行になる失敗テストを先に追加する。
- TDD 先行検査案: 外部 `CancellationTokenSource` を async Step 実行中に cancel し、timeout ではなく cancellation として結果化され、後続 Step が止まる失敗テストを先に追加する。
- TDD 先行検査案: sync Step 実行中の timeout/cancellation は強制中断されず、完了後に後続 Step を開始しない、または現行互換として pre-cancelled single sync Step は `STEP_EXECUTION_FAILED` にならない、という採用規則を固定するテストを追加する。
- E2E 先行検査案: `.csx` の `RunAsync` Step で `Task.Delay(..., cancellationToken)` を使い、`CsxEntryLoader` 経由で timeout option を渡した場合に `STEP_TIMEOUT` になる統合検査を追加する。CLI timeout option は T21 対象外なら CLI E2E は追加しない。
- E2E 先行検査案: logging を記録するテスト logger で timeout/cancellation 時の Step/Entry log と error code を確認する。
- Markdown 検査結果: `npm run lint:md` は成功。ただし full lint 対象は `AGENTS.md`、`doc/workflow_engine_spec.md`、`phases-status.md`、`tasks-status.md`、`tools/lint/README.md` で、`reports/` は対象外。
- Markdown 検査結果: 変更レポートへの focused textlint は成功。focused cspell は repo の ignorePaths により `reports/` が除外されたため skipped。focused whitelist コマンドはエラーなし。

## リスク

- 未解決のリスクまたは後続対応:
- 外部 cancellation 用 error code を追加するか、既存 error code 内で表現するかは未決。`STEP_EXECUTION_FAILED` に混ぜると利用者が通常例外と cancellation を区別できない。
- sync Step は `CancellationToken` を受け取らないため、timeout や cancellation は強制中断できない。長時間実行や副作用を止める保証は T21 対象外として明示する必要がある。
- timeout の指定単位を per-workflow total timeout に広げると後続 Step 停止や trace 表現が変わる。T21 では per-step timeout に限定するのが最小影響。
- CLI option を同時に追加すると `Program`、CLI tests、usage 表示まで影響する。T21 では engine option と loader 経由の検査に限定する方が安全。
- `ExecutionTraceStepStatus` を拡張すると公開契約と既存テストに影響する。T21 では error code による区別を優先し、状態追加は後続課題に残す。
- Markdown lint は実行済み。ただし `reports/` は repo の full lint と cspell ignorePaths から外れているため、レポート本文の spell gate は pass ではなく skipped と扱う。
