# 課題 #27 / PR #28 通常確認の追加修正

## 対象

- ブランチ: `feat/issue-27-file-based-csharp`
- 作業開始時 HEAD: `53af6b36c6e173a0ed96e51189b5f67e48335085`
- 対象: SDK 実工程の同一性、公開 `Build` の取消境界、fixture のゲート期限。
- merge、手動 CI、権限変更は行わない。

## 修正

1. restore/build の MSBuild 工程内で `NETCoreSdkVersion`、`MSBuildSDKsPath`、`MSBuildExtensionsPath` を専用ファイルへ書き出し、初回に選択した SDK と照合する。外側の `--version` / `--info` probe が一致しても、工程の記録だけが異なる場合に `SCRIPT_BUILD_ENVIRONMENT_UNSUPPORTED` で停止する。
2. 公開 `Build` は SDK process 中の協調取消を `OperationCanceledException` として伝播する。出力排他の取得前または待機中の取消は従来の `SCRIPT_BUILD_FAILED` 結果を返す。
3. Provider constructor / `Create()` のテスト用ゲートループに期限を設け、親側の期限・ゲート解放・実行完了回収後の一時領域削除を保つ。

## 検証

- SDK 10.0.404 の一時 `.cs` fixture で `CustomAfterMicrosoftCommonTargets` が restore と build の実行中に版・配置パスを記録することを確認。
- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --no-restore -f net10.0 --filter 'FullyQualifiedName~FileBasedCsWorkflowBuilderLifecycleTests|FullyQualifiedName~FileBasedSdkRunnerCancellationTests|FullyQualifiedName~CsFileInputPreflightTests|FullyQualifiedName~CSharpDeclarationsFollowT31CodingStandards'` により50件成功。restore/build の版差・配置先差各2ケース、排他取得前/待機中の取消、既存 SDK キャンセル2件を含む。
- T31 チェッカーの起動に限り `DOTNET_ROLL_FORWARD=Major` を指定。これは標準 .NET 8 runtime による実行ではない。
- `dotnet build tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --no-restore -f net8.0 --verbosity minimal`: 成功、警告0、エラー0。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes --no-restore` と `git diff --check`: 成功。
- Markdown lint は `node_modules`、`textlint`、`cspell` が利用できず未実施。追加導入はしていない。

## 未確認

- 標準 .NET 8 runtime でのテスト実行、Windows 実機、全件ローカル試験は未実施。
- 自動 CI は新しい HEAD に対して push 後に確認する。手動起動はしない。
