# Sub-agent実行レポート

## タスク

- 目的: T22「retry 実行契約」の設計更新をレビューする。
- タスク種別: review / design update

## sub-agentを使う理由

- 理由: 親がマネージャーとして進行し、レビューを sub-agent に委譲する指示のため。

## 対象範囲

- `doc/workflow_engine_spec.md` の T22 retry 関連差分
- `reports/t22-retry-design-impact-20260607020500.md`
- `reports/t22-retry-design-update-20260607022000.md`

## 対象外

- 設計書、実装、検査、tracking、既存 report の修正
- commit、PR 作成

## 実行コマンド

- `git status --short --branch`
- `git diff -- doc/workflow_engine_spec.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/review-enforcer/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/report-output-manager/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `sed -n '1,260p' reports/t22-retry-design-impact-20260607020500.md`
- `sed -n '1,280p' reports/t22-retry-design-update-20260607022000.md`
- `nl -ba doc/workflow_engine_spec.md | sed -n '570,640p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '880,935p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1238,1320p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1320,1378p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1394,1535p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1530,1555p'`
- `rg -n "日本語表記|表記|リトライ|再試行|retry|Retry|Attempt|MaxAttempts|timeout|cancel|キャンセル|Produce|StoreAs|Discard|backoff|filter|CLI|Config|script load|script|load|Config検証|検証|外部キャンセル|trace|Trace" doc/workflow_engine_spec.md reports/t22-retry-design-impact-20260607020500.md reports/t22-retry-design-update-20260607022000.md`
- `rg -n "実行主体|parent|sub-agent|Codex|gpt|修正|編集|変更" reports/t22-retry-design-impact-20260607020500.md reports/t22-retry-design-update-20260607022000.md`
- `sed -n '1,220p' package.json`
- `ls tools/lint`
- `sed -n '1,220p' tools/lint/README.md`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`
- `npx textlint reports/t22-retry-design-impact-20260607020500.md reports/t22-retry-design-update-20260607022000.md --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)"`
- `npx textlint reports/t22-retry-design-review-20260607023000.md --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)"`

## 対象ファイル

- 確認: `doc/workflow_engine_spec.md`
- 確認: `reports/t22-retry-design-impact-20260607020500.md`
- 確認: `reports/t22-retry-design-update-20260607022000.md`
- 作成: `reports/t22-retry-design-review-20260607023000.md`

## 指摘事項

### Finding 1: script load 失敗が retry 対象外として明示されていない

- severity: Medium
- file:line: `doc/workflow_engine_spec.md:606`
- 根拠: 入力 report は、retry 対象外として「script load」を設計書へ明記する方針を提示している。更新後の 11.4 は `入力取得失敗、Config 検証失敗、.csx コンパイル失敗、参照解決失敗` を retry 対象外としているが、`.csx` ロード失敗または script load 失敗を明示していない。設計書の既存エラー対象には `.csx` のロード失敗が別項目として存在するため、コンパイル失敗や参照解決失敗だけでは同じ契約を表しているとは読み切れない。
- 期待動作: retry 対象外に、`.csx` ロード失敗または script load 失敗が明示され、入力 report の採用方針とレビュー観点の「script load」が設計書上で確認できる。
- 推奨修正: `doc/workflow_engine_spec.md:606` の retry 対象外列挙に `.csx` ロード失敗を追加する。必要なら 17 章や 18 章の検証、ロード、実行結果の契約と同じ語にそろえる。

## 結果

- 指摘件数: 1
- ブロッカー: なし
- `WorkflowExecutionOptions.Retry`、`RetryOptions.MaxAttempts`、既定値、初回を含む最大試行回数は明確に記載されている。
- timeout、外部キャンセル、`Produce`、`StoreAs`、`Discard`、入力取得、Config 検証、CLI / Config 指定、Step 別 retry 方針、retry 待機時間制御、例外型による絞り込みは retry 対象外または T22 対象外として確認できる。
- script load 失敗のみ、retry 対象外としての明示が不足している。
- T21 との timeout / cancel、外部キャンセル優先、trace 状態を増やさない方針との矛盾は確認されなかった。
- `ExecutionTraceStep.Attempt` とログの `Attempt` は T22 完了条件を満たす設計として確認した。
- Markdown lint、用語 lint、focused textlint、`git diff --check` は成功した。
- 禁止された「日本語表記」形式は確認されなかった。
- 対象 report に虚偽の実行主体記録は確認されなかった。

## リスク

- 指摘 1 を未修正のまま実装に進むと、script load 失敗が retry 対象外であることを検査や実装から読み取りにくくなる。
