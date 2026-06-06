# Sub-agent実行レポート

## タスク

- 目的: csx 完結型の現行設計を、利用者が `.csx` ワークフローを作成、検証、実行、失敗調査する流れで点検し、仕様の穴を洗い出す。
- タスク種別: investigation

## sub-agentを使う理由

- 理由: 親側の旧仕様差分点検と並行し、実際の利用手順から見た不足を独立観点で確認するため。

## 対象範囲

- 対象: `doc/workflow_engine_spec.md`
- 対象: `reports/attachments/issue-1-csx-workflow-engine-design.md`
- 対象: 旧仕様 `git show cefc001:doc/workflow_engine_spec.md`

## 対象外

- 対象外: ファイル編集、実装コード作成、コミット作成、PR作成。

## 実行コマンド

- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-doc-maintainer/SKILL.md`
- `sed -n '1,260p' reports/issue-1-spec-usage-gap-scan-20260606161536.md`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1,260p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '261,620p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '621,900p'`
- `nl -ba reports/attachments/issue-1-csx-workflow-engine-design.md | sed -n '1,260p'`
- `nl -ba reports/attachments/issue-1-csx-workflow-engine-design.md | sed -n '261,620p'`
- `nl -ba reports/attachments/issue-1-csx-workflow-engine-design.md | sed -n '621,900p'`
- `rg -n "CLI|entry|Entry|validate|run|Step\\(|CompositeStep|StepContext|StepInput|Dotnet.Script.Core|#load|#r|NuGet|ログ|trace|Trace|エラー|Error|Result|結果|信頼|Config|登録|name|Run<|Produce|Validate" doc/workflow_engine_spec.md reports/attachments/issue-1-csx-workflow-engine-design.md`
- `git show cefc001:doc/workflow_engine_spec.md | nl -ba | sed -n '1,260p'`
- `git show cefc001:doc/workflow_engine_spec.md | nl -ba | sed -n '261,620p'`
- `git show cefc001:doc/workflow_engine_spec.md | nl -ba | sed -n '621,980p'`
- `git show cefc001:doc/workflow_engine_spec.md | rg -n "CLI|validate|run|entry|entryFlow|Step ID|Step ID|StepContext|Logger|ExecutionTrace|trace|エラー|Error|RunResult|Result|信頼|sandbox|#load|#r|NuGet|scriptOptions|allow|Config|Workflow Config|Step Config"`
- `git show cefc001:doc/workflow_engine_spec.md | nl -ba | sed -n '1780,1868p'`
- `git show cefc001:doc/workflow_engine_spec.md | nl -ba | sed -n '1900,2120p'`
- `git show cefc001:doc/workflow_engine_spec.md | nl -ba | sed -n '2248,2580p'`
- `git show cefc001:doc/workflow_engine_spec.md | nl -ba | sed -n '2580,2705p'`
- `git status --short -- reports/issue-1-spec-usage-gap-scan-20260606161536.md`
- `git diff --check -- reports/issue-1-spec-usage-gap-scan-20260606161536.md`
- `git diff -- reports/issue-1-spec-usage-gap-scan-20260606161536.md`

## 対象ファイル

- 読了: `/home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- 読了: `/home/ibis/AI/CodexSkill/skills/design-doc-maintainer/SKILL.md`
- 読了・更新: `reports/issue-1-spec-usage-gap-scan-20260606161536.md`
- 点検: `doc/workflow_engine_spec.md`
- 点検: `reports/attachments/issue-1-csx-workflow-engine-design.md`
- 比較参照: `cefc001:doc/workflow_engine_spec.md`

## 指摘事項

- 指摘1: Blocking. CLI の `run` / `validate` / entry 指定契約が不足している。現行設計は Config 指定例として `engine run main.csx --config appsettings.json` を示すだけで、`validate` コマンド、entry 指定オプション、デフォルト entry、終了コード、標準出力の形式、検証時に Step 実行を行うかの境界を定義していない（`doc/workflow_engine_spec.md:305`, `doc/workflow_engine_spec.md:313`, `doc/workflow_engine_spec.md:516`）。旧仕様には `run` / `validate` と検証対象の列挙があったため（`cefc001:doc/workflow_engine_spec.md:2580`）、csx 完結型でも利用者が `main.csx` を作った直後に何を実行すればよいかを決める契約が必要。

- 指摘2: Blocking. entry 選択と Step 登録名の規則が未確定のまま残っている。現行設計は `Step("Main")` の例と「指定された Step を実行する」という処理順を示すが（`doc/workflow_engine_spec.md:47`, `doc/workflow_engine_spec.md:525`）、複数の名前付き `CompositeStep` がある場合の選択方法、`Main` の既定扱い、名前の大小文字、予約名、同名定義、外部 `.csx` から読み込んだ定義との衝突は未確定事項に残っている（`doc/workflow_engine_spec.md:793`）。利用者が `#load` で分割した時点で entry 解決が不安定になる。

- 指摘3: Blocking. `main.csx` と外部 `.csx` の公開形が不足している。現行設計は `#load "./build.csx"` の例と `Dotnet.Script.Core` 利用を示すだけで（`doc/workflow_engine_spec.md:488`, `doc/workflow_engine_spec.md:503`）、`main.csx` がどの API を呼ぶと CompositeStep が登録されるのか、外部 `.csx` は登録だけを行うのか、トップレベル副作用を許すのか、validate 時に script 評価が副作用を起こし得ることをどう扱うのかが定義されていない。旧仕様は合成 entry や Step csx の副作用注意を定義していたが（`cefc001:doc/workflow_engine_spec.md:2357`, `cefc001:doc/workflow_engine_spec.md:2379`）、現行の csx 完結型では利用者作成の `main.csx` が entry になるため、この契約を置き換えて定義する必要がある。

