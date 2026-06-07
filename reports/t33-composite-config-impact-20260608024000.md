# T33 Composite Config 影響調査

## 目的

T33 の未コミット差分が、Step 内 Config 型と CompositeStep 境界 Config 型に関する指摘へどこまで対応しているかを調査した。

親は本報告を材料に、T33 の設計差し替え要否を判断する。

## 読んだ範囲

- `AGENTS.md`
- `tasks-status.md` の T33 周辺
- `doc/workflow_engine_spec.md` の Config 章と Step 登録単位 Config API 章
- 指定された T33 未コミット差分
- `design-doc-maintainer` skill
- `report-output-manager` skill
- `markdown-word-checker` skill

## 結論

現在差分は、Step 登録単位 Config の実行時機構には対応している。

- `CompositeStep<TOut>.WithConfig<TConfig>(string sectionPath)` を追加している。
- `StepConfigRegistration` に Step 型、YAML 区画 path、Config 型、Step index を保持している。
- `CsxEntryLoader` は Entry ロード後に `StepConfigRegistrations` を見て、単一 Config YAML から区画ごとの Config を事前読み込みする。
- `StandardConfigLoader` は区画選択、接頭辞付き `--set`、型変換、検証を最初の Step 実行前に完了する。
- `CompositeStep` は対象 Step の実行直前に検証済み Config を `StepContext` へ登録する。

ただし、ユーザー指摘のうち「Config 型は各 Step の内側に定義されるべきではないか」「CompositeStep は内部で呼ぶ Step の Config を内包する Config クラスを持つと YAML mapping が自然ではないか」には、契約としては未対応である。

現在の設計書とテストは、`LoadConfig`、`ConvertConfig`、`SaveConfig` を Step 外の独立型として例示しており、`LoadStep.Config`、`ConvertStep.Config`、`SaveStep.Config`、`MainConfig` のような所有境界を標準契約にしていない。

## 現在差分で対応できていること

### Step 登録単位の関連付け

差分は `WithConfig<TConfig>("Load")` を直前 Step の metadata として扱う方向で実装されている。

このため、`TConfig` がトップレベル型でも Step 内の nested 型でも、型として public に参照できれば原理的には読み込める。つまり、次のような呼び出し自体は現在の API でも表現できる。

```csharp
var Main = CompositeStep.Define("Main")
    .Run<LoadStep, LoadResult>()
        .WithConfig<LoadStep.Config>("Load");
```

この範囲は指摘の前半に部分対応している。

### 実行前の一括読み込み

差分は宣言済み Step Config を実行前にすべて読み込み、失敗時は最初の Step を実行しない。

これは T33 の受け入れ条件である「Config 変換または検証失敗時は最初の Step 実行前に失敗する」を満たす方向である。

### YAML 区画と override

差分は `Load`、`Convert`、`Save` のような YAML 区画 path を `WithConfig<TConfig>(string sectionPath)` の `sectionPath` と一致させる。

`--set Convert.ToUpper=false` は `Convert` 区画へ対応付けられ、Config 型へは `ToUpper=false` として適用される。未宣言区画や prefix 関係の区画は失敗扱いになっている。

## 現在差分で未対応のこと

### Step 内 Config 型を標準契約にしていない

設計書は `LoadConfig`、`ConvertConfig`、`SaveConfig` を Step 外のトップレベル型として示している。

テストの `.csx` 例も同じで、`input.Context.Get<LoadConfig>()` のように取得している。

そのため、現在差分は Step 内 Config 型を許容しうるが、推奨契約として要求していない。

### CompositeStep 境界 Config 型を扱っていない

現在差分は各 Step Config を個別区画から直接読み込み、`StepConfigValue` の一覧として実行 option に保持する。

`MainConfig` のような CompositeStep 境界 Config 型を生成し、その中の `Load`、`Convert`、`Save` property へ YAML を mapping する構造は持っていない。

### YAML の自然な mapping が API に反映されていない

現在契約では YAML の親 mapping は sectionPath の集合として扱われる。

一方、ユーザー指摘の自然な形は、CompositeStep が `MainConfig` を境界型として持ち、YAML 全体がその型へ mapping され、各 property が Step 内 Config 型になる形である。

```csharp
public sealed class MainConfig
{
    public LoadStep.Config Load { get; set; } = new();

    public ConvertStep.Config Convert { get; set; } = new();

    public SaveStep.Config Save { get; set; } = new();
}
```

この形は現在の `WithConfig<TConfig>("section")` 群だけでは契約として表現されない。

## 推奨する標準契約

T33 は、標準契約を次の 1 案へ差し替えるのが自然である。

### 契約案

