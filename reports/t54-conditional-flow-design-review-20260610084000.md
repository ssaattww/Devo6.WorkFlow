# Sub-agent実行レポート

## タスク

T54 条件付き実行設計反映とタスク分解のレビュー。

## sub-agentを使う理由

設計契約と追跡粒度の妥当性を、実装担当とは別の観点で点検するため。

## 対象範囲

- 現在の unstaged 差分
- `doc/workflow_engine_spec.md`
- `tasks-status.md`
- `phases-status.md`
- `reports/if-switch-execution-gist-design-20260610082643.md`
- `reports/t54-conditional-flow-design-update-20260610082652.md`

## 対象外

- C# 実装の追加
- 検査コードの追加
- README と sample の実装内容
- コミット、送信、取り込み依頼作成

## 実行コマンド

- `git status --short`
- `git diff --stat`
- `git diff -- doc/workflow_engine_spec.md tasks-status.md phases-status.md`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`
- `nl -ba doc/workflow_engine_spec.md`
- `nl -ba tasks-status.md`
- `nl -ba phases-status.md`
- `nl -ba reports/if-switch-execution-gist-design-20260610082643.md`
- `nl -ba reports/t54-conditional-flow-design-update-20260610082652.md`

## 対象ファイル

- `doc/workflow_engine_spec.md`
- `tasks-status.md`
- `phases-status.md`
- `reports/if-switch-execution-gist-design-20260610082643.md`
- `reports/t54-conditional-flow-design-update-20260610082652.md`
- `reports/t54-conditional-flow-design-review-20260610084000.md`

## 指摘事項

- High: `doc/workflow_engine_spec.md:1287` から `doc/workflow_engine_spec.md:1317` の `CompositeStep<TOut>` API 一覧が、Gist 原文にある `StepInput` を受ける条件と選択子の overload を落としている。具体的には、戻り値型が変わらない `RunIf<TStep>` / `RunIfAsync<TStep>`、`TapIf<TStep>` / `TapIfAsync<TStep>`、`If<TNext>`、`Switch<TCase,TNext>` に `Func<TOut, StepInput, ...>` 版がない。本文は `doc/workflow_engine_spec.md:370` で条件判定が `StepInput.Context` から Config を読める契約を置いているため、このままだと Config 依存の条件判定と selector を公開 API で表現できない。
- High: `doc/workflow_engine_spec.md:1351` から `doc/workflow_engine_spec.md:1377` の `BranchBuilder<TOut>` API 一覧が、`doc/workflow_engine_spec.md:744` で約束している分岐内の非同期 `Lambda Step`、`RunIf`、`TapIf`、入れ子の `If`、入れ子の `Switch` を定義できる形になっていない。Gist 原文も分岐内に `RunIf`、`RunIfAsync`、`TapIfAsync`、`If`、`Switch` を追加する前提を置いているため、T57 以降が本文の分岐内契約を満たせないおそれがある。

## 結果

- `git diff --stat` は `doc/workflow_engine_spec.md`、`phases-status.md`、`tasks-status.md` の 3 ファイルで 212 行追加、9 行削除を示した。未追跡の report 3 件は `git diff --stat` の対象外だったため、直接読んで確認した。
- `npm run lint:md` は成功した。
- `npm run lint:md:terms` は成功した。
- `git diff --check` は成功した。
- Gist 原文の中心契約である `fallback`、`TapIf` の `Unit` 制約、`If` / `Switch` の同一戻り値型、`Default` 必須、`case` 重複の定義時エラー、空分岐禁止、trace の `Skipped`、retry / timeout の境界、分岐 Config の事前検証は本文に反映されていた。
- 実装例を不要とした指示について、既存設計書側には Gist 原文の内部実装例は増えていない。Gist 原文保存 report には原文由来の例が残るが、原文保存の対象として扱った。
- T55-T60 と P28 は task ごとの点検を前提に分かれている。ただし上記 API 欠落を直さないまま進むと、T55-T58 の実装単位が設計本文の契約とずれる。

## リスク

- 今回はユーザー指定により Serena、nested Codex、sub-agent 起動を使わず、レビュー担当として直接点検した。`review-enforcer` の通常 sub-agent 要求とは異なるため、独立 reviewer 実行としての完全な skill 契約は満たしていない。
- C# 実装と検査コードは対象外のため、API 署名が実装可能かどうかは未検証である。
- `reports/` 配下は通常の `npm run lint:md` 対象に含まれないため、この review report 自体の文体検査は repository の full lint では確認されない。
