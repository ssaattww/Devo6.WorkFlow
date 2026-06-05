# issue #1 フル csx 設計更新レポート

## 対象

- issue: <https://github.com/ssaattww/Devo6.WorkFlow/issues/1>
- 添付資料: `reports/attachments/issue-1-csx-workflow-engine-design.md`
- 設計書: `doc/workflow_engine_spec.md`
- 用語設定: `tools/lint/markdown-whitelist.yaml`

## 実施内容

- issue #1 添付の csx ワークフローエンジン設計を参照した。
- `doc/workflow_engine_spec.md` を YAML ワークフロー定義中心の rev4 から、csx 完結型の設計へ置換した。
- ワークフロー定義に YAML を使わず、名前付きの `CompositeStep` を `.csx` で定義する方針を明記した。
- Config はワークフロー定義形式と分離し、実行時入力として `StepContext` に保持する方針へ整理した。
- `dotnet-script core` 表記を `Dotnet.Script.Core` に寄せた。
- 一般語は本文側で日本語化し、公開 API 名だけを whitelist に追加した。

## whitelist 追加語

- `StepInput`: Step へ渡す唯一の入力集合を表す公開 API 型名。
- `CompositeStep`: 複数の Step を順番に実行する Step 型名。
- `IStep`: Step の実行契約を表す公開 API 型名。
- `ConfigStore`: 実行時 Config を保持する補助型名。
- `EngineArguments`: エンジン起動時の引数を保持する補助型名。
- `Produce`: Step の戻り値から下流入力を生成する API 名。
- `StoreAs`: Step の戻り値をそのまま入力集合へ登録する API 名。
- `Discard`: Step の戻り値を入力集合へ登録しない API 名。
- `FailurePolicy`: Step 失敗時の挙動候補を表す列挙型名。

## 検証

- `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md`: 成功。
- `npm_config_cache=/tmp/devo6-workflow-npm-cache npm run lint:md:terms`: 成功。`SudachiPy term variants: none`

## 関連レポート

- `reports/issue-1-full-csx-impact-scan-20260605130308.md`

## 残リスク

- 非同期 API、Config 読み込み責務、CLI override、Step 登録名、`Produce` 後の値の寿命は設計上の未確定事項として残る。
