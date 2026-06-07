# Sub-agent実行レポート

## タスク

- 目的: T14 の `.csx` Entry 読み込み実装に必要な `Dotnet.Script.Core` の最小利用方法を調査する。
- タスク種別: investigation

## sub-agentを使う理由

- 理由: T14 実装前に外部 package API と既存設計の対応を切り分け、実装 worker の試行錯誤を減らすため。

## 対象範囲

- 対象: `Dotnet.Script.Core` の package 追加方法、`.csx` 実行または評価 API、script から `CompositeStep` を取得する最小経路、既存 project との参照方法。

## 対象外

- 対象外: 実装編集、T15 の `#load` / `#r` 詳細検証、T16 の validate 全体、CLI。

## 実行コマンド

- 実行コマンド:
  - `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
  - `sed -n '1,220p' AGENTS.md`
  - `rg -n "T14|Task 14|\\.csx|Dotnet\\.Script" tasks-status.md`
  - `sed -n '1,260p' reports/t14-csx-entry-loader-investigation-20260606190000.md`
  - `rg -n "^(##? )?(9|10|15|16\\.1)(\\.| |$)|^#+ .*Entry|SCRIPT_LOAD_FAILED|SCRIPT_COMPILE_FAILED|CompositeStep|WorkflowResult|Devo6\\.WorkFlow" doc/workflow_engine_spec.md`
  - `rg -n "class CompositeStep|record CompositeStep|interface CompositeStep|CompositeStep|WorkflowResult|SCRIPT_LOAD_FAILED|SCRIPT_COMPILE_FAILED" . -g '!bin' -g '!obj' -g '!reports/t14-csx-entry-loader-investigation-20260606190000.md'`
  - `find . -maxdepth 3 -type f \\( -name '*.csproj' -o -name '*.sln' -o -name 'Directory.Build.props' -o -name 'global.json' \\) -print`
  - `sed -n '491,555p' doc/workflow_engine_spec.md`
  - `sed -n '801,842p' doc/workflow_engine_spec.md`
  - `sed -n '1050,1090p' doc/workflow_engine_spec.md`
  - `sed -n '1128,1156p' doc/workflow_engine_spec.md`
  - `sed -n '1,260p' src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - `sed -n '1,180p' src/Devo6.WorkFlow.Abstractions/WorkflowResult.cs && sed -n '1,140p' src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs && sed -n '1,120p' src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj && sed -n '1,160p' tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj`
  - `dotnet --version && dotnet nuget list source`
  - `rm -rf /tmp/devo6-t14-dotnet-script-api && mkdir -p /tmp/devo6-t14-dotnet-script-api && dotnet new console --framework net8.0 --output /tmp/devo6-t14-dotnet-script-api`（拒否）
  - `dotnet new console --framework net8.0 --output /tmp/devo6-t14-dotnet-script-api-20260606`（読み取り専用の CLI home で失敗）
  - `DOTNET_CLI_HOME=/tmp/devo6-dotnet-cli-home-t14 dotnet new console --framework net8.0 --output /tmp/devo6-t14-dotnet-script-api-20260606`
  - `DOTNET_CLI_HOME=/tmp/devo6-dotnet-cli-home-t14 NUGET_PACKAGES=/tmp/devo6-t14-nuget dotnet add /tmp/devo6-t14-dotnet-script-api-20260606/devo6-t14-dotnet-script-api-20260606.csproj package Dotnet.Script.Core`
  - `find /tmp/devo6-t14-nuget/dotnet.script.core/2.0.1 -type f -maxdepth 5`
  - `sed -n '1,120p' /tmp/devo6-t14-dotnet-script-api-20260606/devo6-t14-dotnet-script-api-20260606.csproj`
  - `DOTNET_CLI_HOME=/tmp/devo6-dotnet-cli-home-t14 NUGET_PACKAGES=/tmp/devo6-t14-nuget dotnet run --project /tmp/devo6-t14-dotnet-script-api-20260606/devo6-t14-dotnet-script-api-20260606.csproj`（reflection と script 評価確認）
  - `find src tests -path '*/bin/*/net8.0/*.dll' -maxdepth 8 -type f`
  - `git status --short`
  - `rg -n "lint:md|markdown" package.json tools/lint -g '*.md' -g '*.json' -g '*.yaml' -g '*.yml'`
  - `npm run lint:md`
  - `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t14-csx-entry-loader-investigation-20260606190000.md`
  - `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t14-csx-entry-loader-investigation-20260606190000.md`

## 対象ファイル

- 変更または確認したファイル:
  - 確認: `AGENTS.md`
  - 確認: `tasks-status.md`
  - 確認: `doc/workflow_engine_spec.md` 9、10、15、18.1、18.2、19.1
  - 確認: `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - 確認: `src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj`
  - 確認: `src/Devo6.WorkFlow.Abstractions/WorkflowResult.cs`
  - 確認: `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
  - 確認: `tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj`
  - 確認: `/tmp/devo6-t14-dotnet-script-api-20260606/devo6-t14-dotnet-script-api-20260606.csproj`
  - 確認: `/tmp/devo6-t14-dotnet-script-api-20260606/Program.cs`
  - 確認: `/tmp/devo6-t14-dotnet-script-api-20260606/main.csx`
  - 変更: `reports/t14-csx-entry-loader-investigation-20260606190000.md`

