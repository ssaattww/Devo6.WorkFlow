# Issue #27 `.cs` Provider / Step 実行 実装報告

## 対象

- Repository: `ssaattww/Devo6.WorkFlow`
- Branch / base: `feat/issue-27-file-based-csharp` / `master` (`f78414d369bf751c2b5c77c3bc1c4bf843ecf731`)
- 対象 task / phase: T85 / P38。承認済み設計 `doc/issue-27-dotnet10-file-based-app-design.md` の Provider 契約と `.cs` `run` 境界を実装した。
- 既存 T82–T84 の変更、設計書、過去レポートを維持した。

## 実装

- `FileBasedWorkflowExecutor` は metadata-only 検査後に選択済み Provider を一度だけ生成し、`Create()` を一度だけ呼ぶ。Provider インターフェース、Entry インターフェース、`CompositeStep<>` は host の Engine / Abstractions に解決し、同じ CLR 型インスタンスを使う。
- `FileBasedWorkflowLoadContext` は Engine / Abstractions の要求について host アセンブリとの full identity を比較して host 型を共有する。他の管理依存は SDK が生成した deps 情報から解決し、未解決依存はロード失敗にする。
- `Create()` が返した値が host の `CompositeStep<>` であることと、`QualifiedName` が PE metadata で選択した Entry 名と一致することを Step より前に検査する。Provider のロード失敗は `SCRIPT_LOAD_FAILED`、Provider constructor / `Create()` 例外は `SCRIPT_ENTRY_PROVIDER_FAILED`、型契約不一致は `SCRIPT_API_IDENTITY_MISMATCH` または `SCRIPT_ENTRY_PROVIDER_INVALID`、Entry 不一致は `ENTRY_STEP_NOT_FOUND` で失敗する。
- Builder に `Run(...)` を追加。SDK build 後の metadata 検査、AssemblyLoadContext、Provider、Config 準備、既存 `CsxEntryLoader.ExecuteEntry` による Step 実行を、既存 `BuildCore` の `afterBuild` callback 内で完了させる。callback は出力ディレクトリ単位の `using(outputLock)` の内側で呼ばれるため、実行完了または失敗まで同じ排他を保持する。
- CLI の `.cs` 分岐を `validate` と `run` に分けた。`validate` は PE metadata reader のみにとどまり、`run` のみ Provider / workflow assembly をロードする。既存 `.csx` 経路は同じ loader と CLI 処理を維持する。net8 資産では引き続き SDK 起動前に `.cs` を拒否する。
- T85 の自動テスト `FileBasedWorkflowExecutorTests` は成功実行、`Create()` が返す Entry 名不一致、Provider `Create()` 例外を確認する。後二者では Step の marker が作られない。
- 既定の `TargetFramework` を multi-target 製品から外したときに空になる standalone XML/C# checker に `net8.0` を明示し、T31 checker が新規ファイルも検査できるよう XML summary を追加した。
- PR / publish workflow で .NET 8 runtime と SDK 10.0.404 の両方を setup し、`net8.0` と `net10.0` の multi-target test/build を通常 CI で選択できるようにした。

## 実測

公式 Linux x64 SDK 10.0.404 を今回限りの `/tmp/devo6-issue27-sdk-10.0.404` に展開した。アーカイブの SHA-512 は事前提示値と一致した。

```text
577d0eb17fec33d029104c5c63a1ab0accc3e28776274abef2218dbac5af18d2109b5406d26b31d76f5c245215e3ba0926a94c20246c672c41847c9e1eb1dc50
```

現行 CLI / Engine をローカル `0.1.0` nupkg に pack し、`.NET tool` として一時 tool path に install した。入力ソースは `Devo6.WorkFlow.Engine@[0.1.0]` と専用 lock を持つ一時 class-only fixture で、Engine の公開 Provider / CompositeStep API を呼ぶ。

