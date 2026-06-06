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

初期版では、Step は定義順に逐次実行する。

以下は初期版では対象外とする。

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

`Produce` により既存キーへ再登録しようとした場合、エンジンは実行時エラーとして扱う。

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
  Step 間で明示的に受け渡す可変長入力集合

StepContext:
  実行全体で共有される長寿命の値
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
    public ConvertResult Execute(StepInput input)
    {
        AppConfig config = input.Context.Get<AppConfig>();
        ConvertInput convertInput = input.Get<ConvertInput>();

        return new ConvertResult
        {
            ConvertedText = config.Convert.ToUpper
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

例:

```yaml
load:
  path: ./input.txt

convert:
  toUpper: true

save:
  path: ./output.txt
```

### 6.3 Config 読み込み

標準 Config 読み込みは、エンジンの実行前処理として行う。

Entry 側は、標準 Config 型を `CompositeStep.Define("Main").WithConfig<AppConfig>()` で明示する。

`WithConfig<TConfig>()` は Entry のメタ情報として Config 型を保持する。Step 専用引数は増やさない。

CLI `run` は Entry `.csx` をロードした後、Entry の Config 型メタ情報と `--config` の path を使って YAML を型付き Config に変換する。

変換に成功した Config は、最初の Step 実行前に `StepContext.Set<TConfig>(config)` で登録する。

T23 では単一 Config 型のみを扱う。複数 Config、名前付き Config、Config 型自動推論は対象外とする。

Config 読み込み用 Step をユーザーが明示的に定義する方式も、標準外の拡張として許可する。

Config 読み込み Step を使用する場合は、ワークフローの先頭またはまとまりの先頭で一度だけ行うことを想定する。

### 6.4 Config を StepContext に格納する例

```csharp
public sealed class ConvertStep : IStep<ConvertResult>
{
    public ConvertResult Execute(StepInput input)
    {
        AppConfig config = input.Context.Get<AppConfig>();

        return new ConvertResult
        {
            Mode = config.Convert.Mode
        };
    }
}
```

Entry 側の宣言例:

```csharp
var Main = CompositeStep.Define("Main")
    .WithConfig<AppConfig>()
    .Run<ConvertStep, ConvertResult>();
```

### 6.5 CLI による Config 指定

エンジン起動時に Config ファイルを指定できる。

```bash
engine run main.csx --config appsettings.yaml
```

`--config` は Config ファイルパスとして `EngineArguments` に保持する。

Entry が `WithConfig<TConfig>()` を使っている場合、CLI `run` は `--config` の YAML を `TConfig` に変換し、Step 実行前に `StepContext` に登録する。

`--config` 未指定で、Entry が `WithConfig<TConfig>()` を使っていない場合は既存どおり成功する。

`--config` 未指定で、Entry が `WithConfig<TConfig>()` を使っている場合は、Step 実行前に `CONFIG_NOT_FOUND` で失敗する。利用者へ早く原因を返すためである。

### 6.6 CLI による Config 上書き

CLI 引数で Config の一部を上書きできることを要件とする。

例:

```bash
engine run main.csx --config appsettings.yaml --set Convert.ToUpper=false
```

`--set` は、標準 Config に対するプロパティ path override として扱う。

書式は `--set key=value` とする。CLI 解析層は最初の `=` より前を key、最初の `=` 以降を値として保持する。値の中の `=` は許可する。

CLI 解析層で `--set` に値がない場合、`key=value` になっていない場合、または key が空の場合は、Config 型を見ない CLI 解析エラーとして終了コード 2 で失敗する。

key は C# の公開プロパティ名を `.` でたどる。プロパティ名は大小文字を区別する完全一致とし、存在しないプロパティは `CONFIG_LOAD_FAILED` とする。

入れ子プロパティの途中が `null` の場合、引数なしで生成できるクラスは自動生成して続行する。生成できない場合は `CONFIG_LOAD_FAILED` とする。

配列またはリストは、`Items[0].Name=value` のように既存要素への添字 override だけを扱う。自動拡張、配列全体またはリスト全体の置換は初期範囲外とする。添字が範囲外、負数、または数値でない場合は `CONFIG_LOAD_FAILED` とする。

型変換は override 対象プロパティの型に対して行う。初期範囲では `string`、`bool`、`int`、`long`、`double`、`decimal`、`enum`、nullable な基本型を扱う。型変換に失敗した場合は `CONFIG_LOAD_FAILED` とする。

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

### 7.3 Produce

`Produce` は、Step の戻り値から `StepInput` に追加する値を生成する。

```csharp
.Produce<TValue>(Func<TOut, TValue> selector)
```

名前付き登録も許可する。

```csharp
.Produce<TValue>(string name, Func<TOut, TValue> selector)
```

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

### 7.5 Discard

`Discard` は Step の戻り値を `StepInput` に登録しない。

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
    public LoadResult Execute(StepInput input)
    {
        AppConfig config = input.Context.Get<AppConfig>();

        string text = File.ReadAllText(config.Load.Path);

        return new LoadResult
        {
            Text = text,
            FilePath = config.Load.Path
        };
    }
}
```

### 8.2 Step は必要な入力を StepInput から明示的に取得する

```csharp
public sealed class ConvertStep : IStep<ConvertResult>
{
    public ConvertResult Execute(StepInput input)
    {
        ConvertInput convertInput = input.Get<ConvertInput>();
        AppConfig config = input.Context.Get<AppConfig>();

        return new ConvertResult
        {
            ConvertedText = config.Convert.ToUpper
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
7. Entry の Config 型メタ情報を確認する
8. 必要な場合は `--config` の YAML を型付き Config に変換する
9. `--set` のプロパティ path override を型付き Config に適用する
10. `DataAnnotations` と `IValidatableObject` で Config を検証する
11. 検証済み Config を `StepContext` に登録する
12. 指定された Step を実行する

---

## 11. エラー処理

### 11.1 基本方針

Step 実行中に例外が発生した場合、エンジンは失敗として扱う。

初期版では、デフォルト動作は停止とする。

### 11.2 FailurePolicy

将来的に Step 単位で失敗時の挙動を定義できるようにする。

```csharp
public enum FailurePolicy
{
    Stop,
    Continue
}
```

初期版で `Continue` を実装するかは未確定。

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
- `#load` 解決失敗

Entry が標準 Config 型を要求している場合の `--config` 未指定と、存在しない Config ファイルは `CONFIG_NOT_FOUND` とする。

読み込み不能、YAML 構文エラー、型変換失敗、`--set` の Config 適用失敗、`DataAnnotations` または `IValidatableObject` の失敗は `CONFIG_LOAD_FAILED` とする。

### 11.4 retry と timeout

T22 では Step 本体の通常例外に対する retry を実装する。

retry は `WorkflowExecutionOptions.Retry` で指定する。

`RetryOptions.MaxAttempts` は初回を含む最大試行回数とする。

`Retry = null` または `MaxAttempts <= 1` の場合、retry は行わない。

例えば `MaxAttempts = 3` は、対象 Step を最大 3 回実行することを意味する。

T22 の retry は全 Step 一律の指定に限定する。

Step 別 retry、待機時間制御、例外型による絞り込み、CLI 指定、Config 指定は T22 の対象外とする。

retry 対象は Step 本体の通常例外に限定する。

最終的に `STEP_EXECUTION_FAILED` になる候補だけを retry 対象とする。

入力取得失敗、Config 検証失敗、`.csx` ロード失敗、`.csx` コンパイル失敗、参照解決失敗は retry 対象外とする。

`Produce`、`StoreAs`、`Discard` の失敗は retry 対象外とする。

失敗した試行の `Produce` は実行しない。

途中の試行が成功した場合、成功した最後の試行の戻り値だけに `Produce` を実行し、後続 Step は 1 回だけ開始する。

全試行が失敗した場合、後続 Step は開始しない。

timeout は `WorkflowExecutionOptions.StepTimeout` で指定する。

`StepTimeout` の既定値は `null` とし、timeout を設定しない現行動作を維持する。

timeout は強制停止ではなく、`CancellationToken` による協調キャンセルとして扱う。

timeout と外部キャンセルは retry 対象外とする。

timeout は `STEP_TIMEOUT`、外部キャンセルは `STEP_CANCELED` として扱い、どちらも retry を止める。

timeout と外部キャンセルの両方が観測される場合は、T21 と同じく外部キャンセルを優先して `STEP_CANCELED` とする。

各試行の timeout は、試行開始時に作成した Step 単位の timeout 用 `CancellationTokenSource` で判定する。

実行中 Step の強制停止、workflow 全体 timeout は T22 の対象外とする。

---

## 12. 非同期対応

### 12.1 方針

T20 では、既存の同期 Step API を維持したまま、非同期 Step 用の明示 API を追加する。

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

この案は既存の `IStep<TOut>` 実装を破壊するため、T20 では採用しない。

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

### 13.4 Config を StepContext に置く例

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadConfigStep, Unit>()
        .Discard()
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
public sealed class CompositeStep<TOut> : IStep<TOut>, IAsyncStep<TOut>
{
    public CompositeStep<TOut> WithConfig<TConfig>();

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
}
```

`WithConfig<TConfig>()` は Entry に標準 Config 型メタ情報を設定する。T23 では 1 つの Entry に設定できる Config 型は 1 つだけとする。

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

T22 では `Retry` を CLI オプションまたは Config から直接指定しない。

標準 Config 型は `WorkflowExecutionOptions` ではなく、Entry の `CompositeStep` メタ情報から取得する。

CLI `run` は `.csx` ロード後に Entry を解決し、Entry の `ConfigType`、`EngineArguments.ConfigPath`、`EngineArguments.Settings` を使って標準 Config を読み込む。

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
```

`.csx` 上では以下のように名前付き Step を定義する。

```csharp
var Main = CompositeStep.Define("Main")
    .WithConfig<AppConfig>()
    .Run<LoadStep, LoadResult>()
        .Produce<ConvertInput>(x => new ConvertInput { Text = x.Text })
    .Run<ConvertStep, ConvertResult>()
        .StoreAs();
```

エンジンはロード済み `.csx` から、指定名に一致する `CompositeStep` を取得して実行する。

非同期 Step を含む Entry の通常実行では、エンジンは `ExecuteWorkflowAsync` を使って非同期 Step の完了を待つ。

同期 Step だけの Entry では、既存の `ExecuteWorkflow` を維持する。

指定された Entry が存在しない場合は検証エラーとする。

### 15.2 Step 名の一意性

ロード済み `.csx` 全体で、実行対象として公開される Step 名は一意でなければならない。

同名の `CompositeStep` が複数見つかった場合、エンジンは実行前の検証で失敗する。

外部 `.csx` を `#load` した場合も、読み込み後の全体で同じ規則を適用する。

### 15.3 ファイル構成

標準的な構成は以下とする。

```text
workflow-root/
├── main.csx
├── build.csx
├── test.csx
├── steps/
│   ├── load-step.csx
│   └── save-step.csx
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

### 15.4 トップレベルステートメント

Entry `.csx` と外部 `.csx` のトップレベルステートメントは、Step 定義、型定義、`using`、`#load`、`#r` に限定することを推奨する。

検証時にも `.csx` のロードや評価が必要になるため、トップレベルでファイル削除、外部通信、環境変更などの副作用を起こしてはならない。

初期版では、副作用のあるトップレベルステートメントを完全には検出しない。

利用者は、同期処理を `IStep<TOut>.Execute` 内に置く。

非同期処理は `IAsyncStep<TOut>.ExecuteAsync` 内に置く。

---

## 16. csx 解決と参照方針

### 16.1 Dotnet.Script.Core の利用範囲

`Dotnet.Script.Core` は、`.csx` のロード、`#load` 解決、`#r` 解決、NuGet 復元、Roslyn コンパイルに使う。

Step 実行、`StepInput` 構築、Config 保持、検証、ログ、実行結果の生成はエンジン側で制御する。

通常実行経路では、Step ごとに外部プロセスとして `dotnet script` CLI を起動しない。

### 16.2 `#load` 解決

初期版では、ローカルファイルの `#load` に対応する。

```csharp
#load "./build.csx"
#load "./steps/load-step.csx"
```

解決規則は以下とする。

| 項目 | 規則 |
| --- | --- |
| 相対パス基準 | `#load` を書いた `.csx` のディレクトリ |
| パス正規化 | `..` とシンボリックリンクを解決した正規パス |
| root 制限 | 初期版では `workflow-root` 配下のみ許可 |
| 循環読み込み | 検出して検証エラー |
| 重複読み込み | 同一正規パスは 1 回だけ読み込む |

`#load "nuget: ..."` は初期版では対象外とする。

### 16.3 `#r` と NuGet 参照

初期版では、以下を明示許可された場合に限り対応する。

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
| 浮動バージョン | 初期版では禁止 |
| パッケージ参照元 | 既定の参照元または明示許可された参照元のみ許可 |

NuGet 復元は `Dotnet.Script.Core` の仕組みを利用してよい。

ただし、NuGet 参照は実行できるコードを増やすため、信頼済みワークフローでのみ使う。

### 16.4 AssemblyLoadContext

初期版では、ワークフロー単位で `AssemblyLoadContext` を分離してよい。

エンジンが公開する API の assembly はホスト側と共有する。

Script 側で公開 API assembly の別コピーが読み込まれた場合、型名が同じでも CLR 上は別型になる。

その場合、`IStep<TOut>`、`StepInput`、`StepContext` の受け渡しが壊れるため、検証エラーとする。

### 16.5 キャッシュ

コンパイルキャッシュを使う場合、最低限以下をキャッシュキーに含める。

- Entry `.csx` の正規パスと内容ハッシュ
- `#load` された全 `.csx` の正規パスと内容ハッシュ
- `#r` の参照一覧
- NuGet パッケージ ID、バージョン、参照元
- エンジンのバージョン

いずれかが変わった場合、該当キャッシュは無効化する。

### 16.6 信頼境界

初期版では、`.csx` は信頼済みワークフローのみを実行対象とする。

未信頼ユーザーがアップロードした `.csx` を、このエンジンで直接実行してはならない。

初期版では以下を提供しない。

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
engine validate main.csx --config appsettings.yaml
```

### 17.2 検証対象

検証対象は以下とする。

- Entry `.csx` の存在
- 指定 Entry 名の存在
- 公開 Step 名の重複
- `#load` の参照解決
- `#load` の循環
- `#r` の許可判定
- NuGet 参照の許可判定
- `.csx` のコンパイル
- `IStep<TOut>` または `IAsyncStep<TOut>` 実装の確認
- `StepInput` と `StepContext` の API 互換
- Config ファイル指定時の存在確認

実行時の `StepInput` 内容に依存する型検証は、実行時に行う。

T23 の `validate` は Config path の存在確認までを必須とする。Config 型変換と Config 値検証は `validate` の必須対象外であり、後続で扱う。

### 17.3 StepInput 検証

`StepInput.Get<T>()` と `StepInput.Get<T>(name)` は、値が存在しない場合や型が一致しない場合に失敗する。

失敗時は Step を実行せず、エンジンの実行結果を失敗にする。

`TryGet` は失敗を戻り値で返し、エンジンの失敗にはしない。

### 17.4 Config 検証

CLI `run` では、Entry が `WithConfig<TConfig>()` を使う場合に標準 Config 読み込みを行う。

`--config` と `--set` は `EngineArguments` として `StepContext` に格納する。

`--config` の YAML は `TConfig` に型変換する。その後、`--set` を標準 Config に適用し、`DataAnnotations` と `IValidatableObject` を検証する。

検証に成功した Config は、Step 専用引数ではなく `StepContext` に登録する。

検証に失敗した Config は `StepContext` に登録してはならない。

空 Config は Config 型を生成でき、検証に通れば成功とする。検証に失敗した場合は `CONFIG_LOAD_FAILED` とする。

ユーザーが定義した Config 読み込み Step を使う場合は、その Step 内で型変換と検証を行う。

### 17.5 Step 出力検証

Step の戻り値が `null` であり、`TOut` が null を許さない型である場合、エンジンは Step 失敗として扱う。

Step 出力に `DataAnnotations` または `IValidatableObject` が使われている場合、エンジンは検証対象にできる。

初期版では、Step 出力検証を標準で有効にするかは実装時に選択できる。

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

CLI 実行では、成功時は終了コード 0、失敗時は 0 以外を返す。

非同期ワークフロー実行 API として `ExecuteWorkflowAsync` を追加する。

非同期 Step を含むワークフローでは、各非同期 Step の完了を待ってから後続 Step へ進む。

非同期 Step の完了後に `Produce`、`StoreAs`、`Discard` を実行する。

retry が有効な場合、Step 本体の通常例外は `RetryOptions.MaxAttempts` まで再試行する。

途中の試行が成功した場合、エンジンは成功した試行の戻り値だけに `Produce`、`StoreAs`、`Discard` を実行する。

失敗した試行の `Produce`、`StoreAs`、`Discard` は実行しない。

全試行が失敗した場合、エンジンは `WorkflowResult.Succeeded = false` の失敗結果を返す。

全試行失敗時の `ErrorCode` は `STEP_EXECUTION_FAILED` とする。

全試行失敗時の `ErrorMessage` は、最後に観測した例外 message を基本とする。

全試行失敗後、エンジンは後続 Step を開始しない。

Step timeout または外部キャンセルが発生した場合、エンジンは `WorkflowResult.Succeeded = false` の失敗結果を返す。

timeout の場合、`ErrorCode` は `STEP_TIMEOUT` とする。

外部キャンセルの場合、`ErrorCode` は `STEP_CANCELED` とする。

timeout または外部キャンセルで Step が失敗した場合、その Step の `Produce`、`StoreAs`、`Discard` は実行しない。

timeout または外部キャンセルを検出した後、エンジンは後続 Step を開始しない。

timeout と外部キャンセルは retry 対象外とし、観測した時点で workflow を失敗終了する。

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

### 18.3 ログ

ログはエンジンで独自実装せず、`Microsoft.Extensions.Logging` を利用する。

エンジンは `ILoggerFactory` を外部から受け取り、具体的な logger provider には直接依存しない。

ユーザー Step は `StepContext.Logger` から記録出力を取得する。

ログには、Entry 名、Step 名、実行状態、失敗時のエラーコードを含める。

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

初期版では、`StepInput`、Config、Step 出力の値そのものは既定では保存しない。

値を保存する場合は、明示設定と秘匿化の規則を必要とする。

同期 Step と非同期 Step が混在する場合も、trace は定義順の実行履歴として記録する。

`ExecutionTraceStep` には試行番号を追加する。

```csharp
public sealed record ExecutionTraceStep(
    string StepName,
    ExecutionTraceStepStatus Status,
    TimeSpan Duration,
    string? ErrorCode,
    int Attempt);
```

`Attempt` は 1 から始まる実試行番号とする。

retry された Step は、同じ Step 名の trace 記録を試行ごとに追加する。

途中で成功した場合、失敗試行の trace 記録を複数件追加し、最後に成功試行の trace 記録を 1 件追加する。

全試行が失敗した場合、失敗試行の trace 記録を試行数分追加し、最後の記録の `ErrorCode` は `STEP_EXECUTION_FAILED` とする。

非同期 Step で例外が発生した場合、非同期待機で観測した例外を `STEP_EXECUTION_FAILED` の失敗 trace に変換する。

timeout が発生した場合、対象 Step の trace は `ExecutionTraceStepStatus.Failed` とし、`ErrorCode` は `STEP_TIMEOUT` とする。

外部キャンセルで Step 実行が止まった場合、対象 Step の trace は `ExecutionTraceStepStatus.Failed` とし、`ErrorCode` は `STEP_CANCELED` とする。

timeout または外部キャンセルの後に実行しなかった後続 Step は trace に追加しない。

T21 では `TimedOut`、`Canceled`、`Skipped` などの trace 状態は追加しない。

---

## 19. 初期実装範囲

### 19.1 初期版で扱う範囲

初期版では以下を扱う。

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
- T23 の標準 Config 読み込み
- T24 の CLI override による標準 Config 上書き
- Entry の `WithConfig<TConfig>()` による単一 Config 型メタ情報
- CLI `run` の `--config` YAML 型変換と `StepContext` 登録
- CLI `run` の `--set` プロパティ path override と Config 検証前適用

### 19.2 初期版で扱わない範囲

初期版、T23、および T24 では以下を扱わない。

- 独立した Flow 概念
- YAML ワークフロー定義
- Step 専用 Config 引数
- Step 間の自動依存解決
- 並列実行
- 分岐実行
- 統合実行
- `#load "nuget: ..."`
- 未信頼 `.csx` の安全な実行
- 複数 Config
- 名前付き Config
- Config 型自動推論
- `--set` による配列全体またはリスト全体の置換
- `--set` による配列またはリストの自動拡張
- `engine validate` での override 型検証
- 値を含む `ExecutionTrace`
- CLI の timeout オプション
- CLI の retry オプション
- Config による retry 指定
- Step 別 retry 方針
- retry 待機時間制御
- retry の例外型による絞り込み
- 実行中 Step の強制停止
- workflow 全体 timeout
- timeout またはキャンセル専用の trace 状態

### 19.3 次フェーズ候補

次フェーズ候補は以下とする。

- CLI の timeout オプション
- CLI の retry オプション
- Config による retry 指定
- Step 別 retry 方針
- retry 待機時間制御
- retry の例外型による絞り込み
- workflow 全体 timeout
- timeout またはキャンセル専用の trace 状態
- 値を含む `ExecutionTrace`
- NuGet ロックファイル
- `#load "nuget: ..."`
- Step 名の名前空間化

---

## 20. 明確に禁止すること

初期設計では以下を禁止する。

```text
・Flow という独立概念を作ること
・Flow(...) という専用記法を作ること
・Step に Config 専用引数を追加すること
・Step に StepContext 専用引数を追加すること
・Step 間の入出力を自動推論すること
・上流出力を自動的に下流入力へ変換すること
・Config を Step ごとに自動注入すること
・並列実行すること
・分岐、統合を初期版に含めること
```

---

## 21. 未確定事項

以下は今後決める必要がある。

### 21.1 非同期 API

T20 では以下を採用する。

- `IAsyncStep<TOut>` を追加する
- 既存 `IStep<TOut>` は維持する
- `IStep<TOut>` を `Task<TOut>` 系へ統一しない
- `IStep<Task<T>>` は通常の同期 Step の戻り値型として扱う
- 非同期 Step は `RunAsync<TStep, TOut>()` などの明示 API で登録する
- 非同期ワークフロー実行 API として `ExecuteWorkflowAsync` を追加する
- 非同期 Step の `ExecuteAsync` には `CancellationToken` を渡す

T21 では以下を採用する。

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

T21 では以下を扱わない。

- CLI の timeout オプション
- 実行中 Step の強制停止
- workflow 全体 timeout
- timeout またはキャンセル専用の trace 状態

T22 では以下を採用する。

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

T22 では以下を扱わない。

- CLI の retry オプション
- Config による retry 指定
- Step 別 retry 方針
- retry 待機時間制御
- retry の例外型による絞り込み

### 21.2 Config 読み込み責務

T23 では、標準 Config 読み込みをエンジン実行前処理として提供する。

Entry 側は `WithConfig<TConfig>()` で単一 Config 型を明示する。

CLI `run` は `.csx` ロード後、Entry の Config 型メタ情報と `--config` の path を使って YAML を型付き Config に変換し、Step 実行前に `StepContext.Set<TConfig>(config)` で登録する。

`--set` は T23 では `EngineArguments.Settings` に保持するだけで、標準 Config には反映しない。

複数 Config ファイルの統合、名前付き Config、Config 型自動推論は今後の課題とする。

### 21.3 CLI override の仕様

T24 では、`--set` を標準 Config に対するプロパティ path override として扱う。

適用順は以下とする。

1. `--config` の YAML を `TConfig` に変換する
2. `--set` を Config に適用する
3. `DataAnnotations` と `IValidatableObject` を検証する
4. 検証済み Config を `StepContext` に登録する

プロパティ path は C# の公開プロパティ名を `.` でたどる。プロパティ名の照合は実行環境の言語設定に依存しない `StringComparison.Ordinal` 相当の完全一致とし、存在しないプロパティは `CONFIG_LOAD_FAILED` とする。

入れ子プロパティの途中が `null` の場合、引数なしで生成できるクラスは自動生成して続行する。生成できない場合は `CONFIG_LOAD_FAILED` とする。

配列またはリストは、`Items[0].Name=value` のような既存要素への添字 override だけを扱う。自動拡張、配列全体またはリスト全体の置換は扱わない。

型変換は override 対象プロパティの型に対して行う。初期範囲では `string`、`bool`、`int`、`long`、`double`、`decimal`、`enum`、nullable な基本型を扱う。

同一 key の複数 override は後勝ちとする。

無効書式、存在しないプロパティ、型変換失敗、配列またはリストの添字不正は `CONFIG_LOAD_FAILED` とする。ただし、CLI 解析層で `--set` の値がない、`key=value` になっていない、または key が空の場合は CLI 解析エラーとして終了コード 2 で失敗する。

`engine validate` は T24 では Config path の存在確認までを維持し、override の型検証は `engine run` で行う。

複数 Config ファイル指定時の統合規則は T24 では扱わない。

### 21.4 Produce 後の値の寿命

`StepInput` に追加された値を、最後まで保持するか、スコープ管理するかは未確定。

初期版では、CompositeStep の実行中は保持し続ける設計が単純である。

### 21.5 トレース値の保存

`ExecutionTrace` に `StepInput`、Config、Step 出力の値を保存するかは未確定。

初期版では、値そのものは保存せず、Step 名、状態、所要時間、エラーコードを優先する。

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
エンジンは Step 間の接続を自動推論しない。
```

この設計により、以下を両立する。

- Step の疎結合
- Config と実行時データの統一的な扱い
- 上流出力の一部だけを下流へ渡す明示性
- Flow/Step 概念の一本化
- `.csx` での実用的な書き心地
- `Dotnet.Script.Core` による NuGet / 外部 `.csx` 解決
