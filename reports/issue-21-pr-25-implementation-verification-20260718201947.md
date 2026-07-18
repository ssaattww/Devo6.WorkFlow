# Sub-agent実行レポート

## タスク

- 目的: PR #25 の scope 管理、nested composite、branch、retry、timeout、cancellation の実装を独立に検証し、疑わしい経路を再現する。
- タスク種別: 実装検証

## sub-agentを使う理由

- 理由: レビュー担当とは別視点で実装の再現性を確認するため。ユーザーが implementation 担当として terra / medium を指定したため。
- implementation verifier: terra
- dispatch profile: terra / medium（起動時に skill symlink を未追跡だったため、可視 tool schema 上の override 適用結果は未確認）

## 対象範囲

- 対象: PR #25 head `75c67a77b3ede7b201aba37fd40766f35a6bcdb7` の `EngineLoggingProvider`、`CompositeStep`、追加テストと関連経路。

## 対象外

- 対象外: コード変更、PRコメント投稿、ユーザー承認済みの失敗時 artifact 作成仕様、PR #25 と無関係な既存テスト失敗。

## 実行コマンド

- 実行コマンド: `git show 75c67a7:<path>`、`git diff --unified=50 0681e23e..75c67a7 -- <path>`、`git grep -n ... 75c67a7 -- <path>` で対象差分・設計・テストを確認した。
- 実行コマンド: `git archive -o <temp>/source.tar 75c67a77b3ede7b201aba37fd40766f35a6bcdb7` で対象 head をリポジトリ外の一時領域へ展開し、再現テストだけを追加した。
- 実行コマンド: `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~Pr25ReviewReproductionTests"`。timeout、外部 cancellation、nested retry の再現 3 件が成功した。
- 実行コマンド: `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~HierarchicalLoggingContractTests"`。既存の console ベース階層ログテストは 6/6 成功した。
- 実行コマンド: `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~EngineLoggingHierarchyTests|FullyQualifiedName~SwitchBranchLoggingSafetyTests"`。Windows でファイル共有違反により 0/7、7 件失敗を確認した。

## 対象ファイル

- 変更または確認したファイル: `src/Devo6.WorkFlow.Cli/EngineLoggingProvider.cs`、`src/Devo6.WorkFlow.Engine/CompositeStep.cs`、`tests/Devo6.WorkFlow.Tests/EngineLoggingHierarchyTests.cs`、`tests/Devo6.WorkFlow.Tests/HierarchicalLoggingContractTests.cs`、`tests/Devo6.WorkFlow.Tests/SwitchBranchLoggingSafetyTests.cs`、`doc/issue-21-hierarchical-logging-design.md`、`.github/workflows/pr-xunit-tests.yml` を対象 head で確認した。
- 変更または確認したファイル: リポジトリ内の実装・テストコードは変更していない。このレポートだけを更新し、一時領域に `Pr25ReviewReproductionTests.cs` を作成した。

## 指摘事項

- 指摘要約または「指摘なし」: **[高] 非協調的な nested Composite の timeout/cancellation で成功 lifecycle が出た後に workflow が失敗する。** `CompositeStep.cs:923-935` は内側 Step が token を無視して正常 return すると cancellation 状態を確認せず `Step succeeded` を出し、`CompositeStep.cs:797` も `Composite succeeded` を出す。その後、外側 engine の `CompositeStep.cs:1021-1035` が timeout/cancellation を検出し、同じ実行を `STEP_TIMEOUT` または `STEP_CANCELED` で失敗にする。再現では timeout と外部 cancellation の両方で `Composite succeeded` の直後に `Step stopped ... STEP_TIMEOUT/STEP_CANCELED` が並んだ。ログが最終結果と矛盾し、Issue #21 の lifecycle ログを監視に使えない。`ExecuteSimpleStepSequenceAsync` で各 Step/branch の await 後かつ producer・成功ログ前に `cancellationToken.ThrowIfCancellationRequested()` を行い、Composite 成功ログ前にも cancellation を確定させる修正が必要。
- 指摘要約または「指摘なし」: **[中] outer retry の Attempt が nested Composite の leaf Step scope で消える。** `EngineLoggingProvider.cs:591-595` は `StepName` を持つ node ごとに `attempt = nodeAttempt` とするため、外側 workflow Step の `Attempt=1/2` を、Attempt を持たない simple nested Step scope が null で上書きする。再現では outer Step が 2 回 retry されても `nested-retry-body-1/2` の両ログに `[attempt=...]` が無かった。一方、Composite started/succeeded には outer attempt が残るため同じ nested 実行内で文脈が不整合になる。Attempt を持つ最も内側の scope の値を維持するか、nested attempt の表現を設計で明示し、outer retry 1/2 と nested child body の Text/JSON テストを追加すべき。
- 指摘要約または「指摘なし」: **[中] PR 追加のファイルログテスト 7 件が Windows ではすべて共有違反で失敗する。** `EngineLoggingHierarchyTests.cs:40,90,134,199` と `SwitchBranchLoggingSafetyTests.cs:41,82,123` は `using` 中の `EngineLoggerFactory` が保持する writer を閉じる前に `File.ReadAllText/ReadAllLines` で同じファイルを開く。workflow は `ubuntu-latest` のみなので検出されていない。logger factory の scope を閉じてからファイルを読む形へテストを修正する必要がある。

## 結果

- 結果: PR #25 head の通常 Step、nested path、If/Switch branch、直接 retry の既存 console 契約は focused test 6/6 で成立した。一方、組合せ経路を追加再現した結果、timeout と外部 cancellation の双方で成功/失敗ログの矛盾、および outer retry attempt の nested leaf での欠落を確認した。Windows では追加ファイルログテスト 7 件もテスト実装上の共有違反で失敗した。以上から、階層ログ実装は修正が必要と判断する。

## リスク

- 未解決のリスクまたは後続対応: branch 内 nested Composite が timeout/cancellation された場合も同じ simple execution helper を通るため、成功ログ矛盾が branch path 付きで発生する可能性が残る。If/Switch × nested × timeout/cancellation の組合せテストが必要。
- 未解決のリスクまたは後続対応: 設計書のテスト計画には retry、失敗、cancellation、scope leak があるが、追加テストは timeout/cancellation と nested retry の複合経路を覆っていない。非協調 Step（token を無視して return）、token を尊重して `OperationCanceledException` を投げる Step、producer 前後の cancellation を分けて固定すべき。
- 未解決のリスクまたは後続対応: 一時展開した全体テストでは OS、symlink 権限、改行、NuGet fixture など PR #25 外の環境依存失敗も出たため、全体 303 件のローカル結果は合否根拠に使用していない。上記 finding は対象を絞った再現結果に基づく。
