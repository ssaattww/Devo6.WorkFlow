# Sub-agent実行レポート

## タスク

T27 NuGet ロックファイルの採用設計を `doc/workflow_engine_spec.md` へ反映する。

## sub-agentを使う理由

ユーザー指示により、設計書編集は sub-agent に委譲する。

## 対象範囲

- `doc/workflow_engine_spec.md`
- T27 調査レポート
- Markdown lint と用語 lint の確認

## 対象外

- C# 実装と検査コードの変更
- T28 の `#load "nuget: ..."` 実装
- T29 以降の作業

## 実行コマンド

- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-executor/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `sed -n '1,240p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,240p' reports/t27-nuget-lock-design-update-20260607141000.md`
- `sed -n '1,260p' reports/t27-nuget-lock-dotnet-script-investigation-20260607134000.md`
- `rg -n "T27|NuGet|lock|ロック|#r|#load|Dotnet\\.Script|Dotnet Script|CachedRestorer|restore|SCRIPT_NUGET" tasks-status.md phases-status.md doc/workflow_engine_spec.md`
- `nl -ba doc/workflow_engine_spec.md | sed -n '560,690p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1138,1280p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1380,1435p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1570,1668p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1640,1798p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1790,1840p'`
- `sed -n '1,220p' package.json`
- `sed -n '1,220p' tools/lint/README.md`
- `sed -n '1,220p' tools/lint/markdown-targets.json`
- `npm run lint:md`
- `npm run lint:md:terms`
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" doc/workflow_engine_spec.md`
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" doc/workflow_engine_spec.md reports/t27-nuget-lock-design-update-20260607141000.md`
- `git diff --check`

## 対象ファイル

- `doc/workflow_engine_spec.md`
  - 11.3、16.3、16.5、17.2、18.2、19.2、19.3、21.6 を更新。
- `reports/t27-nuget-lock-design-update-20260607141000.md`
  - 本レポートの未記入欄を更新。

## 指摘事項

- 追加の指摘事項はなし。
- `Dotnet.Script.Core` と `Dotnet.Script.DependencyModel` に NuGet 復元と依存関係解決を委ね、リポジトリ側ではロックファイルの読み書き、欠落、不一致、許可済み直接参照、解決済み依存関係の比較だけを持つ方針に整理した。
- `Dotnet.Script.DependencyModel.Context.CachedRestorer` は性能キャッシュであり、利用者向け NuGet ロックファイルではないことを明記した。

## 結果

- `doc/workflow_engine_spec.md` に T27 の採用設計を反映した。
- T27 は `#r "nuget: package, version"` の再現性を対象とし、T28 の `#load "nuget: ..."` は対象外のままと明記した。
- ロックファイル名は Entry `.csx` の workflow root に置く `devo6.nuget.lock.yaml` とした。
- ロックファイルには直接参照、解決済み依存関係、`targetFramework`、実行時識別子、パッケージ参照元、`Dotnet.Script.Core` version を記録し、絶対実行時 assembly path は記録しない方針にした。
- lock 欠落と直接参照不一致は復元前に拒否し、解決済み依存関係の不一致は `Dotnet.Script.Core` の解決結果と比較する方針にした。
- error code として `SCRIPT_NUGET_LOCK_MISSING` と `SCRIPT_NUGET_LOCK_MISMATCH` を追加し、NuGet 復元失敗は `SCRIPT_NUGET_RESTORE_FAILED` を使う方針にした。
- 通常の `dotnet test` が外部通信に依存しないよう、依存関係 provider を注入し、検査 provider は固定データを返す設計にした。
- `npm run lint:md` は、本文の一般英語と未許可語を修正した後に成功した。
- `npm run lint:md:terms` は成功した。
- focused textlint は `doc/workflow_engine_spec.md` と本レポートで成功した。
- `git diff --check` は成功した。
- `reports/` は full lint の対象外のため、本レポートは focused textlint で確認する。

## リスク

- `devo6.nuget.lock.yaml` の詳細 YAML schema は実装時に詰める必要がある。
- `Dotnet.Script.Core` の public API で取得できる依存関係情報の粒度により、ロックファイルへ記録する解決済み依存関係の項目名は調整が必要になる可能性がある。
- T27 では `#load "nuget: ..."` を解禁しないため、T28 実装時に lock 対象を拡張する必要がある。
