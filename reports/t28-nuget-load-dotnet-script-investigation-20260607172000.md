# T28 `#load "nuget: ..."` dotnet-script 実装調査

## 目的

T28 で `#load "nuget: ..."` を実装する前に、現在の拒否箇所、dotnet-script 既存実装、lock file との関係、外部通信に依存しない検査方針を確認する。

調査担当はコード編集を行わず、この報告だけを作成した。

## Blocking 前提の注意点

- T28 は `#load "nuget: ..."` を lock 検査に進めるだけでは完了しない。最終 compile 時に Roslyn の source resolver が package 内 `.csx` を読める必要がある。
- 現在の loader は local `#load` を自前で展開し、`#r "nuget: ..."` は lock 検査後に source から取り除いて runtime assembly path を手で追加している。このまま `#load "nuget: ..."` 行だけ残しても、最終 `CSharpScript.Create` 側の `ScriptOptions` に dotnet-script の script map が無いため解決できない。
- `Dotnet.Script.Core` / `Dotnet.Script.DependencyModel` には `#load "nuget: ..."` 用の parser、restore、package 内 script file 抽出、Roslyn source resolver が既にある。T28 で package 展開、contentFiles 走査、`project.assets.json` 解析を再実装するのは方針違反になりやすい。
- dotnet-script の公開文法は `#load "nuget: PackageId, Version"` であり、directive 内に `path/to/file.csx` を足す仕様は確認できなかった。path 指定を足す場合は dotnet-script 互換から外れる。

## 現在の拒否箇所

`src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs` の `LoadScriptFile` が `#load` directive を読み、値が `nuget:` で始まる場合に即座に `CsxReferenceValidationException` を投げる。

- `CsxEntryLoader.cs:266-272`
  - `TryReadDirective(line, "load", out string loadValue)`
  - `loadValue.StartsWith("nuget:", ...)`
  - error code は `WorkflowErrorCodes.ScriptReferenceNotAllowed`
  - message は `#load with nuget references is not supported.`

このため `VerifyNuGetLock` には到達しない。T27 の検査にも、`#load "nuget: CsvHelper, 33.0.1"` は unsupported のまま、fake provider の `ResolveCallCount` が `0` であることを確認する検査がある。

- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:308-326`

## 既存 T27 実装との関係

T27 の lock 検査は、`context.HasNuGetReferences` が true の場合にだけ `devo6.nuget.lock.yaml` を読み、直接参照、metadata、解決済み依存関係を比較する。

- `CsxEntryLoader.cs:391-415`

本番 provider は `Dotnet.Script.Core.ScriptCompiler.CreateCompilationContext<object, object>(...)` を呼び、`RuntimeDependencies` から package / version / runtime assembly path を取得している。

- `CsxEntryLoader.cs:1137-1182`

ただし、現在の `CsxNuGetDependencyGraph` は以下だけを返す。

- 解決済み依存関係
- compile に追加する runtime assembly path
- target framework / runtime identifier / package source / Dotnet.Script.Core version metadata

`RuntimeDependency.Scripts` や package 名から script file への map は保持していない。T28 ではこの情報が必要になる。

## dotnet-script 側の一次情報

ローカル NuGet cache では `Dotnet.Script.Core` と `Dotnet.Script.DependencyModel` の `2.0.1` を確認した。nuspec は repository commit として `ea2da11cf4452d6e8263a6d23c728ebf97cbb915` を指している。

- `~/.nuget/packages/dotnet.script.core/2.0.1/dotnet.script.core.nuspec:4-13`
- `~/.nuget/packages/dotnet.script.dependencymodel/2.0.1/dotnet.script.dependencymodel.nuspec:4-13`

同 commit の公開ソースを `/tmp/dotnet-script-t28.umNGwA` に shallow clone して確認した。

### 公式文法

README は script package の consuming 例として以下の形を示す。

```csharp
#load "nuget:simple-targets-csx, 6.0.0"
```

REPL 例でも `#r` と `#load` の NuGet support は同じ syntax と説明され、`#load "nuget: simple-targets-csx, 6.0.0"` が使われている。

