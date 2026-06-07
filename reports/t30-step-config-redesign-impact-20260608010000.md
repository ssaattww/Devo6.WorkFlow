# Sub-agent実行レポート

## タスク

Step 単位 Config 再設計の影響調査。

## sub-agentを使う理由

Config 契約が README 表現ではなく実装済み API と設計範囲に影響するため、親側の判断前に既存実装、検査、設計書、task 追跡への影響を独立して洗い出すため。

## 対象範囲

- `doc/workflow_engine_spec.md`
- `README.md`
- `tasks-status.md`
- `phases-status.md`
- `src/`
- `tests/`
- `reports/t30-step-config-redesign-impact-20260608010000.md`

## 対象外

- C# 実装
- C# 検査実装
- 設計書の確定編集
- task 追跡の確定編集
- commit
- PR 本文更新

## 実行コマンド

- 実行コマンド:
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-doc-maintainer/SKILL.md`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/task-consistency-manager/SKILL.md`
  - `sed -n '1,260p' reports/t30-step-config-redesign-impact-20260608010000.md`
  - `git status --short`
  - `rg --files | rg '(^README\.md$|^doc/workflow_engine_spec\.md$|^tasks-status\.md$|^phases-status\.md$|CompositeStep\.cs$|CsxEntryLoader\.cs$|StandardConfigLoader\.cs$|StepContext\.cs$|StandardConfigLoadingContractTests\.cs$|CompositeStepTests\.cs$)'`
  - `sed -n` による調査対象 file の確認
  - `rg -n "ConfigType|StandardConfigLoader|WithConfig|StepContext|Set\<|Get\<|--set|Settings|ConfigPath|ValidateConfig" src tests doc README.md tasks-status.md phases-status.md`
  - `git diff -- doc/workflow_engine_spec.md`
  - `nl -ba` による指摘箇所の行番号確認
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
  - `sed -n '1,220p' package.json`
  - `rg --files tools/lint && sed -n '1,220p' tools/lint/README.md`
  - `npm run lint:md:targets`
  - `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t30-step-config-redesign-impact-20260608010000.md`
  - `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t30-step-config-redesign-impact-20260608010000.md`

## 対象ファイル

- 変更または確認したファイル:
  - 変更: `reports/t30-step-config-redesign-impact-20260608010000.md`
  - 確認: `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - 確認: `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - 確認: `src/Devo6.WorkFlow.Engine/StandardConfigLoader.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/StepContext.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/StandardConfigLoadingContractTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`
  - 確認: `doc/workflow_engine_spec.md`
  - 確認: `README.md`
  - 確認: `tasks-status.md`
  - 確認: `phases-status.md`
  - 確認: `src/Devo6.WorkFlow.Cli/Program.cs`
  - 確認: `src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/EngineArguments.cs`
  - 確認: `package.json`
  - 確認: `tools/lint/README.md`

## 指摘事項

