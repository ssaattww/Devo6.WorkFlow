# T31 tests standards 監査

## 目的

T31 の標準に照らして `tests/**/*.cs` を監査した。
実装変更と Git 操作は行っていない。

## 監査条件

- 対象: `tests/**/*.cs`
- 関数名: すべて英語名にする。
- XML コメント: 日本語で書く。
- 対象宣言: 型、関数、プロパティ、`record` の primary constructor property。
- 埋め込み csx 文字列は、実ファイル上の C# 宣言とは分けて扱った。

## 検出概要

| 分類 | 件数 |
| --- | ---: |
| 日本語を含む関数名 | 63 |
| XML コメントなし | 140 |
| XML コメントはあるが日本語を含まない | 49 |

主な違反は次の通り。

- 既存の単体検査メソッドに日本語名が残っている。
- 型の XML コメントがない検査クラスと内部 helper 型がある。
- private helper 関数に XML コメントがない。
- `record` の primary constructor property に `<param>` がない。
- 既存 XML コメントの一部が英語本文のまま残っている。

## ファイル別違反

### `tests/Devo6.WorkFlow.Tests/AsyncStepApiContractTests.cs`

- 日本語関数名: 3 件。
- XML コメントなし: 31 件。
- 日本語でない XML コメント: 3 件。
- XML コメントなし:
  - L8 type `AsyncStepApiContractTests`
  - L139 function `RequireAsyncStepType`
  - L149 function `InvokeRunAsync`
  - L180 function `InvokeProduce`
  - L195 function `ExecuteWorkflowAsync`
  - L225 type `ExecutionLog`
  - L231 function `Clear`
  - L236 function `Add`
  - L242 type `AsyncStepTypeFactory`
  - L250 function `Create`
  - L282 type `AsyncStepHandlers`
  - L284 function `ReturnStringAsync`
  - L291 function `ProduceAsyncOutputAsync`
  - L303 function `ThrowAsync`
  - L314 type `ImplementableAsyncStep`
  - L318 type `FirstOutput`
  - L318 record property `FirstOutput.Value`
  - L320 type `AsyncInput`
  - L320 record property `AsyncInput.Value`
  - L322 type `AsyncOutput`
  - L322 record property `AsyncOutput.Value`
  - L324 type `FinalInput`
  - L324 record property `FinalInput.Value`
  - L326 type `SyncOnlyStep`
  - L328 function `Execute`
  - L334 type `FirstSyncStep`
  - L336 function `Execute`
  - L344 type `FinalSyncStep`
  - L346 function `Execute`
  - L355 type `ShouldNotRunStep`
  - L357 function `Execute`
- 日本語でない XML コメント:
  - L14 function `IAsyncStepはStepInputとCancellationTokenでTask戻り値を実装できる`
  - L40 function `RunAsyncはSyncAsyncSyncを定義順に実行しAwait後のProduceを下流へ渡す`
  - L78 function `AsyncStep例外はExecuteWorkflowAsyncでStepExecutionFailedになり後続を止める`

### `tests/Devo6.WorkFlow.Tests/CliRunValidateTests.cs`

- 日本語関数名: 7 件。
- XML コメントなし: 8 件。
- 日本語でない XML コメント: 10 件。
- XML コメントなし:
  - L437 function `RunCliAsync`
  - L462 function `AssertSuccess`
  - L469 function `CreateScript`
  - L479 function `FindRepositoryRoot`
  - L496 type `CliResult`
  - L496 record property `CliResult.ExitCode`
  - L496 record property `CliResult.StandardOutput`
  - L496 record property `CliResult.StandardError`
- 日本語でない XML コメント:
  - L9 type `CliRunValidateTests`
  - L17 function `EngineRunMainCsxは成功時ExitCode0になる`
  - L48 function `EngineValidateMainCsxは成功時ExitCode0になる`
  - L74 function `EngineRunMainCsxEntryBuildは指定Entryを実行する`
  - L109 function `EngineValidateMainCsxEntryBuildは指定Entryを検証する`
  - L310 function `ConfigはEntryDirectory基準で解決されStepContextから取得できる`
  - L347 function `複数Setは文字列としてStepContextから取得できる`
  - L419 function `ValidateとRunの失敗時はExitCodeが0以外になる`
  - L502 type `ProcessStartInfoExtensions`
  - L510 function `AddArguments`

