# Issue #27 / PR #28 通常レビュー対応記録

## 対象

- 対象ブランチ: `feat/issue-27-file-based-csharp`
- 着手時 HEAD: `d28e51966a121db278feee122a4fae1d52fe4c95`
- ベース: `f78414d369bf751c2b5c77c3bc1c4bf843ecf731`
- 対象レビュー: [F01–F08](https://github.com/ssaattww/Devo6.WorkFlow/pull/28#issuecomment-7155881523)
- 変更は実装、検査、CI、作業追跡に限定。認証、NuGet 公開、Windows 実機、既定ブランチは対象外。

## 指摘ごとの対応

- **F01**: `.cs` 共通前検査へ CLI の `--allow-nuget` 集合を渡す。`validate` と `run` の両方で許可集合外の直接依存を `SCRIPT_REFERENCE_NOT_ALLOWED` とし、SDK runner 呼び出しがゼロであることを検査する。
- **F02**: SDK の最低版を `10.0.404` とし、`dotnet --version` で選択版を得る。`dotnet --info` を読む子プロセスだけ `DOTNET_CLI_UI_LANGUAGE=en-US` に固定し、選択された実 SDK の `Base Path` を解析する。呼出元 locale は変更しない。同一版が複数 installation root にあっても版番号のみで選ばない。SDK が提供する `-getProperty` / `-getItem` の evaluation-only 結果を restore 前に照合し、TFM、Library、AOT、NuGet sources、厳密な `PackageReference` 一覧を確認する。
- **F03**: SDK 10.0.404 の実装に合わせ、Windows は temp、Unix 系は LocalApplicationData 配下の `dotnet/runfile` を使う。エントリパスの大文字小文字正規化後に SDK と同じ SHA-256 を使い、assets の探索と出力排他を同じ artifacts directory に結び付ける。
- **F04**: キャンセル時に SDK プロセスツリーを停止し、親プロセス終了と stdout/stderr pipe の完了まで待ってから runner を返す。親が先に終了し子が pipe を保持するローカル shell fixture、同一 output lock を使う次要求、子の同期書込み完了を検査する。固定 sleep で書込み終了を推測しない。
- **F05**: Provider 生成前に PE の AssemblyReference をたどって private 依存を解決・ロードする。未解決依存は `SCRIPT_LOAD_FAILED` にする。`ILogger` を host 共有型に加え、ロガー使用 Step と依存 DLL 欠落時の ctor marker 不在を検査する。
- **F06**: `CS####` コンパイラー診断を `SCRIPT_COMPILE_FAILED`、それ以外の build 診断を `SCRIPT_BUILD_FAILED` に分ける検査を追加する。SDK の標準出力・標準エラーにある診断本文は保持する。
- **F07**: metadata 候補を公開型に限定してから Entry 名を照合する。Issue #27 と承認設計は generic base や依存 DLL からの Provider 継承を要件にしていないため、Entry 型自身が Provider interface と `Create()` を直接宣言する契約を明記し、同一 assembly、generic base、dependency assembly からの継承を metadata 検証で拒否する。検証時に fixture の ctor / `Create()` を実行しない。
- **F08**: PR CI を `net8.0` / `net10.0` の連続した二実行へ分け、各 TFM の TRX とログを別ディレクトリに保存する。同期 Step テストは進入通知後に外部キャンセルし、別ゲート解除で返却させ、タイマー競合を避ける。

## 検証と制約

- ローカル .NET SDK 10.0.404 は公式 Linux x64 SDK として以前に SHA-512 を照合した一時配置を再利用した。SDK 公式 `dotnet build <file.cs> --no-restore -getProperty:... -getItem:PackageReference` を一時 fixture に実行し、JSON に `TargetFramework=net10.0`、`OutputType=Library`、`PublishAot=false`、Engine の `Version=[0.1.0]` が返ることを確認した。evaluation-only で restore は実行していない。
- 変更した C# ファイルを SDK 同梱 Roslyn parser で構文解析し、構文エラーなし。`git diff --check` は成功。
- solution build は `--no-restore` で試みたが、`project.assets.json` がないため `NETSDK1004` で停止した。公開 NuGet 復元は実施していない。型チェック、焦点テスト、全件テストは未実施。
- Markdown lint は Node 依存が作業環境に存在しないため未実施。依存導入や許可語一覧の変更はしていない。PR #28 の過去レビューが報告した aggregate lint の CSpell 223件、whitelist 316件超は、今回作業では再計測・差分帰属できていない。
- 同一 HEAD `d28e51966a121db278feee122a4fae1d52fe4c95` の公式 GitHub Actions run [45564801005](https://github.com/ssaattww/Devo6.WorkFlow/actions/runs/45564801005) は完了。Restore と Format verification は成功、Test は失敗。`net8.0`: 321 passed / 1 failed / 3 skipped、`net10.0`: 339 passed / 3 skipped。失敗は `external cancellation during sync step returns STEP_CANCELED after completion` で、同期 Step の開始より前に 30 ms timer を設定していた。対象コードとテストはこの run の base と PR SHA で差分なし。従ってタイマー競合を改善候補としたが、base での再現原因までは確定していない。実行結果 artifact の取得は GitHub blob URL の `403 Forbidden` で失敗した。
- 初回修正 commit `1fb48421c6b3c03dc750947e1747c7253fccad9c` の CI [45574034150](https://github.com/ssaattww/Devo6.WorkFlow/actions/runs/45574034150) は Restore 成功後、Linux 専用 process fixture の `File.SetUnixFileMode` に対する CA1416 警告で Format verification が失敗し、Test は実行されなかった。この呼出しを Windows 判定で囲み、Linux の mock 検査だけを実行するよう修正した。
- commit `9aaab38c59431b0aa4d20f9c2f030a0dcdadb429` の CI [45574280551](https://github.com/ssaattww/Devo6.WorkFlow/actions/runs/45574280551) は Restore / Format verification 成功。net8.0 は `MSBuildSDKsPath` が CI に設定されているため、builder のテスト数件が `SCRIPT_BUILD_ENVIRONMENT_UNSUPPORTED` となる前提検査バグでコンパイルも一部失敗。net10.0 は 352 passed / 10 failed / 3 skipped。失敗内訳は T31 コメント不足、非公開 Provider を `ENTRY_STEP_NOT_FOUND` とする新契約に沿っていない旧期待値、MSBuildSDKsPath 環境拒否、キャンセル fixture のプロセス自然終了競合。この修正 commit で SDK path を evaluation の実効値として選択 SDK と照合し、テストの TFM 条件・期待値・コメント・キャンセル fixture を修正した。
- 上記修正差分の C# XML コメント checker を SDK 10.0.404 同梱 Roslyn で一時コンパイルし、全 `src` / `tests` / `tools` を走査して違反 0 を確認した。`git diff --check` も成功。
- 依存 assets と package cache がチェックアウトおよび既定 NuGet cache に存在しない。net8.0 と net10.0 のテスト project `build --no-restore` はどちらも `NETSDK1004`（`tests/Devo6.WorkFlow.Tests/obj/project.assets.json` 不在）で開始できなかった。公開 NuGet restore は行わず、両 TFM のコンパイル、net10.0 失敗対象だけの focused test は未確認。よって次の長時間全件 CI はまだ開始していない。
- 再レビュー [7157650770](https://github.com/ssaattww/Devo6.WorkFlow/pull/28#issuecomment-7157650770) の追加対象に対応。F03 は SDK runfile root を一度だけ取得し、同じ値を lock、assets 探索、DLL path に渡す。F05 は欠落 DLL で Provider ctor marker が残らない既存テストに加え、Logger assembly が host と同一であることを load context と実 Step で確認する。F07 は非公開 candidate のみなら `ENTRY_STEP_NOT_FOUND` とする既存仕様を維持する。継承 Provider はこの後の対応で契約外と判断し、サポートしないよう変更した。
- 今回の C# 差分は SDK 同梱 Roslyn parser で構文 error 0、XML comment checker 全対象の違反 0、`git diff --check` 成功。両 TFM の test-project build は再試行して `NETSDK1004`。焦点テストは assets がないため開始できず、公開 NuGet restore と次の全件 CI は行っていない。

## 現在の状態

F02/F03/F05/F07 の追加修正とテストを作成した。Roslyn 構文検査、XML コメント checker、diff check は成功。依存 assets 不在により両 TFM build と focused tests は未実行。次の長時間 CI と Markdown lint は未実施。修正はまだ既存 PR branch に push していない。

## 継続検証: restore と焦点確認

候補 HEAD `91c32c6ece534a6c84ad9c55992d033dd542281c` を維持し、以前の T83 一時 SDK を探した。`/tmp/devo6-issue27-sdk-10.0.404` と `/tmp/devo6-workflow-nuget` は存在せず、以前の `DOTNET_CLI_HOME` / `NUGET_PACKAGES` 配置も今回の配置とは異なる。再利用可能だった公式 Linux x64 SDK 10.0.404 は `/tmp/devo6-review-sdk-10.0.404`。NuGet.Config は nuget.org のみを指し、repo の固定 package version を変更せず通常の既存 solution restore を一度実行した。

```sh
DOTNET_CLI_HOME=/tmp/devo6-dotnet-home \
NUGET_PACKAGES=/tmp/devo6-workflow-nuget \
DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 \
/tmp/devo6-review-sdk-10.0.404/dotnet restore Devo6.WorkFlow.sln --verbosity minimal
```

restore は Abstractions / Engine / Tests / CLI の各 project で成功した。取得先は既定の nuget.org、保存先は今回専用の `/tmp/devo6-workflow-nuget`。repo の feed、source、credential、dependency、永続環境設定は変更していない。

成功後、同 SDK と取得済み assets を用いた Release solution build を `--no-restore` で `net8.0` / `net10.0` 両方に実施し、両方とも 0 warning / 0 error で成功した。焦点検査は以下の結果。

- `net10.0`: `CsFileInputPreflightTests`, `FileBasedCsWorkflowBuilderContractTests`, `FileBasedSdkRunnerCancellationTests`, `FileBasedCsMetadataValidatorTests`, `FileBasedWorkflowExecutorTests`, `ExternalCancellationDuringSyncStepReturnsStepCanceledAfterCompletion`, `CodingStandardsContractTests` を含む filter で **56 passed, 0 failed, 0 skipped**。
- `net8.0`: `CsFileInputPreflightTests` と既存 `CliRunValidateTests` を含む filter で **50 passed, 0 failed, 0 skipped**。SDK 10.0.404 への `DOTNET_ROLL_FORWARD=Major` による実行であり、native .NET 8 runtime ではない。
- F04 の同期 Step キャンセルは上記 net10 filter 内で進入通知後・解放前にキャンセルする検査として成功。F04 の SDK 子プロセス／出力 pipe 待ち検査も `FileBasedSdkRunnerCancellationTests` 内で成功。
- F08 の TFM 分岐と `.csx` 回帰は net8 focus 50 件の中で成功。T31 `CodingStandardsContractTests` も net10 focus に含まれ 3 件成功した。
- XML コメント checker は直前の変更時に全 `src` / `tests` / `tools` 対象で違反 0。今回 `npm run lint:md` を試したが、スクリプトが要求する `review-enforcer` Codex skill ディレクトリがこの実行環境にないというエラーで完了しなかった。textlint / spell / whitelist を含む集約 Markdown lint の成功とは扱わない。

## F05 配布 tool と環境要因の切り分け

一時 local feed の Engine / CLI `0.1.0` package から CLI tool を install し、`ILogger` を呼ぶ Step と Provider ctor / `Create()` / Step の個別 marker を持つ fixture で再検証した。`.cs` の lock は実際に生成された `project.assets.json` に合わせた。SDK 10.0.404、`DOTNET_CLI_HOME=/tmp/devo6-dotnet-home`、`NUGET_PACKAGES=/tmp/devo6-workflow-nuget`、local-only `NuGet.Config` を使用した。

最初の restore 失敗では、`XDG_DATA_HOME` が指定済みで migration file `1` は `/tmp/devo6-pr28-dist.93gf2T/local-data/NuGet/Migrations/1` に書かれた一方、Linux runtime が named mutex の共有メモリファイルを `XDG_RUNTIME_DIR` 未設定時の `/home/agent/.dotnet/corefx/Runtime/SharedMemory/.dotnet/shm/session...` に作ろうとして `EROFS` になった。`DOTNET_CLI_HOME` と `NUGET_PACKAGES` はこの runtime path を変更しない。`XDG_DATA_HOME` は NuGet migration のデータ先、`XDG_RUNTIME_DIR` は named mutex の runtime path で別設定である。`.NET Runtime` が示す Linux の緩和策どおり、既存の `/tmp` 配下に mode 700 の `XDG_RUNTIME_DIR` を作り、その process だけに設定したところ、mutex file は `/tmp/devo6-pr28-dist.93gf2T/runtime/.dotnet/shm/global/` 以下になり restore は進行した。`XDG_CONFIG_HOME` は後続の成功試行では不要だった。参照: [NuGet MigrationRunner の移行先と mutex 呼び出し](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Common/Migrations/MigrationRunner.cs)、[.NET Runtime の XDG_RUNTIME_DIR 説明](https://github.com/dotnet/runtime/issues/135726)。システム領域、権限、永続設定は変更していない。

有効な lock graph で `engine validate main.cs --entry Main --allow-nuget Devo6.WorkFlow.Engine,0.1.0` は exit 0。validate 後に Provider ctor、`Create()`、Step の marker がすべて存在しないことを確認した。続く `engine run` は exit 0 で logger message `distributed logger contract reached` を出力し、3 marker がすべて作られた。さらに ref asset のみを含み runtime DLL を含めない一時 `HiddenDependency` package を利用した CLI `run` は `SCRIPT_LOAD_FAILED` で終了し、Provider ctor marker は不在だった。従って今回の F05 配布 CLI / Logger / validate 非実行 / 依存欠落前の ctor 拒否 fixture は成功。作業中にだけ作った lock graph の余分な2 package、HiddenDependency package、fixture namespace の修正は `/tmp` の test fixture 限定である。

F05 の test-project 側の証拠も維持する。`FileBasedWorkflowExecutorTests` で private DLL 欠落時に Provider constructor marker が残らないことと、host と共有する `ILogger` 型を Step で使えることを含み、成功済み net10 focused 56件に含まれる。候補コードやテスト差分は今回変更していないため、前回の両 TFM build / focused 結果を引き継いだ。

## 最終文書検査とレビュー引き継ぎ

`package-lock.json` と同じ既存依存（CSpell 9.8.0 等）および同じ設定・対象文書で比較した。基準 `f78414d369bf751c2b5c77c3bc1c4bf843ecf731` は9文書で0件、公開 PR `9aaab38c59431b0aa4d20f9c2f030a0dcdadb429` は10文書で228件、現在候補は10文書で128件だった。PR と比べた新規語は0件。`tasks-status.md` から100件を日本語化で除去し、残る128件は既存の設計書100件と段階表28件である。語別の比較は `reports/issue-27-markdown-lint-diff-20261125.md` に記録した。

前回の文書整理時点では、全対象の textlint、変更文書に対する許可語検査、SudachiPy、`git diff --check` が成功した。SudachiPy は PR と同じ用語揺れ候補を1組報告し終了コード0。CSpell 段階はその時点の既存128件で非0となり、集約 Markdown lint は未通過だった。下記の今回の文書差分検査は、後日の検査結果として別に記録する。`AGENTS.md` に全体 Markdown 合格の必須記載はなく、PR CI `.github/workflows/pr-xunit-tests.yml` も Markdown lint を実行しない。

現在の候補は commit `91c32c6ece534a6c84ad9c55992d033dd542281c` と作業ツリー差分で構成する。PR #28 は open draft、公開 head `9aaab38c59431b0aa4d20f9c2f030a0dcdadb429` のまま。GitHub への書き込み、commit、push、CI 起動はせず、親担当が既存レビュワーへ差分と検証根拠を渡す。

## 通常再レビュー未公開候補 7160026786 対応

### 対応

- **F02**: `--version` で得た選択版を保持し、`dotnet --info` を起動する子プロセスだけ `DOTNET_CLI_UI_LANGUAGE=en-US` に設定する。選択 SDK 自身が表示する `Base Path` と評価後 `MSBuildSDKsPath` の完全 path を照合するため、同じ版が複数 root にある場合も実際の選択 root を識別できる。無効 path / 版不一致を拒否する。Contract test と builder recorder で `--version` → `--info` → evaluation 呼出しを確認する。SDK process の UI language 固定は caller process の環境を変更しない。
- **F04**: runner は親 process が正常終了した場合も stdout/stderr pipe が閉じるまで同期して待ち、その後キャンセル token を再確認して `OperationCanceledException` を返す。テスト fixture は親 PID の終了と子の開始を別々に同期する。子は gate を開くまで pipe を保持し、許可後に marker を書いて終了する。キャンセル中に実出力先由来の lock path を次の要求が開けず、子の完了後、先行 lock 解放後にだけ次要求が取れることを確認する。固定 sleep による終了推測は使わない。
- **F07**: Issue #27 は class-only 入力や明示 Entry を求めるが、generic base / dependency assembly からの Provider 継承を受入条件としていない。設計の最小 Entry 契約に沿って、属性付き Entry 型自身が `IWorkflowEntryProvider` と公開 `Create()` を直接宣言する形に限定した。継承契約を metadata-only で `SCRIPT_ENTRY_PROVIDER_INVALID` とし、同一 assembly、generic base、別 dependency assembly の試験を追加した。既存の非公開候補除外・検証時非実行は維持する。

### 今回の検証

変更影響範囲を .NET SDK 10.0.404 の既存一時配置と事前に復元済み assets で確認した。呼出元の `DOTNET_CLI_UI_LANGUAGE=ja-JP` を継承したプロセスで次の焦点コマンドが成功し、50 passed / 0 failed / 0 skipped。`--info` の SDK 子 process だけ `en-US`、別コマンドの子 process とテスト runner は `ja-JP` のままであることを確認した。

```sh
DOTNET_CLI_HOME=/tmp/devo6-dotnet-home NUGET_PACKAGES=/tmp/devo6-workflow-nuget \
DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 \
/tmp/devo6-review-sdk-10.0.404/dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj \
  -c Release -f net10.0 --no-restore \
  --filter 'FullyQualifiedName~CsFileInputPreflightTests|FullyQualifiedName~FileBasedCsWorkflowBuilderContractTests|FullyQualifiedName~FileBasedCsMetadataValidatorTests|FullyQualifiedName~FileBasedSdkRunnerCancellationTests' \
  --logger 'console;verbosity=minimal'
```

この検査は変更した Engine / test project を net10.0 Release でコンパイルした。変更後の最終焦点検査は SDK child locale の試験も含む上記 50 件。成功済み Release build と F01/F03/F05/F06 の過去の焦点証拠は変更範囲外として再実行していない。net8.0 は対象コードが `NET10_0` 条件内のため今回の変更でコンパイル影響なし。native .NET 8 runtime、Windows、全件 CI、公開 NuGet は未確認のまま。

変更文書 lint は環境不足で未完了。`CODEX_SKILLS_DIR=/tmp/CodexSkill/skills npm run lint:md:whitelist -- --changed` は skill script が `yaml` Node package を解決できず停止し、同設定の `npm run lint:md:text` は `textlint` executable 不在で停止した。Node 依存は導入していない。以前の差分比較にある CSpell 128件はこの文書追記以前の候補の記録で、現行文書の再計測はしていない。既存 CSpell 語を増やす・許可語へ加える変更は行っていない。

最終レビュー候補は commit `91c32c6ece534a6c84ad9c55992d033dd542281c` と本報告までの未コミット差分。公開 PR HEAD は `9aaab38c59431b0aa4d20f9c2f030a0dcdadb429` のままであり、候補を push/commit していない。`git diff --check` は今回の変更後も成功。

## F04 builder 経由の同一出力先検査

同じ `.cs` 入力に対して二つの `FileBasedCsWorkflowBuilder` 要求を起動する Linux 用回帰検査を更新した。builder の最小限の観測処理が、後続要求による実際の出力 lock path への取得試行と `IOException` の競合を通知する。通知で得た path が builder の出力先から算出した lock path と一致し、先行要求が未完了で SDK 呼出しがまだないことを、子の解放前に確認する。先行 process の親が終了した後も、子は出力 pipe を保持する。子は一時ファイルへの書込みを閉じた後、完了名へ原子的に名前を変更し、後続の復元模擬処理はその内容を読んで一致を確認する。このため、ファイルの存在だけでは完了と判断しない。意図的に排他を早期解放する改変版との対照検査は行っていない。製品動作は変更せず、内部テスト注入の観測処理だけを追加した。

```sh
DOTNET_CLI_HOME=/tmp/devo6-dotnet-home NUGET_PACKAGES=/tmp/devo6-workflow-nuget \
DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 \
/tmp/devo6-review-sdk-10.0.404/dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj \
  -c Release -f net10.0 --no-restore \
  --filter 'FullyQualifiedName~BuilderWaitsForCancelledRestoreChildBeforeStartingTheNextRestore' \
  --logger 'console;verbosity=minimal'
```

結果は **1 passed, 0 failed, 0 skipped**。これは追加した単一の Linux 子 process fixture を使う焦点検査であり、前回の他のテスト、配布確認、通常再レビュー対象の技術差分は変更していない。

## 今回の文書差分検査

前回の lint 記録（上記「通常再レビュー未公開候補」節）では、実行環境で skill script の `yaml` 依存と `textlint` 実行ファイルを解決できず、文書検査は未完了だった。これを成功扱いしない。

前回の文書整理時は、既存の固定 Node 依存（textlint 15.7.1、CSpell 9.8.0、yaml 2.9.0）と同じ設定で対象3文書を比較した。textlint は成功。許可語指摘は比較元351件から350件へ減り、増加は0件。CSpell は両方100件で差分0件となり、前回候補で増えていた `parameterless` と `textlint` は解消した。

2026-11-25 11:51（協定世界時）の再確認でも同じ3文書を同じ固定依存で比較した。textlint は成功。許可語指摘は比較元と候補が350件ずつ、CSpell は100件ずつで、単語別の新規差分はいずれも0件。前回候補で増えていた2つの綴り指摘が戻っていないことも確認した。全体の whitelist / CSpell は既存候補が残るため未通過で、SudachiPy は未導入のため今回も未実施。許可語一覧の追加や変更はしていない。
