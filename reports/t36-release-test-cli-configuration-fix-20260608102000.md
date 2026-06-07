# T36 リリース構成テスト修正レポート

## 目的

GitHub 上の `dotnet test Devo6.WorkFlow.sln --configuration Release --no-restore` で、CLI 統合検査が Debug 出力を探して失敗する問題を修正する。

## 原因

CLI 統合検査の helper が `dotnet run --project ... --no-build` を使っていた。`--configuration` を渡していないため、リリース構成の検査中でも `dotnet run` が既定の Debug 出力を探していた。

## 変更内容

- `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs` の CLI 起動 helper に、現在の検査実行ディレクトリから取得した build 構成を渡すようにした。
- `tests/Devo6.WorkFlow.Tests/StandardConfigLoadingContractTests.cs` も同じ構成解決を使うようにした。
- `dotnet run` に `--configuration <Debug|Release>` を明示し、`--no-build` と構成が一致するようにした。

## 検証

- `dotnet build Devo6.WorkFlow.sln --configuration Release`
- `dotnet test Devo6.WorkFlow.sln --configuration Release --no-restore`

## 結果

- リリース構成の全体検査は 171 件成功した。
