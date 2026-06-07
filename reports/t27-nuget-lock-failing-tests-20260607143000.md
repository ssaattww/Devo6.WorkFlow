# Sub-agent実行レポート

## タスク

T27 NuGet ロックファイルの検査を検査先行で追加する。

## sub-agentを使う理由

ユーザー指示により、検査追加は sub-agent に委譲する。

## 対象範囲

- `tests/Devo6.WorkFlow.Tests/` 配下の T27 検査
- 既存 NuGet 参照検査の必要最小限の補強
- 追加検査の失敗または既存実装との差分記録

## 対象外

- `src/` 配下の実装変更
- 設計書と進捗ファイルの編集
- T28 の `#load "nuget: ..."` 実装
- 既存テスト名の一括変更

## 実行コマンド

- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/tdd-executor/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/implementation-executor/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,240p' reports/t27-nuget-lock-failing-tests-20260607143000.md`
- `sed -n '1,260p' reports/t27-nuget-lock-dotnet-script-investigation-20260607134000.md`
- `sed -n '1,260p' tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
- `sed -n '1,260p' tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`
- `sed -n '1,280p' src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `sed -n '260,620p' tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
- `sed -n '260,620p' tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`
- `sed -n '280,760p' src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `git status --short && rg -n "class CsxEntryLoaderOptions|AllowedNuGetReferences|WorkflowErrorCodes|ScriptNuGet|NuGet" -S src tests/Devo6.WorkFlow.Tests`
- `sed -n '1,220p' tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj`
- `sed -n '1,120p' src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
- `dotnet test Devo6.WorkFlow.sln --filter NuGetLockContractTests`
- `dotnet test Devo6.WorkFlow.sln`
- `git diff --check`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `sed -n '1,220p' tools/lint/README.md`
- `sed -n '1,220p' package.json`
- `sed -n '1,220p' tools/lint/markdown-targets.json`
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t27-nuget-lock-failing-tests-20260607143000.md`
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t27-nuget-lock-failing-tests-20260607143000.md`

## 対象ファイル

- 変更:
  - `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
  - `reports/t27-nuget-lock-failing-tests-20260607143000.md`
- 参照:
  - `reports/t27-nuget-lock-dotnet-script-investigation-20260607134000.md`
  - `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
  - `tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`
  - `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
  - `tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj`

## 指摘事項

- T27 の予定 contract に対応する `CsxEntryLoaderOptions.NuGetDependencyGraphProvider`、`ICsxNuGetDependencyGraphProvider`、`CsxNuGetDependencyGraph`、`CsxNuGetDependencyGraphRequest` は現行実装に存在しない。
- T27 の予定 error code に対応する `WorkflowErrorCodes.ScriptNugetLockMissing`、`WorkflowErrorCodes.ScriptNugetLockMismatch` は現行実装に存在しない。ただし今回の compile は dependency graph 型の欠落で先に停止したため、この error code 欠落までは到達していない。
- 新規検査は default lock file 名 `devo6.nuget.lock.yaml`、workflow root からの既定 lock path、fake dependency graph provider 注入、lock 欠落、不一致、一致、浮動版優先拒否、許可外 NuGet 優先拒否、`#load "nuget: ..."` unsupported 維持を固定している。
- focused test と full test はどちらも compile error で赤になった。代表 error は `CS0246: The type or namespace name 'CsxNuGetDependencyGraph' could not be found`、`CS0246: The type or namespace name 'ICsxNuGetDependencyGraphProvider' could not be found`、`CS0246: The type or namespace name 'CsxNuGetDependencyGraphRequest' could not be found` である。

## 結果

- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs` を追加し、以下の検査を検査先行で追加した。
  - `ExecuteReturnsLockMissingWhenNuGetReferenceHasNoLockFile`
  - `ValidateReturnsLockMissingWhenNuGetReferenceHasNoLockFile`
  - `ExecuteReturnsLockMismatchWhenDirectNuGetVersionDiffers`
  - `ValidateReturnsLockMismatchWhenResolvedNuGetDependencyDiffers`
  - `ExecuteUsesLockedNuGetDependenciesWhenLockMatches`
  - `ValidateKeepsFloatingNuGetVersionRejectedBeforeLockCheck`
  - `ExecuteDoesNotResolveDependenciesWhenNuGetReferenceIsNotAllowed`
  - `NuGetLoadDirectiveRemainsUnsupportedBeforeT28`
- `dotnet test Devo6.WorkFlow.sln --filter NuGetLockContractTests` は失敗した。現行実装に T27 用 dependency graph provider contract が無いため compile error になった。
- `dotnet test Devo6.WorkFlow.sln` は失敗した。focused test と同じ compile error で test assembly build が止まった。
- `git diff --check` は成功した。
- focused textlint は成功した。
- cspell は repo の ignore 設定により reports 配下を skip した。
- このレポートの未記入箇所を更新した。

## リスク

- 追加検査は想定 API を先に固定しているため、現時点では compile red であり、runtime の lock 欠落や不一致 assertion までは到達していない。
- 予定 API 名や graph model は実装担当が設計更新と合わせて調整する可能性がある。その場合も、検査意図である外部通信非依存、lock 欠落、不一致、優先順位、`#load "nuget: ..."` 境界は維持する必要がある。
- `ExecuteUsesLockedNuGetDependenciesWhenLockMatches` は fake provider だけで通常 `dotnet test` が外部通信に依存しないことを期待している。実装時に `Dotnet.Script.Core` restore 経路へ直接流すと、この検査の目的を満たせない。
