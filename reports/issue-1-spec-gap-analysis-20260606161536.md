# 課題 #1 仕様減量後の穴点検

## 目的

- 旧 rev4 仕様から現行の csx 完結型設計へ置換したことで、実装前に必要な利用契約が落ちていないかを点検する。
- YAML ワークフロー定義、独立 Flow、Step 専用 Config 引数の廃止で不要になった記述と、フル csx でも必要な記述を分ける。

## 対象

- `doc/workflow_engine_spec.md`
- 旧仕様 `git show cefc001:doc/workflow_engine_spec.md`
- `reports/attachments/issue-1-csx-workflow-engine-design.md`
- `reports/issue-1-full-csx-design-update-20260605130833.md`
- `reports/issue-1-full-csx-design-review-20260605130833.md`

## 判定基準

- 利用者が `.csx` ワークフローを作成、分割、実行、設定注入、失敗調査できるか。
- 実装者が初期版で何を作り、何を作らないか判断できるか。
- 旧仕様のうち YAML 構文依存ではない実行契約が残っているか。

## 親側の暫定所見

- 旧 rev4 から現行設計への置換では、YAML ワークフロー定義、独立 Flow、Step 専用 Config 引数は削除してよい。
- 一方で、利用者が `.csx` を作成して検証、実行、失敗調査するための契約も削られていた。
- 添付仕様は中核 DSL の方針を示す資料として有効だが、実装 task 分解の前提仕様としては薄い。
- `doc/workflow_engine_spec.md` に、Entry、ファイル構成、csx 参照解決、検証、実行結果、ログ、トレース、初期実装範囲、型定義方針、信頼境界を追記した。

## sub-agent 所見

- `reports/issue-1-spec-usage-gap-scan-20260606161536.md`: 利用手順観点で 8 件のブロッキング指摘。CLI、Entry、外部 `.csx`、`#load` / `#r` / NuGet、Config、`StepInput` / `StepContext`、ログ / トレース、信頼境界が不足。
- `reports/issue-1-spec-old-contract-gap-scan-20260606161536.md`: 旧仕様移植漏れ観点で 6 件のブロッキング指摘。型定義方針、Config snapshot、検証詳細、エラー / retry / timeout、ログ / 実行結果 / Trace、信頼境界が不足。
- どちらの調査も、YAML ワークフロー定義、独立 Flow、Step 専用 Config 引数の復活は不要と判定している。

## 穴候補

- Entry 選択と Step 名の一意性。
- Entry `.csx` と外部 `.csx` の標準ファイル構成。
- トップレベルステートメントの副作用制限。
- `#load`、`#r`、NuGet の解決規則と許可境界。
- AssemblyLoadContext と公開 API assembly の同一性。
- コンパイルキャッシュのキー。
- 検証コマンドと検証対象。
- `StepInput`、Config、Step 出力の検証。
- 型定義方針と Config のスナップショット扱い。
- `WorkflowResult`、エラーコード、ログ、`ExecutionTrace`。
- 初期実装範囲と次フェーズ候補。
- 未信頼 `.csx` の扱いとサンドボックス非提供の明示。

## 設計追記方針

- 旧仕様の YAML 固有構文は戻さない。
- `workflow.yaml`、`Flow`、`next`、binding 式、Step 専用 Config 引数は復活させない。
- 旧仕様で YAML と混在していた非 YAML 契約は、`main.csx`、`CompositeStep`、`StepInput`、`StepContext` を中心に再定義する。
- 初期版で作るものと作らないものを `## 19. 初期実装範囲` に明記する。
- 未確定のまま実装者判断にしたくない項目は、初期版対象外または次フェーズ候補として明示する。

## 検証

- `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md`: 成功。
- `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md:terms`: 成功。`SudachiPy term variants: none`
- `git diff --check`: 成功。

## 結論

- 文章量減少への懸念は正当だった。
- 中核 DSL 方針は残っていたが、利用契約と実装判断に必要な周辺仕様が落ちていた。
- 現時点では、設計書へ実行入口、参照解決、検証、診断、信頼境界、初期実装範囲を追記し、実装 task 分解に進める前提を補強した。
