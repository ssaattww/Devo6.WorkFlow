# Sub-agent実行レポート

## タスク

T56 `RunIf` と `TapIf` の条件付き実行を実装する。

## sub-agentを使う理由

親は進捗、レビュー、コミット管理を担当し、検査先行の実装作業を独立した実装担当へ委譲するため。

## 対象範囲

- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
- `tests/Devo6.WorkFlow.Tests/RunIfTapIfContractTests.cs`
- 必要な最小限の既存検査調整

## 対象外

- `If`
- `Switch`
- `BranchBuilder`
- README と sample の更新
- コミット、送信、取り込み依頼操作

## 実行コマンド

- `dotnet test Devo6.WorkFlow.sln --filter RunIfTapIf`
  - 実装前失敗確認: 失敗。`RunIf` / `TapIf` / `RunIfAsync` / `TapIfAsync`、`ExecutionTraceStepStatus.Skipped`、`WorkflowErrorCodes.ConditionEvaluationFailed` が未定義でコンパイル失敗。
- `dotnet test Devo6.WorkFlow.sln --filter RunIfTapIf`
  - 実装後成功確認: 成功。11 件成功。
- `dotnet test Devo6.WorkFlow.sln --filter "RunIfTapIf|LambdaStep|Retry|Timeout|TraceValue|CodingStandards|StandardConfig"`
  - 実装後回帰確認: 成功。85 件成功。
- `git diff --check`
  - 成功。

## 対象ファイル

- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
- `src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
- `tests/Devo6.WorkFlow.Tests/RunIfTapIfContractTests.cs`
- `reports/t56-runif-tapif-implementation-20260610102000.md`

## 指摘事項

- `Skipped` trace status は `ExecutionTraceStepStatus` に存在しなかったため、T56 契約を満たす最小変更として `ExecutionTrace.cs` に enum 値を追加した。
- `tasks-status.md` と `reports/t56-runif-tapif-review-20260610103000.md` は作業前から差分または未追跡として見えているが、今回の担当範囲外のため編集していない。

## 結果

- `RunIf` / `RunIfAsync` と同一型省略 API を追加した。
- `TapIf` / `TapIfAsync` を追加し、`Unit` Step 実行時も現在値を維持するようにした。
- 条件 false 時は Step 本体を実行せず、`RunIf` は `otherwise` 値、同一型 `RunIf` と `TapIf` は現在値を維持し、trace を `Skipped` にするようにした。
- `RunIf` / `TapIf` false 時も `Produce` / `StoreAs` と trace value capture が通常どおり動くことを検査した。
- 条件判定例外を `CONDITION_EVALUATION_FAILED` として扱い、Step 本体例外は既存 retry 対象のまま維持した。

## リスク

- `ExecutionTrace.cs` は当初の編集許可一覧外だが、`Skipped` enum 値がないと必須契約を実装できないため最小変更した。
- `otherwise` 評価中の通常例外は既存の Step 実行失敗扱いとして `STEP_EXECUTION_FAILED` になる。
