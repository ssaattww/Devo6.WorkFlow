# T47 publish workflow 参照用パッケージ作成失敗修正 再レビュー報告

## 指摘事項

- 重大な指摘なし。

## レビュー担当

- 担当: T47 専任レビュー担当として、前回と同じ観点で再レビューした。
- 対象: 現在の未コミット差分全体。

## 対象範囲

- `.github/workflows/publish-nuget.yml`
- `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs`
- `tasks-status.md`
- `phases-status.md`
- `reports/t47-publish-reference-package-no-build-fix-implementation-20260608204000.md`
- `reports/t47-publish-reference-package-no-build-fix-review-20260608205000.md`

## 確認結果

- `.github/workflows/publish-nuget.yml:117` から `.github/workflows/publish-nuget.yml:124` の CLI tool pack step は変更されていない。
- `.github/workflows/publish-nuget.yml:126` から `.github/workflows/publish-nuget.yml:134` の reference package pack step は、`--no-build` と `-p:BuildProjectReferences=false` を併用している。
- `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs:129` から `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs:140` の回帰検査は、reference package pack step の `--no-build` と `-p:BuildProjectReferences=false` を固定している。
- `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs:282` から `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs:297` の helper は英語関数名で、追加 XML コメントは日本語になっている。
- `tasks-status.md:50` と `phases-status.md:31` は、前回指摘した Markdown whitelist violation を起こさない表現に修正されている。

## 検証

- `npm run lint:md`: 成功。CSpell は 6 files checked、issues 0。Markdown whitelist も成功。
- `npm run lint:md:terms`: 成功。`SudachiPy term variants: none`。
- `git diff --check`: 成功。
- `ruby -e 'require "yaml"; YAML.load_file(".github/workflows/publish-nuget.yml")'`: 成功。
- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter PublishWorkflowDisablesProjectReferenceBuildForReferencePackage`: 成功。1 件成功、失敗 0 件。

## Markdown lint gate

- aggregate gate state: pass
- focused/full: repo-local `npm run lint:md` が成功。
- user review requirement: なし。Markdown whitelist、prh、target exclusion の設定変更は行われていない。

## 結論

- 前回の tracking Markdown lint 指摘は解消済み。
- workflow 修正は期待仕様どおり、reference package pack step だけに `-p:BuildProjectReferences=false` を追加しており、CLI tool pack step は不要に変更されていない。
- T47 差分について、再レビュー時点で blocking finding はない。
