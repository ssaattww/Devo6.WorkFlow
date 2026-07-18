# `Devo6.WorkFlow`

[![検査](https://github.com/ssaattww/Devo6.WorkFlow/actions/workflows/pr-xunit-tests.yml/badge.svg)](https://github.com/ssaattww/Devo6.WorkFlow/actions/workflows/pr-xunit-tests.yml)
[![公開](https://github.com/ssaattww/Devo6.WorkFlow/actions/workflows/publish-nuget.yml/badge.svg)](https://github.com/ssaattww/Devo6.WorkFlow/actions/workflows/publish-nuget.yml)

**CLI ツール用パッケージ**
[![NuGet 版](https://img.shields.io/nuget/v/Devo6.WorkFlow.Cli)](https://www.nuget.org/packages/Devo6.WorkFlow.Cli/)
[![NuGet 導入数](https://img.shields.io/nuget/dt/Devo6.WorkFlow.Cli)](https://www.nuget.org/packages/Devo6.WorkFlow.Cli/)

**参照用パッケージ**
[![NuGet 版](https://img.shields.io/nuget/v/Devo6.WorkFlow.Engine)](https://www.nuget.org/packages/Devo6.WorkFlow.Engine/)
[![NuGet 導入数](https://img.shields.io/nuget/dt/Devo6.WorkFlow.Engine)](https://www.nuget.org/packages/Devo6.WorkFlow.Engine/)

---

`Devo6.WorkFlow` は、C# script だけでワークフローを定義して実行するエンジンです。利用者は `.csx` に Step 型、入出力型、Config 型、実行入口の `CompositeStep` を書きます。YAML は実行時 Config の入力であり、ワークフロー定義には使いません。

Step は `IStep<TOut>` または `IAsyncStep<TOut>` を実装します。Step 間の値は `Produce` や `StoreAs` で明示的に渡し、Step は必要な値を `StepInput` から取得します。Config は Step 専用引数ではなく、対象 Step の実行直前に `StepContext` へ登録されます。

## 必要な環境

別の端末に導入して使う場合は、.NET 8 以降の開発環境を入れてください。`dotnet tool install`、`dotnet tool update`、手元のパッケージ作成、`.csx` の実行検証に `dotnet` CLI が必要です。開発環境には通常の実行に必要な実行環境も含まれます。

ワークフロー内で NuGet 参照を使う場合は、利用するパッケージ参照元へアクセスできることが必要です。`NuGet.config` がある場合は、通常の NuGet 設定として参照元に使われます。

## 導入

リポジトリからパッケージを作る場合は、CLI ツールと参照用パッケージをそれぞれ作成します。

```bash
dotnet pack src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj -c Release -o ./artifacts/packages
dotnet pack src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj -c Release -o ./artifacts/packages
```

CLI ツールを別の端末で使う場合は、配置先ディレクトリをパッケージ参照元として指定します。

```bash
dotnet tool install --global Devo6.WorkFlow.Cli --add-source ./artifacts/packages
```

導入後は `engine` コマンドを使います。

```bash
engine run main.csx --workflow-config appsettings.yaml
engine validate main.csx --workflow-config appsettings.yaml
```

更新する場合は同じパッケージ参照元を指定します。

```bash
dotnet tool update --global Devo6.WorkFlow.Cli --add-source ./artifacts/packages
```

公開 API を利用するプロジェクトでは、参照用パッケージを追加します。このパッケージには Engine と `Abstractions` が含まれます。

```bash
dotnet add package Devo6.WorkFlow.Engine --source ./artifacts/packages
```

NuGet の公開先へ登録済みの安定版を導入する場合は、パッケージ参照元の指定は不要です。

```bash
dotnet tool install --global Devo6.WorkFlow.Cli
dotnet add package Devo6.WorkFlow.Engine
```

## 複数フォルダの例

`samples/multi-folder-composite/main.csx` は、参照用 NuGet パッケージを `#r "nuget: Devo6.WorkFlow.Engine, 0.1.0"`、YAML 解析器を `#r "nuget: YamlDotNet, 16.3.0"` で参照し、別フォルダにある Step を `#load` します。内側 `CompositeStep` は YAML 前付け付き文書を読み込み、付加情報と本文の分離、本文整形、統計作成、保存内容作成を担当します。外側 `CompositeStep` は内側の結果を受け取り、保存 Step を呼びます。

```text
samples/multi-folder-composite/
  main.csx
  appsettings.yaml
  input/source.txt
  shared/contracts.csx
  steps/load/appsettings.yaml
  steps/load/load-text-step.csx
  steps/parse/appsettings.yaml
  steps/parse/parse-document-step.csx
  steps/normalize/appsettings.yaml
  steps/normalize/normalize-text-step.csx
  steps/analyze/appsettings.yaml
  steps/analyze/analyze-text-step.csx
  steps/report/appsettings.yaml
  steps/report/build-report-step.csx
  steps/save/appsettings.yaml
  steps/save/save-text-step.csx
  engine.yaml
  README.md
```

```bash
engine run samples/multi-folder-composite/main.csx --workflow-config appsettings.yaml --engine-config engine.yaml
```

絶対 path の Entry も同じ規則で実行できます。

```bash
ENTRY=$(pwd)/samples/multi-folder-composite/main.csx
engine run "$ENTRY" --workflow-config appsettings.yaml --engine-config engine.yaml
```

この例では、外側 `CompositeStep` が `Pipeline.*` と `Save` の境界 Config を読み込み、同じ `StepInput` と `StepContext` を使って内側 `CompositeStep` を実行します。各 `steps/*/appsettings.yaml` を Step 側の既定 Config として読み、root の `appsettings.yaml` は `Pipeline.Load.Path`、`Pipeline.Normalize.Uppercase`、`Pipeline.Report.*`、`Save.Path` を部分上書きします。CLI からは `--wset Pipeline.Normalize.Uppercase=false` や `--wset Pipeline.Report.Heading=...` のように上書きできます。

engine config とログ出力の利用例は `samples/multi-folder-composite/README.md` を参照してください。標準出力には `Entry started`、`Loading source text from input/source.txt`、`Building report with heading Composite sample report`、`Saving report text to output/result.txt`、`Entry succeeded` などが出るため、処理の進捗表示として確認できます。`--wset` で workflow 値、`--eset` で engine 値を上書きできます。

NuGet 参照を使う実行では、公開済みの `Devo6.WorkFlow.Engine` 0.1.0 と `YamlDotNet` 16.3.0 を取得できる環境であれば、そのまま通常の NuGet 設定で解決します。`NuGet.config` がある場合は通常の NuGet 設定として使われます。必要に応じて `--allow-nuget PackageId,Version` を指定すると、その一覧に含まれない NuGet 直接参照を拒否できます。このサンプルは通常利用を示すためロックファイルを置きません。再現性を固定したい場合だけ `devo6.nuget.lock.yaml` を置き、`--locked` を指定します。

## 最小例

`main.csx`:

```csharp
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using System.Collections.Generic;
using System.IO;

public sealed class MainConfig
{
    public LoadStep.Config Load { get; set; } = new();

    public ConvertStep.Config Convert { get; set; } = new();

    public SaveStep.Config Save { get; set; } = new();
}

public sealed record LoadResult(string Text);

public sealed record ConvertInput(string Text);

public sealed record ConvertResult(string ConvertedText);

public sealed record SaveInput(string Content);

public sealed class LoadStep : IStep<LoadResult>
{
    public sealed class Config
    {
        public string Path { get; set; } = "";
    }

    public LoadResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();

        return new LoadResult(File.ReadAllText(config.Path));
    }
}

public sealed class ConvertStep : IStep<ConvertResult>
{
    public sealed class Config
    {
        public bool ToUpper { get; set; }

        /// <summary>
        /// 変換に付与するタグを取得または設定します。
        /// </summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// 変換対象の一覧を取得または設定します。
        /// </summary>
        public List<Target> Targets { get; set; } = new();
    }

    /// <summary>
    /// 変換対象を表します。
    /// </summary>
    public sealed class Target
    {
        /// <summary>
        /// 対象名を取得または設定します。
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 変換対象を有効にするかどうかを取得または設定します。
        /// </summary>
        public bool Enabled { get; set; }
    }

    public ConvertResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        ConvertInput convertInput = input.Get<ConvertInput>();
        string text = config.ToUpper
            ? convertInput.Text.ToUpperInvariant()
            : convertInput.Text;

        return new ConvertResult(text);
    }
}

public sealed class SaveStep : IStep<Unit>
{
    public sealed class Config
    {
        public string Path { get; set; } = "";
    }

    public Unit Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        SaveInput saveInput = input.Get<SaveInput>();
        File.WriteAllText(config.Path, saveInput.Content);

        return Unit.Value;
    }
}

var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
        .Produce<ConvertInput>(x => new ConvertInput(x.Text))
    .Run<ConvertStep, ConvertResult>()
        .WithConfig<ConvertStep.Config>("Convert")
        .Produce<SaveInput>(x => new SaveInput(x.ConvertedText))
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

`appsettings.yaml`:

```yaml
Load:
  Path: ./input.txt

Convert:
  ToUpper: true
  Tags: [default, document]
  Targets: [{Name: guide, Enabled: true}]

Save:
  Path: ./output.txt
```

`MainConfig` は CompositeStep 境界 Config 型です。`Load`、`Convert`、`Save` は境界 Config 型上のプロパティ path です。`WithConfig<MainConfig>()` で境界 Config 型を宣言し、`WithConfig<LoadStep.Config>("Load")` のように Step ごとの Config 型と境界 Config 型上のプロパティ path を対応させます。

実行時は、Step 側の既定 Config YAML、root Config の該当区画、CLI `--workflow-set` または `--wset` の順に値を重ねてから `MainConfig` へ変換します。その後、対象 Step の実行直前に `MainConfig.Load` を `StepContext.Set<LoadStep.Config>()` へ登録します。`Convert` と `Save` も同じ規則です。

## 条件付き実行

`CompositeStep` では現在値を見て後続 Step を選べます。`RunIf` は条件が true の場合だけ値を返す Step を実行し、false の場合は現在値または指定した代替値を使います。`TapIf` は条件が true の場合だけ `Unit` Step を実行し、現在値は変えません。`If` は真側または偽側の分岐、`Switch` は選択値に一致する分岐または既定分岐を実行します。

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, Document>()
        .Produce<Document>(x => x)
    .RunIf<NormalizeStep>(x => x.NeedsNormalize)
    .TapIf<ValidateStep>(x => x.Category == "guide")
    .If(
        "DocumentLength",
        x => x.WordCount >= 10,
        thenFlow => thenFlow.Run("KeepDetailedDocument", x => x),
        elseFlow => elseFlow.Run("MarkShortDocument", x => x with { IsShort = true }))
    .Switch<DocumentKind, Document>(
        "DocumentKind",
        x => x.Kind,
        cases => cases
            .Case(DocumentKind.Guide, branch => branch.Run("UseGuideReport", x => x))
            .Case(DocumentKind.Reference, branch => branch.Run("UseReferenceReport", x => x))
            .Default(branch => branch.Run("UseDefaultReport", x => x)));
```

分岐の中でも `Run`、`RunIf`、`TapIf`、`If`、`Switch`、`Produce`、`StoreAs`、`Discard` を使えます。選ばれなかった分岐の Step は実行されず、実行記録にも分岐内 Step としては出ません。

## 実行と検証

既定の Entry 名は `Main` です。

```bash
engine run main.csx --workflow-config appsettings.yaml
engine validate main.csx --workflow-config appsettings.yaml
```

`run` は Entry `.csx` を読み込み、指定 Entry を解決して Step を実行します。Config API を使う Entry で `--workflow-config` を省略し、既定 Config YAML だけでも Config を構成できない場合は、最初の Step 実行前に失敗します。

`validate` は実行前検証用です。Entry `.csx` の存在、Entry 解決、`#load`、参照、コンパイル、Config path の存在などを確認します。`validate` は Config path の存在確認までで、Config 型変換、`--workflow-set` / `--wset` 適用、Config 値検証は行いません。これらは `run` 時に行います。

Config の一部は `--workflow-set` または短縮名の `--wset` で上書きできます。

```bash
engine run main.csx --workflow-config appsettings.yaml --workflow-set Convert.ToUpper=false
```

Step 登録単位 Config では、`--workflow-set` または `--wset` の key は CompositeStep 境界 Config 型上のプロパティ path です。`Convert.ToUpper=false` は `MainConfig.Convert.ToUpper` への上書きとして扱われます。

`ConvertStep.Config` の `Tags` と `Targets` は YAML inline array で collection 全体を置換できます。空白を含む断片や object 配列は、`Key=value` 全体を引用します。

PowerShell:

```powershell
engine run main.csx --workflow-config appsettings.yaml --workflow-set 'Convert.Tags=[release, urgent]'
engine run main.csx --workflow-config appsettings.yaml --wset 'Convert.Targets=[{Name: guide, Enabled: true}, {Name: api, Enabled: false}]'
engine run main.csx --workflow-config appsettings.yaml --wset 'Convert.Tags=[]'
```

bash:

```bash
engine run main.csx --workflow-config appsettings.yaml --workflow-set 'Convert.Tags=[release, urgent]'
engine run main.csx --workflow-config appsettings.yaml --wset 'Convert.Targets=[{Name: guide, Enabled: true}, {Name: api, Enabled: false}]'
engine run main.csx --workflow-config appsettings.yaml --wset 'Convert.Tags=[]'
```

Entry を明示する場合は `--entry` を使います。

```bash
engine run main.csx --entry Build
engine validate main.csx --entry Deploy.Build
```

名前空間付き Entry は次のように定義します。

```csharp
var DeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
    .Run<DeployBuildStep, Unit>();
```

この Entry は `--entry Deploy.Build` で指定します。短い `Build` が複数の名前空間にある場合は曖昧なので、完全修飾名を指定してください。

## `#load` と参照

ローカル `.csx` は `#load` で分割できます。

```csharp
#load "./steps/load-step.csx"
#load "./shared/common.csx"
```

ローカル `#load` の相対パスは、`#load` を書いた `.csx` のディレクトリ基準です。読み込みはワークフロー root 配下に制限され、循環読み込みは検証エラーです。

Entry `.csx` とローカル `#load` は絶対 path でも指定できます。絶対 path の Entry を指定した場合も、`--workflow-config appsettings.yaml` と `--engine-config engine.yaml` は Entry ディレクトリ基準で解決され、`EngineArguments.EntryPath` には解決済み Entry path が入ります。絶対 path のローカル `#load` も workflow root 配下だけ許可され、root 外の path は拒否されます。

assembly 名参照とファイル参照は明示許可された参照だけを使えます。NuGet 参照は固定 version を指定して通常の NuGet 設定で解決します。

```csharp
#r "System.Text.Json"
#r "./lib/custom-helper.dll"
#r "nuget: CsvHelper, 33.0.1"
```

NuGet script パッケージは `dotnet-script` 互換の形式で読み込めます。

```csharp
#load "nuget: Simple.Targets.Csx, 6.0.0"
```

`#r "nuget: ..."` と `#load "nuget: ..."` は、`devo6.nuget.lock.yaml` がある場合だけロック検証の対象です。ロックファイルはワークフロー root に置き、直接参照、解決済み依存関係、`targetFramework`、実行時識別子、`Dotnet.Script.Core` version を記録します。既定では `NuGet.config` など通常の NuGet 設定で依存関係を解決し、ロックファイルが無い場合はロック比較を行いません。`--locked` を指定した場合だけロックファイル欠落を失敗にします。`verifyPackageSources: true` を指定した場合だけ `packageSources` と実際の参照元一覧を順序非依存で照合します。ロックファイルがある場合の不一致は検証または実行前に失敗します。

## 現行契約外

以下は現行契約外、または未採用です。

- YAML ワークフロー定義
- Step 専用 Config 引数
- Step 型への Config 自動注入
- 任意の複数 `--workflow-config` 指定
- Config 型自動推論
- `validate` での Config 型変換、`--workflow-set` / `--wset` 適用、Config 値検証
- CLI の timeout オプション
- CLI の retry オプション
- Config による retry 指定

## ライセンス

このリポジトリは [MIT](LICENSE) ライセンスで公開します。

詳細な設計とエラー規則は `doc/workflow_engine_spec.md` を参照してください。
