# Sub-agent実行レポート

## タスク

- 目的: P6 として、初期版後の候補を次フェーズの task として扱える粒度に分解する。
- タスク種別: planning / investigation

## sub-agentを使う理由

- 理由: ユーザー指示により、調査と実装準備作業も sub-agent に委譲し、parent は採用判断、追跡更新、commit、push を担当するため。

## 対象範囲

- 対象: `doc/workflow_engine_spec.md` 19.3 と 21 の未確定事項を読み、P6 の次フェーズ候補を task、依存関係、完了条件、検査方針へ分解する。

## 対象外

- 対象外: 新機能の実装、設計本文の変更、`tasks-status.md` と `phases-status.md` の直接編集、既存実装の改変。

## 実行コマンド

- 実行コマンド:
  - `pwd`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/task-breakdown-planner/SKILL.md`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/report-output-manager/SKILL.md`
  - `sed -n '1,260p' reports/issue-1-post-initial-candidates-breakdown-20260606195401.md`
  - `rg -n "^(##|###) .*19\\.3|^(##|###) .*21|19\\.3|21\\." doc/workflow_engine_spec.md`
  - `sed -n '1,260p' tasks-status.md`
  - `sed -n '1,260p' phases-status.md`
  - `sed -n '1138,1255p' doc/workflow_engine_spec.md`
  - `git status --short`
  - `nl -ba reports/issue-1-post-initial-candidates-breakdown-20260606195401.md | sed -n '1,220p'`
  - `rg --files src tests`
  - `sed -n '1,240p' src/Devo6.WorkFlow.Abstractions/IStep.cs`
  - `sed -n '1,260p' src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - `sed -n '220,520p' src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - `sed -n '1,220p' src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
  - `sed -n '1,220p' src/Devo6.WorkFlow.Abstractions/EngineArguments.cs`
  - `sed -n '1,220p' src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
  - `sed -n '1,280p' src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - `sed -n '280,620p' src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - `sed -n '1,220p' src/Devo6.WorkFlow.Abstractions/StepInput.cs`
  - `sed -n '1,220p' src/Devo6.WorkFlow.Abstractions/StepContext.cs`
  - `sed -n '1,220p' src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
  - `sed -n '1,220p' tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`
  - `sed -n '1,220p' tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`
  - `sed -n '1,220p' tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
  - `npm run lint:md`
  - `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/issue-1-post-initial-candidates-breakdown-20260606195401.md`
  - `npx cspell --config cspell.config.jsonc reports/issue-1-post-initial-candidates-breakdown-20260606195401.md`
  - `npm run lint:md:whitelist -- --stdin reports/issue-1-post-initial-candidates-breakdown-20260606195401.md`
  - `sed -n '1,220p' tools/lint/run-skill-script.js`
  - `node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/run-cspell-markdown.js`
  - `node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/list-markdown-targets.js`
  - `sed -n '1,220p' .codex/skills/review-enforcer/scripts/run-cspell-markdown.js`
  - `sed -n '1,220p' .codex/skills/review-enforcer/scripts/list-markdown-targets.js`
  - `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/issue-1-post-initial-candidates-breakdown-20260606195401.md`

## 対象ファイル

- 変更または確認したファイル:
  - 変更: `reports/issue-1-post-initial-candidates-breakdown-20260606195401.md`
  - 確認: `doc/workflow_engine_spec.md`
  - 確認: `tasks-status.md`
  - 確認: `phases-status.md`
  - 確認: `src/Devo6.WorkFlow.Abstractions/IStep.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/StepInput.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/StepContext.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/EngineArguments.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
  - 確認: `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - 確認: `src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
  - 確認: `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`

## 指摘事項

- 指摘要約または「指摘なし」:
  - P6 は実装 task ではなく、初期版後候補を後続 phase に載せられる粒度へ分解する planning / investigation として扱うのが妥当。
  - `doc/workflow_engine_spec.md` 19.3 の候補は、21章の未確定事項と合わせると、実行制御、Config、値保持と trace、csx 参照、Step 名管理の5系統に分けられる。
  - `phases-status.md` 上の P6 完了条件は、非同期 Step API、timeout、標準 Config 読み込み、retry、値を含む trace、NuGet ロックファイル、`#load "nuget: ..."`、Step 名名前空間化を初期版とは別 task として扱える状態にすること。
  - 現行実装は同期 `IStep<TOut>.Execute(StepInput input)`、値を保存しない `ExecutionTrace`、`EngineArguments` による Config パスと `--set` の文字列保持、`#load "nuget: ..."` の明示拒否を前提にしている。
  - 検査結果: `npm run lint:md`、対象レポート直接指定の `textlint`、対象レポート直接指定の whitelist 検査は成功。`cspell` はリポジトリ設定の `ignorePaths` により reports 配下を skip するため、runner 経由でも `Files checked: 0, skipped: 1` だった。

## 結果

