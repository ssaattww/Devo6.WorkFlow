# Sub-agent実行レポート

## タスク

T28 `#load "nuget: ..."` の失敗検査作成。

## sub-agentを使う理由

実装前に利用者目線の契約を検査で固定し、親エージェントが管理とレビューに集中するため。

## 対象範囲

- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- 必要な最小範囲の test helper
- `reports/t28-nuget-load-failing-tests-20260607182000.md`

## 対象外

- production code 実装
- 設計書の追加変更
- commit
- PR 本文更新

## 実行コマンド

- `dotnet test Devo6.WorkFlow.sln --filter NuGetLockContractTests`
- `git diff --check`
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes --include tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs --no-restore`
- `./node_modules/.bin/textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t28-nuget-load-failing-tests-20260607182000.md`
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t28-nuget-load-failing-tests-20260607182000.md`
- `npm run lint:md:whitelist:changed`

## 対象ファイル

- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `reports/t28-nuget-load-failing-tests-20260607182000.md`

## 指摘事項

- `#load "nuget: CsvHelper, 33.0.1"` が fake provider の script 解決情報から展開され、読み込まれた `CsvHelperLoadedStep` を実行できる成功系を追加した。
- `Validate` でも同じ NuGet script load の成功系が通ることを追加した。
- 未許可 NuGet script load と浮動 version NuGet script load が lock file 検査および provider 解決より前に `SCRIPT_REFERENCE_NOT_ALLOWED` で拒否されることを追加した。
- lock file 欠落は provider 解決前に `SCRIPT_NUGET_LOCK_MISSING`、直接参照不一致は provider 解決前に `SCRIPT_NUGET_LOCK_MISMATCH` になることを追加した。
- provider 解決後の resolved dependency 不一致が `SCRIPT_NUGET_LOCK_MISMATCH` になることを追加した。
- NuGet script load の循環が `SCRIPT_LOAD_CYCLE_DETECTED` に正規化されることを期待する赤テストを追加した。
- 同じ NuGet script load の重複が一度だけ展開され、Step も重複しないことを marker file と trace で確認する赤テストを追加した。
- fake provider の script 解決情報契約として、未実装の `CsxResolvedNuGetScript` と `CsxNuGetDependencyGraph` の `scripts` 引数をテストから参照した。

## 結果

`dotnet test Devo6.WorkFlow.sln --filter NuGetLockContractTests` は期待どおり失敗した。

失敗理由は、T28 実装で追加されるべき NuGet script 解決情報 contract が production code にまだ存在しないためである。

- `NuGetLockContractTests.cs(751,23): error CS0246: The type or namespace name 'CsxResolvedNuGetScript' could not be found`
- `NuGetLockContractTests.cs(768,20): error CS0246: The type or namespace name 'CsxResolvedNuGetScript' could not be found`

`git diff --check` は成功した。

`dotnet format Devo6.WorkFlow.sln --verify-no-changes --include tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs --no-restore` は成功した。

レポート単体の textlint は成功した。

cspell は `reports/` が ignore 対象のため skip となり、issue は 0 件だった。

`npm run lint:md:whitelist:changed` は成功した。

## リスク

- 現時点では production code が `#load "nuget: ..."` を `SCRIPT_REFERENCE_NOT_ALLOWED` で拒否するため、`CsxResolvedNuGetScript` contract 実装後にも複数の追加テストは赤のまま残る見込みである。
- `CsxResolvedNuGetScript` の具体 property 名と `CsxNuGetDependencyGraph` の `Scripts` contract は T28 実装時に確定が必要である。
- NuGet source resolver または Roslyn 側の循環例外を常に `SCRIPT_LOAD_CYCLE_DETECTED` に正規化できるかは実装時の例外情報に依存する。