### `tests/Devo6.WorkFlow.Tests/CompositeStepTests.cs`

- 日本語関数名: 8 件。
- XML コメントなし: 31 件。
- 日本語でない XML コメント: 0 件。
- XML コメントなし:
  - L8 type `CompositeStepTests`
  - L91 function `CompositeStepは定義順にStepを実行しProduceで型付き値を下流へ渡す`
  - L109 function `名前付きProduceは下流Stepに名前付き値を渡す`
  - L126 function `StoreAsは戻り値全体を登録する`
  - L141 function `Discardは戻り値を登録しない`
  - L156 function `Produceは同じ型と名前の重複登録を失敗させる`
  - L171 function `CompositeStep自体をIStepとして実行できる`
  - L186 function `CompositeStepは保持時点のStep列と戻り値型を後続Runから守る`
  - L236 type `ExecutionLog`
  - L242 function `Clear`
  - L247 function `Add`
  - L253 type `FirstOutput`
  - L253 record property `FirstOutput.Value`
  - L255 type `SecondInput`
  - L255 record property `SecondInput.Value`
  - L268 type `FirstStep`
  - L277 function `Execute`
  - L285 type `SecondStep`
  - L287 function `Execute`
  - L295 type `TitleStep`
  - L297 function `Execute`
  - L303 type `BodyStep`
  - L305 function `Execute`
  - L311 type `MergeStep`
  - L313 function `Execute`
  - L319 type `ReadsStoredOutputStep`
  - L321 function `Execute`
  - L327 type `StoredOutputMissingStep`
  - L329 function `Execute`
  - L335 type `DuplicateNamedValueStep`
  - L337 function `Execute`

### `tests/Devo6.WorkFlow.Tests/CsxEntryLoaderTests.cs`

- 日本語関数名: 17 件。
- XML コメントなし: 2 件。
- 日本語でない XML コメント: 18 件。
- XML コメントなし:
  - L858 function `CreateScript`
  - L875 function `CreateWorkflowDirectory`
- 日本語でない XML コメント:
  - L9 type `CsxEntryLoaderTests`
  - L40 function `SampleCsxから既定Entry名MainのCompositeStepを取得して実行できる`
  - L70 function `RunAsyncを含むCsxEntryを読み込んで実行できる`
  - L116 function `SampleCsxから指定Entry名BuildのCompositeStepを取得して実行できる`
  - L463 function `EntryFileが存在しない場合はScriptLoadFailedの失敗結果になる`
  - L478 function `ScriptCompileErrorはScriptCompileFailedの失敗結果になる`
  - L501 function `Entry名が存在しない場合はEntryStepNotFoundの失敗結果になる`
  - L529 function `EntryCsxから相対LoadしたFile側のCompositeStepを実行できる`
  - L561 function `Load内の相対PathはLoadを書いたCsxのDirectory基準で解決される`
  - L601 function `WorkflowRoot外へのLoadはScriptReferenceNotAllowedになる`
  - L624 function `Root内SymlinkがRoot外Fileを指すLoadはScriptReferenceNotAllowedになる`
  - L649 function `Root内SymlinkがRoot外Directoryを指すLoadはScriptReferenceNotAllowedになる`
  - L675 function `Load循環はScriptLoadCycleDetectedになる`
  - L696 function `同一正規Pathの重複LoadはCompileを壊さない`
  - L735 function `許可外RはScriptReferenceNotAllowedになる`
  - L752 function `許可されたAssembly名参照は実行できる`
  - L786 function `許可一覧にないNuGet参照はScriptReferenceNotAllowedになる`
  - L841 function `浮動NuGetVersionはScriptReferenceNotAllowedになる`

### `tests/Devo6.WorkFlow.Tests/CsxEntryValidationTests.cs`

- 日本語関数名: 11 件。
- XML コメントなし: 2 件。
- 日本語でない XML コメント: 12 件。
- XML コメントなし:
  - L375 function `CreateScript`
  - L392 function `CreateWorkflowDirectory`
