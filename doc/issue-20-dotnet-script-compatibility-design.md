# 課題 #20 dotnet-script 互換設計

## 1. 位置付け

本書は `doc/workflow_engine_spec.md` を補足し、課題 #20 で扱う `dotnet-script` 互換のコンパイル診断、NuGet 復元用キャッシュ、エディタ補完の境界を定める。

対象となる実装は `Dotnet.Script.Core` 2.0.1 を利用する現在の `CsxEntryLoader` とする。実装時に依存パッケージの版を更新する場合は、本書の互換対象を更新し、差分を別途点検する。

本書では実行時コンパイルとエディタ補完を別の処理として扱う。エンジンが `.csx` を実行できることだけでは、別プロセスで動作する C# 言語サービスの参照更新までは保証しない。

## 2. 背景

現在の `CsxEntryLoader` は、`ScriptCompiler.CreateScriptOptions` で `ScriptOptions` を作成した後、`CSharpScript.Create` を直接呼び出す。コンパイル時は `DiagnosticSeverity.Error` の診断だけを失敗として扱う。

`Dotnet.Script.Core` 2.0.1 の `ScriptCompiler` は、`CS8600` から `CS8655` までの nullable 関連診断を `ReportDiagnostic.Error` として扱う。現在のエンジン経路は、この診断設定を適用する処理を通っていない。この差により、同じ `.csx` がエンジンでは成功し、`dotnet-script` ではコンパイルエラーになる場合がある。

また、現在の実装は `ScriptCompiler` の第 2 引数へ Entry のディレクトリを渡している。この引数は作業ディレクトリではなくキャッシュの基準パスであるため、ワークフロー配下に `dotnet-script/` が生成される。`dotnet-script` の既定キャッシュ位置と一致しないため、依存復元結果を共有しにくい。

エディタ補完は C# 言語サービスが独自に `.csx` の依存関係を解決して構成する。NuGet 復元結果が存在しても、言語サービス側の script NuGet 設定と再読み込みがなければ補完は更新されない。

## 3. 目的

課題 #20 では、次を満たす。

- nullable 関連診断の失敗判定を `dotnet-script` 2.0.1 と一致させる
- `Execute` と `Validate` で同じコンパイル診断規則を使う
- NuGet 復元用キャッシュの既定位置を `dotnet-script` の規則へ戻す
- `DOTNET_SCRIPT_CACHE_LOCATION` を既定経路で尊重する
- エディタ補完に必要な `omnisharp.json` の設定例と再読み込み手順を用意する
- 既存の NuGet 許可、固定版要求、ロック検査、`#load "nuget: ..."` の契約を維持する

## 4. 対象外

初期対応には次を含めない。

- `dotnet-script` CLI のすべてのオプションとの互換
- `dotnet-script` の実行用 assembly キャッシュとの完全共有
- エンジンから稼働中の C# 言語サービスを再起動する処理
- `engine run` または `engine validate` による既存 `omnisharp.json` の自動変更
- C# 言語サービスごとの差を吸収する専用連携
- NuGet パッケージの配置規則、`project.assets.json`、`contentFiles` の独自解析
- `Dotnet.Script.Core` の非公開メンバーをリフレクションで変更する実装

## 5. コンパイル診断の契約

### 5.1 nullable 関連診断

`CS8600` から `CS8655` までの診断は、元の重大度が警告であってもコンパイルエラーとして扱う。

`#nullable enable` が存在しないことだけでは失敗にしない。nullable annotation の使用などにより対象診断が実際に発生した場合に失敗する。たとえば nullable コンテキスト外で `string?` を使用して `CS8632` が発生した場合は、`SCRIPT_COMPILE_FAILED` とする。

対象範囲外の通常警告は、従来どおりコンパイル失敗にしない。エンジン固有の全警告エラー化は行わない。

診断 ID、対象ファイル、行、列、メッセージは Roslyn の診断文字列を維持する。複数のコンパイルエラーがある場合は、現在と同様に改行で結合する。

### 5.2 `Execute` と `Validate`

`CsxEntryLoader.Execute` と `CsxEntryLoader.Validate` は、同じ診断生成ヘルパーを使用する。

処理順は次のとおりとする。

