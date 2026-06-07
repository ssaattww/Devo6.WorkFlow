# Sub-agent実行レポート

## タスク

T28 `#load "nuget: ..."` のレビュー指摘修正。

## sub-agentを使う理由

レビューで見つかった契約漏れを、実装担当に独立して修正させるため。

## 対象範囲

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `reports/t28-nuget-load-review-fix-20260607202000.md`

## 対象外

- 進捗同期
- PR 本文更新
- commit
- package cache 探索の独自実装
- `project.assets.json` 解析の独自実装

## 実行コマンド

- `dotnet test Devo6.WorkFlow.sln --filter NuGetLockContractTests`
- `dotnet test Devo6.WorkFlow.sln`
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`
- `textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t28-nuget-load-review-fix-20260607202000.md`
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t28-nuget-load-review-fix-20260607202000.md`

## 対象ファイル

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `reports/t28-nuget-load-review-fix-20260607202000.md`

## 指摘事項

- provider が返した `CsxResolvedNuGetScript.SourceCode` 内の nested `#load "nuget: ..."` が `ValidateNuGetReference` を通らず、許可一覧、固定 version、2 要素文法、lock directReferences の検査対象から漏れる。
- provider が返す script source を見るまで nested load は分からないため、nested load 由来の directReferences 完全一致は provider 後の検査になる。

## 結果

- `CsxLoadContext` に NuGet script load 直接参照を分けて保持し、entry/local script で既知の directReferences は provider 前に lock と照合するようにした。
- provider 後に reachable な resolved NuGet script source を走査し、nested NuGet script load を `ValidateNuGetReference` に通してから directReferences に追加するようにした。
- provider 後に combined directReferences を lock directReferences と完全一致で照合し、resolvedDependencies の direct flag も combined directReferences に合わせて検査するようにした。
- nested load の未許可、浮動 version、package path 付き独自文法、lock direct mismatch、lock 済み成功ケースを `NuGetLockContractTests` に追加した。
- `dotnet test Devo6.WorkFlow.sln --filter NuGetLockContractTests`: 成功。
- `dotnet test Devo6.WorkFlow.sln`: 成功。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`: 成功。
- `npm run lint:md`: 成功。
- `npm run lint:md:terms`: 成功。
- `git diff --check`: 成功。
- `textlint ... reports/t28-nuget-load-review-fix-20260607202000.md`: `textlint` command が PATH に無く失敗。
- `npx textlint ... reports/t28-nuget-load-review-fix-20260607202000.md`: 成功。

## リスク

- provider 結果を見ないと nested NuGet script load は分からないため、nested load 由来の lock direct mismatch は provider 前には検出できない。entry/local script で既知の directReferences については provider 前の lock 検査を維持している。
- package cache 探索、contentFiles 選択、`project.assets.json` 解析、runtime assembly 解決の独自実装は追加していない。
