# 課題 #27: Step 呼び出し境界の外部取消

## 変更

`CompositeStep` は workflow 外部の token を `StepRegistration` の共通実行ラッパーと登録デリゲートへ分けて渡す。登録デリゲート内の利用者コードを評価した後、同期・非同期 Step 本体へ入る直前にも外部 token を確認する。取消済みなら `OperationCanceledException` を既存の捕捉経路へ渡し、`STEP_CANCELED` を返す。timeout 用の合成 token ではなく外部 token を確認するため、同期 Step の timeout 動作は変えない。

| 登録形態 | 利用者コードの順序 | 取消確認位置 |
|---|---|---|
| 型付き `Run` / `RunAsync` | `new TStep()` → `Execute` / `ExecuteAsync` | constructor の後、本体の直前 |
| `RunIf` / `TapIf` と async 版 | `when` →（true の場合）constructor → Step 本体 | 条件後、constructor 後 |
| Lambda 登録 | 登録デリゲートから利用者 body | 共通ラッパーと body 呼出し直前 |
| `BranchBuilder` 登録 | 上記と同じ `StepRegistration` factory | 同じ確認を共有 |

共通 Engine の `RunIf` / `TapIf` 条件、型付き Step constructor と、ファイルベース `.cs` の公開 `Run` 経路に回帰検査を追加した。公開 `Run` は `Create()` 完了後の条件評価または constructor をファイル gate で停止し、その間に外部取消してから gate を解除する。Step 本体の副作用印が作られないこと、取消結果と診断 session が維持されることを検査する。

## 検証

- 修正前の対照: 新しい共通 Engine の条件2件・constructor 1件と、公開 `Run` の `RunIf` / `TapIf` / constructor の計6ケースが失敗。条件後に constructor と body が実行され、constructor 中の取消後にも body が実行されることを確認。
- 修正後: `CompositeStepTests`、`TimeoutCancellationContractTests`、`FileBasedWorkflowExecutorTests`、`FileBasedCsWorkflowBuilderLifecycleTests` を合わせて55件成功。
- `CodingStandardsContractTests.CSharpDeclarationsFollowT31CodingStandards`: 1件成功。
- `dotnet build tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --no-restore --framework net8.0`: 成功、警告0、エラー0。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes --no-restore`: 成功。
- `git diff --check`: 成功。Markdown lint は `node_modules` がないため未実施。依存追加はしていない。
- SDK 10.0.404 / runtime 10.0.15 を使用。実 .NET 8 runtime は未導入のため、net8.0 の試験実行は未確認。全件ローカル試験と新 HEAD の自動 CI も未確認。
