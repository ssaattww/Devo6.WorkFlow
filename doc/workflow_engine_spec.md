# csx 完結型ワークフローエンジン設計資料

## 1. 目的

本設計資料は、C# Script（`.csx`）上で定義できるワークフローエンジンのライブラリ要件および基本設計をまとめる。

本ライブラリは、処理定義を C# で組み立てられる書き心地を参考にしつつ、実体としては以下を目的とする。

- `.csx` で Step を組み合わせたワークフローを定義できること
- Step と Flow を別概念にせず、すべて Step として扱うこと
- 上流 Step の結果を下流 Step へ明示的に渡せること
- Step の入力を可変長に扱えること
- Config、StepContext、上流出力を統一的に扱えること
- ワークフロー定義に YAML を使わず、名前付きの `CompositeStep` を `.csx` で定義すること
- Config は実行時入力として `StepContext` に保持できること
- `.csx` の解決、NuGet 解決、外部 `.csx` 解決には `Dotnet.Script.Core` を利用すること

---

## 2. 基本方針

### 2.1 Step が唯一の実行単位

本エンジンでは、Step を唯一の実行単位とする。

従来の意味での Flow は、独立した概念としては持たない。

代わりに、複数の Step を順番に実行する Step を **CompositeStep** として扱う。

```text
Step
├─ 通常Step
└─ CompositeStep
   ├─ Step
   ├─ Step
   └─ Step
```

### 2.2 Flow 関数は持たない

`Step` と `Flow` を同じものとして扱うため、`Flow(...)` のような特別な API は持たない。

ワークフロー定義は、名前付きの CompositeStep として定義する。

例:

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .Produce<ConvertInput>(x => new ConvertInput
        {
            Text = x.Text
        })
    .Run<ConvertStep, ConvertResult>()
        .Produce<SaveInput>(x => new SaveInput
        {
            Content = x.ConvertedText
        })
    .Run<SaveStep, Unit>()
        .Discard();
```

---

## 3. 実行モデル

### 3.1 逐次実行

Step は定義順に逐次実行する。

以下は逐次実行モデルには含めない。

- 並列実行
- 分岐実行
- 統合実行
- 複雑な依存関係解決
- Step の自動依存解決

### 3.2 自動推論しない

エンジンは以下を行わない。

- Step の入力型を見て依存 Step を自動実行する
- 上流 Step の出力から下流 Step の入力を自動生成する
- Config を Step に自動注入する
- 前 Step の出力型と次 Step の入力型を自動接続する

Step 間のデータ受け渡しは、CompositeStep 定義内でユーザーが明示する。

---

## 4. StepInput

### 4.1 StepInput の役割

`StepInput` は、Step に渡される唯一の入口である。

Step は `StepInput` 以外から入力を受け取らない。

```csharp
public interface IStep<TOut>
{
    TOut Execute(StepInput input);
}
```

### 4.2 StepInput は可変長入力集合

`StepInput` は、型付き・名前付きの可変長入力集合である。

`StepInput` は、`Produce` と `StoreAs` で追加された値を同一 `CompositeStep` 実行中の後続すべての Step へ保持する追記型集合である。

登録前の上流 Step 以前からは、追加予定の値を読めない。

含まれる値の例:

- 上流 Step の出力全体
- 上流 Step の出力の一部
- 上流 Step の出力から生成した下流用の型
- Config から作成された値
- 固定値
- エンジン引数
- 実行中に生成された任意の共有値

### 4.3 StepInput の識別キー

値は以下のいずれかで識別する。

```text
Type
Type + name
```

同じ型の値を複数扱う場合は、名前付き登録を使用する。

同じ型と名前の組み合わせを複数登録してはならない。

`Produce` または `StoreAs` により既存キーへ再登録しようとした場合、エンジンは実行時エラーとして扱う。

この制約は、複数 Step にまたがる再登録にも適用する。暗黙上書きは行わない。

型キーと名前付きキーは、同じ CLR 型でも別キーとして扱う。

例えば `string`、`string` + `title`、`string` + `body` は別の値として共存できる。

例:

```csharp
string title = input.Get<string>("title");
string body = input.Get<string>("body");
```

### 4.4 StepInput API 案

```csharp
public sealed class StepInput
{
    public StepContext Context { get; }

    public T Get<T>();

    public T Get<T>(string name);

    public bool TryGet<T>(out T value);

    public bool TryGet<T>(string name, out T value);
}
```

---

## 5. StepContext

### 5.1 StepContext は StepInput に自動で含める

`StepContext` は、エンジンが自動で生成し、`StepInput` に含める。

Step から見た入口は引き続き `StepInput` のみである。

```csharp
StepContext context = input.Context;
```

または、実装上は以下のように `StepInput` 内に含めてもよい。

```csharp
StepContext context = input.Get<StepContext>();
```

ただし、API としては `input.Context` を用意する方が明確である。

### 5.2 StepContext の責務

`StepContext` は、実行全体で共有される値を保持する。

主な用途:

- Config
- ConfigStore
- EngineArguments
- 記録出力
- 実行全体で共有したい値

### 5.3 StepInput と StepContext の使い分け

```text
StepInput:
  Step 間で明示的に受け渡す追記型の可変長入力集合
  Produce と StoreAs で追加した値を後続 Step へ保持する
  既存値を削除せず、同じキーを暗黙上書きしない

StepContext:
  実行全体で共有される長寿命の値
  Set により同じキーを明示上書きできる
```

Config は `StepContext` に置く方針とする。

### 5.4 StepContext API 案

```csharp
public sealed class StepContext
{
    public ILogger Logger { get; }

    public T Get<T>();

    public T Get<T>(string name);

    public void Set<T>(T value);

    public void Set<T>(string name, T value);

    public bool TryGet<T>(out T value);

    public bool TryGet<T>(string name, out T value);
}
```

`StepContext.Set<T>()` と `StepContext.Set<T>(name, value)` は、同じ型と名前の組み合わせが既に存在する場合、明示的な上書きとして扱う。

`StepInput` は Step 間の入力集合であり、`StepContext` は実行全体の共有値であるため、重複登録の扱いを分ける。

---

## 6. Config

### 6.1 Config の基本方針

Config は Step 専用引数として渡さない。

Step の入口は `StepInput` のみであり、Config は `StepInput.Context` から取得する。

例:

```csharp
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

        return new ConvertResult
        {
            ConvertedText = config.ToUpper
                ? convertInput.Text.ToUpperInvariant()
                : convertInput.Text
        };
    }
}
```

### 6.2 Config 入力形式

ワークフロー定義には YAML を使わない。

Config は `.csx` 内で生成する値、エンジン引数、環境変数、または Config ファイルから生成した値として扱う。

Config ファイルの標準形式は YAML とする。

この YAML は実行時設定の入力であり、ワークフロー定義ではない。

Config ファイルの相対パスは、Entry `.csx` の存在するディレクトリを基準に解決する。

`appsettings.yaml` の例:

```yaml
Load:
  Path: ./input.txt

Convert:
  ToUpper: true
  Mode: Normal

Save:
  Path: ./output.txt
```

`Load`、`Convert`、`Save` は CompositeStep 境界 Config 型上のプロパティ path である。

標準契約では、各 Step は `public sealed class Config` などの自分の Config 型を Step 型の内側に持つ。CompositeStep は `MainConfig` のような境界 Config 型を持ち、`MainConfig.Load` は `LoadStep.Config`、`MainConfig.Convert` は `ConvertStep.Config`、`MainConfig.Save` は `SaveStep.Config` を保持する。YAML の区画名は Step 実行順を定義しない。Step 実行順は `.csx` の `CompositeStep` 定義で決める。

Step を別フォルダに分ける場合、各 Step フォルダにその Step Config 型だけを表す既定 Config YAML を置いてよい。たとえば `steps/load/appsettings.yaml` は `LoadStep.Config` の既定値、`steps/convert/appsettings.yaml` は `ConvertStep.Config` の既定値として扱う。

実行時に `--config` へ渡す YAML は、CompositeStep 境界 Config 型に対応する root Config ファイルである。root Config は、Step 側の既定 Config YAML に対する部分上書きとして扱う。エンジンは最初の Step 実行前に、Step 側の既定 Config YAML、root Config の該当区画、CLI `--set` の順に値を重ねてから境界 Config 型へ変換する。

root Config の例:

```yaml
Convert:
  Prefix: "sample: "
```

この例では `steps/convert/appsettings.yaml` を `ConvertStep.Config` の既定値として読み、root Config の `Convert.Prefix` だけを上書きする。

既定 Config YAML の規約 path は、Entry `.csx` の存在するディレクトリを基準に、`steps/{sectionPath}/appsettings.yaml` とする。`sectionPath` の `.` はディレクトリ区切りに変換し、各 path 要素は小文字と `-` の区切りに変換する。たとえば `Convert` は `steps/convert/appsettings.yaml`、`Text.Convert` は `steps/text/convert/appsettings.yaml` である。

規約 path と異なる既定 Config YAML を使う場合は、`WithConfig<TConfig>(string sectionPath, string defaultConfigPath)` で Entry `.csx` からの相対 path を明示する。明示 path は規約 path より優先する。

既存互換として、宣言済み Step Config 区画の値が YAML path だけの場合は、その YAML を既定 Config YAML として扱う。この書き方は部分上書きを含まない省略形であり、新規の例では Step 側の既定 Config YAML と root Config の部分上書きを分けて書くことを推奨する。

### 6.3 Config 読み込み

標準 Config 読み込みは、エンジンの実行前処理として行う。

推奨契約では、Entry 側は CompositeStep 境界 Config 型と、Step 登録単位の Config 型および境界 Config 型上のプロパティ path を明示する。

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
        .Produce<ConvertInput>(x => new ConvertInput
        {
            Text = x.Text
        })
    .Run<ConvertStep, ConvertResult>()
        .WithConfig<ConvertStep.Config>("Convert")
        .Produce<SaveInput>(x => new SaveInput
        {
            Content = x.ConvertedText
        })
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

`WithConfig<TConfig>()` は、Step 登録単位 Config がある場合は CompositeStep 境界 Config 型の宣言として扱う。

`WithConfig<TConfig>(string sectionPath)` は、直前に登録した Step のメタ情報として Step Config 型と境界 Config 型上のプロパティ path を保持する。既定 Config YAML は規約 path から解決する。Step 専用引数は増やさない。

`WithConfig<TConfig>(string sectionPath, string defaultConfigPath)` は、直前に登録した Step のメタ情報として Step Config 型、境界 Config 型上のプロパティ path、既定 Config YAML path を保持する。`defaultConfigPath` は Entry `.csx` の存在するディレクトリを基準に解決する。

CLI `run` は Entry `.csx` をロードした後、宣言済み Step Config ごとに既定 Config YAML を読み、`--config` の root YAML にある該当区画を部分上書きとして重ねる。その後、結合済み YAML 全体を境界 Config 型へ変換する。

`run` は最初の Step を実行する前に、境界 Config 型への型変換、CLI override 適用、`DataAnnotations` と `IValidatableObject` の検証をすべて完了する。いずれか 1 つでも失敗した場合、最初の Step は実行しない。

実行時は、対象 Step の実行直前に、境界 Config 型上の `sectionPath` プロパティ値を取り出し、`StepContext.Set<TConfig>(config)` で登録する。たとえば `WithConfig<LoadStep.Config>("Load")` は、`MainConfig.Load` を `StepContext.Set<LoadStep.Config>()` へ登録する宣言である。

Step 登録単位 Config があるのに `WithConfig<MainConfig>()` のような境界 Config 型宣言がない場合は、最初の Step 実行前に `CONFIG_LOAD_FAILED` とする。

旧 `.WithConfig<TConfig>()` だけを使う Entry 全体 Config 互換 API は維持する。Step 登録単位 Config がない場合は従来どおり YAML 全体を `TConfig` に変換し、最初の Step 実行前に `StepContext.Set<TConfig>(config)` で登録する。

任意の複数 `--config` 指定、Config 型自動推論、Step 型への Config 自動注入、Step 専用引数は採用しない。

Step フォルダに置いた既定 Config YAML は、宣言済み Step Config の規約 path または明示 `defaultConfigPath` としてのみ読み込む。標準エンジンは未宣言 Step の Config を探索せず、複数 root Config の優先順位解決も行わない。

Config 読み込み用 Step をユーザーが明示的に定義する方式は、標準外の拡張として許可する。

Config 読み込み Step を使用する場合は、標準 Config 読み込み、CLI override、実行前 Config 検証の対象外とする。

### 6.4 標準 Config の定義と取得

```csharp
public sealed class LoadStep : IStep<LoadResult>
{
    public sealed class Config
    {
        public string Path { get; set; } = "";
    }

