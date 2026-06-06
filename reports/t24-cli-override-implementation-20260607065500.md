# T24 CLI override 実装レポート

## 概要

T24「CLI override の標準仕様」について、標準 Config 読み込み時に `--set` を適用する実装を追加した。

実装は `StandardConfigLoader` に閉じ、`CsxEntryLoader` は `EngineArguments.Settings` を渡すだけにした。CLI parse 層と `validate` の既存境界は変更していない。

## TDD 赤から緑

実装前に指定フィルタのテストを実行し、`StandardConfigLoadingContractTests` の T24 追加ケースが失敗することを確認した。

- 正常系の override は標準 Config に反映されず、YAML 値のままだった。
- 存在しない property、型変換失敗、list 添字不正は Step 実行まで進んでいた。
- `validate` と CLI parse 層の既存境界は、実装対象外として維持する前提で確認した。

実装後、同じフィルタのテストは 27 件すべて成功した。

## 変更内容

- `src/Devo6.WorkFlow.Engine/StandardConfigLoader.cs`
  - YAML deserialize 後、検証前に `EngineArguments.Settings` の raw override を標準 Config に適用する処理を追加した。
  - public instance property 名を `StringComparison.Ordinal` 相当で照合する property path 解決を追加した。
  - `Items[0].Name=value` 形式の list と array の既存要素 override を追加した。
  - 入れ子 property が `null` の場合、引数なし constructor を持つ class を自動生成する処理を追加した。
  - `string`、`bool`、`int`、`long`、`double`、`decimal`、enum、nullable の変換を追加した。
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - 標準 Config 読み込み時に `options.EngineArguments.Settings` を `StandardConfigLoader.Load` へ渡すよう変更した。

## 設計との対応

- override 適用順は、YAML 読み込み、`--set` 適用、DataAnnotations と `IValidatableObject` 検証、`StepContext` 登録の順にした。
- `EngineArguments.Settings` は変更せず、CLI 由来の raw 文字列保持を維持した。
- 同一 key の後勝ちは既存 `Dictionary` 契約のまま維持した。
- list と array は既存要素への index override のみ対応し、自動拡張と全体置換は実装していない。

## error 境界

- Config 型を見ない `--set` 無効書式は CLI parse 層のままで、exit code 2 を維持した。
- run 時の property 不在、型変換失敗、添字不正、生成不能な中間 object は例外として `CsxEntryLoader` に返し、既存の `CONFIG_LOAD_FAILED` へ集約した。
- override 適用失敗時は標準 Config を `StepContext` に登録せず、Step も実行しない。

## validate 境界

`engine validate` は T24 範囲では Config path 存在確認までを維持し、override の型検証は行わない。`Program.cs` と `CsxEntryLoader.Validate` は変更していない。

## 検証結果

- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter "FullyQualifiedName~StandardConfigLoadingContractTests|FullyQualifiedName~CliRunValidateTests"`: 成功。27 件成功。
- `dotnet test Devo6.WorkFlow.sln`: 成功。98 件成功。
- `npm run lint:md`: 成功。
- `npm run lint:md:terms`: 成功。SudachiPy term variants は none。
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t24-cli-override-implementation-20260607065500.md`: 成功。
- `git diff --check`: 成功。

## 残リスク

- T24 範囲外として、list と array の自動拡張、collection 全体置換、`engine validate` での override 型検証は未実装。
- 型変換は T24 の最低要件に限定しており、任意型や complex object の direct override は未対応。
- 実装担当 worker は reviewer ではないため、独立 review は後続 reviewer の対象として残る。
