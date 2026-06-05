# csx ワークフローエンジン 設計資料

## 1. 目的

本設計資料は、C# Script（`.csx`）上で定義できるワークフローエンジンライブラリの要件および基本設計をまとめる。

本ライブラリは、Cake のような書き心地を一部参考にしつつ、実体としては以下を目的とする。

- `.csx` で Step を組み合わせたワークフローを定義できること
- Step と Flow を別概念にせず、すべて Step として扱うこと
- 上流 Step の結果を下流 Step へ明示的に渡せること
- Step の入力を可変長に扱えること
- Config、Context、上流出力を統一的に扱えること
- Config は YAML から読み込み、Context に保持できること
- `.csx` の解決、NuGet 解決、外部スクリプト解決には dotnet-script の core ライブラリを利用すること

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
Step("Main")
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
- マージ実行
- グラフ型依存解決
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
- 上流 Step の出力から生成した下流用 DTO
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

### 5.1 Context は StepInput に自動で含める

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

### 5.2 Context の責務

`StepContext` は、実行全体で共有される値を保持する。

主な用途:

- Config
- ConfigStore
- EngineArguments
- Logger
- CancellationToken
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
    public T Get<T>();

    public T Get<T>(string name);

    public void Set<T>(T value);

    public void Set<T>(string name, T value);

    public bool TryGet<T>(out T value);

    public bool TryGet<T>(string name, out T value);
}
```

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

### 6.2 Config ファイル形式

Config ファイルは YAML とする。

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

Config 読み込みは以下のどちらも許可する。

1. エンジンが初期処理として読み込み、Context に格納する
2. Config 読み込み用 Step をユーザーが明示的に定義し、Context に格納する

ただし、Step ごとに Config 読み込み Step を挟む設計は避ける。

Config 読み込み Step を使用する場合は、ワークフローの先頭またはまとまりの先頭で一度だけ行うことを想定する。

### 6.4 Config を Context に格納する例

```csharp
public sealed class LoadConfigStep : IStep<Unit>
{
    public Unit Execute(StepInput input)
    {
        EngineArguments args = input.Context.Get<EngineArguments>();

        AppConfig config = YamlConfig.Load<AppConfig>(args.ConfigPath);

        input.Context.Set(config);

        return Unit.Value;
    }
}
```

### 6.5 CLI による Config 指定

エンジン起動時に Config ファイルを指定できる。

```bash
engine run main.csx --config config.yaml
```

### 6.6 CLI による Config 上書き

CLI 引数で Config の一部を上書きできることを要件とする。

例:

```bash
engine run main.csx --config config.yaml --set convert.toUpper=false
```

上書き仕様の詳細は未確定だが、最低限以下を検討対象とする。

- 単純キーの上書き
- ネストキーの上書き
- bool / int / string などの型変換
- 複数 `--set` 指定

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
Step("Main")
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

`StoreAs` は、戻り値をそのまま登録するための省略 API として扱う。

```csharp
.StoreAs<LoadResult>()
```

これは以下の省略形である。

```csharp
.Produce<LoadResult>(x => x)
```

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

Step はメソッドチェーンで定義せず、通常の C# クラスとして定義する。

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

`#load` による外部スクリプト解決を想定する。

```csharp
#load "./build.csx"
#load "./test.csx"
```

`.csx` の実行、NuGet 解決、外部スクリプト解決には dotnet-script の core ライブラリを利用する。

---

## 10. dotnet-script core 利用

### 10.1 必須要件

エンジンは dotnet-script の core ライブラリを利用する。

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
5. `.csx` を dotnet-script core 経由でロードする
6. `.csx` 上の CompositeStep 定義を取得する
7. 指定された Step を実行する

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
- Config が存在しない
- Config のバインド失敗
- Step 実行時例外
- `.csx` のロード失敗
- NuGet 解決失敗
- `#load` 解決失敗

---

## 12. 非同期対応

### 12.1 方針

内部設計は将来的な非同期対応を考慮する。

同期 Step を基本としつつ、非同期 Step も扱える余地を残す。

候補:

```csharp
public interface IStep<TOut>
{
    TOut Execute(StepInput input);
}
```

```csharp
public interface IAsyncStep<TOut>
{
    Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken);
}
```

または、初期から `Task<TOut>` に統一する。

```csharp
public interface IStep<TOut>
{
    Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken);
}
```

どちらを採用するかは未確定。

---

## 13. DSL 案

### 13.1 基本形

```csharp
Step("Main")
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
Step("Main")
    .Run<ReadTitleStep, string>()
        .Produce<string>("title", x => x)
    .Run<ReadBodyStep, string>()
        .Produce<string>("body", x => x)
    .Run<MergeStep, Article>()
        .StoreAs<Article>();
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

### 13.3 Config を Context に置く例

```csharp
Step("Main")
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

## 14. 主要インターフェース案

### 14.1 IStep

```csharp
public interface IStep<TOut>
{
    TOut Execute(StepInput input);
}
```

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
public sealed class CompositeStep<TOut> : IStep<TOut>
{
    public TOut Execute(StepInput input);
}
```

### 14.5 Unit

```csharp
public readonly struct Unit
{
    public static readonly Unit Value = new Unit();
}
```

---

## 15. 明確に禁止すること

初期設計では以下を禁止する。

```text
・Flow という独立概念を作ること
・Flow(...) という専用 DSL を作ること
・Step に Config 専用引数を追加すること
・Step に Context 専用引数を追加すること
・Step 間の入出力を自動推論すること
・上流出力を自動的に下流入力へ変換すること
・Config を Step ごとに自動注入すること
・並列実行すること
・分岐、マージを初期版に含めること
```

---

## 16. 未確定事項

以下は今後決める必要がある。

### 16.1 非同期 API

- 同期 API を基本にするか
- 初期から `Task<T>` に統一するか
- 同期・非同期の両方を許可するか

### 16.2 Config 読み込み責務

- エンジンが標準で Config を読み込むか
- Config 読み込み Step を標準部品として提供するか
- 両方を許可するか

現時点では、Config は Context に置く方針で合意済み。

### 16.3 CLI override の仕様

- ネストキーの書式
- 配列の上書き
- 型変換仕様
- 複数 Config ファイル指定時のマージルール

### 16.4 Step 登録名

CompositeStep を名前で参照する場合、以下を決める必要がある。

- Step 名の一意性
- 同名定義時の扱い
- 外部 `.csx` から読み込んだ Step 名の衝突時の扱い

### 16.5 Produce 後の値の寿命

`StepInput` に追加された値を、最後まで保持するか、スコープ管理するかは未確定。

初期版では、CompositeStep の実行中は保持し続ける設計が単純である。

---

## 17. 最終整理

本設計の中核は以下である。

```text
Step が唯一の実行単位。
Flow は独立概念ではなく CompositeStep として扱う。
Step は StepInput のみを受け取る。
StepInput は可変長の型付き・名前付き入力集合である。
Context は StepInput に自動で含まれる。
Config は Context に置く。
上流 Step の結果を下流に渡す場合は Produce で明示する。
エンジンは Step 間の接続を自動推論しない。
```

この設計により、以下を両立する。

- Step の疎結合
- Config と実行時データの統一的な扱い
- 上流出力の一部だけを下流へ渡す明示性
- Flow/Step 概念の一本化
- `.csx` での実用的な書き心地
- dotnet-script core による NuGet / 外部スクリプト解決
