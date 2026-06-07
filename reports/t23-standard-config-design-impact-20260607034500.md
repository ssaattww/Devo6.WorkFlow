# Sub-agent実行レポート

## タスク

T23「標準 Config 読み込みと `StepContext` 格納」の設計影響調査。

## sub-agentを使う理由

Config 契約、CLI 契約、Engine 実行経路、`StepContext` API、検証仕様にまたがるため、実装前に設計影響を分離して確認する。

## 対象範囲

- `tasks-status.md` の T23
- `phases-status.md` の P8
- `doc/workflow_engine_spec.md` の Config、`EngineArguments`、`StepContext`、CLI `run`、`validate` 関連節
- `src/Devo6.WorkFlow.Abstractions/EngineArguments.cs`
- `src/Devo6.WorkFlow.Abstractions/StepContext.cs`
- `src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `src/Devo6.WorkFlow.Cli/Program.cs`
- `tests/Devo6.WorkFlow.Tests` の CLI、Config、`StepContext` 関連テスト

## 対象外

- 実装変更
- 設計書の編集
- `tasks-status.md` と `phases-status.md` の更新
- T24 の CLI override 本実装
- README 作成

## 実行コマンド

- `git status --short --branch`
- `rg -n "T23|標準 Config|Config|EngineArguments|StepContext|--config|--set|validate|CLI run|P8" tasks-status.md phases-status.md doc/workflow_engine_spec.md`
- `rg --files src tests/Devo6.WorkFlow.Tests | rg '(EngineArguments|StepContext|WorkflowExecutionOptions|CompositeStep|Program|Config|Cli|CLI|StepContext|Workflow)'`
- `nl -ba src/Devo6.WorkFlow.Abstractions/EngineArguments.cs | sed -n '1,220p'`
- `nl -ba src/Devo6.WorkFlow.Abstractions/StepContext.cs | sed -n '1,260p'`
- `nl -ba src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs | sed -n '1,220p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CompositeStep.cs | sed -n '1,620p'`
- `nl -ba src/Devo6.WorkFlow.Cli/Program.cs | sed -n '1,320p'`
- `rg -n "EngineArguments|Config|config|--config|--set|StepContext|WorkflowExecutionOptions|Settings|validate|run" tests/Devo6.WorkFlow.Tests src -g '*.cs'`
- `nl -ba src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs | sed -n '1,170p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs | sed -n '340,430p'`
- `nl -ba src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs | sed -n '1,120p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs | sed -n '1,270p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs | sed -n '230,290p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs | sed -n '1,110p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs | sed -n '1,130p'`
- `rg -n "Yaml|YAML|YamlDotNet|System.Text.Json|Configuration|PackageReference" src tests *.sln **/*.csproj`
- `nl -ba doc/workflow_engine_spec.md | sed -n '230,360p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '520,610p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '880,970p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1140,1200p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1390,1460p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1530,1560p'`
- `cat package.json`

## 対象ファイル

- `tasks-status.md`
- `phases-status.md`
- `doc/workflow_engine_spec.md`
- `src/Devo6.WorkFlow.Abstractions/EngineArguments.cs`
- `src/Devo6.WorkFlow.Abstractions/StepContext.cs`
- `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
- `src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `src/Devo6.WorkFlow.Cli/Program.cs`
- `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`
- `tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`
- `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
- `tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs`
- `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
- `src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj`
- `src/Devo6.WorkFlow.Abstractions/Devo6.WorkFlow.Abstractions.csproj`

## 指摘事項

### 現在の tracking

T23 は未着手であり、Config ファイル値を Step が `StepContext` から型付き取得できること、存在しない設定ファイルと読み込み不能な設定ファイルが失敗になることを完了条件にしている。T24 は T23 後続であり、`--set` の入れ子キー、配列またはリスト値、型変換、複数 override の優先順位、無効書式を扱う。P8 は T23 と T24 による Config 標準化フェーズである。

### 現在の実装状態

`--config` と `--set` は、現時点では標準 Config 読み込みまでは行っていない。CLI は `--config` を Entry `.csx` ディレクトリ基準で絶対パス化し、`EngineArguments.ConfigPath` に入れる。`--set` は `key=value` の文字列辞書として `EngineArguments.Settings` に入れる。`Program.Main` は `run` の場合に `WorkflowExecutionOptions(engineArguments: ...)` を作り、`CsxEntryLoader.Execute` へ渡す。

`CompositeStep.ExecuteWorkflowAsync` は `StepContext` を作成し、`WorkflowExecutionOptions.EngineArguments` があれば `context.Set(options.EngineArguments)` で型キー登録する。標準 YAML 読み込み、型変換、Config の `StepContext` 登録はない。

