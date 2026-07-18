# Sub-agent実行レポート

## タスク

- 目的: PR #23 の Markdown whitelist failure を PR 起因と既存 baseline に分離し、利用者確認用の exact entry 候補を作る。
- タスク種別: lint 候補調査。

## sub-agentを使う理由

- 理由: `markdown-word-checker` の `needs user review` gate に必要な evidence を、同じ Terra 担当で継続採取するため。

## 対象範囲

- 対象: `origin/master...HEAD` と現在の tracking/review report 差分に含まれる Markdown、repo-local whitelist checker の focused 実行、未知語の分類、exact YAML entry 候補。

## 対象外

- 対象外: whitelist・`prh`・target exclusion の編集、本文修正、既存 baseline 語の一括是正、実エディタ補完確認、T70 追加検査。

## 実行コマンド

- 実行コマンド: bundled Node を task 専用 `PATH` に置き、`CODEX_SKILLS_DIR=C:\Users\taiga\DotnetWs\CodexSkill\skills` を指定して以下を実行した。
  - `git diff --name-only origin/master...HEAD -- '*.md'`
  - `node tools/lint/run-skill-script.js review-enforcer/scripts/check-markdown-whitelist.js --files <PR #23 の変更 Markdown> --list-unknown`（exit 1）
  - `node tools/lint/run-skill-script.js review-enforcer/scripts/check-markdown-whitelist.js --files tasks-status.md phases-status.md <review report> --list-unknown`（exit 1）
  - `node tools/lint/run-skill-script.js review-enforcer/scripts/check-markdown-whitelist.js --changed --list-unknown`（exit 1。上記 tracking scope と一致）
  - `Get-Content reports/issue-20-pr-23-review-fix-20260718171830.md -Raw | node tools/lint/run-skill-script.js review-enforcer/scripts/check-markdown-whitelist.js --stdin reports/issue-20-pr-23-review-fix-20260718171830.md --list-unknown`（exit 1。reports 除外の影響を分離する診断）
  - `node tools/lint/run-skill-script.js review-enforcer/scripts/check-markdown-whitelist.js --list-unknown`（exit 1、full configured baseline）

## 対象ファイル

- 変更または確認したファイル: PR #23 scope の `doc/issue-20-dotnet-script-compatibility-design.md` と `samples/multi-folder-composite/README.md`、親追加の `tasks-status.md` と `phases-status.md`、`reports/issue-20-pr-23-review-20260718165936.md`、`reports/issue-20-pr-23-review-fix-20260718171830.md`、`tools/lint/markdown-whitelist.yaml`、`tools/lint/markdown-targets.json`、本レポート。`reports/` は `markdown-targets.json` の `ignoreDirectories` にあるため、`--files` と `--changed` では review report を対象にしない。

## 指摘事項

