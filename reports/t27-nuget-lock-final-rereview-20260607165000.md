# T27 NuGet lock final rereview レポート

## タスク

T27 NuGet lock 修正後の再レビューを行い、前回 final review の blocking 指摘が解消したか確認する。

## レビュー担当

再レビュー担当 sub-agent。

親エージェントは管理に専念し、本担当はレビュー、検証、報告のみを実施した。

## 対象範囲

- T27 全体の未コミット差分
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
- `doc/workflow_engine_spec.md`
- `reports/t27-nuget-lock-*.md`
- T27 完了条件との整合

## 対象外

- コード修正
- `tasks-status.md` と `phases-status.md` の親エージェント同期
- T28 の `#load "nuget: ..."` 実装
- T31 で予定されている既存テスト名と既存コメントの全面点検

## 実行コマンド

- `git status --short --branch`
- `git diff --name-status`
- `rg -n "T27|NuGet lock|lock" tasks-status.md phases-status.md doc/workflow_engine_spec.md reports -g '*.md'`
- `git diff -- src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `git diff -- tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
- `sed -n '1,760p' tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `git diff -- doc/workflow_engine_spec.md`
- `git diff -- src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
- `sed -n '1,240p' reports/t27-nuget-lock-blocking-fix-20260607161000.md`
- `sed -n '1,180p' reports/t27-nuget-lock-final-review-20260607154000.md`
- `rg -n "Dotnet\\.Script|DependencyModel|dotnet restore|project\\.assets|csproj|RuntimeDependencies|CreateCompilationContext|CachedRestorer|PackageSource|TargetFramework|RuntimeIdentifier|NuGetLock|ScriptNuget" -S src tests doc/workflow_engine_spec.md reports/t27-nuget-lock-*.md`
- `dotnet test Devo6.WorkFlow.sln --filter NuGetLockContractTests`
- `dotnet test Devo6.WorkFlow.sln`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`

## Blocking

なし。

前回 blocking 指摘だった lock metadata 欠落は解消済み。`CsxEntryLoader` は lock file 読み込み後、直接参照一致、metadata 完備、provider 解決、解決 metadata 一致、解決済み依存関係一致の順で確認している。

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:406`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:407`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:408`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:413`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:428`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:429`

lock YAML は `directReferences`、`resolvedDependencies`、`targetFramework`、`runtimeIdentifier`、`packageSources`、`dotnetScriptCoreVersion` を保持する DTO と検査を持つ。metadata 欠落は provider 呼び出し前に mismatch となり、metadata 差分は provider 後に mismatch となる。

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:504`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:524`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:112`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:133`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:154`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:175`

`packageSources` は順序差を許容し、値差分を mismatch にする実装と検査がある。

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:529`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:588`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:196`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:220`

lock missing と direct mismatch は dependency graph provider 呼び出し前に検出される。resolved dependency mismatch は provider 呼び出し後に検出される。

- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:36`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:53`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:71`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:89`

許可外 NuGet、浮動 version、`#load "nuget: ..."` は lock 検査より前に `SCRIPT_REFERENCE_NOT_ALLOWED` で止まり、provider は呼ばれない。

- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:266`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:285`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:307`

本番 provider は `ScriptCompiler.CreateCompilationContext` と `RuntimeDependencies` を使い、repo 側で `dotnet restore` 起動、一時 `.csproj` 生成、`project.assets.json` 解析、runtime assembly 解決を独自再実装していない。

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:1137`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:1162`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:1166`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:1174`

設計書も、T27 が `Dotnet.Script.Core` と `Dotnet.Script.DependencyModel` に NuGet 復元と依存関係解決を委ね、lock YAML の比較だけを engine 側に持つ方針と一致している。

- `doc/workflow_engine_spec.md:1198`
- `doc/workflow_engine_spec.md:1200`
- `doc/workflow_engine_spec.md:1208`
- `doc/workflow_engine_spec.md:1210`
- `doc/workflow_engine_spec.md:1215`
- `doc/workflow_engine_spec.md:1860`
- `doc/workflow_engine_spec.md:1862`

T27 で追加、変更された関数名は英語であり、追加、変更された型、constructor、method、property には日本語 XML コメントが付いている。既存の日本語テストメソッド名は T31 対象として扱い、今回の blocking にはしない。

## Non-blocking

1. 既存の許可済み NuGet 実行テストは、実 package 型を使う確認から fake provider と `"locked"` 文字列だけの確認へ弱くなっている。通常の `dotnet test` を外部通信に依存させない目的は妥当で、T27 完了を止める blocker ではない。ただし、provider が返す runtime assembly path により package 型を compile / execute できる経路の通常検査は薄いままなので、local assembly path fixture などで外部通信不要の success path を追加すると回帰耐性が上がる。
   - `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs:490`
   - `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs:504`
   - `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs:515`

2. `tasks-status.md` の T27 と `phases-status.md` の P10 は未着手のまま。ユーザー指示どおり未更新自体は blocking にしない。親エージェントが T27 完了記録として、今回の再レビューレポート、blocking fix レポート、検証結果、残る non-blocking concern を同期する必要がある。
   - `tasks-status.md:32`
   - `phases-status.md:16`

## User-confirmation-required

なし。

## 検証結果

- `dotnet test Devo6.WorkFlow.sln --filter NuGetLockContractTests`
  - 成功。14 件成功。
- `dotnet test Devo6.WorkFlow.sln`
  - 成功。132 件成功。
- `npm run lint:md`
  - 成功。Markdown 対象 5 file、cspell issue 0、whitelist 検査成功。
- `npm run lint:md:terms`
  - 成功。`SudachiPy term variants: none`。
- `git diff --check`
  - 成功。

## 結果

T27 は、レビュー対象の未コミット差分に関して完了条件を満たすと判断する。blocking はない。

残る対応は、親エージェントによる `tasks-status.md` と `phases-status.md` の同期、および任意の non-blocking 検査補強である。
