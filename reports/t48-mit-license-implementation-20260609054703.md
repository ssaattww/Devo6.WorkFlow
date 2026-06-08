# T48 MIT ライセンス追加 実装報告

## 範囲

- ルートに `LICENSE` を追加する。
- `README.md` にライセンス節を追加する。
- 進捗を `tasks-status.md` と `phases-status.md` に記録する。

## 実施内容

- 既存のパッケージ情報で `PackageLicenseExpression` が `MIT` になっていることを前提に、標準的な MIT ライセンス本文を追加した。
- 著作権表示は既存のパッケージ `Authors` に合わせて `Devo6` とした。
- 利用者が確認できるように `README.md` の末尾にライセンス節を追加した。

## 検証

- `npm run lint:md` に成功した。
- `npm run lint:md:terms` に成功した。
- `git diff --check` に成功した。
- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter ReadmeDisplaysStatusAndNuGetBadges` に成功した。

## 補足

- 利用者指示により、検査先行の新規検査追加は行わなかった。
- Markdown whitelist は、README と進捗ファイルで必要になった `MIT` と `ライセンス` のみ追加した。
