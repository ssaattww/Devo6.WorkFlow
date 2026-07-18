# Sub-agent 実行レポート

## タスク

- 目的: 対象4文書の Markdown 許可語検査違反を解消し、文章品質検査の結果を記録する。
- タスク種別: 文書修正、検証。

## sub-agent を使う理由

- 理由: 利用者指定の `Terra`、`medium` で、実装文書と並行して既存の進捗文書と仕様文書を整理するため。
- 実行設定: `terra`、`medium`。

## 対象範囲

- 対象: `tasks-status.md`、`phases-status.md`、`doc/workflow_engine_spec.md`、`README.md`。
- 検査記録: `reports/issue-21-pr-25-markdown-cleanup-20260718205423.md`。

## 対象外

- 対象外: コード、`doc/issue-21-hierarchical-logging-design.md`、サンプル文書、進捗状態の完了更新、履歴登録、取り込み依頼。
- T78 と P32 は「対応中」のまま維持した。

## 実行コマンド

検査には Codex に同梱された Node.js v24.14.0 を使用し、`CODEX_SKILLS_DIR` にはリポジトリの `.codex/skill` を指定した。

```powershell
node tools/lint/run-skill-script.js review-enforcer/scripts/check-markdown-whitelist.js --files tasks-status.md phases-status.md doc/workflow_engine_spec.md README.md
```

```powershell
node node_modules/textlint/bin/textlint.js --config .textlintrc.json --rulesdir C:\Users\taiga\DotnetWs\Devo6.WorkFlow\.codex\skill\review-enforcer\scripts\textlint-rules tasks-status.md phases-status.md doc/workflow_engine_spec.md README.md
```

```powershell
node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js tasks-status.md phases-status.md doc/workflow_engine_spec.md README.md
```

```powershell
git diff --check -- tasks-status.md phases-status.md doc/workflow_engine_spec.md README.md
```

## 対象ファイル

- `tasks-status.md`: T64-T67 と T73-T78 の英語の普通名詞を自然な日本語へ直し、型、値、成果物識別子だけをインラインコードとして残した。
- `phases-status.md`: P30-P32 の用語を進捗文書に合う日本語へ統一した。
- `doc/workflow_engine_spec.md`: コレクション全体置換の契約、対象外、失敗条件、コマンド例を意味を変えずに日本語化した。
- `README.md`: コレクション全体置換とコマンド実行環境の説明を日本語化した。

## 指摘事項

初回と修正後の許可語検査件数は次のとおり。

| ファイル | 修正前 | 修正後 |
| --- | ---: | ---: |
| `tasks-status.md` | 100 | 0 |
| `phases-status.md` | 32 | 0 |
| `doc/workflow_engine_spec.md` | 42 | 0 |
| `README.md` | 6 | 0 |
| 合計 | 180 | 0 |

- 普通の英文を検査回避のためにインラインコードへ移していない。
- `PowerShell`、`Bash`、`CompositeStep`、`Text`、`JSON`、モデル名、コマンド、型、キーなど、固有の識別子だけをインラインコードとして残した。
- 許可語一覧、表記修正规則、検査対象除外の変更は行っていない。

## 結果

- 許可語検査: 対象4ファイルすべて0件、終了コード0。集約結果は合格。
- textlint: 対象4ファイルで指摘0件、終了コード0。集約結果は合格。
- cspell: ラッパーは終了コード1となり、診断を出力しなかった。Windows で `.cmd` を子プロセスとして起動するラッパー経路の問題として、許可語検査および textlint の合格とは分離して `unsupported` と分類する。
- 差分検査: `git diff --check` は終了コード0。
- T78 と P32 の状態は「対応中」のまま。受入条件と参照先は維持した。

## リスク

- cspell は合格ではない。ラッパーの Windows 対応後に同じ4ファイルを再検査する必要がある。
- リポジトリ全体の Markdown 検査には、別担当が編集中の文書も含まれる。本報告は指定された4ファイルだけを対象とし、全体検査の結果を保証しない。
- 対象外ファイルに対する変更は行っていない。
