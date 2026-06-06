# T24 CLI override 設計更新

## 対象

- `doc/workflow_engine_spec.md`
- 入力 report: `reports/t24-cli-override-design-impact-20260607054000.md`

## 更新内容

`--set` を標準 Config に対する property path override として定義した。

主な更新点は以下である。

- `--set key=value` の value は最初の `=` 以降をそのまま保持する
- key は C# public instance property 名を `.` でたどり、property 名は ordinal 一致とする
- YAML 読み込み後、Config 検証前、`StepContext` 登録前に override を適用する
- 入れ子 property の途中が `null` の場合、parameterless constructor で生成できる class は自動生成する
- 配列または list は `Items[0].Name=value` のような既存要素への index override だけを扱う
- 型変換は対象 property 型に対して行い、初期範囲を `string`、`bool`、`int`、`long`、`double`、`decimal`、`enum`、nullable primitive とする
- 複数 override は同一 key 後勝ちとする
- `EngineArguments.Settings` は raw 文字列保持の公開契約として維持する
- Config 適用失敗は `CONFIG_LOAD_FAILED`、CLI parse 層の書式不正は command error exit code 2 として分ける
- `engine validate` は T24 では Config path 存在確認までを維持する

## 採用した方針

入力 report の推奨どおり、CLI override を標準 Config の検証済み値へ反映する方針を採用した。

`EngineArguments.Settings` は既存 API 互換のため元の指定保持として残し、Step から CLI 指定値そのものも参照できる設計にした。

## 対象外

以下は T24 の対象外として仕様書に残した。

- 配列全体または list 全体の置換
- 配列または list の自動拡張
- `engine validate` での override 型検証
- 複数 Config ファイル指定時の統合規則
- README 更新

## 検証結果

以下を実行し、すべて成功した。

- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t24-cli-override-design-update-20260607055500.md`

## 残リスク

実装時に、CLI 解析後の `Dictionary` 契約と指定順保持の扱いを確認する必要がある。同一 key は後勝ちで足りるが、異なる key 間の順序依存を将来扱う場合は内部表現の追加が必要になる可能性がある。

YAML の未知 property は既存 loader が無視する一方、CLI override の未知 property は明示入力として `CONFIG_LOAD_FAILED` にする。この差は実装とテストで固定する必要がある。
