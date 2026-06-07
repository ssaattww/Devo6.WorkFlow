# T22 retry 設計更新レポート

## 更新内容

- `doc/workflow_engine_spec.md` の 11.4 を、T22 の retry 実行契約に更新した。
- 14.5 に `WorkflowExecutionOptions.Retry` と `RetryOptions.MaxAttempts` を追加する公開 API 案を追記した。
- 18.1 から 18.4 に、retry 後の `WorkflowResult`、エラーコード、ログのスコープ、試行ごとの trace 契約を追記した。
- 19.1 から 19.3 に、T22 で扱う retry 範囲と次フェーズ候補を反映した。
- 21.1 に T22 の採用事項と T22 対象外を追記した。

## 採用方針

- `WorkflowExecutionOptions.Retry` と `RetryOptions.MaxAttempts` を採用する。
- `Retry = null` または `MaxAttempts <= 1` は retry なしとする。
- `MaxAttempts` は初回を含む最大試行回数とする。
- T22 の retry は全 Step 一律に限定する。
- retry 対象は Step 本体の通常例外だけとし、最終的に `STEP_EXECUTION_FAILED` になる候補に限定する。
- timeout と外部キャンセルは retry 対象外とし、両方観測時は外部キャンセルを優先する。
- `Produce`、`StoreAs`、`Discard` の失敗は retry 対象外とする。
- 成功した最後の試行だけ `Produce`、`StoreAs`、`Discard` を実行する。
- 全試行失敗時は `WorkflowResult.Succeeded = false`、`ErrorCode = STEP_EXECUTION_FAILED` とし、`ErrorMessage` は最後の例外 message を基本にする。
- `ExecutionTraceStep` に `Attempt` を追加し、試行ごとの trace 記録を残す。
- ログのスコープの `Attempt` は実試行番号にし、retry 予定と最終失敗を構造化ログで記録する。

## 対象外

- Step 別 retry 方針
- retry 待機時間制御
- retry の例外型による絞り込み
- CLI の retry オプション
- Config による retry 指定
- 実行中 Step の強制停止
- workflow 全体 timeout
- timeout またはキャンセル専用の trace 状態

## 検証結果

- `npm run lint:md`: 成功
- `npm run lint:md:terms`: 成功
- `git diff --check`: 成功
- focused textlint: 成功

## 残リスク

- `ExecutionTraceStep` への `Attempt` 追加は公開 C# record 型の構築子利用箇所に影響するため、実装時に既存呼び出し更新または互換性対策が必要。
- `Produce`、`StoreAs`、`Discard` 失敗を retry 対象外にする契約は、実装時に Step 本体の例外と成功後処理の例外を明確に分ける必要がある。
- CLI と Config から retry を指定する導線は T22 対象外のため、後続 task で `WorkflowExecutionOptions.Retry` へ写す仕様を別途決める必要がある。
