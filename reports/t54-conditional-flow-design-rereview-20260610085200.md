# Sub-agent実行レポート

## タスク

T54 条件付き実行設計反映の再レビュー。

## sub-agentを使う理由

初回レビューの High 指摘を修正したため、設計契約と API 案が整合したか独立して確認するため。

## 対象範囲

- 現在の unstaged 差分
- `doc/workflow_engine_spec.md`
- `tasks-status.md`
- `phases-status.md`
- `reports/t54-conditional-flow-design-review-20260610084000.md`

## 対象外

- C# 実装の追加
- 検査コードの追加
- README と sample の実装内容
- コミット、送信、取り込み依頼作成

## 実行コマンド

- `sed -n '1,240p' /home/ibis/AI/CodexSkill/skills/review-enforcer/SKILL.md`
- `sed -n '1,240p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `sed -n '1,260p' reports/t54-conditional-flow-design-rereview-20260610085200.md`
- `git status --short`
- `git diff --stat`
- `git diff -- doc/workflow_engine_spec.md tasks-status.md phases-status.md`
- `sed -n '1,260p' reports/t54-conditional-flow-design-review-20260610084000.md`
- `sed -n '1,520p' reports/if-switch-execution-gist-design-20260610082643.md`
- `sed -n '1,260p' reports/t54-conditional-flow-design-update-20260610082652.md`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`
- `nl -ba doc/workflow_engine_spec.md`
- `nl -ba tasks-status.md`
- `nl -ba phases-status.md`
- `rg -n "retry 対象|retry と timeout|If.*Switch.*retry|Step 本体" doc/workflow_engine_spec.md`
- `rg -n "Default|case|重複|空分岐|Config|StepContext|Skipped|CONDITION|SWITCH|fallback" doc/workflow_engine_spec.md`

## 対象ファイル

- `doc/workflow_engine_spec.md`
- `tasks-status.md`
- `phases-status.md`
- `reports/if-switch-execution-gist-design-20260610082643.md`
- `reports/t54-conditional-flow-design-update-20260610082652.md`
- `reports/t54-conditional-flow-design-review-20260610084000.md`
- `reports/t54-conditional-flow-design-rereview-20260610085200.md`

## 指摘事項

- Medium: `doc/workflow_engine_spec.md:2277` は `RunIf` false の trace を `Skipped` として記録しつつ、その `fallback` 値に対する `Produce` や `StoreAs` を通常どおり実行できるとしている。一方で `doc/workflow_engine_spec.md:2094` は trace 値の基礎単位を「Step 成功後」の値登録としており、`Skipped` trace に `ProducedValues` を持たせるのか、値登録だけ行って trace 値は空にするのかが不明確である。T56 と T59 で trace 検査の期待値が割れるおそれがある。
- Low: `doc/workflow_engine_spec.md:2445` の補足設計詳細では retry 対象を「Step 本体の通常例外だけ」としており、`doc/workflow_engine_spec.md:961` と `doc/workflow_engine_spec.md:2592` で追加された `Lambda Step`、`RunIf`、`TapIf` への retry / timeout 適用が補足側の一覧に反映されていない。本文の主契約は読めるが、T59 の統合時に補足節だけを参照すると条件付き実行の retry 境界を取り落とすおそれがある。

## 結果

- 初回 High 指摘 2 件は解消されている。`CompositeStep<TOut>` の API 一覧には `StepInput` を受ける `RunIf`、`TapIf`、`If`、`Switch` が追加されている。`BranchBuilder<TOut>` の API 一覧にも、分岐内の非同期 `Lambda Step`、`RunIf`、`TapIf`、入れ子 `If`、入れ子 `Switch` が追加されている。
- Gist 原文の重要契約である `fallback`、`TapIf` の `Unit` 制約、`If` / `Switch` の同一戻り値型、`Default` 必須、`case` 重複の定義時エラー、空分岐禁止、分岐 Config の事前検証、条件判定前 Config 登録、trace の `Skipped`、retry / timeout の境界は、おおむね設計書に反映されている。
- 既存設計書側に、新規の具体的な利用例や実装例の章は増えていない。`WithCriteria` の短い断片は採用しない形を説明する反例であり、Gist 原文保存 report 内の具体例は原文保存対象として扱った。
- T55-T60 と P28 は、検査先行、実装、task ごとの点検、最終検証に分かれており、1 task ごとにコミット可能な単位として扱える粒度である。
- `git diff --stat` は `doc/workflow_engine_spec.md`、`phases-status.md`、`tasks-status.md` の 3 ファイルで 336 行追加、9 行削除を示した。未追跡 report 4 件は `git diff --stat` には出ないため、直接読んで確認した。
- `npm run lint:md` は成功した。
- `npm run lint:md:terms` は成功した。
- `git diff --check` は成功した。

## リスク

- 今回はユーザー指定により Serena、nested Codex、development-orchestrator、sub-agent 起動を使わず、再レビュー担当として直接点検した。`review-enforcer` の通常 sub-agent 要求とは異なるため、skill の通常実行形とは一致していない。
- C# 実装と検査コードは対象外のため、API 署名が実装可能かどうかと、trace / retry / timeout の実装上の整合は未検証である。
- `reports/` 配下は通常の `npm run lint:md` 対象に含まれないため、report 文面は手動確認で扱った。
