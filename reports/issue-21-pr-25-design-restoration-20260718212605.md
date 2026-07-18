# Sub-agent 実行レポート

## タスク

- 目的: 圧縮された階層ログ設計書を、`3ea20ec` の PR #25 取り込み時点にある詳細版を基礎として復元し、T78 の契約を最小限追記する。
- タスク種別: 設計文書の復元、Markdown 検査、許可語設定の承認済み項目追加。
- 実施日時: 2026-07-18。

## sub-agent を使う理由

- 理由: 利用者指定の Terra / medium で、原文の情報保持、日本語の手直し、Markdown 検査を担当するため。
- 実行設定: terra / medium。

## 対象範囲

- 復元対象: `doc/issue-21-hierarchical-logging-design.md`。
- 設定対象: `tools/lint/markdown-whitelist.yaml` の承認済み項目だけ。
- 報告対象: `reports/issue-21-pr-25-design-restoration-20260718212605.md`。

## 対象外

- 実装コード、検査コード、進捗文書、履歴登録は変更していない。
- `CompositeStep` の既存許可語項目は変更しておらず、`Composite` の別名も追加していない。
- 不承認の許可語候補は追加していない。

## 原文保持の照合

- `git show 3ea20ec:doc/issue-21-hierarchical-logging-design.md` で取得した 655 行を復元の基礎にした。
- 最終版は 666 行であり、600 行以上という条件を満たす。空行を除く行数は原文 453 行、最終版 461 行である。
- 見出しは原文 62 個、最終版 62 個で、見出し水準の並びも完全に一致した。19 章とすべての小節、変更対象ファイルを表す見出しを保持した。
- fenced code block は原文 30 個、最終版 30 個で、内容と順序が完全に一致した。C#、JSON、ログ本文、階層図、出力例は変更していない。
- 原文の inline code は 160 個あり、最終版で欠落した識別子群は 0 件だった。最終版は T78 の追記により 179 個になった。
- 表の行数は原文 11 行、最終版 11 行である。箇条書きは原文 57 行、最終版 58 行で、追加 1 行は T78 の変更対象である。
- 文字数は原文 15,445 文字、最終版 14,953 文字で、492 文字減った。減少は普通英文を自然な日本語へ置き換えたためであり、行数、章、小節、段落、表、コード例、ログ例の圧縮や削除によるものではない。
- `git diff 3ea20ec --numstat -- doc/issue-21-hierarchical-logging-design.md` は 202 行追加、191 行削除と表示した。削除表示は主に同じ意味単位を日本語へ書き換えた行であり、情報ブロックの削除はない。

## T78 の追加箇所

- 7.2 に、非協調の Step 本体の `await` 直後にキャンセル要求を確認し、確定時は同期の `step.Produce` と成功記録を実行しない契約を追加した。
- 7.2 に、同期処理である `step.Produce` の直前と直後にもキャンセル要求を確認し、確定後の `Step succeeded` を抑止する契約を追加した。
- 7.3 に、内側の Step 列の `await` 直後にキャンセル要求が確定している場合、`Composite succeeded` を抑止する契約を追加した。
- 7.3 に、外側の再試行の `Attempt` を、`Attempt` を持たない内側の Step へ継承し、内側の明示値を優先する契約を追加した。
- 14、15.6、15.7、16 に対応する変更対象、検査計画、受け入れ条件を追記した。
- 15.10 に、Windows のファイル出力検査では記録出力部品を破棄し、ファイルハンドルを解放してから内容を読む手順を追加した。

## 許可語設定

- 利用者が exact entry を承認した後、`JSON`、`Text`、`Windows`、`Lambda Step`、`logger category`、`snapshot`、`span`、`scope chain`、`Microsoft.Extensions.Logging`、`リリースノート` の 10 項目だけを追加した。
- `scope chain` は最初の説明候補を採用せず、利用者が承認した親から子への説明を exact に追加した。
- `logger category` の説明は承認どおり `Microsoft.Extensions.Logging` を含む形で維持し、そのライブラリ名も別の承認済み項目として追加した。
- `リリースノート` は exact entry の承認後に追加し、PR #25 原文の 4 箇所を「公開時の説明」から「リリースノート」へ戻した。
- PR #25 原文で使われていた承認済み専門語は本文の元の位置へ戻した。普通語だけを日本語化した。
- 通常文を backtick や引用符で検査から隠していない。inline code は識別子、型、キー、メソッド、ファイルパス、実際の出力値に限った。

## 実行コマンド