`validate` は `CsxEntryLoader.Validate` の `CsxValidationOptions.ConfigPaths` を通じて Config ファイルの存在だけを確認する。読み込み不能、YAML 構文、型変換、DataAnnotations 検証は現在の `validate` では扱っていない。

`WorkflowErrorCodes` には `CONFIG_NOT_FOUND` と `CONFIG_LOAD_FAILED` がすでに存在する。`CONFIG_LOAD_FAILED` は T23 の読み込み不能、YAML 構文、型変換失敗、検証失敗の結果化に使える候補である。ただし、詳細分類を増やす設計はまだない。

### 設計書の現在の状態

`doc/workflow_engine_spec.md` は、Config は `StepInput.Context` から取得し、YAML を標準形式とし、相対パスは Entry `.csx` ディレクトリ基準で解決するとしている。一方で、初期版では `--config` は `EngineArguments` に保持し、Config YAML は標準では型変換しない、と明記している。

同じ設計書は、Config 読み込みを「エンジン初期処理」または「ユーザー定義 Config 読み込み Step」のどちらも許可するとしている。T23 は「標準 Config 読み込み」を実装する task なので、この候補を未確定のままにせず、標準経路の契約を追加する必要がある。

`WorkflowExecutionOptions` の設計節は `EngineArguments`、`StepTimeout`、`Retry` だけを示している。T23 で型付き Config の対象型や読み込みオプションを渡すなら、この節の更新が必要である。

検証節は、初期版では Config ファイル存在確認だけ、型付き Config への変換と検証はユーザー Step が行う、としている。T23 後は CLI `run` の標準読み込みと、`validate` で確認する範囲の追記が必要である。

初期版の対象外一覧には「Config YAML の標準型変換」が残っている。T23 完了後は、初期版後の対象として実装済みに寄せるか、対象外の文脈を「初期版では扱わない」に限定する整理が必要である。

### 採用案

T23 の採用案は、標準 Config 読み込みを engine 実行前処理として実装し、Step 実行開始前に型付き Config を `StepContext` へ登録する形が最小である。

標準 Config の対象型は `WorkflowExecutionOptions` に明示的に渡す。例として `ConfigType` または generic API を持つオプションを用意し、CLI `run` は `.csx` から Entry をロードした後、Entry 側に宣言された Config 型または CLI/loader 側で指定された Config 型を使って YAML を変換する。T23 の最小契約では、Config は型キーで `StepContext.Set<TConfig>(config)` に登録し、Step は `input.Context.Get<TConfig>()` で取得する。名前付きキーは標準では使わず、複数 Config や差し替えは T23 の対象外に置く。

読み込み責務は CLI ではなく engine 側に置くのがよい。理由は、`Program.Main` に YAML 解析と型変換を持たせると CLI 以外の `CsxEntryLoader.Execute` や `CompositeStep.ExecuteWorkflow` から同じ契約を使えないためである。`CsxEntryLoader` は Entry `.csx` のロード、Entry 選択、Entry ディレクトリ基準の情報を持つため、標準 Config 読み込みの起点として自然である。ただし、実際の YAML 解析と型変換は専用の engine 内部 loader に分ける方が、CLI、validate、単体検査で再利用しやすい。

`WorkflowExecutionOptions` は、既存の `EngineArguments` を残したまま、標準 Config 読み込みのための最小オプションを追加するのがよい。候補は `Type? ConfigType`、または `IReadOnlyList<ConfigBinding>` である。T23 は単一 Config を扱うなら `ConfigType` で十分だが、将来の複数 Config を考えると `ConfigBinding` 形式の方が拡張しやすい。T23 の完了条件は単一 Config で足りるため、設計書には「T23 は単一 Config 型、複数 Config は対象外」と明記する。

`EngineArguments.ConfigPath` は引き続き Step から参照できる実行時引数として残す。標準読み込み済み Config は `EngineArguments` の中に入れず、`StepContext` の独立した型付き値として登録する。これにより、既存の `EngineArguments` 契約と T24 の `Settings` 契約を壊さない。

### エラー扱い

`--config` 未指定時は、標準 Config 型が要求されていないなら成功とする。標準 Config 型が要求されている場合に `--config` 未指定を失敗にするかは設計判断が必要である。T23 の利用者目線 E2E は `--config` 指定時の読み込みを優先する条件なので、最小案では「Config 型が要求され、Config path が空なら Config は登録しない。Config を必要とする Step が `Get<TConfig>()` した時点で通常の未登録値失敗になる」とする。ただし、利用者に早く原因を返すなら `CONFIG_NOT_FOUND` ではなく `CONFIG_LOAD_FAILED` または新しい error code が必要になる。

