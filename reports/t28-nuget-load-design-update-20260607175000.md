# Sub-agent実行レポート

## タスク

T28 `#load "nuget: ..."` の設計更新。

## sub-agentを使う理由

設計文書の契約更新を、実装前に独立して点検可能な形で行うため。

## 対象範囲

- `doc/workflow_engine_spec.md`
- `reports/t28-nuget-load-dotnet-script-investigation-20260607172000.md`
- `tasks-status.md`
- `phases-status.md`

## 対象外

- C# 実装
- C# 検査実装
- PR 本文更新
- commit

## 実行コマンド

- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`
- `./node_modules/.bin/textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t28-nuget-load-design-update-20260607175000.md`

## 対象ファイル

- `doc/workflow_engine_spec.md`
- `reports/t28-nuget-load-design-update-20260607175000.md`

## 指摘事項

- `#load "nuget: ..."` の文法は `dotnet-script` 互換の `#load "nuget: PackageId, Version"` のみに限定した。
- `#load "nuget: Package, Version, path/to/file.csx"` の独自文法は採用しないことを明記した。
- NuGet script パッケージの探索、`contentFiles` 選択、`project.assets.json` 解析、実行時 assembly 解決、最終コンパイル用の NuGet source 解決機構は `Dotnet.Script.Core` と `Dotnet.Script.DependencyModel` に委ねる方針を明記した。
- T28 では `#load "nuget: ..."` も `devo6.nuget.lock.yaml` の `directReferences`、`resolvedDependencies`、`metadata` 比較対象に含めることを明記した。
- provider 契約は `RuntimeDependency.Scripts` 相当、または `NuGetSourceReferenceResolver` に渡せる script 解決情報を返す必要があることを明記した。
- ローカル `#load` と NuGet script 読み込みの循環、重複読み込みの扱いを分けて明記した。
- 未許可 NuGet、浮動 version、lock 欠落、不一致、restore 失敗の error code 優先順位を明記した。
- 通常の `dotnet test` は偽 provider などで外部通信非依存にし、ローカルの NuGet 参照元を使う検証は追加検証として分ける方針を明記した。

## 結果

`doc/workflow_engine_spec.md` の T27/P10 周辺を更新し、T28 実装前に必要な `#load "nuget: ..."` の仕様、lock 比較範囲、provider 契約、エラー優先順位、検査方針を確認できる状態にした。

`reports/` は通常の `npm run lint:md` 対象外であるため、本レポートは単体 textlint で確認した。

## リスク

- `Dotnet.Script.Core` / `Dotnet.Script.DependencyModel` の具体 API 接続方法は実装時に確認が必要である。
- NuGet script 読み込みの循環を Roslyn または source resolver 側の例外から常に `SCRIPT_LOAD_CYCLE_DETECTED` へ正規化できるかは、実装時の例外情報に依存する。
- ローカルの NuGet 参照元を使う追加検証は設計上の候補に留め、通常検査の必須要件にはしていない。