- 日本語でない XML コメント:
  - L9 type `CsxEntryValidationTests`
  - L15 function `ValidCsxはValidationSuccessになりErrorsが空になる`
  - L42 function `EntryCsxが存在しない場合はEntryScriptNotFoundが返る`
  - L58 function `指定Entry名が存在しない場合はEntryStepNotFoundが返る`
  - L122 function `公開CompositeStep名の重複はDuplicateStepNameが返る`
  - L226 function `Load参照解決エラーはValidationErrorとして返る`
  - L246 function `Load循環はValidationErrorとして返る`
  - L265 function `許可外RはValidationErrorとして返る`
  - L280 function `許可外NuGetはValidationErrorとして返る`
  - L295 function `CsxCompileErrorはScriptCompileFailedが返る`
  - L317 function `別CopyのPublicApiAssembly参照はScriptApiIdentityMismatchが返る`
  - L345 function `存在しないConfigFilePathはConfigNotFoundが返る`

### `tests/Devo6.WorkFlow.Tests/ProjectSkeletonTests.cs`

- 日本語関数名: 3 件。
- XML コメントなし: 5 件。
- 日本語でない XML コメント: 0 件。
- XML コメントなし:
  - L5 type `ProjectSkeletonTests`
  - L10 function `Solutionに必要なProjectが含まれる`
  - L23 function `検査Projectから中核Projectを参照できる`
  - L34 function `CliProjectは最小入口で起動できる`
  - L60 function `FindRepositoryRoot`

### `tests/Devo6.WorkFlow.Tests/PublicApiFoundationTests.cs`

- 日本語関数名: 8 件。
- XML コメントなし: 11 件。
- 日本語でない XML コメント: 0 件。
- XML コメントなし:
  - L5 type `PublicApiFoundationTests`
  - L8 function `IStepはStepInputから値を取得して同期実行できる`
  - L19 function `StepInputは型付き取得と名前付き取得ができる`
  - L36 function `StepInputは同じ型と名前の重複登録を失敗させる`
  - L50 function `StepInputの公開ApiはContextとGetとTryGetに限定する`
  - L82 function `StepContextは型付き取得と名前付き取得を明示上書きできる`
  - L101 function `未登録値と無効な名前は分かりやすく失敗する`
  - L120 function `StepValueKeyは型キーと名前付きキーを区別する`
  - L136 function `Unitは単一の公開値を持つReadonlyStructとして使える`
  - L143 type `EchoStep`
  - L145 function `Execute`

### `tests/Devo6.WorkFlow.Tests/RetryExecutionContractTests.cs`

- 日本語関数名: 0 件。
- XML コメントなし: 20 件。
- 日本語でない XML コメント: 0 件。
- XML コメントなし:
  - L9 type `RetryExecutionContractTests`
  - L260 type `RetryOutput`
  - L260 record property `RetryOutput.Value`
  - L262 type `NextInput`
  - L262 record property `NextInput.Value`
  - L264 type `ThirdAttemptSucceedsStep`
  - L283 type `AlwaysFailsStep`
  - L297 type `TimeoutStep`
  - L313 type `ExternallyCanceledStep`
  - L330 type `ProduceFailureSourceStep`
  - L344 type `FollowingStep`
  - L358 type `RetryContractState`
  - L452 type `RecordingLoggerFactory`
  - L492 type `RecordingLogger`
  - L558 type `ScopePopper`
  - L579 type `LogEntry`
  - L579 record property `LogEntry.Level`
  - L579 record property `LogEntry.Message`
  - L579 record property `LogEntry.Exception`
  - L579 record property `LogEntry.Scopes`

### `tests/Devo6.WorkFlow.Tests/TimeoutCancellationContractTests.cs`

- 日本語関数名: 0 件。
- XML コメントなし: 10 件。
- 日本語でない XML コメント: 0 件。
- XML コメントなし:
  - L8 type `TimeoutCancellationContractTests`
  - L232 type `TimedOutput`
  - L248 type `NextInput`
  - L264 type `TimeoutObservingAsyncStep`
  - L282 type `ExternallyCanceledAsyncStep`
  - L301 type `SlowSynchronousStep`
  - L316 type `SlowExternallyCanceledSynchronousStep`
  - L331 type `SlowTimeoutAndExternallyCanceledSynchronousStep`
  - L346 type `ShouldNotRunStep`
  - L359 type `ContractTestState`

