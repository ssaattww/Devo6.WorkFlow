# 課題 #20 dotnet-script 互換設計点検

## 1. 対象

- `doc/issue-20-dotnet-script-compatibility-design.md`
- 課題 #20 の本文
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj`
- `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
- `samples/multi-folder-composite/main.csx`
- `doc/workflow_engine_spec.md` の型定義方針と csx 依存再現性
- `Dotnet.Script.Core` 2.0.1 の `ScriptCompiler`
- `Dotnet.Script.DependencyModel` 2.0.1 のキャッシュ位置と復元経路
- OmniSharp の script NuGet 参照設定と依存解決経路

## 2. 点検観点

- `dotnet-script` と現在のエンジンで nullable 関連診断が異なる原因を設計が正しく扱っているか
- `#nullable enable` の欠落自体と、実際に発生する nullable 関連診断を区別しているか
- `Execute` と `Validate` の失敗契約が一致するか
- 非公開 Roslyn 実装へ依存しない方式になっているか
- `ScriptCompiler` の第 2 引数をキャッシュパスとして扱っているか
- 独自 provider の互換性を維持できる API 変更か
- NuGet ロックと許可一覧の既存契約を変更していないか
- エンジン実行とエディタ補完の責務を混同していないか
- 自動検査と手動検証の境界が明確か
- 実装 task が検査先行で分割されているか

## 3. 確認結果

### 3.1 コンパイル診断

現在のエンジンは `ScriptCompiler.CreateScriptOptions` の後に `CSharpScript.Create` を直接呼び、`DiagnosticSeverity.Error` だけを失敗としている。一方、`Dotnet.Script.Core` 2.0.1 は `CS8600` から `CS8655` を `ReportDiagnostic.Error` へ設定する。

設計はこの差を 1 つの共通ヘルパーで埋め、`Execute` と `Validate` の両方へ適用する。`#nullable enable` がないことだけを失敗とせず、`CS8632` など対象診断の発生を条件とするため、挙動の記述は妥当である。

公開 API の `CSharpCompilationOptions.WithSpecificDiagnosticOptions` を使い、`Dotnet.Script.Core` 内部の非公開フィールド変更を再現しない方針も妥当である。

### 3.2 キャッシュ

現在の `new ScriptCompiler(logFactory, workingDirectory, true)` は、作業ディレクトリをキャッシュパスとして渡している。設計は既定値を `null` に戻し、`DOTNET_SCRIPT_CACHE_LOCATION` と OS 標準位置へ委譲する。

`CsxEntryLoaderOptions` と `CsxNuGetDependencyGraphRequest` への init property 追加は、既存コンストラクタと独自 provider を維持できるため、互換性の高い変更である。CLI オプションを増やさず、環境変数とライブラリ API に限定する範囲も初期対応として妥当である。

### 3.3 エディタ補完

エンジンが NuGet 復元を行っても、別プロセスの C# 言語サービスへ参照を直接追加できない。設計は `omnisharp.json`、`engine validate`、言語サービス再読み込みを成立条件として明示し、エンジン単独での即時補完を保証していない。

`run` または `validate` による設定ファイルの暗黙変更を避け、将来の初期化コマンドを別 task とする判断は、既存設定の破壊を避けるため妥当である。

### 3.4 既存契約

固定 version、許可一覧、任意ロック、必須ロック、`#load "nuget: ..."`、復元失敗のエラーコードは維持される。キャッシュ位置はロック比較の入力にせず、解決結果とメタデータを比較する現在の契約を保っている。

### 3.5 検査

nullable 診断、通常警告、共通失敗契約、キャッシュ伝達、環境変数、workflow root の生成物、既存 NuGet 契約を自動検査へ分けている。エディタ補完は外部プロセスを含むため、設定構造と復元を自動化し、実際の補完を手動検証に分ける方針は妥当である。

## 4. 指摘事項

通常経路を妨げる設計上の blocker は確認しなかった。

実装時には次を再確認する。

- `CSharpCompilationOptions` を変更したコンパイルから取得した診断と、実行する `Script<object>` のソース、参照、parse option が同一であること
- `DOTNET_SCRIPT_CACHE_LOCATION` を変更する検査が並列検査へ干渉しないこと
- `defaultTargetFramework` がサンプルと配布対象の .NET 版に一致すること
- C# 言語サービスの手動検証結果に、再読み込み前後の差を記録すること

## 5. 点検の制約

本点検は同一作業内の設計点検であり、実装差分に対する独立レビューではない。T69 以降の実装完了前に、実装担当と分離したレビューを追加する。

接続環境ではリポジトリのローカル clone と既存 npm 依存の取得を行えなかったため、repo 固有の Markdown lint は未実行である。文書については見出し階層、コード囲み、表の列数、末尾空白、空行を局所検査した。PR の実行定義で追加検査が開始された場合は、その結果を確認する。

## 6. 結論

課題 #20 の設計は実装 task へ分解できる状態であり、設計 PR として提出可能と判断する。
