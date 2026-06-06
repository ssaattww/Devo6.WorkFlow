# Sub-agent実行レポート

## タスク

T25 `Produce` 後の値の寿命と有効範囲を固定する検査を、検査先行で追加する。

## sub-agentを使う理由

ユーザー指示により、実装、調査、レビューは sub-agent に委譲する。

## 対象範囲

- `tests/Devo6.WorkFlow.Tests/` 配下の T25 検査
- 既存実装で検査が通るか失敗するかの確認
- 必要最小限の検査実行結果

## 対象外

- `src/` 配下の実装変更
- 設計書の編集
- 既存テスト名の一括変更

## 実行コマンド

- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/tdd-executor/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/implementation-executor/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,240p' reports/t25-produce-value-lifetime-failing-tests-20260607083000.md`
- `sed -n '1,240p' reports/t25-produce-value-lifetime-design-impact-20260607074500.md`
- `sed -n '1,260p' tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`
- `sed -n '1,620p' src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `sed -n '1,220p' src/Devo6.WorkFlow.Abstractions/StepInput.cs`
- `rg -n "Retry|ExecuteWorkflowAsync|Produce|Discard|StoreAs|Attempt|StepTimeout" tests/Devo6.WorkFlow.Tests`
- `dotnet test Devo6.WorkFlow.sln --filter ProduceValueLifetimeContractTests` : 成功。7 件成功、0 件失敗。
- `dotnet test Devo6.WorkFlow.sln` : 成功。105 件成功、0 件失敗。
- `git diff --check` : 成功。
- `npm run lint:md` : 成功。repo の Markdown 対象 5 件で textlint、cspell、whitelist 検査が成功。
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t25-produce-value-lifetime-failing-tests-20260607083000.md` : 成功。
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t25-produce-value-lifetime-failing-tests-20260607083000.md` : report は `ignorePaths` により対象外として skip。

## 対象ファイル

- 変更: `tests/Devo6.WorkFlow.Tests/ProduceValueLifetimeContractTests.cs`
- 変更: `reports/t25-produce-value-lifetime-failing-tests-20260607083000.md`
- 参照: `reports/t25-produce-value-lifetime-design-impact-20260607074500.md`
- 参照: `tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`
- 参照: `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- 参照: `src/Devo6.WorkFlow.Abstractions/StepInput.cs`

## 指摘事項

- 追加検査は既存実装で緑だった。`src/` 配下の実装変更なしで T25 の契約を検査として固定した。
- `Step1` が `Produce<SharedInput>` した値を `Step2` と `Step3` の両方が読めることを追加した。
- `Step1` が `Produce<string>("title")`、`Step2` が `Produce<string>("body")` し、`Step3` が両方を読めることを追加した。
- 型付き `string` と名前付き `string` が同じ CLR 型でも別キーとして共存することを追加した。
- 同じ型キーへの再 `Produce<SameInput>` が `Step2` 後処理で失敗し、`Step3` が開始しないことを追加した。
- 同じ `Type + name` への再 `Produce` が失敗し、後続 Step が開始しないことを追加した。
- `Discard` は現在 Step の戻り値を登録しないが、既存値は削除しないことを追加した。
- retry では失敗 attempt の値は残らず、成功 attempt の値だけが後続から読めることを追加した。

## 結果

新規 `ProduceValueLifetimeContractTests.cs` に T25 の利用者目線統合検査を 7 件追加した。対象検査は `dotnet test Devo6.WorkFlow.sln --filter ProduceValueLifetimeContractTests` で全件成功したため、追加直後の赤は発生しなかった。既存実装が T25 の追加契約を満たしていることを確認し、`src/` 配下は変更していない。

全体検査 `dotnet test Devo6.WorkFlow.sln` も 105 件すべて成功した。`git diff --check` も成功した。

Markdown 検査は `npm run lint:md` が成功した。repo の Markdown 対象は reports を含まないため、更新した report には focused textlint を明示実行して成功を確認した。focused cspell は repo 設定の `ignorePaths` により report が対象外として skip された。

## リスク

- 設計書更新は別担当の所有範囲であり、本作業では `doc/workflow_engine_spec.md` を編集していない。
- 作業開始時点で `doc/workflow_engine_spec.md` に既存変更があったが、所有範囲外のため触れていない。
- T25 の値寿命契約は同期 Step と `ExecuteWorkflowAsync` の retry/失敗 post-processing 経路で固定した。timeout、外部 cancel、trace 値保存形式の詳細は既存検査または T26 側の範囲として扱った。
- repo の Markdown lint 対象は reports を含まないため、report に対する cspell は `ignorePaths` により skip された。textlint は明示ファイル指定で成功している。
