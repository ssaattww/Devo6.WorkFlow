# `Devo6.WorkFlow`

`Devo6.WorkFlow` は、C# script だけでワークフローを定義して実行するエンジンです。利用者は `.csx` に Step 型、入出力型、Config 型、実行入口の `CompositeStep` を書きます。YAML は実行時 Config の入力であり、ワークフロー定義には使いません。

Step は `IStep<TOut>` または `IAsyncStep<TOut>` を実装します。Step 間の値は `Produce` や `StoreAs` で明示的に渡し、Step は必要な値を `StepInput` から取得します。Config は Step 専用引数ではなく、対象 Step の実行直前に `StepContext` へ登録されます。

## 必要な環境

別の端末に導入して使う場合は、.NET 8 以降の開発環境を入れてください。`dotnet tool install`、`dotnet tool update`、手元のパッケージ作成、`.csx` の実行検証に `dotnet` CLI が必要です。開発環境には通常の実行に必要な実行環境も含まれます。

ワークフロー内で NuGet 参照を使う場合は、利用するパッケージ参照元へアクセスできることと、`devo6.nuget.lock.yaml` がワークフロー root にあることも必要です。

## ツールとして使う

リポジトリからパッケージを作る場合は、次のように作成します。

```bash
dotnet pack src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj -c Release -o ./artifacts/packages
```

別の端末へパッケージを配置した後、配置先ディレクトリをパッケージ参照元として指定します。

```bash
dotnet tool install --global Devo6.WorkFlow.Cli --add-source ./artifacts/packages
```

導入後は `engine` コマンドを使います。

```bash
engine run main.csx --config appsettings.yaml
engine validate main.csx --config appsettings.yaml
```

更新する場合は同じパッケージ参照元を指定します。

```bash
dotnet tool update --global Devo6.WorkFlow.Cli --add-source ./artifacts/packages
```

NuGet の公開先へ登録済みの安定版を導入する場合は、パッケージ参照元の指定は不要です。

```bash
dotnet tool install --global Devo6.WorkFlow.Cli
```

## 複数フォルダの例

`samples/multi-folder-composite/main.csx` は、別フォルダにある読み込み、変換、保存の Step を `#load` し、1 つの `CompositeStep` として実行します。

```text
samples/multi-folder-composite/
  main.csx
  appsettings.yaml
  shared/contracts.csx
  steps/load/appsettings.yaml
  steps/load/load-text-step.csx
  steps/convert/appsettings.yaml
  steps/convert/convert-text-step.csx
  steps/save/appsettings.yaml
  steps/save/save-text-step.csx
```

```bash
engine run samples/multi-folder-composite/main.csx --config appsettings.yaml
```

この例の `appsettings.yaml` は、`Load`、`Convert`、`Save` の各区画で Step フォルダ内の YAML 断片を参照します。実行時は参照先を結合してから境界 Config 型へ変換します。

## 最小例

`main.csx`:

```csharp
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
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

Save:
  Path: ./output.txt
```

`MainConfig` は CompositeStep 境界 Config 型です。`Load`、`Convert`、`Save` は境界 Config 型上のプロパティ path です。`WithConfig<MainConfig>()` で境界 Config 型を宣言し、`WithConfig<LoadStep.Config>("Load")` のように Step ごとの Config 型と境界 Config 型上のプロパティ path を対応させます。

実行時は、`--config` の YAML 全体を `MainConfig` へ変換します。その後、対象 Step の実行直前に `MainConfig.Load` を `StepContext.Set<LoadStep.Config>()` へ登録します。`Convert` と `Save` も同じ規則です。

## 実行と検証

既定の Entry 名は `Main` です。

```bash
engine run main.csx --config appsettings.yaml
engine validate main.csx --config appsettings.yaml
```

`run` は Entry `.csx` を読み込み、指定 Entry を解決して Step を実行します。Config API を使う Entry で `--config` を省略すると、最初の Step 実行前に失敗します。

`validate` は実行前検証用です。Entry `.csx` の存在、Entry 解決、`#load`、参照、コンパイル、Config path の存在などを確認します。`validate` は Config path の存在確認までで、Config 型変換、`--set` 適用、Config 値検証は行いません。これらは `run` 時に行います。

Config の一部は `--set` で上書きできます。

```bash
engine run main.csx --config appsettings.yaml --set Convert.ToUpper=false
```

Step 登録単位 Config では、`--set` の key は CompositeStep 境界 Config 型上のプロパティ path です。`Convert.ToUpper=false` は `MainConfig.Convert.ToUpper` への上書きとして扱われます。

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

`#r` は明示許可された参照だけを使えます。

```csharp
#r "System.Text.Json"
#r "./lib/custom-helper.dll"
#r "nuget: CsvHelper, 33.0.1"
```

NuGet script パッケージは `dotnet-script` 互換の形式で読み込めます。

```csharp
#load "nuget: Simple.Targets.Csx, 6.0.0"
```

`#r "nuget: ..."` と `#load "nuget: ..."` は `devo6.nuget.lock.yaml` の対象です。ロックファイルはワークフロー root に置き、直接参照、解決済み依存関係、`targetFramework`、実行時識別子、パッケージ参照元などを記録します。ロックファイルの欠落や不一致は検証または実行前に失敗します。

## 現行契約外

以下は現行契約外、または未採用です。

- YAML ワークフロー定義
- Step 専用 Config 引数
- Step 型への Config 自動注入
- 任意の複数 `--config` 指定
- Config 型自動推論
- `validate` での Config 型変換、`--set` 適用、Config 値検証
- CLI の timeout オプション
- CLI の retry オプション
- Config による retry 指定

詳細な設計とエラー規則は `doc/workflow_engine_spec.md` を参照してください。
