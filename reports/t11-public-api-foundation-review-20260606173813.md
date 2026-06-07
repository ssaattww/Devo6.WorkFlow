# Sub-agent実行レポート

## タスク

- 目的: T11 の公開 API 基盤実装をレビューする。
- タスク種別: review

## sub-agentを使う理由

- 理由: review は `review-enforcer` と `codex-delegation-executor` のルールで sub-agent 実行が必須であり、ユーザーも sub-agent 利用を要求しているため。

## 対象範囲

- 対象: `src/Devo6.WorkFlow.Abstractions/`、`tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs`、関連 project 参照、`reports/t11-public-api-foundation-implementation-20260606173813.md`

## 対象外

- 対象外: `CompositeStep` 実行、Step 間の値渡し、csx 読み込み、CLI 引数処理、Config YAML 処理、設計書本文変更、lint 設定変更。

## 実行コマンド

- 実行コマンド:
  - 差分確認: `git diff -- src/Devo6.WorkFlow.Abstractions tests/Devo6.WorkFlow.Tests reports/t11-public-api-foundation-implementation-20260606173813.md reports/t11-public-api-foundation-review-20260606173813.md`
    - 結果: tracked 変更として project 参照差分を確認。新規 API、検査、report は未追跡ファイルとして別途確認。
  - 周辺確認: `sed` / `rg` / `nl` による `AGENTS.md`、`tasks-status.md`、`phases-status.md`、`doc/workflow_engine_spec.md` 4、5、14.1-14.6、関連実装、検査、実装 report の確認。
    - 結果: 確認完了。
  - build 確認: `DOTNET_CLI_HOME=/tmp/devo6-dotnet-cli-home NUGET_PACKAGES=/tmp/devo6-workflow-nuget MSBUILDDISABLENODEREUSE=1 dotnet build Devo6.WorkFlow.sln --disable-build-servers`
    - 結果: 成功。warning 0、error 0。
  - test 確認: `DOTNET_CLI_HOME=/tmp/devo6-dotnet-cli-home NUGET_PACKAGES=/tmp/devo6-workflow-nuget MSBUILDDISABLENODEREUSE=1 dotnet test Devo6.WorkFlow.sln --disable-build-servers`
    - 結果: 成功。10 件通過。
  - Markdown 確認: `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md`
    - 結果: 成功。
  - Markdown 確認: `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md:terms`
    - 結果: 成功。`SudachiPy term variants: none`。
  - 差分確認: `git diff --check`
    - 結果: 成功。
  - 再レビュー差分確認: `git diff -- src/Devo6.WorkFlow.Abstractions tests/Devo6.WorkFlow.Tests reports/t11-public-api-foundation-implementation-20260606173813.md reports/t11-public-api-foundation-review-20260606173813.md`
    - 結果: tracked 変更として project 参照差分を確認。新規 API、`AssemblyInfo.cs`、検査、report は未追跡ファイルとして別途確認。
  - 再レビュー周辺確認: `sed` / `rg` / `nl` による `AGENTS.md`、`doc/workflow_engine_spec.md` 4.4 / 14.2、`StepInput`、`StepContext`、`AssemblyInfo.cs`、`PublicApiFoundationTests.cs`、実装 report、review report の確認。
    - 結果: 確認完了。
  - 再レビュー build 確認: `DOTNET_CLI_HOME=/tmp/devo6-dotnet-cli-home NUGET_PACKAGES=/tmp/devo6-workflow-nuget MSBUILDDISABLENODEREUSE=1 dotnet build Devo6.WorkFlow.sln --disable-build-servers`
    - 結果: 成功。warning 0、error 0。
  - 再レビュー test 確認: `DOTNET_CLI_HOME=/tmp/devo6-dotnet-cli-home NUGET_PACKAGES=/tmp/devo6-workflow-nuget MSBUILDDISABLENODEREUSE=1 dotnet test Devo6.WorkFlow.sln --disable-build-servers`
    - 結果: 成功。11 件通過。
  - 再レビュー Markdown 確認: `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md`
    - 結果: 成功。通常の Markdown 対象 5 件で指摘なし。report 個別の focused lint 入口は repo に存在しないため full lint 結果を記録。
  - 再レビュー Markdown 確認: `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md:terms`
    - 結果: 成功。`SudachiPy term variants: none`。
  - 再レビュー差分確認: `git diff --check`
    - 結果: 成功。

## 対象ファイル

