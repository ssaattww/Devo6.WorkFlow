# 課題 #19 CLI 配列上書き調査

## 調査対象

- 課題 #19「yamlの配列をコマンドライン引数から渡す方法」
- `doc/workflow_engine_spec.md` の CLI override 契約
- `src/Devo6.WorkFlow.Cli/Program.cs` の引数解析
- `src/Devo6.WorkFlow.Engine/StandardConfigLoader.cs` の上書き処理
- `tests/Devo6.WorkFlow.Tests/StandardConfigLoadingContractTests.cs` の配列検査
- `README.md` の CLI 利用例

## 結論

指摘どおり、現行 CLI では配列またはリストを引数だけで新規構成できない。

現行契約が扱えるのは、YAML に既に存在する要素に対する `Items[0].Name=value` 形式の上書きだけである。空の配列またはリストへの追加、範囲外添字の自動拡張、配列またはリスト全体の置換は、設計書で明示的に対象外とされている。

推奨案は、配列またはリスト型のプロパティ全体を対象にした場合だけ、`--workflow-set` または `--wset` の値を YAML インライン配列として解釈することである。

例:

```powershell
engine run main.csx --workflow-config appsettings.yaml --wset 'Items=[alpha, beta]'
engine run main.csx --workflow-config appsettings.yaml --wset 'Items=["alpha beta", gamma]'
```

オブジェクト配列も対象に含める場合は、次の表現へ拡張できる。

```powershell
engine run main.csx --workflow-config appsettings.yaml --wset 'Items=[{Name: alpha}, {Name: beta}]'
```

## 現行挙動

CLI 解析は `--workflow-set key=value` を最初の `=` で分割し、`Dictionary<string, string>` に保持する。このため、値に `[`、`]`、`,` を含むこと自体は妨げていない。

制約は Config 変換側にある。`StandardConfigLoader.ConvertValue` が扱う型は `string`、`bool`、`int`、`long`、`double`、`decimal`、`enum`、nullable な基本型に限定される。配列またはリスト型のプロパティ全体を指定すると、未対応型として `CONFIG_LOAD_FAILED` になる。

添字指定は既存要素だけを操作する。要素数以上の添字は `CONFIG_LOAD_FAILED` になるため、次の指定は YAML 側に 1 件目がある場合だけ成功する。

```powershell
--wset 'Items[0].Name=cli-value'
```

対象検査 `SetOverridesExistingListAndArrayElements` を実行し、既存要素の上書きが成功することを確認した。

## 候補比較

| 候補 | 例 | 利点 | 問題 | 判断 |
| --- | --- | --- | --- | --- |
| YAML インライン配列 | `Items=[alpha, beta]` | YAML 設定との意味がそろい、空配列から全体を構成できる。基本型とオブジェクトの両方へ拡張できる | 空白や引用符を含む値はシェル側の引用が必要 | 推奨 |
| 添字の自動拡張 | `Items[0]=alpha`、`Items[1]=beta` | 現行のプロパティ path と連続性がある | 配列全体指定が冗長。欠番、既定値、配列再確保の規則が必要 | 補助候補 |
| 同一 key の繰り返し | `Items=alpha` を複数回 | 単純な文字列配列では読みやすい | 現行の同一 key 後勝ちおよび Dictionary 保持と衝突する。順序付き内部表現が必要 | 非推奨 |
| 区切り文字列 | `Items=alpha,beta` | 実装が小さい | 区切り文字、空要素、引用符、オブジェクト配列を一貫して表現しにくい | 非推奨 |
| 別ファイル参照 | `Items=@items.yaml` | 大きい配列を扱いやすい | workflow config YAML との役割が重複する | 将来候補 |

## 推奨契約

既存の基本型と既存要素添字上書きは互換維持する。

対象プロパティが配列またはリスト型の場合、値全体を YAML 断片として対象型へ変換し、現在の collection を置換する。`[]` は空 collection として扱う。変換不能、要素型不一致、対象型を生成できない場合は `CONFIG_LOAD_FAILED` とし、Step は実行しない。

