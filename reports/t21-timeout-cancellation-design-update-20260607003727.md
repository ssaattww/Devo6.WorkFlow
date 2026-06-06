# Sub-agent実行レポート

## タスク

- 目的: T21 の採用方針に従い、timeout と協調キャンセルの設計書を更新する。
- タスク種別: design update

## sub-agentを使う理由

- 理由: 既存レポート形式を再利用するため本見出しを維持する。ただし今回の指示で codex exec、nested Codex、その他 agent 起動は禁止されたため、sub-agent は起動せず parent が直接更新した。

## 対象範囲

- 対象: `doc/workflow_engine_spec.md` に per-step timeout、外部キャンセル、`STEP_TIMEOUT`、後続 Step 停止、同期 Step を強制中断しない方針を反映する。

## 対象外

- 対象外: コード実装、検査実装、tracking 更新、retry、Config、NuGet、Step 名前空間化、commit。

## 実行コマンド

- 実行コマンド:
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-executor/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/report-output-manager/SKILL.md`
- `sed -n '1,260p' reports/t21-timeout-cancellation-design-update-20260607003727.md`
- `sed -n '1,260p' reports/t21-timeout-cancellation-design-impact-20260607001742.md`
- `find tools/lint -maxdepth 2 -type f | sort`
- `sed -n '1,200p' tools/lint/README.md`
- `sed -n '1,200p' package.json`
- `rg -n "^(##+ )?(11\\.4|12\\.1|18\\.1|18\\.2|18\\.3|19\\.1|19\\.2|19\\.3|21\\.1)|timeout|Timeout|CancellationToken|キャンセル|STEP_TIMEOUT|StepTimeout|STEP_CANCELED|STEP_CANCELED" doc/workflow_engine_spec.md`
- `sed -n '586,660p' doc/workflow_engine_spec.md`
- `sed -n '830,900p' doc/workflow_engine_spec.md`
- `sed -n '1140,1235p' doc/workflow_engine_spec.md`
- `sed -n '1235,1355p' doc/workflow_engine_spec.md`
- `rg -n 'timeout 超過時の詳細な結果化|協調キャンセルの詳細挙動|timeout 超過時の `STEP_TIMEOUT`|T21 で扱う|T21 では以下を決める|### 14\\.[0-9]' doc/workflow_engine_spec.md`
- `git diff -- doc/workflow_engine_spec.md`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`
- `npx textlint reports/t21-timeout-cancellation-design-update-20260607003727.md --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)"`

## 対象ファイル

- 変更または確認したファイル:
- 変更: `doc/workflow_engine_spec.md`
- 変更: `reports/t21-timeout-cancellation-design-update-20260607003727.md`
- 確認: `reports/t21-timeout-cancellation-design-impact-20260607001742.md`
- 確認: `tools/lint/README.md`
- 確認: `package.json`

## 指摘事項

- 指摘要約または「指摘なし」:
- 更新前の `doc/workflow_engine_spec.md` は 11.4、12.1、18.2、19.2、19.3、21.1 で timeout と協調キャンセルを T21 の未確定事項として残していた。
- T21 の採用方針に従い、未確定事項を採用済み方針へ移す必要があった。
- `STEP_CANCELED` を追加しない場合、外部キャンセルが通常の Step 例外や timeout と混ざるため、設計上の区別が不足する。

## 結果

- 結果:
- `WorkflowExecutionOptions` に `TimeSpan? StepTimeout` を追加する方針を 14.5 に追加した。C# 文書注釈は日本語で記載した。
- `StepTimeout` の既定値は `null` とし、timeout を設定しない現行動作を維持する方針にした。
- `ExecuteWorkflowAsync` の外部 `CancellationToken` と timeout 用の `CancellationToken` を Step 実行ごとに合成する方針にした。
- 非同期 Step には合成した `CancellationToken` を渡す方針にした。
- timeout は `WorkflowErrorCodes.StepTimeout` / `STEP_TIMEOUT` の失敗結果に変換する方針にした。
- 外部キャンセルは `WorkflowErrorCodes.StepCanceled` / `STEP_CANCELED` の失敗結果に変換し、timeout と区別する方針にした。
- timeout または外部キャンセル時は対象 Step を `ExecutionTraceStepStatus.Failed` とし、error code を trace に記録する方針にした。
- timeout または外部キャンセル時は対象 Step の `Produce`、`StoreAs`、`Discard` と後続 Step を実行しない方針にした。
- 同期 Step は実行中に強制中断せず、完了後に cancellation が要求済みであれば後続 Step を開始しない方針にした。
- CLI timeout オプション、retry との統合、実行中 Step の強制停止、workflow 全体 timeout、timeout またはキャンセル専用の trace 状態は T21 対象外に残した。
- `npm run lint:md` は初回、英語語句と「トークン」の語彙検査で失敗した。本文を日本語化し、`CancellationToken` / `CancellationTokenSource` で具体化した後に成功した。
- `npm run lint:md:terms` は成功した。
- `git diff --check` は成功した。
- report focused textlint は成功した。

## リスク

- 未解決のリスクまたは後続対応:
- `STEP_CANCELED` は設計上追加したが、src と tests の変更は禁止されているため実装は未対応。
- 同期 Step は `CancellationToken` を受け取らないため、timeout や外部キャンセルで実行中の処理を止める保証はない。
- timeout と外部キャンセルが同時に観測された場合は外部キャンセルを優先する方針にした。実装時は `OperationCanceledException` の分類順序をこの方針に合わせる必要がある。
- CLI timeout オプション、retry、強制停止、workflow 全体 timeout、trace 状態追加は T21 対象外として残る。