    public LoadResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();

        string text = File.ReadAllText(config.Path);

        return new LoadResult
        {
            Text = text,
            FilePath = config.Path
        };
    }
}

public sealed class ConvertStep : IStep<ConvertResult>
{
    public sealed class Config
    {
        public bool ToUpper { get; set; }

        public string Mode { get; set; } = "";
    }

    public ConvertResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();
        ConvertInput convertInput = input.Get<ConvertInput>();

        return new ConvertResult
        {
            ConvertedText = config.ToUpper
                ? convertInput.Text.ToUpperInvariant()
                : convertInput.Text,
            Mode = config.Mode
        };
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

public sealed class MainConfig
{
    public LoadStep.Config Load { get; set; } = new();

    public ConvertStep.Config Convert { get; set; } = new();

    public SaveStep.Config Save { get; set; } = new();
}
```

各 Step は `StepContext.Get<TConfig>()` で自分の Config 型を取得する。

| Step | 境界 Config プロパティ path | Step Config 参照例 |
| --- | --- | --- |
| `LoadStep` | `Load` | `input.Context.Get<LoadStep.Config>().Path` |
| `ConvertStep` | `Convert` | `input.Context.Get<ConvertStep.Config>().ToUpper`, `input.Context.Get<ConvertStep.Config>().Mode` |
| `SaveStep` | `Save` | `input.Context.Get<SaveStep.Config>().Path` |

対応関係は、`MainConfig` のプロパティ、Step 登録時の `WithConfig<TConfig>(sectionPath)`、Step 実装の `StepContext.Get<TConfig>()` で明示する。Step ごとの Config 専用引数や、Step 型に対する Config 自動注入は追加しない。

`Convert` プロパティの複数プロパティを使う場合も、Step は同じ `ConvertStep.Config` から必要な値だけを読む。

```csharp
public sealed class ConvertStep : IStep<ConvertResult>
{
    public sealed class Config
    {
        public string Mode { get; set; } = "";
    }

    public ConvertResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();

        return new ConvertResult
        {
            Mode = config.Mode
        };
    }
}
```

Entry 側の宣言例:

```csharp
var Main = CompositeStep.Define("Main")
    .Run<ConvertStep, ConvertResult>()
        .WithConfig<MainConfig>()
        .WithConfig<ConvertStep.Config>("Convert");
```

Step は Config 専用引数を受け取らない。標準 Config は対象 Step の実行直前に `StepContext` へ登録され、Step は `StepContext.Get<TConfig>()` で取得する。

### 6.5 CLI による Config 指定

エンジン起動時に Config ファイルを指定できる。

```bash
engine run main.csx --config config/appsettings.yaml
```

`--config` は Config ファイルパスとして `EngineArguments` に保持する。

Entry が Step 登録単位 Config を使っている場合、CLI `run` は `--config` の YAML 全体を CompositeStep 境界 Config 型に変換し、Step 実行前に検証する。

Entry が旧 Entry 全体 Config 互換 API だけを使っている場合、CLI `run` は従来どおり `--config` の YAML 全体をその `TConfig` に変換し、Step 実行前に検証する。

`--config` 未指定で、Entry が Config API を使っていない場合は既存どおり成功する。

`--config` 未指定で、Entry が Config API を使っている場合は、Step 実行前に `CONFIG_NOT_FOUND` で失敗する。利用者へ早く原因を返すためである。

### 6.6 CLI による Config 上書き

CLI 引数で Config の一部を上書きできることを要件とする。

例:

```bash
engine run main.csx --config config/appsettings.yaml --set Convert.ToUpper=false
engine run main.csx --config config/appsettings.yaml --set Save.Path=./override.txt
```

`--set` は、CompositeStep 境界 Config 型に対するプロパティ path override として扱う。

書式は `--set Key=value` とする。CLI 解析層は最初の `=` より前を key、最初の `=` 以降を値として保持する。値の中の `=` は許可する。

CLI 解析層で `--set` に値がない場合、`key=value` になっていない場合、または key が空の場合は、Config 型を見ない CLI 解析エラーとして終了コード 2 で失敗する。

Step 登録単位 Config では、key の先頭は宣言済みの境界 Config プロパティ path と一致しなければならない。プロパティ path は `.` 区切りの path 要素として完全一致させる。`--set Convert.ToUpper=false` は `MainConfig.Convert.ToUpper` への override として扱い、対象 Step 実行直前には `MainConfig.Convert` を `StepContext.Set<ConvertStep.Config>()` へ登録する。`ConvertExtra.ToUpper` は `Convert` プロパティ path に一致しない。

同一 Entry 内の宣言済みプロパティ path は、互いに先頭から同じ path 要素列になってはならない。たとえば `Convert` と `Convert.Options` の併用は標準契約に含めず、違反時は最初の Step 実行前に `CONFIG_LOAD_FAILED` とする。宣言済みプロパティ path に一致しない `--set` も `CONFIG_LOAD_FAILED` とする。

プロパティ path は C# の公開プロパティ名を `.` でたどる。プロパティ名は大小文字を区別する完全一致とし、存在しないプロパティは `CONFIG_LOAD_FAILED` とする。

Entry 全体 Config 互換 API では、従来どおり `--set` key 全体を `TConfig` のプロパティ path として扱う。

入れ子プロパティの途中が `null` の場合、引数なしで生成できるクラスは自動生成して続行する。生成できない場合は `CONFIG_LOAD_FAILED` とする。

配列またはリストは、`Items[0].Name=value` のような既存要素への添字 override だけを扱う。自動拡張、配列全体またはリスト全体の置換は標準 Config override には含めない。添字が範囲外、負数、または数値でない場合は `CONFIG_LOAD_FAILED` とする。

型変換は override 対象プロパティの型に対して行う。標準契約では `string`、`bool`、`int`、`long`、`double`、`decimal`、`enum`、nullable な基本型を扱う。型変換に失敗した場合は `CONFIG_LOAD_FAILED` とする。

複数 override は、同一 key について最後の指定を有効にする。これは `EngineArguments.Settings` の既存 `Dictionary` 契約と合わせる。

`EngineArguments.Settings` は、override 適用後も CLI から受け取った元の文字列を保持する。Step は標準 Config の検証済み値と、CLI 指定そのものの両方を参照できる。

---

## 7. CompositeStep

### 7.1 CompositeStep の役割

`CompositeStep` は、複数の Step を順番に実行する Step である。

役割:

1. `StepInput` を保持する
2. Step を定義順に実行する
3. Step の戻り値を受け取る
4. ユーザーが明示した `Produce` に従って、戻り値から 0 個以上の値を `StepInput` に追加する
5. 必要であれば戻り値を破棄する

### 7.2 上流から下流への値渡し

上流 Step の結果を下流 Step に渡す場合は、CompositeStep 定義内で `Produce` によって明示する。

例:

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .Produce<ConvertInput>(x => new ConvertInput
        {
            Text = x.Text
        })
    .Run<ConvertStep, ConvertResult>()
        .Produce<SaveInput>(x => new SaveInput
        {
            Content = x.ConvertedText
        })
    .Run<SaveStep, Unit>()
        .Discard();
```

この例では、`LoadResult` 全体は `ConvertStep` に渡さない。

`LoadResult.Text` から `ConvertInput` を生成し、それだけを `StepInput` に登録する。

登録された `ConvertInput` は、この `CompositeStep` 実行中の後続すべての Step から読める。

### 7.3 Produce

`Produce` は、Step の戻り値から `StepInput` に追加する値を生成する。

```csharp
.Produce<TValue>(Func<TOut, TValue> selector)
```

名前付き登録も許可する。

```csharp
.Produce<TValue>(string name, Func<TOut, TValue> selector)
```

`Produce` で追加された値は、登録した Step より後に実行されるすべての Step から読める。

登録前の Step からは読めない。

同じ型キー、または同じ型と名前のキーへ再登録しようとした場合は実行時エラーとする。

型キーと名前付きキーは、同じ CLR 型でも別キーである。

trace 値を保存する場合は、`Produce` ごとに明示する。

```csharp
.Produce<TValue>(Func<TOut, TValue> selector, TraceValueCapture traceValueCapture)
.Produce<TValue>(string name, Func<TOut, TValue> selector, TraceValueCapture traceValueCapture)
```

既存の `Produce` と名前付き `Produce` は値を trace に保存しない。

`TraceValueCapture.Serialized` は値本文を `System.Text.Json` の直列化文字列として保存する。

`TraceValueCapture.Redacted` は型名、任意の名前、登録元、保存状態だけを保存し、値本文は保存しない。

### 7.4 StoreAs

`StoreAs` は、現在の Step 戻り値をその型のまま登録する省略 API として扱う。

```csharp
.StoreAs()
```

これは以下の省略形である。

```csharp
.Produce<TOut>(x => x)
```

ここでの `TOut` は、現在の `Run<TStep, TOut>()` の戻り値型である。

ただし、主要 API は `Produce` とする。

理由は、`StoreAs` だけでは上流出力の一部を下流用入力として生成できないためである。

`StoreAs` で登録された値の寿命、可視範囲、重複キーの扱いは `Produce` と同じである。

trace 値を保存する場合は、`StoreAs` にも明示する。

```csharp
.StoreAs(TraceValueCapture traceValueCapture)
```

既存の `StoreAs` は値を trace に保存しない。

### 7.5 Discard

`Discard` は Step の戻り値を `StepInput` に登録しない。

`Discard` は現在 Step の戻り値登録を抑止するだけであり、既に `StepInput` に登録済みの値を削除しない。

`Discard` は trace 値を生成しない。

```csharp
.Run<SaveStep, Unit>()
    .Discard();
```

---

## 8. Step 定義

### 8.1 通常 Step はクラスで定義する

Step はメソッドの連続呼び出しで定義せず、通常の C# クラスとして定義する。

```csharp
public sealed class LoadStep : IStep<LoadResult>
{
    public sealed class Config
    {
        public string Path { get; set; } = "";
    }

    public LoadResult Execute(StepInput input)
    {
        Config config = input.Context.Get<Config>();

        string text = File.ReadAllText(config.Path);

        return new LoadResult
        {
            Text = text,
            FilePath = config.Path
        };
    }
}
```

### 8.2 Step は必要な入力を StepInput から明示的に取得する

```csharp
public sealed class ConvertStep : IStep<ConvertResult>
{
    public sealed class Config
    {
        public bool ToUpper { get; set; }
    }

    public ConvertResult Execute(StepInput input)
    {
        ConvertInput convertInput = input.Get<ConvertInput>();
        Config config = input.Context.Get<Config>();

        return new ConvertResult
        {
            ConvertedText = config.ToUpper
                ? convertInput.Text.ToUpperInvariant()
                : convertInput.Text
        };
    }
}
```

### 8.3 Step は前後の Step を知らない

Step は以下を知らない。

- 自分の前にどの Step があるか
- 自分の後にどの Step があるか
- 自分の入力がどの Step から生成されたか

Step は `StepInput` から必要な値を取得するだけである。

---

## 9. ネスト

### 9.1 CompositeStep は Step として扱える

