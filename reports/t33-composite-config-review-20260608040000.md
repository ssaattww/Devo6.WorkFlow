# T33 Composite Config レビュー

## 対象

- T33: Step 単位 Config API と読み込み処理
- `doc/workflow_engine_spec.md`
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `src/Devo6.WorkFlow.Engine/StandardConfigLoader.cs`
- `src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
- `tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`
- `tests/Devo6.WorkFlow.Tests/StandardConfigLoadingContractTests.cs`
- `tasks-status.md`
- `reports/t33-*.md`

## レビュー観点

- Step 内 Config 型と CompositeStep 境界 Config 型の契約に合っているか。
- `WithConfig<MainConfig>()` が境界 Config 型として扱われ、Step 登録単位 Config があるのに未宣言の場合は `CONFIG_LOAD_FAILED` になるか。
- `--set Convert.ToUpper=false` が境界 Config 型への上書きとして扱われるか。
- `validate` が Config path 存在確認に留まり、型変換や値検証へ進まないか。
- 旧 `.WithConfig<TConfig>()` だけを使う Entry 全体 Config 互換 API が壊れていないか。
- C# の新規または変更コメントが日本語で、関数名と検査名が英語か。
- 不要な旧 API や古い「区画 path」表現が残っていないか。
- Markdown lint と表記揺れ検査が通る状態か。

## 検証結果

- `dotnet test Devo6.WorkFlow.sln --filter StandardConfigLoadingContractTests`: 成功、24 件。
- `dotnet test Devo6.WorkFlow.sln --filter CompositeStepTests`: 成功、11 件。
- `dotnet test Devo6.WorkFlow.sln`: 成功、167 件。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`: 成功。
- `npm run lint:md`: 成功。
- `npm run lint:md:terms`: 成功。
- `git diff --check`: 成功。

## 指摘

- [中] `StandardConfigLoader.LoadSection` が不要な旧 API として残っています。現契約では Step 登録単位 Config は YAML 全体を CompositeStep 境界 Config 型へ変換し、`sectionPath` は境界 Config 型上のプロパティ path 抽出に使うだけです。一方で `LoadSection` は YAML の一部を個別 deserialize し、区画接頭辞を剥がした `settings` を適用する旧契約そのものです。`rg` では呼び出し元もなく、現在の正常系では不要です。残すと今後の修正時に旧経路へ戻る余地があり、レビュー観点の「不要な旧 API」に該当します。対象: `src/Devo6.WorkFlow.Engine/StandardConfigLoader.cs:36`, `src/Devo6.WorkFlow.Engine/StandardConfigLoader.cs:43`
- [低] 新規または変更コメントに古い「区画 path」表現が残っています。`WithConfig<TConfig>(string sectionPath)` と `StepConfigRegistration.SectionPath` は現在の契約では境界 Config 型上のプロパティ path ですが、コメント上は `YAML 区画 path` や `Config YAML から読み込む区画 path` のままです。設計書本文は境界 Config プロパティ path に寄せているため、API 利用者と後続実装者に旧契約を示します。対象: `src/Devo6.WorkFlow.Engine/CompositeStep.cs:205`, `src/Devo6.WorkFlow.Engine/CompositeStep.cs:208`, `src/Devo6.WorkFlow.Engine/CompositeStep.cs:795`, `src/Devo6.WorkFlow.Engine/CompositeStep.cs:816`

## 判断

指摘あり。機能契約については、差分上は `WithConfig<MainConfig>()` を境界 Config 型として扱い、Step 登録単位 Config の境界未宣言を `CONFIG_LOAD_FAILED` にし、`--set Convert.ToUpper=false` を境界 Config 型への override として適用する実装になっています。`validate` も Config path 存在確認に留まり、旧 `.WithConfig<TConfig>()` だけの Entry 全体 Config 互換経路も維持されています。

ただし、旧 `LoadSection` と古い「区画 path」コメントは T33 の整理対象として残っています。修正後に同じ検査と Markdown lint を再実行する必要があります。

本レビューで追加実行した検証:

- `npm run lint:md`: 成功。
- `npm run lint:md:terms`: 成功。
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t33-composite-config-review-20260608040000.md`: 成功。
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t33-composite-config-review-20260608040000.md`: repo 設定の `ignorePaths` により skip。

レビュー指摘 2 件は修正済みです。`StandardConfigLoader.LoadSection` と専用補助関数を削除し、Step 登録単位 Config の path コメントを境界 Config 型上の property path として揃えました。

再レビュー結果: 指摘なし。`StandardConfigLoader.LoadSection`、`DeserializeNode`、`NodeIsEmptySection`、`SerializeNode` は現在の `StandardConfigLoader.cs` に存在しません。`CompositeStep.cs` の `WithConfig<TConfig>(string sectionPath)` と `StepConfigRegistration.SectionPath` のコメントは、境界 Config 型上の property path 契約に揃っています。`StandardConfigLoader.cs` の Step 登録単位 Config 関連コメントも property path 契約に揃っており、追加ブロッカーはありません。
