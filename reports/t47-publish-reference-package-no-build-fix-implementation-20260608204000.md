# T47 publish workflow 参照用パッケージ作成失敗修正 実装報告

## 目的

master への push 後に失敗した `Publish NuGet Package` workflow を修正する。

失敗箇所は `Pack reference package` step であり、`dotnet pack` に `--no-build` を指定した状態で `Devo6.WorkFlow.Abstractions.csproj` の Build target が起動し、`NETSDK1085` で失敗していた。

## 原因

`Devo6.WorkFlow.Engine` は参照用パッケージに `Devo6.WorkFlow.Abstractions.dll` を同梱するため、pack 時に project reference の出力を収集する。

publish workflow は `dotnet test` で Release build 済みの成果物を使うため `dotnet pack --no-build` を指定していたが、参照プロジェクトの build が起動されると `NoBuild=true` と衝突する。

## 実装

- `.github/workflows/publish-nuget.yml` の `Pack reference package` step に `-p:BuildProjectReferences=false` を追加した。
- CLI tool 側の pack step は変更していない。
- `ProjectSkeletonTests` に publish workflow の回帰検査を追加し、参照用パッケージ step が `--no-build` と `BuildProjectReferences=false` を併用することを固定した。

## TDD

先に `PublishWorkflowDisablesProjectReferenceBuildForReferencePackage` を追加し、修正前の workflow で失敗することを確認した。

その後 workflow を修正し、同じ検査が成功することを確認した。

## 検証

- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter PublishWorkflowDisablesProjectReferenceBuildForReferencePackage`: 成功。
- `dotnet pack src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj --configuration Release --no-build --output /tmp/devo6-publish-fix-pack -p:ContinuousIntegrationBuild=true -p:BuildProjectReferences=false -p:PackageVersion=0.1.0-ci.fix`: 成功。

## 残リスク

GitHub Actions 上の `Publish NuGet Package` は PR merge 後の master push で再確認される。ローカルでは失敗条件の再現と修正後 pack 成功を確認した。
