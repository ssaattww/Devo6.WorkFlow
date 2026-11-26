# 課題 #27 metadata-only validate 実装報告

## 対象と識別情報

- Repository: `ssaattww/Devo6.WorkFlow`
- Branch: `master`
- 作業前 / 現在の HEAD: `f78414d369bf751c2b5c77c3bc1c4bf843ecf731`（T82-T84 を含む差分は未コミット）
- 対象: T84 / P37。承認済み設計 `doc/issue-27-dotnet10-file-based-app-design.md` §5–10。
- T83 の `.cs` preflight、版・lock/assets 照合、分離 build、既存 `.csx` 経路は保った。

## 実装内容

- Engine に `IWorkflowEntry`、`IWorkflowEntryProvider`、`WorkflowEntryAttribute` を追加し、`CompositeStep<T>` は `IWorkflowEntry` を実装する。設計書の入力例と公開契約を一致させた。
- `FileBasedCsMetadataValidator` は .NET 10 target 限定で `PEReader` / `MetadataReader` を使い、成果 DLL を実行用 `AssemblyLoadContext` に入れずに読み取る。属性は custom attribute blob と TypeReference / AssemblyReference から decode し、コンストラクターは呼び出さない。
- 入力 Library 内の Provider 候補、`WorkflowEntryAttribute` の名前と namespace、重複名、Entry の既存照合順（完全修飾名、root 名、唯一の短縮名）、公開・sealed・具象・非 generic class、公開引数なし constructor、`IWorkflowEntryProvider` と `Create()` の戻り型を検査する。Engine の host assembly identity を照合し、Abstractions 参照が成果物にあれば同じ host identity か検査する。
- `FileBasedCsWorkflowBuilder.Validate` は相対 Config path を入力 `.cs` の親ディレクトリから解決し、指定 Config を build 前に確認する。ビルド後の PE 検査は SDK の出力ロックを保持したまま実施する。CLI `.cs validate` は検証成功時だけ `Validation succeeded.` と exit 0、build / metadata / Config の失敗は既存安定コードを表示して非 0 で返す。`run` は同じ metadata 検証後も `SCRIPT_ENTRY_EXECUTION_UNAVAILABLE` で非 0 のまま。
- `.csx` 経路と `.nuspec` による CLI 配布版解決は変更していない。`PACKAGE_VERSION_UNKNOWN` と Engine 版不一致の restore 前拒否も保持した。

## 検証

Linux x86_64 用の公式 .NET SDK 10.0.404 を `/tmp/devo6-issue27-sdk-10.0.404` に一時展開し、既知の公式 SHA-512 を照合した。

```text
577d0eb17fec33d029104c5c63a1ab0accc3e28776274abef2218dbac5af18d2109b5406d26b31d76f5c245215e3ba0926a94c20246c672c41847c9e1eb1dc50
```

NuGet restore はリポジトリ既存の固定依存だけを一時 package cache に取得した。追加 package reference は導入していない。

| 検査 | 結果 |
| --- | --- |
| `dotnet build Devo6.WorkFlow.sln -f net8.0 -c Release --no-restore` | 成功、警告 0 / error 0 |
| `dotnet build Devo6.WorkFlow.sln -f net10.0 -c Release --no-restore` | 成功、警告 0 / error 0 |
| `FileBasedCsMetadataValidatorTests` と `CsFileInputPreflightTests`, net10.0 | 21 passed、0 failed |
| `CsFileInputPreflightTests`, net8.0 | 7 passed、0 failed |
| 既存 `CliRunValidateTests`（`.csx`）, net8.0 | 43 passed、0 failed |
| 既存 `CliRunValidateTests`（`.csx`）, net10.0 | 43 passed、0 failed |
| `git diff --check` | 成功 |

副作用検査では、別 DLL に含む module initializer、Provider の static initializer / constructor / `Create()`、custom attribute constructor、Step constructor / `Execute()` が marker file を書く fixture をコンパイルした。Metadata validator の正常な validate 後も全 marker が 0 件で、`AssemblyLoadContext.Default` に当該 fixture DLL が存在しないことを確認した。型不正、要求 Entry 不在、同名 Entry 重複もそれぞれ適切なコードで失敗した。

実配布形を確認するため、一時 CLI / Engine nupkg を pack し、CLI `0.1.0` を `--framework net10.0` で一時 tool path に install して通常の `engine` コマンドから fixture を検証した。CLI は `.nuspec` から配布版を読んだ。実行結果:

```text
engine validate ./main.cs --entry Main                  => exit 0, Validation succeeded.
engine validate ./main.cs --entry Missing               => exit 1, ENTRY_STEP_NOT_FOUND
engine validate ./main.cs --workflow-config ./missing.yaml => exit 1, CONFIG_NOT_FOUND
engine run ./main.cs --entry Main                       => exit 1, SCRIPT_ENTRY_EXECUTION_UNAVAILABLE
```

上記の各操作後も marker は 0 件。ソース隣接 `bin/obj` は作られず、SDK の runfile 出力配下に DLL と assets が生成された。valid fixture の Engine package / lock / assets graph を通して実際の SDK restore・build・PEReader validate を確認した。

net8.0 target のテスト実行には SDK 同梱 .NET 10.0.15 runtime と `DOTNET_ROLL_FORWARD=Major` を使った。.NET 8 runtime のみの環境での実行は未確認。作業に使った SDK、nupkg、fixture、tool install は検証後に task-specific `/tmp` から削除した。

## 未完了・次の作業

- Provider / `Create()` の実行、Entry 名の戻り値照合、共有 AssemblyLoadContext、Config 型・Step 実行は未実装。`run` は明示的に未対応診断を返す。T85 / P38 へ分けて記録した。
- .NET 8 runtime のみの環境、Windows、公開配布後の install/update、CI は未検証・未実施。
- 作業中に初回の test build は `EmitResult` の using 不足で失敗した。また最初の型不正テスト fixture は top-level private class が C# で許可されず fixture compile に失敗した。using を追加し、top-level `internal` 型に修正した後の最終 build / test はすべて成功した。
- commit、push、PR、CI、GitHub 書き込みはしていない。独立レビューも行っていない。
