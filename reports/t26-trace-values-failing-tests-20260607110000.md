# Sub-agent実行レポート

## タスク

T26 値を含む `ExecutionTrace` の検査を検査先行で追加する。

## sub-agentを使う理由

ユーザー指示により、検査追加は sub-agent に委譲する。

## 対象範囲

- `tests/Devo6.WorkFlow.Tests/` 配下の T26 検査
- 既存 trace 契約検査の必要最小限の更新
- 追加検査の失敗または既存実装との差分記録

## 対象外

- `src/` 配下の実装変更
- 設計書と進捗ファイルの編集
- 既存テスト名の一括変更

## 実行コマンド

- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/tdd-executor/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/implementation-executor/SKILL.md`
- `sed -n '1,260p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,260p' reports/t26-trace-values-failing-tests-20260607110000.md`
- `sed -n '1,260p' reports/t26-trace-values-design-impact-20260607102000.md`
- `sed -n '1,260p' tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
- `sed -n '1,260p' tests/Devo6.WorkFlow.Tests/ProduceValueLifetimeContractTests.cs`
- `sed -n '1,260p' tests/Devo6.WorkFlow.Tests/RetryExecutionContractTests.cs`
- `sed -n '1,260p' tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs`
- `sed -n '1,220p' src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
- `rg -n "Produce<|StoreAs\\(|Discard\\(|ExecuteWorkflow|class CompositeStep|record|enum" src/Devo6.WorkFlow.Engine src/Devo6.WorkFlow.Abstractions tests/Devo6.WorkFlow.Tests -g '*.cs'`
- `sed -n '1,260p' src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `sed -n '260,620p' src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `sed -n '1,220p' tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj`
- `sed -n '1,160p' src/Devo6.WorkFlow.Abstractions/StepInput.cs`
- `sed -n '260,660p' tests/Devo6.WorkFlow.Tests/RetryExecutionContractTests.cs`
- `sed -n '220,520p' tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs`
- `dotnet test Devo6.WorkFlow.sln --filter TraceValueContractTests`
- `dotnet test Devo6.WorkFlow.sln`
- `git diff --check`
- `git status --short`
- `sed -n '1,240p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `ls tools/lint package.json .textlintrc.json cspell.config.jsonc 2>/dev/null`
- `sed -n '1,200p' tools/lint/README.md`
- `sed -n '1,220p' package.json`
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t26-trace-values-failing-tests-20260607110000.md`
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t26-trace-values-failing-tests-20260607110000.md`

## 対象ファイル

- `tests/Devo6.WorkFlow.Tests/TraceValueContractTests.cs`
  - T26 の trace value capture 契約検査を新規追加した。
  - 追加した検査は `DefaultTraceDoesNotCaptureProducedValues`、`ExplicitTraceCaptureRecordsTypedProducedValue`、`ExplicitTraceCaptureRecordsNamedProducedValue`、`StoreAsTraceCaptureRecordsStoredOutput`、`RedactedProducedValueDoesNotExposeSerializedValue`、`NonSerializableProducedValueIsMarkedWithoutFailingWorkflow`、`FailedAttemptDoesNotCaptureProducedValues`、`ProduceFailureDoesNotCaptureProducedValues` である。
- `reports/t26-trace-values-failing-tests-20260607110000.md`
  - 実行コマンド、対象ファイル、指摘事項、結果、リスクを追記した。

確認のみの主な参照ファイルは次である。

- `reports/t26-trace-values-design-impact-20260607102000.md`
- `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/ProduceValueLifetimeContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/RetryExecutionContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs`
- `src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `src/Devo6.WorkFlow.Abstractions/StepInput.cs`

`src/` 配下、`doc/workflow_engine_spec.md`、`tasks-status.md`、`phases-status.md` は編集していない。

## 指摘事項

1. T26 の想定 API は現行実装に存在しないため、追加検査はコンパイル失敗で赤になった。
   - `ExecutionTraceStep.ProducedValues` が未定義である。
   - `TraceValueCapture` が未定義である。
   - `Produce` / `StoreAs` の trace capture overload が未定義である。

2. 追加検査は、現行の既定動作を壊さずに「明示 opt-in だけ trace value を残す」契約を固定している。既存 `Produce` / `StoreAs` は `ProducedValues` 空、`Discard` は値を生成しない前提で、T26 実装側が既定無効を維持する必要がある。

3. 失敗時の検査は、retry 途中失敗 attempt と producer 失敗 trace に値を残さない契約を固定している。現行 producer は `Action<StepInput, object?>` なので、実装時は producer 成功後だけ trace value を確定する構造変更が必要である。

## 結果

- `tests/Devo6.WorkFlow.Tests/TraceValueContractTests.cs` を新規追加し、T26 の要求検査 8 件を検査先行で追加した。
- `dotnet test Devo6.WorkFlow.sln --filter TraceValueContractTests` は失敗した。主な compile error は次である。
  - `CS1061: 'ExecutionTraceStep' does not contain a definition for 'ProducedValues'`
  - `CS0103: The name 'TraceValueCapture' does not exist in the current context`
- `dotnet test Devo6.WorkFlow.sln` も同じ compile error で失敗した。
- `git diff --check` は成功した。
- focused textlint は成功した。
- focused cspell は repo 設定の `ignorePaths` により `reports/` 配下が skip され、0 files checked、issues 0 で終了した。

追加検査は赤である。赤の理由は、T26 の public API と overload がまだ未実装であるためのコンパイル失敗である。

## リスク

T26 実装担当が採用する API 名、enum 名、capture status 名、source 表現が追加検査の想定とずれる場合、テスト名と検査意図は維持しつつ assertion 名称の調整が必要になる。

直列化失敗理由は `SerializationFailureReason` に `"serialization"` を含むことだけを確認している。実装側で秘匿された短い message にする場合でも、この文字列契約が過度に強い可能性がある。

作業中に `doc/workflow_engine_spec.md` の変更が作業ツリーに存在したが、編集禁止範囲のため触っていない。設計書担当の変更とこの検査の API 想定が同期しているかは、後続の親側統合で確認が必要である。

`reports/` は cspell の ignore 対象であり、対象レポートの spelling gate は repo 設定どおり skip された。
