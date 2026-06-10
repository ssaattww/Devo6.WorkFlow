# Sub-agent実行レポート

## タスク

T56 `RunIf` / `TapIf` レビュー指摘の修正。

## sub-agentを使う理由

レビューで見つかった共有可変状態の不具合と検査不足を、実装担当へ小さく切り出して修正するため。

## 対象範囲

- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `tests/Devo6.WorkFlow.Tests/RunIfTapIfContractTests.cs`
- 必要な最小限の関連ファイル
- `reports/t56-runif-tapif-review-fix-20260610104500.md`

## 対象外

- `If`
- `Switch`
- `BranchBuilder`
- README と sample の更新
- コミット、送信、取り込み依頼操作

## 実行コマンド

- `dotnet test Devo6.WorkFlow.sln --filter RunIfTapIf`
  - 成功。15 件成功。
- `dotnet test Devo6.WorkFlow.sln --filter "RunIfTapIf|LambdaStep|Retry|Timeout|TraceValue|CodingStandards|StandardConfig"`
  - 成功。89 件成功。
- `git diff --check`
  - 成功。
- `rg -n "LastStatus|SetLastStatus" src/Devo6.WorkFlow.Engine/CompositeStep.cs tests/Devo6.WorkFlow.Tests/RunIfTapIfContractTests.cs`
  - 該当なし。

## 対象ファイル

- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `tests/Devo6.WorkFlow.Tests/RunIfTapIfContractTests.cs`
- `reports/t56-runif-tapif-review-fix-20260610104500.md`

## 指摘事項

- High 指摘の `StepRegistration.LastStatus` 共有可変状態を削除し、`StepExecutionResult.Status` を workflow 実行ローカル変数として trace 追加まで保持する形へ修正した。
- Medium 指摘の不足検査として、並行 `RunIf` status 分離、`RunIfAsync` false と非同期 otherwise、`TapIfAsync` false、async / StepInput overload の null 引数、`TapIf` 条件判定例外を追加した。
- 追加の未対応指摘はなし。

## 結果

- 同じ `CompositeStep` instance を並行実行しても、`Skipped` と `Succeeded` の trace status が実行間で混ざらない構造になった。
- async false path でも `Skipped` trace と ProducedValues が記録されることを検査した。
- `TapIf` 条件判定例外が `CONDITION_EVALUATION_FAILED` として失敗し、retry されないことを検査した。
- 指定された dotnet test 2 件と `git diff --check` は成功した。

## リスク

- T56 対象外の `If` / `Switch` / `BranchBuilder`、README、sample、設計書、進捗ファイルは未変更。
- 既存の対象外差分と未追跡ファイルは作業前から存在しており、本修正では戻していない。
