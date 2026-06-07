# T34 ツール化レポート

## 目的

CLI を別端末へ導入できるツール用パッケージとして作成できるようにし、README に必要な開発環境と導入手順を記載した。

## 変更内容

- `src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj` にツール配布用のパッケージ情報を追加した。
- パッケージのコマンド名を `engine` にした。
- パッケージ ID を `Devo6.WorkFlow.Cli` にした。
- パッケージ同梱の README としてリポジトリルートの `README.md` を指定した。
- `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs` に CLI プロジェクトのツール設定検査を追加した。
- `README.md` に .NET 8 以降の開発環境、パッケージ作成、導入、更新、導入後の `engine` コマンドを追記した。
- `artifacts/` を生成物として `.gitignore` に追加した。

## 検査先行

- `dotnet test Devo6.WorkFlow.sln --filter CliProjectIsConfiguredAsDotnetToolPackage` はパッケージ情報追加前に失敗することを確認した。
- パッケージ情報追加後、同じ検査が成功することを確認した。

## 検証

- `dotnet test Devo6.WorkFlow.sln --filter CliProjectIsConfiguredAsDotnetToolPackage`
- `dotnet pack src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj -c Release -o ./artifacts/packages`
- `dotnet tool install --tool-path "$toolpath" Devo6.WorkFlow.Cli --add-source ./artifacts/packages --version 0.1.0`
- `"$toolpath/engine"`

## 結果

- パッケージ `artifacts/packages/Devo6.WorkFlow.Cli.0.1.0.nupkg` が作成できた。
- `--tool-path` への一時導入後、`engine` コマンドが起動し、使い方を表示した。
- 別端末で導入する利用者には .NET 8 以降の開発環境が必要であることを README に明記した。

## リスク

- NuGet.org への公開手順はまだ記載していない。現時点の README は、作成済みパッケージを配置して `--add-source` で導入する手順に限定している。
