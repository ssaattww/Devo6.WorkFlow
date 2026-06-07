# T31 標準検査先行報告

## 目的

T31 の C# coding standard を機械的に確認する独立 tool と xUnit 契約検査を追加し、現状の source と tests で失敗することを確認しました。
修正対象コードの標準違反は修正していません。

## 変更ファイル

- `tests/Devo6.WorkFlow.Tests/CodingStandardsContractTests.cs`
- `tools/csharp-xml-doc-checker/CSharpXmlDocChecker.csproj`
- `tools/csharp-xml-doc-checker/Program.cs`
- `reports/t31-standards-failing-tests-20260608060000.md`

`tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj` は変更していません。

## 追加した検査

`tools/csharp-xml-doc-checker` と `CodingStandardsContractTests.CSharpDeclarationsFollowT31CodingStandards` を追加しました。

- `src/**/*.cs` と `tests/**/*.cs` を走査します。
- `bin` と `obj` は除外します。
- string literal、raw string literal、verbatim string literal、通常 comment、block comment 内は宣言検出から除外します。
- 関数と property に XML comment があることを確認します。
- `operator` と `partial` を関数宣言として扱います。
- record primary constructor property ごとに `<param name="...">` があることを確認します。
- 失敗 message は `file:line: 理由` の形式で出します。
- xUnit は `tools/csharp-xml-doc-checker` を起動する契約検査だけを持ちます。

## 期待どおり失敗した検査

`dotnet test Devo6.WorkFlow.sln --filter CodingStandardsContractTests` は exit code 1 で失敗しました。
失敗した検査は `C# declarations follow T31 coding standards` です。

代表的な失敗理由は次のとおりです。

- `src/Devo6.WorkFlow.Cli/Program.cs:72`: 関数 `PrintValidationResult` に XML コメントがありません。
- `src/Devo6.WorkFlow.Cli/Program.cs:193`: 関数 `CliCommand` に XML コメントがありません。
- `tests/Devo6.WorkFlow.Tests/AsyncStepApiContractTests.cs:229`: プロパティ `Values` に XML コメントがありません。
- `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs:193`: 関数 `RecordingLoggerFactory` に XML コメントがありません。
- `tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs:91`: 関数 `CompositeStepは定義順にStepを実行しProduceで型付き値を下流へ渡す` に XML コメントがありません。

## 実行した検証

- `dotnet run --project tools/csharp-xml-doc-checker -- .`: 失敗。T31 違反を検出したため期待どおりです。
- `dotnet test Devo6.WorkFlow.sln --filter CodingStandardsContractTests`: 失敗。T31 違反を検出したため期待どおりです。
- `dotnet format tools/csharp-xml-doc-checker/CSharpXmlDocChecker.csproj --verify-no-changes`: 成功。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes`: 失敗。担当範囲外の `tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs` にある xUnit1024 を検出しました。
- `npm run lint:md`: 失敗。担当範囲外の `tasks-status.md` にある `ゲート` whitelist 違反を検出しました。
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t31-xml-doc-tool-placement-20260608061500.md reports/t31-standards-failing-tests-20260608060000.md`: 成功。
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t31-xml-doc-tool-placement-20260608061500.md reports/t31-standards-failing-tests-20260608060000.md`: skipped。報告書 2 件は ignorePaths 対象でした。
- `git diff --check`: 成功。

## 残リスク

- 検査は追加参照なしの行ベース実装であり、完全な Roslyn parser ではありません。
- string literal と comment の大半は除外しますが、すべての C# 構文を完全には解釈しません。
- property と関数の検出は通常の宣言形を対象にしており、特殊な改行や複雑な宣言形では追加調整が必要になる可能性があります。
- local function の扱いは限定的です。
- `dotnet run` と契約テスト実行時に、NuGet 脆弱性情報 cache の read-only 警告が出ました。
- solution format と Markdown lint には担当範囲外の既存失敗が残っています。