CompositeStep は `IStep<TOut>` を実装する。

そのため、CompositeStep を別の CompositeStep の中で実行できる。

```text
MainStep
├─ CleanStep
├─ BuildCompositeStep
│  ├─ RestoreStep
│  └─ CompileStep
└─ TestStep
```

### 9.2 csx 分割

CompositeStep 定義は外部 `.csx` に分割できる必要がある。

`#load` による外部 `.csx` 解決を想定する。

```csharp
#load "./build.csx"
#load "./test.csx"
```

`.csx` の実行、NuGet 解決、外部 `.csx` 解決には `Dotnet.Script.Core` を利用する。

---

## 10. Dotnet.Script.Core 利用

### 10.1 必須要件

エンジンは `Dotnet.Script.Core` を利用する。

目的:

- `.csx` の実行
- NuGet パッケージ解決
- `#r "nuget: ..."` の解決
- `#load` による外部 `.csx` 解決

### 10.2 エンジンの役割

エンジンは以下を行う。

1. CLI 引数を解析する
2. 初期 `StepContext` を生成する
3. 初期 `StepInput` を生成する
4. `StepInput` に `StepContext` を含める
5. `.csx` を `Dotnet.Script.Core` 経由でロードする
6. `.csx` 上の CompositeStep 定義を取得する
7. Entry の Step Config メタ情報を確認する
8. 必要な場合は `--config` の root YAML を読み込む
9. Step 登録単位 Config では宣言済み Step Config ごとの既定 Config YAML を読み込む
10. root YAML の宣言済み Step Config 区画を既定 Config YAML に部分上書きする
11. 結合済み YAML 全体を境界 Config 型または Entry 全体 Config 型へ変換する
12. `--set` のプロパティ path override を Config に適用する
13. `DataAnnotations` と `IValidatableObject` で Config を検証する
14. 検証済み Config を対象 Step の実行直前に `StepContext` に登録する
15. 指定された Step を実行する

---

## 11. エラー処理

### 11.1 基本方針

Step 実行中に例外が発生した場合、エンジンは失敗として扱う。

既定動作は停止とする。

### 11.2 FailurePolicy

Step 単位の失敗時動作を拡張する場合は、次の列挙型を使う。

```csharp
public enum FailurePolicy
{
    Stop,
    Continue
}
```

`Continue` の実行継続動作は標準実行契約には含めない。

### 11.3 エラー対象

以下はエラーとする。

- 存在しない Step 名の実行
- `StepInput.Get<T>()` で値が存在しない
- `StepInput.Get<T>(name)` で値が存在しない
- Entry が標準 Config 型を要求しているが `--config` が未指定
- 指定された Config ファイルが存在しない
- Config ファイルの読み込み、YAML 構文、型変換、または検証の失敗
- `--set` の存在しないプロパティ、型変換失敗、または配列またはリストの添字不正
- Step 実行時例外
- `.csx` のロード失敗
- NuGet 解決失敗
- 明示的な厳格指定時の NuGet ロックファイル欠落
- NuGet ロックファイルと参照または解決済み依存関係の不一致
- `#load` 解決失敗

Entry が標準 Config 型を要求している場合の `--config` 未指定と、存在しない Config ファイルは `CONFIG_NOT_FOUND` とする。

読み込み不能、YAML 構文エラー、型変換失敗、`--set` の Config 適用失敗、`DataAnnotations` または `IValidatableObject` の失敗は `CONFIG_LOAD_FAILED` とする。

### 11.4 retry と timeout

Step 本体の通常例外に対する retry を提供する。

retry は `WorkflowExecutionOptions.Retry` で指定する。

`RetryOptions.MaxAttempts` は初回を含む最大試行回数とする。

`Retry = null` または `MaxAttempts <= 1` の場合、retry は行わない。

例えば `MaxAttempts = 3` は、対象 Step を最大 3 回実行することを意味する。

retry は全 Step 一律の指定に限定する。

Step 別 retry、待機時間制御、例外型による絞り込み、CLI 指定、Config 指定は標準 retry 契約には含めない。

retry 対象は Step 本体の通常例外に限定する。

最終的に `STEP_EXECUTION_FAILED` になる候補だけを retry 対象とする。

入力取得失敗、Config 検証失敗、`.csx` ロード失敗、`.csx` コンパイル失敗、参照解決失敗は retry 対象外とする。

`Produce`、`StoreAs`、`Discard` の失敗は retry 対象外とする。

失敗した試行の `Produce`、`StoreAs`、`Discard` は実行しない。

失敗した試行の戻り値由来の値は `StepInput` に残さない。

途中の試行が成功した場合、成功した最後の試行の戻り値だけに `Produce` を実行し、後続 Step は 1 回だけ開始する。

全試行が失敗した場合、後続 Step は開始しない。

timeout は `WorkflowExecutionOptions.StepTimeout` で指定する。

`StepTimeout` の既定値は `null` とし、timeout を設定しない現行動作を維持する。

timeout は強制停止ではなく、`CancellationToken` による協調キャンセルとして扱う。

timeout と外部キャンセルは retry 対象外とする。

timeout は `STEP_TIMEOUT`、外部キャンセルは `STEP_CANCELED` として扱い、どちらも retry を止める。

timeout と外部キャンセルの両方が観測される場合は、外部キャンセルを優先して `STEP_CANCELED` とする。

各試行の timeout は、試行開始時に作成した Step 単位の timeout 用 `CancellationTokenSource` で判定する。

timeout または外部キャンセルで失敗した Step は `Produce`、`StoreAs`、`Discard` を実行せず、値を `StepInput` に残さない。

実行中 Step の強制停止、workflow 全体 timeout は標準実行契約には含めない。

---

## 12. 非同期対応

### 12.1 方針

既存の同期 Step API を維持したまま、非同期 Step 用の明示 API を提供する。

既存の `IStep<TOut>` は同期 Step の契約として維持する。

```csharp
public interface IStep<TOut>
{
    TOut Execute(StepInput input);
}
```

非同期 Step は `IAsyncStep<TOut>` として定義する。

```csharp
public interface IAsyncStep<TOut>
{
    Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken);
}
```

`IStep<TOut>` を `Task<TOut>` 系へ統一しない。

`IStep<Task<T>>` は非同期 Step として特別扱いしない。

この型は通常の同期 Step の戻り値型として扱い、エンジンは `IAsyncStep<TOut>` として登録された Step だけを非同期待機対象にする。

非同期 Step は `RunAsync<TStep, TOut>()` などの明示 API で登録する。

同期 Step と非同期 Step が混在する場合も、エンジンは定義順に 1 Step ずつ実行する。

非同期 Step は `ExecuteAsync` の完了を待ってから、その戻り値に対して `Produce` または `StoreAs` を実行する。

非同期 Step の実行中に例外が発生した場合、同期 Step と同じく `STEP_EXECUTION_FAILED` の実行結果と trace に変換する。

`ExecuteWorkflowAsync` が受け取った外部 `CancellationToken` は、各 Step の実行制御に使う。

`WorkflowExecutionOptions.StepTimeout` が設定されている場合、エンジンは Step 実行ごとに timeout 用の `CancellationTokenSource` を作る。

外部 `CancellationToken` と timeout 用の `CancellationToken` は Step 実行ごとに合成する。

非同期 Step には合成した `CancellationToken` を `IAsyncStep<TOut>.ExecuteAsync` へ渡す。

同期 Step は `CancellationToken` を受け取らないため、実行中の timeout や外部キャンセルで強制中断しない。

同期 Step 実行中に timeout または外部キャンセルが要求された場合、エンジンは同期 Step の完了を待つ。

同期 Step 完了後にキャンセルが要求済みであれば、`Produce`、`StoreAs`、`Discard`、後続 Step を実行せず、失敗結果に変換する。

timeout と外部キャンセルは区別する。

timeout は `STEP_TIMEOUT`、外部キャンセルは `STEP_CANCELED` の失敗結果として扱う。

採用しない案:

```csharp
public interface IStep<TOut>
{
    Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken);
}
```

この案は既存の `IStep<TOut>` 実装を破壊するため、採用しない。

---

## 13. 定義 API 案

### 13.1 基本形

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .Produce<ConvertInput>(x => new ConvertInput
        {
            Text = x.Text
        })
        .Produce<AuditInput>(x => new AuditInput
        {
            FilePath = x.FilePath,
            Length = x.Length
        })
    .Run<ConvertStep, ConvertResult>()
        .Produce<SaveInput>(x => new SaveInput
        {
            Content = x.ConvertedText
        })
    .Run<SaveStep, Unit>()
        .Discard();
```

### 13.2 名前付き Produce

```csharp
var Main = CompositeStep.Define("Main")
    .Run<ReadTitleStep, string>()
        .Produce<string>("title", x => x)
    .Run<ReadBodyStep, string>()
        .Produce<string>("body", x => x)
    .Run<MergeStep, Article>()
        .StoreAs();
```

取得側:

```csharp
public sealed class MergeStep : IStep<Article>
{
    public Article Execute(StepInput input)
    {
        string title = input.Get<string>("title");
        string body = input.Get<string>("body");

        return new Article
        {
            Title = title,
            Body = body
        };
    }
}
```

### 13.3 非同期 Step

非同期 Step は `RunAsync` で明示的に登録する。

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .Produce<ConvertInput>(x => new ConvertInput
        {
            Text = x.Text
        })
    .RunAsync<ConvertStep, ConvertResult>()
        .Produce<SaveInput>(x => new SaveInput
        {
            Content = x.ConvertedText
        })
    .Run<SaveStep, Unit>()
        .Discard();
```

`RunAsync` で登録された Step は、非同期待機後の戻り値を `Produce`、`StoreAs`、`Discard` の対象にする。

### 13.4 Step 登録単位 Config の例

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
        .Produce<ConvertInput>(x => new ConvertInput
        {
            Text = x.Text
        })
    .Run<ConvertStep, ConvertResult>()
        .WithConfig<ConvertStep.Config>("Convert")
        .Produce<SaveInput>(x => new SaveInput
        {
            Content = x.ConvertedText
        })
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

---

## 14. 主要公開 API 案

### 14.1 Step 契約

```csharp
public interface IStep<TOut>
{
    TOut Execute(StepInput input);
}

public interface IAsyncStep<TOut>
{
    Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken);
}
```

`IStep<TOut>` は同期 Step 用の既存契約として維持する。

`IAsyncStep<TOut>` は非同期 Step 用の追加契約とする。

### 14.2 StepInput

```csharp
public sealed class StepInput
{
    public StepContext Context { get; }

    public T Get<T>();

    public T Get<T>(string name);

    public bool TryGet<T>(out T value);

    public bool TryGet<T>(string name, out T value);
}
```

### 14.3 StepContext

```csharp
public sealed class StepContext
{
    public ILogger Logger { get; }

    public T Get<T>();

    public T Get<T>(string name);

    public void Set<T>(T value);

    public void Set<T>(string name, T value);

    public bool TryGet<T>(out T value);

    public bool TryGet<T>(string name, out T value);
}
```

### 14.4 CompositeStep

```csharp
public static class CompositeStep
{
    public static CompositeStepDefinition Define(
        string name,
        string? namespaceName = null);
}

public sealed class CompositeStep<TOut> : IStep<TOut>, IAsyncStep<TOut>
{
    public CompositeStep<TOut> WithConfig<TConfig>();

    public CompositeStep<TOut> WithConfig<TConfig>(string sectionPath);

    public CompositeStep<TOut> WithConfig<TConfig>(
        string sectionPath,
        string defaultConfigPath);

    public CompositeStep<TStepOut> Run<TStep, TStepOut>()
        where TStep : IStep<TStepOut>, new();

    public CompositeStep<TStepOut> RunAsync<TStep, TStepOut>()
        where TStep : IAsyncStep<TStepOut>, new();

    public TOut Execute(StepInput input);

    public Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken);

    public WorkflowResult ExecuteWorkflow(WorkflowExecutionOptions? options = null);

    public Task<WorkflowResult> ExecuteWorkflowAsync(
        WorkflowExecutionOptions? options = null,
        CancellationToken cancellationToken = default);

    public Type? ConfigType { get; }

    public IReadOnlyList<StepConfigRegistration> StepConfigRegistrations { get; }

    public string Name { get; }

    public string? NamespaceName { get; }

    public string QualifiedName { get; }
}

public sealed class StepConfigRegistration
{
    public Type StepType { get; }

    public string SectionPath { get; }

    public Type ConfigType { get; }

    public string? DefaultConfigPath { get; }
}
```

