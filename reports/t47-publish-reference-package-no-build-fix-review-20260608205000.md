# T47 publish workflow 参照用パッケージ作成失敗修正 レビュー報告

## 指摘事項

- 重大度: Blocking
  - `phases-status.md:31` と `tasks-status.md:50` の T47 進捗文言が Markdown whitelist に反しており、期待仕様の「Markdown / tracking の文言が lint に反しない」を満たしていない。最終確認の `npm run lint:md` は `phases-status.md:31` の `PR` と `ブランチ`、`tasks-status.md:50` の `ビルド` を whitelist violation として検出し、exit code 1 で失敗した。
  - 対応方針: 進捗文言を既存 whitelist に合う表現へ直すか、必要な用語として扱う場合は repo-local lint 設定の追加をユーザー確認付きで行い、`npm run lint:md` を再実行する必要がある。

## レビュー担当

- 担当: T47 専任レビュー担当として現在の Codex セッションでレビューした。
- 備考: ユーザー指示により、親はマネージャーであり、このセッションは変更を戻さずレビューに徹した。

## 対象範囲

- `.github/workflows/publish-nuget.yml`
- `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs`
- `tasks-status.md`
- `phases-status.md`
- `reports/t47-publish-reference-package-no-build-fix-implementation-20260608204000.md`

## 確認結果

- `.github/workflows/publish-nuget.yml:117` から `.github/workflows/publish-nuget.yml:124` の CLI tool pack step は変更されていない。
- `.github/workflows/publish-nuget.yml:126` から `.github/workflows/publish-nuget.yml:134` の reference package pack step は `--no-build` と `-p:BuildProjectReferences=false` を併用している。
- `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs:129` から `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs:140` の回帰検査は、`Pack reference package` step に `--no-build` と `-p:BuildProjectReferences=false` が含まれることを固定している。
- `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs:282` から `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs:297` の helper は英語関数名で、追加 XML コメントは日本語になっている。
- 実装報告は失敗 step、原因、修正内容、TDD、検証、残リスクを記録しており、workflow 変更と矛盾していない。

## 検証

- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter PublishWorkflowDisablesProjectReferenceBuildForReferencePackage`: 成功。1 件成功、失敗 0 件。
- `ruby -e 'require "yaml"; YAML.load_file(".github/workflows/publish-nuget.yml")'`: 成功。
- `git diff --check`: 成功。
- `npm run lint:md:terms`: 成功。`SudachiPy term variants: none`。
- `npm run lint:md`: 失敗。CSpell は issues 0 だが、Markdown whitelist が `phases-status.md:31` と `tasks-status.md:50` を検出した。

## Markdown lint gate

- aggregate gate state: failed gate
- focused/full: repo-local `npm run lint:md` を実行し、tracking Markdown の whitelist violation により失敗。
- user review requirement: whitelist 追加が必要な場合は、repo-local 設定の exact entry をユーザー確認してから実装する必要がある。

## 結論

- publish workflow 修正と回帰検査そのものには、期待仕様に反する重大なコード指摘は見つからなかった。
- ただし tracking Markdown が lint に反しているため、T47 はこのまま完了扱いにできない。
