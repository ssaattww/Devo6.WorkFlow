# 文書検査

Markdown 文書を確認するため、repo の最上位で次を実行する。

```bash
npm install
npm run lint:md
```

SudachiPy で語彙を分類する場合は次も用意する。

```bash
python3 -m venv .venv
. .venv/bin/activate
python3 -m pip install -r tools/lint/requirements.txt
```

対象文書を確認する場合は次を実行する。

```bash
npm run lint:md:targets
```

設計書などの語彙候補を分類する場合は次を実行する。

```bash
.venv/bin/python tools/lint/extract-sudachi-vocabulary-chunked.py --files doc/workflow_engine_spec.md
```

この repo 固有の許可語は `tools/lint/markdown-whitelist.yaml` に置く。表記揺れの修正規則は `tools/lint/prh.yml` に置く。

許可語や表記規則を追加、変更、削除する場合は、具体的な差分を確認してから反映する。
