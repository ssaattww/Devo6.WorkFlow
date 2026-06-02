# 設計書 Markdown lint 用語分類

## 対象

- `doc/workflow_engine_spec.md`
- 元 report: `reports/task-markdown-lint-terms-20260602152805.md`

## 分類方針

- 設計の公開概念、型名、Step 名、YAML key、設定名、外部技術名は英語を許可する。
- 一般的な説明語、状態語、英語でなくても意味が通る語は日本語へ直す。
- 文脈で判断が割れる語は保留にし、実際の出現箇所を見て whitelist か本文修正に分ける。
- 片仮名語は SudachiPy の正規形を参考に、表記統一候補を別に扱う。

## 英語許可候補

### 中核 DSL / 公開概念

| 語 | 回数 | 理由 |
| --- | ---: | --- |
| Step | 212 | DSL の中核概念。 |
| Config | 123 | DSL の中核概念。 |
| Workflow | 72 | DSL の中核概念。 |
| Flow | 67 | DSL の中核概念。 |
| Input | 35 | 型付き入出力の設計語。 |
| Message | 27 | message 型の設計語。 |
| Output | 23 | 型付き入出力の設計語。 |
| ID | 22 | 識別子の仕様語。 |
| Built-in | 17 | built-in step の分類名。 |
| Script | 16 | C# Script / Script Step の設計語。 |
| Control | 8 | Control Step の分類名。 |
| ExecutionTrace | 7 | 実行履歴の型名。 |
| Override | 5 | 実行時 override の設計語。 |
| Descriptor | 4 | 記述子型の設計語。 |
| Entry | 4 | entry flow などの設定語。 |
| StepContext | 1 | 公開 API 型名。 |
| StepContext.Logger | 1 | 公開 API の property 名。 |
| StepProvider | 1 | provider 型名。 |
| WorkflowResult | 1 | 結果型名。 |
| WorkflowStepException | 1 | 例外型名。 |
| Unit | 1 | 単位値の型名。 |

### Step / 制御機能名

| 語 | 回数 | 理由 |
| --- | ---: | --- |
| IfStep | 5 | 制御 Step 名。 |
| ForEachStep | 4 | 制御 Step 名。 |
| WhileStep | 4 | 制御 Step 名。 |
| ParallelStep | 2 | 制御 Step 名。 |
| SwitchStep | 2 | 制御 Step 名。 |
| TryCatchStep | 2 | 制御 Step 名。 |
| If | 2 | Control Step の機能名。 |
| ForEach | 3 | Control Step の機能名。 |
| While | 2 | Control Step の機能名。 |
| Switch | 1 | Control Step の機能名。 |
| Call | 3 | Flow 呼び出しの機能名。 |
| Run | 12 | 実行 snapshot / run config の設計語。 |
| Start | 1 | 開始状態や start step の設計語。 |

### YAML key / 設定名 / DSL 属性

| 語 | 回数 | 理由 |
| --- | ---: | --- |
| validation | 37 | YAML の validation 設定名として使う。 |
| binding | 34 | input/config binding の設計語。 |
| timeout | 13 | 設定名。 |
| retry | 12 | 設定名。 |
| merge | 12 | config merge の設計語。 |
| version | 12 | YAML key。 |
| path | 11 | YAML key / path 設定。 |
| null | 10 | YAML 値と validation の仕様語。 |
| trace | 9 | trace 設定名。 |
| key | 7 | binding key の仕様語。 |
| provider | 6 | config provider の仕様語。 |
| source | 5 | binding source の仕様語。 |
| root | 5 | root config / root path の仕様語。 |
| edge | 4 | graph edge の設計語。 |
| next | 3 | YAML key。 |
| schema | 3 | schema 設定。 |
| schemaVersion | 3 | YAML key。 |
| scriptOptions | 3 | YAML key。 |
| non-nullable | 3 | validation 仕様語。 |
| nullable | 3 | validation 仕様語。 |
| true | 3 | YAML boolean 値。 |
| false | 1 | YAML boolean 値。 |
| key-value | 1 | config override 形式の仕様語。 |
| nullability | 1 | validation 仕様語。 |

### C# / .NET / 外部技術名

