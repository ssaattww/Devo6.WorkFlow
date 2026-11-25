# 課題 #27 .NET 10 のファイル単位 C# ワークフロー設計

## 1. 位置付けと判定

本書は、製品コードの実装前に設計点検5項目を反映し、`.cs` 入力の読込み契約を定める。既存の `.csx` は従来どおり維持する。

**採用案は B: .NET 10 対応ホストで、クラス定義のみのライブラリを読み込む方式。** コマンド実行部、実行エンジン、参照用パッケージを `net8.0` と `net10.0` の複数対象向けにし、既存の `engine` コマンドとパッケージ ID を保つ。既存 `.csx` はどちらの対象実行環境でも処理する。新しい `.cs` は `net10.0` の資産だけで処理し、`net8.0` 資産では開発基盤の版が共存していても復元を始める前に拒否する。新コマンド、開発基盤の同梱、別の実行用パッケージは追加しない。

現在の `.csx` 経路は Roslyn の入力コードを実行し、変数に含まれる `CompositeStep<T>` を見つける。`.csx` の `validate` も入力を実行する。この動作は変えない。`.cs` の `validate` はメタデータだけを調べる別経路とし、提供型と処理を実行せず、読込み単位の初期化処理もその定義方法を問わず実行しない。

関連する [課題 #27](https://github.com/ssaattww/Devo6.WorkFlow/issues/27) と[設計合意コメント](https://github.com/ssaattww/Devo6.WorkFlow/issues/27#issuecomment-7131666315)。

## 2. 現行契約

- `Directory.Build.props` の共通対象実行環境は `net8.0`。コマンド実行部は `Devo6.WorkFlow.Cli` の .NET ツールで、コマンド名は `engine`。
- `CsxEntryLoader` は `IStep<TOut>`、`IAsyncStep<TOut>`、`CompositeStep.Define(...).Run<...>()` を使う。入口は名前付き `CompositeStep<T>` として解決し、コマンド実行部は設定、ログ、設定値を `WorkflowExecutionOptions` に渡す。
- 入口名は大文字小文字を区別する。完全修飾名を先に照合し、名前空間を持たない最上位の入口は短い名前でも照合する。名前空間付き入口の短縮名は候補が一つだけなら解決し、複数候補があれば完全修飾名を要求する。
- `CompositeStep<T>.ExecuteWorkflow(...)` が公開実行入口。標準設定の準備は `CsxEntryLoader` 内で行うため、`.cs` 経路と共有できるよう抽出する。
- 開発基盤のファイル単位入力は `OutputType=Library` を指定して、クラス定義だけの `.cs` からライブラリを生成できる。上流の [`Build_Library` 試験](https://github.com/dotnet/sdk/blob/14f25e6853d517655d20de117132d8b6e2dd41ad/test/dotnet.Tests/CommandTests/Run/RunFileTests_BuildCommands.cs#L366-L386)は、この生成が成功し、そのライブラリを `dotnet run` では起動できないことを確認している。

## 3. 方式比較

| 方式 | 対象実行環境と型共有 | 利点 | 判定 |
|---|---|---|---|
| A. .NET 8 ホストと互換のライブラリ | .NET 10 の開発基盤で `net8.0` 向けを生成し、既存ホストで読み込む | 既存ホストと型を共有しやすい。開発基盤と実行環境を分けられる | 比較・互換確認に限る。製品の `.cs` 対応対象にはしない |
| B. .NET 10 対応ホスト | `net10.0` 向けを `net10.0` のコマンド実行部と実行エンジンが同一プロセスで読み込む | `CompositeStep<T>`、設定、エンジン実行経路を直接共有できる。.NET 10 対応という目的を満たす | **採用** |
| C. .NET 8 コマンド実行部と .NET 10 の別プロセス実行部 | 別プロセス内でライブラリの読込みからワークフロー実行まで完結する | 起動側と `.cs` の実行環境を分けられる | 初期対応では見送る。任意の CLR 型を境界越しに渡せず、設定、ログ、結果、終了状態、キャンセルの通信手順と追加配布物が必要。別プロセスだけでは隔離環境にならない |

B は、既存の型付きワークフロー契約と設定処理を再利用でき、境界越しの独自シリアライズが不要なため選ぶ。A は .NET 10 の開発基盤で生成しても、実行対象が `net10.0` になるわけではない。C は、ホストと利用者のコードをプロセス境界で分ける要件が生じたときに再検討する。

## 4. 実運用の最小構成

### 4.1 開発基盤と配布

- `.cs` は開発基盤10のファイル単位入力として `dotnet restore <file.cs>`、`dotnet build <file.cs> --no-restore` で処理する。開発基盤は実行環境に別途導入済みであることを要件とし、実行エンジン、NuGet ツール、起動用ファイルから自動導入しない。
- 開発基盤の基準版は `10.0.404`。ワークフローの基準ディレクトリまたはその親にある `global.json` で選び、同じ機能帯の最新修正版だけを許す。コマンド実行部は子 `dotnet` の作業ディレクトリを `.cs` のある場所にし、その階層にある開発基盤の選択規則を適用する。選択可能な版がなければ復元前に失敗し、取得や切替は行わない。

```json
{
  "sdk": {
    "version": "10.0.404",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

- 既存のパッケージ ID と `engine` コマンドを維持し、コマンド実行部、実行エンジン、共通型を `net8.0;net10.0` 向けに生成する。参照用の実行エンジン用パッケージも同じ ID のまま、両方の実行環境向け資産を含める。既存 `.csx` はどちらでも利用可能にする。新しい `.cs` は `net10.0` 資産だけで受け付ける。`net8.0` 資産では開発基盤10が共存していても、版の選択・復元前に「.NET 10 ホストが必要」と判別できるエラーで拒否する。
- コマンド用パッケージは `net8.0` と `net10.0` 向けの資産を含める。`dotnet tool install` と `dotnet tool update` の通常選択と、各コマンドの `--framework net8.0` / `--framework net10.0` による明示選択を受入試験で確認する。.NET 8 だけの環境は `net8.0`、.NET 10 環境は `net10.0` を選ぶ。既存 `.csx` は両方で処理し、新しい `.cs` は `net10.0` 資産だけで処理する。`net8.0` 資産は開発基盤10が共存していても、復元前に `.cs` を拒否する。
- 入力ライブラリと .NET 10 ホストは実行時の機械語変換を前提とする。`AssemblyLoadContext` で入力ライブラリを実行時に読み込むため、[事前変換を行う公開方式のうち `PublishAot` が制御する方式](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)だけを無効にする。利用者の `.cs` にも `PublishAot=false` を要求する。他の事前変換方式まで禁止する規則ではない。
- ファイル単位入力が開発基盤に導入された時期とサポート対象は変化し得る。基準版と同じ機能帯から版を選ぶ規則は、設計時点の[公式選択資料](https://learn.microsoft.com/dotnet/core/versions/selection)と[.NET 10 配布資料](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)に基づく。

### 4.2 入力例

初期対応は単一の `.cs` ファイルに限る。クラス定義を同じファイルに置き、`OutputType=Library` により `Main` を必要としない形式で生成する。トップレベル文は書かない。

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

`IWorkflowEntry`、`IWorkflowEntryProvider`、`WorkflowEntryAttribute` は `Devo6.WorkFlow.Engine` の公開 API で、`CompositeStep<T>` は `IWorkflowEntry` を実装する。提供型は公開され、抽象型でも型引数付きでもなく、引数なしで公開生成できることを契約とする。この最小例も `Devo6.WorkFlow.Engine` が推移外部依存を持つため、`devo6.nuget.lock.yaml` を必須とする。固定情報がなければ `SCRIPT_NUGET_LOCK_MISSING` として、最初の復元を一度も開始せず終了する。

```sh
engine validate ./main.cs --entry Main --workflow-config ./appsettings.yaml
engine run ./main.cs --entry Main --workflow-config ./appsettings.yaml
```

既存 `.csx` のオプション、パスの基準、結果コード、`engine` コマンドは変えない。`.cs` に対する相対設定パスは `.cs` ファイルの親ディレクトリを基準にする。

## 5. 入口と公開型契約

- 候補は入力ライブラリ本体に定義された公開型だけとする。依存アセンブリからの提供型自動探索、名前規約による候補追加、複数ファイルの自動取込みはしない。
- 候補型は `WorkflowEntryAttribute` が付き、公開・具象・非ジェネリックで、`IWorkflowEntryProvider` を直接実装し、引数なしで公開生成できる継承不可のクラスとする。属性で指定する名前空間名と入口名から、現在の `.csx` と同じ `QualifiedName` を作る。
- 入口名の照合は大文字小文字を区別する序数比較とする。完全修飾名、名前空間を持たない最上位の名前、候補が一つだけの短縮名という現行の優先順を適用する。属性で示した完全修飾名が重複する、候補がない、または短縮名が曖昧な場合は、提供型を生成せず失敗する。
- `validate` は属性、型、実装契約、候補名を確認する。候補不在、重複、未対応の型形状、要求名との不一致は、提供型を生成する前に検出する。
- `run` は選んだ提供型を一度だけ生成し、`Create()` を一度だけ呼ぶ。返却値はホストと同じ `IWorkflowEntry` 型であり、`CompositeStep<T>` の実体であることと、`QualifiedName` が属性の宣言に一致することを処理実行前に確認する。返却名の不一致や提供型の例外があれば、処理を実行しない。
- `Create()` は定義の構築だけを行う契約とするが、副作用を完全には防げない。呼出しは `run` に限り、入力と提供型は信頼済みとして扱う。

## 6. 型同一性と依存解決

- パッケージの配布版照合は新しい `.cs` 経路だけに適用し、入力形式を選ぶ前には実行しない。既存 `.csx` のコマンド実行・埋め込み実行は従来経路を保つ。ツール格納場所や `.nuspec` を参照できないだけで既存経路を停止しない。
- コマンド実行側の `.cs` 経路では、実行中のツールが配布されている実際の NuGet 版を、ツール格納先の該当 `.nuspec` の `<metadata><version>` から取得する。`.cs` を選んだ後にこの値を読めない場合は `PACKAGE_VERSION_UNKNOWN` で停止し、CLR アセンブリ版や製品表示版で代用しない。現行の配布処理ではコマンド実行部と実行エンジンを同じ `PackageVersion` で梱包するため、これを実行エンジンの配布版として扱う。
- 公開埋め込み API の版照合も、新しい `.cs` 用の呼出し形式だけに適用する。既存 `.csx` 用の引数と実行は維持し、入力形式を選ぶ前に `hostPackageVersion` を要求しない。新しい `.cs` 用の呼出しは `hostPackageVersion` を必須引数とする。例は `Validate(input, hostPackageVersion: "1.2.3", ...)` と `Run(input, hostPackageVersion: "1.2.3", ...)`。呼出し側は、ホストが実際に配布した NuGet パッケージの 版情報から得た NuGet 版を渡す。パッケージ ID 文字列は渡さない。引数なし、空、不正な NuGet 版は `PACKAGE_VERSION_UNKNOWN` で停止し、アセンブリ版から補完しない。
- 両経路ともファイル単位入力の復元後、生成された `project.assets.json` の `libraries` にある `Devo6.WorkFlow.Engine/<resolved-version>` を読む。コマンド実行側はツール配布版、埋め込み API は必須引数の版と照合する。異なる場合は CLR アセンブリの版や識別情報が一致していても `SCRIPT_API_IDENTITY_MISMATCH` とし、提供型の生成前に拒否する。`Devo6.WorkFlow.Abstractions` も、ホストに含まれる同じ API と CLR 上の識別情報が一致することを調べる。
- 実行時の `AssemblyLoadContext` はホストの `Devo6.WorkFlow.Engine` と `Devo6.WorkFlow.Abstractions` を返し、別の読込み領域に複製を読み込まない。その他の依存ライブラリは生成物の依存情報から解決する。参照アセンブリの名前、版、公開鍵情報がホストと異なる場合は `SCRIPT_API_IDENTITY_MISMATCH` 相当で拒否する。コマンド実行側と公開埋め込み API は同一の選択結果と型契約を共有し、呼出し側が独自に読み込んだ実行エンジン型は受け入れない。
- `validate` は生成物を実行用の `AssemblyLoadContext` に読み込まない。`System.Reflection.PortableExecutable.PEReader` と `System.Reflection.Metadata.MetadataReader` で成果物と参照のメタデータを読み、入口属性の引数と公開提供型の形を調べる。属性の生成処理を呼ばず、型初期化子、提供型、処理を一切読み込み・実行しない。読込み単位の初期化処理も定義方法を問わず実行しない。C# の `ModuleInitializerAttribute` はその指定方法の一例である。
- 実行しないメタデータ検査には `PEReader` を使う。追加 NuGet パッケージとして `MetadataLoadContext` を導入せず、.NET 10 ホストから参照できる `System.Reflection.Metadata` を使う。参照できない環境は開発基盤またはホスト構成のエラーとして明示し、別方式へ暗黙に切り替えない。

## 7. 信頼境界と依存固定

`engine validate main.cs` と `engine run main.cs` は、指定された入力と生成環境が利用者に信頼されていることを前提とする。`validate` がワークフロー本体を実行しないことは保証する。一方、開発基盤の復元・生成で処理される構築設定、解析器、コード生成器、NuGet パッケージが信頼できることまでは保証しない。未信頼の入力に対する安全な実行環境とは説明しない。

復元を始める前に、以下を静的に調べる。

1. 入力ファイル、依存する利用者設定、開発基盤の選択結果が、明示されたワークフロー基準ディレクトリの信頼範囲内にある。自動探索や自動生成は行わない。
2. 許可するファイル指定は `OutputType=Library`、`TargetFramework=net10.0`、`PublishAot=false`、厳密な一点版の `#:package` だけとする。NuGet が下限版として扱う裸の `@1.2.3` は拒否し、`#:package PackageId@[1.2.3]` を許す。開発基盤が生成する実効 `PackageReference` を初回復元前に評価し、それぞれの版が厳密な範囲 `[x.y.z]` と一致することを確かめる。`#:sdk`、`#:include`、`#:project`、`#:ref`、許可外の構築設定、重複値の競合は初期対応で拒否する。補助型は同じ `.cs` 内に定義する。入口候補は属性付きの公開クラスとし、継承不可、非抽象、型引数なしで、`IWorkflowEntryProvider` を直接実装する。公開の `Create()` と、引数なしで公開生成できる要件もそのクラス自体に定義する。基底型や依存先から契約または `Create()` を継承する形は初期対応では受け入れない。これらの型形状は復元前の入力検査では拒否せず、生成後のメタデータ検証で拒否する。
3. `dotnet --version` で選択版を確認し、`dotnet --info` を読む子プロセスだけ `DOTNET_CLI_UI_LANGUAGE=en-US` とする。`Base Path` から選択された配置場所を得て、呼出し元の環境設定は変えない。同じ版が複数の場所にあっても、版番号だけでは判定せず選択された場所を使う。選択版が基準版で、実行時の機械語変換を前提に、構築設定の実効値が `net10.0`、ライブラリ形式、`PublishAot=false`（この項目で制御する公開方式だけを無効）であることを確認する。継承する `global.json`、`Directory.Build.props`、`Directory.Build.targets`、`Directory.Packages.props`、`NuGet.Config` の値とパッケージ取得元も復元前に確認する。実効値を評価できない設定、未許可の対象指定や設定読込み、信頼されていない取得元は復元前に拒否する。
4. 実効 `PackageReference` はすべて厳密な一点版とし、可変版、範囲、`*` は復元前に拒否する。`--allow-nuget` が指定された場合は、既存コマンドと同じ許可一覧で直接参照を照合し、不一致なら初回復元前に拒否する。設定された取得元に要求版がなく上位版だけがある場合、上位版に置き換えず初回復元を失敗させ、生成前に停止する。復元が成功した場合も、`project.assets.json` の実解決版が要求された一点版に一致することを確認し、不一致なら生成前に拒否する。
5. `devo6.nuget.lock.yaml` があれば対象実行環境、直接参照、取得元、推移依存すべてを調べる。実行エンジン API パッケージだけを直接の API 参照として許可する。他の直接外部依存は、直接分と推移分の全てが固定情報に記録されている場合だけ許可する。実行エンジン API 自体が推移外部依存を持つため、初期対応では実行エンジン API だけを直接参照する最小例にも専用固定情報が必須。実行エンジン API の推移依存を含む実際の外部パッケージは全て固定する。固定情報がなければ `SCRIPT_NUGET_LOCK_MISSING` として、初回復元前に失敗する。`--locked` 指定時も固定情報の欠落は初回復元前に失敗する。固定情報がある場合は対象実行環境、直接参照、取得元を復元前に照合する。

静的検査を終えてから初回復元を一度だけ実行する。復元済み `project.assets.json` の実効参照と推移依存すべてを固定情報と照合し、要求版から上位版へのすり替わり、未記録の外部依存追加、固定内容の不一致があれば生成へ進まない。照合後は `dotnet build --no-restore` で生成し、復元を繰り返さない。`--locked` は固定情報必須かつ全依存一致、`--allow-nuget` は指定された直接参照の許可集合という意味で `.csx` とそろえる。既存 `.csx` で依存固定情報が任意である動作は維持するが、新しい `.cs` では追加の外部依存があれば固定情報を必須とする。

## 8. `validate` / `run` の順序と失敗

### `validate`

```text
入力・入口・開発基盤の選択を確認
  -> 指定 Config の存在確認
  -> 指定行・実効構築設定・取得元・一点指定版・依存固定情報を復元前に検査
  -> dotnet restore（必要な場合のみ、1 回）
  -> 復元結果の依存すべてを固定情報と照合
  -> dotnet build --no-restore
  -> PEReader / MetadataReader で入口と API 参照を確認
  -> ValidationError を返す
```

指定されたワークフロー設定が存在しない場合は `CONFIG_NOT_FOUND` とし、復元前に失敗する。設定の型変換や提供型の生成はしない。設定パスが省略されている場合は、必要な設定の有無を断定しない。必要性と設定型は、`run` で作られた `CompositeStep` の既存メタデータを使って判断する。

### `run`

```text
validate と同じ復元前検査・復元・固定情報照合・生成
  -> 実行エンジン / 共通型の同一性を確認
  -> 選択した提供型を一度だけ生成し、Create()
  -> 返却された入口名を照合
  -> 既存の標準設定と処理設定の読込みを適用
  -> ExecuteWorkflow を呼ぶ
```

| 状況 | 結果 |
|---|---|
| 入力なし、開発基盤なし、禁止指定・構築設定、可変依存版、許可一覧との不一致 | 復元前に識別コードと対象パスを返す |
| `--locked` 指定で固定情報なし | `SCRIPT_NUGET_LOCK_MISSING`。復元しない |
| 復元失敗または直接依存の復元失敗 | `SCRIPT_NUGET_RESTORE_FAILED` 相当。生成、提供型呼出し、処理実行をしない |
| 依存固定情報との不一致 | `SCRIPT_NUGET_LOCK_MISMATCH`。生成しない |
| C# 構文・型検査の失敗 | `SCRIPT_COMPILE_FAILED` 相当。診断 ID、対象ファイルのパス、行、列を保持し、読込みしない |
| 構築基盤または生成自体の失敗 | `SCRIPT_BUILD_FAILED`。診断を保持し、提供型の生成前に終了 |
| 入口なし、重複、曖昧名、未対応の提供型 | `ENTRY_STEP_NOT_FOUND` / `DUPLICATE_STEP_NAME` 相当または提供型の診断。提供型の生成前に失敗 |
| API 型・NuGet 版の不一致、依存ライブラリの解決失敗 | `SCRIPT_API_IDENTITY_MISMATCH` / `SCRIPT_LOAD_FAILED` 相当。提供型の生成前に失敗 |
| 提供型が返した入口名の不一致 | 入口解決エラー。設定と処理の実行前に失敗 |

既存 `.csx` のエラーコードと終了コードは変更しない。`.cs` 固有の開発基盤なし、禁止指定、実効対象実行環境の不一致には安定した診断コードを設ける。コマンドの成功は `0`、失敗は非 `0` とする。

## 9. 一時生成物とキャッシュ

- 開発基盤のファイル単位入力が使う標準の一時出力を用い、入力ファイルの親ディレクトリとワークフローを定義するコード一式を置く最上位ディレクトリのどちらにも `bin/obj`、依存ライブラリ、生成された `.dll` を作らない。
- キャッシュキーには入力のハッシュ、許可した指定、開発基盤の版、実効対象実行環境、ワークフロー基準ディレクトリの構築・NuGet 設定、復元済み依存固定情報を含める。入力や設定が変わった場合に古い成果物を再利用しない。
- 入力パスごとの調整用排他を復元前に取得し、同じ入力の通常要求は順に処理する。SDK 子プロセスの終了や出力 pipe の閉鎖だけでは、その子が出力を書き終えた証明にしない。各要求は OS の一時領域に利用者専用の session directory を作り、SDK runfile、一時領域、CLI home、NuGet package / HTTP cache を分離する。SDK が実際に使う `XDG_DATA_HOME` または `TEMP` / `TMP` をその session に設定する。assets、DLL、中間出力も同じ session の runfile root に置き、実効 MSBuild 出力先を復元前に照合する。中止後に子の書込みが残っても、後続要求は別 session だけを照合・使用する。安全に終了を確認できない session は再利用・即時削除しない。
- 検証から実行までの間に入力のハッシュが変わった場合、成果物は使わず、入力を再度解決する。

## 10. 受入試験表

| ID | 経路 | 入力・操作 | 受入条件 |
|---|---|---|---|
| A | 開発基盤によるライブラリ生成 | .NET 10 の開発基盤で `OutputType=Library`、`TargetFramework=net10.0`、`PublishAot=false` を指定したクラス定義だけの `file.cs` に対して `dotnet restore file.cs` を行い、`dotnet build file.cs --no-restore` で生成 | `Main` なしで成功する。`dotnet run` では起動せず、ライブラリだけ生成する |
| B | 開発基盤の選択 | 基準ディレクトリに `global.json` を置き、`10.0.404` と同機能帯の修正版、他機能帯、開発基盤なしを用意 | 同機能帯の修正版を選択する。他機能帯や開発基盤なしは復元前に診断し、自動導入しない |
| C | 配布と選択 | 同じ `Devo6.WorkFlow.Cli` パッケージを .NET 8 のみ、.NET 10、両方の環境で導入・更新 | 通常選択と `--framework` の明示選択を確認する。.NET 8 のみでは `net8.0` 資産と既存 `.csx`、.NET 10 では `net10.0` 資産と `.cs` / `.csx` を選ぶ。開発基盤10がある .NET 8 資産からの `.cs` は復元前に拒否する。同じパッケージ ID と `engine` コマンドを保つ |
| D | 型同一性 | コマンド実行部と公開埋め込み API の両方で提供型と処理を解決 | 両経路の `IsAssignableFrom` と入口の目印型がホストと同一。CLR アセンブリ版が同じでも NuGet 版が異なる場合は提供型の生成前に失敗する |
| E | 検証時の非実行 | 読込み単位の初期化処理（`ModuleInitializerAttribute` で指定）、静的初期化子、属性の生成処理、提供型生成、`Create()`、処理に個別の目印を記録 | `engine validate` 後も目印が一つも記録されない。実行用 `AssemblyLoadContext` に成果物を読み込まない |
| F | 設定の検証 | 存在する / 存在しない `--workflow-config` を指定 | 復元前に存在を確認し、欠落は `CONFIG_NOT_FOUND`。設定変換や提供型の呼出しをしない |
| G | 入口選択 | 完全名・短縮名、大小文字差、入口なし・重複・曖昧名、無効な提供型、`Create()` の返却名不一致 | 現行 `.csx` と同じ照合規則にする。メタデータで分かる異常は提供型の生成前、返却値の異常は処理実行前に失敗する |
| H | 依存の事前検査 | 可変版、禁止指定、実効対象の上書き、未許可の取得元、`--allow-nuget` 不一致 | 最初の復元が一度も呼ばれないことを記録と模擬復元の印で確認する |
| I | 固定情報と配布版 | 実行エンジンだけを直接参照する最小例で、固定情報なし、追加依存の固定漏れ、`--locked` 固定漏れ、上位版しかない取得元、直接・推移依存の不一致、正しい固定情報を試す | 実行エンジンだけの最小例も `SCRIPT_NUGET_LOCK_MISSING` で初回復元前に失敗し、復元を一度も始めない。追加依存の固定漏れ、不正指定、直接版の不一致も初回復元前に拒否する。要求版が取得元にない場合は初回復元で失敗し、上位版へ置き換えず生成に進まない。推移依存の不一致は復元後・生成前に失敗し、正しい固定情報では生成を `--no-restore` で行う |
| J | 実行 | 正常な提供型と処理、提供型・入口の例外、同じ入力の連続実行 | 選択した提供型を一度だけ生成し、`Create()` も一度だけ呼ぶ。処理は `run` だけで実行する。結果、実行記録、設定、ログは既存契約と一致する |
| K | 既存コマンドの回帰 | .NET 8 のみで `engine run` と `engine validate` に `.csx`、`#load`、NuGet の許可・固定、設定の相対パス、既存の不正入力を渡す。ツール保管場所や `.nuspec`、配布版のメタデータが読めない条件も含める | コマンド実行部と埋め込み API の既存 `.csx` 経路は、版照合のために入力形式を選ぶ前に停止せず実行する。結果、診断、終了コード、相対パスの基準を維持する |
| L | 出力と並行実行 | 同じ入力パスに異なる取得元・固定情報を指定して並行起動し、一方の SDK 要求を中止後も専用出力へ遅れて書き込ませる | 各要求の runfile、temporary、CLI、NuGet cache directory が分離され、後続の復元・検査・生成が旧要求の遅延書込みを読み取らない。調整用排他は通常要求の順序を保ち、入力側のファイルを汚さない |
| M | 失敗診断 | 不正な C#、復元失敗、生成失敗、依存ライブラリ欠落 | 構文・型検査は `SCRIPT_COMPILE_FAILED`、復元は `SCRIPT_NUGET_RESTORE_FAILED`、構築・生成は `SCRIPT_BUILD_FAILED`、依存ライブラリ欠落は `SCRIPT_LOAD_FAILED` 相当とする。診断と原因位置を保ち、全て非 `0` で終了し、提供型・処理を実行しない |

## 11. 対象外

- `.csx` の評価方式、NuGet 解決、API 契約、既存コマンド診断の変更。
- `.cs` による通常の `Main`、トップレベル文、複数ファイル `#:include`、任意のプロジェクト参照（`#:project`）・開発基盤の参照（`#:sdk`）、実行時の実行物起動。
- .NET 開発基盤の取得や同梱、全体へのパッケージ追加、別プロセス実行部。
- 信頼されていない入力、NuGet パッケージ、構築設定を実行できる隔離環境。
- `validate` での提供型登録コード実行や処理・設定の型変換。
- `net8.0` でのクラス定義だけの入力対応。方式 A は比較試験だけに用いる。

## 12. 実測状況

上流開発基盤の `Build_Library` 試験は、`OutputType=Library` を指定してクラス定義だけの `.cs` からライブラリを生成でき、`dotnet run` では起動できないことを示す。この作業環境には .NET 開発基盤がなく、導入していない。本製品のライブラリ生成、開発基盤の選択、複数対象向けパッケージの導入・更新、型同一性、`PEReader` による副作用の非実行、復元と依存固定、既存コマンドの回帰は全て**未実測**として受入試験に残す。
