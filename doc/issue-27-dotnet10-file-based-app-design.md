# 課題 #27 .NET 10 file-based C# workflow 設計

## 1. 位置付けと判定

本書は課題 #27 の設計レビュー指摘 5 項目を反映し、製品コード実装前に `.cs` 読み込み契約を固定する。既存 `.csx` は従来どおり維持する。

**採用案は B: .NET 10 ホスト内で class-only Library をロードする方式。** CLI / Engine と参照用パッケージは `net8.0` と `net10.0` の同一パッケージ内マルチターゲットとし、既存の `engine` コマンドとパッケージ ID を保つ。既存 `.csx` は両 TFM 資産で処理する。新規 `.cs` は `net10.0` 資産で処理し、`net8.0` 資産では SDK 10 が共存していても restore 前に拒否する。新しいコマンド名、SDK 同梱、別ワーカーパッケージは追加しない。

現行コードは `.csx` の Roslyn Script を実行して変数中の `CompositeStep<T>` を見つける。`.csx validate` もスクリプトを実行する。この動作は変更しない。`.cs validate` は別のメタデータ専用経路とし、Provider、Step、モジュール初期化子を実行しない。

関連課題: [Issue #27](https://github.com/ssaattww/Devo6.WorkFlow/issues/27)。設計の合意方向は [Issue #27 の合意コメント](https://github.com/ssaattww/Devo6.WorkFlow/issues/27#issuecomment-7131666315)。

## 2. 現行契約

- `Directory.Build.props` の共通 TFM は `net8.0`。CLI は `Devo6.WorkFlow.Cli` の .NET ツールで、コマンド名は `engine`。
- `CsxEntryLoader` は `IStep<TOut>`、`IAsyncStep<TOut>`、`CompositeStep.Define(...).Run<...>()` を用いる。Entry は名前付き `CompositeStep<T>` として解決され、CLI は Config、ログ、設定値を `WorkflowExecutionOptions` に渡す。
- Entry 名は大文字小文字を区別する。完全修飾名を先に照合し、ルート Entry は短い名前でも照合する。名前空間付き Entry の短い名前は候補が一つだけのときに解決し、複数候補なら完全修飾名を要求する。
- `CompositeStep<T>.ExecuteWorkflow(...)` が公開実行入口。標準 Config の準備は `CsxEntryLoader` 内で実施されているため、`.cs` 経路でも同じ処理を共有できるよう抽出する。
- SDK の file-based `OutputType=Library` は class-only `.cs` をビルドできる。上流 `Build_Library` テストはこのビルド成功と、その Library を `dotnet run` できないことを確認している。[SDK テスト](https://github.com/dotnet/sdk/blob/14f25e6853d517655d20de117132d8b6e2dd41ad/test/dotnet.Tests/CommandTests/Run/RunFileTests_BuildCommands.cs#L366-L386)

## 3. 方式比較

| 方式 | TFM / 型共有 | 利点 | 採否 |
|---|---|---|---|
| A. .NET 8 ホスト互換 Library | `.NET 10 SDK` で `net8.0` Library をビルドし、既存ホストでロード | 既存ホストと型を共有しやすい。SDK と実行 TFM を分離できる | 比較・互換検証のみ。製品の `.cs` 対象 TFM にはしない |
| B. .NET 10 対応ホスト | `net10.0` Library を `net10.0` CLI / Engine が同一プロセスでロード | `CompositeStep<T>`、Config、Engine 実行経路を直接共有できる。`.NET 10` 対応という目的を満たす | **採用** |
| C. .NET 8 CLI と .NET 10 ワーカーの別プロセス | ワーカー内で Library のロードから workflow 実行までを完結 | ツールの起動 TFM と `.cs` 実行 TFM を分離できる | 初期対応では見送り。任意の CLR 型を境界越しに渡せず、Config、ログ、結果、終了状態、キャンセルのプロトコルと追加配布物が必要。別プロセスだけではサンドボックスにならない |

B を選ぶ理由は、既存の型付き workflow 契約と Config 処理を再利用でき、境界越しの独自シリアライズを追加しないためである。A は `.NET 10 SDK` でビルドするだけでは `net10.0` の利用を意味しない。C は将来、ホストとユーザーコードをプロセス境界で分離する要件が生じた場合に再検討する。

## 4. 実運用の最小構成

### 4.1 SDK と配布

- `.cs` は SDK 10 の file-based app として `dotnet restore <file.cs>`、`dotnet build <file.cs> --no-restore` で処理する。SDK は実行環境に別途導入済みであることを要件とし、Engine、NuGet ツール、起動スクリプトから自動導入しない。
- SDK の基準版は `10.0.404`。ワークフロー root またはその親の `global.json` で選び、同じ feature band 内の最新パッチだけへ進める。CLI は子 `dotnet` の作業ディレクトリを `.cs` のディレクトリに設定し、同ファイル階層の SDK 選択を適用する。選択した SDK がない場合は復元前に失敗し、SDK を取得・切替しない。

```json
{
  "sdk": {
    "version": "10.0.404",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

- 現行パッケージ ID とコマンド `engine` を維持し、CLI、Engine、Abstractions を `net8.0;net10.0` でビルドする。参照用 Engine パッケージも同じ ID のまま両 TFM の資産を含める。既存 `.csx` は両資産で使える。新規 `.cs` は `net10.0` ホスト資産のみ受け付け、`net8.0` ホスト資産では、SDK 10 が共存していても SDK 選択・restore の前に「.NET 10 ホストが必要」という識別可能なエラーで拒否する。
- tool package は `net8.0` と `net10.0` の両 framework asset を含める。`dotnet tool install/update` の通常選択で環境に適した framework asset を選び、必要なら同コマンドの `--framework net8.0` / `--framework net10.0` で固定する。8-only 環境の install/update は `net8.0`、10 環境は `net10.0` を選ぶことを受入試験で確認する。既存 `.csx` は両 asset で実行できる。新規 `.cs` は `net10.0` asset のみで処理し、`net8.0` asset からは SDK 10 が共存していても restore 前に拒否する。
- 入力 Library と .NET 10 ホストは JIT 実行を前提とし、AOT 公開を無効にする。ユーザー `.cs` でも `PublishAot=false` を要求する。AssemblyLoadContext から実行時にロードする設計と AOT 公開は組み合わせない。
- `.NET 10 SDK` が file-based app に導入された時期とサポート対象は変化する。基準版と同一 feature band の選択規則は設計時点の Microsoft 公式配布情報に基づく。[.NET SDK version selection](https://learn.microsoft.com/dotnet/core/versions/selection)、[.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

### 4.2 入力例

初期対応は単一 `.cs` ファイルに限定する。クラス定義を同じファイルに置き、SDK がアプリ Main を要求しない Library としてビルドする。トップレベル文は書かない。

```csharp
#:property OutputType=Library
#:property TargetFramework=net10.0
#:property PublishAot=false
#:package Devo6.WorkFlow.Engine@[0.1.0]

using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

public sealed class GreetingStep : IStep<string>
{
    public string Execute(StepInput input) => "hello";
}

[WorkflowEntry("Main")]
public sealed class MainWorkflow : IWorkflowEntryProvider
{
    public IWorkflowEntry Create() =>
        CompositeStep.Define("Main").Run<GreetingStep, string>();
}
```

`IWorkflowEntry`、`IWorkflowEntryProvider`、`WorkflowEntryAttribute` は Engine の公開 API であり、`CompositeStep<T>` は `IWorkflowEntry` を実装する。Provider の公開・非抽象・非ジェネリック要件と公開引数なし生成を契約に含める。この最小例も `Devo6.WorkFlow.Engine` 自身が推移外部依存を持つため、`devo6.nuget.lock.yaml` を必須とする。lock がない場合は `SCRIPT_NUGET_LOCK_MISSING` で初回 restore を一度も開始せず終了する。

```sh
engine validate ./main.cs --entry Main --workflow-config ./appsettings.yaml
engine run ./main.cs --entry Main --workflow-config ./appsettings.yaml
```

既存 `.csx` のオプション、パス基準、結果コード、CLI コマンドは変えない。`.cs` の相対 Config パスも `.cs` ファイルの親ディレクトリ基準とする。

## 5. Entry と公開型契約

- 候補は入力 Library 本体に定義された公開型だけとする。依存アセンブリからの Provider 自動探索、名前規約による候補追加、複数ファイルの自動取り込みはしない。
- 候補型は `WorkflowEntryAttribute` が付いた公開・具象・非ジェネリック型で、`IWorkflowEntryProvider` を実装し、公開引数なしコンストラクターを持つ sealed class とする。属性の名前空間名と Entry 名から現在の `.csx` と同じ `QualifiedName` を作る。
- Entry 名の照合は大文字小文字を区別する序数比較。完全修飾名、名前空間なしのルート名、唯一の短縮名という現行優先順を適用する。属性の完全修飾名が重複する場合、候補が見つからない場合、短縮名が曖昧な場合は Provider を作らず失敗する。
- `validate` は属性・型・実装インターフェイス・コンストラクター・候補名を確認する。候補不在、重複、対象外の型形状、要求名不一致を Provider 生成前に検出する。
- `run` は選択済み Provider を一度だけ生成し、`Create()` を一度だけ呼ぶ。返却値はホストと同一の `IWorkflowEntry` であり、`CompositeStep<T>` の実体であることと、`QualifiedName` が属性宣言に一致することを Step 実行前に確認する。返却名不一致や Provider 例外では Step を実行しない。
- `Create()` は純粋な定義構築を行う契約とするが、実装で副作用を完全には防げない。呼び出しは `run` に限定し、入力と Provider は信頼済みとして扱う。

## 6. 型同一性と依存解決

- package version の検査は新規 `.cs` 経路にだけ適用し、入力形式を選別する前には実行しない。既存 `.csx` の CLI / 埋め込み実行は従来経路を保ち、tool store や `.nuspec` を参照できなくても止めない。
- CLI の `.cs` 経路では、実行中の tool package のインストール情報から `Devo6.WorkFlow.Cli` の実際の NuGet package version を取得する。NuGet tool store の package root にある当該 `.nuspec` の `<metadata><version>` を読む。`.cs` を選択した後にその metadata を解決できなければ `PACKAGE_VERSION_UNKNOWN` で停止し、CLR assembly version / informational version を代用しない。現行 publish workflow は CLI と Engine の両方を同じ `PackageVersion` で pack するため、これが Engine の配布版でもある。
- 公開埋め込み API でも version 検査は新規 `.cs` overload にだけ適用する。既存 `.csx` overload の引数・実行は維持し、ファイル形式の分岐前に `hostPackageVersion` を要求しない。新規 `.cs` overload は `hostPackageVersion` を必須引数として受ける。例えば `Validate(input, hostPackageVersion: "1.2.3", ...)` / `Run(input, hostPackageVersion: "1.2.3", ...)` とし、呼出元は自身が実際に配布した Devo6.WorkFlow パッケージの version metadata を渡す。新規 `.cs` 呼出しで引数欠落・空・不正な NuGet version は `PACKAGE_VERSION_UNKNOWN` で停止し、assembly version から補完しない。
- 両経路で file-based app の restore 後、生成された `project.assets.json` の `libraries` から `Devo6.WorkFlow.Engine/<resolved-version>` を読み、CLI では取得した tool package version、埋め込み API では必須引数の version と比較する。値が異なる場合は CLR assembly version / identity が一致していても `SCRIPT_API_IDENTITY_MISMATCH` として Provider 生成前に拒否する。`Devo6.WorkFlow.Abstractions` も Engine package に同梱されるホスト API と CLR identity が一致することを検査する。
- `run` の AssemblyLoadContext はホストの Engine / Abstractions アセンブリを返し、コピーを別ロードコンテキストへ重複ロードしない。その他の依存 DLL はビルド成果物の依存情報で解決する。参照のアセンブリ名・版または公開鍵情報がホストと違う場合は `SCRIPT_API_IDENTITY_MISMATCH` 相当で拒否する。同一の選択結果と型契約を CLI と公開埋め込み API が共有し、呼出元が独自ロードした Engine 型を受け入れない。
- `validate` はアセンブリを実行用 AssemblyLoadContext にロードしない。`System.Reflection.PortableExecutable.PEReader` / `System.Reflection.Metadata.MetadataReader` で成果物と参照メタデータを読み、Entry 属性引数と公開 Provider の形を確認する。属性コンストラクターは呼び出さず、モジュール初期化子、型初期化子、Provider、Step は一切ロード・実行しない。
- `PEReader` は実行しないメタデータ検査に使う。追加 NuGet パッケージとして `MetadataLoadContext` を入れず、.NET 10 ホストの参照可能な `System.Reflection.Metadata` を使う。参照できない環境は SDK/ホスト構成エラーとして明示し、別方式へ黙って切り替えない。

## 7. 信頼境界と依存ロック

`engine validate main.cs` と `engine run main.cs` は、指定した入力とビルド環境を利用者が信頼していることを前提とする。`validate` がワークフロー本体を実行しないことは保証するが、SDK の restore/build で処理される MSBuild 定義、analyzer、source generator、NuGet パッケージが信頼できることまでは保証しない。未信頼のソースに対する安全な実行環境として説明しない。

復元の前に、次を静的に検査する。

1. 入力ファイル、依存するユーザー設定、SDK 選択結果が明示された workflow root の信頼範囲内にあること。自動探索・自動ビルドはしない。
2. 認める file-based directives は `OutputType=Library`、`TargetFramework=net10.0`、`PublishAot=false`、厳密な一点版を持つ `#:package` のみ。NuGet の下限指定として解釈される裸の `@1.2.3` は拒否し、`#:package PackageId@[1.2.3]` を許可する。SDK が生成した実効 `PackageReference` を初回 restore より前に評価し、各 Version が厳密範囲 `[x.y.z]` と一致することを確認する。`#:sdk`、`#:include`、`#:project`、`#:ref`、他の MSBuild property、重複値の競合は初期対応で拒否する。ヘルパー型は同じ `.cs` 内に定義する。入口に使える型は属性付きの公開クラスで、継承不可、非抽象、型引数なしとし、`IWorkflowEntryProvider` を直接実装する。公開で引数を取らない `.ctor` と公開 `Create()` も、そのクラスに直接定義する。基底型または依存先から契約や `Create()` を引き継ぐ形は初期対応で受け入れない。該当する型は復元前の入力検査では拒否せず、生成完了後のメタデータ検証で拒否する。
3. `dotnet --version` で選択版を確認し、`dotnet --info` を読む子プロセスだけ `DOTNET_CLI_UI_LANGUAGE=en-US` に設定して、`Base Path` から選択された配置先を得る。呼出元の設定は変更しない。同じ版が複数の配置先にあっても、一覧の版番号だけでは決めず、選択された場所を使う。選択版が基準版であり、MSBuild の実効値が `net10.0`、Library、AOT 無効であること。継承する `global.json`、`Directory.Build.props/targets`、`Directory.Packages.props`、`NuGet.Config` の値と package source を復元前に確認する。実効値を評価できない設定、許可していない target/import、未信頼 source は復元前に拒否する。
4. 実効 `PackageReference` はすべて厳密な一点版であり、可変版、範囲、`*` を restore 前に拒否する。`--allow-nuget` を指定したときは現在の CLI と同じ許可リスト意味で直接参照を照合し、不一致は初回 restore 前に拒否する。構成された source に要求一点版がなく、上位版だけがある場合は上位版へ置き換えず、NuGet の初回 restore を失敗させて build 前に停止する。restore が成功した場合も `project.assets.json` の実解決版が要求一点版と完全一致することを確認し、不一致は build 前に拒否する。
5. `devo6.nuget.lock.yaml` がある場合は target framework、直接参照、source、全推移依存グラフを検査する。Engine API パッケージは唯一許可する直接 API 参照とし、それ以外の直接外部依存は lock に直接・推移依存すべてが記録されている場合に限り許可する。Engine API パッケージ自身が推移外部依存を持つため、新規 `.cs` では Engine API だけを直接参照する場合も専用 lock を必須とする。Engine API の推移外部依存を含め、実際に復元される外部パッケージは全て lock で固定する。lock がなければ `SCRIPT_NUGET_LOCK_MISSING` として初回 restore を開始する前に失敗する。`--locked` 指定時も lock 欠落は初回 restore 前に失敗する。lock がある場合は restore 前に TFM、直接参照、source を照合する。

静的事前検査を完了してから初回 restore を一度だけ行う。復元済み `project.assets.json` の実効参照と全推移依存グラフを lock と照合し、要求一点版からの上位版へのすり替わり、未記録の追加外部依存、lock 不一致があれば build へ進まない。一致後は `dotnet build --no-restore` でビルドし、二度目の restore を行わない。`--locked` は lock 必須かつ全グラフ一致、`--allow-nuget` は指定された直接参照の許可集合という意味で `.csx` と揃える。`.csx` の既存ロック任意動作は維持するが、新規 `.cs` では追加外部依存に lock を必須とする。

## 8. `validate` / `run` の順序と失敗

### `validate`

```text
入力・Entry・SDK 選択を確認
  -> Config path の存在確認
  -> directives / 実効 MSBuild / source / fixed direct dependencies / lock を restore 前検査
  -> dotnet restore（必要な場合のみ、1 回）
  -> assets の推移依存グラフと lock を照合
  -> dotnet build --no-restore
  -> PEReader / MetadataReader で Entry と API 参照を確認
  -> ValidationError を返す
```

指定された workflow Config が存在しない場合は `CONFIG_NOT_FOUND` で restore 前に失敗する。Config の型変換や Provider 生成はしない。Config path が未指定である場合に必要 Config の有無を断定しない。必要性と Config 型は `run` で生成された `CompositeStep` の既存 metadata を使って判定する。

### `run`

```text
validate と同じ復元前検査・restore・lock 照合・build
  -> Engine / Abstractions の完全一致を確認
  -> 選択 Provider を一度だけ生成して Create()
  -> 返却 Entry 名を照合
  -> 既存の標準 Config / Step Config 読み込みを適用
  -> ExecuteWorkflow を呼ぶ
```

| 状況 | 結果 |
|---|---|
| 入力なし、SDK 不在、禁止 directive / MSBuild 設定、可変依存版、許可リスト不一致 | restore 前に識別コードと対象パスを返す |
| `--locked` でロック欠落 | `SCRIPT_NUGET_LOCK_MISSING`、restore しない |
| restore 失敗、直接依存の restore 失敗 | `SCRIPT_NUGET_RESTORE_FAILED` 相当。ビルド・Provider 呼び出し・Step 実行をしない |
| 依存グラフのロック不一致 | `SCRIPT_NUGET_LOCK_MISMATCH`、ビルドしない |
| C# compile 失敗 | `SCRIPT_COMPILE_FAILED` 相当。SDK の ID、path、行、列、診断を保持し、ロードしない |
| MSBuild / build 自体の失敗 | `SCRIPT_BUILD_FAILED`。SDK 診断を保持し、Provider 生成前に終了 |
| Entry 不在・重複・曖昧名・非対応 Provider 型 | 既存 `ENTRY_STEP_NOT_FOUND` / `DUPLICATE_STEP_NAME` 相当または Provider 型診断。Provider 生成前に失敗 |
| API 型・NuGet 版不一致、依存 DLL 解決失敗 | `SCRIPT_API_IDENTITY_MISMATCH` / `SCRIPT_LOAD_FAILED` 相当。Provider 生成前に失敗 |
| Provider が返した Entry 名の不一致 | Entry 解決エラー。Config と Step の実行前に失敗 |

既存 `.csx` 用エラーコードと終了コードは変更しない。`.cs` 専用の SDK 不在、禁止 directive、実効 TFM 不一致には安定した診断コードを追加し、CLI は従来どおり成功 `0`、失敗非 `0` とする。

## 9. 一時生成物とキャッシュ

- SDK file-based app の標準一時出力を使い、workflow source root へ `bin/obj` や DLL を作らない。
- キャッシュキーには入力ソース hash、許可された directive、SDK version、実効 TFM、workflow root の build/NuGet 設定、復元済み package lock を含める。source/config が変わったときに古い成果物を再利用しない。
- SDK file-based build の実出力先は入力パス由来なので、キャッシュキーとは別に実際の出力先パス単位の排他ロックを取る。ロックは restore 開始前から assets 検査、build、成果物取得、validate 完了または run 完了まで保持し、失敗・キャンセル時にも解放する。同一入力パスへの設定違いの要求はこのロックで直列化する。
- 検証と実行の間に source hash が変わった場合は成果物を使わず、再度入力を解決する。

## 10. 受入テスト表

| ID | 経路 | 入力・操作 | 受入条件 |
|---|---|---|---|
| A | SDK Library | `OutputType=Library`、class-only `.cs`、`net10.0` を SDK 10 で build | Main なしで成功。`dotnet run` には渡さず Library のみ生成する |
| B | SDK 選択 | root に `global.json`、10.0.404 と同一 feature band の patch、他 feature band、SDK 不在 | 同一 feature band の patch を選択し、他 feature band / SDK 不在では restore 前に診断。自動導入しない |
| C | 配布 | 同じ `Devo6.WorkFlow.Cli` パッケージの install/update を .NET 8 のみ、.NET 10、両方の環境で実施 | 通常の framework 選択と `--framework` 明示選択を確認。8 のみは net8 asset + 既存 `.csx`、10 は net10 asset + `.cs` と既存 `.csx`。10 SDK が共存する net8 asset でも `.cs` は restore 前に拒否。同一パッケージ ID、コマンド `engine` のみ |
| D | 型同一性 | CLI と公開埋め込み API の両方から Provider と Step を解決する | 両経路で `IsAssignableFrom` と Entry marker 型がホストと同一。CLR assembly version が同じでも NuGet package version が違えば Provider 生成前に失敗 |
| E | validate 非実行 | モジュール初期化子、静的初期化子、属性コンストラクター、Provider constructor / `Create`、Step に個別 marker を書く | `engine validate` 後も全 marker が存在しない。実行用 AssemblyLoadContext に成果物をロードしない |
| F | Config 検証 | 存在する / 存在しない `--workflow-config` を指定 | 存在確認は restore 前に行い、欠落は `CONFIG_NOT_FOUND`。Config 型変換・Provider 呼び出しはしない |
| G | Entry | exact/short/qualified、大小文字差、Entry 不在、重複、曖昧短縮名、無効 Provider、Create 名不一致 | 現行 `.csx` の照合規則と一致。metadata で検出可能な異常は Provider 生成前、Create 返却値の異常は Step 実行前に失敗 |
| H | 依存前検査 | 可変版、禁止 directive、MSBuild TFM 上書き、未許可 source、`--allow-nuget` 不一致 | 最初の restore が呼ばれないことをログと fake restore stamp で確認 |
| I | lock / package version | Engine のみを直接参照する最小例で lock 欠落、追加外部依存の lock 欠落、`--locked` 欠落、要求一点版がなく上位版だけの source、直接参照不一致、transitive 不一致、正しい lock | Engine のみの最小例も `SCRIPT_NUGET_LOCK_MISSING` で初回 restore を一度も始めず失敗。追加外部依存の必須 lock 欠落・不正指定・直接不一致も初回 restore 前に拒否。要求一点版が source にない場合は初回 restore 自体が失敗し、上位版へ置換せず build に進まない。推移 graph 不一致は restore 後・build 前に失敗。正しい lock の後の build は `--no-restore` |
| J | run | 正常 Provider と Step、Provider/Entry の例外、同一入力の連続実行 | `Create` は選択した一度だけ、Step は `run` のみ実行。結果・trace・Config・ログは既存契約と一致 |
| K | 既存 CLI 回帰 | .NET 8 のみで `engine run/validate` の `.csx`、`#load`、NuGet allow/lock、Config 相対パス、既存不正入力。tool store / `.nuspec` と package version metadata が使えない条件も含める | CLI と埋め込み API の既存 `.csx` 経路は版照合のため分岐前に停止せず実行し、既存結果、診断、終了コード、相対パス基準を維持する |
| L | 出力と並行実行 | 同一入力パスへ異なる package source / lock 設定を指定し、validate/run を並行起動する | 実出力先単位の排他により restore、assets 検査、build、成果物取得、実行が交錯せず、各要求は自身の設定で完了する。source tree を汚さない |
| M | 失敗診断 | 不正 C# compile、restore 失敗、build 失敗、依存 DLL 欠落をそれぞれ起こす | compile は `SCRIPT_COMPILE_FAILED`、restore は `SCRIPT_NUGET_RESTORE_FAILED`、MSBuild / build は `SCRIPT_BUILD_FAILED`、DLL 欠落は `SCRIPT_LOAD_FAILED` 相当。診断と原因箇所を保持し、全ケースで非 0 終了、Provider / Step は実行されない |

## 11. 対象外

- `.csx` の評価方式、NuGet 解決、API 契約、CLI 既存診断の変更。
- `.cs` の通常アプリ Main、トップレベル文、複数ファイル `#:include`、Project/SDK 任意参照、実行時アプリ起動。
- .NET SDK のダウンロード、同梱、グローバルなパッケージ追加、別プロセス worker。
- 未信頼のソース、NuGet package、MSBuild 定義を実行できるサンドボックス。
- `validate` 時の Provider 登録コード実行や Step/Config の型変換。
- `net8.0` class-only input の製品サポート。A 案は比較テストに限定する。

## 12. 実測状況

上流 SDK の `Build_Library` テストは `OutputType=Library` と class-only `.cs` の build 成功、および `dotnet run` の不可を示す。この cloud checkout には .NET SDK がなく、SDK 導入は行っていない。Devo6 の生成 Library、SDK 選択、マルチターゲット tool install/update、型同一性、PEReader による副作用非実行、restore/lock、既存 CLI 回帰はすべて**未実測**であり、本書の受入テストに残す。
