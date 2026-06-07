# T38 複数フォルダ CompositeStep 例レビュー

## 対象

- `samples/multi-folder-composite/`
- `tests/Devo6.WorkFlow.Tests/SampleWorkflowTests.cs`
- `README.md`
- `doc/workflow_engine_spec.md`
- `tasks-status.md`
- `phases-status.md`
- `.gitignore`

## 観点

- `main.csx` が異なるフォルダの Step を `#load` していること。
- 外側 `CompositeStep` の Step が、同じ `StepInput` と `StepContext` で内側 `CompositeStep` を実行していること。
- Step ごとの Config 型と境界 Config 型の対応が読み取れること。
- Step ごとの既定 Config YAML が Step フォルダに置かれていること。
- 設計書で Step 側既定 Config YAML と root Config の結合規則が説明されていること。
- README が利用者向けの実行方法だけを説明し、公開運用の内部説明を含まないこと。
- サンプルの出力ファイルが作業ツリーに残らないこと。

## 指摘

なし。

## 確認結果

- `dotnet test Devo6.WorkFlow.sln --filter MultiFolderCompositeSampleRuns`
- `dotnet test Devo6.WorkFlow.sln --filter MultiFolderCompositeSampleRootConfigContainsOnlyOverrides`
- `dotnet test Devo6.WorkFlow.sln --filter MultiFolderCompositeSampleUsesNestedCompositeStep`
- `dotnet test Devo6.WorkFlow.sln --filter MultiFolderCompositeSampleMergedYamlFragmentsCanBeOverridden`
- `dotnet run --project src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj -- validate samples/multi-folder-composite/main.csx --config appsettings.yaml`
- `dotnet run --project src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj -- run samples/multi-folder-composite/main.csx --config appsettings.yaml`
- `dotnet test Devo6.WorkFlow.sln`
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`
- `reports/t38-config-fragment-merge-review-20260607184013.md` による sub-agent 差分レビュー

## 残るリスク

- サンプルはローカルファイル入出力だけを扱う。NuGet 読み込みを含む分割例は別の例として扱う。
