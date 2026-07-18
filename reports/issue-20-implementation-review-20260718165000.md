# 課題 #20 dotnet-script 互換実装点検

## 対象

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `tests/Devo6.WorkFlow.Tests/DotnetScriptCompatibilityTests.cs`
- `tests/Devo6.WorkFlow.Tests/StandardConfigLoadingContractTests.cs`
- `samples/multi-folder-composite/main.csx`
- `samples/multi-folder-composite/omnisharp.json`
- `samples/multi-folder-composite/README.md`
- `doc/issue-20-dotnet-script-compatibility-design.md`
- `reports/issue-20-implementation-20260718164500.md`

## 点検観点

- `Dotnet.Script.Core` 2.0.1 の nullable 診断範囲と一致するか。
- `Execute` と `Validate` の診断生成が共通化されているか。
- 通常 warning を一律にエラー化していないか。
- Roslyn または `Dotnet.Script.Core` の非公開実装へ依存していないか。
- cache path が loader option から依存解決 request と既定 provider へ一貫して伝達されるか。
- NuGet の固定 version、許可一覧、ロック、NuGet script package の既存契約を壊していないか。
- エンジン実行とエディタ補完の責務を分離しているか。
- 検査が外部 NuGet source を通常前提にしていないか。
- 既存 fixture への変更が新しい診断契約への明示対応に限定されているか。

## 確認結果

### nullable 診断

`Enumerable.Range(8600, 56)` は `CS8600` から `CS8655` までを含み、`Dotnet.Script.Core` 2.0.1 の診断設定と一致する。

`GetCompileErrors` は `CSharpCompilationOptions.WithSpecificDiagnosticOptions` を使って診断を取得する。非公開 field の書き換えや reflection は追加されていない。

`Execute` と `Validate` は同じヘルパーを使用し、エラーがある場合は script 実行前に停止する。対象外 warning を確認する回帰検査もあり、全 warning のエラー化にはなっていない。

### cache path

`CsxEntryLoaderOptions.DotnetScriptCachePath` は `CsxNuGetDependencyGraphRequest.DotnetScriptCachePath` へ設定され、既定 provider の `ScriptCompiler` に渡される。

未指定時に `ScriptCompiler` へ渡す空文字列は、`Dotnet.Script.DependencyModel` が `string.IsNullOrEmpty` で標準 cache 位置を選択する既存規則と整合する。request 上では `null` を維持するため、独自 provider は未指定と明示指定を区別できる。

既存の lock 比較材料には cache path を追加しておらず、cache の有無が再現性判定へ混入しない。

### エディタ補完

`omnisharp.json` は script NuGet 参照を有効にし、sample の対象 framework と同じ `net8.0` を指定する。

サンプル README は `engine validate` による復元と C# 言語サービスの再読み込みを別手順として説明している。Engine が稼働中の言語サービスへ参照を直接注入する、または既存設定を暗黙変更する説明にはなっていない。

### 検査

nullable 診断、通常 warning、cache path 伝達、OmniSharp 設定を 1 つの focused test class にまとめている。NuGet dependency graph は固定 provider を使い、通常の solution test が外部通信を要求しない。

GitHub Actions `PR xUnit Tests` run #89 は成功した。

## 指摘事項

通常経路を妨げる blocker は確認しなかった。

非ブロッキングの注意点は次のとおりである。

- 実際の補完操作は xUnit の範囲外であり、利用環境での言語サービス再読み込み確認が必要である。
- `Dotnet.Script.Core` 更新時は、診断 ID 範囲と cache 規則を再調査する必要がある。
- 最終提出前に format、Markdown、差分検査を追加実行する。

## 点検の制約

本点検は同一セッション内の実装差分レビューである。GitHub Actions による solution test は独立環境で実行されたが、別担当者による人的レビューではない。

## 結論

実装は課題 #20 の設計と整合し、solution test が成功している。format、Markdown、差分検査を最終確認した後、実装 PR として更新可能である。
