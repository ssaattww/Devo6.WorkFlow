# Sub-agent実行レポート

## タスク

T56 `RunIf` / `TapIf` 実装のレビュー。

## sub-agentを使う理由

条件付き実行の中核実装が設計契約、TDD、Config、trace、retry、timeout、XML コメント標準を満たすか独立して点検するため。

## 対象範囲

- T56 実装差分
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
- `tests/Devo6.WorkFlow.Tests/RunIfTapIfContractTests.cs`
- `reports/t56-runif-tapif-implementation-20260610102000.md`

## 対象外

- `If`
- `Switch`
- `BranchBuilder`
- README と sample の更新
- コミット、送信、取り込み依頼操作

## 実行コマンド

- `git status --short`
  - 対象差分と未追跡ファイルを確認。
- `git diff --stat`
  - 成功。tracked 差分は 4 files changed, 505 insertions(+), 14 deletions(-)。
- `git diff -- src/Devo6.WorkFlow.Engine/CompositeStep.cs src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs tests/Devo6.WorkFlow.Tests/RunIfTapIfContractTests.cs tasks-status.md`
  - 成功。未追跡の `RunIfTapIfContractTests.cs` は `git diff` 出力には含まれないため、行番号付きで別途本体を確認。
- `dotnet test Devo6.WorkFlow.sln --filter RunIfTapIf`
  - 成功。11 件成功。
- `dotnet test Devo6.WorkFlow.sln --filter "RunIfTapIf|LambdaStep|Retry|Timeout|TraceValue|CodingStandards|StandardConfig"`
  - 成功。85 件成功。
- `git diff --check`
  - 成功。
- `npm run lint:md`
  - 成功。repo の Markdown target 7 件を確認。ただし未追跡の review report は target 一覧に含まれない。
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t56-runif-tapif-review-20260610103000.md`
  - 成功。
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t56-runif-tapif-review-20260610103000.md`
  - 成功扱い。repo の ignorePaths により対象 report は skipped、CSpell checked 0 files。

## 対象ファイル

- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
- `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
- `tests/Devo6.WorkFlow.Tests/RunIfTapIfContractTests.cs`
- `reports/t56-runif-tapif-implementation-20260610102000.md`
- `tasks-status.md`
- 現在の unstaged 差分全体。`reports/t56-runif-tapif-review-20260610103000.md` は本レビュー結果記入対象として確認。

## 指摘事項

- High: `src/Devo6.WorkFlow.Engine/CompositeStep.cs:737` と `src/Devo6.WorkFlow.Engine/CompositeStep.cs:853`。`StepRegistration.LastStatus` が共有登録オブジェクト上の可変状態として保存され、成功後の trace 生成時に再読込されている。同じ `CompositeStep` instance を並行して `ExecuteWorkflowAsync` した場合、片方の実行が `Skipped`、もう片方が `Succeeded` を設定する競合で、別実行の trace status が入れ替わり得る。T56 の重点観点である `CompositeStep` 再利用と並行実行で状態漏れがあるため、Step 実行結果の status は実行ローカル値として trace 生成まで保持する必要がある。
- Medium: `tests/Devo6.WorkFlow.Tests/RunIfTapIfContractTests.cs:181` と `tests/Devo6.WorkFlow.Tests/RunIfTapIfContractTests.cs:278`。T56 の確認項目に対し、`RunIfAsync` false と async `otherwiseAsync`、`TapIfAsync` false の `Skipped` trace / ProducedValues、`RunIfAsync` / `TapIfAsync` / StepInput overload の null 引数、`TapIf` 系の条件判定例外が直接検査されていない。同期系と共通実装で一部は推定できるが、公開 API と async 条件付き実行契約としては検査が不足している。

## 結果

- 指摘あり。T56 の対象範囲外である `If` / `Switch` / `BranchBuilder` 実装へ踏み出した差分は確認されなかった。
- `RunIf` / `RunIfAsync` / `TapIf` / `TapIfAsync` の公開 API 形状は設計書の T56 範囲に概ね対応している。
- `ExecutionTraceStepStatus.Skipped` と `WorkflowErrorCodes.ConditionEvaluationFailed` の XML コメントは日本語で意味のある文章になっている。
- 変更範囲の関数とプロパティには日本語 XML コメントがあり、テスト関数名は英語だった。
- 指定された dotnet test と `git diff --check` は成功したが、上記 High finding は並行実行の未検査競合であり、現行テスト結果だけでは解消できない。

## リスク

- `StepRegistration.LastStatus` の共有可変状態により、同一 `CompositeStep` 定義を並行 reuse する利用者で trace の `Skipped` / `Succeeded` が誤記録されるリスクが残る。
- async false path と公開 overload の null 引数検査が薄く、将来 overload ごとの分岐や修正で T56 契約が崩れても検査が検出しないリスクが残る。
- Markdown full lint と report 直接 textlint は成功したが、repo の cspell ignorePaths によりこの review report 自体の cspell は skipped のため、report 固有の spell check は未実施として扱う。
- review-enforcer と coding standards enforcer は読んだが、ユーザー指定により Serena、nested Codex、codex exec、親 workflow 再入は使っていない。そのため mandatory sub-agent 形式ではなく、現セッションのレビューとして記録した。