### `tests/Devo6.WorkFlow.Tests/WorkflowResultContractTests.cs`

- 日本語関数名: 6 件。
- XML コメントなし: 20 件。
- 日本語でない XML コメント: 6 件。
- XML コメントなし:
  - L7 type `WorkflowResultContractTests`
  - L171 type `LoggingStep`
  - L173 function `Execute`
  - L181 type `ThrowingStep`
  - L183 function `Execute`
  - L189 type `RecordingLoggerFactory`
  - L193 function `RecordingLoggerFactory`
  - L198 property `Entries`
  - L200 function `AddProvider`
  - L204 function `CreateLogger`
  - L209 function `Dispose`
  - L214 type `RecordingLogger`
  - L222 function `IsEnabled`
  - L238 type `NullScope`
  - L240 property `Instance`
  - L242 function `Dispose`
  - L247 type `LogEntry`
  - L247 record property `LogEntry.Level`
  - L247 record property `LogEntry.Message`
  - L247 record property `LogEntry.Exception`
- 日本語でない XML コメント:
  - L13 function `WorkflowResultは成功と失敗Entry名エラーTraceを保持できる`
  - L49 function `ValidationErrorはPathCodeMessageを保持できる`
  - L67 function `基本エラーコードは公開契約として提供される`
  - L93 function `ExecutionTraceは構造化履歴を持つが値そのものを公開しない`
  - L123 function `CompositeStep実行時にStepはStepContextLoggerを使えEngineは状態を記録できる`
  - L149 function `Step例外はWorkflowResult失敗とStepExecutionFailedのTraceになる`

## 日本語テストメソッド名と候補

### `AsyncStepApiContractTests.cs`

- L14 `IAsyncStepはStepInputとCancellationTokenでTask戻り値を実装できる`
  - 候補: `IAsyncStepCanBeImplementedWithStepInputCancellationTokenAndTaskReturnValue`
- L40 `RunAsyncはSyncAsyncSyncを定義順に実行しAwait後のProduceを下流へ渡す`
  - 候補: `RunAsyncExecutesSyncAsyncSyncInDefinitionOrderAndPassesProducedValueAfterAwait`
- L78 `AsyncStep例外はExecuteWorkflowAsyncでStepExecutionFailedになり後続を止める`
  - 候補: `AsyncStepExceptionBecomesStepExecutionFailedAndStopsFollowingSteps`

### `CliRunValidateTests.cs`

- L17 `EngineRunMainCsxは成功時ExitCode0になる`
  - 候補: `EngineRunMainCsxReturnsExitCodeZeroOnSuccess`
- L48 `EngineValidateMainCsxは成功時ExitCode0になる`
  - 候補: `EngineValidateMainCsxReturnsExitCodeZeroOnSuccess`
- L74 `EngineRunMainCsxEntryBuildは指定Entryを実行する`
  - 候補: `EngineRunMainCsxEntryBuildRunsSpecifiedEntry`
- L109 `EngineValidateMainCsxEntryBuildは指定Entryを検証する`
  - 候補: `EngineValidateMainCsxEntryBuildValidatesSpecifiedEntry`
- L310 `ConfigはEntryDirectory基準で解決されStepContextから取得できる`
  - 候補: `ConfigPathResolvesFromEntryDirectoryAndIsAvailableFromStepContext`
- L347 `複数Setは文字列としてStepContextから取得できる`
  - 候補: `MultipleSetArgumentsAreAvailableAsStringsFromStepContext`
- L419 `ValidateとRunの失敗時はExitCodeが0以外になる`
  - 候補: `ValidateAndRunFailuresReturnNonZeroExitCode`

### `CompositeStepTests.cs`

- L91 `CompositeStepは定義順にStepを実行しProduceで型付き値を下流へ渡す`
  - 候補: `CompositeStepExecutesStepsInDefinitionOrderAndPassesProducedTypedValueDownstream`
- L109 `名前付きProduceは下流Stepに名前付き値を渡す`
  - 候補: `NamedProducePassesNamedValueToDownstreamStep`
- L126 `StoreAsは戻り値全体を登録する`
  - 候補: `StoreAsRegistersWholeReturnValue`
