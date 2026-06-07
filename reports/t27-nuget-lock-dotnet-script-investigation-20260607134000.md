# Sub-agent実行レポート

## タスク

T27 NuGet ロックファイル実装前に、`Dotnet.Script.Core` の NuGet 解決実装を調査し、車輪の再発明を避ける方針を整理する。

## sub-agentを使う理由

ユーザー指示により、調査、実装、レビューは sub-agent に委譲する。

## 対象範囲

- 現在参照している `Dotnet.Script.Core` の version と利用箇所
- パッケージ内または公開 source にある NuGet restore / dependency resolution / lock 関連 API
- 既存 `CsxEntryLoader` の参照検証との接続点
- T27 の設計影響と検査候補

## 対象外

- 設計書、C# 実装、検査コードの編集
- T28 の `#load "nuget: ..."` 実装
- T29 以降の作業

## 実行コマンド

- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-doc-maintainer/SKILL.md`
- `sed -n '1,260p' /home/ibis/AI/CodexSkill/skills/tdd-executor/SKILL.md`
- `sed -n '1,260p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,260p' reports/t27-nuget-lock-dotnet-script-investigation-20260607134000.md`
- `rg -n "T27|NuGet|lock|#r|#load|Dotnet.Script|dotnet-script|P10" ...`
- `sed -n '1,240p' src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `sed -n '220,760p' src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `sed -n '1,220p' src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj`
- `sed -n '1,260p' tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
- `sed -n '1,260p' tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`
- `sed -n '580,690p' doc/workflow_engine_spec.md`
- `sed -n '1140,1248p' doc/workflow_engine_spec.md`
- `sed -n '1258,1276p' doc/workflow_engine_spec.md && sed -n '1548,1624p' doc/workflow_engine_spec.md && sed -n '1814,1824p' doc/workflow_engine_spec.md`
- `find ~/.nuget/packages/dotnet.script.core/2.0.1 ~/.nuget/packages/dotnet.script.dependencymodel/2.0.1 -type f | sort`
- `find ~/.nuget/packages/dotnet.script.dependencymodel.nuget -maxdepth 5 -type f | sort`
- `sed -n '1,240p' ~/.nuget/packages/dotnet.script.core/2.0.1/dotnet.script.core.nuspec`
- `sed -n '1,260p' ~/.nuget/packages/dotnet.script.dependencymodel/2.0.1/dotnet.script.dependencymodel.nuspec`
- `sed -n '1,260p' ~/.nuget/packages/dotnet.script.dependencymodel.nuget/2.0.1/dotnet.script.dependencymodel.nuget.nuspec`
- `unzip -l ~/.nuget/packages/dotnet.script.core/2.0.1/dotnet.script.core.2.0.1.nupkg | sort`
- `unzip -l ~/.nuget/packages/dotnet.script.dependencymodel/2.0.1/dotnet.script.dependencymodel.2.0.1.nupkg | sort`
- `strings .../Dotnet.Script.Core.dll | rg -n "NuGet|Restore|Package|Lock|Dependency|ScriptCompiler|..."`
- `strings .../Dotnet.Script.DependencyModel.dll | rg -n "NuGet|Restore|Package|Lock|Dependency|..."`
- `strings .../Dotnet.Script.DependencyModel.NuGet.dll | rg -n "NuGet|Restore|Package|Lock|Dependency|..."`
- `DOTNET_CLI_HOME=/tmp dotnet new console -o /tmp/devo6-reflect-dotnet-script-20260607 --framework net8.0 --no-restore`
- `DOTNET_CLI_HOME=/tmp dotnet run --project /tmp/devo6-reflect-dotnet-script-20260607 -- .../Dotnet.Script.Core.dll .../Dotnet.Script.DependencyModel.dll .../Dotnet.Script.DependencyModel.NuGet.dll`
- `curl -fsSL https://api.github.com/repos/dotnet-script/dotnet-script/git/trees/ea2da11cf4452d6e8263a6d23c728ebf97cbb915?recursive=1 | rg 'ScriptParser.cs|ScriptProjectProvider.cs|RuntimeDependencyResolver.cs|DotnetRestorer.cs|CachedRestorer.cs|NuGetSourceReferenceResolver.cs|NuGetMetadataReferenceResolver.cs'`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel/ProjectSystem/ScriptParser.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel/ProjectSystem/ScriptParserInternal.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel/ProjectSystem/ScriptProjectProvider.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel/ProjectSystem/ProjectFile.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel/ProjectSystem/PackageReference.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel/ProjectSystem/PackageVersion.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel/Runtime/RuntimeDependencyResolver.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel/Context/DotnetRestorer.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel/Context/CachedRestorer.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel.Nuget/NuGetMetadataReferenceResolver.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel.Nuget/NuGetSourceReferenceResolver.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel/ProjectSystem/FileUtils.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.DependencyModel/Environment/ScriptEnvironment.cs | sed -n '1,220p'`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/src/Dotnet.Script.Core/ScriptCompiler.cs`
- `curl -fsSL https://raw.githubusercontent.com/dotnet-script/dotnet-script/ea2da11cf4452d6e8263a6d23c728ebf97cbb915/README.md | rg -n 'restore|NuGet|#r|#load|cache|project.assets' -C 2`
- `rg -n "TargetFramework|Restore|RestorePackagesWithLockFile|packages.lock|lock|NuGetLock|AllowedNuGet|PackageSources|Dotnet.Script.Core|CsxNuGetReference|nuget:" -S .`
- `rg --files -g '*.csproj' -g 'Directory.*' -g 'NuGet.config' -g 'packages.lock.json'`
- `dotnet list src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj package --include-transitive`
- `sed -n '1,120p' src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `sed -n '1,220p' tools/lint/README.md`
- `sed -n '1,220p' package.json`
- `sed -n '1,220p' tools/lint/markdown-targets.json`
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t27-nuget-lock-dotnet-script-investigation-20260607134000.md`
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t27-nuget-lock-dotnet-script-investigation-20260607134000.md`
- `npm run lint:md:targets`

## 対象ファイル

- `tasks-status.md`
  - T27 は未着手。完了条件は、ロックファイル一致、不一致、欠落、既存の浮動 NuGet 版禁止、通常の `dotnet test` が外部通信に依存しないこと。
- `phases-status.md`
  - P10 は未着手。T27-T28 で NuGet ロックファイルと `#load "nuget: ..."` を外部通信依存を避けた検査で確認する。