- 原文取得: `git show 3ea20ec:doc/issue-21-hierarchical-logging-design.md`。
- focused textlint: Codex 同梱の Node.js で `node_modules/textlint/bin/textlint.js --config .textlintrc.json --rulesdir <review-enforcer>/scripts/textlint-rules doc/issue-21-hierarchical-logging-design.md`。
- focused whitelist: `check-markdown-whitelist.js --files doc/issue-21-hierarchical-logging-design.md`。
- focused cspell: `run-cspell-markdown.js doc/issue-21-hierarchical-logging-design.md`。Windows の `.cmd` 起動は終了状態を返さず失敗したため、同じスクリプトが生成する一時設定を維持したまま `node_modules/cspell/bin.mjs` を Node.js で起動する代替経路を使った。
- full 対象列挙: `list-markdown-targets.js`。対象は `AGENTS.md`、設計書 2 件、`phases-status.md`、最上位 `README.md`、サンプルの `README.md`、`tasks-status.md`、`tools/lint/README.md` の 8 文書だった。
- full textlint: focused と同じ設定で上記 8 文書を明示指定した。
- full whitelist: `check-markdown-whitelist.js` を対象指定なしで実行した。
- full cspell: focused と同じ設定生成処理と Windows 代替経路で上記 8 文書を明示指定した。
- 差分検査: `git diff --check`。

## 対象ファイル

- 変更: `doc/issue-21-hierarchical-logging-design.md`。
- 変更: `tools/lint/markdown-whitelist.yaml`。
- 変更: `reports/issue-21-pr-25-design-restoration-20260718212605.md`。
- 読み取り確認: full 検査対象の残り 7 文書、`AGENTS.md`、指定された 5 skill、`tools/lint/` の検査設定。

## 検査結果

- focused textlint: 成功、終了値 0。
- focused whitelist: 成功、終了値 0。許可語設定自体の読み込みと整合性確認を含む。
- focused configured cspell: 1 文書、問題 0、終了値 0。
- 本報告の focused textlint と focused whitelist: 成功、終了値 0。
- 本報告の configured cspell: `reports/` が `ignorePaths` の対象であるため 1 文書を除外し、検査対象 0 文書として終了値 0。pass ではなく設定どおりの skip と分類する。
- full textlint: 8 文書、成功、終了値 0。
- full whitelist: 8 文書、成功、終了値 0。
- full configured cspell: 8 文書、問題 0、終了値 0。
- `git diff --check`: 成功、終了値 0。改行形式に関する Git の注意表示だけで、空白エラーはなかった。
- 集約判定: pass。

## 指摘事項

- 復元時の原文保持照合では、章、小節、コード例、ログ例、表、inline code の欠落はなかった。
- T78 の初稿で出力生成処理を非同期と読める記述があったため、`step.Produce` が同期処理であること、および直前と直後の確認へ修正した。
- Sol / high の P2 指摘に対応し、表見出しの `` `Category` `` を通常文の `logger category` へ変更した。inline code による通常文の検査回避を解消した。
- 同じ P2 指摘に含まれた「公開時の説明」4 箇所は、`リリースノート` の exact entry 承認後に原文の用語へ戻した。
- full whitelist の途中実行では、別担当が変更中だった `tasks-status.md` に 1 件の未知語があった。別担当の本文修正後に再実行し、最終的に 8 文書すべて成功した。
- 専用のレビュー結果は別のレビュー報告へ記録する。本報告は復元作業と検査証跡を扱う。

## 最新同期後の再検査

- 親担当が T79 / P33 を完了同期した後の最新作業木で、full 8 文書を再検査した。
- full textlint: 8 文書、成功、終了値 0。
- full whitelist の最初の再検査では、`tasks-status.md:77` の「コード・ログ」と「ブロック」の 2 件を未知語として検出した。親担当が「コード例とログ例30件」へ修正した後に再実行し、8 文書すべて成功、終了値 0 となった。
- full configured cspell: 8 文書、問題 0、終了値 0。
- `git diff --check`: 成功、終了値 0。改行形式の注意表示だけで、空白エラーはなかった。
- 最新同期後の集約判定は pass である。当担当は許可どおり本報告以外を変更していない。

## 結果

- PR #25 取り込み時点の詳細設計を 666 行の文書として復元し、原文の全情報を保持した。
- T78 のキャンセル、試行番号継承、Windows ファイル読み取り契約を、既存設計へ局所的に追加した。
- 承認済みの許可語項目だけを反映し、focused と full の全 Markdown 検査を成功させた。
- Sol / high の P2 対応後に focused と full の textlint、whitelist、configured cspell、および `git diff --check` を再実行し、すべて成功した。

## リスク

- cspell の共有スクリプトは Windows で `cspell.cmd` を直接起動できなかった。今回は共有スクリプトが生成した同一設定を使う Node.js 代替経路で検査したため、設定差はないが、Windows の直接起動問題自体は残る。
- `reports/` は `markdown-targets.json` と `cspell.config.jsonc` の対象外である。本報告は要求された full 8 文書には含まれない。明示指定した textlint と whitelist は成功したが、configured cspell は設定どおり skip となった。
- 実装コードと実行時検査は本作業の対象外であり、T78 契約の実装確認は後続作業で必要になる。
