# T49 allow-nuget 任意化 点検報告

## 範囲

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `src/Devo6.WorkFlow.Cli/Program.cs`
- `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
- `tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`
- `README.md`
- `doc/workflow_engine_spec.md`
- `tasks-status.md`
- `phases-status.md`

## 点検結果

- 指摘なし。

## 確認観点

- `--allow-nuget` が通常利用で必須ではないこと。
- 固定 version 指定は引き続き必須であること。
- `--allow-nuget` を指定した場合だけ一覧外の NuGet 直接参照を拒否すること。
- README と設計書が実装と同じ契約を説明していること。
- 追加または変更した C# 関数とプロパティに日本語 XML コメントがあること。

## 検証根拠

- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter "AllowNuGet|NuGetLock|明示制限外|restricted"`
- `dotnet test Devo6.WorkFlow.sln --filter CodingStandards`
- `dotnet test Devo6.WorkFlow.sln`
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`
