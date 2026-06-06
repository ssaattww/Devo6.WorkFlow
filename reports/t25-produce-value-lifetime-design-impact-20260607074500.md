# T25 Produce 後の値の寿命と有効範囲 設計影響調査

## 調査結果

T25 は `Produce`、`StoreAs`、`Discard` が `StepInput` に作る値の寿命と有効範囲を固定する task である。`tasks-status.md` では、複数 Step で同じ型や名前の値がいつ読めるか、いつ読めないかを検査で固定し、既存 API の意味を壊さないことが完了条件になっている。`phases-status.md` の P9 は T25 と T26 をまとめ、値寿命と値を含む `ExecutionTrace` を秘匿と失敗時挙動まで確認する phase としている。

設計書では、`StepInput` は Step に渡される唯一の入口であり、型付き・名前付きの可変長入力集合である。値のキーは `Type` または `Type + name` で、同じ型と名前の組み合わせを複数登録してはならず、`Produce` による既存キー再登録は実行時エラーとされている。一方、`StepContext` は実行全体で共有される長寿命の値を保持し、`Set<T>()` と `Set<T>(name, value)` は明示上書きである。したがって、T25 は `StepInput` の値寿命を固定しつつ、`StepContext` の共有値や Config の上書き契約と混同しないように追記する必要がある。

現行実装では、`CompositeStep.Execute` と `ExecuteWorkflowAsync` は 1 つの `StepInput` インスタンスを全 Step に渡し続ける。各 Step の成功後、登録済み producer が同じ `StepInput` に値を追加する。`StoreAs()` は `Produce<TOut>(value => value)` の省略形であり、`Discard()` は現在 Step の producer 一覧を空にする。`StepInput` の内部辞書は同じキーが存在すると `InvalidOperationException` を投げ、上書きしない。`StepContext` は別の辞書を持ち、同じキーへの `Set` は上書きする。

既存テストでは、型付き `Produce` が下流へ渡ること、名前付き `Produce` が下流へ渡ること、`StoreAs` が戻り値全体を登録すること、`Discard` が戻り値を登録しないこと、同じ型と名前の重複登録が失敗することが確認されている。ただし、T25 の中心である「最初の下流 Step だけでなく、さらに後続の Step からも読めるか」「別 Step が同じ型または同じ名前を再度生成した場合の境界」「名前付き値と型付き値が同じ型でも別キーとして共存すること」は、利用者目線の E2E としてまだ明示されていない。

retry、timeout、cancel との関係は既に重要な前提がある。Step 本体の失敗 attempt では `Produce`、`StoreAs`、`Discard` は実行しない。retry の途中成功時は、成功した attempt の戻り値だけに producer を実行し、後続 Step は 1 回だけ開始する。timeout または外部 cancel で Step が失敗した場合、その Step の producer は実行せず、後続 Step も開始しない。`Produce`、`StoreAs`、`Discard` 自体の失敗は retry 対象外で、Step 失敗として扱う。

## 採用案

採用案は、`StepInput` に追加された値を CompositeStep 実行の最後まで保持する契約である。

- `Produce` と `StoreAs` で登録された値は、登録した Step より後に実行されるすべての Step から読める。
- 登録前の Step からは読めない。
- `Discard` はその Step の戻り値を新しく登録しないだけで、既に登録済みの値を削除しない。
- `StepInput` は追記型の入力集合であり、同じ `Type` または同じ `Type + name` の再登録は失敗する。暗黙上書きは行わない。
- 型付き値と名前付き値は別キーとして扱う。同じ CLR 型でも、型キー、`name = "title"`、`name = "body"` は別の値として共存できる。
- 長寿命で上書き可能な共有値は `StepContext` に置く。Step 間で明示的に受け渡す値は `StepInput` に置く。

この案は現行実装と既存設計に最も近く、`Produce` は「後続 Step 用入力を追加する」、`StoreAs` は「戻り値全体を追加する」、`Discard` は「戻り値を追加しない」という説明を維持できる。値の削除やスコープ指定を新設しないため、T25 の実装範囲も小さい。

