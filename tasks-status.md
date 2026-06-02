# task 状態

| task | 状態 | 範囲 | 完了条件 | 根拠 |
| --- | --- | --- | --- | --- |
| T0 | 完了 | 最低限の作業管理ファイルを用意する。 | `AGENTS.md`、`tasks-status.md`、`phases-status.md`、`reports/` が存在する。 | このファイル、`AGENTS.md`、`phases-status.md`、`reports/.gitkeep` |
| T1 | 完了 | Markdown lint の実行準備を行う。 | `npm run lint:md` の入口と repo 固有設定が存在する。 | `package.json`、`tools/lint/`、`.textlintrc.json`、`cspell.config.jsonc`、`npm run lint:md` |
| T2 | 完了 | `doc/workflow_engine_spec.md` の Markdown lint 用語候補を収集する。 | 設計書から未許可語を抽出し、確認用の報告に記録する。 | `reports/task-markdown-lint-terms-20260602152805.md` |
| T3 | 完了 | `doc/workflow_engine_spec.md` の Markdown lint 用語候補を分類する。 | 設計機能名として許可する英語、本文を日本語化する語、確認が必要な語を分けて記録する。 | `reports/task-markdown-lint-term-classification-20260602154500.md` |
| T4 | 完了 | 分類済みの設計機能名を whitelist に反映する。 | 許可対象の英語設計語と片仮名語が `tools/lint/markdown-whitelist.yaml` に登録され、whitelist 自己検査が通る。 | `tools/lint/markdown-whitelist.yaml`、`npm run lint:md:whitelist -- --stdin tools/lint/markdown-whitelist.yaml` |
| T5 | 未着手 | `doc/workflow_engine_spec.md` から実装 task を分解する。 | 具体的な実装 task と順序がこのファイルに記録される。 |  |