- 指摘要約または「指摘なし」:
  - 現行実装では Step ごとに Config 型を宣言できない。制約は `CompositeStep<TOut>.ConfigType` が Entry 全体に 1 個だけ保持され、`WithConfig<TConfig>()` がその 1 個を設定する点にある。
  - `StepRegistration` は Step 名、実行処理、値生成処理だけを保持しており、Step 登録単位の Config 型、YAML path、override scope を持たない。
  - `CsxEntryLoader` は Entry 解決後に `ConfigType` だけを reflection で取得し、単一 `--config` YAML を `StandardConfigLoader.Load(configPath, configType, settings)` で 1 個の標準 Config に変換する。
  - `CompositeStep.ExecuteWorkflowAsync` は Step 実行前に `options.StandardConfig` を `StepContext.Set<TConfig>(config)` で 1 回だけ登録する。Step ごとの登録、切り替え、同型 Config の名前分けは行わない。
  - `StepContext` 自体は型付き値と名前付き値を保持できるため、基盤としては複数 Config を置ける。ただし engine 経路から複数 Config を読み込んで登録する API と実装がない。
  - `StandardConfigLoader` は YAML 全体を指定型へ deserialize し、`--set` を Config root からの public property path として適用する。YAML の部分木を Step Config 型へ読む入口や、section path に応じて `--set` prefix を剥がす処理はない。
  - 推奨 API 案は Step 登録単位の明示 Config: `Run<TStep,TOut>().WithConfig<TConfig>("Load")`。既存 `WithConfig<AppConfig>()` は Entry 全体 Config として残し、文字列引数付き overload を現在の Step 登録へ適用する。YAML path は単一 `--config` file 内の section path とし、`--set Load.Path=x` は `Load` section 用に `Path=x` として適用する。`run` では宣言済み Step Config を全て読み込み、変換、override 適用、検証してから最初の Step を実行する。実行時は対象 Step の直前に `StepContext.Set<TConfig>(config)` で登録する。
  - 代替案 1 は Step 型が `IConfigStep<TConfig>` などで Config 型を宣言する方式。Step の再利用性は高いが、YAML section 名の既定値や同じ Step 型を複数回使う場合の path override が必要になり、構成側の明示性が落ちる。
  - 代替案 2 は複数の名前付き Config を Entry で宣言し、Step が `StepContext.Get<TConfig>("Load")` で読む方式。既存 `StepContext` に合うが、Step 実装が呼び出し元の名前を知る必要があり、reusable Step が自己完結しにくい。
  - 代替案 3 は Config 読み込みを通常 Step として明示する方式。既存 API で近いことはできるが、標準の実行前検証、`--set` 適用、README で説明できる再利用契約になりにくい。
  - 最初の TDD は CLI 利用者目線の E2E がよい。`LoadConfig`、`ConvertConfig`、`SaveConfig` を個別型にし、`Run<LoadStep,LoadResult>().WithConfig<LoadConfig>("Load")` のように登録した `.csx` を `engine run main.csx --config config/appsettings.yaml --set Convert.ToUpper=false` で実行し、各 Step が `StepContext.Get<各Config>()` から値を読めることを確認する。あわせて後続 Step Config の検証失敗が最初の Step 実行前に `CONFIG_LOAD_FAILED` になる検査を置くと、validate/run 境界と部分実行防止を固定できる。
  - task 追跡は T30 README を現状のまま完了させると、中央集約 Config を利用者向け契約として固定してしまう。T30 は Step 単位 Config 再設計の判断と設計更新まで block 扱いにするのが妥当。新 task として T32「Step 単位 Config 契約の設計更新」と T33「Step 単位 Config API と読み込み実装」を追加し、P12 とは別の Config 再設計 phase、または P12 の前提 task として明示する案を推奨する。
  - 未コミット `README.md` は、最小例の `AppConfig` 集約、各 Step の `StepContext.Get<AppConfig>()`、末尾 `.WithConfig<AppConfig>()`、Config 章の「全体 Config を取得」説明が、望ましい Step 単位 Config と逆方向である。
  - 未コミット `doc/workflow_engine_spec.md` 差分は、`AppConfig` の `Load`、`Convert`、`Save` property に各 section を対応させる説明、各 Step が全体 Config を読む説明、単一 Config 型のみを扱う説明、Step ごとの Config 自動注入禁止の表現が、再設計後の契約と衝突する可能性が高い。既存互換として中央集約 Config を残す場合でも、推奨例として前面に出すべきではない。

## 結果

- 結果:
  - 調査完了。現行実装は Entry 単位の単一 Config 型だけを標準経路として持ち、Step 単位 Config は未対応。
  - 推奨は Step 登録単位の明示 Config API を追加し、単一 YAML file の section path と `--set` prefix を対応させる案。
  - 設計本文、C# 実装、検査、`tasks-status.md`、`phases-status.md` は編集していない。
  - `dotnet test` は調査範囲では必須ではないため未実行。
  - Markdown focused textlint は成功。cspell は repo 設定の対象外 path として skip。

## リスク

- 未解決のリスクまたは後続対応:
  - 同じ Config 型を複数 Step で別 section として使う場合、`StepContext.Set<TConfig>()` の上書きでよいか、名前付き登録も標準化するかは設計判断が必要。
  - `engine validate` を従来どおり Config path 存在確認までにするか、Step Config 型の変換と検証まで広げるかは契約変更として判断が必要。
  - 既存 `WithConfig<AppConfig>()` を互換 API として残す場合、README では旧方式と推奨方式の位置づけを明確に分けないと、再び中央集約 Config が推奨に見える。
  - `reports/` は `npm run lint:md:targets` の通常対象外であり、cspell も `ignorePaths` で skip されるため、レポートの spell gate は pass ではなく skip 扱い。