- 結果:
  - 次フェーズ task 候補 P7-T1: 非同期 Step API の契約決定と検査追加。
    - 範囲: `IAsyncStep<TOut>` 追加案と `IStep<TOut>` の `Task<TOut>` 統一案を比較し、採用する公開 API、同期 Step との共存、`CompositeStep` での実行順、例外結果化の契約を決める。
    - 依存関係: P6 完了後。timeout、retry、CLI run の非同期化はこの決定に依存する。
    - 完了条件: 採用 API が設計または task に明記され、最小の非同期 Step を `CompositeStep` から実行できる検査が失敗検査として先に置かれ、その後 `dotnet test Devo6.WorkFlow.sln` が通る。
    - TDD / E2E 方針: まず `CompositeStep` が非同期 Step を定義順に待機し、戻り値と `Produce` を下流に渡す検査を追加する。CLI E2E はこの task では必須にせず、公開 API とエンジン統合検査を優先する。
  - 次フェーズ task 候補 P7-T2: timeout と協調キャンセル。
    - 範囲: `CancellationToken` の渡し方、`WorkflowExecutionOptions` の timeout 指定、Step timeout 時の `WorkflowErrorCodes.StepTimeout`、ログと trace の状態を実装対象にする。
    - 依存関係: P7-T1。同期 API のみで timeout を先行実装する場合でも、後続の非同期 API と衝突しない契約確認が必要。
    - 完了条件: timeout 超過が失敗 `WorkflowResult` になり、後続 Step が実行されず、trace とログに timeout が残り、協調キャンセルを受け取る Step の検査が通る。
    - TDD / E2E 方針: 長時間 Step を使う単体または統合検査を先に置き、実時間待ちが長くならないよう短い timeout と制御可能な `TaskCompletionSource` 相当の検査を使う。CLI E2E は `run` が非 0 を返す経路だけ後続で追加する。
  - 次フェーズ task 候補 P7-T3: retry 実行契約。
    - 範囲: retry 回数、対象エラー、timeout との関係、attempt 番号のログ scope、trace での試行表現、成功時と失敗時の結果契約を決めて実装する。
    - 依存関係: P7-T2。少なくとも timeout と通常例外のエラー分類が決まっていること。
    - 完了条件: 失敗後に指定回数だけ再試行され、途中成功では成功結果になり、全試行失敗では最後のエラーまたは集約結果が安定して返り、trace とログに attempt が残る。
    - TDD / E2E 方針: 呼び出し回数を記録する Step で、1回失敗後成功、全失敗、retry 対象外エラーを先に検査する。CLI E2E は retry オプションを CLI に出す task が別途立つまで不要。
  - 次フェーズ task 候補 P8-T1: 標準 Config 読み込みと StepContext 格納。
    - 範囲: `--config` のパス保持から、標準 Config 読み込み、型付き取得、`StepContext` への格納、読み込み失敗時の `ConfigLoadFailed` を扱う。
    - 依存関係: CLI override の統合規則を最小限決める必要がある。複数 Config や配列 merge を後続に分けるなら単一 Config 読み込みから開始できる。
    - 完了条件: Config ファイルを読み込んだ値を Step が `StepContext` から型付き取得でき、存在しない file と読み込み不能 file が検証または実行結果で失敗になる。
    - TDD / E2E 方針: 先に CLI `run main.csx --config appsettings.yaml` の利用者目線 E2E を追加し、Step が読み込まれた Config 値を書き出す検査で確認する。型変換の境界は単体検査で補う。
  - 次フェーズ task 候補 P8-T2: CLI override の標準仕様。
    - 範囲: 入れ子キー、配列上書き、型変換、複数 Config ファイル時の統合規則を定義し、`EngineArguments.Settings` の文字列保持から標準 Config への反映経路を追加する。
    - 依存関係: P8-T1。複数 Config の統合を含める場合は標準 Config 読み込みの基本形が必要。
    - 完了条件: `--set a.b=value`、配列またはリスト値、型変換、複数 override の優先順位が検査で固定され、無効な override が明確な validation error になる。
    - TDD / E2E 方針: CLI E2E を先に置き、Config ファイル値が `--set` で上書きされる正常系と、無効書式の非 0 終了を検査する。merge 細部は純粋関数に分離できるなら単体検査で扱う。
  - 次フェーズ task 候補 P9-T1: Produce 後の値の寿命と scoping。
    - 範囲: `StepInput` に追加された値を最後まで保持する現状を維持するか、Step 範囲または明示 scope で破棄するかを決める。
    - 依存関係: 値を含む trace より先。Config の `StepContext` 格納とは独立だが、同じ値管理方針として整合確認が必要。
    - 完了条件: 値寿命の仕様が検査で固定され、保持継続または scope 破棄のどちらでも、既存 `Produce`、`StoreAs`、`Discard` の意味が壊れない。
    - TDD / E2E 方針: 複数 Step で同じ型や名前の値がいつ読めるか、いつ読めないかを `CompositeStepTests` に先に追加する。CLI E2E は不要。
  - 次フェーズ task 候補 P9-T2: 値を含む ExecutionTrace。
    - 範囲: `StepInput`、Config、Step 出力のどれを保存するか、既定で保存するか、秘匿または opt-in にするか、serialization 失敗時の `TraceSerializationFailed` を扱う。
    - 依存関係: P9-T1。Config 値を trace 対象にするなら P8-T1 以降。
    - 完了条件: trace に保存される値の種類、形式、失敗時挙動、保存しない値の規則が検査で固定される。
    - TDD / E2E 方針: Step 出力と Config 値を含む成功 trace、秘匿対象を含まない trace、serialize 不能値の失敗または除外を単体検査で先に置く。必要なら CLI run の出力形式検査は別 task にする。
  - 次フェーズ task 候補 P10-T1: NuGet ロックファイル。
    - 範囲: 現行の明示許可 NuGet 参照を、復元結果の固定、lock file の生成または検証、差分検出エラーに拡張する。
    - 依存関係: 現行 `#r "nuget: ..."` 許可経路。`#load "nuget: ..."` 対応より先に実装するのが安全。
    - 完了条件: lock file がある場合は一致する依存だけ許可され、不一致または欠落時に安定した validation error になり、既存の浮動 NuGet 版禁止が維持される。
    - TDD / E2E 方針: ネットワークに依存しない fixture または復元抽象を使い、lock 一致、不一致、欠落を先に検査する。実 NuGet 復元の E2E は環境依存を分離して任意検査にする。
  - 次フェーズ task 候補 P10-T2: `#load "nuget: ..."` 対応。
    - 範囲: 現在明示拒否している `#load "nuget: ..."` の許可条件、読み込む script の解決、root 制限との関係、lock file との関係を実装する。
    - 依存関係: P10-T1。lock なしで許可する場合でも、復元と読み込み元の信頼境界を先に決める必要がある。
    - 完了条件: 許可された NuGet script load は実行でき、未許可、浮動版、lock 不一致、循環または重複読み込みが検証で失敗する。
    - TDD / E2E 方針: `CsxEntryLoaderTests` に `#load "nuget: ..."` の許可と拒否の検査を先に置く。外部取得が必要な部分は loader の依存抽象を通して固定 fixture で確認する。
  - 次フェーズ task 候補 P11-T1: Step 名の名前空間化。
    - 範囲: public Step 名重複検出を、namespace または entry group を含む名前へ拡張し、CLI `--entry` と validation error の表示を決める。
    - 依存関係: 既存の `DuplicateStepName` 検証。Config や async とは独立。
    - 完了条件: 同名 Step が異なる namespace で共存でき、同一 namespace 内の重複は失敗し、CLI から名前空間付き Entry を指定できる。
    - TDD / E2E 方針: `.csx` 内に同名 public Step を複数置く validation 検査を先に追加し、名前空間付き指定の CLI `validate` / `run` E2E を追加する。
  - phase 分割案:
    - P7 実行制御 phase: P7-T1 非同期 Step API、P7-T2 timeout と協調キャンセル、P7-T3 retry。実行ループ、ログ scope、trace、失敗結果が密接に関係するため同一 phase がよい。
    - P8 Config phase: P8-T1 標準 Config 読み込み、P8-T2 CLI override。Config の標準構造と override merge は同一利用者体験なので同一 phase がよい。
    - P9 値と trace phase: P9-T1 Produce 値寿命、P9-T2 値を含む `ExecutionTrace`。trace 値保存は値寿命に依存するため同一 phase 内で順序付けるのがよい。
    - P10 csx 依存再現性 phase: P10-T1 NuGet ロックファイル、P10-T2 `#load "nuget: ..."`。NuGet 由来 script 読み込みは lock と信頼境界に依存するため同一 phase がよい。
    - P11 Step 名管理 phase: P11-T1 Step 名の名前空間化。既存 validation と CLI 指定に影響するが、他候補とは依存が薄いため別 phase として単独で扱うのがよい。

## リスク

- 未解決のリスクまたは後続対応:
  - 非同期 API は公開 API の破壊可能性が最も高い。`IStep<TOut>` を残すか `Task<TOut>` へ統一するかを実装前に決めないと、timeout、retry、CLI run の設計が揺れる。
  - Config 標準化は YAML 形式、型変換、複数 file merge、override 書式の決定が必要。初期版では `EngineArguments` 保持だけなので、利用者向け互換性の境界を明示する必要がある。
  - 値を含む trace は秘匿情報、巨大 object、serialize 不能値を扱うため、既定で保存するか opt-in にするかを先に決める必要がある。
  - NuGet lock と `#load "nuget: ..."` は外部取得、再現性、信頼境界、オフライン検査に影響する。通常の `dotnet test` がネットワーク依存にならない検査設計が必要。
  - Step 名名前空間化は既存の重複検出、`--entry` 指定、trace の StepName 表示を変える可能性があるため、表示名と識別子を分けるか確認が必要。
  - 今回はユーザー指示により `doc/workflow_engine_spec.md`、`tasks-status.md`、`phases-status.md` は変更していない。採用時は parent 側で追跡ファイルへ転記する必要がある。
