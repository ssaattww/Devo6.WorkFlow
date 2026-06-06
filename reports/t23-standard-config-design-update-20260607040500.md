# Sub-agent実行レポート

## タスク

T23「標準 Config 読み込みと `StepContext` 格納」の設計更新。

## sub-agentを使う理由

親が設計更新を worker に委譲しており、Config 契約、CLI 契約、Engine 実行前処理、検証仕様の更新を最小差分で行うため。

## 対象範囲

- `doc/workflow_engine_spec.md`
- `reports/t23-standard-config-design-update-20260607040500.md`

## 対象外

- 実装変更
- README 更新
- T24 の `--set` override 仕様決定
- 複数 Config、名前付き Config、Config 型自動推論
- `validate` での Config 型変換検証

## 実行コマンド

- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-executor/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-doc-maintainer/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/report-output-manager/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `sed -n '1,220p' reports/t23-standard-config-design-impact-20260607034500.md`
- `rg -n "^#{1,4} (6\\.3|6\\.4|6\\.5|6\\.6|10\\.2|11\\.3|14\\.5|17\\.2|17\\.4|19\\.1|19\\.2|19\\.3|21\\.2|21\\.3)|Config|WorkflowExecutionOptions|CompositeStep|CONFIG_" doc/workflow_engine_spec.md`
- `sed -n '1,220p' tools/lint/README.md`
- `git diff -- doc/workflow_engine_spec.md`
- `npm run lint:md`
- `npm run lint:md:terms`
- `git diff --check`
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t23-standard-config-design-update-20260607040500.md`

## 対象ファイル

- `doc/workflow_engine_spec.md`
- `reports/t23-standard-config-design-update-20260607040500.md`

## 指摘事項

なし。

## 結果

`doc/workflow_engine_spec.md` を更新し、T23 の標準 Config 読み込みを engine 実行前処理として定義した。

採用した内容:

- Entry 側の標準 Config 型宣言は `CompositeStep.Define("Main").WithConfig<AppConfig>()` とする。
- `WithConfig<TConfig>()` は Entry の Config 型メタ情報を保持し、Step 専用引数は増やさない。
- CLI `run` は Entry `.csx` ロード後、Entry の Config 型メタ情報と `--config` の path から YAML を型付き Config に変換し、Step 実行前に `StepContext.Set<TConfig>(config)` で登録する。
- `--config` 未指定で Entry が `WithConfig<TConfig>()` を使う場合は `CONFIG_NOT_FOUND` とする。
- 存在しない Config ファイルは `CONFIG_NOT_FOUND` とする。
- 読み込み不能、YAML 構文エラー、型変換失敗、`DataAnnotations` または `IValidatableObject` の失敗は `CONFIG_LOAD_FAILED` とする。
- 空 Config は Config 型を生成でき、検証に通れば成功とする。

対象外として残した内容:

- T23 では `--set` を標準 Config に反映せず、`EngineArguments.Settings` に保持するだけとする。
- T23 では単一 Config 型のみを扱い、複数 Config、名前付き Config、Config 型自動推論は扱わない。
- `validate` は T23 では Config path 存在確認までを必須とし、Config 型変換検証は後続に残す。

検証結果:

- `npm run lint:md`: pass
- `npm run lint:md:terms`: pass
- `git diff --check`: pass
- 新規 report の focused textlint: pass

## リスク

- `YamlDotNet` は候補として設計に記載したが、実装時の依存追加と細かな変換挙動は実装 task で確定する必要がある。
- Config 型変換の `validate` 対応は T23 の必須対象外にしたため、`run` と `validate` の検証範囲に差が残る。
