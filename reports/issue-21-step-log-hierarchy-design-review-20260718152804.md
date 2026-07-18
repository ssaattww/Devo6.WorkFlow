# 課題 #21 Step ログ階層表示設計レビュー

## 1. task

課題 #21 の Step 名付きログと実行階層表示について、追加した設計書が現行実装、既存公開契約、CLI 出力契約と整合するかを点検する。

## 2. 点検対象

- `doc/issue-21-step-log-hierarchy-design.md`
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `src/Devo6.WorkFlow.Abstractions/StepContext.cs`
- `src/Devo6.WorkFlow.Cli/EngineLoggingProvider.cs`
- `src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
- `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`
- `samples/multi-folder-composite/main.csx`
- `samples/multi-folder-composite/README.md`

## 3. 点検観点

- 課題 #21 の必須要件である実行中 Step 名を確認できるか
- nested CompositeStep の親子関係を表現できるか
- `If` と `Switch` の選択分岐を過不足なく表現できるか
- retry、timeout、キャンセルの既存契約と矛盾しないか
- `StepContext.Logger` の利用者ログにも同じ実行位置を付与できるか
- CLI の Text 形式と JSON 形式が同じ構造を利用するか
- ログファイル名の `{RootStepName}` が変化しないか
- 入力、出力、Config、Switch case 値を意図せず記録しないか
- 公開 Step API、`WorkflowResult`、`ExecutionTrace` を変更せず実装できるか
- 実装 task と検査観点が分離されているか

## 4. 現行実装との整合

### 4.1 Step 名

現行の `StepRegistration` は、クラス Step では型名、Lambda Step では API に渡した名前、`If` と `Switch` では制御単位名を保持する。

設計書は既存の `StepRegistration.Name` を利用し、新しい公開命名 API を要求しない。現行モデルと整合する。

### 4.2 root workflow のスコープ

現行の `ExecuteWorkflowAsync` は Entry と Step のスコープを既に作成している。CLI provider が `EntryName` だけを取得し、`StepName` と `Attempt` を出力しないことが、Step 名を確認しにくい直接原因である。

設計書は既存スコープを廃止せず、Entry、Step、試行の責務を整理した上で CLI provider がスコープ chain を読む方針としている。変更範囲は妥当である。

### 4.3 nested CompositeStep

現行の `CompositeStep.Execute` と `ExecuteAsync` は、同じ `StepInput` を使って内側 Step 列を実行できるが、単純実行経路は Step ごとの logger スコープとライフサイクルログを作らない。

設計書は `CompositeName` と内側 `StepName` のスコープを追加し、外側 workflow の Entry と Step のスコープを継承する。複数フォルダサンプルの `Main > RunTextPipelineStep > TextPipeline > LoadTextStep` を表現できる。

### 4.4 分岐

現行の `BranchExecutionPlan` は Step 列と開始 Step index を保持するが、分岐名を保持しない。

設計書は内部実行計画へ `BranchName` を追加し、選択された Step 列の再帰実行中だけ分岐スコープを有効にする。未選択分岐をログへ出さない既存 trace 方針とも一致する。

Switch の case 値を直接文字列化せず、`case[n]` を使う判断は、任意型の副作用と機密値混入を避けるため妥当である。

### 4.5 retry と試行番号

現行 workflow 経路は Step ごとの retry 試行を持ち、成功後処理でも成功した試行番号を利用する。

設計書は `StepName` と `Attempt` を同じスコープ node の情報として扱う。nested CompositeStep の内側 Step が外側 Step の試行番号を誤って継承しない規則も定めており、現在 Step の解決方法として妥当である。

### 4.6 CLI provider

現行 `EngineLoggingScopeState` は `AsyncLocal` で親スコープを保持し、`EntryName` を chain から検索できる。

設計書の snapshot は、既存の親 node を外側から内側へ並べ直して既知 key を抽出する方式であり、provider の基本構造を維持できる。別の深さカウンターを導入しないため、例外時の破棄と非同期継続にも適合しやすい。

## 5. 出力契約の点検

### 5.1 Text 形式

既存の時刻、レベル、category を維持し、category と本文の間へ `[実行パス]` を追加する設計となっている。

Step 名をログ本文へ個別に重複させず、Entry ログ、エンジン Step ログ、Step 本体ログを同じ表示規則へ揃えられる。

ログ全文を固定解析する利用者への影響はあるが、設計書で JSON 形式または structured logging provider の利用を案内している。互換性の記載は十分である。

### 5.2 JSON 形式

既存の `Timestamp`、`Level`、`Category`、`Message`、`Exception` を維持し、`EntryName`、`StepName`、`Attempt`、`ExecutionPath` を追加する。

実行階層を配列として保持するため、Text の区切り文字を再解析する必要がない。null と空配列の規則も定義されており、field 構造を検査で固定できる。

### 5.3 ログファイル名

`{RootStepName}` は root Entry の `EntryName` から解決し、nested CompositeStep または現在 Step で切り替えない。

現行の `Main.log` 相当の命名を維持できるため、既存ファイル出力契約と整合する。

## 6. 公開契約と機密情報

設計書は次を変更対象外としている。

- `IStep<TOut>` と `IAsyncStep<TOut>`
- `StepInput` と `StepContext`
- `CompositeStep` の公開連鎖 API
- `WorkflowResult`
- `ExecutionTrace` と `ExecutionTraceStep`

スコープ key と分岐実行計画は内部変更に限定される。

入力、出力、Config、selector 結果、Switch case 値を自動記録しない。新たにログへ追加する値は、workflow 定義上の名前、分岐の登録順識別子、試行番号に限定される。初期対応として妥当である。

## 7. task 分解の点検

設計、Engine の root と nested 実行、分岐と retry、CLI formatter、文書と統合検証に分割されている。

- T73 は設計だけを確定する
- T74 は Engine の基本スコープと nested CompositeStep を扱う
- T75 は分岐、retry、timeout、キャンセルを統合する
- T76 は CLI Text と JSON 出力を扱う
- T77 は利用者文書、サンプル、全体検証、進捗同期を扱う

一つの task に Engine、CLI、文書更新を集中させず、検査先行で段階的に進められる構成である。

## 8. 非阻害リスク

### 8.1 スコープ key の綴りずれ

Engine と CLI は別 assembly であり、初期対応では公開定数型を追加しない。このため、`EntryName`、`CompositeName`、`StepName`、`BranchName`、`Attempt` の綴りずれが実装時リスクとなる。

実装では各 assembly 内で定数へ集約し、CLI 統合検査で全 key の伝達を固定することを推奨する。公開 API を増やす必要はない。

### 8.2 ログ量の増加

nested CompositeStep と単純実行 Step の開始、成功、失敗ログが追加されるため、ネスト構成のログ行数は増える。

これは Step 実装固有のログがなくても現在位置を確認するための意図した変更である。実装時は同じ境界の二重ログがないことを検査する必要がある。

### 8.3 外部 provider の表示差

外部 logger provider がスコープを表示しない設定の場合、追加情報は画面へ出ない可能性がある。

エンジンは構造化スコープを提供し、表示方法は provider 設定に委ねる方針で妥当である。同梱 CLI では Text と JSON の表示を保証する。

### 8.4 Text 形式の区切り文字

Step 名に `>` が含まれる場合、Text の実行パスは機械的に一意復元できない。

設計書は Text を人向け表示とし、機械処理には JSON 配列またはスコープを使うと定めているため、初期対応の阻害事項ではない。

## 9. 検証記録

GitHub コネクタを使い、課題 #21、現行 `CompositeStep.cs`、`StepContext.cs`、`EngineLoggingProvider.cs`、関連検査、複数フォルダサンプルを確認した。

追加設計書について、次を目視点検した。

- 見出しが連番で構成されている
- 表の列数が一致している
- コード囲みの開始と終了が対応している
- Text と JSON の例が同じ実行パス規則を示している
- task 分解と受入条件が設計本文に対応している
- 入力、出力、Config、case 値を自動記録しない方針が一貫している

この変更は設計書とレビュー記録だけであり、C# 実装変更はないため `dotnet test` は実行していない。

コネクタ経由の文書作成であり、ローカルの npm 依存を利用できないため `npm run lint:md` と `npm run lint:md:terms` は実行していない。実装 task または CI で確認する必要がある。

## 10. 結果

阻害指摘なし。

課題 #21 の要求に対して、構造化 logger スコープを正本とし、通常 Step、Step 本体ログ、nested CompositeStep、選択分岐、retry を一つの実行パスへ統合する設計は妥当である。

公開 Step API、実行結果、trace、ログファイル名を維持しながら、CLI の Text 形式と JSON 形式へ必要な情報を追加できる。
