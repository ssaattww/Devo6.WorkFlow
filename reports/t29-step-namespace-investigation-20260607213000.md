# Sub-agent実行レポート

## タスク

T29 Step 名名前空間化の調査。

## sub-agentを使う理由

名前空間付き Entry 指定と重複検証の影響範囲を、実装前に独立して洗い出すため。

## 対象範囲

- `doc/workflow_engine_spec.md`
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
- `src/Devo6.WorkFlow.Cli/Program.cs`
- 関連 tests

## 対象外

- コード修正
- 設計書編集
- commit
- PR 本文更新

## 実行コマンド

- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-doc-maintainer/SKILL.md`
- `sed -n '1,260p' reports/t29-step-namespace-investigation-20260607213000.md`
- `git status --short`
- `rg -n "CompositeStep|Define\(|CsxEntryLoader|entryName|EntryName|StepName|--entry|Duplicate|重複|Name" src tests doc -g '*.cs' -g '*.md'`
- `rg --files src tests doc | rg '(CompositeStep|CsxEntryLoader|Program|CliRunValidateTests|CsxEntryLoaderTests|CsxEntryValidationTests|workflow_engine_spec)'`
- `nl -ba src/Devo6.WorkFlow.Engine/CompositeStep.cs | sed -n '1,260p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CompositeStep.cs | sed -n '260,620p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CompositeStep.cs | sed -n '620,790p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs | sed -n '1,360p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs | sed -n '360,860p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs | sed -n '860,1160p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs | sed -n '1288,1320p'`
- `nl -ba src/Devo6.WorkFlow.Cli/Program.cs | sed -n '1,240p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs | sed -n '1,320p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs | sed -n '1,360p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs | sed -n '1,340p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '36,62p;1064,1110p;1328,1355p;1418,1435p;1548,1562p;1584,1595p'`
- `rg -n "DuplicateStepName|EntryStepNotFound|WorkflowResult|EntryName|ValidationError|ExecutionTraceStep|StepRegistration|GetCompositeStepName|IsCompositeStep|GetCompositeStepConfigType" src tests -g '*.cs'`
- `rg -n "Define\(\"[A-Za-z0-9_]+\.[A-Za-z0-9_]+|namespaceName|Namespace|EntryIdentifier|Qualified" src tests doc reports -g '*.cs' -g '*.md'`
- `rg -n "publicでなくても|XML|コメント|全関数|全プロパティ|英語" . -g '*.md' -g '*.cs'`
- `dotnet test Devo6.WorkFlow.sln --filter "CsxEntryLoaderTests|CsxEntryValidationTests|CliRunValidateTests"`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t29-step-namespace-investigation-20260607213000.md`
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t29-step-namespace-investigation-20260607213000.md`
- `npm run lint:md`

限定テスト結果: 成功。38 件成功、失敗 0、skip 0。

Markdown 確認結果: 直接 textlint は成功。cspell は repo 設定の `ignorePaths` により `reports/` が対象外で、0 file checked / 1 skipped。`npm run lint:md` は成功。

## 対象ファイル

- `doc/workflow_engine_spec.md`
  - 15.1 は Entry を「名前付きの `CompositeStep`」とし、CLI 例は `--entry Build` である。
  - 15.2 はロード済み `.csx` 全体で公開 Step 名を一意にする契約である。
  - 17.2 は検証対象に「指定 Entry 名の存在」と「公開 Step 名の重複」を含めている。
  - 18.1、18.3、18.4 は `WorkflowResult.EntryName`、log scope の `EntryName` / `StepName`、`ExecutionTraceStep.StepName` を公開表示として扱っている。
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - `CompositeStep.Define(string name)` は `CompositeStepDefinition.Name` に値を保持する。
  - `CompositeStepDefinition.Run` / `RunAsync` は `new CompositeStep<TOut>(Name, ...)` へ `Name` を渡す。
  - `CompositeStep<TOut>.Name` は Entry 名として保持され、`Run` / `RunAsync` / `WithConfig` / `WithCurrentStep` で後続インスタンスへ引き継がれる。
  - `ExecuteWorkflowAsync` は `Name` を `WorkflowResult.EntryName`、log scope の `EntryName`、成功 / 失敗結果に使う。
  - `StepRegistration.Name` は `typeof(TStep).Name` であり、`CompositeStep.Name` とは別で、trace と log の `StepName` に使われる。
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`
  - `Execute(entryPath, entryName)` は `entryName` が空なら `Main` にし、`state.Variables.Where(variable.Name == resolvedEntryName)` で script variable 名を探す。
  - 見つけた variable value が `CompositeStep<>` なら実行し、`CompositeStep.Name` が `resolvedEntryName` と一致するかは確認しない。
  - `Validate(entryPath, entryName)` は全 `CompositeStep<>` variable を集め、`GetCompositeStepName(variable.Value)` の戻り値で重複検証する。
  - `Validate` の存在検証は `entryVariables.Any(variable.Name == resolvedEntryName)` で script variable 名だけを見る。
  - `GetCompositeStepName` は reflection で `CompositeStep<Unit>.Name` property を読む。
  - `PrepareExecutionOptions` の失敗結果は引数の `entryName` を `WorkflowResult.EntryName` に使うため、現状では loader 側の指定名が出る。
