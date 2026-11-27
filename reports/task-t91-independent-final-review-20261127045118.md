# T91 / P40 パッケージ検査短縮の独立最終点検報告

## 対象と予約

- 対象: [Draft PR #29](https://github.com/ssaattww/Devo6.WorkFlow/pull/29)、[Todo](https://kero-todo-sync-pilot.ableisle37.chatgpt.site/todo?view=board#work-todo-56450696-4f5c-48e7-8653-67b1c67b386b)。
- 基点: `402c4cffbe5aa4c152d0f48fe566930d79b521d3`
- ブランチ: `test/pack-disable-msbuild-node-reuse`
- `reviewed_implementation_head`: `86c71ad3299bdfedaeb48779ea552724d1803d2f`
- 対象ツリー: `2387b2240479fd3bf9235d130ab1da72104c604a`
- 技術対象 HEAD と管理用証跡の第一親は上記の実装 HEAD。
- CodexSkill: `dfe07e81d6a1c209f99b908acc8109f70b38a773`
- 予約所有者: `review-enforcer`、予約 ID: `devo6-pr29-t91-ifr-20261127-v1`。
- 予約時刻: 2026-11-27T04:51:33Z。点検時の予約状態: `metadata_only`。
- 予約先: `reports/task-t91-independent-final-review-20261127045118.md`
- 保存方式: `report_attestation_commit`。作成時点は `commit_pending`。証跡版の送信と自動検査は未実施。

親担当が既存予約と固定対象の一致を確認し、独立担当の原本を引き継いだ。本書はその証跡であり、新たなレビューや再試験ではない。作成前にローカルと公開の HEAD の一致、作業ツリーに未コミット変更がなく、予約ファイルが存在しないことを確認した。

## 独立性と判定

- 既存実装、修正、通常レビュー、先行技術確認への関与がない別の作業チャットで、親担当が独立性を確認済み。
- 実行方式: `parallelism_mode: single_agent`、`decomposition_policy: forbidden`。
- 指定モデル: GPT-6.2 Sol High。実際の適用モデル、推論設定および実行時のメタデータは独立確認できておらず不明。
- 判定: **`pass_with_held`（保留事項付き合格）**。
- 必須指摘: 0件。任意修正事項: 0件。

検査項目を削減せず、パッケージ検査時間を短縮する目的を達成したとの独立担当の判定を記録する。

## 対象範囲と技術判定

[基点からの全差分](https://github.com/ssaattww/Devo6.WorkFlow/compare/402c4cffbe5aa4c152d0f48fe566930d79b521d3...86c71ad3299bdfedaeb48779ea552724d1803d2f) は4ファイル。コード変更は [ProjectSkeletonTests.cs の一行](https://github.com/ssaattww/Devo6.WorkFlow/blob/86c71ad3299bdfedaeb48779ea552724d1803d2f/tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs#L359) のみ。

```csharp
Environment = { ["MSBUILDDISABLENODEREUSE"] = "1" },
```

独立担当は `PackEnginePackage()` の子プロセスに限定される設定であることを、[.NET の公式仕様](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo?view=net-10.0) および [MSBuild の公式実装](https://github.com/dotnet/msbuild/blob/master/src/Build/BackEnd/BuildManager/BuildParameters.cs) と照合した。生成引数、Release 構成、一時出力先、DLL 同梱・Abstractions 依存・終了コードの検査、両対象フレームワーク、製品コード、公開 API、検査と配布の自動実行定義は不変。DLL の直接確認は従来どおり `lib/net8.0/` を対象とし、`lib/net10.0/` の新規個別確認とは扱わない。

通常報告と追跡の3文書は既存の証拠と整合するとの判定である。
[通常報告](https://github.com/ssaattww/Devo6.WorkFlow/blob/86c71ad3299bdfedaeb48779ea552724d1803d2f/reports/pr-29-ci-package-check-review-20261127.md)、[T91](https://github.com/ssaattww/Devo6.WorkFlow/blob/86c71ad3299bdfedaeb48779ea552724d1803d2f/tasks-status.md#L95)、[P40](https://github.com/ssaattww/Devo6.WorkFlow/blob/86c71ad3299bdfedaeb48779ea552724d1803d2f/phases-status.md#L49)。

## 検証証拠

独立担当は現行 [CI 45941338092](https://github.com/ssaattww/Devo6.WorkFlow/actions/runs/45941338092) の成功と、試験統合コミット `f73cce2cb43677cd98278c1ad6c03155708a72fa` のツリーが固定対象と一致することを確認した。比較元の統合ツリーも指定基点と一致した。

独立担当が3回分の TRX 原本から集計した結果は以下のとおり。

| 対象検査 | 変更前 | 現行 HEAD |
|---|---:|---:|
| .NET 8: DLL 同梱 | 912.55秒 | 14.21秒 |
| .NET 8: 依存確認 | 902.49秒 | 9.09秒 |
| .NET 10: DLL 同梱 | 905.33秒 | 8.46秒 |
| .NET 10: 依存確認 | 902.39秒 | 9.06秒 |

| 自動検査 | Test 工程の所要時間 | 成功／失敗／除外 |
|---|---:|---:|
| [変更前 45787570932](https://github.com/ssaattww/Devo6.WorkFlow/actions/runs/45787570932) | 約60分58秒 | 841／0／6 |
| [先行修正版 45920610954](https://github.com/ssaattww/Devo6.WorkFlow/actions/runs/45920610954) | 約3分18秒 | 841／0／6 |
| [現行版 45941338092](https://github.com/ssaattww/Devo6.WorkFlow/actions/runs/45941338092) | 約4分46秒 | 841／0／6 |

現行版は変更前から約92%短縮した。3分18秒は先行修正版のみの値である。3回の各 TFM のテスト名と結果の組合せがすべて一致したことも独立担当が確認しており、件数の同一性に加えて検査項目の削減がないことを示す。

独立担当は以下の成果物 ZIP の SHA-256 を再計算し、メタデータと一致することを確認した。

- [変更前 15104689018](https://github.com/ssaattww/Devo6.WorkFlow/actions/runs/45787570932/artifacts/15104689018)
- [先行修正版 15164333398](https://github.com/ssaattww/Devo6.WorkFlow/actions/runs/45920610954/artifacts/15164333398)
- [現行版 15172617613](https://github.com/ssaattww/Devo6.WorkFlow/actions/runs/45941338092/artifacts/15172617613)

現行成果物の SHA-256: `beca96d36b80aa984afd4dff8ee054df0142947e1da76f838c76ef208a59736a`。

本書の保存担当は原本から得られた独立担当の証拠を転記している。長時間の試験や独立点検は再実行していない。

## 観点別の判定

| 観点 | 判定 |
|---|---|
| T91 の要求・設計との整合 | `checked_no_finding` |
| 変更の正当性・プロセス境界 | `checked_no_finding` |
| 検査項目の維持 | `checked_no_finding` |
| 依存関係・公開 API・製品への影響 | `checked_no_finding` |
| CI・配布用定義への影響 | `checked_no_finding` |
| エラー報告・終了コードの確認 | `checked_no_finding` |
| セキュリティ・秘密情報 | `checked_no_finding` |
| 既存検証・現行 CI の妥当性 | `checked_no_finding` |
| 通常報告・追跡との整合 | `checked_no_finding` |
| 回帰・保守性 | `checked_no_finding` |

## 保留・未確認

- 親保有のローカル焦点検査4件と60秒打ち切り試験の原本は独立担当未読。
- Windows／FA780 の実行、公開 NuGet への配布は未実施。
- 実行環境や実行条件によって所要時間は変動し、将来の同じ短縮率を保証しない。
- Todo の ID とリンクは PR 本文と通常報告で対応付けを確認したが、Todo 画面自体は未確認。

これらを保留として維持し、判定を `pass_with_held` とする。

## 証跡の取り扱い

本書の技術判定は固定した実装 HEAD に属する。確認済み実装の後に予約済み報告書だけを追加する、一回限りの管理用証跡コミットとして保存する。第一親は `reviewed_implementation_head` とし、別のパスを変更しない。証跡コミットの SHA は作成後に本書の外で記録する。

証跡の公開後、その正確な版に対応する通常自動検査の結果を別に確認する。後続のコミットは通常の修正確認と同一独立担当の指摘・自動検査差分の限定確認を経ない限り、完了条件を無効にする。マージの承認または実施は本書の範囲外である。
