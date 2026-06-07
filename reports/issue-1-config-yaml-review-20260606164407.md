# 課題 #1 Config YAML 化レビュー

## タスク

- Config ファイルの標準形式を JSON 例から YAML 標準へ変更する。

## レビュー範囲

- `doc/workflow_engine_spec.md`

## レビュー担当

- sub-agent: review-enforcer reviewer

## レビュー観点

- ワークフロー定義 YAML を復活させていないこと。
- Config YAML が実行時設定入力として明記されていること。
- `appsettings.json` などの JSON 例が残っていないこと。
- 初期版では Config YAML を自動型変換しない方針と矛盾しないこと。
- Markdown lint と表記揺れ検査が通っていること。

## Markdown lint 結果

- `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md`: 成功。
- `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md:terms`: 成功。`SudachiPy term variants: none`

## 指摘

- 指摘なし。

## 指摘対応

- 対応不要。
- `doc/workflow_engine_spec.md:263` でワークフロー定義 YAML を使わない方針が維持されている。
- `doc/workflow_engine_spec.md:267` と `doc/workflow_engine_spec.md:269` で Config YAML が実行時設定入力であり、ワークフロー定義ではないことを確認した。
- `doc/workflow_engine_spec.md:325` と `doc/workflow_engine_spec.md:1014` で初期版は Config YAML を標準型変換しない方針と一致している。
- `rg -n "JSON|json|appsettings[.]json|appsettings[.]yaml" doc/workflow_engine_spec.md` で JSON 例と `appsettings.json` が残っていないことを確認した。
- `git diff --check` は成功。

## 結論

- ブロッキング指摘なし。レビュー観点を満たしている。