- `src/Devo6.WorkFlow.Cli/Program.cs`
  - Usage は `[--entry Name]` である。
  - `TryParse` は `--entry` の値を構文解釈せず文字列のまま `CliCommand.EntryName` に保持する。
  - `run` は `CsxEntryLoader.Execute(entryPath, command.EntryName, ...)`、`validate` は `CsxEntryLoader.Validate(entryPath, command.EntryName, ...)` へそのまま渡す。
  - 成功時の出力は `Succeeded: {result.EntryName}` である。
- `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`
  - 既定 `Main`、指定 `Build`、`#load` 先 variable `Build` の実行を確認している。
  - 既存テストでは variable 名と `CompositeStep.Name` が一致しているため、現状の不一致挙動は固定されていない。
- `tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`
  - `Validate(scriptPath, "Build")` は script variable `Build` の存在を見る想定である。
  - 重複検証は `var Main = CompositeStep.Define("Shared")` と `var Build = CompositeStep.Define("Shared")` を `DUPLICATE_STEP_NAME` として固定している。
- `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`
  - CLI E2E は `--entry Build` が run / validate に渡ることを確認している。
  - 名前空間付き entry、同名短縮名の衝突、表示名の検査はまだない。

## 指摘事項

1. 現状の `entryName` は実装上、実行と存在検証では script variable 名である。一方、設計書は「指定名に一致する `CompositeStep`」と書いており、重複検証は `CompositeStep.Name` を見ているため、entry 識別子の基準が混在している。

2. `CompositeStep.Define("Name")` の名前は `CompositeStep<TOut>.Name` として workflow 実行結果、構造化 log の `EntryName`、CLI 成功出力に出る。T29 で名前空間付きの公開 Entry 名を導入する場合、ここは短い名前ではなく、名前空間付き表示名を出す契約にするのが自然である。

3. 現在の重複検証は「ロード済み `.csx` 全体の `CompositeStep.Name` が同一なら重複」と見なす。script variable 名の重複は C# script 側の compile 問題になるが、loader の独自重複検証対象ではない。

4. T29 で満たすべき contract は、少なくとも以下である。
   - `CompositeStep` は短い Step 名と任意の名前空間名を持つ。
   - `CompositeStep` は CLI / loader / result / log 表示用の名前空間付き Entry 名を持つ。
   - 名前空間が異なれば同じ短い Step 名は共存できる。
   - 同一名前空間内で同じ短い Step 名が複数ある場合は `DUPLICATE_STEP_NAME` で失敗する。
   - 名前空間なしの既存 `CompositeStep.Define("Build")` と `--entry Build` は互換維持する。
   - `--entry Deploy.Build` は名前空間付き Entry 名として解決できる。
   - `--entry Build` が複数候補に一致する場合は曖昧な指定として失敗させるか、名前空間付き指定を要求する。既存 error code を使うなら `DUPLICATE_STEP_NAME` より `ENTRY_STEP_NOT_FOUND` ではなく、別 code の追加余地がある。既存 code に閉じるなら検証エラー message で曖昧性を明示する必要がある。