- `doc/workflow_engine_spec.md`
  - 9.2、10、11.3、16.1-16.6、17.2、19.2-19.3、21 の NuGet、`#r`、`#load`、cache、次フェーズ候補を確認。
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - `#load`、`#r`、NuGet 許可検証、浮動版禁止、`ScriptCompiler` 統合、`CsxEntryLoaderOptions.AllowedNuGetReferences` を確認。
- `src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj`
  - `Dotnet.Script.Core` 2.0.1 を top-level package として参照。
- `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
  - `SCRIPT_NUGET_RESTORE_FAILED` は存在するが、lock 欠落や不一致専用 code は未定義。
- `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
  - 許可外 NuGet、許可済み NuGet restore success、浮動 NuGet 版禁止を確認。
- `tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`
  - 許可外 NuGet の validation error を確認。
- restore 済み NuGet package 実体
  - `~/.nuget/packages/dotnet.script.core/2.0.1/`
  - `~/.nuget/packages/dotnet.script.dependencymodel/2.0.1/`
  - `~/.nuget/packages/dotnet.script.dependencymodel.nuget/2.0.1/`
  - nupkg 内に DLL はあるが XML docs は見当たらない。
- 外部一次情報、確認日: 2026-06-07
  - `https://github.com/dotnet-script/dotnet-script` の commit `ea2da11cf4452d6e8263a6d23c728ebf97cbb915`
  - `README.md`
  - `src/Dotnet.Script.Core/ScriptCompiler.cs`
  - `src/Dotnet.Script.DependencyModel/ProjectSystem/ScriptParser.cs`
  - `src/Dotnet.Script.DependencyModel/ProjectSystem/ScriptParserInternal.cs`
  - `src/Dotnet.Script.DependencyModel/ProjectSystem/ScriptProjectProvider.cs`
  - `src/Dotnet.Script.DependencyModel/ProjectSystem/ProjectFile.cs`
  - `src/Dotnet.Script.DependencyModel/ProjectSystem/PackageReference.cs`
  - `src/Dotnet.Script.DependencyModel/ProjectSystem/PackageVersion.cs`
  - `src/Dotnet.Script.DependencyModel/Runtime/RuntimeDependencyResolver.cs`
  - `src/Dotnet.Script.DependencyModel/Context/DotnetRestorer.cs`
  - `src/Dotnet.Script.DependencyModel/Context/CachedRestorer.cs`
  - `src/Dotnet.Script.DependencyModel.Nuget/NuGetMetadataReferenceResolver.cs`
  - `src/Dotnet.Script.DependencyModel.Nuget/NuGetSourceReferenceResolver.cs`
  - `src/Dotnet.Script.DependencyModel/ProjectSystem/FileUtils.cs`
  - `src/Dotnet.Script.DependencyModel/Environment/ScriptEnvironment.cs`

