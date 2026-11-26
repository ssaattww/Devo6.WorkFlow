# 課題 #27: 公開 Run の取消例外分類

## 対象と契約

設計書 `doc/issue-27-dotnet10-file-based-app-design.md` §5 では、選択した `.cs` Provider を `run` だけで一度生成し、`Create()` を一度呼ぶ。公開 `.cs` Run は `FileBasedWorkflowExecutor.Execute` から `CompositeStep.ExecuteWorkflowAsync(options, cancellationToken)` を呼ぶ。

| 境界 | 外部 token | 既存の処理 | 対応 |
|---|---|---|---|
| RunIf / TapIf の条件、同期・非同期 Step 版 | 取消済みで OCE | `EvaluateCondition` が `StepConditionEvaluationException` に包み、`CONDITION_EVALUATION_FAILED` を返す | `STEP_CANCELED` に分類 |
| 同じ条件 callback | 未取消で OCE | `CONDITION_EVALUATION_FAILED` | 維持 |
| 同じ条件 callback | 通常例外 | `CONDITION_EVALUATION_FAILED` | 維持 |
| Provider constructor / `Create()` | 取消済みで OCE | reflection 例外を `SCRIPT_ENTRY_PROVIDER_FAILED` に分類 | `STEP_CANCELED` に分類 |
| Provider constructor / `Create()` | 未取消で OCE、または通常例外 | `SCRIPT_ENTRY_PROVIDER_FAILED` | 維持 |

`CompositeStep.ExecuteAsync(StepInput, CancellationToken)` は別の公開 API である。`.cs` Run の executor はこれを呼ばず、`ExecuteWorkflowAsync` を reflection で呼び出す。今回の差分は `ExecuteAsync` に触れず、実行経路でもない既存 API の動作は変更していない。

## 変更

- `ExecuteWorkflowStepSequenceAsync` の条件評価例外 catch に、外部 token が取消済みかつ内側例外が `OperationCanceledException` の場合を追加し、共通の `STEP_CANCELED` trace/result/log 変換を使う。
- `FileBasedWorkflowExecutor` では Provider の `Activator.CreateInstance` と `Create()` 呼出しを個別に囲み、reflection wrapper をほどいた例外が OCE で外部 token も取消済みの場合だけ既存の `Cancelled` 結果にする。
- 通常例外、未取消 OCE、Build、`.csx`、timeout の処理は変更しない。

## 検証

- 変更前の対象検査30ケース中、取消済み OCE の10ケースが失敗。RunIf/TapIf とその async 版4ケースは `CONDITION_EVALUATION_FAILED`、Provider constructor/Create 2ケースは `SCRIPT_ENTRY_PROVIDER_FAILED`。未取消 OCE と通常例外の20ケースは既存診断のまま成功した。
- 修正後の拡張した対象検査40件が成功。RunIf/TapIf と async 版について、取消済み OCE、token 取消後の通常例外、未取消 OCE、通常例外を共通 Engine と公開 `.cs` Run の両方で確認。Provider constructor/Create についても取消済み OCE、token 取消後の通常例外、未取消 OCE、通常例外を公開 Run で確認し、失敗時の diagnostic session path を検査した。
- 関連する RunIf/TapIf、file-based lifecycle / executor、timeout、If、Switch の集合検査: 131件成功、既存 skip 2件。
- `CodingStandardsContractTests.CSharpDeclarationsFollowT31CodingStandards`: 1件成功。
- `dotnet build tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --no-restore --framework net8.0`: 成功、警告0、エラー0。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes --no-restore` と `git diff --check`: 成功。
- 使用 SDK 10.0.404 / runtime 10.0.15。`dotnet --list-runtimes` に .NET 8 runtime はなく、標準 .NET 8 での試験実行は未確認。全件ローカル試験と push 後の自動 CI は別途確認する。
- Markdown lint は公式 Skill checkout (`CODEX_SKILLS_DIR=/tmp/CodexSkill/skills`) を指定して試したが、textlint 実行ファイルがなく、CSpell と許可語検査も Node package `yaml` がなく実行不能。依存は追加していない。