5. 表現候補の比較。
   - `CompositeStep.Define("Build", namespaceName: "Deploy")`:
     - 既存 `Define("Build")` と互換性が高い。
     - 短い Step 名と名前空間を型上分けられるため、同一名前空間内重複の判定 key を作りやすい。
     - XML コメントと設計書で `Name` / `NamespaceName` / `QualifiedName` の意味を明確にしやすい。
     - overload 追加、保持 property 追加、reflection helper 追加が必要。
   - `CompositeStep.Define("Deploy.Build")`:
     - API 追加が少ない。
     - 既存の `Name` をそのまま完全名として扱える。
     - ただし短い Step 名と名前空間の区切り規則が文字列 convention になり、既存 `Define("A.B")` を使っている利用者がいた場合に意味が変わる。
     - 「同一名前空間内の重複」を実装するには parsing が必要で、名前に dot を許すかどうかの breaking 判断が必要になる。
   - `--entry Deploy.Build`:
     - CLI 指定としては自然で、既存 `--entry Build` と同じ option に載せられる。
     - `TryParse` は値を透過するため、CLI parser の実装規模は小さい。
     - loader 側で script variable 名ではなく公開 Entry 名を解決する変更が必要。
   - `var Deploy_Build = CompositeStep.Define("Build", namespaceName: "Deploy")`:
     - script variable 名は C# の識別子制約に任せ、公開 Entry 名は `Deploy.Build` として扱える。
     - 既存の variable 名一致探索から公開 Entry 名探索へ切り替える必要がある。

6. 推奨は `CompositeStep.Define("Build", namespaceName: "Deploy")` と `--entry Deploy.Build` の組み合わせである。理由は、既存の `Define("Build")` / `--entry Build` を維持しながら、短い Step 名、名前空間名、名前空間付き Entry 名を contract として明示でき、重複検証も文字列 parsing に依存しにくいためである。`Define("Deploy.Build")` は実装が小さく見えるが、名前の文字種、dot の扱い、短い名前の表示が曖昧になりやすい。

7. 解決順序の推奨。
   - `entryName` が dot を含む場合は名前空間付き Entry 名として `CompositeStep.QualifiedName` と完全一致させる。
   - `entryName` が dot を含まない場合は、互換のため短い `CompositeStep.Name` と一致させる。
   - 短い名前で複数候補がある場合は、名前空間付き指定を要求する検証エラーにする。
   - script variable 名による解決は互換リスクがあるため、T29 で完全廃止するか、公開 Entry 名未一致時の fallback とするかを親側で決める必要がある。設計書の文言と重複検証に合わせるなら、公開 Entry 名を主にする方が一貫する。

8. trace / log / result の扱い。
   - `WorkflowResult.EntryName`、CLI 成功出力、log scope `EntryName` は名前空間付き Entry 名を出すのがよい。これにより `Deploy.Build` と `Test.Build` をログ上で区別できる。
   - `ExecutionTraceStep.StepName` と log scope `StepName` は現状どおり実行された Step 型名を維持するのがよい。T29 の対象は Entry の公開名であり、内部 Step 型名まで名前空間化すると trace の意味が変わる。
   - `CompositeStep.Name` を短い名前として残す場合、表示用に `QualifiedName` または `EntryName` property を追加する必要がある。

9. TDD で先に置くべきテストケース。
   - `CompositeStep.Define("Build", namespaceName: "Deploy")` が `Name == "Build"`、`NamespaceName == "Deploy"`、`QualifiedName == "Deploy.Build"` を持つ。
   - `CompositeStep.Define("Build")` は `NamespaceName` が空または null、`QualifiedName == "Build"` で既存互換を維持する。
   - `CsxEntryLoader.Execute(scriptPath, "Deploy.Build")` が `var DeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")` を実行し、`WorkflowResult.EntryName == "Deploy.Build"` になる。
   - `CsxEntryLoader.Validate(scriptPath, "Deploy.Build")` が名前空間付き Entry を成功検証する。
   - `Deploy.Build` と `Test.Build` が同じ読み込み単位に共存し、validate が成功する。
   - `Deploy.Build` が 2 つある場合は `DUPLICATE_STEP_NAME` になる。
   - `Build` が namespace なしと namespace ありで共存する場合の期待を明示する。推奨は `Build` と `Deploy.Build` は別 key として共存可である。
   - 短い `--entry Build` が一意なら既存どおり成功する。
   - 短い `--entry Build` が `Deploy.Build` / `Test.Build` の複数候補に当たる場合は失敗し、message が名前空間付き指定を要求する。
   - CLI E2E: `engine run main.csx --entry Deploy.Build` が exit code 0 で該当 Step を実行し、標準出力に `Succeeded: Deploy.Build` を出す。
   - CLI E2E: `engine validate main.csx --entry Deploy.Build` が exit code 0 になる。
   - CLI E2E: 同一名前空間重複がある `engine validate main.csx --entry Deploy.Build` は exit code 1 で `DUPLICATE_STEP_NAME` を出す。
   - `#load` 先にある `Deploy.Build` を entry script から `--entry Deploy.Build` で解決できる。
   - `WithConfig<TConfig>()` 後も namespace metadata と `QualifiedName` が維持される。
   - `Run` / `RunAsync` / `Produce` / `StoreAs` / `Discard` など、`CompositeStep<TOut>` を返す chain 後も namespace metadata が維持される。