## 指摘事項

- `Dotnet.Script.Core` 2.0.1 には NuGet restore と script dependency resolution の実装がある。`ScriptCompiler.CreateCompilationContext` は `RuntimeDependencyResolver` を通じて一時 csproj を作り、`dotnet restore` を実行し、`obj/project.assets.json` から runtime dependency を読み、Roslyn の `ScriptOptions` に runtime assembly と NuGet script load resolver を加える。
- `Dotnet.Script.DependencyModel.ProjectSystem.ScriptParser` は `#r "nuget: ..."` と `#load "nuget: ..."` の package reference 抽出を既に持つ。`PackageReference`、`PackageVersion`、`ProjectFile` も public API として利用できる。
- `Dotnet.Script.DependencyModel.Context.CachedRestorer` は `script.csproj.cache` と生成 csproj の一致で restore を省く dependency cache であり、利用者向けロックファイルではない。README でも dependency cache は `project.assets.json` を再利用する性能最適化として説明されている。
- `Dotnet.Script.Core` / `Dotnet.Script.DependencyModel` の public API surface には、repo の T27 が求める「lock file 生成」「lock file 検証」「lock 欠落時に restore 前に止める」ための専用 API は確認できなかった。
- package 実体と公開 source には `NuGet.ProjectModel.LockFileUtilities` を使う internal 実装は見えるが、`Dotnet.Script.Core` 経由で stable lock file contract として公開されてはいない。
- 現在の `CsxEntryLoader` は NuGet 参照文字列を独自に検証している。範囲は、`nuget:` prefix 判定、`packageId, version` の2分割、`*`、bracket、parenthesis などの浮動または range 風 version 禁止、`AllowedNuGetReferences` との exact pair 照合、`#load "nuget: ..."` の明示拒否である。
- `Dotnet.Script.DependencyModel.ProjectSystem.PackageVersion.IsPinned` は bracket 付き exact range を pinned 扱いにし得る。repo の現在仕様は bracket と parenthesis を禁止しているため、浮動 NuGet 版禁止を `PackageVersion.IsPinned` へ単純置換すると現在より緩くなる。
- `ScriptParser` は `#load "nuget: ..."` も package reference として抽出するため、T27 で使う場合は T28 前に `#load "nuget: ..."` を解禁しない境界が必要である。
- `Dotnet.Script.Core.Commands.ExecuteScriptCommandOptions` などには `PackageSources`、`CachePath`、`NoCache` がある。現在の `CsxEntryLoader` は独自に `ScriptCompiler` を作っており、`ScriptContext` の `PackageSources` は空配列、`cachePath` は working directory、`useRestoreCache` は true になっている。
- `FileUtils.GetTempPath` は `DOTNET_SCRIPT_CACHE_LOCATION`、Linux の `XDG_CACHE_HOME`、`$HOME/.cache`、`Path.GetTempPath()` の順で cache root を決める。通常検査で副作用や home 依存を避けるには、test 側で cache path と resolver を固定する設計が必要である。

## 結果

結論:

