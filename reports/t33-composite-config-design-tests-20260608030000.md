# T33 Composite Config 設計検査報告

## 目的

T33 の契約を、Step 内 Config 型と CompositeStep 境界 Config 型を使う形へ設計書と検査で先行固定した。

product code は編集していない。

## 変更ファイル

- `doc/workflow_engine_spec.md`
- `tests/Devo6.WorkFlow.Tests/StandardConfigLoadingContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`
- `reports/t33-composite-config-design-tests-20260608030000.md`

## 設計更新

- 各 Step が `LoadStep.Config`、`ConvertStep.Config`、`SaveStep.Config` のように自分の Config 型を内包する契約へ差し替えた。
- CompositeStep 境界 Config 型として `MainConfig` を使い、`MainConfig.Load`、`MainConfig.Convert`、`MainConfig.Save` が各 Step Config を保持する契約へ差し替えた。
- `.WithConfig<MainConfig>()` は Step 登録単位 Config がある場合に境界 Config 型宣言として扱う、と明記した。
- `.WithConfig<LoadStep.Config>("Load")` は境界 Config 型上の `Load` プロパティ path を、対象 Step 実行直前に `StepContext.Set<LoadStep.Config>()` へ登録する宣言とした。
- `--set Convert.ToUpper=false` は境界 Config 型へのプロパティ path override とした。
- `validate` は Config path の存在確認までで、境界 Config 型変換、override 適用、値検証を行わない契約を維持した。
- Step 登録単位 Config がない場合の旧 `.WithConfig<TConfig>()` だけの Entry 全体 Config 互換 API は維持した。

## 検査更新

- `CliRunWithBoundaryConfigLoadsEachDeclaredStepConfig`
  - E2E `.csx` 例を `LoadStep.Config`、`ConvertStep.Config`、`SaveStep.Config` と `MainConfig` へ差し替えた。
  - `--set Convert.ToUpper=false` と `--set Save.Path=cli.txt` が境界 Config 型上の path override として扱われ、対象 Step の `StepContext` から取得されることを検査する。
- `StepConfigRegistrationWithoutBoundaryConfigFailsBeforeFirstStepExecution`
  - Step 登録単位 Config があるのに境界 Config 宣言がない場合、最初の Step 実行前に `CONFIG_LOAD_FAILED` になる検査を追加した。
  - 現状 product code では失敗することを期待する検査である。
- `CompositeStepExposesBoundaryConfigAndStepConfigRegistrationMetadata`
  - `CompositeStep.ConfigType` が境界 Config 型を示し、`StepConfigRegistration.ConfigType` が Step 内 Config 型を示す関係が分かる検査へ調整した。

## 実行結果

- `dotnet test Devo6.WorkFlow.sln --filter StandardConfigLoadingContractTests`
  - 終了コード 1。
  - 24 件中 23 件成功、1 件失敗。
  - 失敗した検査は `StepConfigRegistrationWithoutBoundaryConfigFailsBeforeFirstStepExecution`。
  - 失敗理由は、境界 Config 宣言がない `.csx` でも現状実装が終了コード 0 で成功するため。T33 の新契約では `CONFIG_LOAD_FAILED` が期待値である。
- `dotnet test Devo6.WorkFlow.sln --filter CompositeStepTests`
  - 終了コード 0。
  - 11 件成功。
- `npm run lint:md`
  - 終了コード 123。
  - `doc/workflow_engine_spec.md` の指摘は修正済み。
  - 残る指摘は編集許可外の `tasks-status.md` T33 行にある `mapping` と `property` の cspell 指摘。
- `npm run lint:md:terms`
  - 終了コード 0。
  - `SudachiPy term variants: none`。
- `git diff --check`
  - 終了コード 0。
- focused Markdown lint
  - `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" doc/workflow_engine_spec.md reports/t33-composite-config-design-tests-20260608030000.md`: 終了コード 0。
  - `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js doc/workflow_engine_spec.md reports/t33-composite-config-design-tests-20260608030000.md`: 終了コード 0。報告書は `ignorePaths` により除外され、設計書のみ検査された。

## 残リスク

- product code は未編集のため、境界 Config 宣言必須の新契約はまだ実装されていない。
- `npm run lint:md` は編集許可外の `tasks-status.md` に残る既存語で失敗する。
- 現 API では `WithConfig<TConfig>()` は `CompositeStep<TOut>` 上の API であり、検査と設計例は最初の `Run` 後に境界 Config 型を宣言する形に合わせている。