存在しない config file は、`validate` と `run` の両方で `CONFIG_NOT_FOUND` とする。現行 `validate` は存在確認を持つが、`run` は存在確認を実行前に行っていない。T23 では `run` でも Step 実行前に失敗させる必要がある。

読み込み不能 config file は、ファイルは存在するが読み取り権限や IO 例外で読めない場合を `CONFIG_LOAD_FAILED` とする。YAML 構文エラー、型変換失敗、DataAnnotations または `IValidatableObject` の失敗も、T23 で詳細 error code を増やさないなら `CONFIG_LOAD_FAILED` にまとめる。`ValidationError.Path` または `WorkflowResult.ErrorMessage` には config path またはプロパティパスを含める。

空 config は設計判断が必要である。最小案では、対象 Config 型が parameterless に作成でき、必須検証に失敗しないなら成功とする。必須プロパティや DataAnnotations に違反するなら `CONFIG_LOAD_FAILED` とする。空ファイルを常に失敗にすると、任意設定だけの Config が表現しにくい。

`--config` 未指定時は既存 CLI と互換に保つ。Config 型が要求されていない workflow は成功し、`EngineArguments.ConfigPath` は空文字のままでよい。

### validate と run の扱い

T23 は CLI `run` の利用者目線 E2E を優先する。したがって、最初の失敗検査は「`engine run main.csx --config appsettings.yaml` で YAML 値が `StepContext.Get<AppConfig>()` から取得できる」ことに置くのがよい。

`validate` は T23 では存在確認までを維持し、型変換と DataAnnotations 検証は任意または後続設計にしてもよい。ただし、標準 Config 型を validate 時に解決できる設計を採る場合は、`validate` でも YAML 構文と型変換を確認できる。T23 の scope を絞るなら、設計書には「T23 の必須検証は run 前処理、validate は config path 存在確認まで。型変換 validate は別 task または追加 scope」と明記する。

### T24 との境界

T23 は `--set` を適用しない。既存どおり `EngineArguments.Settings` に文字列辞書として保持するだけに留める。T24 は、入れ子キー、配列またはリスト値、型変換、複数 override の優先順位、無効書式を扱う。T23 で `--set` を YAML に混ぜると、T24 の優先順位と型変換仕様を先取りしてしまう。

T23 の設計書には、`--config` の YAML 値を型付き Config に変換して `StepContext` へ登録するが、`--set` の統合は T24 対象外として明記する必要がある。

### 代替案

代替案 1 は CLI で YAML を読み込む案である。CLI `run` E2E は作りやすいが、CLI 以外の engine 利用者が標準 Config 読み込みを使えず、契約が CLI に閉じるため推奨しない。

代替案 2 はユーザー定義 Config 読み込み Step を標準部品として提供する案である。既存設計の延長だが、T23 の完了条件である「Config ファイル値を Step が `StepContext` から型付き取得できる」を満たすには、各 workflow に明示 Step を挟む必要がある。標準読み込みという task 名ともずれるため、T23 の主案にはしない。

代替案 3 は `StepContext` に YAML ツリーや辞書を登録する案である。T24 の override とは相性がよいが、T23 の完了条件にある型付き取得を満たしにくい。T23 では型付き Config を登録し、必要なら内部実装で YAML ツリーを一時的に使うに留める。

代替案 4 は `EngineArguments` に Config オブジェクトを保持する案である。既存の CLI 引数保持責務が広がり、Step 側の取得が `input.Context.Get<EngineArguments>().Config` のようになって Config を実行時引数に閉じ込めるため、設計の「Config は `StepContext` に置く」方針と合わない。

## 結果

### 設計更新が必要な箇所

