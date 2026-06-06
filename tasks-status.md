# task 状態

| task | 状態 | 範囲 | 完了条件 | 根拠 |
| --- | --- | --- | --- | --- |
| T0 | 完了 | 最低限の作業管理ファイルを用意する。 | `AGENTS.md`、`tasks-status.md`、`phases-status.md`、`reports/` が存在する。 | このファイル、`AGENTS.md`、`phases-status.md`、`reports/.gitkeep` |
| T1 | 完了 | Markdown lint の実行準備を行う。 | `npm run lint:md` の入口と repo 固有設定が存在する。 | `package.json`、`tools/lint/`、`.textlintrc.json`、`cspell.config.jsonc`、`npm run lint:md` |
| T2 | 完了 | `doc/workflow_engine_spec.md` の Markdown lint 用語候補を収集する。 | 設計書から未許可語を抽出し、確認用の報告に記録する。 | `reports/task-markdown-lint-terms-20260602152805.md` |
| T3 | 完了 | `doc/workflow_engine_spec.md` の Markdown lint 用語候補を分類する。 | 設計機能名として許可する英語、本文を日本語化する語、確認が必要な語を分けて記録する。 | `reports/task-markdown-lint-term-classification-20260602154500.md` |
| T4 | 完了 | 分類済みの設計機能名を whitelist に反映する。 | 許可対象の英語設計語と片仮名語が `tools/lint/markdown-whitelist.yaml` に登録され、whitelist 自己検査が通る。 | `tools/lint/markdown-whitelist.yaml`、`npm run lint:md:whitelist -- --stdin tools/lint/markdown-whitelist.yaml` |
| T5 | 完了 | `doc/workflow_engine_spec.md` を Markdown lint に通す。 | 設計書が通常の `npm run lint:md` 対象に含まれ、全文 lint が通る。 | `doc/workflow_engine_spec.md`、`tools/lint/markdown-whitelist.yaml`、`npm run lint:md` |
| T6 | 完了 | SudachiPy で使用用語の揺れ候補を検知する入口を追加する。 | `npm run lint:md:terms` で正規形ごとの表層揺れ候補を確認できる。 | `tools/lint/check-sudachi-term-variants.py`、`npm run lint:md:terms` |
| T7 | 完了 | `doc/workflow_engine_spec.md` から実装 task を分解する。 | 具体的な実装 task と順序がこのファイルに記録される。 | `doc/workflow_engine_spec.md`、`reports/task-breakdown-tracking-update-20260606165027.md`、`reports/task-breakdown-tdd-e2e-update-20260606170118.md` |
| T8 | 完了 | 課題 #1 添付設計を参照し、設計書を csx 完結型に更新する。 | YAML ワークフロー定義中心の設計が削除され、`CompositeStep`、`StepInput`、`StepContext` を中心にした設計へ置換され、Markdown lint と点検が通る。 | `doc/workflow_engine_spec.md`、`tools/lint/markdown-whitelist.yaml`、`reports/issue-1-full-csx-design-update-20260605130833.md`、`reports/issue-1-full-csx-design-review-20260605130833.md`、`npm run lint:md`、`npm run lint:md:terms` |
| T9 | 完了 | 仕様減量で落ちた利用契約を点検し、設計書を補強する。 | Entry、`.csx` 解決、検証、実行結果、ログ、トレース、信頼境界、初期実装範囲が設計書に追記され、Markdown lint と表記揺れ検査が通る。 | `doc/workflow_engine_spec.md`、`reports/issue-1-spec-gap-analysis-20260606161536.md`、`reports/issue-1-spec-usage-gap-scan-20260606161536.md`、`reports/issue-1-spec-old-contract-gap-scan-20260606161536.md`、`npm run lint:md`、`npm run lint:md:terms` |
| T10 | 未着手 | P2 完了後、.NET の最小構成と中核、CLI、検査用プロジェクトの骨格を作る。 | 検査先行で進め、骨格上 E2E が成立しにくい箇所は利用者目線の検査設計または最小公開 API の失敗検査を先に置く。`dotnet build` と空または最小検査が通り、以後の実装を配置できるプロジェクト参照が成立する。 | `doc/workflow_engine_spec.md` 19.1、P3 |
| T11 | 未着手 | T10 完了後、公開 API 基盤として `IStep<TOut>`、`StepInput`、`StepContext`、`Unit`、値キーを実装する。 | 検査先行で進め、公開 API の失敗検査を先に置く。型付き取得、名前付き取得、`StepInput` の重複登録失敗、`StepContext` の明示上書きが検査で確認される。 | `doc/workflow_engine_spec.md` 4、5、14、P3 |
| T12 | 未着手 | T11 完了後、`CompositeStep` の逐次実行と値渡し API を実装する。 | 検査先行で進め、可能な範囲で利用者目線の統合検査を先に置く。`Run`、`Produce`、名前付き `Produce`、`StoreAs`、`Discard`、Step 実行順が検査で確認される。 | `doc/workflow_engine_spec.md` 7、13、19.1、P3 |
| T13 | 未着手 | T12 完了後、実行結果、検証エラー、基本エラーコード、ログ、トレースの初期契約を追加する。 | 検査先行で進め、結果契約とログ、トレースの失敗検査を先に置く。`WorkflowResult`、`ValidationError`、基本エラーコード、値を含まない `ExecutionTrace`、`Microsoft.Extensions.Logging` 連携が検査で確認される。 | `doc/workflow_engine_spec.md` 11、17.6、18、19.1、P3 |
| T14 | 未着手 | T13 完了後、`.csx` Entry 読み込み入口と `Dotnet.Script.Core` 統合の最小経路を実装する。 | 検査先行で進め、サンプル Entry を使う E2E または利用者目線の統合検査を先に置く。Entry `.csx` をロードし、既定 `Main` または指定 Entry の名前付き `CompositeStep` を取得でき、ロード失敗とコンパイル失敗が失敗結果になる。 | `doc/workflow_engine_spec.md` 9、10、15、16.1、P4 |
| T15 | 未着手 | T14 完了後、ローカル `#load` と明示許可された `#r` / NuGet 参照の検証を実装する。 | 検査先行で進め、複数 `.csx` と参照解決の利用者目線の統合検査を先に置く。Entry `.csx` 基準と `#load` 記述元基準の相対パス、root 制限、循環、重複読み込み、許可外参照、浮動 NuGet 版禁止が検査で確認される。 | `doc/workflow_engine_spec.md` 15.3、16.2、16.3、P4 |
| T16 | 未着手 | T15 完了後、実行前 `validate` 処理を実装する。 | 検査先行で進め、`validate` の E2E または利用者目線の統合検査を先に置く。Entry 存在、Entry 名存在、公開 Step 名重複、参照解決、コンパイル、API 同一性、Config ファイル存在の検証結果が `ValidationError` として返る。 | `doc/workflow_engine_spec.md` 15.1、15.2、16.4、17、P4 |
| T17 | 未着手 | T16 完了後、CLI の `run` / `validate` と `EngineArguments` 保持を実装する。 | 検査先行で進め、CLI 利用者目線の E2E を先に置く。`engine run`、`engine validate`、`--entry`、`--config`、複数 `--set` が解析され、Config パスは Entry `.csx` ディレクトリ基準で保持され、成功時 0 / 失敗時非 0 の終了コードが検査で確認される。 | `doc/workflow_engine_spec.md` 6.2、6.5、6.6、15.1、17.1、18.1、P5 |
| T18 | 未着手 | T17 完了後、初期版の統合検証と点検根拠をそろえる。 | 検査先行で進め、サンプル `.csx` の `run` / `validate` E2E を先に置く。Markdown lint、表記揺れ検査、`dotnet test`、点検報告が通り、初期版対象外と未確定事項が実装に混入していないことを記録する。 | `doc/workflow_engine_spec.md` 19.2、19.3、21、P5 |
