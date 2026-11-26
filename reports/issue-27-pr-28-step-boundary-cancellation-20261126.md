# 課題 #27: Step 呼び出し境界の外部取消

## 変更

`CompositeStep` は workflow 外部の token を `StepRegistration` の共通実行ラッパーと登録デリゲートへ分けて渡す。登録デリゲート内の利用者コードを評価した後、同期・非同期 Step 本体へ入る直前にも外部 token を確認する。取消済みなら `OperationCanceledException` を既存の捕捉経路へ渡し、`STEP_CANCELED` を返す。timeout 用の合成 token ではなく外部 token を確認するため、同期 Step の timeout 動作は変えない。

| 登録形態 | 利用者コードの順序 | 取消確認位置 |
|---|---|---|
| 型付き `Run` / `RunAsync` | `new TStep()` → `Execute` / `ExecuteAsync` | constructor の後、本体の直前 |
| `RunIf` / `TapIf` と async 版 | `when` →（true の場合）constructor → Step 本体 | 条件後、constructor 後 |
| Lambda 登録 | 登録デリゲートから利用者 body | 共通ラッパーと body 呼出し直前 |
| `BranchBuilder` 登録 | 上記と同じ `StepRegistration` factory | 同じ確認を共有 |

共通 Engine の `RunIf` / `TapIf` 条件、型付き Step constructor と、ファイルベース `.cs` の公開 `Run` 経路に回帰検査を追加した。公開 `Run` は `Create()` 完了後の条件評価または constructor をファイル gate で停止し、その間に外部取消してから gate を解除する。Step 本体の副作用印が作られないこと、取消結果と診断 session が維持されることを検査する。

## 検証

- 修正前の対照: 新しい共通 Engine の条件2件・constructor 1件と、公開 `Run` の `RunIf` / `TapIf` / constructor の計6ケースが失敗。条件後に constructor と body が実行され、constructor 中の取消後にも body が実行されることを確認。
- 修正後: `CompositeStepTests`、`TimeoutCancellationContractTests`、`FileBasedWorkflowExecutorTests`、`FileBasedCsWorkflowBuilderLifecycleTests` を合わせて55件成功。
- `CodingStandardsContractTests.CSharpDeclarationsFollowT31CodingStandards`: 1件成功。
- `dotnet build tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --no-restore --framework net8.0`: 成功、警告0、エラー0。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes --no-restore`: 成功。
- `git diff --check`: 成功。Markdown lint は `node_modules` がないため未実施。依存追加はしていない。
- SDK 10.0.404 / runtime 10.0.15 を使用。実 .NET 8 runtime は未導入のため、net8.0 の試験実行は未確認。全件ローカル試験と新 HEAD の自動 CI も未確認。

## If / Switch 選択境界の追補

分岐処理は登録 Step の共通実行 wrapper を通らず `GetBranch` を呼ぶため、利用者の条件または selector に入る直前の外部取消確認が必要だった。さらに条件・selector が `OperationCanceledException` を投げた際、外部 token が取消済みでも既存の条件評価失敗へ分類されていた。

`CompositeStep.ExecuteIfStepAsync` は設定を適用した直後、`GetBranch` の直前に外部 token を確認する。取消済みなら利用者 callback を開始しない。条件・selector が `OperationCanceledException` を投げ、外部 token も取消済みの場合は既存の `STEP_CANCELED` trace/log/result 変換を使う。外部 token が未取消の `OperationCanceledException` と通常例外は、従来の If / Switch 診断を維持する。timeout 用 token、同期 Step の timeout 判定、Build 契約は変更していない。

共通 Engine の回帰は、If / Switch の分岐開始ログを同期点にして直前取消を行い、callback が呼ばれないことを確認する。別の If / Switch 検査で、callback 内の `OperationCanceledException` と外部 token の状態に応じた `STEP_CANCELED` または従来診断を確認し、既存の通常例外試験も含める。公開 `.cs` Run では If / Switch の各々について、分岐前取消、評価中の取消例外、通常例外を通し、callback の副作用印、trace code、診断 session の保持を確認する。

- 追加した12件（共通 Engine 6件と公開 `.cs` Run 6件）: 成功。
- 関連する If / Switch、timeout、file-based executor / lifecycle 検査: 75件成功、1件既存 skip。
- T31: 1件成功。`net8.0` build: 成功、警告0、エラー0。solution format check と `git diff --check`: 成功。
- 標準 .NET 8 runtime の test 実行、全件ローカル試験、新候補の自動 CI は未確認。

## File-based entry 事前取消方針の追補

通常再確認で、FileBasedWorkflowExecutor の最終 token 確認後から共有 Engine の開始までに取消された場合、公開 Engine の既存同期 Step 特例を使う境界が確認された。公開経路の既定動作を保つため、`WorkflowExecutionOptions` に内部の `WorkflowEntryCancellationPolicy` を追加した。既定の公開 Engine は `PreservePreCancelledSynchronousStep`、file-based 実行は Config 準備後に `WithStrictCancellationPolicy` で `StopBeforeSynchronousStep` を選ぶ。これにより、Engine の呼出し開始時点ですでに token が取消済みでも file-based 経路は同期 Step を開始しない。

決定的な回帰は、Engine に事前取消済み token と厳格方針を直接渡して同期 Step の実行印が残らないこと、file-based executor の方針作成 helper が厳格方針を返し他の option を維持することを別々に確認する。公開 file-based `Run` では Entry 開始ログ中の取消、事前取消時の Provider constructor 非実行を確認する。前回修正の再発として、公開 Engine の Entry / Composite ログ中取消で同期 Step が動かないこと、および事前取消済み同期 Step の通常例外で retry・Produce・後続 Step が起きず `STEP_EXECUTION_FAILED` が保持されることも確認する。公開 Engine の事前取消時の既存同期 Step 完了契約は維持する。

- 焦点検査: 12件成功。
- 関連 .NET 10 検査: 199件成功、既存 skip 2件。
- `net8.0` 向け生成: 成功、警告0、エラー0。net8.0 対象検査: 132件成功、既存 skip 2件。いずれの実行にも .NET 10.0.15 を使用し、net8.0 対象の試験は `DOTNET_ROLL_FORWARD=Major` で実行した。標準 .NET 8 runtime は未確認。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes --no-restore` と `git diff --check`: 成功。変更した `tasks-status.md` の textlint、CSpell、許可語検査: すべて指摘0件。
- この追補は公開 `bece4808f7565f3bea935ce4da4158328caadce6` からのローカル差分であり、commit、push、自動 CI の起動は未実施。