`WithConfig<TConfig>(string sectionPath)` は直前に登録した Step に Step Config 型と境界 Config 型上のプロパティ path のメタ情報を設定する。既定 Config YAML は規約 path から解決する。`StepConfigRegistrations` は Entry 内の Step 登録順で保持する。

`WithConfig<TConfig>(string sectionPath, string defaultConfigPath)` は、直前に登録した Step に Step Config 型、境界 Config 型上のプロパティ path、既定 Config YAML path のメタ情報を設定する。`DefaultConfigPath` が null ではない場合は規約 path より優先する。

`WithConfig<TConfig>()` は、Step 登録単位 Config がある場合は CompositeStep 境界 Config 型メタ情報を設定する。Step 登録単位 Config がない場合は、互換 API として Entry 全体 Config 型メタ情報を設定する。1 つの Entry に設定できる Config 型は 1 つだけとする。

`CompositeStep.Define("Build", namespaceName: "Deploy")` は、短い名前 `Build`、名前空間 `Deploy`、完全修飾名 `Deploy.Build` を持つ Entry を定義する。

`CompositeStep.Define("Build")` は従来互換のため名前空間なしの Entry を定義し、完全修飾名は短い名前と同じ `Build` とする。

`WithConfig<TConfig>()`、`WithConfig<TConfig>(string sectionPath)`、`Run<TStep, TStepOut>()`、`RunAsync<TStep, TStepOut>()`、`Produce`、`StoreAs`、`Discard` など、`CompositeStep<TOut>` を返す連鎖呼び出しの後も、名前空間メタ情報と完全修飾名は維持する。

### 14.5 `WorkflowExecutionOptions`

```csharp
public sealed class WorkflowExecutionOptions
{
    /// <summary>
    /// Step ごとの timeout 時間を取得または設定します。null の場合は timeout を設定しません。
    /// </summary>
    public TimeSpan? StepTimeout { get; init; }

    /// <summary>
    /// Step 本体の通常例外に対する retry 設定を取得または設定します。null の場合は retry しません。
    /// </summary>
    public RetryOptions? Retry { get; init; }

    /// <summary>
    /// エンジンと Step のログ出力に使う logger factory を取得または設定します。
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>
    /// CLI 由来の config path と set override を StepContext に渡すための引数を取得または設定します。
    /// </summary>
    public EngineArguments? EngineArguments { get; init; }
}

public sealed class RetryOptions
{
    /// <summary>
    /// 初回を含む最大試行回数を取得または設定します。1 以下の場合は retry しません。
    /// </summary>
    public int MaxAttempts { get; init; }
}
```

`StepTimeout` は workflow 全体ではなく、Step 実行ごとの timeout として扱う。

`StepTimeout` が `null` の場合、timeout 用の `CancellationTokenSource` は作らず、外部 `CancellationToken` だけを使う。

`StepTimeout` は CLI オプションではなく、エンジン実行時オプションとして扱う。

`Retry` は全 Step 一律の retry 設定として扱う。

`Retry` が `null`、または `Retry.MaxAttempts <= 1` の場合、retry は行わない。

`Retry.MaxAttempts` は初回を含む最大試行回数であり、`MaxAttempts = 3` は最大 3 回の Step 本体実行を表す。

`Retry` は CLI オプションまたは Config から直接指定しない。

標準 Config 型と境界 Config プロパティ path は `WorkflowExecutionOptions` ではなく、Entry の `CompositeStep` メタ情報から取得する。

CLI `run` は `.csx` ロード後に Entry を解決し、Entry の `ConfigType`、`StepConfigRegistrations`、`EngineArguments.ConfigPath`、`EngineArguments.Settings` を使って Step 登録単位 Config を読み込む。Entry 全体 Config 互換 API を使う場合は、互換メタ情報の `ConfigType` を使う。

`EngineArguments.Settings` は元の文字列を保持する。標準 Config に適用された後も、Step は CLI 指定値そのものを参照できる。

YAML 解析器は実装時に .NET 依存を追加してよい。候補として `YamlDotNet` を利用できるが、設計は特定ライブラリに強く依存しない。

### 14.6 Unit

```csharp
public readonly struct Unit
{
    public static readonly Unit Value = new Unit();
}
```

### 14.7 型定義方針

Step の入力、出力、Config は C# 型として `.csx` に定義する。

Step 間で受け渡す値は `Message` として特別な基底型を要求しない。

ただし、設計上は以下を推奨する。

- `#nullable enable` を有効にする
- 入出力値は `record` などの不変な型で定義する
- 入出力値と Config は `DataAnnotations` による検証対象にできるようにする
- 業務固有の複雑な検証が必要な場合は `IValidatableObject` を利用できるようにする

Config は `StepContext` に登録した後、Run 中は読み取り専用のスナップショットとして扱う。

Config を差し替える場合は、別名の値として登録するか、明示的な上書き規則を持つ Config 読み込み Step で行う。

---

## 15. 実行入口とファイル構成

### 15.1 Entry

ワークフローの実行入口は、名前付きの `CompositeStep` とする。

CLI で実行入口を明示しない場合、エンジンは `Main` を既定の Entry 名として扱う。

```bash
engine run main.csx
engine run main.csx --entry Build
engine run main.csx --entry Deploy.Build
```

`.csx` 上では以下のように名前付き Step を定義する。

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<MainConfig>()
        .WithConfig<LoadStep.Config>("Load")
        .Produce<ConvertInput>(x => new ConvertInput { Text = x.Text })
    .Run<ConvertStep, ConvertResult>()
        .WithConfig<ConvertStep.Config>("Convert")
        .StoreAs();

var DeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
    .Run<DeployBuildStep, Unit>();
```

エンジンはロード済み `.csx` から、指定 Entry 名に一致する `CompositeStep` を取得して実行する。

Entry 名の解決は script 変数名ではなく、`CompositeStep` の公開名で行う。公開名は短い `Name` と完全修飾名の `QualifiedName` である。script 変数名は C# script 上の識別子であり、Entry の契約ではない。

`CompositeStep.Define("Build", namespaceName: "Deploy")` の完全修飾名は `Deploy.Build` とする。CLI は `--entry Deploy.Build` で名前空間付き Entry を指定する。

名前空間なしの既存 `CompositeStep.Define("Build")` は互換維持し、完全修飾名は短い名前と同じ `Build` とする。

短い `--entry Build` は、名前空間なしの `Build` が存在する場合はその Entry へ解決する。名前空間なしの `Build` が存在しない場合は、短い名前が `Build` である Entry が 1 件だけなら互換解決する。複数の名前空間に同じ短い名前がある場合、短い `--entry Build` は曖昧として検証と実行を失敗させ、利用者に `Deploy.Build` のような完全修飾名指定を求める。

曖昧な短い Entry 指定は、完全修飾名の重複ではないため `DUPLICATE_STEP_NAME` ではなく `ENTRY_STEP_NOT_FOUND` とする。エラーメッセージには、短い Entry 名が複数候補へ一致したことと、完全修飾名を指定すべきことを含める。

`#load` 先で定義された名前空間付き Entry も、Entry `.csx` に直接定義された Entry と同じ規則で解決する。

非同期 Step を含む Entry の通常実行では、エンジンは `ExecuteWorkflowAsync` を使って非同期 Step の完了を待つ。

同期 Step だけの Entry では、既存の `ExecuteWorkflow` を維持する。

指定された Entry が存在しない場合は検証エラーとする。

### 15.2 Step 名の一意性

ロード済み `.csx` 全体で、実行対象として公開される `CompositeStep` の完全修飾名は一意でなければならない。

重複判定は完全修飾名単位とする。`Deploy.Build` と `Test.Build` は異なる完全修飾名なので共存できる。`Deploy.Build` が複数見つかった場合、エンジンは実行前の検証で `DUPLICATE_STEP_NAME` として失敗する。

名前空間なしの `Build` と名前空間付きの `Deploy.Build` も異なる完全修飾名なので共存できる。この場合、短い `--entry Build` は名前空間なしの `Build` に解決する。

外部 `.csx` を `#load` した場合も、読み込み後の全体で同じ規則を適用する。

### 15.3 ファイル構成

標準的な構成は以下とする。

```text
workflow-root/
├── main.csx
├── build.csx
├── test.csx
├── steps/
│   ├── load/
│   │   ├── load-step.csx
│   │   └── appsettings.yaml
│   ├── convert/
│   │   ├── convert-step.csx
│   │   └── appsettings.yaml
│   └── save/
│       ├── save-step.csx
│       └── appsettings.yaml
├── shared/
│   └── common.csx
├── config/
│   └── appsettings.yaml
└── lib/
    └── custom-helper.dll
```

`main.csx` はワークフロー定義の入口であり、必要な外部 `.csx` を `#load` する。

相対パスの基準は、原則として実行対象に指定した Entry `.csx` の存在するディレクトリとする。

`#load` 内の相対パスは、`#load` を書いた `.csx` の存在するディレクトリを基準とする。

`steps/` 配下の `appsettings.yaml` は、宣言済み Step Config の既定値として置ける。標準実行時に `--config` へ渡す Config は `config/appsettings.yaml` のような境界 Config 型に対応する root Config ファイルである。エンジンは宣言済み Step Config ごとに Step 側既定 Config YAML を読み込み、root Config の該当区画を部分上書きとして重ねてから境界 Config 型へ変換する。

たとえば `WithConfig<LoadStep.Config>("Load")` は、Entry `.csx` の存在するディレクトリを基準に `steps/load/appsettings.yaml` を既定 Config YAML として読む。`WithConfig<ConvertStep.Config>("Text.Convert")` は `steps/text/convert/appsettings.yaml` を読む。規約パスと異なる既定 Config YAML を使う場合は、`WithConfig<TConfig>(sectionPath, defaultConfigPath)` で明示する。

### 15.4 トップレベルステートメント

Entry `.csx` と外部 `.csx` のトップレベルステートメントは、Step 定義、型定義、`using`、`#load`、`#r` に限定することを推奨する。

検証時にも `.csx` のロードや評価が必要になるため、トップレベルでファイル削除、外部通信、環境変更などの副作用を起こしてはならない。

副作用のあるトップレベルステートメントを完全には検出しない。

利用者は、同期処理を `IStep<TOut>.Execute` 内に置く。

非同期処理は `IAsyncStep<TOut>.ExecuteAsync` 内に置く。

---

## 16. csx 解決と参照方針

### 16.1 Dotnet.Script.Core の利用範囲

`Dotnet.Script.Core` は、`.csx` のロード、`#load` 解決、`#r` 解決、NuGet 復元、Roslyn コンパイルに使う。

Step 実行、`StepInput` 構築、Config 保持、検証、ログ、実行結果の生成はエンジン側で制御する。

通常実行経路では、Step ごとに外部プロセスとして `dotnet script` CLI を起動しない。

### 16.2 `#load` 解決

ローカルファイルの `#load` に対応する。

```csharp
#load "./build.csx"
#load "./steps/load-step.csx"
```

解決規則は以下とする。

| 項目 | 規則 |
| --- | --- |
| 相対パス基準 | `#load` を書いた `.csx` のディレクトリ |
| パス正規化 | `..` とシンボリックリンクを解決した正規パス |
| root 制限 | `workflow-root` 配下のみ許可 |
| 循環読み込み | 検出して検証エラー |
| 重複読み込み | 同一正規パスは 1 回だけ読み込む |

