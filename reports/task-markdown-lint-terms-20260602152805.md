# 設計書 Markdown lint 用語候補収集

## 対象

- `doc/workflow_engine_spec.md`

## 実行結果

実行コマンド:

```bash
node tools/lint/run-skill-script.js review-enforcer/scripts/check-markdown-whitelist.js --stdin doc/workflow_engine_spec.md --list-unknown < doc/workflow_engine_spec.md
```

結果:

- `tools/lint/markdown-whitelist.yaml` に未登録の英字語、片仮名語を抽出した。
- 候補はまだ whitelist に未適用。
- `tools/lint/markdown-targets.json` と `cspell.config.jsonc` の `doc/workflow_engine_spec.md` 除外を外して lint を通すには、下記候補の確認と反映が必要。

## SudachiPy 分類

設計書全体は SudachiPy の 1 回あたり入力上限を超えるため、`tools/lint/extract-sudachi-vocabulary-chunked.py` で chunk 分割して分類した。

実行コマンド:

```bash
.venv/bin/python tools/lint/extract-sudachi-vocabulary-chunked.py --files doc/workflow_engine_spec.md
```

分類件数:

| 種別 | 品詞 | 件数 |
| --- | --- | --- |
| english |  | 180 |
| japanese | 名詞,普通名詞,サ変可能 | 129 |
| japanese | 名詞,普通名詞,サ変形状詞可能 | 2 |
| japanese | 名詞,普通名詞,一般 | 127 |
| japanese | 名詞,普通名詞,副詞可能 | 14 |
| japanese | 名詞,普通名詞,形状詞可能 | 18 |
| katakana | 名詞,固有名詞,一般 | 2 |
| katakana | 名詞,普通名詞,サ変可能 | 15 |
| katakana | 名詞,普通名詞,一般 | 49 |
| katakana | 名詞,普通名詞,形状詞可能 | 1 |

頻出語上位:

| 回数 | 種別 | 表層 | 正規形 | 読み | 品詞 |
| --- | --- | --- | --- | --- | --- |
| 212 | english | Step | step |  |  |
| 123 | english | Config | config |  |  |
| 72 | english | Workflow | workflow |  |  |
| 67 | english | Flow | flow |  |  |
| 65 | japanese | 実行 | 実行 | ジッコウ | 名詞,普通名詞,サ変可能 |
| 59 | japanese | 実装 | 実装 | ジッソウ | 名詞,普通名詞,サ変可能 |
| 58 | japanese | 以下 | 以下 | イカ | 名詞,普通名詞,一般 |
| 56 | english | csx | csx |  |  |
| 49 | japanese | 定義 | 定義 | テイギ | 名詞,普通名詞,サ変可能 |
| 48 | english | YAML | yaml |  |  |
| 46 | japanese | 場合 | 場合 | バアイ | 名詞,普通名詞,副詞可能 |
| 40 | japanese | 参照 | 参照 | サンショウ | 名詞,普通名詞,サ変可能 |
| 38 | japanese | 初期 | 初期 | ショキ | 名詞,普通名詞,一般 |
| 37 | english | validation | validation |  |  |
| 37 | katakana | エンジン | エンジン | エンジン | 名詞,普通名詞,一般 |
| 35 | english | Input | input |  |  |
| 34 | english | binding | binding |  |  |
| 28 | japanese | 解決 | 解決 | カイケツ | 名詞,普通名詞,サ変可能 |
| 27 | english | Message | message |  |  |
| 23 | english | Output | output |  |  |
| 22 | english | ID | id |  |  |
| 21 | japanese | 任意 | 任意 | ニンイ | 名詞,普通名詞,一般 |
| 21 | japanese | 失敗 | 失敗 | シッパイ | 名詞,普通名詞,サ変可能 |
| 21 | japanese | 検証 | 検証 | ケンショウ | 名詞,普通名詞,サ変可能 |
| 21 | japanese | 許可 | 許可 | キョカ | 名詞,普通名詞,サ変可能 |
| 20 | japanese | 必須 | 必須 | ヒッス | 名詞,普通名詞,形状詞可能 |
| 20 | japanese | 指定 | 指定 | シテイ | 名詞,普通名詞,サ変可能 |
| 18 | japanese | 対応 | 対応 | タイオウ | 名詞,普通名詞,サ変可能 |
| 18 | japanese | 規則 | 規則 | キソク | 名詞,普通名詞,一般 |
| 18 | japanese | 設定 | 設定 | セッテイ | 名詞,普通名詞,サ変可能 |
| 17 | english | Built-in | built-in |  |  |
| 17 | japanese | 仕様 | 仕様 | シヨウ | 名詞,普通名詞,一般 |
| 17 | japanese | 明示 | 明示 | メイジ | 名詞,普通名詞,サ変可能 |
| 16 | english | Script | script |  |  |
| 16 | japanese | 対象 | 対象 | タイショウ | 名詞,普通名詞,一般 |
| 16 | japanese | 推奨 | 推奨 | スイショウ | 名詞,普通名詞,サ変可能 |
| 15 | japanese | 利用 | 利用 | リヨウ | 名詞,普通名詞,サ変可能 |
| 15 | katakana | コンパイル | コンパイル | コンパイル | 名詞,普通名詞,サ変可能 |

