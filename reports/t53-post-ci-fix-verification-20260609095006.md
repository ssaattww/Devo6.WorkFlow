# T53 CI 修正後検証報告

## 目的

PR #11 の `test` job 失敗原因を修正した後、T53 の統合検証条件を再確認する。

## 失敗原因

PR 側では `SampleWorkflowTests` の Sample CLI E2E が `#r "nuget: Devo6.WorkFlow.Engine, 0.1.0"` を実 NuGet 復元しようとして失敗した。`Devo6.WorkFlow.Engine` 0.1.0 は公開前で、CI では `SCRIPT_NUGET_RESTORE_FAILED` になっていた。

## 修正方針

Sample を CLI process から直接実行する E2E を削除し、Sample は固定 provider による実行検査と構成検査に限定した。NuGet 参照の解決契約は既存の `CliRunValidateTests` と `CsxEntryLoaderTests` の NuGet 系検査に委ねる。

## 実行コマンド

- `dotnet restore Devo6.WorkFlow.sln`: 成功。
- `dotnet test Devo6.WorkFlow.sln --configuration Release --no-restore --verbosity minimal`: 成功。226 件成功。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`: 成功。
- `npm run lint:md`: 成功。
- `npm run lint:md:terms`: 成功。`SudachiPy term variants: none`。
- `git diff --check`: 成功。

## 結果

T53 の統合検証条件は CI 修正後の差分でも通過した。

## リスク

Sample の `#r "nuget: Devo6.WorkFlow.Engine, 0.1.0"` は利用者向けの公開版参照例として維持している。PR の通常 test ではこの参照を外部復元しない。
