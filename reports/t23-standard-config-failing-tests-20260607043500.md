# T23 標準 Config 失敗検査レポート

## 目的

T23「標準 Config 読み込みと `StepContext` 格納」の実装前に、利用者目線の CLI `run` E2E と公開 API 契約を赤い検査として追加した。

## 追加した検査

- `StandardConfigLoadingContractTests.CliRunWithConfigLoadsYamlIntoStepContext`
  - `.csx` 内で `AppConfig` と `CompositeStep.Define("Main").WithConfig<AppConfig>()` を定義する。
  - `engine run main.csx --config appsettings.yaml` で `input.Context.Get<AppConfig>()` から YAML 値を読み、marker file に `configured|5071` を書けることを期待する。
- `StandardConfigLoadingContractTests.RelativeConfigPathIsResolvedFromEntryDirectory`
  - `--config config/appsettings.yaml` が Entry `.csx` directory 基準で解決され、CLI process の cwd にある同名 path に影響されないことを期待する。
- `StandardConfigLoadingContractTests.CompositeStepExposesWithConfigAndConfigTypeMetadata`
  - `CompositeStep<TOut>` が public `WithConfig<TConfig>()` と public `ConfigType` metadata を持つことを reflection で期待する。
- `StandardConfigLoadingContractTests.MissingConfigArgumentFailsBeforeStepExecutionWithConfigNotFound`
  - Entry が `WithConfig<TConfig>()` を使い、`--config` 未指定なら Step 実行前に `CONFIG_NOT_FOUND` で失敗することを期待する。
- `StandardConfigLoadingContractTests.MissingConfigFileFailsCliRunWithConfigNotFound`
  - 存在しない config file が CLI `run` で非 0 かつ `CONFIG_NOT_FOUND` になることを期待する。
- `StandardConfigLoadingContractTests.InvalidYamlTypeConversionFailsCliRunWithConfigLoadFailed`
  - YAML 値を Config 型へ変換できない場合に CLI `run` が非 0 かつ `CONFIG_LOAD_FAILED` になることを期待する。
- `StandardConfigLoadingContractTests.DataAnnotationsValidationFailureFailsCliRunWithConfigLoadFailed`
  - DataAnnotations 検証失敗が CLI `run` で非 0 かつ `CONFIG_LOAD_FAILED` になることを期待する。
- `StandardConfigLoadingContractTests.SetArgumentsAreNotAppliedToStandardConfigDuringT23`
  - T23 では `--set Title=cli-value` が標準 Config に反映されず、`EngineArguments.Settings` には保持されることを期待する。

## 期待する失敗

実装前の赤として、少なくとも以下が失敗することを期待した。

- `WithConfig<TConfig>()` が public API として存在しない。
- `ConfigType` metadata が public API として存在しない。
- `.csx` から `WithConfig<AppConfig>()` を呼べず、CLI `run` が標準 Config 読み込み前に失敗する。
- Config 未指定、存在しない file、型変換失敗、検証失敗の error code がまだ T23 設計どおりに分類されない。

## 実際の失敗

実行コマンド:

```bash
dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter FullyQualifiedName~StandardConfigLoadingContractTests
```

結果:

- 8 件すべて失敗した。
- public API 検査は `Assert.NotNull()` で失敗した。`CompositeStep<string>` に public `WithConfig` が存在しない。
- CLI E2E と error 分類検査の多くは `SCRIPT_COMPILE_FAILED` になった。`.csx` 内の `CompositeStep<string>` に `WithConfig` が存在しないためである。
- DataAnnotations 検査は `SCRIPT_REFERENCE_NOT_ALLOWED: Assembly reference is not allowed: System.ComponentModel.Annotations` で失敗した。現行 loader では DataAnnotations 用 assembly reference が許可されていないため、Config validation まで到達していない。

## 実装時の注意

- 最初に `CompositeStep<TOut>.WithConfig<TConfig>()` と `ConfigType` metadata を public API として実装すると、CLI E2E の失敗理由が Config 読み込み本体へ進む。
- `WithConfig<TConfig>()` は Entry metadata を設定するだけにし、Step 専用引数を増やさない。
- CLI `run` は Entry `.csx` ロード後、Entry metadata と `EngineArguments.ConfigPath` から YAML を型付き Config に変換し、最初の Step 実行前に `StepContext.Set<TConfig>(config)` で登録する。
- `--config` 未指定かつ Config 型要求あり、または存在しない config file は `CONFIG_NOT_FOUND` に分類する。
- YAML 構文、型変換、DataAnnotations または `IValidatableObject` 検証失敗は `CONFIG_LOAD_FAILED` に分類する。
- DataAnnotations を `.csx` 内の Config 型に書く契約を満たすには、`System.ComponentModel.Annotations` の参照許可または loader の既定参照追加が必要になる可能性がある。
- T23 では `--set` を標準 Config へ反映しない。`EngineArguments.Settings` への保持のみを維持する。

## 検証結果

- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter FullyQualifiedName~StandardConfigLoadingContractTests`
  - 失敗。8 件失敗。T23 実装前の赤を確認した。
- `npm run lint:md`
  - 成功。
- `npm run lint:md:terms`
  - 成功。`SudachiPy term variants: none`。
- `git diff --check`
  - 成功。
- 新規 report focused textlint
  - 成功。`npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t23-standard-config-failing-tests-20260607043500.md` を実行した。

## ブロッカー

実装禁止のため、T23 実装は行っていない。テスト側の追加は完了しており、実装担当は上記の public API 未実装と DataAnnotations 参照許可を順に解消する必要がある。
