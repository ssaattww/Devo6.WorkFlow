# Sub-agent実行レポート

## タスク

- 目的: T15 の `#load` / `#r` / NuGet 参照検証実装を code review し、通常利用経路を壊す問題を検出する。
- タスク種別: review

## sub-agentを使う理由

- 理由: review-enforcer により task 完了前の dedicated review は sub-agent 作業として実施する必要があるため。

## 対象範囲

- 対象: T15 で変更された `src/Devo6.WorkFlow.Engine/`、`tests/Devo6.WorkFlow.Tests/`、関連 report。

## 対象外

- 対象外: T16 の validate 全体、CLI、Config YAML 読み込み、非同期 API、NuGet lock file、`#load "nuget: ..."`。

## 実行コマンド

- 実行コマンド:
  - `git status --short`（確認。tracked: `phases-status.md`、`src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`、`tasks-status.md`、`tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`; untracked: T15 implementation / review reports）
  - `git diff -- src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs tasks-status.md phases-status.md`（確認）
  - `sed` / `rg` による required skill、設計書 15.3 / 16.2 / 16.3、T15 report、対象 source / test の確認
  - `git diff --check`（成功。出力なし）
  - `DOTNET_CLI_HOME=/tmp/devo6-dotnet-cli-home NUGET_PACKAGES=/tmp/devo6-workflow-nuget MSBUILDDISABLENODEREUSE=1 dotnet test Devo6.WorkFlow.sln --disable-build-servers`（成功。Failed: 0, Passed: 39, Skipped: 0, Total: 39）
  - `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md`（成功。CSpell: Issues found: 0）
  - `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md:terms`（成功。SudachiPy term variants: none）
  - `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t15-csx-reference-validation-review-20260606193000.md`（成功。出力なし）
  - 再レビュー: `git status --short`（確認。tracked: `phases-status.md`、`src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`、`tasks-status.md`、`tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`; untracked: T15 implementation / review / review-fix reports）
  - 再レビュー: `git diff -- src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs tasks-status.md phases-status.md`（確認）
  - 再レビュー: `sed` / `rg` による review report、review-fix report、対象 source / test、設計書 15.3 / 16.2 / 16.3 の確認
  - 再レビュー: `DOTNET_CLI_HOME=/tmp/devo6-dotnet-cli-home NUGET_PACKAGES=/tmp/devo6-workflow-nuget MSBUILDDISABLENODEREUSE=1 dotnet test Devo6.WorkFlow.sln --disable-build-servers`（成功。Failed: 0, Passed: 42, Skipped: 0, Total: 42）
  - 再レビュー: `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md`（成功。CSpell: Issues found: 0）
  - 再レビュー: `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md:terms`（成功。SudachiPy term variants: none）
  - 再レビュー: `git diff --check`（成功。出力なし）
  - 再レビュー追記後: `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md`（成功。CSpell: Issues found: 0）
  - 再レビュー追記後: `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md:terms`（成功。SudachiPy term variants: none）
  - 再レビュー追記後: `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t15-csx-reference-validation-review-20260606193000.md`（成功。出力なし）
  - 再レビュー追記後: `git diff --check`（成功。出力なし）

## 対象ファイル

- 変更または確認したファイル:
  - `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
  - `tasks-status.md`
  - `phases-status.md`
  - `doc/workflow_engine_spec.md`
  - `reports/t15-csx-reference-validation-implementation-20260606193000.md`
  - `reports/t15-csx-reference-validation-review-20260606193000.md`
  - 再レビュー: `reports/t15-csx-reference-validation-review-fix-20260606194500.md`

## 指摘事項

- 指摘要約または「指摘なし」:
  - ブロッキング: `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:181` の NuGet 分岐は `ValidateNuGetReference(...)` 後に return し、`#r "nuget: ..."` を source から除去したまま `ScriptOptions` に package restore / reference 情報を渡していない。`CreateScriptOptions` も `source.ReferenceAssemblies` と `source.ReferencePaths` のみを追加しており、許可済み NuGet package を使う script は compile できない。設計書 16.3 は `#r "nuget: CsvHelper, 33.0.1"` を「明示許可された場合に限り対応」としており、T15 の通常利用経路と完了条件を満たしていない。`tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs` も許可外 NuGet と浮動 version の失敗系だけで、許可済み NuGet を使う成功系を検査していない。
  - ブロッキング: `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:118` と `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:337` の path 正規化は `Path.GetFullPath` による字句正規化のみで、設計書 16.2 が求めるシンボリックリンク解決をしていない。workflow root 配下の symlink が root 外を指す場合でも `IsInsideRoot(...)` は配下として許可し、`File.ReadLines(...)` が root 外ファイルを読み込めるため、root 制限の契約を満たしていない。`#r` file reference の許可 directory 判定も同じ問題を持つ。
  - 再レビュー: 指摘なし。前回 blocking 2 件はいずれも修正済みと判断した。

## 結果

- 結果:
  - T15 の `#load` 記述元 directory 基準、`#load "nuget: ..."` fail closed、循環検出、同一正規 path の重複除去、許可外 `#r`、許可 assembly 名参照、許可外 NuGet、浮動 NuGet version、既存 T13 / T14 normal path は既存検査では維持されている。
  - 新規 public API と新規 `[Fact]` の XML summary は揃っており、source documentation policy 上の blocking は見つからなかった。
  - ただし、許可済み NuGet の実行経路と symlink 解決込みの root 制限に blocking が残っているため、T15 はこのまま完了扱いにできない。
  - 再レビュー: 許可済み NuGet は `#r "nuget: NodaTime, 3.1.11"` を source に保持し、`CreateCompilationContext<object, object>(...)` 経由で restore / reference 解決され、script 内で `NodaTime.LocalDate` を使う success path が検査されている。
  - 再レビュー: root 判定は `ResolvePathFinalTarget(...)` で file / directory symlink の最終実体をたどってから判定し、root 内 symlink が root 外 file / directory を指す `#load` を拒否する検査が追加されている。
  - 再レビュー: 新規 public API と新規 `[Fact]` の XML summary 不足は見つからなかった。T15 全体の normal path に新しい blocking は見つからなかった。

## リスク

- 未解決のリスクまたは後続対応:
  - 許可済み NuGet 参照を Dotnet.Script.Core の restore / reference 解決へ渡す成功経路、または T15 で NuGet restore を対象外にする設計同期が必要。
  - `#load` と `#r` file reference の正規 path 判定で symlink を実体解決してから root / allowed directory 判定する必要がある。
  - 再レビュー: 前回の NuGet success path と symlink root 判定リスクは解消済み。NuGet lock file、`#load "nuget: ..."`、T16 validate 全体、CLI、Config YAML 読み込み、非同期 API は T15 対象外として残る。