- L141 `Discardは戻り値を登録しない`
  - 候補: `DiscardDoesNotRegisterReturnValue`
- L156 `Produceは同じ型と名前の重複登録を失敗させる`
  - 候補: `ProduceFailsOnDuplicateTypeAndName`
- L171 `CompositeStep自体をIStepとして実行できる`
  - 候補: `CompositeStepCanExecuteAsIStep`
- L186 `CompositeStepは保持時点のStep列と戻り値型を後続Runから守る`
  - 候補: `CompositeStepKeepsCapturedStepsAndReturnTypeAcrossLaterRunCalls`
- L212 `StoreAsは型引数を受け取らない`
  - 候補: `StoreAsDoesNotAcceptTypeArgument`

### `CsxEntryLoaderTests.cs`

- L40 `SampleCsxから既定Entry名MainのCompositeStepを取得して実行できる`
  - 候補: `SampleCsxLoadsDefaultMainEntryAsCompositeStepAndExecutes`
- L70 `RunAsyncを含むCsxEntryを読み込んで実行できる`
  - 候補: `RunAsyncCsxEntryLoadsAndExecutes`
- L116 `SampleCsxから指定Entry名BuildのCompositeStepを取得して実行できる`
  - 候補: `SampleCsxLoadsNamedBuildEntryAsCompositeStepAndExecutes`
- L463 `EntryFileが存在しない場合はScriptLoadFailedの失敗結果になる`
  - 候補: `MissingEntryFileReturnsScriptLoadFailedResult`
- L478 `ScriptCompileErrorはScriptCompileFailedの失敗結果になる`
  - 候補: `ScriptCompileErrorReturnsScriptCompileFailedResult`
- L501 `Entry名が存在しない場合はEntryStepNotFoundの失敗結果になる`
  - 候補: `MissingEntryNameReturnsEntryStepNotFoundResult`
- L529 `EntryCsxから相対LoadしたFile側のCompositeStepを実行できる`
  - 候補: `EntryCsxCanRunCompositeStepLoadedByRelativeLoadFile`
- L561 `Load内の相対PathはLoadを書いたCsxのDirectory基準で解決される`
  - 候補: `LoadRelativePathResolvesFromContainingCsxDirectory`
- L601 `WorkflowRoot外へのLoadはScriptReferenceNotAllowedになる`
  - 候補: `LoadOutsideWorkflowRootReturnsScriptReferenceNotAllowed`
- L624 `Root内SymlinkがRoot外Fileを指すLoadはScriptReferenceNotAllowedになる`
  - 候補: `RootSymlinkToOutsideFileLoadReturnsScriptReferenceNotAllowed`
- L649 `Root内SymlinkがRoot外Directoryを指すLoadはScriptReferenceNotAllowedになる`
  - 候補: `RootSymlinkToOutsideDirectoryLoadReturnsScriptReferenceNotAllowed`
- L675 `Load循環はScriptLoadCycleDetectedになる`
  - 候補: `LoadCycleReturnsScriptLoadCycleDetected`
- L696 `同一正規Pathの重複LoadはCompileを壊さない`
  - 候補: `DuplicateCanonicalPathLoadDoesNotBreakCompile`
- L735 `許可外RはScriptReferenceNotAllowedになる`
  - 候補: `DisallowedRReferenceReturnsScriptReferenceNotAllowed`
- L752 `許可されたAssembly名参照は実行できる`
  - 候補: `AllowedAssemblyReferenceExecutes`
- L786 `許可一覧にないNuGet参照はScriptReferenceNotAllowedになる`
  - 候補: `DisallowedNuGetReferenceReturnsScriptReferenceNotAllowed`
- L841 `浮動NuGetVersionはScriptReferenceNotAllowedになる`
  - 候補: `FloatingNuGetVersionReturnsScriptReferenceNotAllowed`

### `CsxEntryValidationTests.cs`

- L15 `ValidCsxはValidationSuccessになりErrorsが空になる`
  - 候補: `ValidCsxReturnsValidationSuccessWithEmptyErrors`
- L42 `EntryCsxが存在しない場合はEntryScriptNotFoundが返る`
  - 候補: `MissingEntryCsxReturnsEntryScriptNotFound`
