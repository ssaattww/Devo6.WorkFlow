# T23 標準 Config 実装レポート

## タスク

T23「標準 Config 読み込みと `StepContext` 格納」の実装。

## TDD 結果

実装前の赤は `reports/t23-standard-config-failing-tests-20260607043500.md` の契約テストで確認済み。
実装後は `StandardConfigLoadingContractTests` 8 件が成功した。

## 変更内容

- `CompositeStep<TOut>.WithConfig<TConfig>()` を追加し、Entry の標準 Config 型 metadata を設定できるようにした。
- `CompositeStep<TOut>.ConfigType` を public property として公開し、未指定時は null を返すようにした。
- `CsxEntryLoader.Execute` で Entry `.csx` ロード後に `ConfigType` と `EngineArguments.ConfigPath` を確認し、Step 実行前に標準 Config を読み込むようにした。
- `WorkflowExecutionOptions` に engine 内部向けの標準 Config instance 受け渡しを追加し、`CompositeStep.ExecuteWorkflowAsync` で `StepContext.Set<TConfig>(config)` と同等になるよう登録した。
- `StandardConfigLoader` を追加し、YAML の型付き変換と DataAnnotations / `IValidatableObject` 検証を行うようにした。
- `Devo6.WorkFlow.Engine.csproj` に `YamlDotNet` を追加した。

## 設計との対応

- Entry 側の宣言は `WithConfig<TConfig>()` の metadata のみで行い、Step 専用引数は追加していない。
- Config 未指定または存在しない Config file は `CONFIG_NOT_FOUND` として Step 実行前に失敗する。
- 読み込み不能、YAML 構文エラー、型変換失敗、DataAnnotations / `IValidatableObject` 失敗は `CONFIG_LOAD_FAILED` として Step 実行前に失敗する。
- `CONFIG_LOAD_FAILED` の場合は workflow 実行に進まないため、標準 Config は `StepContext` に登録されない。
- `validate` は既存どおり Config path 存在確認までに留め、型変換 validate は実装していない。

## DataAnnotations 参照

`.csx` の `#r "System.ComponentModel.Annotations"` を許可するため、`CsxEntryLoaderOptions.AllowedAssemblyReferences` の既定値に `System.ComponentModel.Annotations` を追加した。
file reference や任意 NuGet 参照は広げず、DataAnnotations 検証に必要な framework assembly name のみを安全に許可した。

## `--set` 境界

T23 では `--set` を標準 Config に反映していない。
既存どおり `EngineArguments.Settings` に文字列辞書として保持し、Step から取得できることを契約テストで確認した。

## 検証結果

- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter FullyQualifiedName~StandardConfigLoadingContractTests`: 成功。8 件成功。
- `dotnet test Devo6.WorkFlow.sln`: 成功。86 件成功。
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t23-standard-config-implementation-20260607045500.md`: 成功。
- `npm run lint:md`: 成功。
- `npm run lint:md:terms`: 成功。SudachiPy term variants は none。
- `git diff --check`: 成功。

## 残リスク

- 標準 Config は T23 設計どおり単一型のみ対応している。複数 Config、名前付き Config、`--set` の型付き反映は後続 task の対象。
- `validate` では型変換と DataAnnotations 検証を行わないため、Config 内容の不備は `run` 時に検出される。