`dotnet-script` 互換の NuGet script パッケージ読み込みに対応する。

```csharp
#load "nuget: Simple.Targets.Csx, 6.0.0"
```

文法は `#load "nuget: PackageId, Version"` のみとする。

`#load "nuget: PackageId, Version, path/to/file.csx"` のようにパッケージ内 path をディレクティブで指定する独自文法は採用しない。

NuGet script パッケージのパッケージ内 `.csx` 探索、`contentFiles` または `content` 配下の script 選択、入口判定、`project.assets.json` 解析、実行時 assembly 解決は、`Dotnet.Script.Core` と `Dotnet.Script.DependencyModel` に委ねる。

エンジン側では NuGet キャッシュの配置、パッケージ内 `contentFiles` 選択規則、`project.assets.json` 解析、実行時 assembly 解決を再実装しない。

provider 契約は、`RuntimeDependency.Scripts` 相当のパッケージ script 解決情報、または最終コンパイル時に `NuGetSourceReferenceResolver` へ渡せる script 解決情報を返せる必要がある。

`#load "nuget: ..."` のディレクティブを source に残す経路では、最終コンパイルに `NuGetSourceReferenceResolver` が有効な `ScriptOptions` または同等の `ScriptCompilationContext` を使う。

ローカル `#load` の循環は、読み込み中の正規パス一覧で検出し、`SCRIPT_LOAD_CYCLE_DETECTED` とする。

ローカル `#load` の重複は、同一正規パスを 1 回だけ読み込む。

NuGet script 読み込みの循環は、パッケージ ID、解決済み version、パッケージ内 script path からなる script 識別子で扱う。

ローカル script と NuGet script をまたぐ循環を検出できる場合も `SCRIPT_LOAD_CYCLE_DETECTED` とする。

NuGet source 解決機構または Roslyn 側で循環が検出された場合も、識別できる範囲では `SCRIPT_LOAD_CYCLE_DETECTED` に正規化する。

NuGet script 読み込みの重複は、同一パッケージ ID、解決済み version、パッケージ内 script path の script 識別子を 1 回だけ読み込む。

同一パッケージの複数 script 入口やパッケージ内 script からの相対 `#load` の展開順は、`dotnet-script` の `NuGetSourceReferenceResolver` と script パッケージ解決規則に従う。

### 16.3 `#r` と NuGet 参照

以下を明示許可された場合に限り対応する。

```csharp
#r "System.Text.Json"
#r "./lib/custom-helper.dll"
#r "nuget: CsvHelper, 33.0.1"
```

参照規則は以下とする。

| 項目 | 規則 |
| --- | --- |
| assembly 名参照 | 許可一覧に登録された名前のみ許可 |
| ファイル参照 | 許可一覧に登録されたディレクトリ配下のみ許可 |
| NuGet 参照 | 許可一覧に登録されたパッケージ ID とバージョンのみ許可 |
| 浮動バージョン | 禁止 |
| パッケージ参照元 | 通常の NuGet 設定に従う。`verifyPackageSources: true` の場合だけロックファイルと照合 |

NuGet 復元と依存関係解決は `Dotnet.Script.Core` と `Dotnet.Script.DependencyModel` の仕組みを利用する。

エンジン側では `dotnet restore` の起動、一時 `.csproj` の生成、`project.assets.json` の解析、実行時 assembly 解決を再実装しない。

NuGet ロックファイルは任意であり、存在する場合に `#r "nuget: package, version"` と `#load "nuget: package, version"` の再現性を対象とする。

`#load "nuget: package, version"` も NuGet 直接参照として扱い、`devo6.nuget.lock.yaml` の `directReferences`、`resolvedDependencies`、`metadata` 比較の対象に含める。

`directReferences` はディレクティブ種別を区別しないパッケージ ID と version の正規化集合とし、`#r` と `#load` の両方から得た直接 NuGet パッケージ参照を含める。

`#load "nuget: ..."` はパッケージ内 script を実行可能 source として取り込むが、新しい許可一覧を増やさず、NuGet `#r` と同じ許可済みパッケージ ID と version の規則を適用する。

ロックファイルを使う場合は Entry `.csx` の workflow root に置く YAML とし、ファイル名は `devo6.nuget.lock.yaml` とする。

ロックファイルには以下を記録する。

- 直接参照 (`directReferences`)
- 解決済み依存関係 (`resolvedDependencies`)
- 対象 (`targetFramework`)
- 実行時識別子 (`runtimeIdentifier`)
- 参照元照合の有無 (`verifyPackageSources`)
- 必要な場合だけパッケージ参照元 (`packageSources`)
- `Dotnet.Script.Core` version

`verifyPackageSources` の既定値は `false` とする。未指定または `false` の場合、NuGet 復元は NuGet.Config など通常の NuGet 参照元を使い、ロック検証では `packageSources` の必須チェックと一致チェックを行わない。

`verifyPackageSources: true` の場合だけ `packageSources` を必須の再現性情報とし、実際の NuGet 参照元一覧と順序を無視して一致することを検証する。

`devo6.nuget.lock.yaml` が無い場合の既定動作は、通常の NuGet 設定へ委ねて依存関係を解決し、ロック比較を行わない。`NuGet.config` がある場合は、`dotnet-script` や NuGet と同じ通常の NuGet 設定として参照元に反映される。

明示的な厳格指定では `devo6.nuget.lock.yaml` の存在を必須とし、NuGet 直接参照があるのにロックファイルが無い場合は `SCRIPT_NUGET_LOCK_MISSING` とする。CLI では `run` と `validate` の `--locked` がこの指定に対応する。

絶対実行時 assembly path は実行環境に依存するため、ロックファイルには記録しない。

ロック検証の順序は以下とする。

1. 許可外 NuGet 参照、浮動バージョン、`dotnet-script` 互換でない NuGet `#load` 文法を `SCRIPT_REFERENCE_NOT_ALLOWED` として拒否する
2. 明示的な厳格指定があり、NuGet 直接参照があるのに `devo6.nuget.lock.yaml` が無い場合は、復元前に `SCRIPT_NUGET_LOCK_MISSING` として拒否する
3. ロックファイルが無い場合、`Dotnet.Script.Core` による依存関係解決を行い、ロック比較を行わない
4. ロックファイルがある場合、直接参照のパッケージ ID と version がロックファイルと一致しないときは、復元前に `SCRIPT_NUGET_LOCK_MISMATCH` として拒否する
5. ロックファイルに `targetFramework`、`runtimeIdentifier`、`Dotnet.Script.Core` version が無い場合、または `verifyPackageSources: true` で `packageSources` が無い場合は、復元前に `SCRIPT_NUGET_LOCK_MISMATCH` として拒否する
6. `Dotnet.Script.Core` による依存関係解決を行う
7. 復元そのものが失敗した場合は `SCRIPT_NUGET_RESTORE_FAILED` とする
8. ロックファイルがある場合、解決済み依存関係、または `targetFramework`、`runtimeIdentifier`、`Dotnet.Script.Core` version がロックファイルと一致しないときは `SCRIPT_NUGET_LOCK_MISMATCH` とする
9. `verifyPackageSources: true` の場合だけ、実際の参照元一覧との不一致を `SCRIPT_NUGET_LOCK_MISMATCH` とする

リポジトリ側はロックファイルの読み書き、明示的な厳格指定での欠落、不一致、許可済み直接参照の完全一致、解決済み依存関係の比較だけを薄く持つ。

通常の `dotnet test` が外部通信に依存しないよう、依存関係 provider は注入できる設計にする。

本番 provider は `Dotnet.Script.Core` と `Dotnet.Script.DependencyModel` を使い、検査 provider は固定データを返す。

通常検査では偽 provider などの固定データを使い、外部 NuGet source への通信を必須にしない。

必要な場合だけ、ローカルの NuGet 参照元を使う追加検証を分けて用意する。

ただし、NuGet 参照は実行できるコードを増やすため、信頼済みワークフローでのみ使う。

### 16.4 AssemblyLoadContext

ワークフロー単位で `AssemblyLoadContext` を分離してよい。

エンジンが公開する API の assembly はホスト側と共有する。

Script 側で公開 API assembly の別コピーが読み込まれた場合、型名が同じでも CLR 上は別型になる。

その場合、`IStep<TOut>`、`StepInput`、`StepContext` の受け渡しが壊れるため、検証エラーとする。

### 16.5 キャッシュ

コンパイルキャッシュを使う場合、最低限以下をキャッシュキーに含める。

- Entry `.csx` の正規パスと内容ハッシュ
- `#load` された全 `.csx` の正規パスと内容ハッシュ
- `#r` の参照一覧
- `#load "nuget: ..."` から解決されたパッケージ script のパッケージ ID、version、パッケージ内 script 識別子
- NuGet パッケージ ID、バージョン、参照元
- エンジンのバージョン

いずれかが変わった場合、該当キャッシュは無効化する。

`Dotnet.Script.DependencyModel.Context.CachedRestorer` は復元結果を再利用するための性能キャッシュであり、利用者向け NuGet ロックファイルではない。

`script.csproj.cache` や `project.assets.json` をリポジトリの公開ロックファイル仕様として扱わない。

### 16.6 信頼境界

`.csx` は信頼済みワークフローのみを実行対象とする。

未信頼ユーザーがアップロードした `.csx` を、このエンジンで直接実行してはならない。

以下は提供しない。

- 完全なサンドボックス
- プロセス分離
- OS 権限制御
- ネットワーク制限
- ファイル入出力制限
- NuGet 利用制限の完全強制
- 署名検証
- シークレットアクセス制限

`.csx` は任意の C# コードであり、ファイル入出力、ネットワークアクセス、環境変数参照、プロセス起動、リフレクション、シークレットアクセスを行える可能性がある。

参照許可一覧は誤用を減らすための検証規則であり、未信頼コード実行を安全化する境界ではない。

`Dotnet.Script.Core` は依存解決とコンパイルを簡略化するが、セキュリティ境界ではない。

---

## 17. 検証

### 17.1 検証コマンド

CLI は実行前検証のために `validate` を提供する。

```bash
engine validate main.csx
engine validate main.csx --entry Build
engine validate main.csx --entry Deploy.Build
engine validate main.csx --config appsettings.yaml
```

### 17.2 検証対象

検証対象は以下とする。

- Entry `.csx` の存在
- 指定 Entry 名が `CompositeStep` の公開名へ解決できること
- `CompositeStep` の完全修飾名の重複
- `#load` の参照解決
- `#load` の循環
- `#r` の許可判定
- NuGet 参照の許可判定
- 明示的な厳格指定時の NuGet ロックファイル欠落
- NuGet ロックファイルと直接参照の不一致
- NuGet ロックファイルと解決済み依存関係の不一致
- `.csx` のコンパイル
- `IStep<TOut>` または `IAsyncStep<TOut>` 実装の確認
- `StepInput` と `StepContext` の API 互換
- Config ファイル指定時の存在確認

実行時の `StepInput` 内容に依存する型検証は、実行時に行う。

Entry 解決と重複検証は、Entry `.csx` と `#load` 先を読み込んだ全 `CompositeStep` を対象にする。

`--entry Deploy.Build` は `CompositeStep.QualifiedName` と完全一致する Entry を要求する。

短い `--entry Build` は、名前空間なしの `Build` があればそれを優先し、なければ短い名前が一意な Entry に互換解決する。複数候補がある場合は `ENTRY_STEP_NOT_FOUND` として失敗させ、メッセージで完全修飾名指定を促す。

完全修飾名が重複する場合は、指定 Entry の有無にかかわらず `DUPLICATE_STEP_NAME` として失敗する。

`validate` は Config path の存在確認までを必須とする。Config 型変換、`--set` の override 適用、Config 値検証は `validate` では行わず、`run` 時に行う。

### 17.3 StepInput 検証

