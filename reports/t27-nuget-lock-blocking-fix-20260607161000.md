# T27 NuGet lock blocking 修正レポート

## タスク

T27 NuGet lock file 実装の blocking 指摘を受け、lock DTO と比較処理に再現性 metadata を追加した。

## 対象範囲

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`

## 実装内容

- lock YAML DTO に `targetFramework`、`runtimeIdentifier`、`packageSources`、`dotnetScriptCoreVersion` を追加した。
- `CsxNuGetResolutionMetadata` を追加し、dependency graph provider の結果として NuGet 解決 metadata を返すようにした。
- lock file の再現性 metadata が欠落している場合は `SCRIPT_NUGET_LOCK_MISMATCH` にした。
- `packageSources` は順序を無視して比較し、値の差は mismatch にした。
- `targetFramework`、`runtimeIdentifier`、`dotnetScriptCoreVersion` の差を mismatch にした。
- lock missing と direct reference mismatch は引き続き dependency graph provider 呼び出し前に検知する。
- 許可外 NuGet、浮動 version、`#load "nuget: ..."` の優先順位は維持した。

## テスト

- metadata 欠落時に restore 前 mismatch になる検査を追加した。
- `targetFramework` 差分、`runtimeIdentifier` 差分、`dotnetScriptCoreVersion` 差分の mismatch 検査を追加した。
- `packageSources` の順序差を許容し、値差分を mismatch にする検査を追加した。
- 既存の fake provider 利用テストの lock fixture に再現性 metadata を追加した。

## 検証

- `dotnet test Devo6.WorkFlow.sln --filter NuGetLockContractTests`
  - 成功。14 件成功。
- `dotnet test Devo6.WorkFlow.sln`
  - 成功。132 件成功。
- `npm run lint:md`
  - 成功。
- `npm run lint:md:terms`
  - 成功。SudachiPy term variants は none。
- `git diff --check`
  - 成功。
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t27-nuget-lock-blocking-fix-20260607161000.md`
  - 成功。
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t27-nuget-lock-blocking-fix-20260607161000.md`
  - repo 設定により `reports/` が skip され、issue は 0 件。

## 外部 network 依存の補強検討

通常テストでは fake provider が依存関係 graph と解決 metadata を返すため、外部 NuGet source への通信は不要なまま補強できた。

現実の NuGet 型を使う E2E は、本番 provider が Dotnet.Script の復元処理に委譲するため、package cache や source 設定に依存する。通常テストで安定実行するには外部 network か事前 cache の前提が必要になるため、今回は追加していない。

## 残リスク

- 本番 provider の `packageSources` は NuGet configuration の有効 source を記録するため、利用者環境の設定差で lock mismatch になる。
- `#load "nuget: ..."` は T28 対象のため、今回も未対応。