初期範囲は、1 次元配列、`List<T>`、基本型要素、通常の Config object 要素とする。多次元配列、read-only collection、interface だけで具体型を決められない collection は対象外とする。

CLI の YAML 断片は利用者が明示した上書き値なので、オブジェクト要素の未知プロパティを無視しない。collection 全体を対象型へ変換するときは、未知プロパティを許可しない YamlDotNet の変換器を使い、未知プロパティがあれば `CONFIG_LOAD_FAILED` とする。既存 Config YAML 読み込みの `IgnoreUnmatchedProperties()` 契約は変更しない。

シェルによる引数分割を避けるため、利用者文書には引数全体を引用する例を PowerShell と bash 系で示す。空白を含まない単純値なら `Items=[alpha,beta]` も使用できる。

既存の `YamlDotNet` は文字列から型付き object graph を逆直列化できるため、現在の基本型変換へ独自の配列区切り規則を追加するより、collection 型だけ YAML 変換を再利用する方が自然である。

## .NET 設定方式との比較

.NET の標準 Configuration Binder も、数値 key segment を使って配列を object へ bind する。この方式は現行 `Items[0]` と近いが、欠番を詰めて bind する規則など、この CLI の「既存 object への厳密な上書き」と意味が異なる。そのため、Configuration Binder を全面導入せず、現行 path 契約を維持したまま YAML インライン配列の全体置換だけを追加する方が影響を限定できる。

参照:

- [ASP.NET Core configuration の配列 bind](https://learn.microsoft.com/en-ca/aspnet/core/fundamentals/configuration/?view=aspnetcore-8.0#bind-an-array)
- [YamlDotNet 公式リポジトリ](https://github.com/aaubry/YamlDotNet)

## 実装影響

- `doc/workflow_engine_spec.md`
  - 配列全体またはリスト全体の置換を対象外一覧から外す
  - YAML インライン配列、対応 collection 型、失敗規則を追加する
- `src/Devo6.WorkFlow.Engine/StandardConfigLoader.cs`
  - collection 型を対象型として YAML 逆直列化する変換を追加する
  - 既存の基本型変換と添字上書きを維持する
- `tests/Devo6.WorkFlow.Tests/StandardConfigLoadingContractTests.cs`
  - 空の `List<string>`、配列、オブジェクト配列の CLI 実行検査を先に追加する
  - 空配列、要素型不一致、無効 YAML、オブジェクト要素の未知プロパティ、既存添字上書きの回帰を確認する
- `README.md`
  - 現在残っている旧 `--config` / `--set` 例を、現行 `--workflow-config` / `--workflow-set` へ合わせる
  - PowerShell と bash 系の引用例を追加する

## 実行した確認

- GitHub 課題 #19 とコメントを確認した。本文とコメントはなかった
- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter "FullyQualifiedName~SetOverridesExistingListAndArrayElements"`
  - 成功 1 件
- `git diff --check`
  - 成功
- Markdown 検査
  - `npm` が PATH に存在せず、`node_modules` も未セットアップだった
  - `markdown-word-checker` の規則に従い、`lint:md` の npm 経路は `skip` とした
  - focused lint と full lint は、既存環境だけでは実行できないため `unsupported` とした
  - 残リスクは、今回追加した Markdown の用語検査を実行していないことである
  - 検査のための新規セットアップは不要であり、後続 task でも未セットアップ時は同じ分類と理由を記録する

## 確定事項

利用者が本報告の推奨方向を承認したため、初期実装は次の契約とする。

1. オブジェクト配列を含む collection 全体置換を実装する
2. CLI の YAML 断片にある未知プロパティは `CONFIG_LOAD_FAILED` とする
3. 添字指定の自動拡張は追加しない
4. engine config は collection property が導入された時点で同じ変換処理への統合を検討する

## 次 task

- T65 で採用契約を設計書へ反映する
- T66 で利用者目線の失敗検査を先に追加し、実装、回帰検査、利用者文書更新を行う
- T67 で最終点検、進捗同期、履歴登録、送信、取り込み依頼作成を行う