分類からの判断:

- `Step`、`Config`、`Workflow`、`Flow`、`Message`、`Input`、`Output` などは設計語として whitelist 候補。
- `validation`、`binding`、`Built-in`、`assembly`、`cache`、`merge`、`schema` などは DSL / 実装仕様語として whitelist 候補。
- `実行`、`実装`、`定義` などの一般的な日本語名詞をすべて whitelist に入れると辞書が粗くなるため、文章側を直すか、設計語として残すか確認が必要。
- `以下`、`場合`、`通常` などの一般語は whitelist に入れるより、検査側の日本語語彙ルールを緩める候補。
- `ディレクトリ` は SudachiPy の正規形が `ディレクトリー` になるため、表記統一するなら `prh.yml` 候補。

## exact entry 候補

分類版は `reports/task-markdown-lint-term-classification-20260602154500.md` に記録した。

```yaml
  - term: ・コンパイル
    description: 設計書で使う語。
  - term: Abstractions
    description: 設計書で使う語。
  - term: accepted
    description: 設計書で使う語。
  - term: allowlist
    description: 設計書で使う語。
  - term: array
    description: 設計書で使う語。
  - term: assembly
    description: 設計書で使う語。
  - term: AssemblyLoadContext
    description: 設計書で使う語。
  - term: bind
    description: 設計書で使う語。
  - term: binding
    description: 設計書で使う語。
  - term: body
    description: 設計書で使う語。
  - term: bool
    description: 設計書で使う語。
  - term: Built-in
    description: 設計書で使う語。
  - term: cache
    description: 設計書で使う語。
  - term: Call
    description: 設計書で使う語。
  - term: camelCase
    description: 設計書で使う語。
  - term: CancellationToken
    description: 設計書で使う語。
  - term: canonical
    description: 設計書で使う語。
  - term: capture
    description: 設計書で使う語。
  - term: CapturedValue
    description: 設計書で使う語。
  - term: case-insensitive
    description: 設計書で使う語。
  - term: class
    description: 設計書で使う語。
  - term: CLR
    description: 設計書で使う語。
  - term: compilation
    description: 設計書で使う語。
  - term: Condition
    description: 設計書で使う語。
  - term: constructor
    description: 設計書で使う語。
  - term: content
    description: 設計書で使う語。
  - term: Control
    description: 設計書で使う語。
  - term: DAG
    description: 設計書で使う語。
  - term: DataAnnotations
    description: 設計書で使う語。
  - term: dependency
    description: 設計書で使う語。
  - term: Descriptor
    description: 設計書で使う語。
  - term: DI
    description: 設計書で使う語。
  - term: directive
    description: 設計書で使う語。
  - term: edge
    description: 設計書で使う語。
  - term: end
    description: 設計書で使う語。
  - term: Entry
    description: 設計書で使う語。
  - term: error
    description: 設計書で使う語。
  - term: executable
    description: 設計書で使う語。
  - term: execution
    description: 設計書で使う語。
  - term: ExecutionNode
    description: 設計書で使う語。
  - term: false
    description: 設計書で使う語。
  - term: file
    description: 設計書で使う語。
  - term: floating
    description: 設計書で使う語。
  - term: FlowExecutionResult
    description: 設計書で使う語。
  - term: flows
    description: 設計書で使う語。
  - term: ForEach
    description: 設計書で使う語。
  - term: ForEachStep
    description: 設計書で使う語。
  - term: found
    description: 設計書で使う語。
  - term: hash
    description: 設計書で使う語。
  - term: ID
    description: 設計書で使う語。
  - term: idempotent
    description: 設計書で使う語。
  - term: If
    description: 設計書で使う語。
  - term: IfStep
    description: 設計書で使う語。
  - term: immutable
    description: 設計書で使う語。
  - term: inline
    description: 設計書で使う語。
  - term: InputType
    description: 設計書で使う語。
  - term: IValidatableObject
    description: 設計書で使う語。
  - term: key
    description: 設計書で使う語。
  - term: key-value
    description: 設計書で使う語。
  - term: layer
    description: 設計書で使う語。
  - term: limits
    description: 設計書で使う語。
  - term: link
    description: 設計書で使う語。
  - term: list
    description: 設計書で使う語。
  - term: load
    description: 設計書で使う語。
  - term: loading
    description: 設計書で使う語。
  - term: local
    description: 設計書で使う語。
  - term: lock
    description: 設計書で使う語。
  - term: locked
    description: 設計書で使う語。
  - term: Log
    description: 設計書で使う語。
  - term: Logging
    description: 設計書で使う語。
  - term: merge
    description: 設計書で使う語。
  - term: messages
    description: 設計書で使う語。
  - term: metadata
    description: 設計書で使う語。
  - term: mismatch
    description: 設計書で使う語。
  - term: MVP
    description: 設計書で使う語。
  - term: namespace
    description: 設計書で使う語。
  - term: next
    description: 設計書で使う語。
  - term: NLog
    description: 設計書で使う語。
  - term: non-nullable
    description: 設計書で使う語。
  - term: not
    description: 設計書で使う語。
  - term: null
    description: 設計書で使う語。
  - term: nullability
    description: 設計書で使う語。
  - term: nullable
    description: 設計書で使う語。
  - term: object
    description: 設計書で使う語。
  - term: OpenTelemetry
    description: 設計書で使う語。
  - term: Options
    description: 設計書で使う語。
  - term: order-messages
    description: 設計書で使う語。
  - term: OS
    description: 設計書で使う語。
  - term: Override
    description: 設計書で使う語。
  - term: ParallelStep
    description: 設計書で使う語。
  - term: parameter
    description: 設計書で使う語。
  - term: parameterless
    description: 設計書で使う語。
  - term: parse
    description: 設計書で使う語。
  - term: PascalCase
    description: 設計書で使う語。
  - term: path
    description: 設計書で使う語。
  - term: policy
    description: 設計書で使う語。
  - term: prerelease
    description: 設計書で使う語。
  - term: primary
    description: 設計書で使う語。
  - term: property
    description: 設計書で使う語。
  - term: provider
    description: 設計書で使う語。
  - term: public
    description: 設計書で使う語。
  - term: record
    description: 設計書で使う語。
  - term: redaction
    description: 設計書で使う語。
  - term: rejected
    description: 設計書で使う語。
  - term: restore
    description: 設計書で使う語。
  - term: root
    description: 設計書で使う語。
  - term: Roslyn
    description: 設計書で使う語。
  - term: Run
    description: 設計書で使う語。
  - term: scalar
    description: 設計書で使う語。
  - term: scan
    description: 設計書で使う語。
  - term: schema
    description: 設計書で使う語。
  - term: schemaVersion
    description: 設計書で使う語。
  - term: Scope
    description: 設計書で使う語。
  - term: ScriptCompiler
    description: 設計書で使う語。
  - term: scriptOptions
    description: 設計書で使う語。
  - term: secrets
    description: 設計書で使う語。
  - term: sequential
    description: 設計書で使う語。
  - term: serialization
    description: 設計書で使う語。
  - term: Serilog
    description: 設計書で使う語。
  - term: SMTP
    description: 設計書で使う語。
  - term: snake_case
    description: 設計書で使う語。
  - term: snapshot
    description: 設計書で使う語。
  - term: source
    description: 設計書で使う語。
  - term: Start
    description: 設計書で使う語。
  - term: statement
    description: 設計書で使う語。
  - term: StepContext
    description: 設計書で使う語。
  - term: StepContext.Logger
    description: 設計書で使う語。
  - term: StepProvider
    description: 設計書で使う語。
  - term: steps
    description: 設計書で使う語。
  - term: Stream
    description: 設計書で使う語。
  - term: Switch
    description: 設計書で使う語。
  - term: SwitchStep
    description: 設計書で使う語。
  - term: symbolic
    description: 設計書で使う語。
  - term: syntax
    description: 設計書で使う語。
  - term: top-level
    description: 設計書で使う語。
  - term: trace
    description: 設計書で使う語。
  - term: true
    description: 設計書で使う語。
  - term: TryCatchStep
    description: 設計書で使う語。
  - term: Type
    description: 設計書で使う語。
  - term: types
    description: 設計書で使う語。
  - term: UI
    description: 設計書で使う語。
  - term: Unit
    description: 設計書で使う語。
  - term: unknown
    description: 設計書で使う語。
  - term: unload
    description: 設計書で使う語。
  - term: unrestricted
    description: 設計書で使う語。
  - term: using
    description: 設計書で使う語。
  - term: validate
    description: 設計書で使う語。
  - term: validate-order
    description: 設計書で使う語。
  - term: validation
    description: 設計書で使う語。
  - term: Variables
    description: 設計書で使う語。
  - term: version
    description: 設計書で使う語。
  - term: Web
    description: 設計書で使う語。
  - term: While
    description: 設計書で使う語。
  - term: WhileStep
    description: 設計書で使う語。
  - term: WorkflowResult
    description: 設計書で使う語。
  - term: WorkflowStepException
    description: 設計書で使う語。
  - term: アクセス
    description: 設計書で使う語。
  - term: アップロード
    description: 設計書で使う語。
  - term: インスタンス
    description: 設計書で使う語。
  - term: エラー
    description: 設計書で使う語。
  - term: エラーコード
    description: 設計書で使う語。
  - term: エンジン
    description: 設計書で使う語。
  - term: オブジェクト
    description: 設計書で使う語。
  - term: オプション
    description: 設計書で使う語。
  - term: キャンセル
    description: 設計書で使う語。
  - term: キュー
    description: 設計書で使う語。
  - term: クラス
    description: 設計書で使う語。
  - term: コード
    description: 設計書で使う語。
  - term: コピー
    description: 設計書で使う語。
  - term: コレクション
    description: 設計書で使う語。
  - term: コンテナ
    description: 設計書で使う語。
  - term: コンパイル
    description: 設計書で使う語。
  - term: コンパイルキャッシュ
    description: 設計書で使う語。
  - term: サポート
    description: 設計書で使う語。
  - term: サンドボックス
    description: 設計書で使う語。
  - term: サンプル
    description: 設計書で使う語。
  - term: ジェネリック
    description: 設計書で使う語。
  - term: シリアライズ
    description: 設計書で使う語。
  - term: スケジューラ
    description: 設計書で使う語。
  - term: スコープ
    description: 設計書で使う語。
  - term: セキュリティ
    description: 設計書で使う語。
  - term: タイミング
    description: 設計書で使う語。
  - term: チェック
    description: 設計書で使う語。
  - term: ツール
    description: 設計書で使う語。
  - term: ツールパス
    description: 設計書で使う語。
  - term: データ
    description: 設計書で使う語。
  - term: ディレクトリ
    description: 設計書で使う語。
  - term: デバッグ
    description: 設計書で使う語。
  - term: デフォルト
    description: 設計書で使う語。
  - term: トップレベル
    description: 設計書で使う語。
  - term: トレース
    description: 設計書で使う語。
  - term: ネスト
    description: 設計書で使う語。
  - term: ネットワーク
    description: 設計書で使う語。
  - term: ネットワークアクセス
    description: 設計書で使う語。
  - term: バージョン
    description: 設計書で使う語。
  - term: パス
    description: 設計書で使う語。
  - term: パラメータ
    description: 設計書で使う語。
  - term: ファイルパス
    description: 設計書で使う語。
  - term: ファイルハンドル
    description: 設計書で使う語。
  - term: フェーズ
    description: 設計書で使う語。
  - term: フォーマット
    description: 設計書で使う語。
  - term: フォルダ
    description: 設計書で使う語。
  - term: プロジェクト
    description: 設計書で使う語。
  - term: プロセス
    description: 設計書で使う語。
  - term: プロパティ
    description: 設計書で使う語。
  - term: ホスト
    description: 設計書で使う語。
  - term: メソッド
    description: 設計書で使う語。
  - term: メタ
    description: 設計書で使う語。
  - term: メタデータ
    description: 設計書で使う語。
  - term: メモリ
    description: 設計書で使う語。
  - term: モデル
    description: 設計書で使う語。
  - term: ユーザー
    description: 設計書で使う語。
  - term: ライブラリ
    description: 設計書で使う語。
  - term: ラップ
    description: 設計書で使う語。
  - term: リスト
    description: 設計書で使う語。
  - term: リソース
    description: 設計書で使う語。
  - term: リテラル
    description: 設計書で使う語。
  - term: リフレクション
    description: 設計書で使う語。
  - term: ローカル
    description: 設計書で使う語。
  - term: ロード
    description: 設計書で使う語。
  - term: ログ
    description: 設計書で使う語。
  - term: ログイベント
    description: 設計書で使う語。
  - term: ワークフロー
    description: 設計書で使う語。
  - term: ワークフローエンジン
    description: 設計書で使う語。
```