## 代替案

代替案 1 は、直後の Step だけに値を見せる短寿命スコープである。この場合、`Produce` の値が何 Step 先まで読めるかを明示する API がないため、既存の「StepInput を保持する」設計とずれる。複数の後続 Step が同じ上流値を読む実用例にも弱く、T26 の trace でどの Step へ値が渡ったかを追加で追跡する必要が出る。

代替案 2 は、同じキーの後勝ち上書きを許可する案である。`StepContext` の明示上書き契約と似るが、`StepInput` の重複登録失敗という既存設計と既存テストを壊す。利用者から見ると、どの Step の値を読んでいるかが見えにくくなるため、trace 値を入れる T26 でも説明と調査が難しくなる。

代替案 3 は、`Discard` に既存値の削除能力を持たせる案である。現在の `Discard` は「現在の Step 戻り値を登録しない」であり、削除 API ではない。既存値を削除するなら別 API と明示設計が必要で、T25 の最小契約からは外れる。

## TDD 検査案

利用者目線の E2E 寄り検査として、3 Step 以上の `CompositeStep` を使う。

- Step 1 が `Produce<SharedInput>` した値を、Step 2 と Step 3 の両方が読めることを確認する。これで「最後まで保持」を固定する。
- Step 1 が `Produce<string>("title")`、Step 2 が `Produce<string>("body")` し、Step 3 が両方を読めることを確認する。これで名前付き値の累積と同一型別名の共存を固定する。
- Step 1 が `Produce<string>(...)` と `Produce<string>("title", ...)` を同時に登録し、後続 Step が型付き値と名前付き値を別々に読めることを確認する。
- Step 1 と Step 2 が同じ型キーへ `Produce<SameInput>` しようとした場合、Step 2 の post-processing で失敗し、Step 3 が開始しないことを `ExecuteWorkflowAsync` で確認する。
- Step 1 と Step 2 が同じ `Type + name` へ登録しようとした場合も失敗することを確認する。
- `Discard` した Step の戻り値は読めないが、それ以前に登録済みの値は後続 Step から引き続き読めることを確認する。
- retry で 1 回目が失敗し 2 回目が成功する Step について、失敗 attempt の戻り値由来の値は残らず、成功 attempt の値だけ後続 Step から読めることを確認する。
- timeout または外部 cancel で失敗した Step の producer が実行されず、後続 Step が開始しないことは既存検査があるため、T25 では必要に応じて値寿命の文脈から補強する。

## 設計更新が必要な箇所

`doc/workflow_engine_spec.md` の `4.2 StepInput は可変長入力集合` に、`StepInput` は `Produce` と `StoreAs` で追加された値を CompositeStep 実行中の後続 Step に保持する追記型集合である、と追記する。

`4.3 StepInput の識別キー` に、同じ型キーまたは同じ `Type + name` は上書きせず実行時エラー、型キーと名前付きキーは同じ CLR 型でも別キー、という説明を明示する。既存記載はあるが、T25 では複数 Step にまたがる再登録も同じ扱いであることを追加するとよい。

`5.3 StepInput と StepContext の使い分け` に、`StepInput` は後続 Step への明示受け渡しで削除や暗黙上書きをしない、`StepContext` は実行全体の共有値で明示上書きできる、という境界を追記する。

`7.3 Produce`、`7.4 StoreAs`、`7.5 Discard` に、登録された値の可視範囲を追記する。特に `Discard` は既存値を消さないことを明記する必要がある。

`18.1 実行結果` と retry/cancel の既存記載には、失敗 attempt、timeout、外部 cancel では当該 Step の値が `StepInput` に残らないことを T25 の値寿命用語で補足する。

`18.4 トレース` と `21.5 トレース値の保存` には、T26 で値を含む `ExecutionTrace` を設計する前提として、trace 対象は「StepInput に登録された値」または「登録しようとして失敗した値」のどちらを扱うかを分ける必要がある、と追記する。T25 では少なくとも、失敗 attempt、timeout、cancel で producer が実行されない値は通常の登録済み値として扱わない、と固定しておくべきである。

