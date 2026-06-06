# Sub-agent実行レポート

## タスク

- 目的: 旧 rev4 仕様の章を機能カテゴリへ分け、フル csx 方針でも残すべき契約が現行設計から落ちていないかを点検する。
- タスク種別: investigation

## sub-agentを使う理由

- 理由: 旧仕様の削除章から、YAML 固有で削除してよい契約と、csx 完結型でも移植すべき契約を機械的に洗い分けるため。

## 対象範囲

- 対象: `doc/workflow_engine_spec.md`
- 対象: 旧仕様 `git show cefc001:doc/workflow_engine_spec.md`
- 対象: `reports/issue-1-full-csx-design-update-20260605130833.md`
- 対象: `reports/issue-1-full-csx-design-review-20260605130833.md`

## 対象外

- 対象外: ファイル編集、実装コード作成、コミット作成、PR作成。

## 実行コマンド

- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,240p' /home/ibis/AI/CodexSkill/skills/design-doc-maintainer/SKILL.md`
- `sed -n '1,260p' reports/issue-1-spec-old-contract-gap-scan-20260606161536.md`
- `rg -n '^#{1,6} ' doc/workflow_engine_spec.md`
- `git show cefc001:doc/workflow_engine_spec.md | rg -n '^#{1,6} '`
- `wc -l doc/workflow_engine_spec.md reports/issue-1-full-csx-design-update-20260605130833.md reports/issue-1-full-csx-design-review-20260605130833.md reports/issue-1-spec-old-contract-gap-scan-20260606161536.md`
- `git show cefc001:doc/workflow_engine_spec.md | wc -l`
- `sed -n '1,120p' reports/issue-1-full-csx-design-update-20260605130833.md`
- `sed -n '1,140p' reports/issue-1-full-csx-design-review-20260605130833.md`
- `sed -n '1,220p' doc/workflow_engine_spec.md`
- `sed -n '221,520p' doc/workflow_engine_spec.md`
- `sed -n '521,860p' doc/workflow_engine_spec.md`
- `git show cefc001:doc/workflow_engine_spec.md | sed -n '1,3065p'`
- `rg -n "Message|Validation|検証|ILogger|Logger|Trace|ExecutionTrace|WorkflowResult|retry|timeout|AssemblyLoadContext|#r|#load|NuGet|validate|run|FailurePolicy|WorkflowStepException|CancellationToken|信頼|サンドボックス" doc/workflow_engine_spec.md`
- `git diff -- doc/workflow_engine_spec.md reports/issue-1-spec-old-contract-gap-scan-20260606161536.md`
- `git status --short`
- `nl -ba reports/issue-1-spec-old-contract-gap-scan-20260606161536.md`
- `git diff -- doc/workflow_engine_spec.md | sed -n '1,160p'`
- `sed -n '1,240p' reports/issue-1-spec-old-contract-gap-scan-20260606161536.md`
- `rg -n '^#{1,6} ' doc/workflow_engine_spec.md`
- `sed -n '735,1120p' doc/workflow_engine_spec.md`
- `sed -n '1121,1500p' doc/workflow_engine_spec.md`
- `rg -n "Message|DataAnnotations|nullable|IValidatableObject|Required|record|ILoggerFactory|BeginScope|EventId|Retryable|WorkflowStepException|retry|timeout|信頼|サンドボックス|WorkflowResult|ExecutionTrace|AssemblyLoadContext|validate|StepInput 検証|Config 検証" doc/workflow_engine_spec.md`
- `wc -l doc/workflow_engine_spec.md reports/issue-1-spec-old-contract-gap-scan-20260606161536.md`

## 対象ファイル

