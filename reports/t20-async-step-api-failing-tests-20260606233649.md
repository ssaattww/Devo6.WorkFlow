# Sub-agent実行レポート

## タスク

- 目的: T20 の非同期 Step API 実装前に、期待契約を固定する失敗検査を追加する。
- タスク種別: test authoring

## sub-agentを使う理由

- 理由: ユーザー指示により、検査作成は sub-agent に委譲し、parent は管理、採用判断、review、commit、push を担当するため。

## 対象範囲

- 対象: `IAsyncStep<TOut>`、`RunAsync<TStep, TOut>()`、`ExecuteWorkflowAsync`、同期 Step との混在順序、非同期戻り値の `Produce`、非同期例外の失敗結果化を検査で固定する。

## 対象外

- 対象外: 実装コードの追加、設計本文の変更、tracking 更新、timeout 実処理、retry、commit。

## 実行コマンド

- 実行コマンド:
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/tdd-executor/SKILL.md`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/implementation-executor/SKILL.md`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/report-output-manager/SKILL.md`
  - `sed -n '1,240p' reports/t20-async-step-api-failing-tests-20260606233649.md`
  - `rg --files tests/Devo6.WorkFlow.Tests`
  - `sed -n '1,260p' reports/t20-async-step-api-design-impact-20260606232258.md`
  - `rg -n "IAsyncStep|RunAsync|CompositeStep|ExecuteWorkflowAsync|IStep<|Produce|STEP_EXECUTION_FAILED" tests/Devo6.WorkFlow.Tests src doc/workflow_engine_spec.md`
  - `git status --short`
  - `sed -n '1,280p' tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`
  - `sed -n '1,240p' tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs`
  - `sed -n '1,240p' tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
  - `sed -n '1,80p' tests/Devo6.WorkFlow.Tests/GlobalUsings.cs`
  - `sed -n '1,120p' src/Devo6.WorkFlow.Abstractions/IStep.cs`
  - `sed -n '1,320p' src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - `sed -n '1,200p' tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj`
  - `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~AsyncStepApiContractTests`
  - `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName!~AsyncStepApiContractTests`
  - `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t20-async-step-api-failing-tests-20260606233649.md`

## 対象ファイル

- 変更または確認したファイル:
  - 追加: `tests/Devo6.WorkFlow.Tests/AsyncStepApiContractTests.cs`
  - 変更: `reports/t20-async-step-api-failing-tests-20260606233649.md`
  - 確認: `tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/GlobalUsings.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj`
  - 参照のみ: `src/Devo6.WorkFlow.Abstractions/IStep.cs`
  - 参照のみ: `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - 参照のみ: `doc/workflow_engine_spec.md`
  - 参照のみ: `reports/t20-async-step-api-design-impact-20260606232258.md`

## 指摘事項

- 指摘要約または「指摘なし」:
  - `IAsyncStep<TOut>` は現行 `Devo6.WorkFlow.Abstractions` に存在しないため、追加検査は `Assert.NotNull()` で失敗する。
  - `RunAsync<TStep, TOut>()` と `ExecuteWorkflowAsync` の実行契約検査は、先行する `IAsyncStep<TOut>` 欠落により現時点では同じ理由で失敗する。
  - 追加検査を除外した既存検査 60 件は成功しており、既存同期 `Run<TStep, TOut>()` / `IStep<TOut>` の検査は壊していない。

## 結果

- 結果:
  - 追加検査:
    - `IAsyncStep は StepInput と CancellationToken で Task 戻り値を実装できる`
    - `RunAsync は sync async sync を定義順に実行し await 後の Produce を下流へ渡す`
    - `async Step 例外は ExecuteWorkflowAsync で STEP_EXECUTION_FAILED になり後続を止める`
  - `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName~AsyncStepApiContractTests` は期待どおり失敗。3 件失敗、0 件成功、0 件 skip。
  - 主な失敗内容は `Devo6.WorkFlow.Abstractions.IAsyncStep\`1` が取得できず、`AsyncStepApiContractTests.RequireAsyncStepType` の `Assert.NotNull()` が失敗すること。
  - `dotnet test Devo6.WorkFlow.sln --filter FullyQualifiedName!~AsyncStepApiContractTests` は成功。60 件成功、失敗 0、skip 0。
  - report focused textlint は成功。
  - `src`、`doc/workflow_engine_spec.md`、`tasks-status.md`、`phases-status.md`、`tools/lint` は編集していない。

## リスク

- 未解決のリスクまたは後続対応:
  - 現時点では `IAsyncStep<TOut>` 未存在で早期失敗するため、`RunAsync` 登録、`ExecuteWorkflowAsync`、await 後 `Produce`、async 例外 trace の各詳細検査は T20 実装が進むにつれて順に赤から緑へ移行する想定。
  - 動的型生成を使って future API を reflection 経由で検査しているため、T20 実装時に `RunAsync` や `ExecuteWorkflowAsync` の公開名を変える場合は、設計契約の変更としてテスト更新が必要。
  - cspell は repo 設定で `reports/**` が除外されるため、今回の report focused Markdown 検査は textlint のみを証跡とした。
