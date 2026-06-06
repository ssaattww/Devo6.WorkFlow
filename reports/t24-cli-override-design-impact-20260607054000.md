# T24 CLI override 設計影響調査

## 調査対象

- `tasks-status.md` の T24
- `phases-status.md` の P8
- `doc/workflow_engine_spec.md` の Config、CLI override、`--set`、`StepContext`、Validation 関連節
- `src/Devo6.WorkFlow.Cli/Program.cs`
- `src/Devo6.WorkFlow.Engine/StandardConfigLoader.cs`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `src/Devo6.WorkFlow.Abstractions/EngineArguments.cs`
- `tests/Devo6.WorkFlow.Tests/StandardConfigLoadingContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`

## 実行コマンド

- `git status --short --branch`
- `rg -n "T24|CLI override|--set|override|Settings|WithConfig|StandardConfig|StepContext|Validation|CONFIG_LOAD_FAILED|P8" tasks-status.md phases-status.md doc/workflow_engine_spec.md src tests`
- `rg --files -g 'package.json' -g '.textlintrc*' -g 'textlint*' -g '*lint*'`
- `nl -ba tasks-status.md | sed -n '24,32p'`
- `nl -ba phases-status.md | sed -n '10,18p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '280,370p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '552,616p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1218,1240p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1330,1340p;1438,1480p;1588,1608p'`
- `nl -ba src/Devo6.WorkFlow.Cli/Program.cs | sed -n '1,230p'`
- `nl -ba src/Devo6.WorkFlow.Engine/StandardConfigLoader.cs | sed -n '1,180p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs | sed -n '380,475p'`
- `nl -ba src/Devo6.WorkFlow.Abstractions/EngineArguments.cs | sed -n '1,80p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CompositeStep.cs | sed -n '160,185p;505,520p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/StandardConfigLoadingContractTests.cs | sed -n '1,330p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs | sed -n '1,230p'`
- `cat package.json`
- `cat .textlintrc.json`
- `nl -ba src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs | sed -n '1,100p'`
- `rg -n "StandardConfigLoader|EngineArguments|Settings|ConfigLoadFailed|ConfigNotFound" src/Devo6.WorkFlow.Engine src/Devo6.WorkFlow.Abstractions tests/Devo6.WorkFlow.Tests`
- `rg -n "IValidatableObject|Validate\(" src tests doc/workflow_engine_spec.md`
- `nl -ba doc/workflow_engine_spec.md | sed -n '988,1000p;930,942p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs | sed -n '60,130p;130,200p'`

## 調査結果

T24 は未着手で、P8 は T23 完了後の残りとして CLI override を利用者目線の E2E で確認する状態である。T24 の完了条件には、`--set` の入れ子キー、配列またはリスト値、型変換、複数 override の優先順位、無効書式の検証エラー固定が明記されている。

設計書の 6.6 は T23 時点の暫定記述で、`--set` を `EngineArguments.Settings` に保持するだけで標準 Config に反映しないと書いている。21.3 も T24 で決める事項として残っている。T24 実装前に、この暫定記述を標準仕様へ更新する必要がある。

現状の CLI は `--set key=value` を解析し、同一キーは後勝ちで `Dictionary<string, string>` に保持する。`EngineArguments.Settings` は `IReadOnlyDictionary<string, string>` として Step から読める契約であり、T24 後もこの契約は維持すべきである。

標準 Config 読み込みは `CsxEntryLoader.PrepareExecutionOptions` から `StandardConfigLoader.Load(configPath, configType)` を呼び、YAML 変換と `DataAnnotations` 検証を行った後、`WorkflowExecutionOptions.StandardConfig` 経由で `CompositeStep` が `StepContext.Set<TConfig>(config)` に登録する。つまり override は YAML 読み込み後、Config 検証前、`StepContext` 登録前に適用するのが既存構造と一致する。

`validate` は現在、Config path の存在確認だけを行い、Config の型変換や `DataAnnotations` 検証は行っていない。T24 は CLI override の標準仕様なので、最初の検査は `engine run` の E2E に置くのが妥当である。`engine validate` で override の型変換まで検証する場合は、Entry の Config 型メタ情報取得後に Config 読み込み相当の検証を追加する必要があり、T24 の範囲を広げる。

## 採用案

T24 の標準仕様は、`--set` を「標準 Config に対するパス指定 override」として定義する。書式は `key=value` とし、key は Config 型の public instance property を `.` でたどる。例は `Convert.ToUpper=false` とする。CLI で受け取った文字列は引き続き `EngineArguments.Settings` に保持し、Step は指定された生の override 文字列を参照できる。