- 確認: `/home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- 確認: `/home/ibis/AI/CodexSkill/skills/design-doc-maintainer/SKILL.md`
- 確認: `doc/workflow_engine_spec.md`
- 確認: 旧仕様 `git show cefc001:doc/workflow_engine_spec.md`
- 確認: `reports/issue-1-full-csx-design-update-20260605130833.md`
- 確認: `reports/issue-1-full-csx-design-review-20260605130833.md`
- 更新: `reports/issue-1-spec-old-contract-gap-scan-20260606161536.md`

## 指摘事項

- ブロッキング: あり。最新の現行設計では Entry、csx 解決、validate、基本実行結果、ログ、Trace、初期実装範囲が追記済みだが、Message/Config 型定義方針、検証詳細、失敗時動作、retry/timeout、信頼境界など、実装前に契約化すべき非 YAML 契約がまだ残る。
- 章分類: 削除妥当。
  - 旧 `5. YAML仕様`、`10. Binding式仕様`、`11. Step Config仕様`、`13. Binding / Config結び付けの命名規則` は、YAML schema、`flows:`、`steps:`、`next:`、binding 式、YAML Config provider を前提にするため削除妥当。
  - 旧 `15. Flow仕様`、`16. 入れ子Flow仕様` の Flow ID、`entryFlow`、`end`、Flow Call Step、Flow depth は、独立 Flow 廃止後はそのまま移植しない。ただし CompositeStep のネスト制限として要否判断は残る。
  - 旧 `18. 型検証仕様` の `WorkflowStep<T...>` 継承推論、Built-in Step 型推論、YAML 明示型は、現行 `IStep<TOut>.Execute(StepInput)` と CompositeStep 方針に置換済みまたは削除妥当。
  - 旧 `27. サンプル全体` の `workflow.yaml` サンプル、旧 `24.3 Flow一覧`、`24.4 Step一覧`、`24.5 型情報表示` の Flow/YAML 前提表示は削除妥当。必要なら新しい Entry / CompositeStep 向け CLI 表示に置換する。
- 章分類: csx 方針へ置換して移植すべき。
  - 旧 `2.2 型の真実はC#側に置く`、`4.4 Message`、`7. Message型定義`、`8. Config型定義` は、YAML ではなく C# / csx に型の真実を置く契約として残すべき。現行設計は `StepInput` と `Config` はあるが、Message 型の定義方針、`#nullable enable`、`record` 推奨、DataAnnotations 方針が未記載。
  - 旧 `12. Workflow Config仕様` は YAML 既定値を除き、Run 単位の Config snapshot、実行時 Config、override、merge、path 値の扱いを現行 `StepContext` Config 方針へ置換して残すべき。現行設計は初期版では `EngineArguments` に格納し型変換はユーザー Step とする方針だが、snapshot 性、merge、path 基準は未確定。
  - 旧 `14. 検証仕様` は Message/Config/StepInput/Step 出力/CompositeStep 終了時出力に置換して残すべき。現行設計には validate、StepInput 検証、Config 検証が追加されたが、DataAnnotations、nullable 参照型、Step 出力検証、検証エラー形式、検証済み入力を前提にできる範囲が未記載。
  - 旧 `19. エラー処理` は YAML 解析や Flow/next 固有コードを除き、csx ロード/コンパイル失敗、Step 生成失敗、入力取得失敗、Config 不在、Step 実行例外、timeout、retry、エラーコード体系として残すべき。現行設計には基本エラーコードが追加されたが、retry 対象/対象外、timeout の協調キャンセル、`WorkflowStepException` 相当の業務エラー契約が未記載。
  - 旧 `20. ログ仕様` は残すべき。現行設計は `Microsoft.Extensions.Logging` 利用を追加済みだが、`StepContext.Logger` の公開 API、`ILoggerFactory` 受け取り、構造化ログ、Scope、標準 EventId、外部 logger provider 委譲がまだ薄い。
  - 旧 `21. 実行結果とトレース` は残すべき。現行設計は `WorkflowResult` と `ExecutionTrace` 分離を追加済みだが、Step 単位の `ExecutionNode`、Duration、AttemptCount、CapturedValue、redaction policy の詳細は未確定事項に残る。
  - 旧 `22.4 ScriptCompiler抽象`、`23. csxコンパイル仕様` は現行 `16. csx 解決と参照方針` へ概ね移植された。ただし `IScriptCompiler` 抽象、`#r "nuget: ..."` の exact version / lock / package source 詳細、Abstractions assembly identity error、コンパイル診断の扱いは必要なら追記候補。
  - 旧 `23.11 csx信頼境界` はフル csx 方針ではむしろ重要度が上がるため残すべき。現行設計は信頼済みワークフローと未信頼 `.csx` の安全実行対象外を記載したが、未信頼コードを直接実行しない、完全サンドボックスを提供しない、`Dotnet.Script.Core` はセキュリティ境界ではない、という明示が不足している。
  - 旧 `24.1 実行`、`24.2 検証` は `workflow.yaml` から `main.csx` に置換済み。残件は validate の詳細よりも、run 時の入力、Config、override、終了コード、結果出力形式の契約をどこまで初期版に含めるかである。
  - 旧 `25. 実装範囲` はフル csx 版に再定義済み。現行 `19. 初期実装範囲` は妥当だが、上記の型定義、検証詳細、信頼境界を反映した調整余地がある。