- 指摘要約または「指摘なし」:
  - PR #23 focused scope の未知語は `annotation`、`collection`、`dependency`、`directory`、`dotnet`、`format`、`framework`、`graph`、`import`、`initializer`、`JSON`、`object`、`OmniSharp`、`package`、`README`、`request`、`solution`、`エディタ`、`キャッシュパス`、`コンテキスト`、`サービス`、`パッケージキャッシュ`、`フィールド`、`フロー`、`メンバー`、`リポジトリサンプル` である。`reports/` 配下の PR report 3 件は configured target から除外されるため、この scope の結果へは含まれない。
  - 本文修正候補（設定追加は推奨しない）: `nullable annotation` は「nullable 注釈」、`import` は用途を示す日本語、`dependency graph provider` は「依存グラフ provider」、`request` と `object initializer` は「要求」と「オブジェクト初期化子」、`collection` は既存語「コレクション」、`format` は既存語「フォーマット」、`package` は既存語「パッケージ」、`directory` は既存語「ディレクトリ」、`framework` は既存語「フレームワーク」、`README` は実ファイル名 `README.md` として表記、`リポジトリサンプル` は「リポジトリのサンプル」、`パッケージキャッシュ` は「パッケージのキャッシュ」、`フロー` は「処理の流れ」、`メンバー` は「構成要素」へ置換できる。いずれも backtick または引用符での回避ではない。
  - 既存 whitelist entry の alias 候補: `.NET` entry に `dotnet` を alias として追加する。`dotnet-script` の製品名を本文で通常表記した際の先頭語を許可し、既存 `.NET` 概念と別概念にしないためである。

    ```yaml
    - term: .NET
      aliases:
        - dotnet
      description: C# 実行基盤の名称。
    ```

  - 新しい concept としての term 候補は次の最小集合である。`JSON` は設定構造形式、`OmniSharp` は補完に使う製品名、`solution` は .NET solution、`エディタ` と `言語サービス` は補完責務境界、`フィールド` は非公開フィールド、`キャッシュパス` は `DotnetScriptCachePath` の説明、`nullable コンテキスト` は nullable 診断の条件を表す。発生ファイルは、すべて `doc/issue-20-dotnet-script-compatibility-design.md`、`OmniSharp` と `言語サービス` は加えて `samples/multi-folder-composite/README.md`、`エディタ` も `tasks-status.md` と `phases-status.md` にある。

    ```yaml
    - term: JSON
      description: 設定構造を記述する形式名。
    - term: OmniSharp
      description: C# script の補完に使う言語サービスの製品名。
    - term: solution
      description: .NET の複数プロジェクトをまとめる構成単位。
    - term: エディタ
      description: C# script の補完を提供する編集環境。
    - term: 言語サービス
      description: C# の参照解決と補完を提供する別プロセスの機能。
    - term: フィールド
      description: クラス内部で状態を保持する構成要素。
    - term: キャッシュパス
      description: dotnet-script の復元用キャッシュ基準位置。
    - term: nullable コンテキスト
      description: nullable annotation の診断条件となる C# の有効範囲。
    ```

  - 親追加 tracking scope の未知語は、共有候補 `OmniSharp`、`エディタ`、`言語サービス` を除くと、`aggregate`、`baseline`、`blocking`、`cache`、`check`、`collection`、`CRLF`、`dependency`、`diff`、`disposition`、`draft`、`environment`、`fixture`、`focused`、`format`、`gate`、`graph`、`held`、`high`、`interface`、`loader`、`local`、`medium`、`PR`、`read-only`、`request`、`review`、`reviewer`、`sample`、`skip`、`Sol`、`solution`、`Terra`、`Tests`、`warning`、`Windows`、`xUnit`、`シェル`、`セットアップ`、`ブロッキング`、`リスク`、`レビュー` である。これは task 状態の英語混在または既存 whitelist の日本語同義語であり、本文修正候補として分類する。新たな許可語を推奨しない。
  - review report は configured aggregate 対象外である。強制 stdin 診断では `aggregate`、`baseline`、`blocking`、`cache`、`canonical`、`failed`、`gate`、`hold`、`Node`、`Python`、`review-enforcer`、`spell`、`Sub-agent`、`terms`、`tracking` 等を検出したが、report exclusion を広げる提案はしない。
  - full configured baseline にだけある未知語は `array`、`bash`、`inline`、`PowerShell`、`property`、`shell` で、既存 `README.md` と `doc/workflow_engine_spec.md` に由来する。PR #23 と親追加 tracking に起因しないため、本 review の YAML 候補から除外する。

## 結果

- 結果: PR #23、parent tracking、full configured baseline の 3 scope を分離した。全 scope の whitelist command は未知語があるため exit 1 であり、Markdown aggregate は `needs user review` である。本文修正候補と exact YAML entry 候補を提示したが、whitelist、`prh`、target exclusion、本文、tracking は変更していない。
  - 利用者承認後、提示した `.NET` alias `dotnet` と新規 term 8 件をそのまま適用し、本文修正候補のうち課題 #20 の PR Markdown と T68-T72/P34 を最小修正した。`nullable コンテキスト` の指定 description に含まれる `annotation` は checker の whitelist description 検査で未許可となるため、focused scope に 1 件残る。
  - 利用者承認後に description を `C# コンパイラによる nullable 注釈と警告の扱いを定める設定。` へ更新したが、whitelist checker は `コンパイラ` を未許可として PR focused scope に 1 件検出した。追加 whitelist や本文修正は行わず、aggregate は `failed gate` のままとする。
  - 利用者承認後、`コンパイラ` を `C# のコードをコンパイルするツール。` として追加した。entry 全体、PR focused、T68-T72/P34 の新規行の whitelist はすべて pass となり、課題 #20 の自己起因未知語は解消した。full configured の残存語は課題 #19 と既存 baseline に分離して hold する。

## リスク

- 未解決のリスクまたは後続対応: 利用者は上記 alias 1 件と新規 term 8 件を個別に承認または却下する必要がある。本文修正候補を採用する場合は、承認後に最小の本文変更と focused/full whitelist の再実行が必要である。full configured baseline の既存語は別途扱う必要があり、PR #23 の候補だけを承認しても full gate は自動では pass にならない。
