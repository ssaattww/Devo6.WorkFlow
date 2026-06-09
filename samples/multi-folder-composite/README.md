# 複数フォルダ CompositeStep サンプル

このサンプルは、複数フォルダに分けた Step をネストした `CompositeStep` から実行します。workflow の値は `appsettings.yaml` と各 Step の `appsettings.yaml`、engine の値は `engine.yaml` に分けています。

## 通常実行

```bash
engine run samples/multi-folder-composite/main.csx --workflow-config appsettings.yaml --engine-config engine.yaml
```

実行すると `output/result.txt` に結果文書を保存し、`logs/260609-120000_Main.log` のような実行記録を作成します。`engine.yaml` は `Logging.Console.Enabled: true` なので、同じ実行記録は標準出力にも出ます。実行記録名の形式は `engine.yaml` の `{Timestamp:yyMMdd-HHmmss}_{RootStepName}.log` です。このサンプルの root `CompositeStep` 名は `Main` なので、`RootStepName` は `Main` になります。

## workflow 値の上書き

```bash
engine run samples/multi-folder-composite/main.csx --workflow-config appsettings.yaml --engine-config engine.yaml --wset Pipeline.Report.Heading="Override report"
```

`--wset` は workflow config を上書きします。この例では `output/result.txt` の見出しだけを変えます。

## engine 値の上書き

```bash
engine run samples/multi-folder-composite/main.csx --workflow-config appsettings.yaml --engine-config engine.yaml --eset Logging.File.Directory=override-logs
```

`--eset` は engine config を上書きします。この例ではログの出力先を `logs/` から `override-logs/` に変えます。ファイル名を固定したい場合は、次のように上書きします。

```bash
engine run samples/multi-folder-composite/main.csx --workflow-config appsettings.yaml --engine-config engine.yaml --eset Logging.File.NameFormat=sample_{RootStepName}.log
```
