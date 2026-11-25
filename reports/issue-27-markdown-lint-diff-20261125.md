# Issue #27: Markdown lint 差分確認

## 比較条件

同じ既存依存 `node_modules`、CSpell 9.8.0、`cspell.config.jsonc`、`tools/lint/markdown-whitelist.yaml`、`tools/lint/markdown-targets.json`、実行用の `review-enforcer` スクリプトを使った。基準 `f78414d369bf751c2b5c77c3bc1c4bf843ecf731`、公開 PR `9aaab38c59431b0aa4d20f9c2f030a0dcdadb429`、現在候補を一時スナップショットまたは作業コピーで検査した。既存 checkout の reset は行っていない。

基準版は対象9文書で CSpell 0件。公開 PR と現在候補は同じ10文書を検査した。公開 PR は228件、現在候補は128件で、追加対象は `doc/issue-27-dotnet10-file-based-app-design.md`。文書別・語別に比べると、基準版から公開 PR で228件増え、公開 PR から現在候補では追加0件、削除100件だった。削除はすべて `tasks-status.md` の記述を日本語へ整えた結果であり、現在の128件は PR に既に含まれていた設計書100件と段階表28件である。

## 公開 PR に含まれる CSpell 指摘

### `doc/issue-27-dotnet10-file-based-app-design.md`（100件、41語）

`Abstractions`×2、`Build`×5、`Devo`、`Issue`×2、`Library`×5、`Main`、`Microsoft`、`analyzer`、`asset`×5、`band`×2、`based`×5、`build`×5、`class`×3、`compile`、`directive`×3、`directives`、`downloads`、`feature`×2、`file`×5、`framework`×3、`generator`、`hash`、`identity`×2、`import`、`informational`、`install`、`lock`×5、`metadata`×3、`only`×3、`overload`×3、`pack`、`package`×5、`property`、`publish`、`restore`×5、`sealed`、`selection`、`store`×2、`target`×2、`tool`×5、`update`

### `phases-status.md`（28件、20語）

`artifact`、`asset`、`base`、`based`、`closed`、`fail`、`failure`、`file`、`fixture`、`graph`、`host`×2、`lock`×2、`main`、`metadata`×3、`mock`、`only`×2、`package`、`restore`×2、`target`、`validate`×3

### `tasks-status.md`（100件、51語）

`Abstractions`×2、`Create`、`Execute`、`Library`×2、`Metadata`、`Reader`×2、`assets`、`based`×4、`build`×5、`class`×2、`ctor`×4、`dependency`、`direct`、`directive`、`exact`、`file`×4、`focused`×3、`forward`、`generic`、`host`×2、`initializer`×2、`interface`、`load`×2、`loader`、`local`、`lock`×5、`marker`×2、`metadata`×5、`module`、`negative`、`only`×4、`package`、`pass`、`passed`×4、`pipeline`、`positive`、`preflight`×2、`publish`、`recorder`、`restore`×5、`roll`、`sealed`、`signature`、`static`、`success`、`tests`×3、`tool`、`transitive`、`unsupported`、`validate`×5、`warning`×2

以上は公開 PR 時点から残る用語候補である。今回の追記について追加許可語候補を提案する必要はない。許可語一覧は変更していない。

## 今回の文書変更と他の検査

`tasks-status.md` の T81〜T85 を日本語で要約し、T86 の検査結果を更新した。公開 PR と比べ、同ファイルの CSpell 指摘100件を除去し、追加は0件。許可語検査は公開 PR 時点で174件あった同ファイルが候補では0件となり、変更文書の検査に成功した。今回追記から新たに増えた指摘はない。

対象文書の textlint は成功。SudachiPy は終了コード0で、公開 PR と同じ `コンストラクタ`／`コンストラクター` の揺れ候補を1組報告した。`git diff --check` は成功。全体 Markdown 集約検査は、公開 PR 由来の CSpell 128件で失敗する。

## リポジトリの PR 更新ゲート

`AGENTS.md` は作業追跡と報告の記録先を指定するが、Markdown 全体合格を PR 更新の条件にはしていない。`package.json` には手動実行用 `npm run lint:md` があり、textlint、CSpell、許可語検査を順に実行する。一方、`.github/workflows/pr-xunit-tests.yml` は restore、format、net8.0/net10.0 の test、diff check を実行し、Markdown lint は実行しない。したがって、リポジトリ内に PR 更新前の全体 Markdown 合格必須というゲートは見当たらない。

変更文書の textlint、許可語、SudachiPy、差分検査は成功した。親担当の指示に従い、ここでは PR を更新しない。全体 CSpell の残り128件は、PR に既に含まれる設計書と段階表に限られ、今回候補では増えていない。
