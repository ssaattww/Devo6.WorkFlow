# T22 retry 失敗検査レポート

## タスク

T22「retry 実行契約」の TDD 失敗検査を追加した。

## 追加した検査

- `WorkflowExecutionOptions.Retry` と `RetryOptions.MaxAttempts` の公開 API 検査
- `ExecutionTraceStep.Attempt` の公開 API 検査
- Step 本体の通常例外が 2 回失敗し、3 回目に成功する途中成功検査
- Step 本体の通常例外が `MaxAttempts = 3` ですべて失敗する検査
- timeout が retry されず、`STEP_TIMEOUT` で停止する検査
- 外部キャンセルが retry されず、`STEP_CANCELED` で停止する検査
- `Produce` 失敗が Step 本体を retry しない検査
- retry の attempt が trace と log scope の `Attempt` に残る検査

## 期待する失敗

実装前は以下が未実装のため、対象テストは compile error で赤になる。

- `RetryOptions`
- `WorkflowExecutionOptions.Retry`
- `ExecutionTraceStep` の 5 引数 constructor
- `ExecutionTraceStep.Attempt`

## 実際の失敗

`dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter RetryExecutionContractTests` は失敗した。

主な error は以下。

- `CS0246`: `RetryOptions` が見つからない。
- `CS0117`: `WorkflowExecutionOptions` に `Retry` がない。
- `CS1729`: `ExecutionTraceStep` に 5 引数 constructor がない。
- `CS1061`: `ExecutionTraceStep` に `Attempt` がない。

これは TDD の赤として期待どおり。

## 実装時の注意

- `RetryOptions.MaxAttempts` は初回を含む最大試行回数として扱う。
- retry 対象は Step 本体の通常例外だけに限定する。
- timeout と外部キャンセルは retry せず、1 回の失敗 trace だけを残す。
- `Produce` 失敗は既存契約どおり `STEP_EXECUTION_FAILED` として扱い、Step 本体は再実行しない。
- retry された同一 Step は attempt ごとに trace record を追加する。
- 後続 Step は、retry 対象 Step が最終的に成功した後に 1 回だけ実行する。
- log scope の `Attempt` は固定値ではなく実試行番号にする。

## 検証結果

- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter RetryExecutionContractTests`: 失敗。API 未実装による compile error を確認。
- `npm run lint:md`: 成功。
- `npm run lint:md:terms`: 成功。
- `git diff --check`: 成功。

## 変更ファイル

- `tests/Devo6.WorkFlow.Tests/RetryExecutionContractTests.cs`
- `reports/t22-retry-failing-tests-20260607025000.md`

## ブロッカー

検査作成側のブロッカーはない。

次の実装では、公開 API と retry 実行 loop を追加し、この赤を runtime assertion まで進める必要がある。
