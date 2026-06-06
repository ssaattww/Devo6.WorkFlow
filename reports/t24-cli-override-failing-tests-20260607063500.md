# T24 CLI override 失敗検査レポート

## タスク

T24「CLI override の標準仕様」の実装前に、`--set` を標準 Config へ適用する契約を失敗検査として追加した。

## 対象範囲

- `tests/Devo6.WorkFlow.Tests/StandardConfigLoadingContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`

`src` 配下は編集していない。

## 追加した検査

- CLI run E2E で、`.csx` 内の `AppConfig` と `WithConfig<AppConfig>()` を使い、`--set Convert.ToUpper=false --set Save.Path=cli.txt` が YAML 値を上書きし、`EngineArguments.Settings` に raw key/value を保持することを固定した。
- 同一 key の `--set Title=first --set Title=second` は、Config と `EngineArguments.Settings` の両方で後勝ちになることを固定した。
- 空 YAML から `--set Convert.ToUpper=true` を適用し、null の中間 Config が自動生成されることを固定した。
- bool、int、enum、nullable primitive を 1 つの小さな Config にまとめ、override の型変換成功を固定した。
- YAML に存在する list と array の既存要素に対し、`Items[0].Name=cli-list` と `ArrayItems[0].Name=cli-array` が反映されることを固定した。
- 存在しない property、型変換失敗、list index 範囲外、負数 index、数値でない index は `CONFIG_LOAD_FAILED` となり、Step が実行されないことを固定した。
- Config 型を見ない無効書式として `--set =value` と `--set key` は exit code 2 の command error になることを固定した。
- `engine validate main.csx --config appsettings.yaml --set Port=not-a-number` は T24 では override 型検証を行わず、Config path 存在確認までで成功することを固定した。

## 期待する失敗

現行実装では `--set` が `EngineArguments.Settings` には保持されるが、標準 Config には反映されない。そのため、run 側の override E2E、型変換、list/array 上書き、失敗系の検査が赤になることを期待した。

CLI parse error 境界と validate 境界は、既存仕様と合うため成功を期待した。

## 実際の失敗

以下を実行し、10 件の赤を確認した。

```bash
dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter "FullyQualifiedName~StandardConfigLoadingContractTests|FullyQualifiedName~CliRunValidateTests"
```

結果は `Failed: 10, Passed: 17, Skipped: 0, Total: 27` だった。

主な赤は以下である。

- `CliRunSetOverridesStandardConfigAndPreservesRawSettings`: 期待 `False|cli.txt|false|cli.txt`、実際 `True|yaml.txt|false|cli.txt`
- `RepeatedSetUsesLastValueForConfigAndRawSettings`: 期待 `second|second`、実際 `yaml-value|second`
- `SetCreatesMissingNestedConfigObjects`: 期待 `True`、実際 `missing`
- `SetConvertsPrimitiveEnumAndNullableValues`: 期待 `True|8080|Fast|42`、実際 `False|1|Slow|`
- `SetOverridesExistingListAndArrayElements`: 期待 `cli-list|cli-array`、実際 `yaml-list|yaml-array`
- `InvalidSetApplicationFailsBeforeStepExecutionWithConfigLoadFailed`: 5 case すべてで期待は非 0 と `CONFIG_LOAD_FAILED`、実際は exit code 0

## 実装時の注意

- override は YAML 読み込み後、Config 検証前、`StepContext` 登録前に適用する必要がある。
- `EngineArguments.Settings` は raw 文字列保持の公開契約として維持する。
- property path 照合は ordinal 相当の完全一致にし、YAML の未知 property 無視とは別に、CLI override の存在しない property は `CONFIG_LOAD_FAILED` にする。
- 入れ子 property の途中が `null` の場合は、引数なしで生成できる class を生成し、生成できない場合は `CONFIG_LOAD_FAILED` にする。
- list/array は既存要素の index override だけを扱い、自動拡張や全体置換は実装しない。
- Config 型を見ない `--set` 無効書式は CLI parse error のまま exit code 2 とし、run 時の Config override 失敗と混ぜない。
- T24 の `validate` は Config path 存在確認までで、override 型検証を行わない。

## 検証結果

- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter "FullyQualifiedName~StandardConfigLoadingContractTests|FullyQualifiedName~CliRunValidateTests"`: 期待どおり赤。`Failed: 10, Passed: 17, Skipped: 0, Total: 27`
- `npm run lint:md`: 成功
- `npm run lint:md:terms`: 成功
- `git diff --check`: 成功
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t24-cli-override-failing-tests-20260607063500.md`: 成功

## リスク

現行実装では失敗系の `--set` が Config 適用前に Step まで到達するため、実装時は error code だけでなく Step 未実行も維持して修正する必要がある。