`StepInput.Get<T>()` と `StepInput.Get<T>(name)` は、値が存在しない場合や型が一致しない場合に失敗する。

失敗時は Step を実行せず、エンジンの実行結果を失敗にする。

`TryGet` は失敗を戻り値で返し、エンジンの失敗にはしない。

### 17.4 Config 検証

CLI `run` では、Entry が Step 登録単位 Config API または Entry 全体 Config 互換 API を使う場合に標準 Config 読み込みを行う。

`--config` と `--set` は `EngineArguments` として `StepContext` に格納する。

Step 登録単位 Config API では、宣言済み Step Config ごとに既定 Config YAML を読み込む。root YAML に該当区画が存在する場合は、既定 Config YAML に対する部分上書きとして適用する。その後、結合済み YAML 全体を CompositeStep 境界 Config 型に変換し、`--set` を境界 Config 型のプロパティ path override として適用し、`DataAnnotations` と `IValidatableObject` を境界 Config 型から検証する。

宣言済み `sectionPath` が root YAML に存在しなくても、既定 Config YAML が存在する場合はその既定値だけで Step Config を生成できる。

宣言済み `sectionPath` に対応する既定 Config YAML も root YAML 区画も存在しない場合は、Config 型の生成や override 適用へ進まず、最初の Step 実行前に `CONFIG_LOAD_FAILED` とする。

既定 Config YAML が存在するが読み込めない場合、または YAML として読み込めない場合は、最初の Step 実行前に `CONFIG_LOAD_FAILED` とする。

root YAML の宣言済み `sectionPath` が YAML path だけの場合は、既存互換の省略形として、その YAML を既定 Config YAML として扱う。

Step 登録単位 Config があるのに CompositeStep 境界 Config 型が宣言されていない場合は、最初の Step 実行前に `CONFIG_LOAD_FAILED` とする。

境界 Config と宣言済み Step Config の対応はすべて最初の Step 実行前に検証する。1 つでも検証に失敗した場合、最初の Step は実行せず `CONFIG_LOAD_FAILED` とする。

検証に成功した Step Config は、Step 専用引数ではなく対象 Step の実行直前に `StepContext` に登録する。

検証に失敗した Config は `StepContext` に登録してはならない。

宣言済み `sectionPath` が YAML 上に存在し、その値が空または `{}` の場合は、Step Config 型を生成でき、検証に通れば成功とする。検証に失敗した場合は `CONFIG_LOAD_FAILED` とする。

Entry 全体 Config 互換 API では、従来どおり YAML 全体を `TConfig` に型変換し、`--set` key 全体を `TConfig` へ適用してから検証する。

ユーザーが定義した Config 読み込み Step を使う場合は、その Step 内で型変換と検証を行う。

### 17.5 Step 出力検証

Step の戻り値が `null` であり、`TOut` が null を許さない型である場合、エンジンは Step 失敗として扱う。

Step 出力に `DataAnnotations` または `IValidatableObject` が使われている場合、エンジンは検証対象にできる。

Step 出力の `DataAnnotations` と `IValidatableObject` による自動検証は標準実行契約には含めない。

ただし、検証を無効にした場合でも、`Produce` の選択関数が失敗した場合は Step 失敗として扱う。

### 17.6 検証エラー形式

検証エラーは最低限以下を持つ。

```csharp
public sealed class ValidationError
{
    public string Path { get; init; } = "";
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
}
```

`Path` には、`StepInput` の型名、名前付きキー、Config のプロパティのパス、Step 名など、利用者が原因を特定できる情報を入れる。

---

## 18. 実行結果、ログ、トレース

### 18.1 実行結果

エンジンは実行後に `WorkflowResult` を返す。

`WorkflowResult` は最低限以下を持つ。

```csharp
public sealed class WorkflowResult
{
    public string EntryName { get; init; } = "";
    public bool Succeeded { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public ExecutionTrace? Trace { get; init; }
}
```

`EntryName` は実行された Entry の完全修飾名を記録する。名前空間なしの Entry では従来どおり短い名前と同じ値になる。名前空間付き Entry では `Deploy.Build` のような完全修飾名を記録する。

CLI 実行では、成功時は終了コード 0、失敗時は 0 以外を返す。

非同期ワークフロー実行 API として `ExecuteWorkflowAsync` を追加する。

非同期 Step を含むワークフローでは、各非同期 Step の完了を待ってから後続 Step へ進む。

非同期 Step の完了後に `Produce`、`StoreAs`、`Discard` を実行する。

retry が有効な場合、Step 本体の通常例外は `RetryOptions.MaxAttempts` まで再試行する。

途中の試行が成功した場合、エンジンは成功した試行の戻り値だけに `Produce`、`StoreAs`、`Discard` を実行する。

失敗した試行の `Produce`、`StoreAs`、`Discard` は実行しない。

失敗した試行の戻り値由来の値は `StepInput` に残さない。

全試行が失敗した場合、エンジンは `WorkflowResult.Succeeded = false` の失敗結果を返す。

全試行失敗時の `ErrorCode` は `STEP_EXECUTION_FAILED` とする。

全試行失敗時の `ErrorMessage` は、最後に観測した例外 message を基本とする。

全試行失敗後、エンジンは後続 Step を開始しない。

Step timeout または外部キャンセルが発生した場合、エンジンは `WorkflowResult.Succeeded = false` の失敗結果を返す。

timeout の場合、`ErrorCode` は `STEP_TIMEOUT` とする。

外部キャンセルの場合、`ErrorCode` は `STEP_CANCELED` とする。

timeout または外部キャンセルで Step が失敗した場合、その Step の `Produce`、`StoreAs`、`Discard` は実行しない。

timeout または外部キャンセルで失敗した Step の戻り値由来の値は `StepInput` に残さない。

timeout または外部キャンセルを検出した後、エンジンは後続 Step を開始しない。

timeout と外部キャンセルは retry 対象外とし、観測した時点で workflow を失敗終了する。

trace 値の基礎単位は、Step 成功後に `Produce` または `StoreAs` の値登録処理が成功して `StepInput` に登録された値である。

Step 本体失敗、retry 途中失敗、timeout、外部キャンセルでは値生成処理を実行しないため、当該 trace の値一覧は空である。

値生成処理の失敗または重複登録失敗では当該 Step を失敗 trace とし、trace 値の一覧は空にする。

複数の値生成処理の一部が成功していても、Step が値生成処理の失敗または重複登録失敗になった場合は、部分的に成功した値も失敗 trace へ保存しない。

### 18.2 エラーコード

代表的なエラーコードは以下とする。

```text
ENTRY_SCRIPT_NOT_FOUND
ENTRY_STEP_NOT_FOUND
DUPLICATE_STEP_NAME
SCRIPT_COMPILE_FAILED
SCRIPT_LOAD_FAILED
SCRIPT_LOAD_CYCLE_DETECTED
SCRIPT_REFERENCE_NOT_ALLOWED
SCRIPT_NUGET_LOCK_MISSING
SCRIPT_NUGET_LOCK_MISMATCH
SCRIPT_NUGET_RESTORE_FAILED
SCRIPT_API_IDENTITY_MISMATCH
STEP_INPUT_NOT_FOUND
STEP_INPUT_TYPE_MISMATCH
CONFIG_NOT_FOUND
CONFIG_LOAD_FAILED
STEP_CANCELED
STEP_EXECUTION_FAILED
STEP_TIMEOUT
TRACE_SERIALIZATION_FAILED
```

非同期 Step の例外は、同期 Step の例外と同じく `STEP_EXECUTION_FAILED` に変換する。

timeout 用の `CancellationToken` によって Step 実行がキャンセルされた場合は `STEP_TIMEOUT` に変換する。

`ExecuteWorkflowAsync` に渡された外部 `CancellationToken` によって Step 実行がキャンセルされた場合は `STEP_CANCELED` に変換する。

timeout と外部キャンセルの両方が観測される場合は、外部キャンセルを優先して `STEP_CANCELED` とする。

`STEP_CANCELED` は通常の Step 例外を表す `STEP_EXECUTION_FAILED` とは区別する。

retry で全試行が失敗した場合も、retry 専用エラーコードは追加せず `STEP_EXECUTION_FAILED` を使う。

`Produce`、`StoreAs`、`Discard` の失敗は retry 対象外とし、既存の Step 失敗として扱う。

明示的な厳格指定で NuGet ロックファイルが必要な workflow root に存在しない場合は `SCRIPT_NUGET_LOCK_MISSING` とする。

ロックファイルと直接参照、またはロックファイルと解決済み依存関係が一致しない場合は `SCRIPT_NUGET_LOCK_MISMATCH` とする。

`Dotnet.Script.Core` による NuGet 復元そのものが失敗した場合は `SCRIPT_NUGET_RESTORE_FAILED` とする。

`#load "nuget: ..."` では、未許可 NuGet、浮動 version、`dotnet-script` 互換でない文法を `SCRIPT_REFERENCE_NOT_ALLOWED` とする。

`devo6.nuget.lock.yaml` が欠落している場合でも、既定では復元に進み、通常の NuGet 設定へ委ねる。

明示的な厳格指定で `devo6.nuget.lock.yaml` が欠落している場合だけ、復元を試みる前に `SCRIPT_NUGET_LOCK_MISSING` を返す。

直接参照の不一致は復元前に `SCRIPT_NUGET_LOCK_MISMATCH` とし、復元失敗の `SCRIPT_NUGET_RESTORE_FAILED` より優先する。

復元後の解決済み依存関係または `metadata` の不一致は `SCRIPT_NUGET_LOCK_MISMATCH` とする。

trace 値を `System.Text.Json` で直列化できない場合でも、既定動作では workflow を失敗させない。

`TRACE_SERIALIZATION_FAILED` は、trace 外部保存や厳格動作で workflow 失敗を選ぶ拡張用に残す。既定 workflow 失敗には使わない。

### 18.3 ログ

ログはエンジンで独自実装せず、`Microsoft.Extensions.Logging` を利用する。

エンジンは `ILoggerFactory` を外部から受け取り、具体的な logger provider には直接依存しない。

ユーザー Step は `StepContext.Logger` から記録出力を取得する。

ログには、Entry 名、Step 名、実行状態、失敗時のエラーコードを含める。

ログの `EntryName` は `WorkflowResult.EntryName` と同じ完全修飾名とする。

ログの `StepName` は従来どおり、実行された Step 型名を基本とする。個々の Step 型名を名前空間付き Entry 名へ変換しない。

ログ出力では文字列連結ではなく、構造化ログを使う。

エンジンは Entry 名、Step 名、試行番号をログのスコープへ含める。

ログのスコープの `Attempt` は実試行番号とする。

retry が発生する場合、エンジンは試行ごとの Step 失敗、retry 予定、最終失敗を構造化ログとして記録する。

retry 予定のログには、`EntryName`、`StepName`、`Attempt`、次の試行番号、`ErrorCode` を含める。

最終失敗のログには、`EntryName`、`StepName`、`Attempt`、`ErrorCode`、最終試行であることを含める。

timeout または外部キャンセルで終了した場合、対象 Step 名とエラーコードをログに含める。

Serilog、NLog、OpenTelemetry などへの転送は、利用者が選択した logger provider に委譲する。

### 18.4 トレース

ログと `ExecutionTrace` は分離する。

| 要素 | 役割 |
| --- | --- |
| ログ | 実行中の観測と障害調査 |
| ExecutionTrace | 実行結果として保存できる構造化履歴 |

`StepInput`、Config、Step 出力の値そのものは既定では保存しない。

値を含む trace の基礎単位は「Step 成功後に `Produce` または `StoreAs` の値登録処理が成功して `StepInput` に登録された値」とする。

既存の `Produce`、名前付き `Produce`、`StoreAs` は値を trace に保存しない。

trace 値は、`TraceValueCapture.Serialized` または `TraceValueCapture.Redacted` を明示した値生成処理だけが生成する。

`Discard` は trace 値を生成しない。