- GitHub README: https://github.com/dotnet-script/dotnet-script
- `/tmp/dotnet-script-t28.umNGwA/README.md:419-427`
- `/tmp/dotnet-script-t28.umNGwA/README.md:502-516`

README では script package は `content` または `contentFiles` 配下に `.csx` を置く通常の NuGet package と説明されている。entry point は root の `main.csx`、root に 1 つだけある `.csx`、または判定不能なら全 script file という扱い。

### NuGet directive parser

`Dotnet.Script.DependencyModel.ProjectSystem.ScriptParser` は `#r` と `#load` の両方から NuGet package reference を読む。

- `ScriptParser.ParseFromCode` は `ReadPackageReferencesFromReferenceDirective` と `ReadPackageReferencesFromLoadDirective` を両方 union する。
- `ScriptParser.ParseFromFiles` も各 `.csx` file について同じ処理をする。
- `ReadPackageReferencesFromLoadDirective` は `DirectivePatternPrefix + "load" + NuGetDirectivePatternSuffix` を使う。

参照:

- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel/ProjectSystem/ScriptParser.cs:19-30`
- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel/ProjectSystem/ScriptParser.cs:35-50`
- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel/ProjectSystem/ScriptParser.cs:68-90`
- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel/ProjectSystem/ScriptParserInternal.cs:7-32`

`NuGetPattern` は `nuget:`、package id、任意の comma + version までであり、package 内 path を明示する第 3 要素は無い。

### restore と dependency context

`RuntimeDependencyResolver.GetDependencies` は `ScriptProjectProvider.CreateProjectForScriptFile` で一時 project file を作り、`dotnet restore` を実行し、`project.assets.json` から dependencies を読む。

- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel/Runtime/RuntimeDependencyResolver.cs:44-49`
- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel/Runtime/RuntimeDependencyResolver.cs:66-73`

`ScriptDependencyContextReader` は `project.assets.json` の lock file target を読み、package folder を `FallbackPackagePathResolver` で解決する。`targetLibrary.ContentFiles` がある場合、package folder から script file dependencies を取得する。

- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel/Context/ScriptDependencyContextReader.cs:36-58`
- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel/Context/ScriptDependencyContextReader.cs:162-186`

### package 内 script file の解決

`Dotnet.Script.DependencyModel.ScriptPackage.ScriptFilesDependencyResolver` は package folder 配下の `.csx` を探し、`contentFiles` または `content` の `csx/{tfm}` 構造から対象を選ぶ。

- `contentFiles` / `content` + `csx` + target framework の regex がある。
- 現状の target framework 選択は `any` 優先、次に `netstandard2.0`。
- entry point は root `.csx` が 1 つならそれ、複数なら `main.csx`、判定できなければ root 配下の script files を返す。

参照:

- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel/ScriptPackage/ScriptFilesDependencyResolver.cs:13-20`
- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel/ScriptPackage/ScriptFilesDependencyResolver.cs:29-58`
- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel/ScriptPackage/ScriptFilesDependencyResolver.cs:68-98`

### Roslyn source resolver

`Dotnet.Script.DependencyModel.NuGet.NuGetSourceReferenceResolver` は `#load "nuget: ..."` の path を package 名として解析し、`scriptMap` に 1 script だけあればその path を返す。複数 script がある場合は memory stream で複数の local `#load` を生成する。

- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel.Nuget/NuGetSourceReferenceResolver.cs:13-21`
- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel.Nuget/NuGetSourceReferenceResolver.cs:37-61`
- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.DependencyModel.Nuget/NuGetSourceReferenceResolver.cs:64-89`

`ScriptCompiler.CreateScriptOptions` は `RuntimeDependency` から `scriptMap` を作り、この resolver を `WithSourceResolver(...)` に設定する。

- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.Core/ScriptCompiler.cs:94-103`

