# 課題 #1 仕様補強レビュー

## タスク

- 仕様減量で落ちた利用契約を点検し、`doc/workflow_engine_spec.md` を実装 task 分解前の仕様として補強する。

## レビュー範囲

- `doc/workflow_engine_spec.md`
- `tasks-status.md`
- `phases-status.md`
- `reports/issue-1-spec-gap-analysis-20260606161536.md`
- `reports/issue-1-spec-usage-gap-scan-20260606161536.md`
- `reports/issue-1-spec-old-contract-gap-scan-20260606161536.md`

## レビュー担当

- sub-agent: review-enforcer レビュー担当 sub-agent

## レビュー観点

- YAML ワークフロー定義、独立 Flow、Step 専用 Config 引数を復活させていないこと。
- Entry、Step 名、ファイル構成、`#load`、`#r`、NuGet、AssemblyLoadContext、キャッシュ、信頼境界が実装者に十分な粒度で追記されていること。
- Config は `StepContext` に置く方針を維持し、初期版の `EngineArguments` 方針と将来の標準 Config 読み込みが矛盾しないこと。
- 検証、実行結果、エラーコード、ログ、トレース、初期実装範囲の追記が互いに矛盾しないこと。
- サブエージェントのブロッキング指摘が解消または明示的に次フェーズへ分類されていること。
- Markdown lint と表記揺れ検査の結果が記録と一致していること。

## Markdown lint 結果

- `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md`: 成功。
- `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md:terms`: 成功。`SudachiPy term variants: none`
- `git diff --check`: 成功。

## 指摘

- 指摘1: Blocking. Config 検証の初期版範囲が、`EngineArguments` 方針と矛盾している。`doc/workflow_engine_spec.md:1002` から `doc/workflow_engine_spec.md:1004` は初期版の `validate` 対象に `DataAnnotations` / `IValidatableObject` による Config 検証を含めている。一方、`doc/workflow_engine_spec.md:1018` から `doc/workflow_engine_spec.md:1028` と `doc/workflow_engine_spec.md:1220` は、初期版では `--config` / `--set` を `EngineArguments` として `StepContext` に格納し、型付き Config への変換と検証はユーザー Step が行うとしている。`validate` がユーザー Step を実行しない前提なら型付き Config が存在せず、標準 Config 読み込みも次フェーズ候補であるため、実装者が初期版でどの Config 検証を行うべきか決められない。
- 指摘2: Blocking. `StepContext` の公開 API が章間で不一致。`doc/workflow_engine_spec.md:209` から `doc/workflow_engine_spec.md:227` は `Logger` と `CancellationToken` を `StepContext` API に含めているが、主要公開 API 案の `doc/workflow_engine_spec.md:745` から `doc/workflow_engine_spec.md:761` には含まれていない。さらに `doc/workflow_engine_spec.md:1103` から `doc/workflow_engine_spec.md:1107` はユーザー Step が `StepContext.Logger` を使う前提で、`doc/workflow_engine_spec.md:1150` は初期版に logging 統合を含めている。公開 API のどちらを正とするかが不明なため、ログ契約と初期実装範囲が実装者に十分な粒度で確定していない。
- 再レビュー指摘: なし。前回のブロッキング指摘 2 件は解消済みで、新たなブロッキング矛盾は確認しなかった。

## 指摘対応

- 指摘1は対応済み。`validate` の初期版対象から `DataAnnotations` / `IValidatableObject` による Config 検証を外し、Config ファイル存在確認に限定した。型付き Config の変換と検証は、ユーザー定義の Config 読み込み Step 内で行う契約に統一した。
- 指摘2は対応済み。主要公開 API 案の `StepContext` に `Logger` を追加し、`StepContext` の用途一覧から `CancellationToken` を外した。`CancellationToken` は非同期 API / timeout の未確定事項へ寄せた。
- 旧契約語検索では、YAML ワークフロー定義、独立 Flow、Step 専用 Config 引数の復活は確認しなかった。検出された語は、禁止事項、対象外、過去調査報告、または `steps/` ディレクトリ名としての用例だった。
- Markdown lint、表記揺れ検査、`git diff --check` は再実行し、既存の記録どおり成功した。

## 結論

- 再レビュー完了。前回のブロッキング指摘 2 件は解消済みで、現時点の追加ブロッキング指摘はない。