同期 Step と非同期 Step が混在する場合も、trace は定義順の実行履歴として記録する。

`ExecutionTraceStep` には試行番号を追加する。

`ExecutionTraceStep.StepName` は従来どおり、実行された Step 型名を基本とする。Entry の名前空間化は `WorkflowResult.EntryName` とログスコープの `EntryName` で表す。

```csharp
public sealed record ExecutionTraceStep(
    string StepName,
    ExecutionTraceStepStatus Status,
    TimeSpan Duration,
    string? ErrorCode,
    int Attempt);
```

`Attempt` は 1 から始まる実試行番号とする。

`ExecutionTraceStep` は `ProducedValues` を持つ。

```csharp
public IReadOnlyList<ExecutionTraceValue> ProducedValues { get; init; }
```

`ProducedValues` は、その Step の成功した値生成処理が明示的に trace 保存した値の一覧である。

`ExecutionTraceValue` は型名、任意の名前、source、保存状態、直列化文字列、直列化失敗理由を持つ。

source は `Produce` または `StoreAs` とする。

`TraceValueCapture.Serialized` の値は `System.Text.Json` で文字列へ直列化して保存する。

`TraceValueCapture.Redacted` の値は型名、任意の名前、source、保存状態だけを残し、値本文を保存しない。

直列化できない値は workflow を失敗させず、当該 trace 値を `NotSerializable` として値本文なしで残す。

retry された Step は、同じ Step 名の trace 記録を試行ごとに追加する。

途中で成功した場合、失敗試行の trace 記録を複数件追加し、最後に成功試行の trace 記録を 1 件追加する。

全試行が失敗した場合、失敗試行の trace 記録を試行数分追加し、最後の記録の `ErrorCode` は `STEP_EXECUTION_FAILED` とする。

非同期 Step で例外が発生した場合、非同期待機で観測した例外を `STEP_EXECUTION_FAILED` の失敗 trace に変換する。

timeout が発生した場合、対象 Step の trace は `ExecutionTraceStepStatus.Failed` とし、`ErrorCode` は `STEP_TIMEOUT` とする。

外部キャンセルで Step 実行が止まった場合、対象 Step の trace は `ExecutionTraceStepStatus.Failed` とし、`ErrorCode` は `STEP_CANCELED` とする。

timeout または外部キャンセルの後に実行しなかった後続 Step は trace に追加しない。

`TimedOut`、`Canceled`、`Skipped` などの trace 状態は追加しない。

Step 本体失敗、retry 途中失敗、timeout、外部キャンセルでは値生成処理を実行しないため、当該 trace の `ProducedValues` は空である。

値生成処理の失敗または重複登録失敗では当該 Step を失敗 trace とし、`ProducedValues` は空にする。

値生成処理が複数ある場合でも、失敗 trace には部分的に成功した値を保存しない。

---

## 19. 標準実装範囲

### 19.1 標準範囲

標準実装は以下を扱う。

- `.csx` での名前付き `CompositeStep` 定義
- 既定 Entry 名 `Main`
- CLI の `run` と検証コマンド
- 逐次実行
- `IStep<TOut>.Execute(StepInput input)`
- `IAsyncStep<TOut>.ExecuteAsync(StepInput input, CancellationToken cancellationToken)`
- 非同期 Step 登録 API
- 非同期ワークフロー実行 API
- `StepInput` の型付き、名前付き取得
- `StepContext` の共有値保持
- Config ファイルパスと `--set` の `EngineArguments` 格納
- ローカルファイル `#load`
- 明示許可された `#r`
- 明示許可された NuGet 参照
- `Dotnet.Script.Core` によるロードとコンパイル
- `Microsoft.Extensions.Logging` 統合
- `WorkflowResult` と基本エラーコード
- `WorkflowExecutionOptions.StepTimeout` による Step 単位の timeout
- `WorkflowExecutionOptions.Retry` による全 Step 一律の retry
- `ExecuteWorkflowAsync` の外部 `CancellationToken` と timeout 用の `CancellationToken` の合成
- timeout と外部キャンセルの失敗結果化
- retry 試行ごとのログと trace
- 標準 Config 読み込み
- CLI override による標準 Config 上書き
- Step 内 `Config` 型
- CompositeStep 境界 Config 型を宣言する `WithConfig<TConfig>()`
- Step 登録単位の `WithConfig<TConfig>(string sectionPath)` による Step Config 型と境界 Config プロパティ path メタ情報
- Step 登録単位の `WithConfig<TConfig>(string sectionPath, string defaultConfigPath)` による既定 Config YAML path メタ情報
- 規約 path または明示 path からの Step 既定 Config YAML 読み込み
- Step 既定 Config YAML、root Config 区画、CLI `--set` の順による上書き
- CLI `run` の境界 Config 型変換、CLI override 適用、Config 検証
- CLI `run` の `--set` プロパティ path override と Config 検証前適用
- 対象 Step 実行直前の `StepContext.Set<TStep.Config>()` 登録
- Step 登録単位 Config がない場合の `WithConfig<TConfig>()` による Entry 全体 Config 互換 API
- `engine validate` での Config path 存在確認
- 値を含む `ExecutionTrace`
- `TraceValueCapture.Serialized` と `TraceValueCapture.Redacted`
- NuGet ロックファイルによる直接参照と解決済み依存関係の再現性検証
- `#load "nuget: PackageId, Version"` による NuGet script パッケージ読み込み
- Entry の短い名前、名前空間名、完全修飾名
- `--entry Deploy.Build` のような完全修飾 Entry 指定

### 19.2 標準契約外の未実装・未採用範囲

以下は実装済みではない、または意図的に採用していない契約である。

- 独立した Flow 概念
- YAML ワークフロー定義
- Step 専用 Config 引数
- Step 間の自動依存解決
- 並列実行
- 分岐実行
- 統合実行
- 未信頼 `.csx` の安全な実行
- 任意の複数 `--config` 指定
- 標準 Config 読み込みによる永続的な名前付き Config 取得
- Config 型自動推論
- Step 型への Config 自動注入
- `--set` による配列全体またはリスト全体の置換
- `--set` による配列またはリストの自動拡張
- `engine validate` での Config 型変換、override 型検証、Config 値検証
- CLI の timeout オプション
- CLI の retry オプション
- Config による retry 指定
- Step 別 retry 方針
- retry 待機時間制御
- retry の例外型による絞り込み
- 実行中 Step の強制停止
- workflow 全体 timeout
- timeout またはキャンセル専用の trace 状態

---

## 20. 明確に禁止すること

本設計では以下を禁止する。

```text
・Flow という独立概念を作ること
・Flow(...) という専用記法を作ること
・Step に Config 専用引数を追加すること
・Step に StepContext 専用引数を追加すること
・Step 間の入出力を自動推論すること
・上流出力を自動的に下流入力へ変換すること
・Config を Step 専用引数または Step 型プロパティへ自動注入すること
・並列実行すること
・分岐、統合を逐次実行モデルに含めること
```

---

## 21. 補足設計詳細

以下は実装済みの標準契約を補足する詳細である。

### 21.1 非同期 API

非同期 API は以下を採用する。

- `IAsyncStep<TOut>` を追加する
- 既存 `IStep<TOut>` は維持する
- `IStep<TOut>` を `Task<TOut>` 系へ統一しない
- `IStep<Task<T>>` は通常の同期 Step の戻り値型として扱う
- 非同期 Step は `RunAsync<TStep, TOut>()` などの明示 API で登録する
- 非同期ワークフロー実行 API として `ExecuteWorkflowAsync` を追加する
- 非同期 Step の `ExecuteAsync` には `CancellationToken` を渡す

timeout と外部キャンセルは以下を採用する。

- `WorkflowExecutionOptions` に `TimeSpan? StepTimeout` を追加する
- `StepTimeout` の既定値は `null` とし、timeout を設定しない
- `ExecuteWorkflowAsync` の外部 `CancellationToken` と timeout 用の `CancellationToken` を Step 実行ごとに合成する
- 非同期 Step には合成した `CancellationToken` を渡す
- timeout は `STEP_TIMEOUT` の失敗結果に変換する
- 外部キャンセルは `STEP_CANCELED` の失敗結果に変換し、timeout と区別する
- timeout または外部キャンセル時は対象 Step を失敗 trace とし、エラーコードを記録する
- timeout または外部キャンセル時は対象 Step の `Produce` と後続 Step を実行しない
- 同期 Step 実行中は強制中断しない
- 同期 Step 完了後にキャンセルが要求済みであれば、後続 Step を開始しない

timeout と外部キャンセルの標準契約には以下を含めない。

- CLI の timeout オプション
- 実行中 Step の強制停止
- workflow 全体 timeout
- timeout またはキャンセル専用の trace 状態

retry は以下を採用する。

- `WorkflowExecutionOptions` に `RetryOptions? Retry` を追加する
- `RetryOptions` に `int MaxAttempts` を追加する
- `Retry = null` または `MaxAttempts <= 1` は retry なしとする
- `MaxAttempts` は初回を含む最大試行回数とする
- retry は全 Step 一律の指定に限定する
- retry 対象は Step 本体の通常例外だけとする
- timeout と外部キャンセルは retry 対象外とする
- timeout と外部キャンセルの両方が観測される場合は外部キャンセルを優先する
- `Produce`、`StoreAs`、`Discard` の失敗は retry 対象外とする
- 成功した最後の試行だけ `Produce`、`StoreAs`、`Discard` を実行する
- 全試行失敗時は `STEP_EXECUTION_FAILED` の失敗結果にする
- `ExecutionTraceStep` に `Attempt` を追加する
- ログのスコープの `Attempt` は実試行番号にする
- retry 予定と最終失敗を構造化ログで記録する

retry の標準契約には以下を含めない。

- CLI の retry オプション
- Config による retry 指定
- Step 別 retry 方針
- retry 待機時間制御
- retry の例外型による絞り込み

### 21.2 Config 読み込み責務

標準 Config 読み込みはエンジン実行前処理として提供する。

Entry 側は `WithConfig<TBoundaryConfig>()` で CompositeStep 境界 Config 型を宣言し、Step 登録単位の `WithConfig<TConfig>(string sectionPath)` で Step Config 型と境界 Config 型上のプロパティ path を明示する。既定 Config YAML が規約 path と異なる場合は、`WithConfig<TConfig>(string sectionPath, string defaultConfigPath)` で既定 Config YAML path も明示する。

CLI `run` は `.csx` ロード後、Entry の境界 Config 型、Step Config メタ情報、`--config` の path を使って root YAML を読み込む。Step 登録単位 Config では、各 Step の既定 Config YAML を読み、root YAML の宣言済み Step Config 区画を部分上書きとして重ねてから YAML 全体を境界 Config 型へ変換する。

宣言された境界 Config と Step Config はすべて最初の Step 実行前に読み込み、型変換、override 適用、`DataAnnotations` と `IValidatableObject` による検証まで完了する。

対象 Step の実行直前に、境界 Config 型上の `sectionPath` プロパティ値を `StepContext.Set<TConfig>(config)` で登録する。同じ Config 型を別プロパティ path で複数回使う場合、後続 Step の Config が同じ型の値を上書きしうる。

Step 登録単位 Config があるのに境界 Config 型宣言がない場合は `CONFIG_LOAD_FAILED` とする。

Step 登録単位 Config がない場合、既存の `WithConfig<TConfig>()` は Entry 全体 Config 互換 API として残す。

`--set` は `EngineArguments.Settings` に保持し、標準 Config へも適用する。

任意の複数 `--config` 指定、標準 Config 読み込みによる永続的な名前付き Config 取得、Config 型自動推論、Step 型への Config 自動注入、Step 専用引数は標準 Config 契約には含めない。

Step 配下に置く既定 Config YAML は、Step Config 型の入力例または再利用素材として扱う。標準 Config 読み込みは未宣言 Step の YAML を探索しない。宣言済み Step Config に対しては、明示 `defaultConfigPath` があればそれを読み、なければ規約 path を読む。

