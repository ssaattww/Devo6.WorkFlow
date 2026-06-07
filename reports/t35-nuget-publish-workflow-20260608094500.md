# T35 NuGet 自動公開レポート

## 目的

NuGet.org へ CLI ツールを自動公開し、公開後に `dotnet tool install` で導入できる状態にする。

## 変更内容

- `.github/workflows/publish-nuget.yml` を追加した。
- 公開対象を `src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj` に限定した。
- GitHub の公開版登録、`master` への反映、手動実行で公開できるようにした。
- 手動実行時は `package_version` で公開版を上書きできるようにした。
- 公開前に公開対象プロジェクトの restore、build、`dotnet pack`、一時配置先への `dotnet tool install`、`engine` 起動確認を行うようにした。
- `NUGET_API_KEY` が未設定の場合は、公開前に明示的に失敗するようにした。
- `README.md` に NuGet.org からの導入手順と必要な秘密情報を追記した。

## 検査方針

- GitHub の実行定義は、単体テストへ文字列検査を追加しない。
- ローカルでは同じ `dotnet pack` と `dotnet tool install` 経路を実行して導入可能性を確認する。
- GitHub 固有の実行可否は、実際の GitHub 実行結果で最終確認する。

## 検証

- `ruby -e "require 'yaml'; YAML.load_file('.github/workflows/publish-nuget.yml'); puts 'yaml ok'"`
- `dotnet test Devo6.WorkFlow.sln`
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`
- `dotnet restore src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj`
- `dotnet build src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj --configuration Release --no-restore`
- `dotnet pack src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj -c Release -o ./artifacts/packages`
- `dotnet pack src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj --configuration Release --no-build --output ./artifacts/workflow-check-fast -p:ContinuousIntegrationBuild=true -p:PackageVersion=0.1.0-ci.1000`
- `dotnet tool install --tool-path "$toolpath" Devo6.WorkFlow.Cli --add-source ./artifacts/packages --version 0.1.0`
- `dotnet tool install --tool-path "$toolpath" Devo6.WorkFlow.Cli --add-source ./artifacts/workflow-check-fast --version 0.1.0-ci.1000`
- `"$toolpath/engine"`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`

## 補足

- `actionlint` はローカル環境に無かったため未実行。
- NuGet.org への実 push は `NUGET_API_KEY` を設定した GitHub 実行時に行われる。
- 公開前版番号でも `dotnet tool install --version` による導入は通った。
- 公開 workflow から `dotnet test` は外した。テストは PR や通常 CI の責務とし、公開ジョブは SSC と同じく作成と公開に寄せる。