1. `LoadScriptSource` でローカル `#load`、NuGet 参照、ロック検査を処理する
2. `CreateScriptOptions` で Roslyn script 用の参照と import を作成する
3. `CSharpScript.Create` で script を作成する
4. `CSharpCompilationOptions.WithSpecificDiagnosticOptions` を使って nullable 関連診断をエラーへ昇格する
5. 昇格後のコンパイルからエラー診断を取得する
6. エラーがなければ、`Execute` だけが script を実行する

`Execute` は診断確認前に `RunAsync` を呼ばない。`Validate` は script の実行が Entry 候補の収集に必要な現在の契約を維持するが、コンパイルエラーがある場合は実行しない。

### 5.3 実装方式

`DotnetScriptDiagnosticOptions` 相当の不変な診断設定を `CsxEntryLoader` 内部に 1 か所だけ定義する。

概念上の実装は次の形とする。

```csharp
private static readonly ImmutableDictionary<string, ReportDiagnostic>
    DotnetScriptDiagnosticOptions = Enumerable
        .Range(8600, 56)
        .ToImmutableDictionary(
            number => $"CS{number}",
            _ => ReportDiagnostic.Error);
```

診断取得ヘルパーは `script.GetCompilation()` から `CSharpCompilation` を取得し、公開 API の `WithSpecificDiagnosticOptions` を使ったコンパイルから診断を得る。

`Dotnet.Script.Core` が内部で行う非公開フィールドの変更は再利用しない。非公開実装への依存を避け、現在参照する 2.0.1 の診断範囲を回帰検査で固定する。

## 6. NuGet 復元用キャッシュの契約

### 6.1 既定位置

`ScriptCompiler` に渡すキャッシュパスの既定値は `null` とする。これにより `Dotnet.Script.DependencyModel` の標準規則へ委譲する。

標準規則では `DOTNET_SCRIPT_CACHE_LOCATION` が設定されている場合はその値を使い、未設定の場合は OS ごとの利用者キャッシュまたは一時ディレクトリを使う。

既定実行で workflow root 配下へ `dotnet-script/` を作らない。既存の `.gitignore` は、旧版で作成された生成物と明示キャッシュ指定への後方互換のため、初期対応では削除しない。

### 6.2 公開オプション

`CsxEntryLoaderOptions` に次を追加する。

```csharp
public string? DotnetScriptCachePath { get; init; }
```

- `null` は `dotnet-script` の標準キャッシュ位置を使う
- 絶対パスはその位置をキャッシュ基準として使う
- 相対パスは `Dotnet.Script.DependencyModel` の既存規則へ委ねる
- 主な用途はライブラリ利用者による明示制御と検査の分離である

初期対応では CLI オプションを追加しない。CLI 利用者は標準位置または `DOTNET_SCRIPT_CACHE_LOCATION` を使う。

### 6.3 dependency graph provider への伝達

`CsxNuGetDependencyGraphRequest` に、互換性を壊さない追加プロパティとして次を追加する。

```csharp
public string? DotnetScriptCachePath { get; init; }
```

既存の 4 引数コンストラクタは維持する。`CsxEntryLoader` が request を生成するときに、`CsxEntryLoaderOptions.DotnetScriptCachePath` を object initializer で設定する。

既定の `DotnetScriptCsxNuGetDependencyGraphProvider` は request の値を `ScriptCompiler` のキャッシュパスへ渡す。独自 provider は追加プロパティを無視しても既存動作を維持できる。

### 6.4 復元とロック検査

次の契約は変更しない。

- NuGet 直接参照は固定 version を必須とする
- `AllowedNuGetReferences` が空でなければ許可一覧を検査する
- `devo6.nuget.lock.yaml` が存在する場合は解決結果を比較する
- `RequireNuGetLock` が有効な場合だけロック欠落を失敗にする
- `#load "nuget: PackageId, Version"` は `dotnet-script` 互換構文を使う
- 復元失敗は `SCRIPT_NUGET_RESTORE_FAILED` とする

キャッシュ位置の変更は、ロック一致の判定材料を変更しない。ロック検査はキャッシュの有無ではなく、解決済み依存関係とメタデータを比較する。