| 実行 | 観測 |
| --- | --- |
| 実配布 tool `engine validate main.cs --entry Main` | exit 0、`Validation succeeded.`。module initializer / Provider constructor / `Create()` / Step の marker は 0 件。 |
| 同じ tool `engine run main.cs --entry Main` | exit 0、`Succeeded: Main`。module / constructor / `Create()` / Step の marker を各1件確認。host `CompositeStep<>` 上で Step が成功。 |
| Provider metadata は `Main`、`Create()` の返却 Entry は `Wrong` の `engine run` | exit 1、`ENTRY_STEP_NOT_FOUND`。module / Provider / `Create()` は実行後、Step marker は 0 件。 |

同一入力パスの別プロセス排他は、今回だけの信頼済み fixture と同じローカル CLI tool で一度確認した。1つ目の `engine run` を Step 内の release marker で停止し、2つ目の `engine run` を同じ `.cs` 入力で開始した。停止中に log は `start` が1件、`end` が0件であり、二つ目の Step は入っていなかった。release marker 作成後は両 process が exit 0 で終わり、最終 log は `start, end, start, end` の順だった。fixture、package feed、SDK、tool install は task-specific `/tmp` に限定した。

ビルドと試験:

| 検査 | 結果 |
| --- | --- |
| `dotnet build Devo6.WorkFlow.sln --framework net10.0 --no-restore` | 成功、警告 0 / error 0 |
| `dotnet build Devo6.WorkFlow.sln --framework net8.0 --no-restore` | 成功、警告 0 / error 0 |
| net10.0: `FileBasedWorkflowExecutorTests`, `FileBasedCsMetadataValidatorTests`, `CsFileInputPreflightTests` | 24 passed |
| net10.0: 既存 `CliRunValidateTests` (`.csx`) | 43 passed |
| net8.0: `CsFileInputPreflightTests` + `CliRunValidateTests` | 50 passed、SDK 付属 .NET 10 runtime へ `DOTNET_ROLL_FORWARD=Major` で実行 |
| net10.0: `CodingStandardsContractTests` | 3 passed。multi-target 化で TargetFramework が空になっていた standalone checker project に `net8.0` を明示し、新規宣言の XML comments を追加した後の結果 |
| `git diff --check` | 成功 |

最終候補ゲートは Release で一度だけ実行した。`dotnet restore Devo6.WorkFlow.sln --verbosity minimal` と `dotnet format Devo6.WorkFlow.sln --verify-no-changes --no-restore` は成功。全 solution test は15分 timeout で終了し、TRX は生成されなかった。ログには timeout を伴う次の4失敗が記録された。`ConditionalFlowIntegrationTests.ConditionalFlowTimeoutInSelectedBranchIsNotRetried`、`TraceValueContractTests.TimeoutFailureDoesNotCaptureProducedValues`（2件）、`TimeoutCancellationContractTests.AsyncStepTimeoutReturnsStepTimeoutAndStopsProduceAndFollowingSteps`。test 全件の完了数・総失敗数は確定できていないため、一括テスト合格とは扱わない。個別に成功した上記の focus tests は別の検査結果として保持する。

文書検査は `/tmp/CodexSkill/skills` を `CODEX_SKILLS_DIR` に指定して実行した。`lint:md:text` は成功。`lint:md:spell` は Issue #27 の設計書・追跡表・実装報告の英語技術語に CSpell 指摘（計223件）を出し、`lint:md:whitelist` は設計書と追跡表に未登録用語を検出（出力上316件超）。したがって集約 `npm run lint:md` は失敗。Sudachi の `lint:md:terms` は終了コード0で、`コンストラクタ` / `コンストラクター` の候補を報告した。workflow 2ファイルは Node `yaml` parser で構文確認成功。`git diff --check` は成功。用語辞書への一括登録は行っていない。

## 未確認と次の作業

- `net8.0` は net8 runtime-only 環境では未実行。今回の net8 test assembly は SDK の .NET 10 runtime に major roll-forward して動かした。
- Windows、実 NuGet 公開 feed からの install/update、独立した埋め込み API consumer、net8 runtime-only での起動は未確認。
- 全体ゲートと文書検査の個別結果は上記の通り。restore / format / textlint / Sudachi / workflow YAML / diff check は成功し、Release 全 solution test と集約 Markdown lint は失敗または未完了として扱う。
