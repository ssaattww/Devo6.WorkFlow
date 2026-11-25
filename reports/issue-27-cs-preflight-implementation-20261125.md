# 課題 #27 `.cs` preflight 実装報告

## 対象と識別情報

- Repository: `ssaattww/Devo6.WorkFlow`
- Branch: `master`
- 実装前 HEAD: `f78414d369bf751c2b5c77c3bc1c4bf843ecf731`
- 現在の HEAD: 同じ。変更は未コミット。
- 作業対象: T82 / P36。設計合格版 `doc/issue-27-dotnet10-file-based-app-design.md`（blob `6f393a2f1d35825ecabbca002d51af7b2505cdd0`）
- 作業開始時から残っていた未コミットの設計書と追跡変更は保持した。設計書本文は変更していない。

## 範囲

`.csx` は既存 CLI / `CsxEntryLoader` 経路に委譲し続ける。`.cs` を拡張子で先に分岐し、現在のホスト TFM、単一ファイルの許可 directive、必須 `devo6.nuget.lock.yaml` の存在を restore より前に検査する。禁止・不正 directive と net10 以外のホストは安定した診断コードで失敗させる。

この作業単位は preflight と fail-closed の接続までである。有効な `.cs` 入力も、preflight 成功後に `SCRIPT_BUILD_FAILED` を返す。正常な restore、build、metadata-only validate、型共有、Provider / Step 実行は未実装で、成功として扱っていない。

## 変更

- `src/Devo6.WorkFlow.Cli/Program.cs`: `.cs` の拡張子分岐を loader / Config 設定より前に追加。実行時 TFM は `AppContext.TargetFrameworkName` を用いる。テスト用に `Run` の内部引数で TFM を注入可能にした。
- `src/Devo6.WorkFlow.Cli/CsFileInputPreflight.cs`: `OutputType=Library`、`TargetFramework=net10.0`、`PublishAot=false`、Engine の厳密版 `#:package` を要求し、不許可 directive、浮動版、重複 property/package、行頭でない directive を拒否する。入力隣接の専用 lock file がなければ `SCRIPT_NUGET_LOCK_MISSING`。
- `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`: `SCRIPT_FILE_REQUIRES_NET10_HOST`、`SCRIPT_FILE_DIRECTIVE_INVALID`、`SCRIPT_BUILD_FAILED` を追加。
- `tests/Devo6.WorkFlow.Tests/CsFileInputPreflightTests.cs`: TFM 拒否、Engine のみ参照する場合の必須 lock 欠落、禁止・不正 directive、preflight 成功でも未実装経路を成功扱いしない検査。
- `tasks-status.md` / `phases-status.md`: T82 完了、P35 設計完了、P36 実装進行中、T83 の次境界を記録。
- 詳細証拠: `reports/issue-27-cs-preflight-implementation-20261125.md`。

`.csx` は拡張子分岐に入らず従来の loader に進む。版情報や `.nuspec` を読む変更は加えていない。既存 `CliRunValidateTests` 43 件はこの経路の焦点回帰として通過した。

## 検証

SDK 10.0.404 は `https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.404/dotnet-sdk-10.0.404-linux-x64.tar.gz` から今回限りの `/tmp/devo6-issue27-sdk-10.0.404` に展開した。Linux `x86_64` を確認し、公式提示 SHA-512 と一致した。

```text
577d0eb17fec33d029104c5c63a1ab0accc3e28776274abef2218dbac5af18d2109b5406d26b31d76f5c245215e3ba0926a94c20246c672c41847c9e1eb1dc50
```

依存 restore はリポジトリに既存の固定 PackageReference のみで実行し、NuGet cache と CLI home は上記一時領域に隔離した。インストールした SDK は 10.0.404、含まれる runtime は 10.0.15。`.NET 8` runtime は追加していないため `DOTNET_ROLL_FORWARD=Major` を設定し、net8.0 target の検査 assembly を runtime 10.0.15 で実行した。

最終焦点コマンド:

```sh
dotnet test tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj --no-restore \
  --filter 'FullyQualifiedName~CsFileInputPreflightTests|FullyQualifiedName~CliRunValidateTests' \
  --logger 'console;verbosity=minimal'
```

結果: 成功、50 passed、0 failed、0 skipped（preflight 7 件と既存 CLI run/validate 43 件）。対象 project、参照 project はすべて `net8.0` で build 成功。`git diff --check` も成功。

初回検査では、新規 test fixture の raw string delimiter が不正で `CS8997` 等のコンパイル診断が出た。fixture を行配列に直して再実行し、最終 50 件の成功を確認した。

## 検証境界と保留

net10 host 分岐はテストから TFM 文字列を注入して preflight の単体・CLI接続を検証した。実際の net10.0 製品 asset / tool host はまだありません。新規 `.cs` の拒否経路は loader と NuGet graph provider に到達しないことを検査し、このコードには SDK restore process 呼び出しを実装していない。ただし SDK restore の独立 spy / 呼出し回数カウンターはまだ存在せず、0 回という証拠は実 process instrument ではなく入口の分岐と provider 呼出し回数である。

未確認:

- net10.0 の CLI / Engine / Abstractions asset と tool package install/update、実際の .NET 10 host 選択。
- File-based SDK の restore/build、lock 内容と package version の assets 照合、上位版しかない source の挙動。
- 配布 `.nuspec` と埋め込み API からの package version 取得。
- PEReader / MetadataReader の non-execution、Entry 検証、依存 DLL と型同一性。
- 正常な `.cs` run / validate と .NET 8 runtime のみの実機回帰。

## 次の作業境界

T83 は CLI と埋め込み入口が共有する .NET 10 file-based build pipeline を追加する。net8.0 / net10.0 の multi-target 資産、SDK 10.0.404 の選択、実配布 package version、厳密な direct / transitive lock 検査、専用出力先、初回 restore 1 回と `build --no-restore` を対象にし、Engine だけの lock 欠落・不正入力で restore ゼロを runner spy でも確認する。PE metadata-only validate と Provider / Step 実行は後続単位に分ける。

## 公開・レビュー境界

commit、push、CI、PR、Issue の更新は行っていない。独立レビューや自己変更のレビュー判定は実施していない。現在の作業ツリーは親へレビュー用に渡す未コミット差分である。
