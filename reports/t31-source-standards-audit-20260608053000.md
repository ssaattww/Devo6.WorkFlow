# T31 source standards 監査

## 前提

- 監査対象は `src/**/*.cs` です。ただし `obj` 配下の生成物は除外しました。
- T31 の対象は public / non-public を問わない型、関数、property です。
- record primary constructor property は、対応する日本語の `param` 説明があるかを確認しました。
- 関数名に日本語を含む宣言は見つかりませんでした。

## 重大な違反カテゴリ

- XML コメントなし: 型、private helper、property に多数あります。
- XML コメントが英語: public API を中心に残っています。
- record primary constructor property の説明なし: `CliCommand` で確認しました。
- 自動検査不足: 現状の標準 dotnet 警告だけでは non-public と record primary constructor property を十分に検出できません。

## ファイル別違反

### `src/Devo6.WorkFlow.Abstractions/EngineArguments.cs`

- 3-21: 型 `EngineArguments` と property `EntryPath`、`ConfigPath`、`Settings` の XML コメントが英語です。

### `src/Devo6.WorkFlow.Abstractions/IAsyncStep.cs`

- 3-21: 型 `IAsyncStep<TOut>`、関数 `ExecuteAsync`、`typeparam`、`param`、`returns` の XML コメントが英語です。

### `src/Devo6.WorkFlow.Abstractions/IStep.cs`

- 3: 型 `IStep<TOut>` に XML コメントがありません。
- 5: 関数 `Execute` に XML コメントがありません。

### `src/Devo6.WorkFlow.Abstractions/StepContext.cs`

- 6: 型 `StepContext` に XML コメントがありません。
- 10, 15: constructor に XML コメントがありません。
- 20: property `Logger` に XML コメントがありません。
- 22, 27, 32, 37, 42, 47, 52, 62: 関数 `Get`、`Set`、`TryGet` に XML コメントがありません。

### `src/Devo6.WorkFlow.Abstractions/StepInput.cs`

- 3: 型 `StepInput` に XML コメントがありません。
- 7, 12: constructor に XML コメントがありません。
- 18: property `Context` に XML コメントがありません。
- 20, 25, 30, 35, 40, 45, 50, 60, 70: 関数 `Add`、`Get`、`TryGet` に XML コメントがありません。

### `src/Devo6.WorkFlow.Abstractions/StepValueKey.cs`

- 3: 型 `StepValueKey` に XML コメントがありません。
- 5: private constructor に XML コメントがありません。
- 11, 13: property `ValueType`、`Name` に XML コメントがありません。
- 15, 20, 25, 30, 35, 40, 57: 関数 `For`、`Equals`、`GetHashCode`、`ToString`、`ValidateName` に XML コメントがありません。
- 69, 74: operator `==`、`!=` に XML コメントがありません。

### `src/Devo6.WorkFlow.Abstractions/Unit.cs`

- 3: 型 `Unit` に XML コメントがありません。

### `src/Devo6.WorkFlow.Abstractions/ValidationError.cs`

- 3-21: 型 `ValidationError` と property `Path`、`Code`、`Message` の XML コメントが英語です。

### `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`

- 3-6: 型 `WorkflowErrorCodes` の XML コメントが英語です。
- 8-101: 既存の XML コメントの多くが英語です。対象は `EntryScriptNotFound`、`EntryStepNotFound`、`DuplicateStepName`、`ScriptCompileFailed`、`ScriptLoadFailed`、`ScriptLoadCycleDetected`、`ScriptReferenceNotAllowed`、`ScriptNugetRestoreFailed`、`ScriptApiIdentityMismatch`、`StepInputNotFound`、`StepInputTypeMismatch`、`ConfigNotFound`、`ConfigLoadFailed`、`StepExecutionFailed`、`StepTimeout`、`TraceSerializationFailed` です。

### `src/Devo6.WorkFlow.Abstractions/WorkflowResult.cs`

- 3-31: 型 `WorkflowResult` と property `EntryName`、`Succeeded`、`ErrorCode`、`ErrorMessage`、`Trace` の XML コメントが英語です。

### `src/Devo6.WorkFlow.Cli/Program.cs`

- 6-16: 型 `Program` と関数 `Main` の XML コメントが英語です。
- 72, 89, 161, 176: private helper `PrintValidationResult`、`TryParse`、`TryReadValue`、`ResolveConfigPath` に XML コメントがありません。
- 193-198: record `CliCommand` と primary constructor property `Name`、`EntryPath`、`EntryName`、`ConfigPath`、`Settings` に XML コメントがありません。