## 7. エディタ補完の契約

### 7.1 責務境界

エンジンは NuGet 復元を実行し、標準のパッケージキャッシュと `dotnet-script` 互換キャッシュを準備する。

C# 言語サービスは別プロセスであり、補完用プロジェクトと assembly 参照を独自に構成する。エンジンは稼働中の言語サービスへ assembly 参照を直接注入しない。

したがって、補完の成立条件は次の組み合わせとする。

- ワークフローの NuGet 参照が固定 version である
- `engine validate` または `engine run` により復元が成功する
- workflow root に script NuGet 参照を有効にする `omnisharp.json` がある
- NuGet 参照を追加または変更した後に C# 言語サービスを再読み込みする

### 7.2 `omnisharp.json`

サンプルと利用者文書では次を標準例とする。

```json
{
  "script": {
    "enableScriptNuGetReferences": true,
    "defaultTargetFramework": "net8.0"
  }
}
```

`defaultTargetFramework` はワークフローが対象とする .NET の版に合わせる。現在のリポジトリサンプルでは `net8.0` を使う。

`engine run` と `engine validate` は、このファイルを自動作成または自動更新しない。既存のエディタ設定を暗黙に変更しないためである。

将来、初期化を自動化する場合は `engine init` またはエディタ設定専用コマンドとして別 task で設計する。既存 JSON の統合規則、上書き確認、複数言語サービスへの対応をその task で扱う。

### 7.3 推奨操作

補完準備だけを目的とする場合は、Step を実行しない `engine validate` を推奨する。

```bash
engine validate main.csx --workflow-config appsettings.yaml
```

新しい `#r "nuget: ..."` または `#load "nuget: ..."` を追加した後は、検証成功後に C# 言語サービスを再起動または再読み込みする。

エンジン実行直後、言語サービスの再読み込みなしに補完が即時更新されることは保証しない。

## 8. 処理フロー

### 8.1 `engine validate`

```text
Entry path 解決
  -> local #load と参照検査
  -> NuGet dependency graph 解決
  -> 任意のロック検査
  -> Roslyn script 作成
  -> dotnet-script 互換診断設定の適用
  -> コンパイルエラー確認
  -> Entry 解決と検証結果返却
```

### 8.2 `engine run`

```text
Entry path 解決
  -> local #load と参照検査
  -> NuGet dependency graph 解決
  -> 任意のロック検査
  -> Roslyn script 作成
  -> dotnet-script 互換診断設定の適用
  -> コンパイルエラー確認
  -> script 実行
  -> Entry 解決
  -> Config 準備
  -> workflow 実行
```

### 8.3 エディタ補完

```text
omnisharp.json を配置
  -> engine validate で NuGet 復元
  -> C# 言語サービスを再読み込み
  -> 言語サービスが補完用参照を再構成
```

## 9. エラー契約

| 事象 | 結果 |
| --- | --- |
| nullable 関連の `CS8600` から `CS8655` が発生する | `SCRIPT_COMPILE_FAILED` |
| 対象外の通常警告だけが発生する | コンパイル成功を維持する |
| NuGet 復元に失敗する | `SCRIPT_NUGET_RESTORE_FAILED` |
| 固定 version ではない NuGet 参照 | `SCRIPT_REFERENCE_NOT_ALLOWED` |
| 許可一覧にない NuGet 参照 | `SCRIPT_REFERENCE_NOT_ALLOWED` |
| 必須のロックがない | `SCRIPT_NUGET_LOCK_MISSING` |
| ロック内容が解決結果と一致しない | `SCRIPT_NUGET_LOCK_MISMATCH` |
| エディタ補完が更新されない | workflow 結果には変換せず、設定と再読み込み手順で扱う |

エディタ補完の失敗は、エンジンの実行結果ではないため新しい `WorkflowErrorCodes` を追加しない。

## 10. 検査方針

### 10.1 コンパイル診断

少なくとも次を自動検査する。

- `#nullable enable` なしで nullable annotation を使うと `Execute` が `SCRIPT_COMPILE_FAILED` になる
- 同じ script を `Validate` しても `SCRIPT_COMPILE_FAILED` になる
- エラーメッセージに `CS8632` が含まれる
- `#nullable enable` を追加した同等 script は成功する
- nullable 関連診断を発生させない script は `#nullable enable` なしでも成功する
- 対象範囲外の通常警告だけでは失敗しない
- 構文エラーなど既存のコンパイルエラー契約を維持する

