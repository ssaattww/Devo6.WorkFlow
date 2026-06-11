# Sub-agent実行レポート

## タスク

T57 `If` と分岐構築 API 実装のレビュー。

## sub-agentを使う理由

分岐実行が設計契約、TDD、Config、trace、retry、timeout、XML コメント標準を満たすか独立して点検するため。

## 対象範囲

- T57 実装差分
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `tests/Devo6.WorkFlow.Tests/IfBranchContractTests.cs`
- `reports/t57-if-branch-builder-implementation-20260610112000.md`

## 対象外

- `Switch`
- README と sample の更新
- コミット、送信、取り込み依頼操作

## 実行コマンド

- `sed -n '1,260p' reports/t57-if-branch-builder-review-20260610113000.md`
  - 結果: 成功。既存 report の見出し順、空行、埋め済み text を確認。
- `sed -n '1,260p' reports/t57-if-branch-builder-implementation-20260610112000.md`
  - 結果: 成功。実装 report の検証記録と実差分を照合。
- `git status --short`
  - 結果: 成功。T57 対象 file と task tracking の作業中差分を確認。
- `git diff -- src/Devo6.WorkFlow.Engine/CompositeStep.cs tests/Devo6.WorkFlow.Tests/IfBranchContractTests.cs reports/t57-if-branch-builder-implementation-20260610112000.md reports/t57-if-branch-builder-review-20260610113000.md`
  - 結果: 成功。T57 対象差分を確認。
- `rg -n "If<|BranchBuilder|ExecuteSimpleStepSequenceAsync|ExecuteWorkflowStepSequenceAsync|ExecuteIfStepAsync|EnsureBranchHasSteps|CreateIf|ConditionalBranchRegistration|BranchExecutionPlan|WorkflowSequenceExecutionResult|IfStepRegistrationMarker" src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - 結果: 成功。If / BranchBuilder 関連実装位置を確認。
- `rg -n "StepConfigRegistrations|WithStepConfigs|StepConfigValue|Validate|DefaultConfig" -S src tests | head -n 200`
  - 結果: 成功。Step Config metadata と実行時登録経路を確認。
- `rg -n "StepIndex|step index|Step Config|WithConfig|StandardConfig" tests/Devo6.WorkFlow.Tests src/Devo6.WorkFlow.Engine -S`
  - 結果: 成功。StepIndex と WithConfig 契約の既存検査を確認。
- `dotnet test Devo6.WorkFlow.sln --filter IfBranch`
  - 結果: 成功。Failed: 0, Passed: 10, Skipped: 0, Total: 10。
- `dotnet test Devo6.WorkFlow.sln --filter "IfBranch|RunIfTapIf|LambdaStep|Retry|Timeout|TraceValue|CodingStandards|StandardConfig"`
  - 結果: 成功。Failed: 0, Passed: 99, Skipped: 0, Total: 99。
- `git diff --check`
  - 結果: 成功。
- `npm run lint:md`
  - 結果: 成功。親確認済みの Markdown gate も reviewer 側で再確認。
- `npm run lint:md:terms`
  - 結果: 成功。親確認済みの Markdown terms gate も reviewer 側で再確認。

## 対象ファイル

- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `tests/Devo6.WorkFlow.Tests/IfBranchContractTests.cs`
- `reports/t57-if-branch-builder-implementation-20260610112000.md`
- `reports/t57-if-branch-builder-review-20260610113000.md`
- 参照: `tasks-status.md`

## 指摘事項

- Blocking: `src/Devo6.WorkFlow.Engine/CompositeStep.cs:581` と `src/Devo6.WorkFlow.Engine/CompositeStep.cs:600`
  - 分岐後に追加した通常 Step へ `.WithConfig(...)` を付けると、Step Config の `StepIndex` が flattened 実行 index ではなく `steps.Count - 1` で登録されます。T57 では If の実行時 index を `GetFlattenedStepCount(...)` と `step.FlattenedLength` で扱うようになっているため、例えば `Run -> If(then 1 step, else 1 step) -> Run<After>().WithConfig<AfterConfig>()` は after Step の実行 index が 4 になる一方、metadata は 2 になります。この index は then branch 先頭と衝突し、after Step 実行直前に Config が登録されません。branch 内 Config 自体は `RemapBranchConfigRegistrations(...)` で両分岐分を事前登録できていますが、If の後続 Step Config が通常経路で壊れるため修正が必要です。
- User confirmation required capability gap: なし。
- Held non-blocking concern: なし。

## 結果

- 指摘あり。T57 の If / BranchBuilder は選択 branch のみを実行し、未選択 branch を trace しない検査、空 branch 禁止、両分岐同一 `TNext` API、branch 内 Lambda / RunIf / TapIf / nested If / Produce / StoreAs / Discard、retry / timeout / condition failure の主要検査を通過しています。
- public / internal API と test method の XML コメント標準は対象範囲内で満たしています。
- 実装 report の検証記録は、targeted test、広めの dotnet test、`git diff --check`、Markdown gate の成功と整合しています。
- ただし、If 後続 Step の `.WithConfig(...)` StepIndex が flattened index になっていない blocking finding があるため、T57 は review 完了前に修正が必要です。

## リスク

- 現在の test は branch 内 Config を検査していますが、If の後続 Step に Step Config を付ける regression を直接検出していません。修正時は `If` の後ろに `.Run<...>().WithConfig<...>()` を置く検査を追加し、selected branch によらず後続 Step が Config を取得できることを確認してください。
- T58 `Switch`、README、sample は対象外として未確認。
