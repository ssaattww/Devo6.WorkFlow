# Markdown whitelist 監査

## 対象

- `tools/lint/markdown-whitelist.yaml`
- 突き合わせ元: `reports/task-markdown-lint-term-classification-20260602154500.md`

## 前提

- 分類レポートの「英語許可候補」と「片仮名語の whitelist 候補」は whitelist 反映対象。
- 分類レポートの「日本語修正候補」は本文修正対象であり、原則 whitelist 反映対象ではない。
- 分類レポートの「値名として許可するか本文修正か確認」は出現箇所確認後に扱う対象であり、無条件の whitelist 反映対象ではない。

## 即時修正済み

以下は分類レポートの whitelist 対象ではないため削除済み。

| 語 | 理由 |
| --- | --- |
| `join` | 分類レポートでは「合流」への日本語修正候補。 |
| `parallel` | 分類レポートに裸の語として存在しない。`ParallelStep` のみ制御 Step 名として存在する。 |

本文側も以下のように修正済み。

| 変更前 | 変更後 |
| --- | --- |
| `join` | 合流 |
| `parallel edge` | 並列接続 |
| `Parallelなど` | `ParallelStep` など |

## 日本語修正候補なのに whitelist にある語

分類レポート上は本文修正候補。現行 whitelist では許可されているため、削除または識別子限定の再定義が必要。

| 行 | 語 | 分類レポートの修正例 |
| ---: | --- | --- |
| 60 | `compile` | コンパイルする |
| 226 | `bind` | 結び付ける、binding する |
| 228 | `directive` | directive、指示行 |
| 230 | `end` | 終端 |
| 232 | `messages` | Message 群 |
| 234 | `steps` | Step 群 |
| 250 | `body` | 本文 |
| 262 | `object` | object 値、オブジェクト |
| 266 | `sequential` | 逐次 |
| 268 | `statement` | 文 |
| 274 | `using` | using 指示 |
| 276 | `validate` | 検証する |

## 条件付き確認のまま whitelist にある語

分類レポート上は「型名 / key なら許可、説明文なら日本語化」などの確認対象。現行 whitelist では無条件に許可されている。

| 行 | 語 | 分類レポートの判断 |
| ---: | --- | --- |
| 64 | `package` | NuGet package なら許可。一般語なら日本語化。 |
| 236 | `Abstractions` | assembly 名なら許可。普通語なら抽象化。 |
| 252 | `bool` | YAML / C# 型名なら許可。説明なら真偽値。 |
| 256 | `Log` | API 名なら許可。一般語ならログ。 |
| 258 | `Logging` | namespace / logging 機能名なら許可。一般語ならログ出力。 |
| 260 | `MVP` | 開発段階の略語として許可するか確認。 |
| 264 | `Options` | options 型名なら許可。一般語なら設定。 |
| 270 | `Stream` | 型名なら許可。一般語なら日本語化。 |
| 272 | `Type` | 型名なら許可。一般語なら型。 |
| 278 | `Variables` | 設定名なら許可。一般語なら変数。 |

## 分類レポート外で要確認

現行 whitelist のうち、分類レポートの表に完全一致しない語は116件。主な要確認語は以下。

| 行 | 語 | 確認観点 |
| ---: | --- | --- |
| 27 | `workflow engine` | 分類レポートでは `Workflow` は許可。裸の語句は未分類。 |
| 36 | `C#` | 言語名として妥当だが分類表には未出。 |
| 40 | `C# script` | 形式名として妥当だが分類表には未出。 |
| 42 | `script` | 一般語として残しすぎる可能性あり。 |
| 58 | `Dotnet.Script.Core` | 製品名として妥当だが分類表には未出。 |
| 74 | `logger provider` | 分類表には logger provider 名があり、語句そのものは未分類。 |
| 80 | `fan-out` | 本文で設計語として使うが分類表には未出。 |
| 82 | `edge condition` | 現在本文は「エッジ条件」へ修正済み。削除候補。 |
| 238 | `CapturedValue` | 型名として妥当だが分類表には未出。 |
| 240 | `Condition` | 型名または設定名として妥当か出現箇所確認が必要。 |
| 242 | `ExecutionNode` | 型名として妥当だが分類表には未出。 |
| 244 | `FlowExecutionResult` | 型名として妥当だが分類表には未出。 |
| 246 | `ScriptCompiler` | 設計語として妥当か出現箇所確認が必要。 |
| 248 | `ScriptCompiler抽象` | 章題用の合成語。許可語として広すぎる可能性あり。 |
| 254 | `Layer` | 本文で「層」へ修正済みなら削除候補。 |

## 次の削減方針

1. `日本語修正候補なのに whitelist にある語` は、識別子として残す必要がある箇所だけ確認し、不要なら削除する。
2. `条件付き確認のまま whitelist にある語` は、出現箇所を `doc/workflow_engine_spec.md` で確認し、型名や key だけに限定できないものは本文修正へ回す。
3. `分類レポート外で要確認` は、repo/tooling語、型名、後追いで追加した片仮名語に分ける。設計書本文に出ない語は whitelist から削る。
4. whitelist を削った後に `npm run lint:md` で出た語だけを、本文修正か再許可かに戻す。

## 実行コマンド

```bash
python3 - <<'PY'
# 分類レポートの表と whitelist の term / aliases を突き合わせ
PY
```

結果概要:

```text
whitelist entries including aliases: 238
in English allowed: 86
in Japanese fix candidates: 12
in conditional candidates: 10
in katakana candidates: 14
outside classification report: 116
```
