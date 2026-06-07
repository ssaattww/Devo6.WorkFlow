# T21レビュー指摘修正レポート

## 対象

- 修正対象: `reports/t21-timeout-cancellation-final-review-20260607010500.md` の指摘
- 担当: T21 レビュー指摘の修正担当 worker
- 対象ファイル:
  - `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - `tests/Devo6.WorkFlow.Tests/AsyncStepApiContractTests.cs`
  - `tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs`
  - `reports/t21-timeout-cancellation-standards-check-20260607005724.md`

## 修正内容

- pre-cancel 済みの単一 sync Step を成功扱いにする例外を削除した。
- 外部 `CancellationToken` が sync Step 開始前から cancel 済みの場合、sync Step 完了後に `STEP_CANCELED` の失敗結果へ変換する契約へ更新した。
- sync Step 実行中に外部 `CancellationToken` が cancel 済みになった場合、sync Step 完了後に `STEP_CANCELED` の失敗結果へ変換し、`Produce` と後続 Step を止める検査を追加した。
- standards check report の「親側で直接確認した」という記載を、sub-agent が確認した事実と整合する表現に修正した。

## レビュー指摘ごとの対応

### Blocker

- 対応済み。
- `CompositeStep.DetectCancellationFailure` から pre-cancel 済み単一 sync Step の成功維持分岐を削除した。
- `AsyncStepApiContractTests` の既存 pre-cancel sync Step 検査を、成功期待から `STEP_CANCELED` 期待へ更新した。
- 更新後の検査では、結果失敗、`ErrorCode` が `STEP_CANCELED`、trace が `Failed`、trace の `ErrorCode` が `STEP_CANCELED`、`Produce` 未実行を確認する。

### Minor

- 対応済み。
- `reports/t21-timeout-cancellation-standards-check-20260607005724.md` のリスク欄を、sub-agent による標準確認として整合する記載へ修正した。

## TDD結果

- 実装前に `AsyncStepApiContractTests` の pre-cancel sync Step 検査を T21 契約へ更新した。
- 実装前の失敗確認:
  - コマンド: `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~AsyncStepApiContractTests`
  - 結果: 失敗。
  - 失敗内容: `PreCancelledTokenConvertsSyncStepCompletionToStepCanceled` で `Assert.False()` が失敗し、実際の結果が成功だった。
- 実装後の成功確認:
  - コマンド: `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~AsyncStepApiContractTests`
  - 結果: 成功。4 件成功。
- 追加契約確認:
  - コマンド: `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~TimeoutCancellationContractTests`
  - 結果: 成功。5 件成功。

## 検証結果

- `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~AsyncStepApiContractTests`: 成功。4 件成功。
- `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~TimeoutCancellationContractTests`: 成功。5 件成功。
- `dotnet test Devo6.WorkFlow.sln`: 成功。70 件成功。
- `npm run lint:md`: 成功。
- `npm run lint:md:terms`: 成功。SudachiPy term variants は none。
- `git diff --check`: 成功。
- 新規 report の focused textlint: 成功。

## 残リスク

- 既存の他者変更と未追跡ファイルが作業ツリーに残っているため、今回の worker は所有範囲内の最小差分だけを変更した。
- 今回の修正範囲ではブロッカーは残っていない。