### `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`

- 22-52: 型 `CsxEntryLoader`、constructor、関数 `Execute` の XML コメントが英語です。
- 126-133: 関数 `Validate` の XML コメントが英語です。
- 1045, 1070, 1087, 1109, 1121: private helper `ValidateConfigPaths`、`ValidateApiAssemblyIdentity`、`RejectCopiedApiAssembly`、`TryReadDirective`、`MoveUsingDirectivesToTop` に XML コメントがありません。
- 1312, 1320, 1330, 1339: private helper `LooksLikeFileReference`、`IsFloatingNuGetVersion`、`IsInsideRoot`、`ResolvePathFinalTarget` に XML コメントがありません。
- 1442, 1457, 1469: private helper `GetExistingFileSystemInfo`、`Failure`、`ToValidationError` に XML コメントがありません。

### `src/Devo6.WorkFlow.Engine/StandardConfigLoader.cs`

- 10: 型 `StandardConfigLoader` に XML コメントがありません。

### `src/Devo6.WorkFlow.Engine/WorkflowValidationResult.cs`

- 5-29: 型 `WorkflowValidationResult`、`CsxValidationOptions` と property `Succeeded`、`Errors`、`ConfigPaths` の XML コメントが英語です。

## 違反なしと見た範囲

- `src/Devo6.WorkFlow.Abstractions/AssemblyInfo.cs`: 宣言対象なし。
- `src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`: 型、関数、property、record primary constructor property は日本語 XML コメントあり。
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`: 型、関数、property は日本語 XML コメントあり。
- `src/Devo6.WorkFlow.Engine/RetryOptions.cs`: 型、property は日本語 XML コメントあり。
- `src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`: 型、関数、property、record primary constructor property は日本語 XML コメントあり。

## 推奨修正分割

- Abstractions 1: `StepContext`、`StepInput`、`StepValueKey`、`IStep`、`Unit` の無注釈を追加します。
- Abstractions 2: `EngineArguments`、`IAsyncStep`、`ValidationError`、`WorkflowResult`、`WorkflowErrorCodes` の英語 XML コメントを日本語化します。
- Engine 1: `CsxEntryLoader` の public API 英語コメントと private helper 無注釈を修正します。
- Engine 2: `WorkflowValidationResult` の英語コメントと `StandardConfigLoader` 型コメントを修正します。
- Cli: `Program`、private helper、`CliCommand` record primary constructor property を修正します。

## 自動検査案

- 対象 file 確認:
  `rg --files src -g '*.cs' -g '!**/obj/**'`
- 関数名の日本語混入確認:
  `rg -n "^\\s*(public|internal|private|protected|static|async).*([ぁ-んァ-ヶ一-龠]).*\\(" src -g '*.cs' -g '!**/obj/**'`
- 英語 XML コメント候補:
  `rg -n "/// [A-Z][A-Za-z0-9 ,.;:'()/#<>._-]+$" src -g '*.cs' -g '!**/obj/**'`
- public API の最低限確認:
  `dotnet build Devo6.WorkFlow.sln /p:GenerateDocumentationFile=true /warnaserror:1591`
- 書式と analyzer 確認:
  `dotnet format Devo6.WorkFlow.sln --verify-no-changes`
- T31 完全検査:
  non-public 関数、operator、record primary constructor property まで見る Roslyn analyzer または補助 script を追加するのが必要です。

## 実行した検証

- `rg --files src -g '*.cs' -g '!**/obj/**'`: 対象 C# file を確認しました。
- 宣言と XML コメントの grep / 補助 script: 上記違反一覧を作成しました。
- `npm run lint:md`: 成功しました。repo 設定上、対象は `AGENTS.md`、`doc/workflow_engine_spec.md`、`phases-status.md`、`README.md`、`tasks-status.md`、`tools/lint/README.md` で、`reports/` は対象外でした。
- `npm run lint:md:terms`: 成功しました。`SudachiPy term variants: none` です。
- 報告書単体 textlint: `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t31-source-standards-audit-20260608053000.md` は成功しました。
- 報告書単体 spell check: repo 設定の `ignorePaths` により `reports/t31-source-standards-audit-20260608053000.md` は skip されました。
