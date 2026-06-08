# T49 allow-nuget 任意化 実装報告

## 範囲

- `--allow-nuget` 未指定時の NuGet 参照拒否をやめる。
- `--allow-nuget` 指定時は、その一覧に含まれない NuGet 直接参照を拒否する互換挙動を残す。
- README と設計書を現在の契約に合わせる。

## 検査先行

- CLI `run` が `--allow-nuget` なしで固定 NuGet 参照を通常解決へ渡す検査を追加した。
- CLI `validate` が `--allow-nuget` なしで固定 NuGet 参照を通常解決へ渡す検査を追加した。
- Engine の NuGet lock 検査 helper を、空の許可一覧が制限なしであることを確認できる形に変更した。
- 実装前に対象検査が失敗することを確認した。

## 実装

- `CsxEntryLoader` の NuGet 参照検証を、許可一覧が空の場合は固定 version 検査だけ行うように変更した。
- 許可一覧が 1 件以上ある場合は、従来通りパッケージ ID と version の一致を必須にした。
- CLI の XML コメントを、`--allow-nuget` が任意の参照制限である説明へ更新した。
- README のサンプル実行コマンドから `--allow-nuget` を外した。
- 設計書の NuGet 参照契約を、通常解決と任意の参照制限に更新した。

## 検証

- `dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --filter "AllowNuGet|NuGetLock|明示制限外|restricted"` に成功した。
- `dotnet test Devo6.WorkFlow.sln --filter CodingStandards` に成功した。
- `dotnet test Devo6.WorkFlow.sln` に成功した。
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes` に成功した。
- `npm run lint:md` に成功した。
- `npm run lint:md:terms` に成功した。
- `git diff --check` に成功した。
