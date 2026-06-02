# Sub-agent実行レポート

## タスク

- 目的: 機械置換された設計書差分の不自然な箇所をレビューで洗い出す
- タスク種別: review

## sub-agentを使う理由

- 理由: review-enforcerに従い、完了判断前のレビューを親ではなく専任sub-agentで実施するため

## 対象範囲

- 対象: 未コミット差分のうち、主に `doc/workflow_engine_spec.md` の機械置換結果。関連する `tools/lint/markdown-whitelist.yaml`、`tools/lint/README.md`、`package.json`、`cspell.config.jsonc`、`tasks-status.md` も必要に応じて確認する。
- レビュー観点: 意味が崩れた置換、不自然な日本語、英語と日本語の不自然な混在、lint逃れに見える表現、設計用語として英語を残すべき箇所の過剰変換。

## 対象外

- 対象外: レビュー指摘の修正実装、whitelistの新規提案、設計内容そのものの仕様レビュー、コミット作成。

## 実行コマンド

- `sed -n '1,240p' reports/task-mechanical-replacement-review-20260602162320.md`
- `sed -n '1,240p' /home/ibis/AI/CodexSkill/skills/review-enforcer/SKILL.md`
- `sed -n '1,260p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,260p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `git status --short --branch`
- `git diff -- doc/workflow_engine_spec.md`
- `git diff --word-diff=plain -- doc/workflow_engine_spec.md`
- `git diff --unified=0 -- doc/workflow_engine_spec.md`
- `rg -n "行内 NuGet|正規 path|正規 path化|公開 引数なし|主 コンストラクタ|記号的 リンク|キャッシュ key|version 指定|依存 復元|directive 走査|script パッケージ|edge 条件|Flow 一覧|Step 一覧|未知 key|トップレベル statement|package source|sourceを|assemblyのみ|path配下|AssemblyLoadContext アンロード" doc/workflow_engine_spec.md`
- `git diff -- tools/lint/markdown-whitelist.yaml tools/lint/README.md package.json cspell.config.jsonc tasks-status.md`
- `rg -n "日本語表記|description:.*表記|の日本語" tools/lint/markdown-whitelist.yaml tools/lint/README.md package.json cspell.config.jsonc tasks-status.md doc/workflow_engine_spec.md`
- `npm run lint:md`
- `npm run lint:md:terms`
- `nl -ba doc/workflow_engine_spec.md | sed -n '<確認範囲>p'`
- `nl -ba tools/lint/markdown-whitelist.yaml | sed -n '54,66p'`

## 対象ファイル

- 変更したファイル: `reports/task-mechanical-replacement-review-20260602162320.md`
- 主確認対象: `doc/workflow_engine_spec.md`
- 補助確認対象: `tools/lint/markdown-whitelist.yaml`、`tools/lint/README.md`、`package.json`、`cspell.config.jsonc`、`tasks-status.md`
- 未追跡補助対象: `tools/lint/check-sudachi-term-variants.py` は `npm run lint:md:terms` 経由で実行確認のみ

## 指摘事項

- Blocking normal-path problem:
  - `doc/workflow_engine_spec.md:2956`、`doc/workflow_engine_spec.md:2977`: サンプルファイル見出しの `steps/accepted.csx`、`steps/rejected.csx` が `Step 一覧/accepted.csx`、`Step 一覧/rejected.csx` に置換されている。`steps/` は実ディレクトリ名として示すべき箇所であり、機能名ではなくパスの意味が壊れている。
  - `doc/workflow_engine_spec.md:2426`、`doc/workflow_engine_spec.md:2427`、`doc/workflow_engine_spec.md:2429`、`doc/workflow_engine_spec.md:2448`: `記号的 リンク`、`正規 path化`、`正規 path` が不自然で、`symbolic link` / `canonical path` の技術語を機械的に分断している。lint逃れのために英語片だけを残したように読める。
  - `doc/workflow_engine_spec.md:1352`: `C# レコードの主 コンストラクタ パラメータ` は C# の `primary constructor parameter` の訳として不自然で、`主` だけが浮いている。設計上の用語として残すか、自然な日本語へ統一する必要がある。
  - `doc/workflow_engine_spec.md:2198`: `公開 引数なし コンストラクタ` は `public parameterless constructor` の機械置換で、通常の日本語として読みにくい。API要件の説明なので、`public` と `parameterless constructor` を残すか、自然な日本語へ直す必要がある。
  - `doc/workflow_engine_spec.md:2381`: `トップレベル statement`、`トップレベルの実行可能 statement` は C# の `top-level statement` / `top-level executable statement` を途中だけ置換しており不自然。設計上の機能名として英語を残すか、`トップレベルステートメント` などへ統一する必要がある。
  - `doc/workflow_engine_spec.md:20`、`doc/workflow_engine_spec.md:189`、`doc/workflow_engine_spec.md:2457`: `行内 NuGet参照` は `inline NuGet reference` の技術語として不自然で、`インラインNuGet参照` または原語維持の方が意味を保ちやすい。
  - `doc/workflow_engine_spec.md:2322`、`doc/workflow_engine_spec.md:2468`、`doc/workflow_engine_spec.md:2572`: `version 指定` が複数残っており、英語と日本語の分断が目立つ。`version` を設計語として残すなら周辺も原語寄せ、一般語として訳すなら `バージョン指定` へ寄せる必要がある。
  - `doc/workflow_engine_spec.md:155`、`doc/workflow_engine_spec.md:2715`: `edge 条件` は `edge condition` の一部だけを訳しており、機能名として英語を残すべきか、日本語なら `エッジ条件` などへ統一すべき。
  - `doc/workflow_engine_spec.md:2700`、`doc/workflow_engine_spec.md:2701`、`doc/workflow_engine_spec.md:2702`: `CLI Flow 一覧`、`CLI Step 一覧`、`CLI 型一覧` は `CLI flows` / `steps` / `types` の将来機能名を過剰に日本語化している可能性がある。CLIサブコマンドや機能名であれば原語のまま残すべき。
  - `doc/workflow_engine_spec.md:2562`、`doc/workflow_engine_spec.md:2606`: `directive 走査` は `directive scan` の一部だけを置換しており不自然。`directive` を設計語として残すなら `directive scan`、日本語なら `ディレクティブ走査` などへ統一すべき。
