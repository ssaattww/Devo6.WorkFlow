# 課題 #27: Step 呼び出し境界の外部取消

## 変更

`CompositeStep` は Step のログ・scope と設定初期化を済ませた後、登録済み Step を呼び出す直前に workflow 外部の `CancellationToken` を確認する。取消済みなら `OperationCanceledException` を既存の捕捉経路へ渡し、`STEP_CANCELED` を返す。timeout 用の合成 token ではなく外部 token を確認するため、同期 Step の timeout 動作は変えない。

共通 Engine の実行経路と、ファイルベース `.cs` の公開 `Run` 経路に回帰検査を追加した。後者は Provider の `Create()` が完了した印を確認し、Step 本体の副作用印が作られないこと、診断 session が保持されることを検査する。

## 検証

- 修正前の対照: 新規検査2件が失敗。共通 Engine では `sync-body` の副作用が記録され、公開 `Run` では Step 実行後に `Succeeded` となった。
- 修正後: `TimeoutCancellationContractTests`、`FileBasedWorkflowExecutorTests`、`FileBasedCsWorkflowBuilderLifecycleTests` を合わせて38件成功。
- `CodingStandardsContractTests.CSharpDeclarationsFollowT31CodingStandards`: 1件成功。
- `dotnet build tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --no-restore --framework net8.0`: 成功、警告0、エラー0。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes --no-restore`: 成功。
- `git diff --check`: 成功。Markdown lint は `node_modules` がないため未実施。依存追加はしていない。
- SDK 10.0.404 / runtime 10.0.15 を使用。実 .NET 8 runtime は未導入のため、net8.0 の試験実行は未確認。全件ローカル試験と新 HEAD の自動 CI も未確認。
