# T45 NuGet ロック任意化 レビュー報告

## 対象

- `git diff` の未コミット差分全体
- 主対象: `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs`、`src/Devo6.WorkFlow.Cli/Program.cs`、`tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs`、`tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`、`tests/Devo6.WorkFlow.Tests/SampleWorkflowTests.cs`、`README.md`、`doc/workflow_engine_spec.md`

## レビュー担当

- T45 専任レビュー担当: Codex
- 親がマネージャーであり、本レビューでは実装担当の変更を戻さず、レビュー報告のみ作成した。

## レビュー基準

- NuGet lock file は既定で任意であり、欠落時は通常の NuGet 解決へ進むこと。
- lock file がある場合だけ directReferences、resolvedDependencies、metadata、verifyPackageSources を検証すること。
- `--locked` または `RequireNuGetLock=true` の場合だけ lock file 欠落を `SCRIPT_NUGET_LOCK_MISSING` として失敗させること。
- NuGet.config があればフィード元指定は通常の NuGet 設定へ委ねる説明になっていること。
- 浮動 version 禁止、未許可 NuGet 参照拒否、ロック不一致検出が壊れていないこと。
- XML コメントは日本語で、全関数とプロパティにコメントがあること。
- 関数名は英語であること。

## 指摘

重大な指摘なし。

バグ、退行、期待仕様漏れに該当する重大または中程度の問題は見つからなかった。

### Low: T45 完了証跡にレビュー報告がまだ含まれていない

- 場所: `tasks-status.md:48`、`phases-status.md:29`
- 内容: T45 / P22 は完了扱いになっているが、証跡欄には実装報告までが記録されており、本レビュー報告がまだ含まれていない。
- 影響: 実装仕様や利用者動作には影響しない。進捗管理上、レビュー完了後の証跡が追いづらくなる可能性がある。
- 修正提案: 親マネージャーの progress sync で `reports/t45-nuget-lock-optional-review-20260608123000.md` を T45 / P22 の証跡へ追記する。

## 確認内容

- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:430` から `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:445` で、lock file 欠落時は `RequireNuGetLock=false` なら provider 解決へ進み、`RequireNuGetLock=true` の場合だけ `SCRIPT_NUGET_LOCK_MISSING` を投げる。
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:448` から `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:470` で、lock file が存在する場合の directReferences、metadata、resolvedDependencies の検証は維持されている。
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:373` から `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:394` で、浮動 version と未許可 NuGet 参照の拒否は lock file 有無より前の検査として維持されている。
- `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:729` から `src/Devo6.WorkFlow.Engine/CsxEntryLoader.cs:750` で、`verifyPackageSources` は true の場合だけ必須化と照合に使われる。
- `src/Devo6.WorkFlow.Cli/Program.cs:195` から `src/Devo6.WorkFlow.Cli/Program.cs:204` で、CLI の `--locked` が parse され、`src/Devo6.WorkFlow.Cli/Program.cs:46` から `src/Devo6.WorkFlow.Cli/Program.cs:50` で loader option へ渡される。
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:40` から `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:73` で、lock file 欠落時の既定成功が Execute / Validate の両方で検査されている。
- `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:80` から `tests/Devo6.WorkFlow.Tests/NuGetLockContractTests.cs:108` と `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs:458` から `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs:485` で、厳格指定時の欠落失敗が検査されている。
- `README.md:24`、`README.md:90`、`README.md:261` と `doc/workflow_engine_spec.md:1508` から `doc/workflow_engine_spec.md:1510` で、NuGet.config が通常の NuGet 設定として扱われ、lock file は任意である説明になっている。

## 検証結果

- `dotnet test Devo6.WorkFlow.sln --filter "NuGetLock|AllowNuGet|MultiFolderCompositeSample"`: 成功。43 件。
- `git diff --check`: 成功。
- `dotnet test Devo6.WorkFlow.sln --filter CodingStandards`: 成功。3 件。XML コメントと命名規約の検査として確認した。

## Markdown 検査

- `npm run lint:md`: 成功。通常対象 6 件。`reports/` は通常対象外。
- `npm run lint:md:terms`: 成功。`SudachiPy term variants: none`。
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t45-nuget-lock-optional-review-20260608123000.md`: 成功。
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t45-nuget-lock-optional-review-20260608123000.md`: skip。repo 設定の ignorePaths により `reports/` が除外される。
- 分類: full lint は pass、review report の focused textlint は pass、focused cspell は repo 設定により skip。残リスクは報告ファイルの spell 検査が cspell 対象外であること。

## 残リスク

- NuGet 解決は固定 provider による検査が中心であり、実際の外部フィード、認証、proxy、利用者ごとの NuGet.config の挙動まではこのレビュー内では実機検証していない。
- `--locked` の実 CLI process 実行は今回の指定検証コマンドには含めていないが、`Program.Run` 経由の単体検査で loader option 伝達と失敗経路は確認されている。