- L58 `指定Entry名が存在しない場合はEntryStepNotFoundが返る`
  - 候補: `MissingSpecifiedEntryReturnsEntryStepNotFound`
- L122 `公開CompositeStep名の重複はDuplicateStepNameが返る`
  - 候補: `DuplicatePublicCompositeStepNameReturnsDuplicateStepName`
- L226 `Load参照解決エラーはValidationErrorとして返る`
  - 候補: `LoadReferenceResolutionErrorReturnsValidationError`
- L246 `Load循環はValidationErrorとして返る`
  - 候補: `LoadCycleReturnsValidationError`
- L265 `許可外RはValidationErrorとして返る`
  - 候補: `DisallowedRReferenceReturnsValidationError`
- L280 `許可外NuGetはValidationErrorとして返る`
  - 候補: `DisallowedNuGetReferenceReturnsValidationError`
- L295 `CsxCompileErrorはScriptCompileFailedが返る`
  - 候補: `CsxCompileErrorReturnsScriptCompileFailed`
- L317 `別CopyのPublicApiAssembly参照はScriptApiIdentityMismatchが返る`
  - 候補: `PublicApiAssemblyReferenceFromDifferentCopyReturnsScriptApiIdentityMismatch`
- L345 `存在しないConfigFilePathはConfigNotFoundが返る`
  - 候補: `MissingConfigFilePathReturnsConfigNotFound`

### `ProjectSkeletonTests.cs`

- L10 `Solutionに必要なProjectが含まれる`
  - 候補: `SolutionContainsRequiredProjects`
- L23 `検査Projectから中核Projectを参照できる`
  - 候補: `TestProjectReferencesCoreProject`
- L34 `CliProjectは最小入口で起動できる`
  - 候補: `CliProjectStartsWithMinimalEntryPoint`

### `PublicApiFoundationTests.cs`

- L8 `IStepはStepInputから値を取得して同期実行できる`
  - 候補: `IStepCanReadValueFromStepInputAndExecuteSynchronously`
- L19 `StepInputは型付き取得と名前付き取得ができる`
  - 候補: `StepInputCanReadTypedAndNamedValues`
- L36 `StepInputは同じ型と名前の重複登録を失敗させる`
  - 候補: `StepInputFailsDuplicateRegistrationForSameTypeAndName`
- L50 `StepInputの公開ApiはContextとGetとTryGetに限定する`
  - 候補: `StepInputPublicApiIsLimitedToContextGetAndTryGet`
- L82 `StepContextは型付き取得と名前付き取得を明示上書きできる`
  - 候補: `StepContextCanExplicitlyOverrideTypedAndNamedValues`
- L101 `未登録値と無効な名前は分かりやすく失敗する`
  - 候補: `MissingValuesAndInvalidNamesFailClearly`
- L120 `StepValueKeyは型キーと名前付きキーを区別する`
  - 候補: `StepValueKeyDistinguishesTypeAndNamedKeys`
- L136 `Unitは単一の公開値を持つReadonlyStructとして使える`
  - 候補: `UnitCanBeUsedAsReadonlyStructWithSinglePublicValue`

### `WorkflowResultContractTests.cs`

- L13 `WorkflowResultは成功と失敗Entry名エラーTraceを保持できる`
  - 候補: `WorkflowResultCanHoldSuccessFailureEntryNameErrorAndTrace`
- L49 `ValidationErrorはPathCodeMessageを保持できる`
  - 候補: `ValidationErrorCanHoldPathCodeAndMessage`
- L67 `基本エラーコードは公開契約として提供される`
  - 候補: `BasicErrorCodesAreExposedAsPublicContract`
- L93 `ExecutionTraceは構造化履歴を持つが値そのものを公開しない`
  - 候補: `ExecutionTraceHasStructuredHistoryWithoutPublishingValues`
- L123 `CompositeStep実行時にStepはStepContextLoggerを使えEngineは状態を記録できる`
  - 候補: `CompositeStepExecutionAllowsStepsToUseStepContextLoggerAndEngineRecordsState`
- L149 `Step例外はWorkflowResult失敗とStepExecutionFailedのTraceになる`
  - 候補: `StepExceptionBecomesWorkflowResultFailureAndStepExecutionFailedTrace`

## 推奨修正分割

