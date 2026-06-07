# Sub-agent実行レポート

## タスク

T28 `#load "nuget: ..."` の最終レビュー。

## sub-agentを使う理由

実装担当とは独立した視点で、契約、検査、設計整合、コメント標準を点検するため。

## 対象範囲

- T28 の未コミット差分
- `doc/workflow_engine_spec.md`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `reports/t28-nuget-load-*.md`

## 対象外

- コード修正
- 進捗同期
- commit
- PR 本文更新

## 実行コマンド

- `dotnet test Devo6.WorkFlow.sln --filter NuGetLockContractTests`
  - 成功。23 件通過。
- `dotnet test Devo6.WorkFlow.sln`
  - 成功。141 件通過。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`
  - 成功。差分なし。
- `npm run lint:md`
  - 成功。
- `npm run lint:md:terms`
  - 成功。`SudachiPy term variants: none`。
- `git diff --check`
  - 成功。
- `./node_modules/.bin/textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t28-nuget-load-final-review-20260607194000.md`
  - 成功。

## 対象ファイル

- `doc/workflow_engine_spec.md`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `reports/t28-nuget-load-dotnet-script-investigation-20260607172000.md`
- `reports/t28-nuget-load-design-update-20260607175000.md`
- `reports/t28-nuget-load-failing-tests-20260607182000.md`
- `reports/t28-nuget-load-implementation-20260607190000.md`
- T28 の未コミット差分

## 指摘事項

### Blocking

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:954` の NuGet script source 展開では、provider が返した script 内の `#load "nuget: ..."` を `ParseValidatedNuGetReference` で再作成し、そのまま `AppendResolvedNuGetScripts` に渡している。top-level/local `.csx` の `#load "nuget: ..."` は `ValidateNuGetReference` により `AllowedNuGetReferences`、固定 version、2 要素文法を検査するが、provider 後の nested NuGet script load では同じ検査が行われない。このため、package script 内の未許可 package、浮動 version、または `PackageId, Version, path` 形式が lock direct reference 検査と許可一覧検査の対象外になり得る。T28 の「NuGet script load も `AllowedNuGetReferences`、固定 version、lock directReferences 対象に含める」契約を満たせないため Blocking。

### Non-blocking

- production provider は `ScriptCompiler.CreateCompilationContext` と `RuntimeDependencies` を使っており、package cache 探索、`contentFiles` 選択、`project.assets.json` 解析、runtime assembly 解決の独自実装は確認されなかった。
- `#load "nuget: PackageId, Version"` の採用、独自 path 指定文法の top-level 拒否、lock 欠落、direct mismatch、provider 後 mismatch、restore 失敗、循環、重複の通常検査は追加済みで、指定検証は成功している。
- package 内 script が相対 local `#load` を持つ実 package end-to-end 検査は未追加であり、implementation report のリスクにも記録済み。現時点では通常 `dotnet test` の必須条件ではないため Non-blocking とする。

### User-confirmation-required

- なし。

## 結果

T28 は通常検証コマンド上は成功しているが、NuGet script source 内の nested NuGet `#load` が許可一覧、固定 version、direct reference lock 検査を通らない Blocking があるため、完了扱いにはできない。

今回のレビューは、ユーザー指示により sub-agent / nested Codex を使わず、このセッション内で実施した。

## リスク

- Blocking 修正後は、nested NuGet script load の未許可、浮動 version、path 付き文法、lock 欠落または direct mismatch の優先順位を追加検査で固定する必要がある。
- task/progress 同期は対象外のため未実施。同期時には本 Blocking と検証結果を反映する必要がある。
