# Sub-agent実行レポート

## タスク

T27 NuGet ロックファイルの設計更新、検査、実装をレビューする。

## sub-agentを使う理由

ユーザー指示と `review-enforcer` により、レビューは sub-agent に委譲する。

## 対象範囲

- `doc/workflow_engine_spec.md`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- T27 で修正された既存テスト
- `reports/t27-nuget-lock-*.md`
- T27 完了条件との整合

## 対象外

- T28 の `#load "nuget: ..."` 実装
- T29 以降の作業
- T27 で触っていない既存テスト名と既存コメント不足の一括修正

## 実行コマンド

- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/review-enforcer/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/feedback-coding-standards-enforcer/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,240p' reports/t27-nuget-lock-final-review-20260607154000.md`
- `git status --short`
- `git diff --stat`
- `git diff -- src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `git diff -- src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
- `git diff -- tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
- `git diff -- tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
- `sed -n '1,520p' tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `git diff -- doc/workflow_engine_spec.md`
- `sed -n '1,320p' reports/t27-nuget-lock-*.md`
- `sed -n '1,240p' tasks-status.md`
- `sed -n '1,260p' phases-status.md`
- `rg -n "Dotnet\\.Script|YamlDotNet|PackageReference|PackageSources|TargetFramework|RuntimeIdentifier|NuGetLock|NuGetDependencyGraph|CreateCompilationContext|project\\.assets|dotnet restore|csproj|RuntimeDependencies|PackageSources" -S src tests doc reports/t27-nuget-lock-*.md`
- `dotnet test Devo6.WorkFlow.sln --filter NuGetLockContractTests`
  - 成功。8 件成功。
- `dotnet test Devo6.WorkFlow.sln`
  - 成功。126 件成功。
- `npm run lint:md`
  - 成功。対象 5 file、cspell issue 0、whitelist 検査成功。
- `npm run lint:md:terms`
  - 成功。`SudachiPy term variants: none`。
- `git diff --check`
  - 成功。

## 対象ファイル

- 変更差分:
  - `doc/workflow_engine_spec.md`
  - `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
  - `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
  - `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
  - `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
  - `reports/t27-nuget-lock-dotnet-script-investigation-20260607134000.md`
  - `reports/t27-nuget-lock-design-update-20260607141000.md`
  - `reports/t27-nuget-lock-failing-tests-20260607143000.md`
  - `reports/t27-nuget-lock-implementation-20260607150000.md`
  - `reports/t27-nuget-lock-final-review-20260607154000.md`
- 進捗確認:
  - `tasks-status.md` T27
  - `phases-status.md` P10

## 指摘事項

### blocking normal-path problem

1. `doc/workflow_engine_spec.md:1208` と `doc/workflow_engine_spec.md:1862` は、ロックファイルに `targetFramework`、実行時識別子、`packageSources`、`Dotnet.Script.Core` version を記録し、エンジン側が比較すると定めている。しかし実装 DTO は `version`、`entry`、`directReferences`、`resolvedDependencies` だけを持ち、deserializer も unknown property を無視するため、これらの値が欠落または不一致でも検出されない。T27 の設計更新で reproducibility contract として明文化した項目なので、現状の最小 schema を T27 完了として扱うと設計と実装が矛盾する。少なくとも設計を最小 schema に戻すか、実装と検査でこれらの lock metadata mismatch を `SCRIPT_NUGET_LOCK_MISMATCH` にする必要がある。
   - `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:27`
   - `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:403`
   - `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:961`

### user-confirmation-required capability gap

- なし。

### non-blocking concern

1. 既存の許可済み NuGet 実行テストは、実 package 型を使う確認から fake provider と `"locked"` 文字列だけの確認へ変わっている。通常の `dotnet test` を外部通信に依存させない意図は妥当だが、provider が返した runtime assembly path で script が package 型を compile / execute できる経路の保証は落ちている。blocker ではないが、local assembly path fixture など外部通信不要の success-path 検査を追加すると T27 の回帰耐性が上がる。
   - `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs:470`
   - `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:91`
2. `tasks-status.md` の T27 と `phases-status.md` の P10 は未着手のままである。レビュー担当は編集禁止のため、親側同期事項として記録する。
   - `tasks-status.md`
   - `phases-status.md`

## 結果

- T27 final review を実施した。
- `Dotnet.Script.Core` / `Dotnet.Script.DependencyModel` への委譲については、production provider が `ScriptCompiler.CreateCompilationContext` の `RuntimeDependencies` と runtime assembly path を使っており、repo 側で `dotnet restore`、一時 `.csproj`、`project.assets.json`、runtime assembly 解決を再実装している箇所は確認しなかった。
- lock file 欠落と direct reference mismatch は provider 呼び出し前に失敗する実装になっている。
- resolved dependency mismatch は provider 結果と lock の比較で失敗する実装になっている。
- 許可外 NuGet、浮動 version、`#load "nuget: ..."` は lock check より前に `SCRIPT_REFERENCE_NOT_ALLOWED` で失敗する検査がある。
- 新規または変更された型、constructor、method、property には日本語 XML コメントが付いていることを確認した。
- T27 で追加された新規テスト関数名は英語であることを確認した。
- 検証コマンドはすべて成功した。
- ただし、設計で明記された lock metadata の記録と比較が実装されていないため、T27 は blocker ありとして扱う。
- このレポートの未記入箇所を更新した。レポート以外のファイルは編集していない。

## リスク

- blocker を解消せずに T27 完了へ進めると、設計上は固定されるはずの target framework、runtime identifier、package source、Dotnet.Script.Core version の drift を検出できない。
- 現在の passing tests は direct references と resolved dependencies の最小 schema に閉じているため、設計どおりの lock metadata 比較を追加する場合は契約テストの追加が必要である。
- 実 package 型を使う成功経路の通常検査が薄くなっているため、runtime assembly path 連携の不具合は production NuGet path まで発見が遅れる可能性がある。
