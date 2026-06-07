# T34 ツール化レビュー

## 対象

- `src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj`
- `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs`
- `README.md`
- `tasks-status.md`
- `phases-status.md`
- `.gitignore`

## 観点

- CLI プロジェクトが `dotnet pack` でツール用パッケージを作成できること。
- 別端末で導入する利用者向けに、必要な .NET 8 以降の開発環境と導入手順が README に書かれていること。
- パッケージ作成後、一時ツール配置先から `engine` を起動できること。
- 新規検査と XML コメントがリポジトリの標準に沿っていること。
- Markdown lint と表記揺れ検査が通ること。

## 指摘

なし。

## 確認結果

- `dotnet test Devo6.WorkFlow.sln`
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`
- `dotnet pack src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj -c Release -o ./artifacts/packages`
- `dotnet tool install --tool-path "$toolpath" Devo6.WorkFlow.Cli --add-source ./artifacts/packages --version 0.1.0`
- `"$toolpath/engine"`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`

## 残るリスク

- NuGet.org への公開手順は今回の範囲外。現時点では、作成済みパッケージを配置して `--add-source` から導入する手順を README に記載している。