override 適用順は `--config` YAML を `TConfig` に変換し、次に `--set` を指定順に適用し、最後に `DataAnnotations` と `IValidatableObject` を検証する。成功した Config だけを `StepContext.Set<TConfig>(config)` に登録する。これにより YAML の既定値、CLI override、検証済みスナップショットの順序が明確になる。

複数 override はコマンドライン指定順に適用し、同じ key は最後の指定を有効にする。既存実装は `Dictionary` により同一 key 後勝ちになっているため、標準仕様上も後勝ちを採用する。ただし異なる key の指定順を将来検査する可能性があるため、実装時は CLI 解析時の順序保持が必要かを確認する。

型変換は YAML 変換と同じ Config 型の property 型に対して行う。初期範囲は `string`、`bool`、整数、浮動小数、`enum`、nullable primitive を優先し、既存 YamlDotNet の型変換と矛盾しないことを採用条件にする。変換できない値は `CONFIG_LOAD_FAILED` とする。

入れ子キーは property chain として扱う。途中の property が `null` の場合に自動生成するかは設計書で明示が必要である。推奨は、parameterless constructor で生成できる class は自動生成し、生成できない場合は `CONFIG_LOAD_FAILED` とする。利用者が空 YAML から override だけで Config を作れるためである。

配列またはリスト値は T24 完了条件に含まれるため、最低限の標準仕様を入れる必要がある。推奨は `Items[0].Name=value` の index 指定を標準とし、既存要素の上書きだけを扱う。`List<T>` の不足 index 自動拡張や配列全体の置換は初期範囲外にする。配列 index が範囲外、負数、数値以外の場合は `CONFIG_LOAD_FAILED` とする。

存在しない property、書式不正、型変換失敗、配列 index 不正はすべて Config 読み込みの失敗として `CONFIG_LOAD_FAILED` にまとめる。既存の error code は Config path 不在を `CONFIG_NOT_FOUND`、読み込み不能、YAML 構文、型変換、検証失敗を `CONFIG_LOAD_FAILED` としているため、新規 code は不要である。

## 代替案

代替案 1 は、`--set` を YAML fragment として解釈し、`key=value` の value に JSON または YAML literal を許す方式である。配列や object 全体の置換を表現しやすい一方、CLI の quote 依存が強く、初期 E2E が OS shell 差の影響を受けやすい。

代替案 2 は、T24 で配列やリストを全体置換まで扱う方式である。表現力は高いが、区切り文字、escape、空要素、object list の表現を同時に決める必要がある。T24 の目的が標準仕様固定であることを考えると、index 指定だけを採用し、全体置換は後続 task に分ける方が安定する。

代替案 3 は、無効書式専用の error code を追加する方式である。CLI 利用者には原因が分かりやすいが、既存の Config error code 体系では読み込み、変換、検証の失敗を `CONFIG_LOAD_FAILED` にまとめている。T24 で code を増やす場合は設計書、`WorkflowErrorCodes`、契約検査を広く更新する必要がある。

代替案 4 は、`engine validate` でも Config override の型変換と検証を行う方式である。validate の価値は上がるが、現在の validate は Config path 存在確認までで、Step 実行前処理と同等の Config 読み込みは行っていない。T24 では run E2E を優先し、validate 拡張は別 task または追加範囲として設計判断するのがよい。

## 設計更新が必要な箇所

`doc/workflow_engine_spec.md` の 6.6 は、暫定文を T24 標準仕様に置き換える必要がある。ここに key 書式、入れ子 property、配列 index、型変換、複数指定後勝ち、無効時 error code、`EngineArguments.Settings` 保持をまとめる。

10.2 の実行順序は、`--config` YAML 変換、`--set` override 適用、Config 検証、`StepContext` 登録の順に更新する必要がある。現在は YAML 変換と登録だけが書かれている。

11.3 のエラー対象と error code は、override の書式不正、存在しない property、型変換失敗、配列 index 不正を Config 読み込み失敗に含めるよう更新する必要がある。新規 code を採用しない方針なら `CONFIG_LOAD_FAILED` の説明を拡張する。

17.4 の Config 検証は、`--set` を YAML 変換後かつ検証前に適用し、検証失敗時は Config を `StepContext` に登録しないことを明記する必要がある。

19.1 と 19.2 は T24 完了後の対象範囲を更新する必要がある。`--set` の標準 Config 反映は初期版または T24 対象として扱い、T23 では扱わない範囲からは外す。

21.3 は「T24 で決める」一覧から、決定済み標準仕様へ置き換える必要がある。複数 Config ファイル指定時の統合規則は T24 の調査対象から外れており、T24 では扱わない範囲として残すのがよい。

