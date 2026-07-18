# 課題 #20 dotnet-script 互換実装報告

## タスク

- 課題 #20 の設計に基づき、`dotnet-script` 2.0.1 と同じ nullable 診断境界を Engine に適用する。
- NuGet 依存復元のキャッシュ基準を `dotnet-script` の標準規則へ戻す。
- OmniSharp で `.csx` の NuGet API を補完するための設定例と利用手順を追加する。

## TDD 方針

利用者目線の検査で、次の契約を先に固定した。

- `Execute` は nullable context 外の nullable annotation を `SCRIPT_COMPILE_FAILED` にする。
- `Validate` も同じ診断とエラーコードを返す。
- `#nullable enable` を付けた同等 script は成功する。
- nullable 範囲外の通常 warning は失敗へ昇格しない。
- 明示した `DotnetScriptCachePath` は `Execute` と `Validate` の依存解決 request へ伝達される。
- 未指定の cache path は request 上で `null` を維持する。
- サンプルの `omnisharp.json` は script NuGet 参照と `net8.0` を有効にする。

検査は `tests/Devo6.WorkFlow.Tests/DotnetScriptCompatibilityTests.cs` に集約し、外部 NuGet source を必要とする箇所は固定 dependency graph provider へ置き換えた。

## 実装内容

### コンパイル診断

`CsxEntryLoader` に `CS8600` から `CS8655` までを `ReportDiagnostic.Error` にする不変設定を追加した。

`Execute` と `Validate` は共通の `GetCompileErrors` を使う。ヘルパーは Roslyn の公開 API である `CSharpCompilationOptions.WithSpecificDiagnosticOptions` を使用し、対象外の warning は従来どおり失敗にしない。

### NuGet キャッシュ

`CsxEntryLoaderOptions` に `DotnetScriptCachePath` を追加した。

同じ値を `CsxNuGetDependencyGraphRequest` へ伝達し、既定 provider と script option 作成側の `ScriptCompiler` に渡す。未指定時は空の cache path を渡し、`Dotnet.Script.DependencyModel` の `DOTNET_SCRIPT_CACHE_LOCATION` と OS 標準位置の規則へ委譲する。

NuGet 固定 version、許可一覧、任意または必須ロック、`#load "nuget: ..."`、復元失敗の既存契約は変更していない。

### エディタ補完

`samples/multi-folder-composite/omnisharp.json` を追加し、次を設定した。

- `enableScriptNuGetReferences: true`
- `defaultTargetFramework: net8.0`

サンプル README に、`engine validate` で依存関係を復元した後、OmniSharp または使用中の C# 言語サービスを再読み込みする手順を追加した。Engine は既存のエディタ設定を自動変更しない。

### 既存 fixture の整合

新しい nullable 診断契約で意図せず失敗する既存 Config fixture 2 件とサンプル Entry に `#nullable enable` を追加した。

## 変更ファイル

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `tests/Devo6.WorkFlow.Tests/DotnetScriptCompatibilityTests.cs`
- `tests/Devo6.WorkFlow.Tests/StandardConfigLoadingContractTests.cs`
- `samples/multi-folder-composite/main.csx`
- `samples/multi-folder-composite/omnisharp.json`
- `samples/multi-folder-composite/README.md`
- `doc/issue-20-dotnet-script-compatibility-design.md`
- `reports/issue-20-dotnet-script-compatibility-design-review-20260718152000.md`

## 検証

GitHub Actions の `PR xUnit Tests` run #89 で、checkout、.NET setup、restore、solution test を含む job が成功した。

追加の format、Markdown、差分検査は最終検証で記録する。

## 残リスク

- 実際の補完は別プロセスの C# 言語サービスを含むため、通常の xUnit では完全自動化していない。
- `DOTNET_SCRIPT_CACHE_LOCATION` を使う実 restore は環境依存になるため、通常検査では request 伝達と標準 provider の既存規則を組み合わせて保証する。
- `Dotnet.Script.Core` の version を更新する場合は、nullable 診断範囲と cache 規則を再確認する必要がある。
