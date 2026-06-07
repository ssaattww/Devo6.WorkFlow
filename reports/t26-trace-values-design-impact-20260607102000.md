# Sub-agent実行レポート

## タスク

T26 値を含む `ExecutionTrace` の設計影響と実装方針を調査する。

## sub-agentを使う理由

ユーザー指示により、調査、実装、レビューは sub-agent に委譲する。

## 対象範囲

- `ExecutionTrace` と `ExecutionTraceStep` の既存契約
- `CompositeStep` の `Produce` / `StoreAs` 登録処理
- T25 で固定した登録済み値の境界
- `doc/workflow_engine_spec.md` の T26 関連節
- 必要な検査観点の提案

## 対象外

- 設計書、C# 実装、検査コードの編集
- T27 以降の NuGet ロックファイルと `#load "nuget: ..."`
- T29 以降の README と全面コメント標準対応

## 実行コマンド

- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/design-doc-maintainer/SKILL.md`
- `sed -n '1,220p' /home/ibis/AI/CodexSkill/skills/tdd-executor/SKILL.md`
- `sed -n '1,240p' /home/ibis/AI/CodexSkill/skills/sub-agent-task-manager/SKILL.md`
- `sed -n '1,240p' reports/t26-trace-values-design-impact-20260607102000.md`
- `rg -n "T26|値を含む|ExecutionTrace|trace|StepInput|Produce|StoreAs" tasks-status.md phases-status.md doc/workflow_engine_spec.md`
- `sed -n '1,220p' src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
- `rg -n "ExecutionTrace|Trace|StepInput|Produce|StoreAs|Produced|producer|Retry|Timeout|Cancellation|Duplicate" src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `rg -n "ExecutionTrace|Trace|StepInput|Produce|StoreAs|Produced|Retry|Timeout|Cancellation|Duplicate" tests src -g '*.cs'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '92,150p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '390,480p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '660,705p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1320,1478p'`
- `nl -ba doc/workflow_engine_spec.md | sed -n '1678,1709p'`
- `sed -n '1,260p' reports/t25-produce-value-lifetime-design-impact-20260607074500.md`
- `sed -n '1,260p' reports/t25-produce-value-lifetime-final-rereview-20260607095000.md`
- `nl -ba src/Devo6.WorkFlow.Engine/CompositeStep.cs | sed -n '80,150p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CompositeStep.cs | sed -n '150,370p'`
- `nl -ba src/Devo6.WorkFlow.Engine/CompositeStep.cs | sed -n '520,615p'`
- `nl -ba src/Devo6.WorkFlow.Abstractions/StepInput.cs | sed -n '1,220p'`
- `nl -ba src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs | sed -n '1,220p'`
- `nl -ba src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs | sed -n '1,130p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs | sed -n '1,190p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/ProduceValueLifetimeContractTests.cs | sed -n '70,180p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/RetryExecutionContractTests.cs | sed -n '1,150p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/RetryExecutionContractTests.cs | sed -n '150,270p'`
- `nl -ba tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs | sed -n '1,220p'`
- `rg -n "Serialize|Json|TRACE_SERIALIZATION_FAILED|TraceSerialization|ExecutionTrace" src tests -g '*.cs'`
- `git status --short --branch`
- `sed -n '1,240p' /home/ibis/AI/CodexSkill/skills/markdown-word-checker/SKILL.md`
- `ls tools/lint package.json .textlintrc.json cspell.config.jsonc 2>/dev/null`
- `sed -n '1,200p' tools/lint/README.md`
- `sed -n '1,220p' package.json`
- `sed -n '1,160p' tools/lint/markdown-targets.json`
- `npx textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t26-trace-values-design-impact-20260607102000.md`
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t26-trace-values-design-impact-20260607102000.md`

## 対象ファイル

- `tasks-status.md`
  - T26 は未着手で、値を含む `ExecutionTrace` の保存値の種類、形式、秘匿または明示有効化規則、直列化できない値の失敗または除外を検査で確認する task である。
- `phases-status.md`
  - P9 は進行中で、T25-T26 を値寿命と trace 拡張の phase とし、秘匿と失敗時挙動まで確認する完了条件を持つ。
- `doc/workflow_engine_spec.md`
  - `StepInput` は `Produce` と `StoreAs` で追加された値を後続すべての Step へ保持する追記型集合である。
  - `Produce` と `StoreAs` の登録値は `Type` または `Type + name` で識別し、重複登録は実行時エラーである。
  - retry、timeout、外部キャンセルでは失敗した試行の `Produce`、`StoreAs`、`Discard` を実行せず、値を `StepInput` に残さない。
  - `ExecutionTrace` は初期版で値そのものを保存せず、T25 では値候補の基礎単位を「成功して `StepInput` に登録された値」として境界だけを定め、保存形式、秘匿、直列化できない値の扱いは T26 送りになっている。
- `src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
  - 現在の公開契約は `ExecutionTrace.Steps` と `ExecutionTraceStep` の Step 名、状態、所要時間、エラーコード、試行番号だけであり、値用の型またはプロパティはない。