- 章分類: 未確定事項として残すべき。
  - 旧 `9.3 CancellationTokenの扱い`、`19.2 retry`、`19.3 timeout` は現行 `21.1 非同期 API` と合わせて、同期 API 先行時にどこまで初期実装するか未確定事項に残すべき。
  - 旧 `17. Control Step仕様` は YAML 予約構文としては削除妥当だが、If/ForEach/While/Switch/Parallel/TryCatch を CompositeStep / 通常 Step として将来扱うかは未確定事項に残すべき。
  - 旧 `22.3 Stepインスタンス生成とDI` はフル csx の Step クラス生成、DI、`IDisposable` / `IAsyncDisposable` 破棄方針として必要だが、初期実装に含めるかは未確定事項に残すべき。
- 設計書へ追記すべき章候補と優先度。
  - P0: `型定義方針`。Message / Config / Step 入出力型を csx の C# 型に置くこと、`#nullable enable`、record/不変推奨、DataAnnotations / `IValidatableObject` の扱いを明記する。
  - P0: `検証仕様`。StepInput 取得、Config 取得、Step 実行前後、CompositeStep 出力、csx コンパイル結果の検証タイミング、失敗時に Step を実行しない契約、検証エラー形式を定義する。
  - P0: `信頼境界`。信頼済み csx のみを対象にし、サンドボックスを提供しないこと、未信頼コード実行の禁止、NuGet/参照 allowlist がセキュリティ境界ではないことを明記する。
  - P1: `エラー処理、retry、timeout`。`FailurePolicy` だけでなく、業務例外、retry 対象/対象外、timeout の協調キャンセル、代表エラーコードを定義する。
  - P1: `ログ、実行結果、Trace`。`ILoggerFactory`、`StepContext.Logger`、Scope、標準イベント、`WorkflowResult`、ExecutionTrace、capture/redaction policy を定義する。
  - P1: `Config snapshot / override`。初期版で型変換をユーザー Step に任せる場合でも、`EngineArguments` の不変性、path 基準、複数 Config / `--set` 統合規則を未確定事項に残す。
  - P2: `csx コンパイル詳細`。`IScriptCompiler` 抽象、NuGet lock、package source、診断、Abstractions assembly identity error を必要な粒度まで補う。

## 結果

- 旧 rev4 仕様 28 章を確認し、YAML 固有契約は削除妥当、非 YAML 契約はフル csx 方針への置換対象、将来機能は未確定事項として分類した。
- 調査中に `doc/workflow_engine_spec.md` へ Entry、csx 解決、validate、実行結果、ログ、Trace、初期実装範囲の追記差分が発生していたため、その最新状態を再確認して指摘を絞り直した。
- 移植漏れとして扱うべき指摘は 6 件。内訳は、型定義方針、Config snapshot/override/merge、検証詳細、エラー/retry/timeout、ログ/実行結果/Trace の詳細、信頼境界。
- `reports/issue-1-full-csx-design-review-20260605130833.md` は旧契約の残存を主に点検しており、削除済み旧契約のうち非 YAML 契約が現行設計へ移植されているかまでは確認対象にしていなかった。
- 更新ファイルは `reports/issue-1-spec-old-contract-gap-scan-20260606161536.md` のみ。

## リスク

- 本調査は設計書追記候補の洗い出しまでで、`doc/workflow_engine_spec.md` は編集していない。
- P0 候補を設計書へ追記しないまま実装を始めると、Message/Config 型検証、未信頼 csx の扱い、検証済み入力の範囲が実装者判断で分岐するリスクがある。
- 旧仕様の YAML 固有章に混在していた retry/timeout、trace、検証設定は、フル csx 版で API または CLI options として再設計する必要がある。
