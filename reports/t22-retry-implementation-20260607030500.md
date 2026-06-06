# T22 retry 実装レポート

## TDD 結果

- 赤: `reports/t22-retry-failing-tests-20260607025000.md` で、`RetryOptions`、`WorkflowExecutionOptions.Retry`、`ExecutionTraceStep.Attempt` が未実装のため compile error になることを確認済み。
- 緑: `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter RetryExecutionContractTests` は成功した。

## 変更内容

- `WorkflowExecutionOptions.Retry` を追加した。
- `RetryOptions.MaxAttempts` を追加し、初回を含む最大試行回数として扱った。
- `ExecutionTraceStep.Attempt` を追加し、既存 4 引数 constructor は attempt 1 を設定する互換 constructor とした。
- `CompositeStep.ExecuteWorkflowAsync` に Step 本体の retry loop を追加した。
- Step log scope の `Attempt` を実試行番号にした。

## 設計との対応

- `Retry = null` または `MaxAttempts <= 1` は retry なしとして、最大試行回数を 1 に正規化した。
- Step 本体の通常例外だけを retry し、attempt ごとに failed trace を追加した。
- 全試行失敗時は `STEP_EXECUTION_FAILED`、最後の例外 message、failed trace 1..MaxAttempts、後続 Step 未実行とした。
- 途中成功時は、成功した最後の attempt の戻り値だけを `Produce` し、後続 Step を 1 回だけ実行する。
- retry 待機、Step 別 retry、例外型 filter、CLI / Config 指定は T22 対象外のまま変更していない。

## retry 対象外の扱い

- timeout は retry せず、発生した attempt の `STEP_TIMEOUT` trace 1 件で停止する。
- 外部キャンセルは retry せず、発生した attempt の `STEP_CANCELED` trace 1 件で停止する。
- timeout と外部キャンセルの両方が観測される場合は、既存契約どおり外部キャンセルを優先する。
- `Produce`、`StoreAs`、`Discard` の失敗は retry せず、Step 本体を再実行しない。
- `StepInput.Get<T>()` などが Step 本体の `Execute` / `ExecuteAsync` 内で投げた通常例外は、実装上は Step 本体例外として retry 対象になる。これは T22 入力で指定された「通常の Step 本体例外として発生するなら retry 対象」の方針に合わせた。設計上の retry 対象外である事前検証、Config 検証、script load、`.csx` コンパイル、参照解決とは実行位置が異なるため、矛盾しない。

## 検証結果

- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter RetryExecutionContractTests`: 成功
- `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~TimeoutCancellationContractTests`: 成功
- `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~AsyncStepApiContractTests`: 成功
- `dotnet test Devo6.WorkFlow.sln`: 成功
- `npm run lint:md`: 成功
- `npm run lint:md:terms`: 成功
- `git diff --check`: 成功
- focused textlint: 成功

## 残リスク

- `Produce`、`StoreAs`、`Discard` 失敗は retry 対象外として扱うため、Step 本体の過去 attempt 失敗 trace が既に存在する場合でも、成功 attempt 後の post-processing 失敗で終了する。
- backoff、例外型 filter、Step 別 retry は未実装であり、後続 task の対象として残る。