`21.4 Produce 後の値の寿命` は、未確定記載を採用案に置き換える対象である。

## T26 との関係

T26 は値を含む `ExecutionTrace` を扱うため、T25 で値の寿命と登録境界を固定しておく必要がある。特に、trace に含める候補を「Step 成功後に `StepInput` へ登録された値」に限定するのか、`Produce` 失敗時の選択結果や例外情報も扱うのかで、秘匿、直列化不能値、失敗時挙動の設計が変わる。

推奨は、T25 では trace 保存形式を決めず、登録済み値の定義だけを固定することである。すなわち、Step 本体が成功し、timeout や外部 cancel が検出されず、producer が成功して `StepInput` に追加された値だけを「後続 Step へ可視な値」とする。T26 はこの登録済み値を trace 対象の基本単位にできる。

## Config との相互作用

Config は `StepContext` に登録され、`StepInput.Context` から取得される。T25 の `StepInput` 値寿命は Config の寿命を変更しない。`StepContext` は実行全体で共有され、`Set` は明示上書き可能であるため、`StepInput` の重複登録失敗とは別契約として設計書に残す必要がある。

標準 Config の読み込みに失敗した値は `StepContext` に登録してはならないという既存契約がある。T25 でも、失敗した Step の producer 値を `StepInput` に登録しないという境界とそろえて説明できる。

## リスク

現行実装は「最後まで保持」に既に近いが、`Execute` 経路では producer 失敗がそのまま例外になり、`ExecuteWorkflowAsync` 経路では `WorkflowResult` 失敗になる。設計書では両経路の利用者向け説明を分ける必要がある。

`StepInput` に削除 API がないため、値を長く保持することによるメモリ保持リスクは残る。ただし初期版では契約の単純さを優先し、大きな値や秘匿値を長期共有したい場合は利用者が `StepContext` や外部 store を選ぶ、という説明に留めるのがよい。

同一型を複数 Step が出す workflow では、名前付き値を使わないと重複失敗になる。この挙動は分かりやすい一方、既存利用者には「後勝ち上書きされない」ことを設計書と検査で明示する必要がある。

T26 で値を含む trace を追加すると、保持される値の範囲がそのまま trace 候補の範囲に見えやすい。既定では値を保存しない、明示有効化と秘匿規則が必要、という既存方針を維持しないと、意図せず Config や中間値を保存するリスクがある。

## 実行コマンド

```text
git status --short --branch
rg -n "T25|Produce|StoreAs|Discard|StepInput|StepContext|ExecutionTrace|P9" tasks-status.md phases-status.md doc/workflow_engine_spec.md
nl -ba src/Devo6.WorkFlow.Abstractions/StepInput.cs | sed -n '1,240p'
nl -ba src/Devo6.WorkFlow.Abstractions/StepContext.cs | sed -n '1,260p'
nl -ba src/Devo6.WorkFlow.Engine/CompositeStep.cs | sed -n '1,700p'
rg -n "Produce|StoreAs|Discard|StepInput|StepContext|ExecutionTrace" tests/Devo6.WorkFlow.Tests
nl -ba tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs | sed -n '1,240p'
nl -ba tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs | sed -n '1,130p'
nl -ba tests/Devo6.WorkFlow.Tests/RetryExecutionContractTests.cs | sed -n '60,150p;226,260p'
nl -ba tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs | sed -n '24,130p'
```

## 対象ファイル

- `tasks-status.md`
- `phases-status.md`
- `doc/workflow_engine_spec.md`
- `src/Devo6.WorkFlow.Abstractions/StepInput.cs`
- `src/Devo6.WorkFlow.Abstractions/StepContext.cs`
- `src/Devo6.WorkFlow.Engine/CompositeStep.cs`
- `tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`
- `tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs`
- `tests/Devo6.WorkFlow.Tests/RetryExecutionContractTests.cs`
- `tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs`
