# 課題 #27 .NET 10 build pipeline 実装報告

## 対象と作業状態

- Repository: `ssaattww/Devo6.WorkFlow`
- Branch: `master`
- 作業開始 HEAD / 現在の HEAD: `f78414d369bf751c2b5c77c3bc1c4bf843ecf731`（変更は未コミット）
- 対象: T83 / P36。設計書 `doc/issue-27-dotnet10-file-based-app-design.md`（レビュー合格 blob `6f393a2f1d35825ecabbca002d51af7b2505cdd0`）は変更していない。
- T82 の先行変更と未コミット設計・追跡ファイルを保持した。

## 実装した境界

- Abstractions、Engine、CLI、Tests を `net8.0;net10.0` の multi-target にした。共通 `Directory.Build.props` の固定 `net8.0` を除去し、既存 `.csx` のテスト子プロセスはそのテスト asset と同じ TFM を選ぶ。
- `.csx` は従来の `CsxEntryLoader` 経路を維持する。`.cs` は拡張子分岐後に TFM・許可 directive・必須 lock を検査する。net8.0 asset では SDK を起動する前に拒否する。
- Engine の `FileBasedCsWorkflowBuilder.Build(sourcePath, hostPackageVersion)` が新規 `.cs` の共通ビルド API。埋め込み呼出元は配布 NuGet 版を必須引数で明示し、欠落・不正なら `PACKAGE_VERSION_UNKNOWN` で失敗する。CLI tool はインストール先の `.nuspec` から実配布版を読み取る。CLI / Engine package の CLR assembly version を NuGet 版の代用にしない。
- restore 前に Engine 直接参照の版がホスト配布版と一致すること、許可 directive のみ、厳密な `[x.y.z]` 版指定、`net10.0`、`OutputType=Library`、`PublishAot=false`、入力隣接 `devo6.nuget.lock.yaml`、lock version/TFM/direct graph/全 package graph/source set を検査する。SDK の最初の `restore` 後、assets の実効 FrameworkReference 範囲、全パッケージ版と direct/transitive、source、RID を lock と照合し、通過後のみ `build --no-restore` を呼ぶ。版が存在しない source では SDK restore 自身が exact-version 要求を失敗させ、build に進まない。
- SDK は入力 directory から選択される `10.0.4xx` のみ受け付ける。SDK file-based app の標準 `XDG_DATA_HOME/dotnet/runfile` 出力を使い、同一入力パスの concurrent restore / assets check / build / artifact lookup はパス単位の排他で直列化する。fixture の `bin/obj` は生成されず、成果物と assets は runfile 以下に置かれた。
- `validate` は成果物を生成できても `SCRIPT_METADATA_VALIDATION_UNAVAILABLE`、`run` は `SCRIPT_ENTRY_EXECUTION_UNAVAILABLE` を返す。PE metadata-only 検査、Provider の生成、`Create()`、Step 実行はまだない。未実装処理を成功扱いしない。

## 検証証拠

使用した公式 SDK 10.0.404 は Linux x86_64 用配布 URL から `/tmp/devo6-issue27-sdk-10.0.404` に一時展開し、提示済み SHA-512 と照合した。

```text
577d0eb17fec33d029104c5c63a1ab0accc3e28776274abef2218dbac5af18d2109b5406d26b31d76f5c245215e3ba0926a94c20246c672c41847c9e1eb1dc50
```

実際に pack した一時 CLI / Engine nupkg を file-based `.cs` fixture から使い、CLI tool の `.nuspec` が host package version `0.1.0` を返すことを確認した。`Engine@[0.1.0]` と一致する 28 package lock / assets graph で、`dotnet restore <file.cs>` 1 回の後 `dotnet build <file.cs> --no-restore` が成功した。SDK 呼出し recorder は `--version`、`restore` 1 回、`build --no-restore` 1 回を記録した。ソース隣接の `bin/obj` は 0 件で、DLL と assets は分離 runfile 出力に生成された。

Engine `0.1.1` のみが見つかる一時 feed では、要求 `Engine@[0.1.0]` が restore の `NU1102` で失敗し、SDK recorder は `--version` と restore のみ、build 呼出しは 0 件だった。CLI と fixture に作成した内容は一時ディレクトリ内である。

SDK runner recorder を注入した新規テストで、Engine のみの入力における lock 欠落、禁止 directive、不正 lock、host/Engine 版不一致のいずれも runner 呼出し 0 件を確認した。

最終検査（SDK 10.0.404、Release、既存 restore 済み依存）:

| 検査 | 結果 |
| --- | --- |
| `dotnet build Devo6.WorkFlow.sln -f net8.0 --no-restore` | 成功、警告 0 / error 0 |
| `dotnet build Devo6.WorkFlow.sln -f net10.0 --no-restore` | 成功、警告 0 / error 0 |
| `CsFileInputPreflightTests`, net8.0 | 7 passed |
| `CsFileInputPreflightTests`, net10.0 | 11 passed |
| `CliRunValidateTests` (`.csx` regression), net8.0 | 43 passed |
| `CliRunValidateTests` (`.csx` regression), net10.0 | 43 passed |
| `git diff --check` | 成功 |

テスト対象 net8.0 を動かしたランタイムは SDK 配布の .NET 10.0.15 で、`DOTNET_ROLL_FORWARD=Major` を設定した。`.NET 8 runtime` は導入していないため、net8 runtime のみの環境での起動は未検証。

## 未完了と次の境界

CLI から `.cs` を `validate` / `run` すると、SDK build 後にそれぞれ上記 `...UNAVAILABLE` 診断で終了する。PEReader / MetadataReader の non-execution、Config/Entry 契約、host 型同一性、Provider / Step の正常実行、.NET 8 runtime の実行、NuGet tool install/update の配布更新経路は今回完了していない。次 task T84 で metadata-only validate と Provider / workflow 実行を扱う。

この作業でコミット、push、PR、CI、GitHub 書き込みはしていない。SDK、nupkg、feed、fixture は task-specific `/tmp` に限った。