- 指摘4: Blocking. `#load` / `#r` / NuGet の解決・許可境界が不足している。現行設計は `Dotnet.Script.Core` で `.csx`、NuGet、`#r "nuget: ..."`、`#load` を扱うとだけ述べる（`doc/workflow_engine_spec.md:507`）。相対パス基準、root 外参照、循環読み込み、重複読み込み、assembly `#r` の許可リスト、ファイルパス `#r`、NuGet パッケージ ID / version / source の制御、`Workflow.Abstractions` の同一性は未定義。旧仕様にはこれらの参照方針があった（`cefc001:doc/workflow_engine_spec.md:2412`, `cefc001:doc/workflow_engine_spec.md:2434`, `cefc001:doc/workflow_engine_spec.md:2455`）。利用者が外部 csx や NuGet を使う通常経路で validate の成否が決められない。

- 指摘5: Blocking. Config 入力と `StepContext` 格納の実行時契約が不足している。現行設計は Config を `.csx` 内生成値、エンジン引数、環境変数、任意の設定ファイルから生成できるとし、エンジン読み込みとユーザー Step 読み込みの両方を許可する（`doc/workflow_engine_spec.md:252`, `doc/workflow_engine_spec.md:276`）。しかし `--config` がどの型に bind されるか、`EngineArguments` の公開 API、`ConfigStore` の役割、複数 Config、`--set` のパス記法・型変換・配列・不明キー、Config 読み込み Step とエンジン読み込みを併用した場合の優先順位が未確定（`doc/workflow_engine_spec.md:305`, `doc/workflow_engine_spec.md:778`, `doc/workflow_engine_spec.md:786`）。Step 専用 Config 引数を復活させず、Run 単位の Config を `StepContext` に置く前提のまま詳細化が必要。

- 指摘6: Blocking. `StepInput` / `StepContext` の失敗原因を利用者へ返す契約が不足している。現行設計は `Get<T>()` / `TryGet<T>()` と型・名前キーを示すが（`doc/workflow_engine_spec.md:121`, `doc/workflow_engine_spec.md:139`, `doc/workflow_engine_spec.md:203`）、値がない場合の例外型、エラーコード、対象 key の表示、同じ Type + name の上書き可否、`Set<T>` の衝突規則、named key の比較規則、`Produce` 後の値の寿命は定義されていない（`doc/workflow_engine_spec.md:552`, `doc/workflow_engine_spec.md:801`）。`validate` / `run` 後に「どの Step がどの入力を見つけられなかったか」を確認する契約が必要。

- 指摘7: Blocking. 実行結果、ログ、トレースの利用者向け契約が不足している。現行設計は `StepContext` の用途に「記録出力」を含めるだけで（`doc/workflow_engine_spec.md:178`）、公開 API 案には `Logger`、Run ID、Step ID、結果オブジェクト、Trace、標準エラー出力、失敗時の診断情報がない（`doc/workflow_engine_spec.md:711`）。旧仕様は `Microsoft.Extensions.Logging`、`StepContext.Logger`、構造化ログ、`WorkflowResult`、`ExecutionTrace`、capture / redaction 方針を持っていた（`cefc001:doc/workflow_engine_spec.md:1903`, `cefc001:doc/workflow_engine_spec.md:2013`）。利用者が run 失敗後に原因を追う手順が成立しない。

- 指摘8: Blocking. 信頼境界と参照許可の説明が現行設計から落ちている。現行設計は `Dotnet.Script.Core`、`#load`、`#r "nuget: ..."` を採用するが（`doc/workflow_engine_spec.md:503`）、csx が任意 C# コードであること、未信頼 `main.csx` を実行対象にしないこと、サンドボックスを提供しないこと、NuGet / assembly 参照許可が安全境界ではないことを明記していない。旧仕様は信頼済み Workflow 限定、未提供のサンドボックス機能、参照方針の限界を明示していた（`cefc001:doc/workflow_engine_spec.md:2543`）。利用者の実行手順に関わる安全前提なので、実装前に公開契約として必要。

## 結果

- 指摘件数: 8件。
- ブロッキング: あり。`main.csx` 作成から `#load`、Config 渡し、entry 指定、`validate` / `run`、失敗原因確認までの通常手順で、公開契約が不足している項目が複数ある。
- 設計影響: 公開 CLI、csx DSL、Config、StepInput / StepContext、参照解決、診断、信頼境界の契約に関わるため、実装前に `doc/workflow_engine_spec.md` の設計更新対象として扱う必要がある。
- 対象外確認: YAML ワークフロー定義、独立 Flow、Step 専用 Config 引数の復活は提案していない。

## リスク

- 現行設計のまま実装へ進むと、CLI や entry 選択が実装ごとに分岐し、利用者が同じ `main.csx` を validate / run できる保証が弱くなる。
- `#load` / `#r` / NuGet の許可境界が曖昧なままだと、外部 csx 分割やパッケージ利用の失敗が validate 時に再現可能なエラーとして扱えない。
- 実行結果、ログ、Trace、エラーコードが不足したままだと、StepInput 欠落、Config bind 失敗、csx load 失敗、NuGet 復元失敗を利用者が切り分けられない。
- 信頼境界を明示しないまま `Dotnet.Script.Core` と NuGet を公開手順に入れると、未信頼 csx 実行や過剰な参照許可を利用者が安全な機能と誤解する可能性がある。
