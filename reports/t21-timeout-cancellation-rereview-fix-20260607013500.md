# T21 timeout cancellation 再レビュー修正レポート

## タスク

- 目的: T21 再レビューで指摘された sync Step 完了後の timeout と外部 cancel 同時観測時の優先順位を修正する。
- 対象 report: `reports/t21-timeout-cancellation-rereview-20260607012500.md`
- 対象範囲: `src/Devo6.WorkFlow.Engine/CompositeStep.cs`、`tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs`、`tests/Devo6.WorkFlow.Tests/AsyncStepApiContractTests.cs`

## 修正内容

- `DetectCancellationFailure` の完了後判定を外部 `CancellationToken` 優先に変更した。
- sync Step 実行中に `StepTimeout` と外部 cancel が両方発火した場合に `STEP_CANCELED` を返す regression test を追加した。
- 同時観測ケースで `Produce` と後続 Step が実行されないこと、trace が `Failed` で `STEP_CANCELED` になることを確認する assertion を追加した。
- `AsyncStepApiContractTests.cs` の pre-cancel sync Step テストの XML 文書注釈を日本語へ修正した。

## 再レビュー指摘への対応

- Blocker: 対応済み。
- 修正前は同時観測ケースで `STEP_TIMEOUT` が返っていた。
- 修正後は外部 cancel を先に判定するため、timeout と外部 cancel が両方観測された場合は `STEP_CANCELED` が返る。
- cancellation failure が返る時点で `ToCancellationWorkflowResult` に進むため、対象 Step の `Produce` と後続 Step は実行されない。

## TDD

- 先に `ExternalCancellationWinsWhenSyncStepObservesTimeoutAndCancellation` を追加した。
- 実装修正前の赤確認:
  - command: `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~TimeoutCancellationContractTests.ExternalCancellationWinsWhenSyncStepObservesTimeoutAndCancellation`
  - result: failed
  - failure: `Expected: STEP_CANCELED`、`Actual: STEP_TIMEOUT`
- 実装修正後の緑確認:
  - command: `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~TimeoutCancellationContractTests.ExternalCancellationWinsWhenSyncStepObservesTimeoutAndCancellation`
  - result: passed。1 件成功。

## ユーザー標準確認

- 追加したテスト関数名と helper class 名は英語のままにした。
- 追加したテストメソッドと helper `Execute` には日本語の XML 文書注釈を追加した。
- `AsyncStepApiContractTests.cs` の pre-cancel sync Step テスト説明は日本語へ修正した。
- 今回の所有範囲で追加または変更した関数、プロパティ、テスト周辺の説明文を確認した。

## 検証結果

- `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~TimeoutCancellationContractTests`: passed。6 件成功。
- `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~AsyncStepApiContractTests`: passed。4 件成功。
- `dotnet test Devo6.WorkFlow.sln`: passed。71 件成功。
- `npm run lint:md`: passed。
- `npm run lint:md:terms`: passed。`SudachiPy term variants: none`
- `git diff --check`: passed。
- focused textlint: passed。

## 残リスク

- 今回の修正は再レビュー指摘の優先順位に限定している。
- full Markdown lint は repository の Markdown target 設定に従うため、この report は focused textlint で個別確認する。