## 指摘事項

- 指摘要約または「指摘なし」:
  - 指摘なし。T14 は `Dotnet.Script.Core` 2.0.1 を `src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj` に追加し、`ScriptCompiler` で script option を作って Roslyn `ScriptState.Variables` から名前付き `CompositeStep<TOut>` を回収する薄い loader として実装するのが最小でよい。
  - `dotnet add package Dotnet.Script.Core` は 2026-06-06 時点で `2.0.1` を解決し、package には `lib/net8.0/Dotnet.Script.Core.dll` が含まれる。
  - 確認した主 API は `Dotnet.Script.Core.ScriptCompiler`、`ScriptContext`、`CreateScriptOptions(...)`、`Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript.Create<object>(...)`、`ScriptState.Variables`、`ScriptVariable`。
  - `ScriptCompiler.CreateCompilationContext<TReturn, THost>(...)` でも `#r` を含む script の変数回収は可能だが、repo の `Abstractions` / `Engine` を利用者 script に毎回 `#r` させないなら、T14 では `CreateScriptOptions(...).AddReferences(typeof(IStep<>).Assembly, typeof(CompositeStep).Assembly).AddImports(...)` を使う経路が扱いやすい。

## 結果

- 結果:
  - 推奨実装方針: `CsxEntryLoader` 相当の小さな型を Engine 側に追加し、`Load(entryPath, entryName = "Main")` で `WorkflowResult` と実行対象 `CompositeStep` を返せる内部結果型を作る。Entry 実行までは既存 `CompositeStep<TOut>.ExecuteWorkflow(...)` に委譲する。
  - Entry path が存在しない、読み取りに失敗する、`ScriptCompiler` の dependency 解決中に `FileNotFoundException` などが出る場合は `WorkflowErrorCodes.ScriptLoadFailed` を持つ `WorkflowResult` に変換する。設計書の `ENTRY_SCRIPT_NOT_FOUND` との切り分けは T14 で明示するなら、ファイル不存在だけ `EntryScriptNotFound`、Dotnet.Script.Core のロード例外は `ScriptLoadFailed` が自然。
  - Roslyn compile diagnostics に error がある場合は実行せず、diagnostic の先頭または結合 message を `ErrorMessage` に入れて `WorkflowErrorCodes.ScriptCompileFailed` を返す。
  - script 成功時は `ScriptState.Variables` から `Name == entryName` かつ型が `IStep<>` を実装する `CompositeStep<TOut>` を探す。既定名は設計書 15.1 どおり `Main`。T14 では重複検出や `#load` 全体の一意性は後続 T15/T16 と重なるため、単一 script の変数名一致を優先する。
  - 利用者目線 test は `tests/Devo6.WorkFlow.Tests` の temp directory に `main.csx` を生成し、script 内で `using Devo6.WorkFlow.Abstractions; using Devo6.WorkFlow.Engine;`、小さな `IStep<string>` 実装、`var Main = CompositeStep.Define("Main").Run<HelloStep, string>().StoreAs();` を書く。test は loader で既定 `Main` と指定 Entry を取得できること、存在しない file が `SCRIPT_LOAD_FAILED`、構文または型 error が `SCRIPT_COMPILE_FAILED` になることを確認する。
  - 一時検証では `#r` なし script に対し、engine 側で `Abstractions` / `Engine` の assembly reference と import を足すことで、`errors=0`、`Main` が `Devo6.WorkFlow.Engine.CompositeStep<string>`、`main is IStep<string>` が `True` になることを確認した。
  - Markdown 確認は `npm run lint:md` と report への focused textlint が成功した。focused cspell は `reports/` が ignorePaths 対象のため skip だった。

## リスク

- 未解決のリスクまたは後続対応:
  - `Dotnet.Script.Core` の高水準 `ScriptRunner.Execute<TReturn>` は戻り値取得向きで、名前付き変数収集には向かない。T14 は Roslyn `ScriptState.Variables` 併用を前提にする必要がある。
  - T14 で repo assemblies を engine 側から参照追加する場合、配布後の assembly path / `AssemblyLoadContext` / API identity の扱いは T15 以降で追加検証が必要。
  - `#load`、明示許可された `#r`、NuGet restore、root 制限、循環検出、重複 Step 名検出は対象外なので、T14 test では単一 `.csx` の happy path とロード/コンパイル失敗に限定する。
  - Compile error 時に `RunAsync` まで進むと例外になるため、`Compile()` または `ScriptCompilationContext.Errors` を必ず先に確認してから実行する。
  - `dotnet new` は通常の `$HOME` が読み取り専用で失敗したため、検証コマンドでは `DOTNET_CLI_HOME=/tmp/...` が必要だった。repo の通常 test でも同じ環境変数が必要になる可能性がある。