- 変更または確認したファイル:
  - `src/Devo6.WorkFlow.Abstractions/Devo6.WorkFlow.Abstractions.csproj`
  - `src/Devo6.WorkFlow.Abstractions/IStep.cs`
  - `src/Devo6.WorkFlow.Abstractions/StepInput.cs`
  - `src/Devo6.WorkFlow.Abstractions/StepContext.cs`
  - `src/Devo6.WorkFlow.Abstractions/StepValueKey.cs`
  - `src/Devo6.WorkFlow.Abstractions/Unit.cs`
  - `tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj`
  - `tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs`
  - `reports/t11-public-api-foundation-implementation-20260606173813.md`
  - `reports/t11-public-api-foundation-review-20260606173813.md`
  - `AGENTS.md`
  - `tasks-status.md`
  - `phases-status.md`
  - `doc/workflow_engine_spec.md`
  - `src/Devo6.WorkFlow.Abstractions/AssemblyInfo.cs`
  - `package.json`
  - `tools/lint/README.md`

## 指摘事項

- 指摘要約または「指摘なし」:
  - Blocking: `StepInput.Add` を公開 API として出しているため、ユーザー Step が `Execute(StepInput input)` 内で `input.Add(...)` を直接呼べる。設計書 4.4 / 14.2 の `StepInput` 公開 API 案は `Context`、`Get`、`TryGet` のみで、値追加は `CompositeStep` が `Produce` に従って行う契約になっている。現状の公開 API だと、後続 T12 の `CompositeStep` 実装時に「Step 間の値渡しは CompositeStep 定義内の `Produce` で明示する」という契約を Step 実装側から迂回でき、重複登録失敗もエンジン制御下の `Produce` ではなく任意の Step 実装から発生し得る。T11 の重複登録失敗検査は必要だが、公開 `Add` ではなく internal 登録経路、factory、または後続 `CompositeStep` 側の登録 API で担保する方がよい。参照: `src/Devo6.WorkFlow.Abstractions/StepInput.cs:20`、`tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs:10`、`doc/workflow_engine_spec.md:143`、`doc/workflow_engine_spec.md:724`、`doc/workflow_engine_spec.md:358`、`doc/workflow_engine_spec.md:366`、`doc/workflow_engine_spec.md:878`。
  - 再レビュー対応状況: 解消済み。`StepInput.Add` は `internal` になり、public member は `Context`、`Get`、`TryGet`、constructor に限定されている。`tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs` に `Add` が公開 API に含まれないことを固定する reflection 検査が追加されている。`InternalsVisibleTo` は `Devo6.WorkFlow.Engine` と `Devo6.WorkFlow.Tests` のみで、T12 の Engine 実装経路と現在の検査経路に対して妥当。
  - 再レビュー指摘: 指摘なし。

## 結果

- 結果:
  - レビュー結果: blocking finding 1 件。
  - T11 の型付き取得、名前付き取得、未登録時、無効 name、`StepValueKey` の Type + name 等価性、`StepContext` の明示上書き、既定 logger、logging package 参照、nullable / null value の通常利用範囲、build/test/lint の確認では、上記 blocking 以外の追加指摘はなし。
  - 実装範囲は `CompositeStep`、Step 間の値渡し、csx、CLI、Config を直接実装しておらず、T11 の型群と検査に概ね限定されている。
  - 再レビュー結果: 前回 blocking finding は解消済み。新規 finding なし。
  - 再レビュー確認結果: `StepInput` の公開 API は設計書 4.4 / 14.2 の `Context`、`Get`、`TryGet` に収まっている。internal 登録経路は `StepInput` 内に残り、重複登録失敗検査は維持されている。`StepContext.Set` の明示上書き検査も維持されている。T11 範囲外の `CompositeStep` 実行、Step 間の値渡し、csx、CLI、Config 実装は追加されていない。

## リスク

- 未解決のリスクまたは後続対応:
  - `StepInput.Add` を公開したまま T12 へ進むと、csx 利用者に mutable な Step 間入力 API として露出し、後から非公開化すると破壊的変更になる。T11 の段階で公開 API から外すか、公開するなら設計書側で「Step が input を直接変更できる」契約へ明示的に変更する必要がある。
  - 再レビューリスク: 前回リスクは `Add` の internal 化により解消済み。後続 T12 では `Devo6.WorkFlow.Engine` から internal 登録経路を使い、同じ Type + name キー規則と重複失敗を維持する必要がある。
