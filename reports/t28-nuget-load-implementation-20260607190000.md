# Sub-agent実行レポート

## タスク

T28 `#load "nuget: ..."` の実装。

## sub-agentを使う理由

実装作業を独立担当に任せ、親エージェントが設計整合、レビュー、進捗、Git管理に集中するため。

## 対象範囲

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- 必要な最小範囲の関連テスト
- `reports/t28-nuget-load-implementation-20260607190000.md`

## 対象外

- task / phase 進捗同期
- PR 本文更新
- commit
- 独自の package cache 探索
- 独自の `project.assets.json` 解析

## 実行コマンド

- `dotnet test Devo6.WorkFlow.sln --filter NuGetLockContractTests`
  - 結果: 成功。23 件通過。
- `dotnet test Devo6.WorkFlow.sln`
  - 結果: 成功。141 件通過。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`
  - 結果: 成功。format 差分なし。
- `npm run lint:md`
  - 結果: 成功。
- `npm run lint:md:terms`
  - 結果: 成功。SudachiPy term variants はなし。
- `git diff --check`
  - 結果: 成功。
- `./node_modules/.bin/textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t28-nuget-load-implementation-20260607190000.md`
  - 結果: 成功。

## 対象ファイル

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `reports/t28-nuget-load-implementation-20260607190000.md`

## 指摘事項

- `#load "nuget: PackageId, Version"` を NuGet 直接参照として扱い、`AllowedNuGetReferences`、固定 version、lock 欠落、direct reference 不一致の provider 前検査へ通した。
- `#load "nuget: PackageId, Version, path/to/file.csx"` の独自文法は許可せず、lock file と provider の前に拒否する検査へ置き換えた。
- `CsxNuGetDependencyGraph` に解決済み NuGet script 情報を追加し、fake provider で外部通信なしに source 展開を検査できるようにした。
- production provider は `ScriptCompiler.CreateCompilationContext` の `RuntimeDependencies` から dependencies、runtime assembly path、script path を取得し、script source を読むだけにした。
- lock 一致後、最終 compile source から NuGet `#r` と NuGet `#load` の元 directive を除去し、provider が返した script source を展開するようにした。
- NuGet script load の循環は `SCRIPT_LOAD_CYCLE_DETECTED` に正規化し、同じ NuGet script の重複展開は一度だけにした。

## 結果

T28 `#load "nuget: ..."` の実装を追加し、fake provider による通常 `dotnet test` で外部通信なしに成功系、拒否系、lock 欠落、不一致、解決済み dependency 不一致、循環、重複を確認した。

## リスク

- production provider は Dotnet.Script が返した script path を読み込む最小接続に留めている。実 package での `contentFiles` 選択、package cache 探索、`project.assets.json` 解析、runtime assembly 解決は Dotnet.Script 側へ委譲している。
- package 内 script がさらに相対 local `#load` を持つ場合の end-to-end 検査は今回の通常 test には含めていない。必要になった場合は local package source fixture を別検証として追加する。
