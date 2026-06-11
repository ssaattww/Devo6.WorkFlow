# Sub-agent実行レポート

## タスク

T57 `If` と分岐構築 API の分岐定義を実装する。

## sub-agentを使う理由

親は進捗、レビュー、コミット管理を担当し、検査先行の実装作業を独立した実装担当へ委譲するため。

## 対象範囲

- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `tests/Devo6.WorkFlow.Tests/IfBranchContractTests.cs`
- 必要な最小限の既存検査調整

## 対象外

- `Switch`
- README と sample の更新
- コミット、送信、取り込み依頼操作

## 実行コマンド

- 失敗確認: `dotnet test Devo6.WorkFlow.sln --filter IfBranch`
  - 結果: 失敗。`CompositeStep<T>.If` と `BranchBuilder<T>` が未実装のためコンパイル失敗。
- 実装後: `dotnet test Devo6.WorkFlow.sln --filter IfBranch`
  - 結果: 成功。Failed: 0, Passed: 10, Skipped: 0, Total: 10。
- 実装後: `dotnet test Devo6.WorkFlow.sln --filter "IfBranch|RunIfTapIf|LambdaStep|Retry|Timeout|TraceValue|CodingStandards|StandardConfig"`
  - 結果: 成功。Failed: 0, Passed: 99, Skipped: 0, Total: 99。
- 実装後: `git diff --check`
  - 結果: 成功。
- report 更新後: `npm run lint:md`
  - 結果: 成功。

## 対象ファイル

- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `tests/Devo6.WorkFlow.Tests/IfBranchContractTests.cs`
- `reports/t57-if-branch-builder-implementation-20260610112000.md`

## 指摘事項

- `Switch` は対象外のため未実装。
- nested Codex / development-orchestrator への再入は禁止指定のため未実施。

## 結果

- `CompositeStep<TOut>.If<TNext>` の `Func<TOut,bool>` 版と `Func<TOut,StepInput,bool>` 版を追加。
- `BranchBuilder<TOut>` に通常 Step、非同期 Step、Lambda Step、RunIf / RunIfAsync / TapIf / TapIfAsync、入れ子 If、WithConfig、Produce、StoreAs、Discard を追加。
- If は選択 branch のみを実行し、未選択 branch の実行単位を trace に出さない。
- branch 内 Step は既存の retry / timeout / trace / producer / Step Config 登録経路で実行される。
- branch 内 Config metadata は then / else の両方を `StepConfigRegistrations` に含める。
- 空 branch は定義時に `InvalidOperationException` とし、明示 passthrough を要求する契約にした。

## リスク

- If 制御単位自体は成功時に trace step として記録する。選択 branch 内 Step も trace に続けて記録される。
- branch Config の StepIndex は If、then branch、else branch の flatten 順に割り当てているため、外部で index を直接扱う検査や利用はこの順序に依存する。