- ユーザー確認が必要な問題:
  - なし。今回の指摘は本文の置換崩れとして修正方針を立てられる範囲であり、仕様判断が必要な新規設計変更は見つけていない。
- 保留でよい非ブロッキング懸念:
  - `doc/workflow_engine_spec.md:1101`、`doc/workflow_engine_spec.md:2430`、`doc/workflow_engine_spec.md:2473`、`doc/workflow_engine_spec.md:2524`、`doc/workflow_engine_spec.md:2576`: `未知 key`、`キャッシュ key`、`依存 復元機構`、`AssemblyLoadContext アンロード` など、同じ機械置換由来の不自然な混在が残っている。上記blocking箇所と同じ方針で一括確認するのがよい。
  - `tools/lint/markdown-whitelist.yaml:62`: `compile` の alias に `・コンパイル` が入っている。本文差分の直接レビュー対象ではないが、句読点付き語が許可語として残っており、lint設定の品質リスクとして後続確認対象にできる。
  - `rg -n "日本語表記|description:.*表記|の日本語" ...` は該当なし。`description` に「〜の日本語表記」のような禁止表現は確認できなかった。

## 結果

- 結果: 指摘あり。`npm run lint:md` は成功し、`npm run lint:md:terms` も `SudachiPy term variants: none` だったが、本文レビューでは機械置換による不自然な日本語、技術語の分断、パス名の意味破壊を確認した。表面的なlint成功だけでは完了扱いにできない。

## リスク

- 未解決のリスクまたは後続対応: `doc/workflow_engine_spec.md` の本文差分は、少なくとも上記blocking箇所の修正または置換方針の再適用が必要。特に技術語を「原語維持するもの」「自然なカタカナ語へ寄せるもの」「一般語として日本語化するもの」に分けないまま再置換すると、lintは通っても読めない設計書になるリスクが残る。