- `src/Devo6.WorkFlow.Abstractions/StepInput.cs`
  - 内部 `Add<T>` は `StepValueKey` の重複で `InvalidOperationException` を投げる。公開 API は取得系中心で、登録値一覧を公開していない。
- `src/Devo6.WorkFlow.Abstractions/StepValueKey.cs`
  - 値キーは `Type` と nullable name で構成され、trace 値の型名と名前を作る根拠にできる。
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - `Produce` は `Action<StepInput, object?>` として保持され、現在は登録された値のメタデータを返せない。
  - engine 経路では Step 本体成功後に producer を実行し、その後に成功 trace を追加する。producer 失敗は retry 対象外で、失敗 trace と `STEP_EXECUTION_FAILED` になる。
  - Step 本体失敗、timeout、外部キャンセルでは producer が実行されないため、対象 Step の trace 値は空にすべきである。
- `src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
  - 現在は `LoggerFactory`、`EngineArguments`、`StepTimeout`、`Retry` があり、trace 値保存を制御する option はない。
- `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
  - `TRACE_SERIALIZATION_FAILED` は存在するが、trace 値直列化処理はまだ存在しない。
- `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
  - 既存テストは `ExecutionTrace` が値を公開しないことを固定しているため、T26 で更新または置換が必要である。
- `tests/Devo6.WorkFlow.Tests/ProduceValueLifetimeContractTests.cs`
  - T25 後の値寿命、重複登録失敗、retry 成功試行だけの値可視性を利用者目線で確認している。
- `tests/Devo6.WorkFlow.Tests/RetryExecutionContractTests.cs`
  - 失敗試行、timeout、外部キャンセル、producer 失敗が producer を実行しない、または retry しないことを確認している。
- `tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs`
  - timeout と外部キャンセルで producer と後続 Step が止まることを確認している。
- `reports/t25-produce-value-lifetime-design-impact-20260607074500.md`
  - T26 の基礎単位は、Step 本体が成功し、timeout や外部キャンセルがなく、producer が成功して `StepInput` に追加された値にできる、と整理している。
- `reports/t25-produce-value-lifetime-final-rereview-20260607095000.md`
  - T26 の trace 値保存形式、秘匿規則、直列化できない値の扱いは対象外として残っている。
- `tools/lint/README.md`、`package.json`、`tools/lint/markdown-targets.json`
  - Markdown lint の repo-local 手順を確認した。`reports/` は full target から除外されているため、対象レポートは focused lint で確認する必要がある。

## 指摘事項

1. T26 は設計書更新が必要である。値を含む trace は公開 API、実行結果の観測形式、秘匿規則、失敗時挙動を変えるため、`design-doc-maintainer` の観点では実装前に `doc/workflow_engine_spec.md` の `18.4 トレース` と `21.5 トレース値の保存` を更新すべきである。

2. trace に保存する値の基礎単位は、T25 の「Step 成功後に `Produce` または `StoreAs` の値登録処理が成功して `StepInput` に登録された値」を維持するのがよい。Step 出力全体、Config、初期 `StepInput`、`StepContext` 内の共有値を T26 で一緒に扱うと、秘匿対象と寿命境界が広がりすぎる。

3. 現行 `StepRegistration` は producer を `Action<StepInput, object?>` として保持しているため、trace 値を作るには producer の内部表現を変更する必要がある。候補は、selector 結果、`StepValueKey`、登録元種別、trace capture policy を持つ producer descriptor に置き換える案である。

4. 値保存は既定無効にすべきである。設計書は初期版で値を保存しないと明記しており、値には Config 由来、引数由来、中間成果物、秘匿情報が含まれうる。T26 では少なくとも、明示有効化しない限り `ExecutionTraceStep.ProducedValues` は空になる検査が必要である。

5. 秘匿値の自動判定は T26 で固定しない方がよい。型名、プロパティ名、`ToString()` の内容だけで secret を安全に判定できない。T26 は明示的な除外または redacted 指定を API として用意し、自動秘匿検出、属性ベース秘匿、property 単位秘匿は後続へ送るのが妥当である。

6. 直列化できない値は workflow 全体を失敗させず、個別 trace value を `NotSerialized` または `SerializationFailed` として値本文なしで残す案を推奨する。trace は観測情報であり、利用者の Step 実行成功を trace 直列化可否で失敗へ変えると通常経路を壊す。既存の `TRACE_SERIALIZATION_FAILED` は、trace 全体の外部保存や将来の hard-fail option 用に残すか、T26 では未使用であることを設計書に明記する。

7. 失敗時挙動は次のように固定すべきである。
   - Step 本体失敗: 失敗 trace を追加し、当該試行の trace 値は空。
   - retry 途中失敗: 失敗 attempt の trace 値は空。成功 attempt だけに登録済み値を載せる。
   - timeout、外部キャンセル: producer を実行しないため trace 値は空。後続 Step は trace に追加しない。
   - producer selector 失敗: producer は登録完了していないため trace 値は空で、Step は `STEP_EXECUTION_FAILED`。
   - 重複登録失敗: 重複した値は保存しない。複数 producer の途中で前段 producer が成功済みの場合に、部分登録値を failed trace に載せるかは危険なので、T26 では「failed trace には値を載せない」と固定し、可能なら producer 登録を trace 収集上は全体成功後に確定する。

8. 公開 API 候補は以下である。新規 public 型、プロパティ、メソッドには、後続実装で日本語 XML コメントが必要である。
   - `ExecutionTraceStep.ProducedValues`
   - `ExecutionTraceValue`
   - `ExecutionTraceValueCaptureStatus`
   - `TraceValueCaptureMode` または `TraceValueCapturePolicy`
   - `WorkflowExecutionOptions.TraceValueCapture`
   - `CompositeStep<TOut>.Produce(..., TraceValueCapturePolicy traceValuePolicy)` の overload
   - `CompositeStep<TOut>.StoreAs(TraceValueCapturePolicy traceValuePolicy)` の overload

9. `ExecutionTraceStep` の primary constructor に値引数を追加すると既存呼び出し箇所への影響が大きい。互換性を優先するなら、既存 constructor は維持し、`ProducedValues` を init property または追加 overload で持たせる案が安全である。

## 結果

推奨設計案は、`ExecutionTraceStep` に「その Step の成功した post-processing で `StepInput` に登録された値」の snapshot を載せる案である。

値の単位は Step attempt ごとの produced value である。`StepInput` 全体の最終状態や、Step 出力全体、Config、`StepContext` は T26 の保存対象にしない。`StoreAs` は `Produce<TOut>(x => x)` の省略形なので、trace 上も produced value として扱う。`Discard` は新規値を作らないため trace 値を追加しない。

値の形式は、値本文とメタデータを分けるのがよい。候補は次の要素である。

- `TypeName`: `StepValueKey.ValueType.FullName ?? Name`
- `Name`: 名前付き Produce の name。型キーのみの場合は null。
- `Source`: `Produce` または `StoreAs`
- `CaptureStatus`: `Captured`、`Redacted`、`Excluded`、`NotSerializable`、`Disabled` など
- `SerializedValue`: 明示有効化された場合だけ入る JSON 文字列
- `DisplayValue`: T26 では原則使わない。使う場合も `ToString()` 既定呼び出しではなく明示 formatter が必要である。
- `ErrorMessage`: 直列化失敗理由を利用者が判断できる短い message。秘匿情報を含めない。

秘匿と明示有効化は二段階にするのが安全である。既定では値本文を保存しない。値本文を保存するには workflow 実行 option と producer 側 policy のどちらか、または両方で明示する。T26 の最小実装では、少なくとも「既定無効」「明示した produced value だけ metadata または serialized value を保存」「明示 redacted または excluded 指定では値本文を残さない」を検査で固定する。

直列化は `System.Text.Json` を候補にできる。新しい NuGet 依存を追加せず、net8.0 の標準 API で実装できる。循環参照、delegate、stream、例外を投げる getter などは個別値の `NotSerializable` として扱い、workflow result は成功のままにする案を推奨する。

代替案と却下理由は次のとおりである。

- 既定で全値を保存する案: 中間値、Config 由来値、引数由来値、秘匿値を意図せず保存するため却下する。
- Step 出力全体を保存する案: `Produce` で明示された下流用値だけを渡すという設計とずれ、`Discard` の意味も曖昧になるため却下する。
- `StepInput` 全体 snapshot を各 Step に保存する案: 後続へ保持される過去値まで毎回複製し、秘匿、メモリ、差分説明が複雑になるため却下する。
- 直列化失敗で workflow を失敗させる案: trace の観測都合で成功 workflow を失敗へ変えるため、T26 の既定動作としては却下する。将来 option としては検討余地がある。
- `ToString()` を既定の表示値にする案: 秘匿値漏えいと非安定表示のリスクが高いため却下する。

設計書更新箇所は次である。

- `18.4 トレース`: 値を含む trace の既定無効、保存対象の単位、秘匿または明示有効化、失敗時の空値規則を追記する。
- `21.5 トレース値の保存`: 未確定記載を T26 の採用契約に置き換え、値形式、直列化不可の扱い、後続送り範囲を明示する。
- `18.1 実行結果`: producer 失敗、retry、timeout、外部キャンセル時の trace 値有無を補足する。
- `18.2 エラーコード`: `TRACE_SERIALIZATION_FAILED` を T26 で使うか、将来の trace 外部保存用として残すかを明記する。
- `7.3 Produce`、`7.4 StoreAs`、`7.5 Discard`: trace 値保存 policy を `Produce` / `StoreAs` に紐づけ、`Discard` は trace 値を生成しないことを追記する。

検査候補は英語関数名で追加するのがよい。

- `DefaultTraceDoesNotCaptureProducedValues`
- `ExplicitTraceCaptureRecordsTypedProducedValue`
- `ExplicitTraceCaptureRecordsNamedProducedValue`
- `StoreAsTraceCaptureRecordsStoredOutput`
- `DiscardDoesNotAddTraceValue`
- `RedactedProducedValueDoesNotExposeSerializedValue`
- `ExcludedProducedValueLeavesNoTraceValue`
- `NonSerializableProducedValueIsMarkedWithoutFailingWorkflow`
- `StepBodyFailureDoesNotCaptureProducedValues`
- `RetryCapturesOnlySuccessfulAttemptProducedValues`
- `TimeoutDoesNotCaptureProducedValues`
- `ExternalCancellationDoesNotCaptureProducedValues`
- `ProduceFailureDoesNotCaptureProducedValues`
- `DuplicateProduceFailureDoesNotCaptureDuplicateTraceValue`
- `TraceValueCaptureApiExposesStablePublicContract`

実装候補ファイルは次である。

- `src/Devo6.WorkFlow.Abstractions/ExecutionTrace.cs`
  - `ExecutionTraceStep.ProducedValues`、`ExecutionTraceValue`、capture status enum を追加する候補。
- `src/Devo6.WorkFlow.Engine/WorkflowExecutionOptions.cs`
  - trace 値保存 option を追加する候補。
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
  - producer を descriptor 化し、selector 結果、`StepValueKey`、登録元種別、capture policy を収集してから `StepInput` へ登録し、成功 trace に値 snapshot を渡す候補。
- `src/Devo6.WorkFlow.Abstractions/WorkflowErrorCodes.cs`
  - `TRACE_SERIALIZATION_FAILED` の扱いを実装方針に合わせて確認する候補。
- `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`
  - 値なし trace 固定テストを T26 契約へ更新する候補。
- `tests/Devo6.WorkFlow.Tests/TraceValueContractTests.cs`
  - T26 用に新設する候補。
- `tests/Devo6.WorkFlow.Tests/RetryExecutionContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/ProduceValueLifetimeContractTests.cs`
  - 既存失敗時挙動に trace value assertion を補強する候補。

T26 で決めずに後続へ送るべき範囲は次である。

- secret の自動検出、属性ベース秘匿、property 単位秘匿。
- trace 全体の永続化ファイル形式、CLI 出力形式、外部 storage 連携。
- trace value のサイズ上限、切り詰め、サンプリング。
- 直列化失敗を workflow 失敗にする optional strict mode。
- 既存 `Execute` / `ExecuteAsync` の戻り値だけを返す経路へ trace を導入するかどうか。

このレポートは、既存の見出し順と既存文を保持し、未記入箇所だけを埋めた。

Markdown focused textlint は成功した。focused cspell は repo 設定の `ignorePaths` により `reports/` 配下が skip され、0 files checked、issues 0 で終了した。

## リスク

現行 producer は action であり、値キーや selector 結果を trace へ渡せない。T26 実装では内部構造を変える必要があるため、`Produce`、`StoreAs`、`Discard` の既存挙動を壊すリスクがある。

複数 producer の途中失敗では、現行実装上は前段 producer が `StepInput` に値を追加済みになりうる。T26 で failed trace に値を載せない契約にする場合、内部登録を全体成功後に確定するか、少なくとも trace 収集上は failed step の部分値を捨てる必要がある。

`ExecutionTraceStep` の public contract を変えるため、既存 constructor、record equality、テストの更新範囲に注意が必要である。新規 public API には日本語 XML コメントが必要である。

値本文を JSON として保存すると、循環参照、巨大 object、例外 getter、非公開情報漏えいのリスクがある。T26 では既定無効、明示 opt-in、直列化失敗時の値本文なし記録を固定するべきである。

`TRACE_SERIALIZATION_FAILED` は既に公開定数だが、T26 推奨案では既定の workflow 失敗には使わない。設計書に扱いを明記しないと、実装者が直列化失敗を workflow failure と誤解する可能性がある。

`reports/` は `tools/lint/markdown-targets.json` の full lint 対象から除外されている。対象レポートは focused lint で確認する必要がある。

focused cspell も `reports/` が `ignorePaths` により skip されるため、スペル検査の残リスクは repo 設定どおり残る。