`ScriptCompiler.CreateCompilationContext` はまず runtime dependencies を取得し、その dependencies から script options と assembly references を組み立てる。

- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.Core/ScriptCompiler.cs:137-151`

## T28 の推奨実装方針

### 推奨: dotnet-script の compilation context / RuntimeDependency を再利用する

T28 では、`#load "nuget: ..."` を自前で package cache から探して展開しない。既存実装を使うため、少なくとも以下を dotnet-script に委ねる。

- `#load "nuget: PackageId, Version"` の package reference としての解釈
- temporary project file 作成
- restore
- `project.assets.json` 読み取り
- package folder 解決
- `contentFiles` / `content` 配下の `.csx` 選択
- Roslyn `SourceReferenceResolver` による package script 読み込み

実装の薄い責務は以下に留めるのがよい。

- workflow policy としての許可一覧確認
- 浮動 version 禁止
- `devo6.nuget.lock.yaml` の欠落、不一致、metadata 比較
- direct references / resolved dependencies の比較
- 本リポジトリ固有の public API assembly identity 検査
- local `#load` の workflow root 制限

### 必要になりそうな変更点

現状の `ICsxNuGetDependencyGraphProvider` は `RuntimeDependency.Scripts` を呼び出し元に返さないため、T28 では provider contract の拡張が必要になる可能性が高い。

候補:

1. `CsxNuGetDependencyGraph` に `RuntimeDependencies` または package id -> script paths の map を追加する。
2. `CreateScriptOptions` で `compiler.CreateScriptOptions(context, runtimeDependencies)` を使えるようにし、dotnet-script の `NuGetSourceReferenceResolver` をそのまま使う。
3. `#load "nuget: ..."` が source に残る場合は、最終 compile path も dotnet-script の source resolver 付き `ScriptOptions` を使う。

避けるべきこと:

- package cache の layout を本リポジトリ側で直接前提化する。
- package 内 `contentFiles` 選択規則を独自実装する。
- `project.assets.json` を本リポジトリ側で解析する。
- `#load "nuget: Package, Version, path/to/file.csx"` のような独自文法を先に入れる。

## lock file との関係

推奨は dotnet-script 互換の `#load "nuget: PackageId, Version"` に合わせること。

理由:

- dotnet-script README と実装が同じ文法を示している。
- `ScriptParser` の regex は package id と version を読む構造で、path 第 3 要素は文法化されていない。
- package 内 script file 選択は package layout と entry point 規則で行う設計になっている。
- 設計書は「Dotnet.Script.Core を利用し、restore / project.assets.json / runtime assembly 解決を再実装しない」と明記している。

lock file では、T27 の `directReferences` を「direct NuGet package references」として継続利用し、`#r` と `#load` の両方から得た package id / version を対象にするのが最小で自然。ただし T28 の設計更新では、以下のどちらにするかを明確にした方がよい。

- `directReferences` は directive kind を区別しない package set とする。
- あるいは `directReferences` に `kind: reference|load` 相当を追加し、`#r` と `#load` の許可・lock 不一致を区別する。

セキュリティと期待動作の観点では、`#load` は package 内 script を実行可能 source として取り込むため、`AllowedNuGetReferences` とは別に `AllowedNuGetLoads` を持つ案も検討価値がある。少なくとも設計書で「同じ許可一覧を使うのか、load 用に分けるのか」を決めるべき。

## 外部通信に依存しない検査方針

通常の `dotnet test` は外部 NuGet source に依存させない。

### 単体・契約検査

- 既存の `FakeNuGetDependencyGraphProvider` を拡張し、固定 graph に加えて package script map または runtime dependency script paths を返す。
- `#load "nuget: ..."` が許可済み・lock 一致の場合に provider が 1 回呼ばれることを確認する。
- 許可外 package、浮動 version、lock 欠落、direct reference mismatch、resolved dependency mismatch は fake provider で確認する。
- lock 不一致より前に浮動 version / 許可外が拒否される順序を維持する。
- `#load "nuget: ..."` が source resolver 付き compile path に残る、または dotnet-script compilation context を使うことを contract として固定する。