Config 型の所有者は Step とし、CompositeStep は内部で呼ぶ Step の Config を束ねる境界 Config 型を持つ。

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
        return new LoadResult { Text = File.ReadAllText(config.Path) };
    }
}

public sealed class MainConfig
{
    public LoadStep.Config Load { get; set; } = new();

    public ConvertStep.Config Convert { get; set; } = new();

    public SaveStep.Config Save { get; set; } = new();
}

var Main = CompositeStep.Define("Main")
    .WithConfig<MainConfig>()
    .Run<LoadStep, LoadResult>()
        .WithConfig<LoadStep.Config>("Load")
        .Produce<ConvertInput>(x => new ConvertInput { Text = x.Text })
    .Run<ConvertStep, ConvertResult>()
        .WithConfig<ConvertStep.Config>("Convert")
        .Produce<SaveInput>(x => new SaveInput { Content = x.ConvertedText })
    .Run<SaveStep, Unit>()
        .WithConfig<SaveStep.Config>("Save")
        .Discard();
```

標準 YAML は次の形にする。

```yaml
Load:
  Path: ./input.txt

Convert:
  ToUpper: true
  Mode: Normal

Save:
  Path: ./output.txt
```

`MainConfig` は YAML 全体の mapping 境界を表す。`Load` property は `LoadStep.Config`、`Convert` property は `ConvertStep.Config`、`Save` property は `SaveStep.Config` に対応する。

Step 実行時は、対象 Step の実行直前に境界 Config から該当 property 値を取り出し、`StepContext.Set<LoadStep.Config>(config.Load)` のように登録する。

### この案に絞る理由

- Config の所有関係が Step 型の内側に閉じる。
- YAML の親 mapping と CompositeStep 境界が一致する。
- Step は自分の Config 型だけを `StepContext.Get<Step.Config>()` で読む。
- `--set Convert.ToUpper=false` は `MainConfig.Convert.ToUpper` への override として自然に説明できる。
- CompositeStep が呼び出す Step Config の組み合わせを境界 Config 型で公開できるため、利用者が Entry 単位の Config 構造を把握しやすい。

## 必要な設計書変更

`doc/workflow_engine_spec.md` は差し替えが必要である。

- 6.2 の YAML 区画説明を、独立 Config 型の集合ではなく CompositeStep 境界 Config 型の property mapping として説明する。
- 6.3 の推奨例を `WithConfig<MainConfig>()` と `WithConfig<LoadStep.Config>("Load")` の組み合わせへ差し替える。
- 6.4 の Config 定義例を `LoadStep.Config`、`ConvertStep.Config`、`SaveStep.Config` へ差し替える。
- `StepContext.Get<LoadConfig>()` の例を `StepContext.Get<LoadStep.Config>()` へ差し替える。
- `WithConfig<TConfig>()` の説明を、互換 API だけではなく、標準契約では CompositeStep 境界 Config 型を宣言する API として再定義する。
- Entry 全体 Config 互換 API の位置づけを、標準境界 Config と同じ形に見えるため再整理する。
- 14.4 の API 章に、境界 Config 型と Step 登録単位 Config metadata の関係を明記する。
- 17.4 と 21.2 で、読み込み単位を「各区画を個別に型変換」から「境界 Config 型へ YAML 全体を変換し、登録 metadata で各 Step 用 property を抽出」に変更する。
- 21.3 の `--set` 仕様は、区画 path 接頭辞を境界 Config の property path として扱う説明に変更する。

## 必要なテスト変更

T33 のテストは、現在の外側 Config 型例から標準契約例へ差し替える必要がある。

- CLI 利用者目線 E2E で、`LoadStep.Config`、`ConvertStep.Config`、`SaveStep.Config` を Step 内 nested 型として定義する。
- 同じ `.csx` に `MainConfig` を定義し、`Load`、`Convert`、`Save` property が Step 内 Config 型であることを検査する。
- `WithConfig<MainConfig>()` と `WithConfig<LoadStep.Config>("Load")` の併用で Config が読み込まれることを検査する。
- `--set Convert.ToUpper=false` が `MainConfig.Convert.ToUpper` に適用され、実行直前に `ConvertStep.Config` として登録されることを検査する。
- `MainConfig` に宣言された property がない sectionPath、または型が登録 Config 型と一致しない sectionPath は最初の Step 実行前に失敗する検査を追加する。
- 宣言済み Step Config の property が YAML に存在しない場合の扱いを決め、その契約に沿った検査を追加する。推奨は property 初期値で生成できるなら許可、必須性は DataAnnotations に委ねる形である。
- `validate` は従来どおり Config path の存在確認までで、境界 Config の型変換や override 検証を行わないことを検査する。
- 既存 `WithConfig<TConfig>()` だけを使う旧形式の互換検査を残す場合、標準契約との差が分かる名前に変更する。

## 必要な実装変更

現在差分を活かすなら、以下を追加または差し替える。

- `WithConfig<TConfig>()` が標準では CompositeStep 境界 Config 型を表すことを明確にする。
- `StepConfigRegistration` は Step Config 型と sectionPath に加え、境界 Config 型上の property を解決する責務を持てるようにする。
- `StandardConfigLoader` は Step Config ごとに YAML 区画を個別 deserialize するのではなく、まず境界 Config 型へ YAML 全体を deserialize する。
- `--set` は境界 Config 型へ raw path のまま適用し、Step 登録単位の sectionPath は適用対象 property の検査と Step 直前登録に使う。
- `LoadStep.Config` などの nested 型を YamlDotNet が生成できるよう、public な引数なし constructor と public settable property を標準条件として明記し、実装上も例外を `CONFIG_LOAD_FAILED` に変換する。
- `StepConfigValue` は個別に読み込んだ Config instance ではなく、境界 Config から抽出した Step Config instance を保持する形へ変更する。
- `CsxEntryLoader.PrepareExecutionOptions` は、Step 登録単位 Config がある場合に境界 Config 型が未宣言なら失敗するか、互換 fallback を使うかを明確化する。標準契約としては、Step 登録単位 Config がある場合は境界 Config 型の宣言を必須にするのが自然である。
- 同じ Entry で旧 Entry 全体 Config 互換 API と標準境界 Config API を区別できる metadata が必要である。単一の `ConfigType` だけでは意味が曖昧になる。

## 互換 API の扱い

### 標準契約に含めるべき点

- `WithConfig<TConfig>(string sectionPath)` は Step 登録単位の metadata として残す。
- `sectionPath` は境界 Config 型上の property path として扱う。
- Step 実行直前には、境界 Config から取り出した値を `StepContext.Set<TStep.Config>()` へ登録する。
- `--set` は境界 Config 型への property path override として説明する。区画接頭辞を剥がすというより、境界 Config の property path をそのままたどる契約にした方が自然である。

### 標準契約に含めない方がよい点

- Step 登録単位 Config と旧 Entry 全体 Config 互換 API の自由な併用規則は含めない方がよい。
- `WithConfig<TConfig>()` を「旧 Entry 全体 Config」と「標準境界 Config」の両方に同じ metadata で扱うのは避けるべきである。
- Step 型から `Config` nested 型を自動推論する契約は含めない方がよい。推論は便利だが、T33 の標準契約には `WithConfig<LoadStep.Config>("Load")` の明示を残す方が検証しやすい。
- Step 専用引数、Step 型プロパティへの Config 自動注入、複数 Config ファイル統合は引き続き標準外でよい。

### 判断ポイント

既存 `WithConfig<TConfig>()` は、現設計では Entry 全体 Config 互換 API とされている。

しかし推奨契約では `WithConfig<MainConfig>()` が CompositeStep 境界 Config 型の宣言になるため、同じ API 名の意味が衝突する。

親判断としては、次のどちらかを選ぶ必要がある。

- `WithConfig<TConfig>()` を境界 Config 型の標準 API へ昇格し、旧 Entry 全体 Config は互換動作として残すが、Step 登録単位 Config との併用時は境界 Config とみなす。
- 旧 Entry 全体 Config は `WithEntryConfig<TConfig>()` などへ分離する。破壊的変更になるため、現段階での影響は大きい。

T33 の自然な差し替えとしては、前者を推奨する。

## リスク

- 現在差分のまま進めると、Step 内 Config 型は利用者が選べるだけで、設計書の推奨形は Step 外 Config 型のまま残る。
- `WithConfig<TConfig>()` の意味が Entry 全体 Config のままだと、`MainConfig` を境界型として説明しにくい。
- 境界 Config 型を導入する場合、現在の `StandardConfigLoader.LoadStepConfigs` はかなりの部分を差し替える必要がある。
- 既存互換テストと標準契約テストを分けないと、標準契約が曖昧になる。

## 検証結果

次を実行し、いずれも終了コード 0 で完了した。

- `npm run lint:md`
- `npm run lint:md:terms`

`npm run lint:md` の対象一覧は reports 配下を含まなかったため、報告書単体は追加で次を確認した。

- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t33-composite-config-impact-20260608024000.md`: 終了コード 0
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t33-composite-config-impact-20260608024000.md`: reports 配下が `ignorePaths` により除外され、検査対象 0 件として skip
