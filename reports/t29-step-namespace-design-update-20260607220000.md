# Sub-agent実行レポート

## タスク

T29 Step 名名前空間化の設計更新。

## sub-agentを使う理由

公開 API、CLI Entry 指定、検証契約が変わるため、実装前に設計を独立して更新するため。

## 対象範囲

- `doc/workflow_engine_spec.md`
- `reports/t29-step-namespace-investigation-20260607213000.md`
- `reports/t29-step-namespace-design-update-20260607220000.md`

## 対象外

- C# 実装
- C# 検査実装
- 進捗同期
- commit
- PR 本文更新

## 実行コマンド

- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-executor/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `sed -n '1,240p' /home/ibis/dotnet_ws/devo6.workflow/reports/t29-step-namespace-design-update-20260607220000.md`
- `rg -n "15\\.2|^##? 17|19\\.3|Entry|CompositeStep|DUPLICATE_STEP_NAME|ENTRY_STEP_NOT_FOUND|WithConfig|RunAsync|#load|StepName|EntryName" doc/workflow_engine_spec.md`
- `sed -n '1,260p' reports/t29-step-namespace-investigation-20260607213000.md`
- `rg -n "T29|Step.*namespace|namespace|名前空間" tasks-status.md phases-status.md`
- `ls tools/lint`
- `sed -n '1,220p' tools/lint/README.md`
- `sed -n '1,220p' package.json`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1040,1135p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1328,1390p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1410,1498p;1538,1602p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1698,1738p;1798,1822p;1928,1942p'`
- `git status --short`
- `nl -ba doc/workflow_engine_spec.md | sed -n '930,990p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '386,430p;566,612p;614,630p'`
- `sed -n '1,220p' .textlintrc.json`
- `cat tools/lint/markdown-targets.json`
- `rg -n "qualified|namespaceName|Deploy|DUPLICATE|ENTRY_STEP" tools/lint/markdown-whitelist.yaml tools/lint/prh.yml doc/workflow_engine_spec.md reports/t29-step-namespace-investigation-20260607213000.md`
- `git diff -- doc/workflow_engine_spec.md reports/t29-step-namespace-design-update-20260607220000.md`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t29-step-namespace-design-update-20260607220000.md`

## 対象ファイル

- `doc/workflow_engine_spec.md`
  - 14.4 に `CompositeStep.Define("Build", namespaceName: "Deploy")`、`Name`、`NamespaceName`、`QualifiedName` の API 契約を追加した。
  - 15.1 に `--entry Deploy.Build`、公開名による Entry 解決、短い Entry 名の互換解決、曖昧指定時のエラーコードを追加した。
  - 15.2 に完全修飾名単位の重複判定を追加した。
  - 17.1 / 17.2 に名前空間付き Entry の検証契約を追加した。
  - 18.1 / 18.3 / 18.4 に `EntryName` は完全修飾名、`StepName` は従来どおり Step 型名を基本とする契約を追加した。
  - 19.3 から Step 名の名前空間化を次フェーズ候補から外した。
  - 21.7 に T29 の採用事項、実装時コメント標準、TDD で置くべきテストケースを追加した。
- `tools/lint/markdown-whitelist.yaml`
  - 設計本文で必要になったカタカナ語と `XML` を許可語に追加した。
- `reports/t29-step-namespace-design-update-20260607220000.md`
  - 本設計更新の実行コマンド、対象ファイル、設計判断、結果、リスクを記録した。

## 指摘事項

1. Entry 解決はスクリプト変数名ではなく、`CompositeStep` の公開名で行う契約にした。公開名は短い `Name` と `QualifiedName` である。スクリプト変数名は C# script 上の識別子であり、Entry 契約ではない。

2. `CompositeStep.Define("Build", namespaceName: "Deploy")` を採用し、完全修飾名は `Deploy.Build` とした。既存の `CompositeStep.Define("Build")` は名前空間なし Entry として互換維持し、完全修飾名は `Build` とした。

3. 短い `--entry Build` は、名前空間なしの `Build` があればそれを優先する。名前空間なしの `Build` がなければ、短い名前が一意な Entry に互換解決する。複数候補がある場合は曖昧指定として失敗する。

4. 曖昧な短い Entry 指定のエラーコードは `ENTRY_STEP_NOT_FOUND` とした。完全修飾名の重複ではないため `DUPLICATE_STEP_NAME` ではなく、メッセージで複数候補に一致したことと完全修飾名指定を求めることを明示する。

5. 重複判定は完全修飾名単位とした。`Deploy.Build` と `Test.Build` は共存でき、`Deploy.Build` 同士は `DUPLICATE_STEP_NAME` とする。

6. `WorkflowResult.EntryName`、CLI 成功出力、ログスコープの `EntryName` は完全修飾名を記録する。`ExecutionTraceStep.StepName` とログスコープの `StepName` は従来どおり Step 型名を基本維持する。

7. `WithConfig<TConfig>()`、`Run<TStep, TStepOut>()`、`RunAsync<TStep, TStepOut>()` 後も、短い名前、名前空間名、完全修飾名を維持する契約にした。

8. `#load` 先で定義された名前空間付き Entry も、Entry `.csx` に直接定義された Entry と同じ解決規則にした。

9. T30/T31 のコメント標準は後続 task だが、T29 で追加または変更する API、ヘルパー、テストメソッドは関数名英語、XMLコメント日本語、パブリック以外もコメント対象として実装を進める前提を明記した。

10. TDD で置くべきテストケースを 21.7 に整理した。主な対象は API メタ情報、ローダー実行 / 検証、CLI `run` / `validate`、`#load` 解決、重複判定、短い Entry 名の互換解決、連鎖呼び出し後のメタ情報維持である。

## 結果

- `doc/workflow_engine_spec.md` に T29 Step 名名前空間化の実装前契約を反映した。
- API は `CompositeStep.Define("Build", namespaceName: "Deploy")` を採用し、名前空間なし API は互換維持する設計にした。
- CLI は既存 `--entry` option を維持し、値として `Deploy.Build` を受ける設計にした。
- ローダーは `CompositeStep` の公開名 / 完全修飾名で Entry を解決し、スクリプト変数名だけに依存しない設計にした。
- 重複判定、Entry 解決、実行結果 / ログ / トレースの表示名、`#load` 先解決、連鎖呼び出し後のメタ情報維持を設計書に明記した。
- T29 実装前に置くべき TDD ケースを設計書に列挙した。
- 通常語はインラインコードで逃がさず、必要なカタカナ語を whitelist に追加した。
- `npm run lint:md` は成功した。
- `npm run lint:md:terms` は成功し、SudachiPy term variants は none だった。
- `git diff --check` は成功した。
- レポート単体 textlint は成功した。

## リスク

- 現行実装はスクリプト変数名で Entry を探しているため、公開名解決へ寄せる実装では `var X = CompositeStep.Define("Build"); --entry X` のような未設計利用が動かなくなる可能性がある。設計書ではスクリプト変数名を Entry 契約としない判断にした。
- 短い `--entry Build` は名前空間なし Entry を優先するため、名前空間なし `Build` と名前空間付き `Deploy.Build` が共存する場合、名前空間付き Entry を実行するには `Deploy.Build` 指定が必要である。
- 曖昧な短い Entry 指定は既存エラーコードに閉じるため `ENTRY_STEP_NOT_FOUND` とした。将来、曖昧指定専用コードが必要になった場合は別 task で扱う。
- `reports/` は repo の Markdown full lint 対象から除外されているため、このレポートは full lint の直接対象ではない。検証では可能な範囲で単体 textlint を実行する。
