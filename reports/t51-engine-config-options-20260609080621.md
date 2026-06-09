# Sub-agent実行レポート

## タスク

- 目的: T51 のうち、engine config の Timeout と Retry を読み込み、`WorkflowExecutionOptions` へ反映する。
- タスク種別: 実装

## sub-agentを使う理由

- 理由: ユーザー指示により、実装 sub-agent のみ `gpt-5.3-codex-spark high` を使って実装作業を委譲するため。

## 対象範囲

- 対象: `src/Devo6.WorkFlow.Cli/Program.cs`、必要最小の engine config 読み込み型または helper、T51 に必要な CLI 実行テスト。

## 対象外

- 対象外: ログ出力機構、ログファイル作成、サンプル、README、コミット、push、PR作成。

## 実行コマンド

- `dotnet test Devo6.WorkFlow.sln --filter "EngineConfig|CliRunValidate|Timeout|Retry"`
- `git diff --check -- src/Devo6.WorkFlow.Cli/Program.cs tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`

## 対象ファイル

- `src/Devo6.WorkFlow.Cli/Program.cs`
- `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`
- `reports/t51-engine-config-options-20260609080621.md`

## 指摘事項

- 指摘なし。

## 結果

- `Program` の run 処理で、既定 engine config (`config/engine.defaults.yaml`) を必ず読み込み、`--engine-config` 指定時は既定を上書きする読み込み順に変更しました。
- `Timeout.StepTimeout` は `TimeSpan?`、`Retry.MaxAttempts` は `RetryOptions.MaxAttempts` として `WorkflowExecutionOptions` へ反映します。
- `--engine-set` / `--eset` は `Timeout.StepTimeout` と `Retry.MaxAttempts` のみ適用し、既存 `EngineArguments` の値は維持されるため、実行時の StepContext からも参照可能です。
- 失敗時は `ConfigNotFound` / `ConfigLoadFailed` を使い、標準エラーへ表示して run を失敗終了とします。
- T51d 向けテストを追加:
  - `EngineRunUsesTimeoutFromEngineSet`
  - `EngineRunUsesRetryFromEngineConfig`
  - `EngineRunOverwritesEngineConfigRetryWithEngineSet`

## リスク

- `--engine-set` / `--eset` で `Timeout.` または `Retry.` 配下の未対応パスを指定した場合のみ run 失敗とする実装だが、未対応のその他パスは従来通り無視される仕様のため、将来の要件で厳密拒否に変更する場合は追加対応が必要です。