- `Dotnet.Script.Core` は T27 の NuGet restore / dependency resolution には再利用すべきである。
- ただし、T27 の NuGet ロックファイルそのものの生成、永続 format、欠落や不一致の policy 判定、通常 `dotnet test` を外部通信に依存させない test seam は repo 側で補う必要がある。
- 最小方針は「復元と依存 graph 収集は `Dotnet.Script.Core` / `Dotnet.Script.DependencyModel` に任せ、ロックファイル contract と検証判定だけを repo 側に持つ」である。

推奨設計案:

- T27 では `#load "nuget: ..."` の明示拒否を維持し、対象を `#r "nuget: package, version"` に限定する。
- NuGet directive 抽出は、可能なら `Dotnet.Script.DependencyModel.ProjectSystem.ScriptParser` または同じ `PackageReference` / `PackageVersion` 型を参照して dotnet-script と近い解釈に寄せる。ただし repo の exact version policy は現在の stricter rule を維持する。
- production の依存解決は `ScriptCompiler.CreateCompilationContext` または `RuntimeDependencyResolver.GetDependencies(...)` に寄せる。lock 照合前後で二重 restore にならないよう、実装時は `CreateCompilationContext` の `RuntimeDependencies` を lock verifier に渡せる構造を優先する。
- lock 欠落は restore 前に fail できるよう、`LoadScriptSource` または NuGet directive 収集直後に「NuGet 参照あり、lock file なし」を判定する。
- lock 不一致のうち direct package id/version の不一致も restore 前に fail できる。transitive graph や runtime asset の不一致は `Dotnet.Script` の resolution 結果を lock と比較する。
- lock format は repo 側 contract として設計書に明記する。候補は、entry path 基準の lock file に direct references、resolved dependencies、runtime assembly path ではなく package id/version と asset identity、package sources、target framework、runtime identifier、`Dotnet.Script.Core` version を記録する形。
- 通常 `dotnet test` は外部通信に依存しないよう、T27 の lock verifier に dependency graph provider を注入できるようにする。production provider は `Dotnet.Script`、test provider は固定 fixture を返す。実 NuGet restore を行う E2E は opt-in trait、環境変数、または local package source fixture に分離する。
- restore 失敗は既存 `SCRIPT_NUGET_RESTORE_FAILED` を使える。lock 欠落と lock 不一致は `SCRIPT_NUGET_LOCK_MISSING`、`SCRIPT_NUGET_LOCK_MISMATCH` のような専用 error code を追加する方が検査と利用者表示が安定する。

代替案と却下理由:

- `Dotnet.Script.DependencyModel.CachedRestorer` の `.csproj.cache` を T27 の lock とみなす案は却下。これは一時 csproj の性能 cache であり、利用者が review できる lock file でも、欠落や不一致を安定 error にする contract でもない。
- `project.assets.json` をそのまま repo lock として扱う案は保留または非推奨。NuGet の詳細 format に密結合し、entry `.csx`、direct reference、T28 の script package 境界を表しにくい。内部比較には `NuGet.ProjectModel` を使えるが、公開 contract は repo 専用の薄い format がよい。
- NuGet restore を repo 側で独自実装する案は却下。`Dotnet.Script.Core` が既に一時 csproj、`dotnet restore`、`project.assets.json` 読み取り、runtime assembly 解決、`#load "nuget: ..."` 用 source resolver を持つため、車輪の再発明になる。
- `PackageVersion.IsPinned` だけで浮動版禁止を置き換える案は却下。repo の現在 rule より緩くなる可能性がある。

T27 検査候補:

- `ExecuteReturnsLockMissingWhenNuGetReferenceHasNoLockFile`
- `ValidateReturnsLockMissingWhenNuGetReferenceHasNoLockFile`
- `ExecuteReturnsLockMismatchWhenDirectNuGetVersionDiffers`
- `ValidateReturnsLockMismatchWhenResolvedNuGetDependencyDiffers`
- `ExecuteUsesLockedNuGetDependenciesWhenLockMatches`
- `ValidateKeepsFloatingNuGetVersionRejectedBeforeLockCheck`
- `ExecuteDoesNotRestoreWhenNuGetReferenceIsNotAllowed`
- `RegularDotnetTestUsesFakeNuGetDependencyResolver`
- `LockVerifierRecordsPackageSourcesTargetFrameworkAndRuntimeIdentifier`
- `NuGetLoadDirectiveRemainsUnsupportedBeforeT28`