| 語 | 回数 | 理由 |
| --- | ---: | --- |
| csx | 56 | C# script 拡張子。 |
| YAML | 48 | 設定形式名。 |
| assembly | 14 | .NET 実装語。 |
| NuGet | 13 | package 管理名。 |
| CLI | 11 | CLI 実行口の略語。 |
| API | 7 | 公開 API の略語。 |
| AssemblyLoadContext | 5 | .NET 型名。 |
| DI | 4 | dependency injection の略語。 |
| CancellationToken | 2 | .NET 型名。 |
| DAG | 2 | graph 構造の略語。 |
| NLog | 2 | logger provider 名。 |
| Roslyn | 2 | C# compiler 系技術名。 |
| Serilog | 2 | logger provider 名。 |
| CLR | 1 | .NET 実行基盤の略語。 |
| DataAnnotations | 1 | .NET validation 技術名。 |
| IValidatableObject | 1 | .NET interface 名。 |
| OpenTelemetry | 1 | telemetry 技術名。 |
| OS | 1 | 実行環境の略語。 |
| SMTP | 1 | protocol 名。 |
| UI | 1 | 表示系の略語。 |
| Web | 1 | 実行対象や出力先の分類名。 |

### ファイル名 / 例示 ID

| 語 | 回数 | 理由 |
| --- | ---: | --- |
| workflow-root | 3 | 例示ディレクトリ名。 |
| workflow.yaml | 2 | 標準ファイル名。 |
| accepted.csx | 1 | 例示ファイル名。 |
| order-messages.csx | 1 | 例示ファイル名。 |
| rejected.csx | 1 | 例示ファイル名。 |
| validate-order.csx | 1 | 例示ファイル名。 |
| Flow.InputType | 1 | 型 / property 参照。 |

## 日本語修正候補

### 一般説明語

| 語 | 回数 | 修正例 |
| --- | ---: | --- |
| file | 10 | ファイル |
| error | 9 | エラー、誤り |
| restore | 8 | 復元 |
| allowlist | 6 | 許可一覧 |
| inline | 6 | 行内、インライン参照 |
| validate | 5 | 検証する |
| bind | 4 | 結び付ける、binding する |
| canonical | 4 | 正規の、標準の |
| layer | 4 | 層 |
| lock | 4 | 固定する、ロックする |
| snapshot | 4 | 時点記録 |
| steps | 4 | Step 群 |
| class | 3 | class 定義、クラス |
| compile | 3 | compile する、コンパイルする |
| condition | 3 | 条件 |
| dependency | 3 | 依存関係 |
| found | 3 | 見つかった |
| join | 3 | 合流 |
| not | 3 | ではない |
| parameter | 3 | パラメータ |
| property | 3 | property、プロパティ |
| array | 2 | 配列 |
| compilation | 2 | コンパイル |
| directive | 2 | directive、指示行 |
| limits | 2 | 制限 |
| list | 2 | 一覧、list 値 |
| load | 2 | 読み込み |
| local | 2 | ローカル |
| object | 2 | object 値、オブジェクト |
| policy | 2 | 方針 |
| scan | 2 | 走査 |
| statement | 2 | 文 |
| top-level | 2 | 最上位 |
| unknown | 2 | 不明 |
| unload | 2 | 解放、unload |
| body | 1 | 本文 |
| capture | 1 | 取得 |
| content | 1 | 内容 |
| end | 1 | 終端 |
| executable | 1 | 実行可能 |
| execution | 1 | 実行 |
| floating | 1 | 浮動状態 |
| flows | 1 | Flow 群 |
| hash | 1 | hash 値 |
| link | 1 | link、リンク |
| loading | 1 | 読み込み |
| locked | 1 | 固定済み |
| messages | 1 | Message 群 |
| metadata | 1 | metadata、メタデータ |
| mismatch | 1 | 不一致 |
| namespace | 1 | namespace、名前空間 |
| parameterless | 1 | 引数なし |
| parse | 1 | parse、解析 |
| prerelease | 1 | prerelease、事前公開 |
| primary | 1 | 主、primary |
| public | 1 | public、公開 |
| redaction | 1 | 秘匿化 |
| scalar | 1 | scalar 値、単一値 |
| secrets | 1 | secret、機密値 |
| sequential | 1 | 逐次 |
| serialization | 1 | serialization、直列化 |
| symbolic | 1 | symbolic、記号的 |
| syntax | 1 | 構文 |
| types | 1 | 型群 |
| unrestricted | 1 | 無制限 |
| using | 1 | using 指示 |

### 値名として許可するか本文修正か確認

