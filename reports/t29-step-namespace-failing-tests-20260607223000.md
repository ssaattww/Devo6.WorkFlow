# Sub-agent実行レポート

## タスク

T29 Step 名名前空間化の失敗検査作成。

## sub-agentを使う理由

公開 API、loader、CLI の契約を実装前に検査で固定し、親エージェントが管理とレビューに集中するため。

## 対象範囲

- `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
- `tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`
- `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`
- 必要な最小範囲の test helper
- `reports/t29-step-namespace-failing-tests-20260607223000.md`

## 対象外

- production code 実装
- 設計書の追加変更
- 進捗同期
- commit
- PR 本文更新

## 実行コマンド

- `dotnet test Devo6.WorkFlow.sln --filter "CsxEntryLoaderTests|CsxEntryValidationTests|CliRunValidateTests"`
  - 期待どおり赤。追加した 12 件が失敗し、既存 38 件は成功。
  - 主な失敗理由は `CompositeStep.Define("Build", namespaceName: "Deploy")` の `namespaceName` overload が未実装で、`.csx` compile が `CS1739` / `SCRIPT_COMPILE_FAILED` になるため。
- `git diff --check`
  - 成功。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`
  - 成功。
- `npm run lint:md`
  - 成功。ただし repo の Markdown target は reports を含まない。
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t29-step-namespace-failing-tests-20260607223000.md`
  - 成功。
- `npx cspell reports/t29-step-namespace-failing-tests-20260607223000.md`
  - `Files checked: 0`。`reports/**` が cspell の `ignorePaths` 対象のため、focused cspell は unsupported。

## 対象ファイル

- `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
  - `ExecuteQualifiedNamespaceEntryByPublicName`
  - `ExecuteAllowsSameShortNameInDifferentNamespaces`
  - `ExecuteShortEntryNameResolvesSingleNamespaceCandidate`
  - `ExecuteShortEntryNameAmbiguityFailsWithEntryStepNotFound`
  - `ExecuteLoadedNamespaceEntryByQualifiedName`
  - `ExecutePreservesNamespaceMetadataAfterConfigRunAndRunAsyncChain`
- `tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`
  - `ValidateQualifiedNamespaceEntryByPublicName`
  - `ValidateDuplicateQualifiedNamespaceEntryFailsWithDuplicateStepName`
- `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`
  - `EngineRunMainCsxEntryDeployBuildExecutesNamespaceEntry`
  - `EngineValidateMainCsxEntryDeployBuildValidatesNamespaceEntry`
  - `EngineRunShortEntryBuildResolvesSingleNamespaceCandidate`
  - `EngineValidateShortEntryBuildFailsWhenNamespaceCandidatesAreAmbiguous`
- `reports/t29-step-namespace-failing-tests-20260607223000.md`

## 指摘事項

- 現行 production code は `CompositeStep.Define(string name)` のみで、`namespaceName` named parameter を受け取らない。
- そのため追加検査は test project 自体の compile ではなく、生成 `.csx` の compile 時に `CS1739: The best overload for 'Define' does not have a parameter named 'namespaceName'` で赤になる。
- CLI E2E も同じ未実装 API 由来で `SCRIPT_COMPILE_FAILED` を返しており、T29 実装後は `Deploy.Build` 解決、短い `Build` の一意解決、曖昧指定の `ENTRY_STEP_NOT_FOUND` を通す必要がある。

## 結果

- production code は変更していない。
- 名前空間付き Entry の公開完全修飾名実行 / 検証、異なる名前空間の同名 Entry 共存、完全修飾名重複、CLI run / validate、短い Entry 名互換解決、曖昧指定失敗、`#load` 先解決、`WithConfig` / `RunAsync` chain 後の metadata 維持を失敗検査として追加した。
- `dotnet test` は赤で、T29 production 実装前の期待状態になっている。

## リスク

- 赤の最初の理由が `namespaceName` overload 未実装であるため、Entry 解決や重複判定の詳細不備は production 実装後の次回テスト実行で表面化する。
- 曖昧指定検査は error code に加えて候補名 `Deploy.Build` / `Test.Build` の表示も要求しているため、実装時は error message も設計どおり整える必要がある。
- `reports/**` は repo-local cspell の除外対象なので、レポート単体の cspell focused lint は pass として扱えない。