### 21.3 CLI override の仕様

`--set` は CompositeStep 境界 Config 型に対するプロパティ path override として扱う。

適用順は以下とする。

1. `--config` の root YAML を読み込む
2. 宣言済み Step Config ごとの既定 Config YAML を読み込む
3. root YAML の宣言済み Step Config 区画を既定 Config YAML に部分上書きする
4. 結合済み YAML 全体を境界 Config 型へ変換する
5. `--set` を境界 Config 型のプロパティ path override として適用する
6. `DataAnnotations` と `IValidatableObject` を検証する
7. 対象 Step の実行直前に、宣言済み `sectionPath` のプロパティ値を `StepContext` に登録する

プロパティ path は `.` 区切りの path 要素として完全一致させる。`--set Convert.ToUpper=false` は、境界 Config 型の `Convert.ToUpper` に対する override として扱う。対象 Step 実行直前には、`Convert` プロパティ値を `StepContext.Set<ConvertStep.Config>()` に登録する。`ConvertExtra.ToUpper` は `Convert` プロパティ path に一致しない。

同一 Entry 内の宣言済みプロパティ path は、互いに先頭から同じ path 要素列になってはならない。たとえば `Convert` と `Convert.Options` の併用は標準契約に含めず、違反時は最初の Step 実行前に `CONFIG_LOAD_FAILED` とする。宣言済みプロパティ path に一致しない `--set` も `CONFIG_LOAD_FAILED` とする。

プロパティ path は C# の公開プロパティ名を `.` でたどる。プロパティ名の照合は実行環境の言語設定に依存しない `StringComparison.Ordinal` 相当の完全一致とし、存在しないプロパティは `CONFIG_LOAD_FAILED` とする。

入れ子プロパティの途中が `null` の場合、引数なしで生成できるクラスは自動生成して続行する。生成できない場合は `CONFIG_LOAD_FAILED` とする。

配列またはリストは、`Items[0].Name=value` のような既存要素への添字 override だけを扱う。自動拡張、配列全体またはリスト全体の置換は標準 Config override には含めない。

型変換は override 対象プロパティの型に対して行う。標準契約では `string`、`bool`、`int`、`long`、`double`、`decimal`、`enum`、nullable な基本型を扱う。

同一 key の複数 override は後勝ちとする。

無効書式、存在しないプロパティ、型変換失敗、配列またはリストの添字不正は `CONFIG_LOAD_FAILED` とする。ただし、CLI 解析層で `--set` の値がない、`key=value` になっていない、または key が空の場合は CLI 解析エラーとして終了コード 2 で失敗する。

`engine validate` は Config path の存在確認までを維持し、override の型検証は `engine run` で行う。

任意の複数 `--config` 指定時の統合規則は標準 Config 契約には含めない。

Entry 全体 Config 互換 API では、従来どおり `--set` key 全体を Entry 全体 Config 型へのプロパティ path override として扱う。

### 21.4 Produce 後の値の寿命

`StepInput` に追加された値は、同一 `CompositeStep` 実行中の後続すべての Step へ保持する。

`Produce` と `StoreAs` は、Step 成功後に値を `StepInput` へ追記する。

登録前の上流 Step 以前からは、その値を読めない。

`Discard` は現在 Step の戻り値登録を抑止するだけで、既存値を削除しない。

同じ型キー、または同じ型と名前のキーを再登録しようとした場合は実行時エラーとする。暗黙上書きは行わない。

型キーと名前付きキーは、同じ CLR 型でも別キーとして扱う。

長寿命で上書き可能な共有値は `StepContext` に置く。Step 間で明示的に受け渡す値は `StepInput` に置く。

### 21.5 トレース値の保存

`ExecutionTrace` に `StepInput`、Config、Step 出力全体の値は既定では保存しない。

既定では値そのものを保存せず、Step 名、状態、所要時間、エラーコードを優先する。

`ExecutionTrace` の trace 値の基礎単位は「Step 成功後に `Produce` または `StoreAs` の値登録処理が成功して `StepInput` に登録された値」とする。

失敗した試行、timeout、外部キャンセルにより `Produce` または `StoreAs` の値登録処理が実行されなかった値は、登録済み値として扱わない。

既存の `Produce`、名前付き `Produce`、`StoreAs` は値を trace に保存しない。値を保存する場合は値生成処理ごとに `TraceValueCapture.Serialized` または `TraceValueCapture.Redacted` を明示する。

`Discard` は新しい値を `StepInput` に登録しないため、trace 値も生成しない。

trace 値は `ExecutionTraceStep.ProducedValues` に入る `ExecutionTraceValue` の一覧として表す。

`ExecutionTraceValue` は次を持つ。

- 型名
- 任意の名前
- source
- 保存状態
- 直列化文字列
- 直列化失敗理由

source は `Produce` または `StoreAs` とする。

`TraceValueCapture.Serialized` は、値本文を `System.Text.Json` で文字列へ直列化して保存する。

`TraceValueCapture.Redacted` は、型名、任意の名前、source、保存状態だけを保存し、直列化文字列は保存しない。

直列化できない値は workflow を失敗させず、当該 trace 値を `NotSerializable` として値本文なしで残す。

Step 本体失敗、retry 途中失敗、timeout、外部キャンセルでは値生成処理を実行しないため、当該 trace の値一覧は空である。

値生成処理の失敗または重複登録失敗では当該 Step を失敗 trace とし、値一覧は空にする。部分的に成功した値も失敗 trace へ保存しない。

秘匿値の自動検出、属性による秘匿、プロパティ単位秘匿、trace 永続化形式、CLI 出力形式、大きさ上限、厳格失敗動作は標準 trace 契約には含めない。

`TRACE_SERIALIZATION_FAILED` は trace 外部保存や厳格動作用として残し、既定 workflow 失敗には使わない。

### 21.6 csx 依存の再現性

NuGet 参照の再現性は、利用者が必要に応じて置く `devo6.nuget.lock.yaml` で扱う。

`#r "nuget: package, version"` は NuGet ロックファイルの直接参照として扱う。

`#load "nuget: ..."` は NuGet script パッケージ読み込みとして扱う。

NuGet 復元と依存関係解決は `Dotnet.Script.Core` と `Dotnet.Script.DependencyModel` に委ねる。

エンジン側は `devo6.nuget.lock.yaml` がある場合だけ、許可済み直接参照、解決済み依存関係、`targetFramework`、実行時識別子、`Dotnet.Script.Core` version の比較を担当する。パッケージ参照元は `verifyPackageSources: true` の場合だけ比較する。

`dotnet-script` 互換の `#load "nuget: PackageId, Version"` を採用し、パッケージ内 path をディレクティブに追加する独自仕様は採用しない。

`#load "nuget: ..."` から得た直接参照も、`devo6.nuget.lock.yaml` の `directReferences`、`resolvedDependencies`、`metadata` 比較の対象に含める。

NuGet キャッシュ探索、`contentFiles` 選択、`project.assets.json` 解析、実行時 assembly 解決、最終コンパイル用の NuGet script source 解決機構は `Dotnet.Script.Core` と `Dotnet.Script.DependencyModel` に委ねる。

通常の検査では固定データを返す依存関係 provider を使い、外部 NuGet source への通信を必須にしない。

必要に応じてローカルの NuGet 参照元を使う追加検証を用意するが、通常の `dotnet test` の前提にはしない。

### 21.7 Step 名の名前空間化

Entry として公開される `CompositeStep` は短い名前と任意の名前空間名を持つ。

採用する API は `CompositeStep.Define("Build", namespaceName: "Deploy")` とする。`CompositeStep.Define("Build")` は従来互換の名前空間なし Entry とする。

名前空間付き Entry の完全修飾名は `Deploy.Build` とする。名前空間なし Entry の完全修飾名は短い名前と同じ `Build` とする。

CLI は既存の `--entry` オプションを維持し、`--entry Deploy.Build` で名前空間付き Entry を指定する。新しいオプションは追加しない。

ローダーは script 変数名だけで Entry を解決しない。ロード済み `.csx` と `#load` 先にある `CompositeStep` の `Name`、`NamespaceName`、`QualifiedName` を読み取り、公開名として解決する。

短い `--entry Build` は、名前空間なしの `Build` を優先する。名前空間なしの `Build` がなければ、短い名前が `Build` である Entry が 1 件だけの場合に互換解決する。複数候補がある場合は `ENTRY_STEP_NOT_FOUND` で失敗し、完全修飾名指定を求める。

重複検証は完全修飾名単位とする。`Deploy.Build` と `Test.Build` は共存でき、`Deploy.Build` 同士は `DUPLICATE_STEP_NAME` で失敗する。

`WorkflowResult.EntryName`、CLI 成功出力、ログスコープの `EntryName` は完全修飾名を記録する。`ExecutionTraceStep.StepName` とログスコープの `StepName` は従来どおり Step 型名を基本とする。

`WithConfig<TConfig>()`、`WithConfig<TConfig>(string sectionPath)`、`Run<TStep, TStepOut>()`、`RunAsync<TStep, TStepOut>()` 後も、短い名前、名前空間名、完全修飾名は維持する。

追加または変更する API、ヘルパー、テストメソッドの関数名は英語にする。XMLコメントは日本語とし、パブリック以外の関数、コンストラクタ、プロパティ、レコードのプロパティ、入れ子型もコメント対象とする。

少なくとも以下を検査する。

- `CompositeStep.Define("Build", namespaceName: "Deploy")` が短い名前、名前空間名、完全修飾名 `Deploy.Build` を持つ
- `CompositeStep.Define("Build")` が完全修飾名 `Build` を持ち、既存互換を維持する
- `CsxEntryLoader.Execute(scriptPath, "Deploy.Build")` が名前空間付き Entry を実行し、`WorkflowResult.EntryName == "Deploy.Build"` になる
- `CsxEntryLoader.Validate(scriptPath, "Deploy.Build")` が名前空間付き Entry を成功検証する
- `Deploy.Build` と `Test.Build` が同じ読み込み単位に共存できる
- `Deploy.Build` が 2 つある場合は `DUPLICATE_STEP_NAME` になる
- 名前空間なしの `Build` と `Deploy.Build` が共存でき、短い `--entry Build` は名前空間なしの `Build` に解決する
- 名前空間なしの `Build` がなく、短い名前 `Build` が 1 件だけなら短い `--entry Build` で互換解決できる
- 名前空間なしの `Build` がなく、`Deploy.Build` と `Test.Build` がある状態で短い `--entry Build` を指定すると `ENTRY_STEP_NOT_FOUND` になる
- CLI の `run` と `validate` が `--entry Deploy.Build` を透過し、成功時に `Succeeded: Deploy.Build` を出す
- `#load` 先で定義された `Deploy.Build` を `--entry Deploy.Build` で解決できる
- `WithConfig<TConfig>()`、`WithConfig<TConfig>(string sectionPath)`、`Run<TStep, TStepOut>()`、`RunAsync<TStep, TStepOut>()` 後も名前空間メタ情報が維持される

---

## 22. 最終整理

本設計の中核は以下である。

```text
Step が唯一の実行単位。
Flow は独立概念ではなく CompositeStep として扱う。
Step は StepInput のみを受け取る。
StepInput は可変長の型付き・名前付き入力集合である。
StepContext は StepInput に自動で含まれる。
Config は StepContext に置く。
上流 Step の結果を下流に渡す場合は Produce で明示する。
Entry の公開名は CompositeStep の完全修飾名として扱う。
エンジンは Step 間の接続を自動推論しない。
```

この設計により、以下を両立する。

- Step の疎結合
- Config と実行時データの統一的な扱い
- 上流出力の一部だけを下流へ渡す明示性
- Flow/Step 概念の一本化
- `.csx` での実用的な書き心地
- `Dotnet.Script.Core` による NuGet / 外部 `.csx` 解決
