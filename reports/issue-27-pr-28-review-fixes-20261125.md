# Issue #27 / PR #28 通常レビュー対応記録

## 対象

- 対象ブランチ: `feat/issue-27-file-based-csharp`
- 着手時 HEAD: `d28e51966a121db278feee122a4fae1d52fe4c95`
- ベース: `f78414d369bf751c2b5c77c3bc1c4bf843ecf731`
- 対象レビュー: [F01–F08](https://github.com/ssaattww/Devo6.WorkFlow/pull/28#issuecomment-7155881523)
- 変更は実装、検査、CI、作業追跡に限定。認証、NuGet 公開、Windows 実機、既定ブランチは対象外。

## 指摘ごとの対応

- **F01**: `.cs` 共通前検査へ CLI の `--allow-nuget` 集合を渡す。`validate` と `run` の両方で許可集合外の直接依存を `SCRIPT_REFERENCE_NOT_ALLOWED` とし、SDK runner 呼び出しがゼロであることを検査する。
- **F02**: SDK の最低版を `10.0.404` とし、SDK が提供する `-getProperty` / `-getItem` の evaluation-only 結果を restore 前に照合する。TFM、Library、AOT、NuGet sources、厳密な `PackageReference` 一覧を確認する。継承された MSBuild response、`Directory.Build.props/targets`、`Directory.Packages.props` と対象環境変数は事前拒否する。版不足と評価済み TFM 不一致で restore が呼ばれない recorder 検査を追加する。
- **F03**: SDK 10.0.404 の実装に合わせ、Windows は temp、Unix 系は LocalApplicationData 配下の `dotnet/runfile` を使う。エントリパスの大文字小文字正規化後に SDK と同じ SHA-256 を使い、assets の探索と出力排他を同じ artifacts directory に結び付ける。
- **F04**: キャンセル時に SDK プロセスツリーを停止し、親プロセス終了と stdout/stderr pipe の完了まで待ってから runner を返す。ローカルの無害な shell fixture が子プロセスの遅延書込み前に停止する検査を追加する。
- **F05**: Provider 生成前に PE の AssemblyReference をたどって private 依存を解決・ロードする。未解決依存は `SCRIPT_LOAD_FAILED` にする。`ILogger` を host 共有型に加え、ロガー使用 Step と依存 DLL 欠落時の ctor marker 不在を検査する。
- **F06**: `CS####` コンパイラー診断を `SCRIPT_COMPILE_FAILED`、それ以外の build 診断を `SCRIPT_BUILD_FAILED` に分ける検査を追加する。SDK の標準出力・標準エラーにある診断本文は保持する。
- **F07**: metadata 候補を公開型に限定してから Entry 名を照合する。同一入力 assembly の base/interface をたどり、継承した Provider 契約と `Create` を認識する。非公開同名候補と、継承 Provider の metadata 検査を追加する。検証時に fixture の ctor / `Create()` を実行しない。
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
- 再レビュー [7157650770](https://github.com/ssaattww/Devo6.WorkFlow/pull/28#issuecomment-7157650770) の追加対象に対応。F02 は `dotnet --info` の SDK Base Path と評価後 `MSBuildSDKsPath` の完全 path を比較し、通常選択 SDK を受け入れ、選択 root 外の同版 override を restore 前に拒否する。F03 は SDK runfile root を一度だけ取得し、同じ値を lock、assets 探索、DLL path に渡す。F05 は欠落 DLL で Provider ctor marker が残らない既存テストに加え、Logger assembly が host と同一であることを load context と実 Step で確認する。F07 は非公開 candidate のみなら `ENTRY_STEP_NOT_FOUND`、公開の継承 Provider は metadata-only で受け入れ、既存の副作用なし確認を維持する。
- 今回の C# 差分は SDK 同梱 Roslyn parser で構文 error 0、XML comment checker 全対象の違反 0、`git diff --check` 成功。両 TFM の test-project build は再試行して `NETSDK1004`。焦点テストは assets がないため開始できず、公開 NuGet restore と次の全件 CI は行っていない。

## 現在の状態

F02/F03/F05/F07 の追加修正とテストを作成した。Roslyn 構文検査、XML コメント checker、diff check は成功。依存 assets 不在により両 TFM build と focused tests は未実行。次の長時間 CI と Markdown lint は未実施。修正はまだ既存 PR branch に push していない。
