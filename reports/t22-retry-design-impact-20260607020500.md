# Sub-agent実行レポート

## タスク

- 目的: T22 の retry 実行契約について、設計影響と採用方針候補を調査する。
- タスク種別: investigation / design impact

## sub-agentを使う理由

- 理由: ユーザー指示により、調査、実装、レビューは sub-agent に委譲し、parent はマネージャーとして扱う前提のため。

## 対象範囲

- 対象: `tasks-status.md` の T22、`phases-status.md` の P7、`doc/workflow_engine_spec.md` の retry / timeout / trace / logging / execution options 関連節、`CompositeStep`、`WorkflowExecutionOptions`、公開 API、既存契約テストを確認し、T22 の設計更新案を提示する。

## 対象外

- 対象外: 設計本文の編集、コード実装、検査実装、tracking 更新、commit、README 更新。

## 実行コマンド

- 実行コマンド:
  - `git status --short --branch`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-doc-maintainer/SKILL.md`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/report-output-manager/SKILL.md`
  - `rg -n "T22|retry|再試行|リトライ" tasks-status.md phases-status.md doc/workflow_engine_spec.md`
  - `rg -n "timeout|STEP_TIMEOUT|STEP_CANCELED|trace|logging|log|ExecutionOptions|WorkflowExecutionOptions|Produce|retry|再試行" doc/workflow_engine_spec.md`
  - `rg --files src/Devo6.WorkFlow.Abstractions src/Devo6.WorkFlow.Engine tests/Devo6.WorkFlow.Tests`
  - `rg -n "retry|Retry|timeout|Timeout|STEP_TIMEOUT|STEP_CANCELED|trace|Trace|WorkflowExecutionOptions|Produce|CompositeStep|ExecutionOptions|logging|Logger|Log" src tests/Devo6.WorkFlow.Tests`
  - `sed -n '330,430p' doc/workflow_engine_spec.md`
  - `sed -n '560,670p' doc/workflow_engine_spec.md`
  - `sed -n '830,890p' doc/workflow_engine_spec.md`
  - `sed -n '1180,1290p' doc/workflow_engine_spec.md`
  - `sed -n '1300,1410p' doc/workflow_engine_spec.md`
  - `sed -n '1,230p' src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - `sed -n '230,580p' src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - `sed -n '1,200p' src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
  - `for f in src/Devo6.WorkFlow.Abstractions/*.cs; do printf '\n### %s\n' "$f"; sed -n '1,180p' "$f"; done`
  - `sed -n '1,260p' tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs`
  - `sed -n '260,460p' tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs`
  - `sed -n '1,170p' tests/Devo6.WorkFlow.Tests/AsyncStepApiContractTests.cs`
  - `sed -n '1,220p' tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
  - `sed -n '1,150p' tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs`
  - `sed -n '1,130p' src/Devo6.WorkFlow.Cli/Program.cs`
  - `sed -n '1,140p' reports/t21-timeout-cancellation-design-impact-20260607001742.md`
  - `sed -n '1,130p' reports/t20-async-step-api-design-impact-20260606232258.md`
  - `npx textlint reports/t22-retry-design-impact-20260607020500.md --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)"`

## 対象ファイル

- 変更または確認したファイル:
  - 変更: `reports/t22-retry-design-impact-20260607020500.md`
  - 確認: `tasks-status.md`
  - 確認: `phases-status.md`
  - 確認: `doc/workflow_engine_spec.md`
  - 確認: `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - 確認: `src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
  - 確認: `src/Devo6.WorkFlow.Cli/Program.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/IAsyncStep.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/IStep.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/StepContext.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/StepInput.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/WorkflowResult.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/AsyncStepApiContractTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs`
  - 確認: `reports/t21-timeout-cancellation-design-impact-20260607001742.md`
  - 確認: `reports/t20-async-step-api-design-impact-20260606232258.md`

## 指摘事項

- T22 は `tasks-status.md` で未着手であり、完了条件は「指定回数の再試行、途中成功、全試行失敗、retry 対象外エラー、試行番号のログと trace」を検査で確認することになっている。
- P7 は進行中で、T20 と T21 は完了済み、T22 で非同期 Step API、timeout、retry が矛盾しない契約として提出可能な状態になることを求めている。
- 現在の設計書 11.4 は retry を未実装とし、retry 対象を Step 実行中の一時的な例外に限定する方針だけを持つ。入力取得失敗、Config 検証失敗、`.csx` コンパイル失敗、参照解決失敗は retry 対象外として明記済み。
- T21 後の設計では `WorkflowExecutionOptions.StepTimeout`、`STEP_TIMEOUT`、`STEP_CANCELED`、timeout / 外部キャンセル時の `Produce` と後続 Step 停止が固定されている。T22 はこの結果分類を上書きしない設計が必要。
- `WorkflowExecutionOptions` は logger、`EngineArguments`、`StepTimeout` を持つ。T22 で全体一律の retry 指定を入れる場合、既存の実行時オプション面に載せるのが最小。
- `CompositeStep` の Step 登録は `StepRegistration` の内部配列で、公開 Step 登録 API は `Run` / `RunAsync` / `Produce` / `StoreAs` / `Discard` に限定されている。Step ごとの retry API を追加すると公開 DSL が広がる。
- `CompositeStep.ExecuteWorkflowAsync` は Step ごとに timeout token を作り、成功時だけ `Produce` し、失敗時は trace に 1 件の失敗 Step を追加して終了する。retry を加える場合は、この Step 実行単位を retry ループで包む形が自然。
- `ExecutionTraceStep` は `StepName`、`Status`、`Duration`、`ErrorCode` の公開 record で、試行番号を持たない。T22 の「試行番号の trace」を満たすには公開 API 追加が必要。
- ログ scope は Entry と Step の両方で `Attempt = 1` を含めている。T22 では固定値を実試行番号に置き換えれば、ログ側の契約は公開 API 追加なしで満たせる。
- 公開 C# API には英語名が使われているが、既存の XML 文書注釈は英語と日本語が混在している。ユーザー標準上、T22 で追加する関数、プロパティ、型の説明文は日本語にそろえる必要がある。

## 結果

- 採用案: T22 の最小指定場所は `WorkflowExecutionOptions` とする。例として `int MaxRetryAttempts` または `RetryOptions? Retry` を追加し、既定は retry なしにする。T22 の範囲では全 Step 一律の指定に限定する。
- 採用案: 名前は「最大再試行回数」より「最大試行回数」の方が誤解が少ない。指定回数を「retry 回数」と読むなら総試行回数は `1 + retry 回数` になるため、設計書には `MaxRetryAttempts = 2` なら最大 3 回実行、または `MaxAttempts = 3` なら最大 3 回実行のどちらかを明記する必要がある。
- 採用案: この調査では `WorkflowExecutionOptions.Retry` と `RetryOptions.MaxAttempts` を推奨する。`Retry = null` または `MaxAttempts <= 1` は retry なし、`MaxAttempts = 3` は初回を含め最大 3 回と定義すると、ログと trace の試行番号が `1..MaxAttempts` でそろう。
- 採用案: retry 対象は Step 実行本体が投げた通常例外に限定し、`WorkflowErrorCodes.StepExecutionFailed` に変換される候補だけとする。`OperationCanceledException` のうち timeout token 由来は `STEP_TIMEOUT`、外部 token 由来は `STEP_CANCELED` とし、retry 対象外にする。
- 採用案: 入力取得失敗と `Produce` 失敗は retry 対象外にする。T22 の設計書には「retry は Step 本体の一時的失敗だけで、Step 成功後の `Produce`、`StoreAs`、`Discard`、後続 Step、事前検証、script load、Config 検証は再実行しない」と書くべき。
- 採用案: 途中成功時は、失敗した試行の `Produce` は実行せず、成功した最後の試行の戻り値だけに `Produce` を実行する。後続 Step は成功後に 1 回だけ開始する。
- 採用案: 全試行失敗時は `WorkflowResult.Succeeded = false`、`ErrorCode = STEP_EXECUTION_FAILED`、`ErrorMessage` は最後の例外 message を基本とする。ログには各失敗試行と最終失敗を記録する。
- 採用案: trace は各試行を個別に記録する。`ExecutionTraceStep` に `int Attempt` を追加し、同じ Step 名で failed attempt を複数件、途中成功時は最後に succeeded attempt を 1 件追加する。既存状態 enum は増やさず、`Succeeded` / `Failed` と `ErrorCode` で表す。
- 採用案: timeout は試行ごとの timeout とする。各 retry attempt の開始時に T21 と同じ `StepTimeout` 用 token を新しく作り直す。1 回の attempt が `STEP_TIMEOUT` または `STEP_CANCELED` になった場合は retry せず、その場で workflow を失敗終了する。
- 採用案: 同期 Step の timeout は T21 と同じく強制中断しない。同期 Step 完了後に timeout が観測された場合は `STEP_TIMEOUT` とし、retry 対象外、`Produce` と後続 Step は実行しない。
- 採用案: 外部キャンセルは常に timeout より優先し、retry ループも停止する。T21 の「両方観測時は `STEP_CANCELED` 優先」を維持する。
- 採用案: ログ scope の `Attempt` は attempt 番号に更新する。Step 開始、Step 失敗、Step retry 予定、Step 成功、Entry 失敗のログに `EntryName`、`StepName`、`Attempt`、最終試行かどうか、`ErrorCode` を構造化情報として入れる。
- 採用案: CLI Config は T22 では指定場所にしない。CLI には現在 timeout option もなく、README は T30 前提のため、T22 で CLI 引数や README を広げると範囲が増える。後続 T23 以降の標準 Config 読み込みや CLI option と矛盾しないよう、engine option を先に確定する。
- 代替案: Step 登録時 API として `.Retry(...)` を `Run` / `RunAsync` 後に追加する案は、Step ごとの制御には向く。ただし `CompositeStep<TOut>` の公開 DSL が広がり、T22 の最小範囲を超える。後続で Step 別 policy が必要になったときの候補に残す。
- 代替案: Config または CLI の retry 指定は利用者には自然だが、標準 Config 読み込みや CLI option は別 task の対象である。T22 では engine 内契約を先に固定し、CLI / Config は後続で `WorkflowExecutionOptions.Retry` に写す設計にするのが安全。
- 代替案: trace に retry 用の専用 status や retry event 型を足す案は表現力が高い。ただし T21 で専用状態を追加しない方針が採用されているため、T22 では `Attempt` 追加に限定する方が矛盾が少ない。
- 設計更新対象: 11.4 を「retry と timeout」から T22 採用契約へ更新し、対象例外、対象外エラー、timeout / cancel との優先順位、`Produce` と後続 Step の扱いを明記する。
- 設計更新対象: 14.5 `WorkflowExecutionOptions` に retry option の公開 API 案を追加する。追加する型とプロパティは英語名、XML 文書注釈は日本語、すべての関数とプロパティに説明文を付ける。
- 設計更新対象: 18.1 / 18.2 に retry 後の `WorkflowResult` と error code の扱いを追記する。retry 専用 error code は不要で、全試行失敗は `STEP_EXECUTION_FAILED` を維持する案を推奨する。
- 設計更新対象: 18.3 ログに attempt 番号、retry 予定、最終失敗の構造化ログ項目を追記する。
- 設計更新対象: 18.4 トレースに attempt 番号を追加し、retry された同一 Step が複数 trace record になること、未実行の後続 Step は trace に追加しないことを追記する。
- 設計更新対象: 19.1 / 19.2 / 19.3 に T22 で retry を扱う範囲と、CLI retry option、Step 別 retry policy、backoff、例外型 filter、標準 Config 経由指定を対象外として残すことを反映する。
- 設計更新対象: 21.1 または未確定事項に、T22 の retry 採用方針と後続課題を整理する。T21 の timeout 方針は変更せず、retry と timeout の統合は「timeout/cancel は retry しない」という最小統合として扱う。
- TDD 検査案: 先頭に利用者目線の `ExecuteWorkflowAsync` 契約テストを置く。失敗する Step が 2 回例外を投げ、3 回目で成功し、`WorkflowExecutionOptions.Retry.MaxAttempts = 3` で `WorkflowResult.Succeeded = true`、後続 Step が 1 回だけ実行されることを確認する。
- TDD 検査案: 全試行失敗では attempt が指定回数分だけ実行され、`WorkflowResult.ErrorCode = STEP_EXECUTION_FAILED`、trace は同一 Step の failed attempt を指定回数分持ち、後続 Step と `Produce` は実行されないことを確認する。
- TDD 検査案: retry 対象外エラーとして `Produce` selector の例外、`StepInput.Get<T>()` の未登録値、`STEP_TIMEOUT`、`STEP_CANCELED` を分けて確認する。少なくとも T22 最初の検査では `Produce` 例外と timeout を入れると T21 との矛盾を検出しやすい。
- TDD 検査案: ログ検査では記録用 logger の scope を保持できるようにし、Step 開始と失敗ログの `Attempt` が `1, 2, 3` になること、固定 `Attempt = 1` が残らないことを確認する。
- TDD 検査案: 公開 API 検査では `WorkflowExecutionOptions` の retry property と `ExecutionTraceStep.Attempt` の型、既定値、読み取り可能性を確認する。
- TDD 検査案: E2E 寄りの検査として、`CompositeStep.Define("Main")` から retry option を渡して実行し、途中成功時に後続 Step が成功出力だけを受け取ることを確認する。CLI 引数を使う E2E は T22 では置かない。
- ユーザー標準の注意点: T22 で追加する C# 関数名、型名、プロパティ名は英語にする。C# 文書注釈は日本語にし、追加または変更する関数とプロパティには説明文を必ず付ける。README は T30 対象なので T22 では更新しない。
- 検査結果: report focused textlint は成功した。

## リスク

- 未解決のリスクまたは後続対応:
  - `RetryOptions.MaxAttempts` と `MaxRetryAttempts` は意味が混同されやすい。設計更新時に初回を含むかどうかを必ず固定する必要がある。
  - trace に `Attempt` を追加する案は公開 API 変更であり、既存 `ExecutionTraceStep` record の constructor 利用箇所を更新する必要がある。互換性を重視するなら overload 追加などの検討が必要。
  - `Produce` 失敗を retry 対象に含めると、Step 本体は成功済みなのに副作用を持つ Step が再実行される。T22 では retry 対象外にしないと利用者視点で危険。
  - 同期 Step は timeout で強制中断されないため、長時間実行した同期 Step が完了後に `STEP_TIMEOUT` になる。これを retry すると長時間処理を繰り返す危険があるため、timeout は retry 対象外にする必要がある。
  - 外部キャンセルを retry すると利用者の停止要求に反する。`STEP_CANCELED` は retry ループを止める契約が必要。
  - Step 別 retry、backoff、例外型 filter、CLI / Config 指定を同時に入れると T22 の範囲を超える。後続 task と矛盾しないよう、T22 は engine option の最小一律契約に限定するのが安全。
  - このレポートでは設計本文とコードを編集していないため、T22 実装前に `doc/workflow_engine_spec.md` の設計更新が必要。
