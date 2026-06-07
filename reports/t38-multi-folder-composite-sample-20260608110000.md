# T38 複数フォルダ CompositeStep 例レポート

## 目的

複数の Step が異なるフォルダにある場合に、Entry の `main.csx` から `#load` して 1 つの `CompositeStep` として実行する例を追加する。

## 変更内容

- `samples/multi-folder-composite/main.csx` を追加した。
- `steps/load/`、`steps/convert/`、`steps/save/` に Step を分けて配置した。
- `steps/load/`、`steps/convert/`、`steps/save/` に Step ごとの YAML 断片を分けて配置した。
- `shared/contracts.csx` に Step 間で受け渡す型を配置した。
- `appsettings.yaml` と `input/source.txt` を追加した。
- `README.md` に例の配置と実行コマンドを追記した。
- `doc/workflow_engine_spec.md` に Step 側 YAML 断片と root Config の結合規則を追記した。
- 例が壊れないように `SampleWorkflowTests` を追加した。

## 検証

- `dotnet test Devo6.WorkFlow.sln --filter MultiFolderCompositeSampleRuns`
- `dotnet test Devo6.WorkFlow.sln --filter MultiFolderCompositeSampleRuntimeConfigReferencesYamlFragments`
- `dotnet test Devo6.WorkFlow.sln --filter MultiFolderCompositeSampleMergedYamlFragmentsCanBeOverridden`
- `dotnet run --project src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj -- validate samples/multi-folder-composite/main.csx --config appsettings.yaml`
- `dotnet run --project src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj -- run samples/multi-folder-composite/main.csx --config appsettings.yaml`
- `test "$(cat samples/multi-folder-composite/output/result.txt)" = "converted: HELLO FROM MULTI FOLDER COMPOSITE"`
- `dotnet test Devo6.WorkFlow.sln`
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`
- `npm run lint:md`
- `npm run lint:md:terms`

## 結果

- `samples/multi-folder-composite/output/result.txt` に変換済み文字列を書き出せることを確認した。
- 実行用 Config が Step ごとの YAML 断片を参照し、実行前に結合されることを確認した。
- 設計書上も、root Config から明示参照した Step 側 YAML 断片を結合してから境界 Config 型へ変換することを明記した。