API 節の `EngineArguments` 説明は、`Settings` が override 適用後も生の CLI 指定値を保持することを補足するとよい。`WithConfig<TConfig>()` の単一 Config 型メタ情報はそのまま維持する。

## TDD 検査案

最初の検査は `StandardConfigLoadingContractTests` に CLI run E2E として追加する。`engine run main.csx --config appsettings.yaml --set Convert.ToUpper=false --set Save.Path=cli.txt` を実行し、Step が `StepContext.Get<AppConfig>()` から override 後の値を取得でき、`EngineArguments.Settings` からも同じ raw key と raw value を読めることを確認する。

次に、同一 key の複数指定は最後の値が Config に反映されることを検査する。既存の `EngineArguments.Settings` も最後の値を保持するため、互換性検査を兼ねられる。

入れ子 property は `Convert.Mode=fast` のような class property chain で検査する。途中 property が YAML に存在しない場合の自動生成を採用するなら、空 YAML と override だけで成功する検査を追加する。

型変換は `bool`、`int`、`enum`、nullable primitive を小さな Config 型で固定する。失敗系は `Port=not-a-number` などを `CONFIG_LOAD_FAILED` とし、Step marker file が作成されないことを確認する。

配列またはリストは `Servers[0].Host=localhost` のような既存要素の index override を検査する。範囲外 index、負数 index、数値でない index は `CONFIG_LOAD_FAILED` とする。

存在しない property は `Missing.Name=value` で `CONFIG_LOAD_FAILED` になる検査を追加する。大文字小文字の扱いは設計で固定した上で検査する。推奨は C# property 名に合わせた ordinal match である。

CLI parse の無効書式は `CliRunValidateTests` に残すか、`StandardConfigLoadingContractTests` に利用者目線 E2E として置く。`--set =value`、`--set key`、`--set ""` は exit code 2 の command error として既存 CLI parse 層で扱い、Config 型を見ない無効書式は `CONFIG_LOAD_FAILED` ではなく CLI parse error にするのがよい。

`engine validate` については、T24 では `--set` の保持と Config path 存在確認の既存契約を壊さない回帰検査に留める。validate で型変換まで行う場合は、別途 `CsxEntryLoader.Validate` の Config 読み込み検証を設計追加してから検査する。

## リスク

現在の CLI は同一 key 後勝ちの `Dictionary` で保持しているため、指定順そのものは失われる。T24 で「指定順に逐次適用」を厳密に扱うなら、`EngineArguments.Settings` は維持しつつ、実装内部に順序付き override list を追加する必要がある。

`StandardConfigLoader` は `IgnoreUnmatchedProperties()` を使っているため、YAML の未知 property は無視される。一方、CLI override の未知 property は利用者が明示指定した入力であり、無視せず `CONFIG_LOAD_FAILED` にする方が妥当である。この差を設計書に明記しないと挙動が混乱する。

`IValidatableObject` は設計書に記載されているが、現行 `StandardConfigLoader.Validate` は `Validator.TryValidateObject` のみで、object graph 全体の再帰検証も行っていない。T24 の override 後検証で入れ子 object や list 要素の検証範囲を広げる場合、T23 既存契約を超える可能性がある。

配列や list の自動拡張を採用すると、object 生成規則、default value、constructor 制約、null 許容の扱いが一気に増える。T24 では既存要素の index 上書きに限定する方が実装と検査の範囲を制御しやすい。

`--set` の value に `=` を含む文字列は、現行 parse では最初の `=` 以降を value として保持できる。標準仕様でもこの挙動を維持するか明記が必要である。

README は T30 の対象であるため、T24 では README 更新を必須にしない方が tracking と一致する。ただし設計書とテスト名、C# 文書注釈はユーザー標準に合わせ、関数名は英語、C# 文書注釈は日本語、関数と property の説明文必須を守る必要がある。

## 推奨方針

T24 は `engine run` の利用者目線 E2E を先頭に置き、標準 Config 読み込みへ `--set` override を統合する。適用タイミングは YAML 読み込み後、DataAnnotations と `IValidatableObject` 検証前、`StepContext` 登録前とする。

標準仕様は property path、既存配列または list 要素への index override、型変換、同一 key 後勝ち、Config 読み込み系の失敗を `CONFIG_LOAD_FAILED` に集約する方針で設計書へ反映する。`EngineArguments.Settings` は raw override の既存公開契約として維持する。

ブロッカーはない。ただし、配列または list の自動拡張と `engine validate` での override 型検証を T24 に含めるかは、実装前に親側で明示判断が必要である。
