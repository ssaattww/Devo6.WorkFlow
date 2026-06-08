# T46 第三者 NuGet ネストサンプル実装報告

## 対象

- T46: 複数フォルダ CompositeStep サンプルを第三者 NuGet 利用と意味のあるネスト構成へ更新する。

## 実装内容

- `samples/multi-folder-composite/main.csx` の入口で `Devo6.WorkFlow.Engine` 0.1.0 と `YamlDotNet` 16.3.0 を NuGet 参照する構成へ更新した。
- Step ファイル側には NuGet 参照を置かず、入口だけで参照する形を維持した。
- 入力 `input/source.txt` を YAML 前付け付き文書へ変更した。
- 内側 `TextPipeline` を `LoadTextStep`、`ParseDocumentStep`、`NormalizeTextStep`、`AnalyzeTextStep`、`BuildReportStep` の文書処理パイプラインへ変更した。
- 外側 `Main` は内側の出力文字列を `SaveTextStep` に渡して保存する構成へ変更した。
- Step ごとの既定 Config YAML と root `appsettings.yaml` の部分上書き、CLI `--set` を維持した。
- サンプル説明を `README.md` と `doc/workflow_engine_spec.md` に反映した。

## 検査

- 失敗先行: `dotnet test Devo6.WorkFlow.sln --filter MultiFolderCompositeSample`
  - 旧サンプルに対して、期待する第三者 NuGet 参照、YAML 前付け処理、内外 CompositeStep の責務分離、CLI override が満たされず失敗することを確認した。
- 実装後: `dotnet test Devo6.WorkFlow.sln --filter MultiFolderCompositeSample`
  - 成功。5 件成功。
- `dotnet test Devo6.WorkFlow.sln --filter CodingStandards`
  - 成功。3 件成功。
- `dotnet run --project src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj -- validate samples/multi-folder-composite/main.csx --config appsettings.yaml --allow-nuget Devo6.WorkFlow.Engine,0.1.0 --allow-nuget YamlDotNet,16.3.0`
  - 成功。
- `dotnet run --project src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj -- run samples/multi-folder-composite/main.csx --config appsettings.yaml --allow-nuget Devo6.WorkFlow.Engine,0.1.0 --allow-nuget YamlDotNet,16.3.0`
  - 成功。
- `npm run lint:md`
  - 成功。
- `npm run lint:md:terms`
  - 成功。
- `git diff --check`
  - 成功。

## 自己点検

- lock file は追加していない。
- `main.csx` 以外の Step ファイルに `#r "nuget:` が無いことを検査で確認した。
- `CreateSampleLoader` の固定 provider は `YamlDotNet` を直接参照として扱い、`typeof(YamlDotNet.RepresentationModel.YamlStream).Assembly.Location` を参照 path に含めた。
- Markdown lint 用語は repo の lint 結果に合わせて修正した。

## 残り

- 実装担当としての自己点検は完了。親マネージャー側の正式レビューが必要な場合は、この報告と差分を対象に実施する。