### 10.2 キャッシュ

通常検査は外部ネットワークに依存させない。

- 独自 provider へ `DotnetScriptCachePath` が伝達される
- `DotnetScriptCachePath = null` の既定経路を確認する
- `DOTNET_SCRIPT_CACHE_LOCATION` を一時ディレクトリへ設定した統合検査を用意する
- ローカル NuGet source を使う追加検証で復元結果を確認する
- 既定経路で workflow root 配下に `dotnet-script/` が作られないことを確認する
- `Execute` と `Validate` が同じキャッシュ設定を使うことを確認する
- NuGet ロック、許可一覧、`#load "nuget: ..."` の既存検査を回帰確認する

環境変数を変更する検査は並列実行による干渉を避ける。必要であれば同一 collection にまとめるか、検査単位の直列化を行う。

### 10.3 エディタ補完

エディタ補完は外部プロセスを含むため、通常の `dotnet test` では完全自動化しない。

- サンプルに `omnisharp.json` が存在し、JSON 構造が期待値と一致することは自動検査する
- ローカル NuGet source または取得済みパッケージを使い、`engine validate` が成功することを統合検査する
- 実際の補完は手動検証として、設定前、検証後、言語サービス再読み込み後を記録する
- 手動検証では Engine API と第三者 NuGet API の型補完を確認する

## 11. 利用者への影響

nullable 関連診断がある `.csx` は、従来のエンジンでは成功していても対応後は `SCRIPT_COMPILE_FAILED` になる場合がある。これは `dotnet-script` 2.0.1 との意図した互換変更である。

既存 script の移行方法は次のいずれかとする。

- `#nullable enable` を追加し、nullable annotation と実装を整合させる
- nullable annotation を使わない
- Roslyn の標準機構で対象診断を明示的に扱う

ワークフロー配下に旧版の `dotnet-script/` が残っている場合、対応後の実行では既定利用しない。不要であれば利用者が削除できる。

## 12. task 分解

実装は次の順に分ける。

### T68 設計

- 本書を追加する
- 現行実装、`Dotnet.Script.Core` 2.0.1、エディタ補完の責務境界を記録する
- 実装と検査の完了条件を確定する

### T69 コンパイル診断互換

- nullable 関連診断の昇格ヘルパーを追加する
- `Execute` と `Validate` を共通化する
- 利用者目線の失敗検査と回帰検査を追加する

### T70 キャッシュ位置の整合

- `DotnetScriptCachePath` を公開オプションと request に追加する
- 既定 provider へ伝達する
- 標準キャッシュ位置と環境変数を検査する
- workflow root 配下の生成物が既定では作られないことを確認する

### T71 エディタ補完手順

- サンプルへ `omnisharp.json` を追加する
- README へ `engine validate` と再読み込み手順を追加する
- 自動検査と手動検証記録を追加する

### T72 統合検証

- solution 全体、format、Markdown、差分検査を実行する
- 既存失敗がある場合は今回起因か分類する
- 課題 #20 の追跡を同期し、実装用の取り込み依頼を作成する

## 13. 受入条件

設計全体の完了条件は次のとおりとする。

- `Execute` と `Validate` が `CS8600` から `CS8655` を同じ規則でエラー扱いする
- `#nullable enable` の欠落自体ではなく、発生した nullable 関連診断に基づいて失敗する
- 通常警告の扱いを変更しない
- 既定 NuGet 復元で workflow root 配下に `dotnet-script/` を作らない
- `DOTNET_SCRIPT_CACHE_LOCATION` と明示 `DotnetScriptCachePath` を利用できる
- NuGet 許可、固定 version、ロック、NuGet script package の契約を維持する
- サンプルと文書に `omnisharp.json`、`engine validate`、言語サービス再読み込み手順がある
- エディタ補完は設定と再読み込みを含む条件付き保証であり、エンジン単独の即時保証ではないことが明記される
- 通常検査は外部ネットワークを必須にしない