10. ユーザー標準に関する T29 注意点。
    - 追加する API 名、helper 名、test method 名は英語にする。既存の日本語テスト名を触る場合は、T29 差分内では英語名へ寄せる必要がある。
    - 新規または変更する public / internal / private の関数、constructor、property、record property、nested type には日本語 XML コメントを付ける。
    - `CompositeStep.Define` overload、`NamespaceName`、`QualifiedName`、entry 解決 helper、重複 key helper、CLI E2E の helper を追加する場合、すべてコメント対象になる。
    - 既存 `CsxEntryLoader` には英語 XML コメントとコメントなし private helper が残っているため、T29 で触った箇所は日本語コメントへ更新する。触らない既存不足は T31 対象として扱うのが過去の運用と一致する。
    - `.csx` test fixture 内の Step class / Execute method も、新規追加分は日本語 XML コメントを付けると review 指摘を避けやすい。

## 結果

- 推奨 contract は「短い Step 名」と「名前空間名」と「名前空間付き Entry 名」を分けることである。
- API は `CompositeStep.Define(string name, string? namespaceName = null)` または既存 signature と overload を追加し、`CompositeStep<TOut>` に `NamespaceName` と `QualifiedName` などの公開 metadata を持たせるのがよい。
- CLI は既存 `--entry` を維持し、値として `Deploy.Build` を受ける。新 option は不要である。
- loader は script variable 名ではなく `CompositeStep` metadata から entry を解決する方向が設計と整合する。
- 重複検証は `QualifiedName` を key にする。これにより `Deploy.Build` と `Test.Build` は共存し、`Deploy.Build` 同士は失敗する。
- `WorkflowResult.EntryName`、CLI 成功出力、log scope `EntryName` は `QualifiedName` を出す。`ExecutionTraceStep.StepName` は既存どおり Step 型名を出す。
- 現状の限定テスト `CsxEntryLoaderTests|CsxEntryValidationTests|CliRunValidateTests` は成功しており、T29 実装前の既存契約は維持されている。
- Markdown full lint は成功している。ただし repo 標準対象は `reports/` を除外するため、このレポート自体の spell check は skipped である。

## リスク

- 現行実装が script variable 名で entry を探しているため、公開 Entry 名解決へ切り替えると `var X = CompositeStep.Define("Build"); --entry X` のような未テスト利用が壊れる可能性がある。設計書上は `CompositeStep` 名を entry とするため、互換 fallback を入れるかは親側で判断が必要である。
- `Define("Deploy.Build")` 形式も許す場合、dot を名前区切りとして扱うか、単なる名前文字として扱うかが曖昧になる。推奨案ではこの曖昧性を避けるため、名前空間は `namespaceName` 引数で分ける。
- 短い `--entry Build` の曖昧一致をどの error code にするか未確定である。既存 code だけで進めるなら message の明確化が必要だが、より正確には曖昧指定用の error code 追加も検討余地がある。
- `CompositeStep<TOut>` の constructor と chain method が多く、namespace metadata の引き継ぎ漏れが起きやすい。`WithConfig`、`Run`、`RunAsync`、`WithCurrentStep` を重点的に検査する必要がある。
- log / result 表示を `QualifiedName` に変えると、既存 assertion の `EntryName == "Build"` は namespace なしでは維持されるが、namespace あり新規ケースでは期待値を明確化する必要がある。
- ユーザー標準により、T29 実装で触る private helper と test helper にも日本語 XML コメントが必要である。既存のコメントなし private helper を触る場合、修正範囲が広がる可能性がある。
- `reports/` は repo の Markdown 対象から除外されているため、この調査レポートは full lint の直接対象ではない。直接 textlint は通したが、spell check は repo 設定上 skipped である。