- `doc/workflow_engine_spec.md` の `6.3 Config 読み込み`: T23 後は標準 Config 読み込みを engine 実行前処理として定義し、ユーザー定義 Config 読み込み Step は任意の拡張として位置付ける。
- `doc/workflow_engine_spec.md` の `6.4 Config を StepContext に格納する例`: ユーザー Step で読む例だけでなく、標準読み込みで `StepContext.Get<AppConfig>()` できる例へ更新する。
- `doc/workflow_engine_spec.md` の `6.5 CLI による Config 指定`: 初期版の保持のみという記述に、T23 以降は `--config` を YAML として読み込み、型付き Config を登録する契約を追加する。
- `doc/workflow_engine_spec.md` の `6.6 CLI による Config 上書き`: T23 では `--set` は保持のみ、統合は T24 と明記する。
- `doc/workflow_engine_spec.md` の `10.2 エンジンの役割`: 初期 `StepContext` 生成後、Step 実行前に標準 Config を読み込んで `StepContext` に登録する手順を追加する。
- `doc/workflow_engine_spec.md` の `11.3 エラー対象`: `CONFIG_NOT_FOUND` と `CONFIG_LOAD_FAILED` の扱いを、存在しない file、読み込み不能、YAML 構文、型変換、検証失敗に分けて記述する。
- `doc/workflow_engine_spec.md` の `14.5 WorkflowExecutionOptions`: 標準 Config 読み込みに必要な Config 型指定または Config binding 指定を追加する。
- `doc/workflow_engine_spec.md` の `14.7 型定義方針`: T23 では単一 Config 型、登録後は読み取り専用スナップショットとして扱うことを確認する。
- `doc/workflow_engine_spec.md` の `17.2 検証対象` と `17.4 Config 検証`: T23 で `run` がどこまで確認し、`validate` がどこまで確認するかを更新する。
- `doc/workflow_engine_spec.md` の `19.1`、`19.2`、`19.3`: 「Config YAML の標準型変換」と「標準 Config 読み込み」の扱いを初期版対象外のまま誤読されないように整理する。
- `doc/workflow_engine_spec.md` の `21.2 Config 読み込み責務`: 未確定事項を T23 の決定で更新し、複数 Config は未確定として残す。
- `doc/workflow_engine_spec.md` の `21.3 CLI override の仕様`: T24 に残す項目として維持し、T23 から除外する。

### TDD 検査案

1. CLI `run` E2E: `.csx` 内に `AppConfig` 型と Step を定義し、`engine run main.csx --config appsettings.yaml` で Step が `input.Context.Get<AppConfig>()` から YAML 値を取得して marker file に書けることを確認する。
2. Entry directory 解決: `--config config/appsettings.yaml` が Entry `.csx` ディレクトリ基準で解決され、作業ディレクトリに依存しないことを確認する。
3. 存在しない config file: CLI `run` が Step を実行せず非 0 になり、`CONFIG_NOT_FOUND` を標準エラーへ出すことを確認する。既存 `validate` の存在確認検査も維持する。
4. 読み込み不能 config file: 読み取り失敗または破損 YAML で CLI `run` が非 0 になり、`CONFIG_LOAD_FAILED` を返すことを確認する。
5. 型変換失敗: YAML の値が Config 型へ変換できない場合に `CONFIG_LOAD_FAILED` になり、`StepContext` には Config が登録されないことを engine 単体検査で確認する。
6. 空 config: 空 YAML が、任意プロパティのみの Config では成功し、必須検証を持つ Config では `CONFIG_LOAD_FAILED` になることを確認する。
7. `--config` 未指定: Config 型を要求しない既存 workflow が引き続き成功し、既存 CLI テストが壊れないことを確認する。
8. T24 境界: T23 では `--set` が標準 Config に反映されず、既存どおり `EngineArguments.Settings` から文字列として取得できることを確認する。

### Markdown と用語の標準

T23 の設計更新と実装時は、関数名は英語、C# 文書注釈は日本語、関数とプロパティの説明文は必須とする。README は T30 対象のため、T23 では更新しない。Markdown は repository の textlint と用語 lint に従い、ルールが不適切な場合は報告する。

## リスク

- 標準 Config 型をどこで宣言するかが未決定である。`.csx` 内の Config 型を engine がどう特定するかを決めないと、CLI `run` だけでは型付き変換の対象型が分からない。
- YAML 解析ライブラリの .NET 依存が現時点では見当たらない。T23 実装では `YamlDotNet` などの追加依存が必要になる可能性が高い。
- `WorkflowExecutionOptions` は現在 constructor で `EngineArguments` を受け取り、`StepTimeout` と `Retry` は settable property である。Config binding を constructor に足すか property に足すかで、既存 API との一貫性を確認する必要がある。
- `validate` で型変換まで行うには、Entry `.csx` の Config 型を validate 時に特定する必要がある。T23 では `run` E2E 優先として scope を絞らないと、validate 契約が膨らむ。
- `StepContext.Set<T>()` は上書き可能であるため、標準 Config 登録後にユーザー Step が同じ型を上書きできる。これは既存 API 方針には合うが、読み取り専用スナップショットという設計文言との関係を明確にする必要がある。
- T23 で `--set` を少しでも適用すると、T24 の override 優先順位と型変換仕様を先取りする。T23 では保持のみを維持するのが安全である。
- 読み込み不能 file は OS 権限や実行環境に依存するため、E2E では破損 YAML による `CONFIG_LOAD_FAILED` を主検査にし、権限失敗は単体検査または platform 条件付きにする方が安定する。
