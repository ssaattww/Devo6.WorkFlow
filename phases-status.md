# phase 状態

| phase | 状態 | 範囲 | 完了条件 | 根拠 |
| --- | --- | --- | --- | --- |
| P0 | 完了 | 作業管理の初期化。 | 最低限の制約文書と追跡ファイルが存在する。 | `AGENTS.md`、`tasks-status.md`、`phases-status.md`、`reports/.gitkeep` |
| P1 | 完了 | Markdown lint の準備。 | 文書検査の実行入口と repo 固有設定が存在する。 | `package.json`、`tools/lint/`、`.textlintrc.json`、`cspell.config.jsonc`、`npm run lint:md` |
| P2 | 未着手 | workflow engine 仕様からの実装計画。 | 初期実装の task 分解が記録される。 |  |
| P3 | 未着手 | 初期実装。 | P2 で分解した task が実装され、検証される。 |  |
