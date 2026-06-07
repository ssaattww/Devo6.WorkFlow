# issue #1 フル csx 設計更新レビュー

## タスク

- issue #1 の添付設計を参照し、`doc/workflow_engine_spec.md` を YAML ワークフロー定義中心から csx 完結型へ更新する。

## レビュー範囲

- `doc/workflow_engine_spec.md`
- `tools/lint/markdown-whitelist.yaml`
- `reports/attachments/issue-1-csx-workflow-engine-design.md`
- `reports/issue-1-full-csx-impact-scan-20260605130308.md`
- `reports/issue-1-full-csx-design-update-20260605130833.md`

## レビュー担当

- sub-agent: review-enforcer reviewer sub-agent

## レビュー観点

- YAML ワークフロー定義、独立 Flow、`WorkflowStep<T...>`、Step 専用 Config 引数の旧契約が残っていないこと。
- Config がワークフロー定義 YAML に戻っておらず、`StepContext` に保持する実行時入力として整理されていること。
- `CompositeStep`、`StepInput`、`StepContext`、`IStep<TOut>.Execute(StepInput)` の新契約と矛盾しないこと。
- whitelist 追加語が公開 API 名または補助型名に限定され、一般語の過剰許可になっていないこと。
- Markdown lint の結果と表記揺れ検査結果が作業レポートと一致していること。

## Markdown lint 結果

- `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md`: 成功。
- `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md:terms`: 成功。`SudachiPy term variants: none`

## 指摘

- 指摘なし。
- 旧契約語検索では `WorkflowStep<T...>`、`workflow.yaml`、`flows:`、`steps:`、`next:`、`Config binding`、Step 専用 Config 引数の肯定的な契約は検出しなかった。
- `Flow`、`YAML`、`Config` の残存箇所は、Flow を独立概念にしないこと、ワークフロー定義に YAML を使わないこと、Config を `StepContext` に置くことを示す否定または新契約の文脈だった。
- `doc/workflow_engine_spec.md` と添付設計には表記差分があるが、差分は `StepContext`、`Dotnet.Script.Core`、Config 入力形式、表記揺れ回避の明確化であり、添付設計のフル csx 契約と矛盾しない。
- whitelist 追加語は `StepInput`、`CompositeStep`、`IStep`、`ConfigStore`、`EngineArguments`、`Produce`、`StoreAs`、`Discard`、`FailurePolicy` の公開 API 名または補助型名であり、一般語の過剰許可は検出しなかった。

## 指摘対応

- 対応不要。
- Markdown lint と表記揺れ検査は再実行済みで、作業レポート記載どおり成功した。

## 結論

- ブロッキング指摘なし。
- issue #1 添付設計を参照した csx 完結型設計への更新は、指定レビュー観点に対して整合している。
- Markdown word checker aggregate gate: pass。
