# Sub-agent実行レポート

## タスク

- 目的: T51 のうち、engine config の Logging 設定を CLI 実行時のログ出力へ反映する。
- タスク種別: 実装

## sub-agentを使う理由

- 理由: ユーザー指示により、実装 sub-agent の作業として T51 の残実装を委譲するため。

## 対象範囲

- 対象: `src/Devo6.WorkFlow.Cli/Program.cs`、必要最小の CLI ログ helper、`src/Devo6.WorkFlow.Cli/config/engine.defaults.yaml`、T51 に必要な CLI 実行テスト。

## 対象外

- 対象外: T52 のサンプル更新、サンプル README、README、tasks-status.md、phases-status.md、コミット、push、PR作成。

## 実行コマンド

- `dotnet test Devo6.WorkFlow.sln --filter "EngineConfig|CliRunValidate"`  
  実行結果: `Passed!  - Failed:     0, Passed:    36, Skipped:     0, Total:    36, Duration: 32 s`
- `git diff --check -- src/Devo6.WorkFlow.Cli tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs reports/t51-engine-config-logging-20260609081741.md`
- 実行結果: 変更差分の末尾空白差分なし（エラーなし）

## 対象ファイル

- `src/Devo6.WorkFlow.Cli/config/engine.defaults.yaml`
- `src/Devo6.WorkFlow.Cli/Program.cs`
- `src/Devo6.WorkFlow.Cli/EngineLoggingProvider.cs`（新規）
- `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`

## 指摘事項

- 指摘なし

## 結果

- `--engine.defaults.yaml` に `Logging.Console` / `Logging.File` の基本構造を追加。
- `Program.cs` で `--engine-config` 解決・読み込み順を維持しつつ、`engine config` から `Timeout`/`Retry` のみならず `Logging` を反映。
- CLI 実行時 `WorkflowExecutionOptions.LoggerFactory` に `EngineLoggingProvider` を注入してコンソール/ファイルのログ出力を有効化。
- ログは `CompositeStep` の `EntryName` スコープから `RootStepName` を解決し、`File.NameFormat` の `{Timestamp:...}` / `{RootStepName}` を置換して出力。
- `CliRunValidateTests` にログ出力系のテストを 5 件追加（YAML/engine-set でのファイル有効化、コンソール有効化、未対応 `Logging.File.Format`、未対応 `Logging` path）。

## リスク

- `Logging` セクションの未知キーは YAML ノード検証で検知し `CONFIG_LOAD_FAILED` で失敗するため、現在大きな制約は見当たらない。