1. `foundation` と `skeleton`
   - `PublicApiFoundationTests.cs`
   - `ProjectSkeletonTests.cs`
2. `composite step`
   - `CompositeStepTests.cs`
3. `loader` と `validation`
   - `CsxEntryLoaderTests.cs`
   - `CsxEntryValidationTests.cs`
4. `CLI` と config
   - `CliRunValidateTests.cs`
   - `StandardConfigLoadingContractTests.cs`
5. 非同期、retry、timeout、trace、value lifetime
   - `AsyncStepApiContractTests.cs`
   - `RetryExecutionContractTests.cs`
   - `TimeoutCancellationContractTests.cs`
   - `TraceValueContractTests.cs`
   - `ProduceValueLifetimeContractTests.cs`
6. 結果、logger、補助型
   - `WorkflowResultContractTests.cs`
   - 各ファイル末尾の helper 型

## 自動検査案

### 日本語関数名

次の grep は実装前の簡易検出に使える。

```bash
rg -n --pcre2 '^\s*(public|private|internal|protected).*\b[A-Za-z_[:^ascii:]]+\s*\(.*$' tests -g '*.cs'
rg -n --pcre2 '^\s*(public|private|internal|protected).*[ぁ-んァ-ン一-龯].*\(' tests -g '*.cs'
```

二つ目の結果が空になることを gate にする。

### XML コメント

grep だけでは属性行、複数行宣言、`record` 引数、埋め込み文字列の扱いで誤検出が出る。
T31 の gate とするなら、Roslyn で次を検査する小さな検査を追加するのがよい。

- `TypeDeclarationSyntax` と `EnumDeclarationSyntax` に XML コメントがある。
- `MethodDeclarationSyntax` と `ConstructorDeclarationSyntax` に XML コメントがある。
- `PropertyDeclarationSyntax` に XML コメントがある。
- `RecordDeclarationSyntax` の primary constructor parameter に対応する `<param>` がある。
- XML コメント本文に日本語文字が含まれる。

現在の project 設定には `GenerateDocumentationFile`、StyleCop、独自 analyzer は見つからなかった。
そのため `dotnet format` だけでは T31 標準を十分に検査できない。

### 実行候補

```bash
dotnet test Devo6.WorkFlow.sln
dotnet format Devo6.WorkFlow.sln --verify-no-changes
npm run lint:md
npm run lint:md:terms
```

`dotnet format` は整形差分と既存 analyzer の確認用であり、XML コメント必須検査の代替にはしない。

## 実行した検証

- `rg --files tests -g '*.cs'`
  - 対象 C# ファイル 15 件を確認した。
- `rg -n --pcre2 '[ぁ-んァ-ン一-龯]' tests -g '*.cs'`
  - 日本語を含む行を抽出し、表示名、XML コメント、埋め込み csx と宣言を分けて確認した。
- 文字列とコメントを除外する簡易抽出を実行した。
  - 実ソース宣言 467 件。
  - 日本語を含む関数名 63 件。
  - XML コメントなし 140 件。
  - XML コメントはあるが日本語を含まない宣言 49 件。
- `npm run lint:md`
  - 成功。
  - 対象は `AGENTS.md`、`doc/workflow_engine_spec.md`、`phases-status.md`、`README.md`、`tasks-status.md`、`tools/lint/README.md` の 6 件。
  - repo 設定上、`reports/` は通常対象に含まれていない。
- `npm run lint:md:terms`
  - 成功。
  - `SudachiPy term variants: none`
- `./node_modules/.bin/textlint --config .textlintrc.json --rulesdir "$(node tools/lint/run-skill-script.js --print-path review-enforcer/scripts/textlint-rules)" reports/t31-tests-standards-audit-20260608053000.md`
  - 成功。
- `node tools/lint/run-skill-script.js review-enforcer/scripts/run-cspell-markdown.js reports/t31-tests-standards-audit-20260608053000.md`
  - repo 設定の `ignorePaths` により skip。

## 残リスク

- 簡易抽出は Roslyn ではないため、複雑な C# 構文では過不足があり得る。
- 埋め込み csx 文字列内のサンプル宣言を T31 標準の対象に含める場合は、別に文字列内 C# の注釈方針を決める必要がある。
