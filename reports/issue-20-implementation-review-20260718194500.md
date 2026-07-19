# 課題 #20 dotnet-script 互換実装点検

## 1. 対象

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `tests/Devo6.WorkFlow.Tests/DotnetScriptCompatibilityTests.cs`
- `tests/Devo6.WorkFlow.Tests/StandardConfigLoadingContractTests.cs`
- `samples/multi-folder-composite/main.csx`
- `samples/multi-folder-composite/omnisharp.json`
- `samples/multi-folder-composite/README.md`
- `doc/issue-20-dotnet-script-compatibility-design.md`
- `reports/issue-20-implementation-20260718194000.md`

## 2. 点検観点

- nullable 関連診断の範囲が `Dotnet.Script.Core` 2.0.1 と一致するか
- `Execute` と `Validate` が同じ診断規則を使うか
- 対象外の通常 warning を一律エラー化していないか
- 非公開 Roslyn 実装へ依存していないか
- cache path 未指定時に workflow root を cache root として固定しないか
- 明示 cache path が実行と検証の両方へ伝達されるか
- NuGet lock、許可一覧、固定 version、NuGet script package の既存契約を壊していないか
- OmniSharp と Engine の責務境界が明確か
- 新しい契約により既存検査やサンプルが意図せず失敗しないか

## 3. 確認結果

### 3.1 コンパイル診断

`Enumerable.Range(8600, 56)` は `CS8600` から `CS8655` までを生成し、`ReportDiagnostic.Error` へ設定する。範囲は `Dotnet.Script.Core` 2.0.1 の診断設定と一致する。

`GetCompileErrors` は `script.GetCompilation()` から取得した `CSharpCompilation` に `WithSpecificDiagnosticOptions` を適用する。公開 API だけを使用しており、非公開フィールドやリフレクションへの依存はない。

`Execute` と `Validate` は同じ helper を呼ぶため、同一 source、参照、parse option に対する失敗判定が一致する。通常 warning を対象範囲外まで昇格する処理はない。

### 3.2 キャッシュ

従来は Entry directory を `ScriptCompiler` の cache path に渡していた。対応後は `CsxEntryLoaderOptions.DotnetScriptCachePath` を渡し、null の場合は空指定として Dotnet.Script の標準 cache 規則へ委ねる。

dependency graph provider には `CsxNuGetDependencyGraphRequest.DotnetScriptCachePath` 経由で同じ指定が渡る。既存 constructor は維持され、独自 provider は追加 property を無視しても従来動作を維持できる。

lock 比較の対象、直接参照の検証、固定 version 要求、`#load "nuget: ..."` の解決処理には変更がなく、既存の再現性契約を壊す差分は見つからなかった。

### 3.3 エディタ補完

sample の `omnisharp.json` は script NuGet 参照を有効にし、repository の target framework と同じ `net8.0` を指定する。

README は `engine validate` による依存復元と、言語サービスの再起動または再読み込みを分けて説明する。Engine 単独で稼働中の言語サービスへ参照を注入できるように見せる記述はない。

### 3.4 回帰検査

互換検査は nullable failure、nullable success、通常 warning、明示 cache path、未指定 cache path、OmniSharp 設定を直接確認する。

新しい nullable 契約で失敗した標準 Config fixture 2 件と sample Entry は `#nullable enable` を明示し、利用側 source として新契約へ適合している。

PR xUnit Tests run #89 は restore と Release solution test を含めて成功した。

## 4. 指摘事項

通常経路を妨げる blocker、既存公開契約の破壊、利用者確認が必要な capability gap は確認しなかった。

非ブロッキングの注意点は次のとおりである。

- 実際の補完更新は外部の C# 言語サービスに依存するため、設定追加後も言語サービスの再読み込みが必要である
- `Dotnet.Script.Core` の version を更新する場合は、nullable 診断 ID の範囲と cache 規則を再確認する必要がある
- cache の実ファイル配置は OS と環境変数に依存するため、通常 xUnit では provider request の伝達を主に固定している

## 5. 結論

課題 #20 の設計、実装、検査、サンプル、利用手順は整合している。PR #23 は実装 PR として提出可能であり、課題 #20 を閉じる内容と判断する。