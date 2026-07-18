# 複数フォルダ CompositeStep サンプル

このサンプルは、複数フォルダに分けた Step をネストした `CompositeStep` から実行します。workflow の値は `appsettings.yaml` と各 Step の `appsettings.yaml`、engine の値は `engine.yaml` に分けています。

## 通常実行

```bash
engine run samples/multi-folder-composite/main.csx --workflow-config appsettings.yaml --engine-config engine.yaml
```

絶対 path の Entry も同じ規則で実行できます。

```bash
ENTRY=$(pwd)/samples/multi-folder-composite/main.csx
engine run "$ENTRY" --workflow-config appsettings.yaml --engine-config engine.yaml
```

実行すると `output/result.txt` に結果文書を保存し、`logs/260609-120000_Main.log` のような実行記録を作成します。`engine.yaml` は `Logging.Console.Enabled: true` なので、同じ実行記録は標準出力にも出ます。実行記録名の形式は `engine.yaml` の `{Timestamp:yyMMdd-HHmmss}_{RootStepName}.log` です。このサンプルの root `CompositeStep` 名は `Main` なので、`RootStepName` は `Main` になります。

標準出力ログでは `Entry started`、`Step started for attempt 1`、`Loading source text from input/source.txt`、`Parsing YAML front matter with delimiter ---`、`Building report with heading Composite sample report`、`Saving report text to output/result.txt`、`Entry succeeded` の順に処理状況を追えます。これを進捗表示として見れば、Entry 開始、各 Step の処理内容、成功、Entry 完了を確認できます。

内側の `TextPipeline` は、本文分析後に `RunIf`、`TapIf`、`If`、`Switch` を使います。既定入力では `guide` 文書のメタデータを `TapIf` で検査し、長い文書の `If` 分岐と `guide` の `Switch` 分岐を通ってから結果文書を作ります。入力の `tags:` に `summary` を追加すると、`RunIf` が `tags:` 要約を本文末尾へ追加します。`category: summary` ではなく `tags:` 条件です。

root の `appsettings.yaml` は、Step 側の既定 YAML を全部置き換えるのではなく、境界 Config から必要な値だけを部分上書きします。このサンプルでは `Pipeline.Load.Path`、`Pipeline.Normalize.Uppercase`、`Pipeline.Report.Heading`、`Pipeline.Report.BodyHeading`、`Save.Path` を root 側で示し、`Pipeline.Parse` や `Pipeline.Analyze` は Step 側の既定 YAML をそのまま使います。

## NuGet 参照の補完準備

このディレクトリの `omnisharp.json` は、`.csx` 内の NuGet 参照を C# 言語サービスが解決できるように `enableScriptNuGetReferences` を有効にし、対象となる .NET の版を `net8.0` に固定します。

補完用の依存関係を先に復元する場合は、Step を実行しない `validate` を使います。

```bash
engine validate samples/multi-folder-composite/main.csx --workflow-config appsettings.yaml --engine-config engine.yaml
```

新しい `#r "nuget: ..."` または `#load "nuget: ..."` を追加した後は、検証成功後に OmniSharp または使用中の C# 言語サービスを再起動してください。Engine は NuGet 復元を行いますが、別プロセスで動作する言語サービスへ参照を直接注入せず、既存の `omnisharp.json` も自動変更しません。

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
