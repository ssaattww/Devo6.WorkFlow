# T45 NuGet ロック任意化 実装報告

## 目的

NuGet ロックファイルを任意にし、通常利用では `NuGet.config` を含む通常の NuGet 設定で依存関係を解決する。`devo6.nuget.lock.yaml` が存在する場合だけロック検証し、明示的な厳格指定だけロックファイル欠落を `SCRIPT_NUGET_LOCK_MISSING` にする。

## TDD

先に次の検査を追加または更新した。

- lock file が無い既定の `Execute` / `Validate` は provider 解決へ進んで成功する。
- `RequireNuGetLock=true` では lock file 欠落が `SCRIPT_NUGET_LOCK_MISSING` になる。
- CLI `run` / `validate` の `--locked` は lock file 欠落を失敗にする。
- 複数フォルダサンプルは lock file 無しで検証対象になる。

実装前の確認として `dotnet test Devo6.WorkFlow.sln --filter "NuGetLock|AllowNuGet|MultiFolderCompositeSample"` を実行し、`CsxEntryLoaderOptions.RequireNuGetLock` 未実装の compile error で失敗することを確認した。

## 実装

- `CsxEntryLoaderOptions.RequireNuGetLock` を追加した。
- `VerifyNuGetLock` は lock file が無い場合、既定では provider 解決へ進み、NuGet script load 内の nested 参照検査と直接参照 flag 補正だけを行ってロック比較を省略する。
- lock file が存在する場合は、既存どおり directReferences、resolvedDependencies、再現性情報、`verifyPackageSources` を検証する。
- CLI `run` / `validate` に `--locked` を追加し、loader options へ渡す。
- README と設計書に、`NuGet.config` を含む通常の NuGet 設定へ委ねる既定挙動と、`--locked` の厳格指定を記載した。
- 複数フォルダサンプルの `devo6.nuget.lock.yaml` を削除し、通常利用の例に寄せた。

## 検査結果

- `dotnet test Devo6.WorkFlow.sln --filter "NuGetLock|AllowNuGet|MultiFolderCompositeSample"`: 成功。43 件。
- `dotnet test Devo6.WorkFlow.sln --filter CodingStandards`: 成功。3 件。
- `npm run lint:md`: 初回は進捗 file の `mode` と `metadata` 表記で失敗。表現を修正後、成功。
- `npm run lint:md:terms`: 成功。`SudachiPy term variants: none`。
- `git diff --check`: 成功。

## 変更ファイル

- `README.md`
- `doc/workflow_engine_spec.md`
- `phases-status.md`
- `tasks-status.md`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `src/Devo6.WorkFlow.Cli/Program.cs`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`
- `tests/Devo6.WorkFlow.Tests/SampleWorkflowTests.cs`
- `samples/multi-folder-composite/devo6.nuget.lock.yaml` 削除
- `reports/t45-nuget-lock-optional-implementation-20260608120000.md`

## 残るリスク

通常の `dotnet test` は外部 NuGet 参照元へ依存しない固定 provider で検証している。実際の参照元、認証、proxy などは利用者環境の NuGet 設定に従う。