### local package source fixture

dotnet-script 自身の検査は、script package fixture を `dotnet pack` して local package source から使っている。

- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.Tests/ScriptPackagesFixture.cs:32-45`
- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.Tests/ScriptPackagesTests.cs:21-27`
- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.Tests/ScriptPackagesTests.cs:62-76`
- `/tmp/dotnet-script-t28.umNGwA/src/Dotnet.Script.Tests/ScriptPackagesTests.cs:88-104`

本リポジトリ側でも、必要なら通常 test とは分けて local package source fixture を用意できる。

現実的な案:

- `tests` 配下に script package fixture project を置く。
- test setup で一時 directory に `dotnet pack -o <local-source>` する。
- workflow directory に `NuGet.Config` を作り、local source のみを指す。
- `DOTNET_CLI_HOME` と `NUGET_PACKAGES` を一時 directory に向ける。
- public feed を使わない。
- これは integration category とし、通常の高速 contract tests は fake provider で完結させる。

fixture で確認したいケース:

- package root の `main.csx` が読まれる。
- package root に entry point が無い場合の複数 script 読み込み。
- package 内 script package dependency。
- package 内 local `#load "./sub/file.csx"`。
- package script と workflow local script の循環または重複の扱い。

## T28 完了条件に対して設計更新で明確化すべき事項

- `#load "nuget: ..."` の文法は dotnet-script 互換の `PackageId, Version` のみとし、package 内 path 指定は採用しないこと。
- package 内 `.csx` の配置規則は dotnet-script の script package 規則、すなわち `contentFiles` または `content` 配下の `csx/{tfm}` を使うこと。
- package script entry point は dotnet-script の resolver に委ねること。
- `#load "nuget: ..."` の direct reference を lock file の `directReferences` に含めるか、`kind` 付きで区別するか。
- 許可一覧を `#r` と `#load` で共用するか、load 用に分けるか。
- `#load "nuget: ..."` がある場合、最終 compile は dotnet-script の `NuGetSourceReferenceResolver` が有効な `ScriptOptions` または `ScriptCompilationContext` を使うこと。
- `RuntimeDependency.Scripts` または equivalent な script map を provider contract で扱うこと。
- local `#load` の workflow root 制限と、NuGet package 内 script の package cache path は別の信頼境界として扱うこと。
- 循環検出は、local file の自前検出と Roslyn / dotnet-script source resolver 経由の package script 読み込みで、どこまで同じ error code に寄せるか。
- 重複読み込みは、local file の正規 path 重複と package script の resolver 動作をどう扱うか。
- lock file は絶対 package cache path や `.csx` absolute path を記録しない方針を維持するか。維持するなら script file 内容 hash を lock に入れない理由を明記する。
- 通常 `dotnet test` は fake provider を使い、local package source fixture は必要最小限の integration test に分けること。

## 参照した一次情報

- ローカル NuGet cache:
  - `~/.nuget/packages/dotnet.script.core/2.0.1/dotnet.script.core.nuspec`
  - `~/.nuget/packages/dotnet.script.dependencymodel/2.0.1/dotnet.script.dependencymodel.nuspec`
- dotnet-script 公開リポジトリ:
  - https://github.com/dotnet-script/dotnet-script
  - commit: `ea2da11cf4452d6e8263a6d23c728ebf97cbb915`
  - local checkout: `/tmp/dotnet-script-t28.umNGwA`
- 現行コード:
  - `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`
  - `doc/workflow_engine_spec.md`

## 結論

T28 は `#load "nuget: ..."` の拒否を外すだけではなく、dotnet-script の `RuntimeDependency.Scripts` と `NuGetSourceReferenceResolver` を最終 compile path に通す設計変更が必要である。

文法は dotnet-script 互換の `#load "nuget: PackageId, Version"` を推奨する。package 内 path を directive に追加する独自仕様は、既存実装再利用と互換性を弱めるため推奨しない。
