# Sub-agent実行レポート

## タスク

- 目的: T51 のうち、`EngineArguments` の旧 `ConfigPath` / `Settings` 互換プロパティを削除し、参照を workflow 系の新名へそろえる。
- タスク種別: 実装

## sub-agentを使う理由

- 理由: ユーザー指示により、実装 sub-agent のみ `gpt-5.3-codex-spark high` を使って実装作業を委譲するため。

## 対象範囲

- 対象: `src/Devo6.WorkFlow.Abstractions/EngineArguments.cs`、`src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`、旧名参照が残るテストの最小修正。

## 対象外

- 対象外: CLI 引数の追加実装、engine config 読み込み、ログ出力機構、サンプル README、コミット、push、PR作成。

## 実行コマンド

- 実行コマンド:
  - `rg -n "\.ConfigPath\b|\.Settings\b|ConfigPath =|Settings =" src tests`
  - `rg -n "\\bConfigPath\\b|\\bSettings\\b" src tests`
  - `dotnet test Devo6.WorkFlow.sln --filter "Config"`
  - `git diff --check -- src/Devo6.WorkFlow.Abstractions/EngineArguments.cs src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs tests/Devo6.WorkFlow.Tests`

## 対象ファイル

- 変更: `src/Devo6.WorkFlow.Abstractions/EngineArguments.cs`
- 変更: `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- 変更: `tests/Devo6.WorkFlow.Tests/SampleWorkflowTests.cs`
- 変更: `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
- 確認: `reports/t51-remove-legacy-engine-arguments-20260609080118.md`

## 指摘事項

- 指摘なし。Program.cs / 設計の読み込み系 / ログ系 / timeout/retry 周り、README、samples、tasks/phases 系は未変更。

## 結果

- `EngineArguments` から `ConfigPath` と `Settings` の互換プロパティを削除し、`WorkflowConfigPath` / `WorkflowSettings` の新名のみ使用するよう変更。
- `CsxEntryLoader` の旧参照を 3 箇所置換。
- テスト側の旧名初期化 3 箇所（`ConfigPath`/`Settings =`）を新名へ置換。
- 検索結果:
  - `rg -n "\.ConfigPath\\b|\\.Settings\\b|ConfigPath =|Settings =" src tests` では新名由来の出力を除外して確認可能。
  - `rg -n "\\bConfigPath\\b|\\bSettings\\b" src tests` は `NuGet.Configuration.Settings` 由来の `settings` のみ。
- `dotnet test Devo6.WorkFlow.sln --filter "Config"`: 42件すべて成功。
- `git diff --check ...` は差分問題なし。

## リスク

- 既存の外部利用者コードで `EngineArguments.ConfigPath` / `Settings` を直接参照している場合は、API 変更によりコンパイル破壊の可能性が残る。