| 語 | 回数 | 判断 |
| --- | ---: | --- |
| cache | 12 | compile cache などの機能名なら許可。単なる保存先なら日本語。 |
| package | 10 | NuGet package なら許可。一般語なら package を日本語化。 |
| constructor | 5 | C# constructor の説明なら許可。一般説明ならコンストラクタ表記へ統一。 |
| bool | 1 | YAML / C# 型名なら許可。説明なら真偽値。 |
| record | 1 | C# record なら許可。一般語なら記録。 |
| Stream | 1 | 型名なら許可。一般語なら stream を日本語化。 |
| Type | 1 | 型名なら許可。一般語なら型。 |
| Abstractions | 1 | assembly 名なら許可。普通語なら抽象化。 |
| Options | 2 | options 型名なら許可。一般語なら設定。 |
| Log | 2 | API 名なら許可。一般語ならログ。 |
| Logging | 2 | namespace / logging 機能名なら許可。一般語ならログ出力。 |
| MVP | 2 | 開発段階の略語として許可するか確認。 |
| Scope | 1 | 型名 / API 名なら許可。一般語なら範囲。 |
| Variables | 1 | 設定名なら許可。一般語なら変数。 |
| Task | 1 | .NET 型名なら許可。一般語なら task / 作業単位を統一。 |

## 片仮名語の扱い

### whitelist 候補

| 語 | 回数 | 理由 |
| --- | ---: | --- |
| エンジン | 35 | workflow engine の日本語表記として頻出。 |
| コンパイル | 10 | compile の日本語表記として頻出。 |
| ログ | 12 | logging 関連の日本語表記。 |
| ディレクトリ | 8 | path 説明で頻出。 |
| ファイル | 8 | 文書 / path 説明で頻出。 |
| フォルダ | 7 | directory との表記統一が必要。 |
| パス | 7 | path の日本語表記。 |
| コード | 5 | code の日本語表記。 |
| プロパティ | 5 | property の日本語表記。 |
| エラー | 8 | error の日本語表記。 |
| エラーコード | 4 | error code の日本語表記。 |
| クラス | 4 | C# class の日本語表記。 |
| サポート | 4 | support の日本語表記。 |
| プロセス | 4 | process の日本語表記。 |

### 表記統一候補

| 現在の語 | SudachiPy 正規形 | 方針 |
| --- | --- | --- |
| ディレクトリ | ディレクトリー | repo では `ディレクトリ` を正とするなら whitelist に入れる。 |
| フォルダ | フォルダー | repo では `フォルダ` を正とするなら whitelist に入れる。 |
| プロパティ | プロパティー | repo では `プロパティ` を正とするなら whitelist に入れる。 |

## 次の反映方針案

1. まず「英語許可候補」の中核 DSL、Step / 制御機能名、YAML key、外部技術名を whitelist に追加する。
2. 「日本語修正候補」は本文修正を優先する。`file`、`found`、`not`、`body`、`content` などから直す。
3. 「値名として許可するか本文修正か確認」は出現箇所を見て、型名 / key なら whitelist、説明文なら日本語化する。
4. 片仮名語は `ディレクトリ` / `フォルダ` のような表記方針を決めてから whitelist または `prh.yml` に反映する。

## whitelist 反映結果

利用者確認を受けて、次を `tools/lint/markdown-whitelist.yaml` に反映した。

- 中核 DSL / 公開概念
- Step / 制御機能名
- YAML key / 設定名 / DSL 属性
- C# / .NET / 外部技術名
- ファイル名 / 例示 ID
- 片仮名語の whitelist 候補

反映しなかったもの:

- 日本語修正候補
- 値名として許可するか本文修正か確認が必要な語

反映後の whitelist 自己検査:

```bash
npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md:whitelist -- --stdin tools/lint/markdown-whitelist.yaml < tools/lint/markdown-whitelist.yaml
```

結果: 成功。

設計書の残件:

```text
・コンパイル, Abstractions, allowlist, array, bind, body, bool, cache,
camelCase, canonical, capture, CapturedValue, case-insensitive, class,
compilation, Condition, constructor, content, dependency, directive, end,
error, executable, execution, ExecutionNode, file, floating,
FlowExecutionResult, flows, found, hash, idempotent, immutable, inline,
layer, limits, link, list, load, loading, local, lock, locked, Log, Logging,
messages, metadata, mismatch, MVP, namespace, not, object, Options,
parameter, parameterless, parse, PascalCase, policy, prerelease, primary,
property, public, record, redaction, restore, scalar, scan, Scope,
ScriptCompiler, secrets, sequential, serialization, snake_case, snapshot,
statement, steps, Stream, symbolic, syntax, top-level, Type, types, unknown,
unload, unrestricted, using, validate, Variables
```

これらは本文修正または文脈確認の対象として残す。
