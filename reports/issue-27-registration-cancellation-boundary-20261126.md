# Issue #27 registration cancellation boundary

## Contract comparison

`doc/workflow_engine_spec.md` §18 classifies external cancellation as `STEP_CANCELED`, excludes retry and value production, and prevents later Steps from starting. The existing public Engine exception for a pre-cancelled first synchronous Step remains limited to calls that explicitly select `allowPreCancelledExecution`. The `.cs` Run path keeps the strict entry policy. No design change was needed.

## Registration matrix

The public `.cs` Run fixture cancels synchronously through the existing registration-boundary observer, after the wrapper check and before the registration delegate. Each row expects `STEP_CANCELED` and no marker from the target user callback.

| Registration | Boundary protected | Regression case |
|---|---|---|
| Synchronous Lambda | Check before invoking its body unless the pre-cancel compatibility exception was selected. | `sync-lambda` |
| Asynchronous Lambda | Keep the existing check before its body. | `async-lambda` |
| Typed synchronous Step | Check before and after construction unless the pre-cancel compatibility exception was selected. | `typed-sync` |
| Typed asynchronous Step | Check before and after construction. | `typed-async` |
| `RunIf` / `RunIfAsync` | Check before the condition, after the condition, and before constructing or invoking the alternate callback. | `run-if`, `run-if-async` |
| `TapIf` / `TapIfAsync` | Check before the condition and before constructing the selected Step. | `tap-if`, `tap-if-async` |

`If` and `Switch` branch selection use a separate path: `ExecuteIfStepAsync` checks the external token before `GetBranch`. That path was not changed. Existing branch and selector-cancellation cases were included in the related test run.

## Verification

- The eight registration-boundary cases passed on .NET 10.0.15.
- Related tests on `net10.0`: 204 passed, 2 skipped. This includes Engine pre-cancel behavior, retry and exception handling, branch behavior, and `.csx` loading and validation.
- Related tests targeting `net8.0`: 129 passed, 2 skipped, using `DOTNET_ROLL_FORWARD=Major` on .NET 10.0.15. This is not a native .NET 8 runtime result.
- Release builds for `net10.0` and `net8.0` succeeded with zero warnings and errors.
- `dotnet format Devo6.WorkFlow.sln --verify-no-changes --no-restore`, the T31 declaration check, `npm run lint:md`, and `git diff --check` passed.
- The complete test suite and a native .NET 8 runtime test were not run.
