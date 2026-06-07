# T33 Composite Config product code 実装報告

## 目的

T33 の新契約である Step 内 Config 型と CompositeStep 境界 Config 型を product code に実装し、設計検査で追加された赤い検査を通した。

## 変更ファイル

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `src/Devo6.WorkFlow.Engine/StandardConfigLoader.cs`
- `reports/t33-composite-config-implementation-20260608033000.md`

既存の未コミット差分として、`CompositeStep.cs`、`WorkflowExecutionOptions.cs`、設計書、検査 file、進捗 file の差分が残っている。これらは戻していない。

## 実装内容

- Step 登録単位 Config があるのに CompositeStep 境界 Config 型がない場合、Config file の有無を見る前に `CONFIG_LOAD_FAILED` を返すようにした。
- Step 登録単位 Config がある場合、旧 Entry 全体 Config として境界 Config を `StepContext` に常時登録しないようにした。
- `StandardConfigLoader.LoadStepConfigs` が CompositeStep 境界 Config 型を受け取り、YAML 全体を境界 Config 型へ変換するようにした。
- raw `--set` は宣言済み section path 接頭辞に一致することだけを先に検査し、接頭辞を剥がさず境界 Config 型へそのまま適用するようにした。
- 宣言済み section path が YAML に存在しない場合は、境界 Config 型変換へ進む前に失敗するようにした。
- 境界 Config 型への変換、override 適用、境界 Config 検証後に、各 section path の public property をたどって Step Config 値を抽出するようにした。
- 抽出値が宣言された Step Config 型へ代入できることを検査し、Step Config 値も DataAnnotations で検証するようにした。
- Step 登録単位 Config がない旧 `.WithConfig<TConfig>()` の Entry 全体 Config 互換経路は、従来どおり `StandardConfigLoader.Load` と `WithStandardConfig` を使う形で維持した。

## 検証結果

- `dotnet test Devo6.WorkFlow.sln --filter StandardConfigLoadingContractTests`
  - 終了コード 0。
  - 24 件成功。
- `dotnet test Devo6.WorkFlow.sln --filter CompositeStepTests`
  - 終了コード 0。
  - 11 件成功。
- `dotnet test Devo6.WorkFlow.sln`
  - 終了コード 0。
  - 167 件成功。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`
  - 終了コード 0。
- `npm run lint:md`
  - サブエージェント実行時は終了コード 1。
  - 失敗理由は `tasks-status.md:38` にある未許可語である。
  - 親側で `tasks-status.md` の表記を修正後、再実行して終了コード 0。
- `npm run lint:md:terms`
  - 終了コード 0。
  - `SudachiPy term variants: none`。
- `git diff --check`
  - 終了コード 0。

## 残リスク

- サブエージェント実行時の `npm run lint:md` 指摘は親側の進捗 file 表記修正で解消している。
- `LoadSection` は旧実装の補助 API として残っているが、Step 登録単位 Config の実行経路では使っていない。
