# T37 提出前 .NET 検査 workflow レポート

## 目的

SSC の `pr-xunit-tests.yml` を参考に、pull request と手動実行で .NET 検査を走らせる workflow を追加する。

## 変更内容

- `.github/workflows/pr-xunit-tests.yml` を追加した。
- 対象 branch はこのリポジトリの既定 branch に合わせて `master` にした。
- `git ls-files '*.csproj'` から test project を発見する。
- 発見した test project ごとに `dotnet restore` と `dotnet test --configuration Release --no-restore` を実行する。
- test project が無い場合は notice を出して終了する。

## 検証

- `ruby -e "require 'yaml'; YAML.load_file('.github/workflows/pr-xunit-tests.yml'); puts 'yaml ok'"`
- `dotnet restore tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj`
- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --configuration Release --no-restore --verbosity minimal`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`

## 結果

- ローカルで test workflow 相当の Release 構成検査が通ることを確認した。
