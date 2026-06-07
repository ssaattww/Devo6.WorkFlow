# T35 NuGet 自動公開レビュー

## 対象

- `.github/workflows/publish-nuget.yml`
- `README.md`
- `tasks-status.md`
- `phases-status.md`

## 観点

- 公開対象が CLI ツールプロジェクトに限定されていること。
- 公開 workflow が不要な全体テストを実行せず、公開対象の build、pack、導入確認に限定されていること。
- NuGet.org 公開前にパッケージ作成と `dotnet tool install` の導入確認を行うこと。
- `engine` コマンドが導入後に起動されること。
- 公開には `NUGET_API_KEY` が必要であり、未設定時に明示的に失敗すること。
- README に NuGet.org からの導入手順と必要な秘密情報が書かれていること。

## 指摘

なし。

## 確認結果

- YAML として読み込み可能であることを確認した。
- 公開 workflow から `dotnet test` が外れ、公開対象プロジェクトの restore、build、pack に絞られていることを確認した。
- ローカルパッケージからの `dotnet tool install` と `engine` 起動確認を実施した。
- Release 構成、no-build pack、公開前版番号の組み合わせでも導入確認を実施した。
- Markdown lint と表記揺れ検査を実施した。

## 残るリスク

- GitHub 固有の構文検査ツールである `actionlint` はローカル環境に無かったため未実行。
- 実際の NuGet.org 公開は、`NUGET_API_KEY` を設定した GitHub 実行で最終確認が必要。