設計書更新が必要な箇所:

- `doc/workflow_engine_spec.md` 16.3 に NuGet lock file の required / optional 条件、欠落、不一致、生成対象、package source、exact version rule を追記する。
- `doc/workflow_engine_spec.md` 16.5 の cache 記述に、`Dotnet.Script.Core` の dependency cache と repo の NuGet lock file は別物であることを追記する。
- `doc/workflow_engine_spec.md` 17.2 に lock 欠落、lock 不一致、浮動版禁止、通常検査の外部通信非依存を検証対象として追加する。
- `doc/workflow_engine_spec.md` 11.3 または error code 節に `SCRIPT_NUGET_LOCK_MISSING`、`SCRIPT_NUGET_LOCK_MISMATCH`、`SCRIPT_NUGET_RESTORE_FAILED` の使い分けを追記する。
- `doc/workflow_engine_spec.md` 19.3 / P10 周辺に、T27 は `#r "nuget: ..."` の reproducibility、T28 は `#load "nuget: ..."` の script package 読み込みである境界を追記する。

実装候補ファイル:

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - NuGet directive 収集、lock verifier 呼び出し、restore / compilation context との接続。
- `src/Devo6.WorkFlow.Engine/CsxEntryLoaderOptions.cs` 相当の分離、または現行 `CsxEntryLoaderOptions`
  - lock file path、package sources、dependency resolver 注入口の追加候補。
- `src/Devo6.WorkFlow.Engine/CsxNuGetLockFile.cs`、`CsxNuGetLockVerifier.cs`、`DotnetScriptNuGetDependencyResolver.cs` のような小さな内部型。
- `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
  - lock 欠落、不一致 error code の追加候補。
- `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
  - execution path の lock 検査。
- `tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`
  - validation path の lock 検査。
- `doc/workflow_engine_spec.md`
  - T27 contract 更新。

非推奨の独自実装範囲:

- `dotnet restore` 起動、temp csproj 生成、`project.assets.json` から runtime assembly を拾う処理の全面再実装。
- NuGet source resolver、NuGet script package resolver の独自再実装。
- `Dotnet.Script.Core` の dependency cache を repo lock file として外部 contract 化すること。
- `#load "nuget: ..."` の T27 先取り実装。

レポート更新:

- このレポートの空欄箇所を更新した。
- focused textlint は成功した。cspell は repo の ignore 設定により reports 配下を skip した。`npm run lint:md:targets` でも reports 配下は対象外であることを確認した。

## リスク

- `Dotnet.Script.Core` 2.0.1 の public API は restore / dependency resolution には十分だが、lock file contract は public API として存在しない。repo 側の thin wrapper が必要になる。
- `ScriptCompiler.CreateCompilationContext` は restore と compile context 作成をまとめて行うため、lock mismatch をどの時点で検出するかを設計しないと、不要な restore や compile が走る可能性がある。
- direct reference だけの lock にすると transitive dependency drift を検出できない。runtime dependency graph まで lock するなら target framework、runtime identifier、package source、asset selection の扱いを設計する必要がある。
- absolute runtime assembly path を lock file に入れると machine dependent になる。lock には package id/version と相対 asset identity を入れる方がよい。
- 現在の `許可されたNuGet参照はPackage型を使って実行できる` は実 NuGet restore 経路を通る。cache が無い環境では通常 `dotnet test` が外部通信に依存する恐れがあるため、T27 で test 分離または local source fixture 化が必要である。
- `ScriptParser` は `#load "nuget: ..."` を package reference として扱うため、T27 で採用すると T28 の境界を誤って開くリスクがある。
- `PackageVersion.IsPinned` と repo の浮動版禁止 rule は完全一致しない。dotnet-script の型を使う場合も、repo の exact version policy を別途固定する必要がある。
- GitHub source は package nuspec の repository commit と一致する commit を確認したが、source package / symbol package は restore 済み cache 内には無かった。
