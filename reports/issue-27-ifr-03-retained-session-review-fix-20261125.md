# 課題 #27 IFR-03 修正記録

## 対象

- リポジトリ: `ssaattww/Devo6.WorkFlow`
- 枝: `feat/issue-27-file-based-csharp`
- 着手時 HEAD: `2451e8134b8eb3e272a7f6133f961a38d3515bd6`
- 対象: 通常再確認 IFR-03 の診断領域保持。IFR-04 は変更しない。

## 変更

`FileBasedCsWorkflowBuilder.Validate` と `Run` は、生成物の検査または処理が失敗・取消となったときに成功扱いの `Dispose()` を呼ばない。作業用領域を残し、検証エラーまたは実行結果の診断にその場所を加える。両処理が成功したときだけ領域を回収する。公開 `Build` の呼出し側が成功結果を `Dispose()` して回収する契約と、SDK 生成自体の失敗時に診断領域を残す動作は維持した。

焦点試験は、入口情報のない安全な試験アセンブリによる検証失敗、Step の通常失敗、Step 内での協調取消、正常完了後の回収を確認する。

## 変更ファイル

- `src/Devo6.WorkFlow.Engine/FileBasedCsWorkflowBuilder.cs`: 成功時だけ内部の一時領域を回収し、失敗時の保存先を返す。
- `tests/Devo6.WorkFlow.Tests/FileBasedCsWorkflowBuilderLifecycleTests.cs`: 検証失敗、Step 失敗・取消、成功時の寿命を確認。
- `doc/issue-27-dotnet10-file-based-app-design.md`: 失敗・取消後の保存先を明記し、依存解決順の用語を日本語化。
- `phases-status.md`, `tasks-status.md`: 対応範囲と結果を短く記録。

## 検証

- 実行環境: Linux、bash、ローカル実行可能。SDK 10.0.404 と .NET 10 runtime。
- 対象検査: `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj -c Release -f net10.0 --no-restore --filter 'FullyQualifiedName~FileBasedCsWorkflowBuilderLifecycleTests|FullyQualifiedName~FileBasedCsWorkflowBuilderContractTests|FullyQualifiedName~CsFileInputPreflightTests|FullyQualifiedName~FileBasedWorkflowExecutorTests|FullyQualifiedName~FileBasedSdkRunnerCancellationTests'` — 66/66 成功。
- 書式: `dotnet format Devo6.WorkFlow.sln --verify-no-changes --no-restore` — 成功。
- 差分: `git diff --check` — 成功。
- 文書: 設計書、phase と task の3文書に対して textlint、CSpell、許可語検査がすべて成功。許可語一覧と依存は変更していない。固定 `package-lock.json` と同一 SHA の既存依存を一時リンクで使用し、実行後にリンクを除去した。

## 未確認事項

標準 .NET 8 runtime、Windows 実機、公開 NuGet、今回候補の自動検査と全件検査は未確認。手動の自動検査起動はしていない。変更は通常の再確認用であり、独立最終点検やマージを意味しない。

## 更新状態

この記録は最終差分確認前に作成し、対象変更と同じレビュー用コミットに含める。正確なコミット、push 後の参照、CI の状態は引き継ぎで示す。
