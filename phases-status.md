# phase 状態

| phase | 状態 | 範囲 | 完了条件 | 根拠 |
| --- | --- | --- | --- | --- |
| P0 | 完了 | 作業管理の初期化。 | 最低限の制約文書と追跡ファイルが存在する。 | `AGENTS.md`、`tasks-status.md`、`phases-status.md`、`reports/.gitkeep` |
| P1 | 完了 | Markdown lint の準備。 | 文書検査の実行入口と repo 固有設定が存在する。 | `package.json`、`tools/lint/`、`.textlintrc.json`、`cspell.config.jsonc`、`npm run lint:md` |
| P1.5 | 完了 | 課題 #1 の設計更新。 | `doc/workflow_engine_spec.md` が csx 完結型の設計へ更新され、利用契約の穴点検、Markdown lint、表記揺れ検査、点検が通る。 | `doc/workflow_engine_spec.md`、`reports/issue-1-full-csx-design-update-20260605130833.md`、`reports/issue-1-full-csx-design-review-20260605130833.md`、`reports/issue-1-spec-gap-analysis-20260606161536.md`、`npm run lint:md`、`npm run lint:md:terms` |
| P2 | 未着手 | workflow engine 仕様からの実装計画。 | 初期実装の task 分解が記録される。 |  |
| P3 | 未着手 | 初期実装。 | P2 で分解した task が実装され、検証される。 |  |
